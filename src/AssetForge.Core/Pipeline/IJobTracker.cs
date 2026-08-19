using AssetForge.Core.Contracts;

namespace AssetForge.Core.Pipeline;

/// <summary>
/// Keeps the observable state of jobs: what is queued, running, done or failed.
/// Implementations must be safe to call from many workers at once.
/// </summary>
public interface IJobTracker
{
    void OnEnqueued(AssetJob job);
    void OnStarted(Guid jobId);
    void OnFinished(JobResult result);
    void OnFailed(Guid jobId, JobState state, string? error);
    /// <summary>Clears the counters. A development convenience for taking clean measurements.</summary>
    void Reset();

    JobProgress? Find(Guid jobId);
    PipelineStats GetStats(int queueDepth);
}
