using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using AssetForge.Core.Contracts;

namespace AssetForge.Core.Pipeline
{
    public sealed class InMemoryJobTracker : IJobTracker
    {
        private readonly ConcurrentDictionary<Guid, JobProgress> _jobs = new();
        private long _busyStartedAt;   // Stopwatch timestamp of the current busy stretch
        private long _busyTicks;       // accumulated busy time
        private int _inFlight;
        private int _processed;
        private int _failed;
        private int _fromCache;
        private long _processedMs;
        private long _cachedMs;


        public void OnEnqueued(AssetJob job) => _jobs[job.Id] = new JobProgress
        {
            JobId = job.Id,
            State = JobState.Queued,
            Percent = 0,
        };

        public void OnStarted(Guid jobId)
        {
            // Going from idle to busy starts a new measurement window.
            if (Interlocked.Increment(ref _inFlight) == 1)
            {
                Volatile.Write(ref _busyStartedAt, Stopwatch.GetTimestamp());
            }

            _jobs[jobId] = new JobProgress { JobId = jobId, State = JobState.Running, Percent = 0 };
        }

        public void OnFinished(JobResult result)
        {
            if (Interlocked.Decrement(ref _inFlight) == 0)
            {
                Interlocked.Add(ref _busyTicks, Stopwatch.GetTimestamp() - Volatile.Read(ref _busyStartedAt));
            }

            // Cache hits and real work are different populations: averaging them
            // together describes neither.
            if (result.FromCache)
            {
                Interlocked.Increment(ref _fromCache);
                Interlocked.Add(ref _cachedMs, result.ElapsedMs);
            }
            else
            {
                Interlocked.Increment(ref _processed);
                Interlocked.Add(ref _processedMs, result.ElapsedMs);
            }

            _jobs[result.JobId] = new JobProgress
            {
                JobId = result.JobId,
                State = result.State,
                Percent = 100,
                Message = result.FromCache
                    ? "served from cache"
                    : $"{result.MipLevels} mip levels in {result.ElapsedMs} ms"
            };
        }

        public void OnFailed(Guid jobId, JobState state, string? error)
        {
            // Going from busy back to idle closes the window.
            if (Interlocked.Decrement(ref _inFlight) == 0)
            {
                Interlocked.Add(ref _busyTicks, Stopwatch.GetTimestamp() - Volatile.Read(ref _busyStartedAt));
            }
            Interlocked.Increment(ref _failed);

            _jobs[jobId] = new JobProgress
            {
                JobId = jobId,
                State = state,
                Percent = 0,
                Message = error,
            };
        }
        public void Reset()
        {
            _jobs.Clear();

            Interlocked.Exchange(ref _processed, 0);
            Interlocked.Exchange(ref _fromCache, 0);
            Interlocked.Exchange(ref _failed, 0);
            Interlocked.Exchange(ref _processedMs, 0);
            Interlocked.Exchange(ref _cachedMs, 0);
            Interlocked.Exchange(ref _busyTicks, 0);

            // _inFlight is deliberately untouched: jobs may still be running.
        }

        public JobProgress? Find(Guid jobId) => _jobs.TryGetValue(jobId, out var progress) ? progress : null;

        public PipelineStats GetStats(int queueDepth)
        {
            var processed = Volatile.Read(ref _processed);
            var fromCache = Volatile.Read(ref _fromCache);
            var completed = processed + fromCache;

            var busyTicks = Interlocked.Read(ref _busyTicks);

            if (Volatile.Read(ref _inFlight) > 0)
            {
                busyTicks += Stopwatch.GetTimestamp() - Volatile.Read(ref _busyStartedAt);
            }

            var busySeconds = (double)busyTicks / Stopwatch.Frequency;

            return new PipelineStats
            {
                QueueDepth = queueDepth,
                InFlight = Volatile.Read(ref _inFlight),
                Completed = completed,
                Processed = processed,
                FromCache = fromCache,
                Failed = Volatile.Read(ref _failed),
                CacheHitPercent = completed > 0 ? Math.Round(100.0 * fromCache / completed, 1) : 0,
                AverageProcessedMs = processed > 0 ? Math.Round((double)Interlocked.Read(ref _processedMs) / processed, 2) : 0,
                AverageCachedMs = fromCache > 0 ? Math.Round((double)Interlocked.Read(ref _cachedMs) / fromCache, 2) : 0,
                BusySeconds = Math.Round(busySeconds, 2),
                ThroughputPerSecond = busySeconds > 0 ? Math.Round(completed / busySeconds, 2) : 0
            };
        }
        
    }
}
