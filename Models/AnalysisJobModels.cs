using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TilerElements;
using TilerCrossServerResources;

namespace PostProcessingServer.Models
{
    // Re-export shared types for easier access within PostProcessingServer
    using AnalysisJobStatus = TilerCrossServerResources.AnalysisJobStatus;
    using AnalysisJobType = TilerCrossServerResources.AnalysisJobType;
    using AnalysisJobRequest = TilerCrossServerResources.AnalysisJobRequest;
    using AnalysisJobResponse = TilerCrossServerResources.AnalysisJobResponse;
    using AnalysisJobStatusResponse = TilerCrossServerResources.AnalysisJobStatusResponse;

    /// <summary>
    /// Database entity for tracking analysis jobs - PostProcessingServer specific implementation
    /// with TilerUser foreign key relationship and database indexes
    /// </summary>
    [Table("AnalysisJobs")]
    public class AnalysisJob : AnalysisJobBase
    {

        [ForeignKey("TilerUserId")]
        public TilerUser TilerUser { get; set; }


        // Override indexes from base class for PostProcessingServer-specific indexing
        [Required]
        [Index("IX_AnalysisJob_JobType", 1)]
        public new AnalysisJobType JobType 
        { 
            get => base.JobType; 
            set => base.JobType = value; 
        }

        [Required]
        [Index("IX_AnalysisJob_Status", 1)]
        [Index("IX_AnalysisJob_Status_CreatedAt", 1)]
        public new AnalysisJobStatus Status 
        { 
            get => base.Status; 
            set => base.Status = value; 
        }

        [Required]
        [Index("IX_AnalysisJob_CreatedAt", 1)]
        [Index("IX_AnalysisJob_Status_CreatedAt", 2)]
        public new DateTimeOffset CreatedAt 
        { 
            get => base.CreatedAt; 
            set => base.CreatedAt = value; 
        }

        [Index("IX_AnalysisJob_CompletedAt", 1)]
        public new DateTimeOffset? CompletedAt 
        { 
            get => base.CompletedAt; 
            set => base.CompletedAt = value; 
        }

        public AnalysisJob() : base()
        {
            // Use Ulid for PostProcessingServer for better time-ordered IDs

            string userId = (this.TilerUserId ?? this.TilerUser?.Id ?? "no-user-");
            SetJobId("anJob_"+ userId + Ulid.NewUlid().ToString());
            CreatedAt = Utility.now();
            ExpiresAt = Utility.now().AddDays(7);
        }

        public override string GenerateJobId()
        {
            string retValue = _idPrefix + (this.TilerUserId ?? this.TilerUser?.Id ?? "no-user-") + (Ulid.NewUlid().ToString());
            return retValue;
        }

        /// <summary>
        /// Creates an AnalysisJobStatusResponse from this job
        /// </summary>
        public AnalysisJobStatusResponse ToStatusResponse()
        {
            return AnalysisJobStatusResponse.FromJobBase(this);
        }
    }
}
