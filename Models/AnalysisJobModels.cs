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


        // Override indexes from base class for PostProcessingServer-specific
        // indexing. The enum-typed JobType/Status are [NotMapped] proxies on
        // the base; the actual columns are JobType_DB / Status_DB strings, so
        // indexes belong on the string columns.
        [Required]
        [StringLength(50)]
        [Column("JobType")]
        [Index("IX_AnalysisJob_JobType", 1)]
        public override string JobType_DB
        {
            get => base.JobType_DB;
            set => base.JobType_DB = value;
        }

        [Required]
        [StringLength(50)]
        [Column("Status")]
        [Index("IX_AnalysisJob_Status", 1)]
        [Index("IX_AnalysisJob_Status_CreatedAt", 1)]
        public override string Status_DB
        {
            get => base.Status_DB;
            set => base.Status_DB = value;
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
            CreatedAt = Utility.now();
            ExpiresAt = Utility.now().AddDays(7);
        }

        public override string GenerateJobId()
        {
            string retValue = _idPrefix + (this.TilerUserId ?? this.TilerUser?.Id ?? "no-user") +"_"+ (Ulid.NewUlid().ToString());
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
