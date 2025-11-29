using System.Data.Entity;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;

namespace PostProcessingServer.Models
{
    // You can add profile data for the user by adding more properties to your ApplicationUser class, please visit https://go.microsoft.com/fwlink/?LinkID=317594 to learn more.
    public class ApplicationUser : IdentityUser
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

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure AnalysisJob entity
            modelBuilder.Entity<AnalysisJob>()
                .HasKey(j => j.JobId);

            // Configure foreign key relationship with no cascade delete
            modelBuilder.Entity<AnalysisJob>()
                .HasRequired(j => j.TilerUser)
                .WithMany()
                .HasForeignKey(j => j.TilerUserId)
                .WillCascadeOnDelete(false);
        }

        public static PostProcessorApplicationDbContext Create()
        {
            return new PostProcessorApplicationDbContext();
        }
    }
}