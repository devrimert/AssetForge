using AssetForge.Core.Contracts;

namespace AssetForge.Core.Processing
{
    /// <summary>
    /// Metadata describing a completed cache entry.
    /// </summary>
    public sealed record CacheEntry(int MipLevels, DateTimeOffset CreatedAtUtc);
    public interface IAssetCache
    {
        /// <summary>
        /// Content addressable cache. The key comes from the bytes of the source file and the processing options.
        /// </summary>
        ValueTask<string> ComputeKeyAsync(AssetJob job, CancellationToken cancellationToken);

        /// <summary>
        /// Directory this key's output lives in. May not exist yet.
        /// </summary>
        string GetEntryPath(AssetJob job, string key);

        /// <summary>
        /// returns the entry when it is complete, or null if it is not complete or does not exist.
        /// </summary>
        ValueTask<CacheEntry?> TryReadAsync(string entryPath, CancellationToken cancellationToken);

        /// <summary>
        /// Marks the entry complete. Must be the final write.
        /// </summary>
        ValueTask WriteAsync(string entryPath, CacheEntry entry, CancellationToken cancellationToken);
    }
}
