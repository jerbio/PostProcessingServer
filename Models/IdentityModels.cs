using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Data.Entity.Infrastructure.Annotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using TilerCrossServerResources;
using TilerFront.DatabaseModels;
using TilerElements;

namespace PostProcessingServer.Models
{
    // You can add profile data for the user by adding more properties to your ApplicationUser class, please visit https://go.microsoft.com/fwlink/?LinkID=317594 to learn more.
    public class ApplicationUser : TilerUser
    {
        public async Task<ClaimsIdentity> GenerateUserIdentityAsync(UserManager<ApplicationUser> manager)
        {
            // Note the authenticationType must match the one defined in CookieAuthenticationOptions.AuthenticationType
            var userIdentity = await manager.CreateIdentityAsync(this, DefaultAuthenticationTypes.ApplicationCookie);
            // Add custom user claims here
            return userIdentity;
        }
    }

    public class PostProcessorApplicationDbContext : TilerElements.TilerDbContext
    {
        public PostProcessorApplicationDbContext()
            : base("DefaultConnection")
        {
        }

        /// <summary>
        /// DbSet for tracking analysis jobs in the database
        /// </summary>
        public virtual DbSet<AnalysisJob> AnalysisJobs { get; set; }

        /// <summary>
        /// DbSet for user feedback entries
        /// </summary>
        public virtual DbSet<UserFeedback> UserFeedbacks { get; set; }

        /// <summary>
        /// DbSet for feature flag definitions. PostProcessingServer is the authoritative
        /// writer for this table; TilerFront treats it as read-only and only writes
        /// per-user override rows in <c>FeatureFlagOverride</c>. See
        /// <c>TilerFront/docs/FeatureFlagsAndRoles_Design.md</c>.
        /// </summary>
        public virtual DbSet<FeatureFlag> FeatureFlags { get; set; }
        public virtual DbSet<FeatureFlagOverride> FeatureFlagOverrides { get; set; }
        public virtual DbSet<FeatureFlagAuditLog> FeatureFlagAuditLogs { get; set; }
        public virtual DbSet<Permission> Permissions { get; set; }
        public virtual DbSet<RolePermission> RolePermissions { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure AnalysisJob entity
            modelBuilder.Entity<AnalysisJob>()
                .HasKey(j => j.Id);

            // Configure foreign key relationship with no cascade delete
            modelBuilder.Entity<AnalysisJob>()
                .HasRequired(j => j.TilerUser)
                .WithMany()
                .HasForeignKey(j => j.TilerUserId)
                .WillCascadeOnDelete(false);

            // Phase 1 — feature flag / permission indexes.
            // [Index] is unavailable in netstandard2.0 TilerCrossServerResources,
            // so indexes for those entities are declared here via Fluent API.
            modelBuilder.Entity<Permission>()
                .Property(p => p.Key)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_Permission_Key") { IsUnique = true }));

            modelBuilder.Entity<RolePermission>()
                .Property(rp => rp.RoleId)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_RolePermission_Role_Perm", 1) { IsUnique = true }));
            modelBuilder.Entity<RolePermission>()
                .Property(rp => rp.PermissionId)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_RolePermission_Role_Perm", 2) { IsUnique = true }));

            modelBuilder.Entity<FeatureFlagOverride>()
                .Property(o => o.FlagId)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_FlagOverride_Flag", 1)));
            modelBuilder.Entity<FeatureFlagOverride>()
                .Property(o => o.ScopeType)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_FlagOverride_Flag", 2)));
            modelBuilder.Entity<FeatureFlagOverride>()
                .Property(o => o.ScopeValue)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_FlagOverride_Flag", 3)));

            modelBuilder.Entity<FeatureFlagAuditLog>()
                .Property(a => a.FlagName)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_Audit_FlagName")));
            modelBuilder.Entity<FeatureFlagAuditLog>()
                .Property(a => a.Utc)
                .HasColumnAnnotation("Index",
                    new IndexAnnotation(new IndexAttribute("IX_Audit_Utc")));
        }

        public static PostProcessorApplicationDbContext Create()
        {
            return new PostProcessorApplicationDbContext();
        }
    }
}