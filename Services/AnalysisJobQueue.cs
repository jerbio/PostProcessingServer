using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PostProcessingServer.Models;
using TilerElements;
using TilerCrossServerResources;

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
        private bool _isInitialized = false;
        private readonly object _initLock = new object();

        public static AnalysisJobQueue Instance => _instance.Value;

        private AnalysisJobQueue()
        {
        }

        public int QueueDepth => _queue.Count;
        public int ActiveJobCount => _activeJobs.Count;

        /// <summary>
        /// Loads incomplete jobs from the database and re-queues them for processing
        /// This should be called on application startup to resume interrupted jobs
        /// </summary>
        public int LoadIncompleteJobsFromDatabase()
        {
            lock (_initLock)
            {
                if (_isInitialized)
                {
                    System.Diagnostics.Trace.WriteLine("AnalysisJobQueue already initialized, skipping database load");
                    return 0;
                }

                int loadedCount = 0;
                try
                {
                    using (var db = new PostProcessingServer.Models.PostProcessorApplicationDbContext())
                    {
                        // Find jobs that were queued or processing when the server shut down
                        var incompleteJobs = db.AnalysisJobs
                            .Where(j => j.Status == AnalysisJobStatus.Queued || 
                                       j.Status == AnalysisJobStatus.Processing)
                            .OrderBy(j => j.CreatedAt)
                            .ToList();

                        foreach (var job in incompleteJobs)
                        {
                            // Reset processing jobs back to queued
                            if (job.Status == AnalysisJobStatus.Processing)
                            {
                                job.Status = AnalysisJobStatus.Queued;
                                job.StartedAt = null;
                            }

                            // Add to in-memory queue
                            _queue.Enqueue(job);
                            _activeJobs.TryAdd(job.Id, job);
                            _signal.Release();
                            loadedCount++;
                        }

                        // Update all processing jobs in database to queued
                        var processingJobs = db.AnalysisJobs
                            .Where(j => j.Status == AnalysisJobStatus.Processing)
                            .ToList();

                        foreach (var job in processingJobs)
                        {
                            job.Status = AnalysisJobStatus.Queued;
                            job.StartedAt = null;
                        }

                        if (processingJobs.Any())
                        {
                            db.SaveChanges();
                        }

                        System.Diagnostics.Trace.WriteLine($"Loaded {loadedCount} incomplete jobs from database");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"Failed to load incomplete jobs from database: {ex.Message}\n{ex.StackTrace}");
                }

                _isInitialized = true;
                return loadedCount;
            }
        }

        /// <summary>
        /// Enqueues a job for processing
        /// </summary>
        /// <param name="job">The job to enqueue</param>
        public void Enqueue(AnalysisJob job)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));

            if (string.IsNullOrWhiteSpace(job.Id))
                throw new ArgumentException("Job must have a valid JobId", nameof(job));

            job.Status = AnalysisJobStatus.Queued;
            _queue.Enqueue(job);
            _activeJobs.TryAdd(job.Id, job);
            
            // Persist to database
            try
            {
                using (var db = new PostProcessingServer.Models.PostProcessorApplicationDbContext())
                {
                    db.AnalysisJobs.Add(job);
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Failed to persist new job {job.Id}: {ex.Message}");
            }
            
            // Signal that a new item is available
            _signal.Release();
        }

        public async Task<AnalysisJob> DequeueAsync(CancellationToken cancellationToken = default)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (_queue.TryDequeue(out AnalysisJob job))
            {
                job.Status = AnalysisJobStatus.Processing;
                job.StartedAt = Utility.now();
                return job;
            }

            return null;
        }

        public AnalysisJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return null;

            // First check in-memory cache
            if (_activeJobs.TryGetValue(jobId, out AnalysisJob job))
            {
                return job;
            }

            // If not in memory, try loading from database
            try
            {
                using (var db = new PostProcessingServer.Models.PostProcessorApplicationDbContext())
                {
                    var dbJob = db.AnalysisJobs.Find(jobId);
                    if (dbJob != null)
                    {
                        // Add to in-memory cache for future lookups
                        _activeJobs.TryAdd(jobId, dbJob);
                        return dbJob;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Failed to load job {jobId} from database: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Updates the status of a job
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="status">The new status</param>
        /// <param name="resultData">Optional result data</param>
        /// <param name="errorMessage">Optional error message</param>
        public void UpdateJobStatus(string jobId, AnalysisJobStatus status, string resultData = null, string errorMessage = null)
        {
            if (_activeJobs.TryGetValue(jobId, out AnalysisJob job))
            {
                job.Status = status;
                
                if (status == AnalysisJobStatus.Completed || status == AnalysisJobStatus.Failed || status == AnalysisJobStatus.Cancelled)
                {
                    job.CompletedAt = Utility.now();
                    
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

                // Persist changes to database
                try
                {
                    using (var db = new PostProcessingServer.Models.PostProcessorApplicationDbContext())
                    {
                        var existingJob = db.AnalysisJobs.Find(jobId);
                        if (existingJob != null)
                        {
                            // Update existing job
                            existingJob.Status = job.Status;
                            existingJob.CompletedAt = job.CompletedAt;
                            existingJob.ProcessingDurationMs = job.ProcessingDurationMs;
                            existingJob.ResultData = job.ResultData;
                            existingJob.ErrorMessage = job.ErrorMessage;
                            existingJob.StartedAt = job.StartedAt;
                        }
                        else
                        {
                            // Add new job if it doesn't exist
                            db.AnalysisJobs.Add(job);
                        }
                        
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"Failed to persist job status update for {jobId}: {ex.Message}");
                }
            }
        }

        public int CleanupOldJobs(TimeSpan maxAge)
        {
            var cutoffTime = Utility.now() - maxAge;
            var jobsToRemove = _activeJobs.Values
                .Where(j => (j.Status == AnalysisJobStatus.Completed || 
                            j.Status == AnalysisJobStatus.Failed || 
                            j.Status == AnalysisJobStatus.Cancelled) &&
                           j.CompletedAt.HasValue &&
                           j.CompletedAt.Value < cutoffTime)
                .Select(j => j.Id)
                .ToList();

            int removedCount = 0;
            
            // Remove from in-memory collections
            foreach (var jobId in jobsToRemove)
            {
                if (_activeJobs.TryRemove(jobId, out _))
                {
                    removedCount++;
                }
            }

            // Remove from database
            if (jobsToRemove.Any())
            {
                try
                {
                    using (var db = new PostProcessingServer.Models.PostProcessorApplicationDbContext())
                    {
                        var dbJobsToRemove = db.AnalysisJobs
                            .Where(j => jobsToRemove.Contains(j.Id))
                            .ToList();

                        db.AnalysisJobs.RemoveRange(dbJobsToRemove);
                        db.SaveChanges();
                        
                        System.Diagnostics.Trace.WriteLine($"Removed {dbJobsToRemove.Count} old jobs from database");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"Failed to remove old jobs from database: {ex.Message}");
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
