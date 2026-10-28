using System;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClusterManager;
using PostProcessingServer.Services;
using PostProcessingServer.Tests.Fixtures;
using TilerCrossServerResources;
using TilerElements;
using VibeServiceCore;
using Xunit;

namespace PostProcessingServer.Tests
{
    [Trait("Category", "Integration")]
    public sealed class TileShareScheduleIntegrationTests : IClassFixture<ScheduleUpdateDatabase>
    {
        private readonly ScheduleUpdateDatabase database;
        public TileShareScheduleIntegrationTests(ScheduleUpdateDatabase database) { this.database = database; }
        private static ReferenceNow Now() => new ReferenceNow(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "UTC");
        private VibeTileShareExecution Executor() => new VibeTileShareExecution(database.Open);
        private Task<VibeAction> Prepare() => database.PrepareAsync();

        private async Task<TileShareExecutionResult> Queue(VibeAction action)
        {
            var review = await Executor().ExecuteAsync(action.TilerUserId, action.Id, Now(), executionMode: ChatSessionExecutionMode.AutoAct);
            Assert.Equal("review_required", review.ErrorCode);
            var queued = await Executor().ExecuteAsync(action.TilerUserId, action.Id, Now(), review.ReviewToken);
            Assert.Equal(TileShareExecutionState.Pending, queued.State);
            return queued;
        }
        private sealed class Planner : ITileShareSchedulePlanner
        {
            public Func<TileShareScheduleWork, Task> During { get; set; }
            public async Task PlanAsync(TileShareScheduleWork work, CancellationToken cancellation)
            {
                foreach (var calendar in work.Calendars)
                foreach (var session in calendar.Sessions.Where(s => !s.Preserved)) session.EndMs = session.StartMs + session.DurationMs;
                if (During != null) await During(work);
            }
        }

        private sealed class StopAfterJob : IBackgroundJobHandler
        {
            private readonly IBackgroundJobHandler handler;
            private readonly CancellationTokenSource stop;
            public StopAfterJob(IBackgroundJobHandler handler, CancellationTokenSource stop) { this.handler = handler; this.stop = stop; }
            public string Name => handler.Name;
            public async Task<bool> TryProcessNextAsync(CancellationToken cancellation)
            {
                var processed = await handler.TryProcessNextAsync(cancellation);
                if (processed) stop.Cancel();
                return processed;
            }
        }

        [Fact]
        public async Task SharedPpsDispatcherRecoversInterruptedTileShareJobAndCommitsOnce()
        {
            var action = await Prepare(); var queued = await Queue(action);
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var handler = new TileShareScheduleJobHandler(new VibeTileShareScheduleJobProcessor(database.Open),
                    new Planner { During = work => { stop.Cancel(); return Task.CompletedTask; } });
                await new BackgroundJobDispatcher(new[] { handler }, 1, TimeSpan.FromMilliseconds(10)).RunAsync(stop.Token);
            }
            await AssertOriginal(action);
            using (var db = database.Open())
            {
                var interrupted = await db.TileShareScheduleUpdateJobs.SingleAsync(j => j.Id == queued.JobId);
                Assert.Equal(TileShareScheduleJobState.Planning, interrupted.State);
                interrupted.AvailableAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var handler = new TileShareScheduleJobHandler(new VibeTileShareScheduleJobProcessor(database.Open), new Planner());
                await new BackgroundJobDispatcher(new[] { new StopAfterJob(handler, stop) }, 1,
                    TimeSpan.FromMilliseconds(10)).RunAsync(stop.Token);
            }
            Assert.Equal(TileShareExecutionState.Applied, (await Executor().ExecuteAsync(action.TilerUserId, action.Id, Now())).State);
            using (var db = database.Open())
            {
                Assert.Equal(TileShareScheduleJobState.Completed, (await db.TileShareScheduleUpdateJobs.SingleAsync(j => j.Id == queued.JobId)).State);
                Assert.Equal(1, await db.TileShareActivities.CountAsync(a => a.ActorId == action.TilerUserId && a.EventType_DB == TileShareActivityEventTypes.TiletteEdited));
                Assert.Equal(1, await db.TileShareScheduleUpdateJobs.CountAsync(j => j.ActionId == action.Id));
            }
        }
        private async Task AssertOriginal(VibeAction action)
        {
            using (var db = database.Open())
            {
                Assert.Equal(90 * 60000, (await db.CalEvents.SingleAsync(c => c.CreatorId == action.TilerUserId)).Duration_EventDB);
                Assert.Equal("Review", await db.TileShareTemplates.Where(t => t.TemplateCluster_DB.CreatorId == action.TilerUserId).Select(t => t.TileShareTemplateTileName_DB.Name).SingleAsync());
                Assert.False(await db.TileShareActivities.AnyAsync(a => a.ActorId == action.TilerUserId && a.EventType_DB == TileShareActivityEventTypes.TiletteEdited));
            }
        }

