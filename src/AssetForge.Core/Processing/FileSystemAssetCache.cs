using System.Security.Cryptography;
using System.Text.Json;
using AssetForge.Core.Contracts;

namespace AssetForge.Core.Processing
{
    /// <summary>
    /// Stores cache entries as folders on disk, one per keym each holding the mip files and a manifest that marks the entry complete.
    /// </summary>
    public sealed class FileSystemAssetCache : IAssetCache
    {
        private const string ManifestFileName = "manifest.json";
        public async ValueTask<string> ComputeKeyAsync(AssetJob job, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(job.SourcePath);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);

            // 16 hex characters is 64 bits, far beyound enough for our needs and short enough to be readable folder name.
            var content = Convert.ToHexString(hash)[..16].ToLowerInvariant();
            var options = $"m{job.Options.MaxMipLevels}s{job.Options.MinMipSize}";

            return $"{content}-{options}";
        }

        public string GetEntryPath(AssetJob job, string key) => Path.Combine(job.OutputDirectory, key);

        public async ValueTask<CacheEntry?> TryReadAsync(string entryPath, CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(entryPath, ManifestFileName);
            if(!File.Exists(manifestPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<CacheEntry>(stream, cancellationToken: cancellationToken);
        }

        public async ValueTask WriteAsync(string entryPath, CacheEntry entry, CancellationToken cancellationToken)
        {
            var manifestPath = Path.Combine(entryPath, ManifestFileName);

            await using var stream = File.Create(manifestPath);
            await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: cancellationToken);
        }
    }
}
