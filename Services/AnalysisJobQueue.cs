using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PostProcessingServer.Models;

namespace PostProcessingServer.Services
{
    /// <summary>
    /// In-memory thread-safe queue service for managing analysis jobs
    /// </summary>
    public class AnalysisJobQueue
    {
        private static readonly Lazy<AnalysisJobQueue> _instance = new Lazy<AnalysisJobQueue>(() => new AnalysisJobQueue());
        private readonly ConcurrentQueue<AnalysisJob> _queue = new ConcurrentQueue<AnalysisJob>();
        private readonly ConcurrentDictionary<string, AnalysisJob> _activeJobs = new ConcurrentDictionary<string, AnalysisJob>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        public static AnalysisJobQueue Instance => _instance.Value;

        private AnalysisJobQueue()
        {
        }

        public int QueueDepth => _queue.Count;
        public int ActiveJobCount => _activeJobs.Count;

        public void Enqueue(AnalysisJob job)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));

            if (string.IsNullOrWhiteSpace(job.JobId))
                throw new ArgumentException("Job must have a valid JobId", nameof(job));

            job.Status = AnalysisJobStatus.Queued;
            _queue.Enqueue(job);
            _activeJobs.TryAdd(job.JobId, job);
            _signal.Release();
        }

        public async Task<AnalysisJob> DequeueAsync(CancellationToken cancellationToken = default)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (_queue.TryDequeue(out AnalysisJob job))
            {
                job.Status = AnalysisJobStatus.Processing;
                job.StartedAt = DateTimeOffset.UtcNow;
                return job;
            }

            return null;
        }

        public AnalysisJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return null;

            _activeJobs.TryGetValue(jobId, out AnalysisJob job);
            return job;
        }

        public void UpdateJobStatus(string jobId, AnalysisJobStatus status, string resultData = null, string errorMessage = null)
        {
            if (_activeJobs.TryGetValue(jobId, out AnalysisJob job))
            {
                job.Status = status;
                
                if (status == AnalysisJobStatus.Completed || status == AnalysisJobStatus.Failed || status == AnalysisJobStatus.Cancelled)
                {
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    
                    if (job.StartedAt.HasValue)
                    {
                        job.ProcessingDurationMs = (long)(job.CompletedAt.Value - job.StartedAt.Value).TotalMilliseconds;
                    }
                }

                if (!string.IsNullOrWhiteSpace(resultData))
                {
                    job.ResultData = resultData;
                }

                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    job.ErrorMessage = errorMessage;
                }
            }
        }

        public int CleanupOldJobs(TimeSpan maxAge)
        {
            var cutoffTime = DateTimeOffset.UtcNow - maxAge;
            var jobsToRemove = _activeJobs.Values
                .Where(j => (j.Status == AnalysisJobStatus.Completed || 
                            j.Status == AnalysisJobStatus.Failed || 
                            j.Status == AnalysisJobStatus.Cancelled) &&
                           j.CompletedAt.HasValue &&
                           j.CompletedAt.Value < cutoffTime)
                .Select(j => j.JobId)
                .ToList();

            int removedCount = 0;
            foreach (var jobId in jobsToRemove)
            {
                if (_activeJobs.TryRemove(jobId, out _))
                {
                    removedCount++;
                }
            }

            return removedCount;
        }

        public List<AnalysisJob> GetUserJobs(string userId, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new List<AnalysisJob>();

            return _activeJobs.Values
                .Where(j => j.TilerUserId == userId)
                .OrderByDescending(j => j.CreatedAt)
                .Take(limit)
                .ToList();
        }

        public QueueStatistics GetStatistics()
        {
            var allJobs = _activeJobs.Values.ToList();
            var completedJobs = allJobs.Where(j => j.Status == AnalysisJobStatus.Completed).ToList();

            return new QueueStatistics
            {
                QueueDepth = QueueDepth,
                ActiveJobs = ActiveJobCount,
                QueuedJobs = allJobs.Count(j => j.Status == AnalysisJobStatus.Queued),
                ProcessingJobs = allJobs.Count(j => j.Status == AnalysisJobStatus.Processing),
                CompletedJobs = completedJobs.Count,
                FailedJobs = allJobs.Count(j => j.Status == AnalysisJobStatus.Failed),
                AverageProcessingTimeMs = completedJobs.Any() ? 
                    completedJobs.Average(j => j.ProcessingDurationMs ?? 0) : 0
            };
        }
    }

    public class QueueStatistics
    {
        public int QueueDepth { get; set; }
        public int ActiveJobs { get; set; }
        public int QueuedJobs { get; set; }
        public int ProcessingJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }
        public double AverageProcessingTimeMs { get; set; }
    }
}
