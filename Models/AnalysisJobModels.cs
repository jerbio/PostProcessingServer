using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TilerElements;

namespace PostProcessingServer.Models
{
    /// <summary>
    /// Represents the status of an analysis job
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AnalysisJobStatus
    {
        Queued = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Represents the type of analysis being performed
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AnalysisJobType
    {
        Suggestion = 0,
        ScheduleAnalysis = 1,
        TimelineSummary = 2,
        ProcessRequest = 3
    }

    /// <summary>
    /// Database entity for tracking analysis jobs
    /// </summary>
    [Table("AnalysisJobs")]
    public class AnalysisJob
    {
        [Key]
        [StringLength(128)]
        public string JobId { get; set; }

        [Required]
        [Index("IX_AnalysisJob_TilerUserId", 1)]
        public string TilerUserId { get; set; }
        
        [ForeignKey("TilerUserId")]
        public TilerUser TilerUser { get; set; }

        [Required]
        [Index("IX_AnalysisJob_JobType", 1)]
        public AnalysisJobType JobType { get; set; }

        [Required]
        [Index("IX_AnalysisJob_Status", 1)]
        [Index("IX_AnalysisJob_Status_CreatedAt", 1)]
        public AnalysisJobStatus Status { get; set; }

        public string RequestData { get; set; }
        public string ResultData { get; set; }
        public string ErrorMessage { get; set; }
        public int RetryCount { get; set; }

        [Required]
        [Index("IX_AnalysisJob_CreatedAt", 1)]
        [Index("IX_AnalysisJob_Status_CreatedAt", 2)]
        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? StartedAt { get; set; }
        
        [Index("IX_AnalysisJob_CompletedAt", 1)]
        public DateTimeOffset? CompletedAt { get; set; }
        
        public DateTimeOffset? ExpiresAt { get; set; }
        public long? ProcessingDurationMs { get; set; }

        public AnalysisJob()
        {
            JobId = Ulid.NewUlid().ToString();
            Status = AnalysisJobStatus.Queued;
            CreatedAt = Utility.now();
            ExpiresAt = Utility.now().AddDays(7);
            RetryCount = 0;
        }
    }

    /// <summary>
    /// Request DTO for submitting an analysis job
    /// </summary>
    public class AnalysisJobRequest
    {
        [Required]
        public string UserId { get; set; }

        [Required]
        public AnalysisJobType JobType { get; set; }

        [Required]
        public string RequestData { get; set; }

        public string CorrelationId { get; set; }
        public long Timestamp { get; set; }
        public string Nonce { get; set; }
    }

    /// <summary>
    /// Response DTO when submitting an analysis job
    /// </summary>
    public class AnalysisJobResponse
    {
        public bool Success { get; set; }
        public string JobId { get; set; }
        public AnalysisJobStatus Status { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }

        public static AnalysisJobResponse CreateSuccess(string jobId, string message = "Job queued successfully")
        {
            return new AnalysisJobResponse
            {
                Success = true,
                JobId = jobId,
                Status = AnalysisJobStatus.Queued,
                Message = message
            };
        }

        public static AnalysisJobResponse CreateError(string errorMessage)
        {
            return new AnalysisJobResponse
            {
                Success = false,
                ErrorMessage = errorMessage,
                Status = AnalysisJobStatus.Failed
            };
        }
    }

    /// <summary>
    /// Response DTO for job status queries
    /// </summary>
    public class AnalysisJobStatusResponse
    {
        public bool Success { get; set; }
        public string JobId { get; set; }
        public AnalysisJobStatus Status { get; set; }
        public AnalysisJobType JobType { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public long? ProcessingDurationMs { get; set; }
        public string ResultData { get; set; }
        public string ErrorMessage { get; set; }
        public int RetryCount { get; set; }
        public int? ProgressPercentage { get; set; }

        public static AnalysisJobStatusResponse FromJob(AnalysisJob job)
        {
            return new AnalysisJobStatusResponse
            {
                Success = true,
                JobId = job.JobId,
                Status = job.Status,
                JobType = job.JobType,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                ProcessingDurationMs = job.ProcessingDurationMs,
                ResultData = job.ResultData,
                ErrorMessage = job.ErrorMessage,
                RetryCount = job.RetryCount
            };
        }

        public static AnalysisJobStatusResponse CreateNotFound(string jobId)
        {
            return new AnalysisJobStatusResponse
            {
                Success = false,
                JobId = jobId,
                ErrorMessage = "Job not found"
            };
        }
    }
}
