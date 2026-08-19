using AssetForge.Core.Contracts;
using AssetForge.Core.Pipeline;

namespace AssetForge.Core.Processing
{
    public sealed class TrackingAssetProcessor : IAssetProcessor
    {
        private readonly IAssetProcessor _inner;
        private readonly IJobTracker _tracker;
        public TrackingAssetProcessor(IAssetProcessor inner, IJobTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public async ValueTask<JobResult> ProcessAsync(AssetJob job, CancellationToken cancellationToken)
        {
            _tracker.OnStarted(job.Id);

            try
            {
                var result = await _inner.ProcessAsync(job, cancellationToken);
                _tracker.OnFinished(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                _tracker.OnFailed(job.Id, JobState.TimeOut, "exceed its deadline.");
                throw;
            }
            catch (Exception ex)
            {
                _tracker.OnFailed(job.Id, JobState.Failed, ex.Message);
                throw;
            }
        }
    }
}
