using System;
using System.Collections.Generic;
using System.Text;

namespace AssetForge.Core.Contracts
{
    /// <summary>
    /// Represents a job to be processed in the asset pipeline. Immutable.
    /// </summary>
    public sealed record AssetJob
    {
        public required Guid Id { get; init; }
        public required string SourcePath { get; init; }
        public required string OutputDirectory { get; init; }
        /// <summary>
        /// The number of times this job has been attempted. Starts at 1 for the first attempt. Gets incremented each time when supervisor puts job back.
        /// </summary>
        public int Attempt { get; init; } = 1;
        public DateTimeOffset EnqueuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
        public ProcessingOptions Options { get; init; } = new();
   
    }

    /// <summary>
    /// Operational parameters. Key for cache key.
    /// </summary>
    public sealed record ProcessingOptions
    {
        /// <summary>
        /// The maximum number of mip levels to generate for the asset.
        /// </summary>
        public int MaxMipLevels { get; init; } = 16;
        /// <summary>
        /// The minimum size of the smallest mip level as pixels.
        /// </summary>
        public int MinMipSize { get; init; } = 4;
        /// <summary>
        /// If true, the job will simulate a hang for testing purposes.
        /// </summary>
        public bool SimulateHang { get; init; } = false;
    }
}
