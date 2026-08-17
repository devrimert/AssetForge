using System.Collections.Concurrent;
using AssetForge.Core.Contracts;
using AssetForge.Core.Pipeline;
using AssetForge.Core.Processing;
using AssetForge.Core.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetForge.Core.Tests;

public class PipelineWorkerServiceTests
{
    /// <summary>
    /// Test double. Records what it was asked to do and signals once the
    /// expected number of jobs has been attempted, so tests wait on an
    /// explicit event instead of relying on thread scheduling.
    /// </summary>
    private sealed class FakeProcessor : IAssetProcessor
    {
        private readonly Func<AssetJob, CancellationToken, Task> _behaviour;
        private readonly TaskCompletionSource _allAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _remaining;
        private int _attempts;

        public FakeProcessor(int expectedJobs, Func<AssetJob, CancellationToken, Task>? behaviour = null)
        {
            _remaining = expectedJobs;
            _behaviour = behaviour ?? ((_, _) => Task.CompletedTask);
        }

        /// <summary>Ids of jobs that finished without throwing.</summary>
        public ConcurrentBag<Guid> Succeeded { get; } = [];

        /// <summary>How many jobs the pool handed over, successful or not.</summary>
        public int Attempts => Volatile.Read(ref _attempts);

        public async ValueTask<JobResult> ProcessAsync(AssetJob job, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            try
            {
                await _behaviour(job, cancellationToken);
                Succeeded.Add(job.Id);
                return new JobResult { JobId = job.Id, State = JobState.Completed };
            }
            finally
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    _allAttempted.TrySetResult();
                }
            }
        }

        /// <summary>Completes once every expected job has been attempted.</summary>
        public Task WaitForAllAttemptsAsync() =>
            _allAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static AssetJob NewJob(string fileName = "texture.png") => new()
    {
        Id = Guid.NewGuid(),
        SourcePath = $@"C:\in\{fileName}",
        OutputDirectory = @"C:\out"
    };

    private static PipelineWorkerService CreateService(
        IJobQueue queue,
        IAssetProcessor processor,
        int workerCount = 2,
        TimeSpan? jobTimeout = null)
    {
        var options = Options.Create(new PipelineOptions
        {
            WorkerCount = workerCount,
            JobTimeout = jobTimeout ?? TimeSpan.FromSeconds(5)
        });

        return new PipelineWorkerService(
            queue, processor, options, NullLogger<PipelineWorkerService>.Instance);
    }

    [Fact]
    public async Task Every_queued_job_is_processed_before_shutdown_completes()
    {
        const int jobCount = 50;

        var queue = new ChannelJobQueue(capacity: 16);
        var processor = new FakeProcessor(expectedJobs: jobCount);
        using var service = CreateService(queue, processor, workerCount: 4);

        await service.StartAsync(CancellationToken.None);

        for (var i = 0; i < jobCount; i++)
        {
            await queue.EnqueueAsync(NewJob());
        }

        // Stopping drains: it must not return until the queue is empty.
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(jobCount, processor.Succeeded.Count);
    }

    [Fact]
    public async Task A_failing_job_does_not_stop_the_worker()
    {
        var queue = new ChannelJobQueue();
        var processor = new FakeProcessor(expectedJobs: 2, (job, _) =>
            job.SourcePath.Contains("bad")
                ? Task.FromException(new InvalidOperationException("corrupt asset"))
                : Task.CompletedTask);

        // Queue the work before starting, so the single worker sees both jobs in order.
        await queue.EnqueueAsync(NewJob("bad.png"));
        await queue.EnqueueAsync(NewJob("good.png"));

        using var service = CreateService(queue, processor, workerCount: 1);
        await service.StartAsync(CancellationToken.None);

        await processor.WaitForAllAttemptsAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, processor.Attempts);   // the worker reached both jobs
        Assert.Single(processor.Succeeded);    // only the good one finished
    }

    [Fact]
    public async Task A_hanging_job_is_abandoned_and_the_worker_continues()
    {
        var queue = new ChannelJobQueue();
        var processor = new FakeProcessor(expectedJobs: 2, async (job, cancellationToken) =>
        {
            if (job.SourcePath.Contains("hang"))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        });

        await queue.EnqueueAsync(NewJob("hang.png"));
        await queue.EnqueueAsync(NewJob("good.png"));

        using var service = CreateService(
            queue, processor, workerCount: 1, jobTimeout: TimeSpan.FromMilliseconds(200));

        await service.StartAsync(CancellationToken.None);

        await processor.WaitForAllAttemptsAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, processor.Attempts);
        Assert.Single(processor.Succeeded);
    }
}
