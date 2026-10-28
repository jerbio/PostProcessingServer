using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClusterManager;
using Newtonsoft.Json.Linq;
using TilerElements;
using TilerFront.Models;

namespace PostProcessingServer.Tests.Fixtures
{
    // PPS owns this host-model fixture. It does not load another host's test assembly
    // or call test methods to construct its data. Instantiated only by integration tests.
    public sealed class ScheduleUpdateDatabase : IDisposable
    {
        public string Connection { get; }
        private static readonly DateTimeOffset Start = new DateTimeOffset(2035, 1, 2, 9, 0, 0, TimeSpan.Zero);

        public ScheduleUpdateDatabase()
        {
            Database.SetInitializer<TilerDbContext>(null);
            Database.SetInitializer<ApplicationDbContext>(null);
            Connection = new SqlConnectionStringBuilder {
                DataSource = @"(localdb)\MSSQLLocalDB", IntegratedSecurity = true,
                InitialCatalog = "PpsScheduleTests_" + Guid.NewGuid().ToString("N")
            }.ConnectionString;
            using (var db = new ApplicationDbContext(Connection))
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(Connection);
                    var catalog = builder.InitialCatalog;
                    builder.InitialCatalog = "master";
                    using (var admin = new SqlConnection(builder.ConnectionString))
                    {
                        admin.Open();
                        using (var command = admin.CreateCommand())
                        {
                            command.CommandText = "CREATE DATABASE [" + catalog + "]";
                            command.ExecuteNonQuery();
                        }
                    }
                    // Preserve the established fixture workaround for legacy multiple-cascade paths.
                    var ddl = ((IObjectContextAdapter)db).ObjectContext.CreateDatabaseScript();
                    db.Database.ExecuteSqlCommand(Regex.Replace(ddl, @"(?i)ON\s+DELETE\s+CASCADE", ""));
                }
                catch { db.Database.Delete(); throw; }
            }
        }

        public TilerDbContext Open() => new TilerDbContext(Connection);

        public async Task<VibeAction> PrepareAsync()
        {
            using (var db = Open())
            {
                var user = db.Users.Add(new TilerUser {
                    Id = Guid.NewGuid().ToString(), UserName = Guid.NewGuid().ToString(), IsAnonymous_DB = false
                });
                var now = new ReferenceNow(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, user);
                var session = db.VibeSessions.Add(new VibeSession(now, null, user));
                var request = db.VibeRequests.Add(new VibeRequest(now) {
                    Id = Guid.NewGuid().ToString("N"), TilerUserId = user.Id,
                    VibeSession = session, VibeSessionId = session.Id
                });
                await db.SaveChangesAsync();
                var manager = new TileShareClusterManager(db, now);
                var project = await manager.CreateProjectAsync(user.Id, new TileShareProjectInput { Name = "Launch" });
                var template = await manager.CreateTiletteAsync(user.Id, new TileShareTiletteInput {
                    ClusterId = project.Id, Name = "Review", DurationInMs = 90 * 60000
                });
                template.StartTime_DB = Start.ToUnixTimeMilliseconds();
                template.EndtTime_DB = Start.AddDays(7).ToUnixTimeMilliseconds();
                var assignment = db.DesignatedTileTemplates.Add(new DesignatedTileTemplate {
                    Id = "timeline-assignment-" + Guid.NewGuid().ToString("N"),
                    TileTemplateId = template.Id, ClusterId = project.Id,
                    DesignatedUserId = user.Id, DisplayedIdentifier_DB = Guid.NewGuid().ToString(),
                    InvitationStatus_DB = "accepted", CreationTime_DB = 1
                });
                var parent = db.CalEvents.Add(new CalendarEvent(true) {
                    Id = EventID.GenerateCalendarEvent(user.Id).ToString(), CreatorId = user.Id, Creator_EventDB = user,
                    DesignatedTileTemplateId = assignment.Id,
                    StartTime_EventDB = Start, EndTime_EventDB = Start.AddDays(7), Duration_EventDB = 90 * 60000,
                    IsEnabled_DB = true, NumberOfSplit = 3, ThirdPartyTypeInfo_EventDB = "tiler"
                });
                for (int i = 0; i < 3; i++)
                {
                    var part = SubCalendarEvent.getSimpleSubCalendarEvent(new EventID(parent.Id));
                    part.ParentCalendarEvent = parent; part.CalendarEventId = parent.Id;
                    part.CreatorId = user.Id; part.Creator_EventDB = user;
                    part.CanPersist = true; part.IsEnabled_DB = true; part.RigidSchedule_EventDB = false;
                    part.Complete_EventDB = i == 0;
                    part.StartTime_EventDB = Start.AddHours(i); part.EndTime_EventDB = Start.AddHours(i).AddMinutes(30);
                    part.Duration_EventDB = 30 * 60000;
                    db.SubEvents.Add(part);
                }
                foreach (var name in db.ChangeTracker.Entries<EventName>().Where(e => e.State == EntityState.Added))
                    name.Entity.Creator_EventDB = user;
                var action = new VibeAction("Update shared work", VibeQuery.VibeActionOptions.TileShare_Update_Tilette, user.Id, now) {
                    VibeRequest = request, VibeRequestId = request.Id, VibeSession = session, VibeSessionId = session.Id,
                    FunctionResult = new FunctionCallResult {
                        Name = nameof(VibeQuery.VibeActionOptions.TileShare_Update_Tilette),
                        Arguments = new JObject {
                            ["projectId"] = project.Id, ["tiletteId"] = template.Id,
                            ["changes"] = new JObject { ["name"] = "Reviewed update", ["durationMinutes"] = 120 }
                        }.ToString()
                    }
                };
                action.setState(ActionState.Parsed);
                db.VibeActions.Add(action);
                await db.SaveChangesAsync();
                return action;
            }
        }

        public void Dispose() { using (var db = new ApplicationDbContext(Connection)) db.Database.Delete(); }
    }
}
