
namespace AssetForge.Core.Contracts
{
    /// <summary>
    /// Status of a job in the processing pipeline.
    /// </summary>
    public enum JobState
    {
        Queued,         // Job is waiting to be processed.
        Running,        // Job is currently being processed.
        Completed,      // Job has completed successfully.
        Failed,         // Job has failed during processing.
        Cancelled,      // Job has been cancelled by the user.
        TimeOut         // Job has timed out during processing.
    }
}
