using AssetForge.Core.Contracts;

namespace AssetForge.Core.Queues
{
    public interface IJobQueue
    {
        /// <summary>
        /// Adds a job to the queue. If the queue is full, it waits until a slot frees up (backpressure).
        /// </summary>
        ValueTask EnqueueAsync(AssetJob job, CancellationToken cancellationToken = default);
        /// <summary>
        /// Takes the next job from the queue.
        /// Throws <see cref="InvalidOperationException"/> when the token is cancelled.
        /// </summary>
        ValueTask<AssetJob> DequeueAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// Streams the jobs in the queue until queue is drained and marked complete.
        /// </summary>
        IAsyncEnumerable<AssetJob> ReadAllAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// Number of jobs currently in the queue. Used for dashboard telemetry.
        /// </summary>
        ValueTask<int> GetDepthAsync (CancellationToken cancellationToken = default);
        /// <summary>
        /// Signals that no more jobs will be added to the queue. Consumer finish the remaining items and then their <see cref="ReadAllAsync"/> loop ends."/>
        /// </summary>
        void CompleteAdding();
    }
}
