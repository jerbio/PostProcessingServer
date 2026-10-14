namespace TilerFront.Migrations
{
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Migrations;
    using System.Linq;

    internal sealed class Configuration : DbMigrationsConfiguration<TilerFront.Models.ApplicationDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(TilerFront.Models.ApplicationDbContext context)
        {
            //  This method will be called after migrating to the latest version.

            //  You can use the DbSet<T>.AddOrUpdate() helper extension method
            //  to avoid creating duplicate seed data.
            context.TilerSubscriptions.AddOrUpdate(TilerElements.TilerSubscription.FreeSubcription);
            context.TilerSubscriptions.AddOrUpdate(TilerElements.TilerSubscription.WaitListSubcription);
            context.TilerSubscriptions.AddOrUpdate(TilerElements.TilerSubscription.ProSubcription);
            context.TilerSubscriptions.AddOrUpdate(TilerElements.TilerSubscription.EnterpriseSubcription);
            context.TilerSubscriptions.AddOrUpdate(TilerElements.TilerSubscription.CustomSubcription);
            base.Seed(context);
        }
    }
}
