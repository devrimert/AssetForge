using System.Diagnostics;
using AssetForge.Core.Contracts;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AssetForge.Core.Processing
{
    public sealed class ImageAssetProcessor : IAssetProcessor
    {
        private readonly IAssetCache _cache;
        private readonly ILogger<ImageAssetProcessor> _logger;

        public ImageAssetProcessor(IAssetCache cache, ILogger<ImageAssetProcessor> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async ValueTask<JobResult> ProcessAsync(AssetJob job, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            if (job.Options.SimulateHang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var key = await _cache.ComputeKeyAsync(job,cancellationToken);
            var entryPath = _cache.GetEntryPath(job, key);

            var cached = await _cache.TryReadAsync(entryPath, cancellationToken);
            if(cached is not null)
            {
                _logger.LogDebug("Cache hit for job {JobId} at {EntryPath}, key: {Key}", job.Id, entryPath, key);

                return new JobResult
                {
                    JobId = job.Id,
                    State = JobState.Completed,
                    OutputPath = entryPath,
                    MipLevels = cached.MipLevels,
                    ElapsedMs = (int)stopwatch.ElapsedMilliseconds,
                    FromCache = true
                };
            }

            Directory.CreateDirectory(entryPath);

            var mipLevels = await GenerateMipChainAsync(job, entryPath, cancellationToken);

            await _cache.WriteAsync(entryPath, new CacheEntry(mipLevels, DateTimeOffset.UtcNow), cancellationToken);

            return new JobResult
            {
                JobId = job.Id,
                State = JobState.Completed,
                OutputPath = entryPath,
                MipLevels = mipLevels,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                FromCache = false
            };

        }

        private static async Task<int> GenerateMipChainAsync(AssetJob job, string entryPath, CancellationToken cancellationToken)
        {
            using var source = await Image.LoadAsync(job.SourcePath, cancellationToken);

            var options = job.Options;
            var width = source.Width;
            var height = source.Height;
            var level = 0;

            while(level < options.MaxMipLevels && width >= options.MinMipSize && height >= options.MinMipSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var mip = source.Clone(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(width,height),
                    Sampler = KnownResamplers.Box
                }));

                await mip.SaveAsPngAsync(Path.Combine(entryPath, $"mip_{level}.png"), cancellationToken);
                level++;
                width /= 2; height /= 2;
            }
            return level;
        }
    }
}
