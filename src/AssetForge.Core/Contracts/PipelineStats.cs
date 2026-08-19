namespace AssetForge.Core.Contracts;

/// <summary>A point-in-time view of the pipeline, for dashboards and probes.</summary>
public sealed record PipelineStats
{
    public required int QueueDepth { get; init; }
    public required int InFlight { get; init; }

    /// <summary>Processed + FromCache.</summary>
    public required int Completed { get; init; }

    /// <summary>Jobs that actually did the work.</summary>
    public required int Processed { get; init; }

    /// <summary>Jobs answered from the cache without doing the work.</summary>
    public required int FromCache { get; init; }

    public required int Failed { get; init; }

    public required double CacheHitPercent { get; init; }

    /// <summary>Cost of doing the work. This is the number that profiling moves.</summary>
    public required double AverageProcessedMs { get; init; }

    /// <summary>Cost of answering from cache: hashing the input and reading a manifest.</summary>
    public required double AverageCachedMs { get; init; }

    /// <summary>Seconds with work in flight, excluding idle time.</summary>
    public required double BusySeconds { get; init; }

    /// <summary>End-to-end assets per second, cache hits included - what a user experiences.</summary>
    public required double ThroughputPerSecond { get; init; }
}
