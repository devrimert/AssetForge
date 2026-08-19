using System.Text.Json.Serialization;
using System.Threading.Channels;
using AssetForge.Core.Contracts;
using AssetForge.Core.Pipeline;
using AssetForge.Core.Processing;
using AssetForge.Core.Queues;
using AssetForge.Service.Health;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.SectionName));

// enums travel by name not by number.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<ReadinessState>();
builder.Services.AddSingleton<IJobTracker, InMemoryJobTracker>();
builder.Services.AddSingleton<IAssetCache, FileSystemAssetCache>();
// The queue capacity comes from configuration, so it needs a factory.
builder.Services.AddSingleton<IJobQueue>(provider =>
{
    var options = provider.GetRequiredService<IOptions<PipelineOptions>>().Value;
    return new ChannelJobQueue(options.QueueCapacity);
});
// The real processor, wrapped so the tracker sees every job.
builder.Services.AddSingleton<IAssetProcessor>(provider => new TrackingAssetProcessor(
    new ImageAssetProcessor(
        provider.GetRequiredService<IAssetCache>(),
        provider.GetRequiredService<ILogger<ImageAssetProcessor>>()),
    provider.GetRequiredService<IJobTracker>()));

builder.Services.AddHostedService<PipelineWorkerService>();

var app = builder.Build();

// Stop advertising readiness the instant shutdown begins, so the load balancer
// stops sending work before the queue starts draining.
app.Services.GetRequiredService<IHostApplicationLifetime>()
   .ApplicationStopping
   .Register(app.Services.GetRequiredService<ReadinessState>().MarkNotReady);

#region 'Health'
// Liveness answers "is this process alive". Failing it gets the pod killed,
// so it must not depend on downstream state.
app.MapGet("/healthz/live", () => Results.Ok(new { status = "alive" }));
// Readiness answers "should I receive traffic". Failing it only removes us
// from the load balancer; the process keeps running and finishes its work.
app.MapGet("/healthz/ready", (ReadinessState readiness) => readiness.IsReady
    ? Results.Ok(new { status = "ready" })
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

#endregion

#region 'Jobs'
app.MapPost("/jobs", async (SubmitJobRequest request, IJobQueue queue, IJobTracker tracker, IOptions<PipelineOptions> options) =>
{
    if (!File.Exists(request.SourcePath))
    {
        return Results.BadRequest(new { error = $"File not found: {request.SourcePath}" });
    }

    var job = CreateJob(request.SourcePath, options.Value);
    var outcome = await TrySubmitAsync(queue, tracker, job, options.Value.EnqueueTimeout);

    return outcome switch
    {
        SubmitOutcome.Accepted => Results.Accepted($"/jobs/{job.Id}", new { job.Id }),
        SubmitOutcome.Rejected => Results.StatusCode(StatusCodes.Status429TooManyRequests),
        _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
    };

});

// Bulk entry point. Exists so load can be generated without the desktop tool,
// which is what the Kubernetes demo and the profiling runs need.
app.MapPost("/jobs/scan", async (ScanRequest request, IJobQueue queue, IJobTracker tracker, IOptions<PipelineOptions> options) =>
{
    if (!Directory.Exists(request.Directory))
    {
        return Results.BadRequest(new { error = $"Directory not found: {request.Directory}" });
    }
    var accepted = 0;
    var rejected = 0;

    // Deliberately one producer awaiting each write in turn. A bounded queue
    // can only apply backpressure if the producer actually waits for it -
    // firing a Task per file would move the unboundedness into the scheduler.
    foreach (var file in Directory.EnumerateFiles(request.Directory, "*.png", SearchOption.AllDirectories))
    {
        var job = CreateJob(file, options.Value);
        var outcome = await TrySubmitAsync(queue,tracker, job, options.Value.EnqueueTimeout);

        if (outcome == SubmitOutcome.ShuttingDown)
            break;
        if (outcome == SubmitOutcome.Accepted)
            accepted++;
        if(outcome == SubmitOutcome.Rejected)
            rejected++;
    }
    return Results.Ok(new {accepted,rejected});
});

app.MapGet("/jobs/{id:guid}", (Guid id, IJobTracker tracker) =>
{
    var progress = tracker.Find(id);
    return progress is null ? Results.NotFound() : Results.Ok(progress);
});

#endregion

#region 'Stats'

app.MapGet("/stats", async (IJobQueue queue, IJobTracker tracker) =>
{
    var depth = await queue.GetDepthAsync();
    return Results.Ok(tracker.GetStats(depth));
});

// Development convenience: lets a measurement start from a clean slate.
app.MapPost("/stats/reset", (IJobTracker tracker) =>
{
    tracker.Reset();
    return Results.NoContent();
});

#endregion

app.Run();

#region 'Helpers'
static AssetJob CreateJob(string sourcePath, PipelineOptions options) => new()
{
    Id = Guid.NewGuid(),
    SourcePath = sourcePath,
    OutputDirectory = options.OutputDirectory,
};

static async Task<SubmitOutcome> TrySubmitAsync( IJobQueue queue, IJobTracker tracker,AssetJob job, TimeSpan timeout)
{
    try
    {
        var write = queue.EnqueueAsync(job).AsTask();
        // Cancelling the timer when the write wins keeps a pending Task.Delay
        // from being left behind on every request.
        using var timer = new CancellationTokenSource();
        var expiry = Task.Delay(timeout, timer.Token);

        if(await Task.WhenAny(write,expiry) != write)
        {
            return SubmitOutcome.Rejected;
        }

        timer.Cancel();
        await write; // surfaces anything the write itself threw

        tracker.OnEnqueued(job);
        return SubmitOutcome.Accepted;
    }
    catch (ChannelClosedException)
    {
        // Queue was completed, we cannot accept work, we are shutting down.
        return SubmitOutcome.ShuttingDown;
    }
}

internal sealed record SubmitJobRequest(string SourcePath);

internal sealed record ScanRequest(string Directory);

internal enum SubmitOutcome
{
    Accepted,
    Rejected,
    ShuttingDown
}
#endregion

