namespace AssetForge.Core.Pipeline
{
    /// <summary>
    /// Tunables for the worker pool, bound from configuration.
    /// </summary>
    public sealed class PipelineOptions
    {
        /// <summary>
        /// Configuration section name for binding PipelineOptions.
        /// </summary>
        public const string SectionName = "Pipeline";
        /// <summary>
        /// Number of concurrent workers. CPU bound.
        /// </summary>
        public int WorkerCount { get; set; } = Environment.ProcessorCount;
        /// <summary>
        /// Maximum number of jobs that can be queued.
        /// </summary>
        public int QueueCapacity { get; set; } = 100;
        /// <summary>
        /// Timeout for processing a job. Protects the pool from an asset that makes processor hang forever.
        /// </summary>
        public TimeSpan JobTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Where processed output is written. Overridden by configuration in a container.</summary>
        public string OutputDirectory { get; set; } = "output";

        /// <summary>
        /// How long an incoming request may wait for a free queue slot before it is
        /// rejected with 429. Absorbs short bursts without accepting sustained overload.
        /// </summary>
        public TimeSpan EnqueueTimeout { get; set; } = TimeSpan.FromSeconds(2);
    }
}