        [Theory]
        [InlineData(1, null)]
        [InlineData(40, "schedule_outside_forecast")]
        public async Task AuthoritativeSchedulerForecastDoesNotSaveUntilDomainCommit(int startDays, string expectedError)
        {
            await VerifyEngineForecast(startDays, expectedError);
        }

        private async Task VerifyEngineForecast(int startDays, string expectedError)
        {
            var action = await Prepare();
            var now = DateTimeOffset.UtcNow;
            using (var db = database.Open()) {
                var parent = await db.CalEvents.Include(c => c.AllSubEvents_DB).SingleAsync(c => c.CreatorId == action.TilerUserId);
                parent.StartTime_EventDB = now.AddDays(startDays - 1); parent.EndTime_EventDB = now.AddDays(startDays + 2);
                var template = await db.TileShareTemplates.SingleAsync(t => t.TemplateCluster_DB.CreatorId == action.TilerUserId);
                template.StartTime_DB = parent.Start.ToUnixTimeMilliseconds(); template.EndtTime_DB = parent.End.ToUnixTimeMilliseconds();
                int i = 0;
                foreach (var session in parent.AllSubEvents_DB) {
                    session.StartTime_EventDB = session.Complete_EventDB ? now.AddHours(-2) : now.AddDays(startDays).AddHours(i++);
                    session.EndTime_EventDB = session.Start.AddMinutes(30);
                }
                await db.SaveChangesAsync();
            }
            var queued = await Queue(action);
            var planner = new PostProcessingServer.Services.TileShareScheduleUpdatePlanner(() => new TilerFront.Models.ApplicationDbContext(database.Connection));
            var inspected = new InspectingPlanner(planner, () => AssertOriginal(action));
            await new VibeTileShareScheduleJobProcessor(database.Open).ProcessNextScheduleUpdateAsync(inspected, CancellationToken.None);
            var result = await Executor().ExecuteAsync(action.TilerUserId, action.Id, Now());
            if (expectedError == null)
                Assert.True(result.State == TileShareExecutionState.Applied, result.State + ": " + result.ErrorCode + " " + inspected.Error);
            else {
                Assert.Equal(TileShareExecutionState.Failed, result.State);
                Assert.Equal(expectedError, result.ErrorCode);
                await AssertOriginal(action);
            }
        }

        private sealed class InspectingPlanner : ITileShareSchedulePlanner
        {
            private readonly ITileShareSchedulePlanner inner;
            private readonly Func<Task> inspect;
            public Exception Error { get; private set; }
            public InspectingPlanner(ITileShareSchedulePlanner inner, Func<Task> inspect) { this.inner = inner; this.inspect = inspect; }
            public async Task PlanAsync(TileShareScheduleWork work, CancellationToken cancellation)
            {
                try { await inner.PlanAsync(work, cancellation); }
                catch (Exception error) { Error = error; throw; }
                await inspect();
            }
        }

    }
}
