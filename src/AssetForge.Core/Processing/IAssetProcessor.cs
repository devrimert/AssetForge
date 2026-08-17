using AssetForge.Core.Contracts;

namespace AssetForge.Core.Processing
{
    /// <summary>
    /// Turns one source asset into its processed output.
    /// </summary>
    public interface IAssetProcessor
    {
        /// <summary>
        /// Processes a job asynchronously.
        /// </summary>
        ValueTask<JobResult> ProcessAsync(AssetJob job, CancellationToken cancellationToken);
    }
}
