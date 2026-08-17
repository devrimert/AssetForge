using AssetForge.Core.Contracts;
using AssetForge.Core.Processing;
using AssetForge.Core.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetForge.Core.Pipeline
{
    public sealed class PipelineWorkerService : BackgroundService
    {
        private readonly IJobQueue _queue;
        private readonly IAssetProcessor _processor;
        private readonly PipelineOptions _options;
        private readonly ILogger<PipelineWorkerService> _logger;
        public PipelineWorkerService(IJobQueue queue, IAssetProcessor processor, IOptions<PipelineOptions> options, ILogger<PipelineWorkerService> logger)
        {
            _queue = queue;
            _processor = processor;
            _options = options.Value;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Shitdown means "stop accepting new jobs, but finish processing the ones in the queue"
            using var shutdown = stoppingToken.Register(_queue.CompleteAdding);

            var workerCount = _options.WorkerCount;
            _logger.LogInformation("Starting {WorkerCount} workers", workerCount);

            var workers = Enumerable.Range(0, workerCount).Select(index => RunWorkersAsync($"worker-{index + 1}")).ToArray();

            await Task.WhenAll(workers);

            _logger.LogInformation("All workers stopped.");
        }

        private async Task RunWorkersAsync(string workerId)
        {
            await foreach (var job in _queue.ReadAllAsync())
            {
                await ProcessOneAsync(workerId, job);
            }
            _logger.LogDebug("Worker {WorkerId} stopped.", workerId);
        }

        private async Task ProcessOneAsync(string workerId, AssetJob job)
        {
            using var deadline = new CancellationTokenSource(_options.JobTimeout);

            try
            {
                var result = await _processor.ProcessAsync(job, deadline.Token);
                _logger.LogInformation("{WorkerId} finished {JobId} in {ElapsedMs}ms (cached: {FromCache})", workerId, job.Id, result.ElapsedMs, result.FromCache);
            }
            catch(OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                _logger.LogWarning("{WorkerId} abandoned {JobId}: exceeded {Timeout}", workerId, job.Id, _options.JobTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{WorkerId} failed {JobId}: {Message}", workerId, job.Id, ex.Message);
            }

        }

    }
}
