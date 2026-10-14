using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClusterManager;
using TilerElements;
using TilerFront;
using TilerFront.Models;

namespace PostProcessingServer.Services
{
    // Uses the authoritative engine as a read-only allocator. Only reviewed target sessions
    // are later persisted by ClusterManager; account.Commit/persistToDB are never called.
    public sealed class TileShareScheduleUpdatePlanner : ITileShareSchedulePlanner
    {
        private readonly Func<ApplicationDbContext> open;
        public TileShareScheduleUpdatePlanner() : this(() => new ApplicationDbContext()) { }
        public TileShareScheduleUpdatePlanner(Func<ApplicationDbContext> open) { this.open = open ?? throw new ArgumentNullException(nameof(open)); }
        public async Task PlanAsync(TileShareScheduleWork work, CancellationToken cancellation)
        {
            foreach (var group in work.Calendars.GroupBy(c => c.UserId))
            using (var db = open())
            {
                cancellation.ThrowIfCancellationRequested();
                db.setAsReadOnly();
                var account = new UserAccountDirect(group.Key, db);
                if (!await account.Login().ConfigureAwait(false)) throw new TileShareScheduleUpdateException("participant_unavailable");
                ((LogControlDirect)account.ScheduleLogControl).ReadOnly = true;
                var timestamp = DateTimeOffset.UtcNow;
                var forecastEnd = timestamp.AddDays(TileSharePreview.HorizonDays);
                var location = Location.getDefaultLocation();
                var schedule = new DB_Schedule(account, timestamp, location, retrievalOptions: DataRetrievalSet.forecast,
                    rangeOfLookup: new TimeLine(timestamp.AddDays(-1), forecastEnd),
                    createDump: false, calendarIds: new HashSet<string>(group.Select(c => c.Id)), loadDataFromDB: false);
                await schedule.Initialize(timestamp, location, new HashSet<string>(group.Select(c => c.Id))).ConfigureAwait(false);
                if (!schedule.isScheduleLoadSuccessful) throw new TileShareScheduleUpdateException("participant_unavailable");
                var thirdParty = await account.ScheduleLogControl.readThirdPartyThirdpartyCalendarInfo(group.Key, schedule.Now, new TimeLine(timestamp.AddDays(-1), forecastEnd)).ConfigureAwait(false);
                schedule.updateDataSetWithThirdPartyData(thirdParty);
                var calendars = schedule.getAllCalendarEvents().ToDictionary(c => c.Id);
                var adjustable = new HashSet<string>(group.SelectMany(c => c.Sessions).Where(s => !s.Preserved).Select(s => s.Id));
                var held = calendars.Values.SelectMany(c => c.AllSubEvents).Where(s => !adjustable.Contains(s.Id))
                    .GroupBy(s => s.Id).Select(g => g.First()).Select(s => new { Session = s, s.Start, s.End, Duration = s.Duration_EventDB, s.TravelTimeBefore_DB, s.TravelTimeAfter_DB }).ToList();
                foreach (var item in held) item.Session.SetUserLocked(true);
                foreach (var target in group)
                {
                    if (!calendars.TryGetValue(target.Id, out var calendar)) throw new TileShareScheduleUpdateException("accepted_calendar_missing");
                    calendar.Now = schedule.Now;
                    var planningEnd = DateTimeOffset.FromUnixTimeMilliseconds(Math.Min(target.EndMs, forecastEnd.ToUnixTimeMilliseconds()));
                    var planningStart = DateTimeOffset.FromUnixTimeMilliseconds(target.StartMs);
                    if (planningStart >= planningEnd) throw new TileShareScheduleUpdateException("schedule_outside_forecast");
                    // Connected-calendar data is only loaded through forecastEnd. Never approve
                    // a placement beyond that coverage merely because the task deadline is later.
                    calendar.updateTimeLine(new TimeLine(planningStart, planningEnd), schedule.Now);
                    calendar.Duration_EventDB = target.TotalMs;
                    calendar.IsAutoDeadline_DB = false; calendar.IsAutoRevisableDeadline_DB = false;
                    foreach (var next in target.Sessions.Where(s => !s.Preserved))
                    {
                        var session = calendar.AllSubEvents.SingleOrDefault(s => s.Id == next.Id);
                        if (session == null) throw new TileShareScheduleUpdateException("schedule_changed");
                        if (session.TravelTimeBefore_DB > 0 || session.TravelTimeAfter_DB > 0)
                            throw new TileShareScheduleUpdateException("travel_schedule_update_required");
                        session.Now = schedule.Now; session.Duration_EventDB = next.DurationMs;
                        session.PinToEnd(new TimeLine(calendar.Start, calendar.End));
                    }
                }
                schedule.SetShuffleSeed(0);
                await schedule.UpdateSchedule(isRevise: true).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (held.Any(x => x.Session.Start != x.Start || x.Session.End != x.End || x.Session.Duration_EventDB != x.Duration || x.Session.TravelTimeBefore_DB != x.TravelTimeBefore_DB || x.Session.TravelTimeAfter_DB != x.TravelTimeAfter_DB))
                    throw new TileShareScheduleUpdateException("protected_work_changed");
                var sessions = schedule.getAllCalendarEvents().SelectMany(c => c.AllSubEvents).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
                foreach (var target in group)
                foreach (var next in target.Sessions.Where(s => !s.Preserved))
                {
                    if (!sessions.TryGetValue(next.Id, out var session) || !session.isViable || session.TravelTimeBefore_DB > 0 || session.TravelTimeAfter_DB > 0 || session.Start < timestamp || session.End > forecastEnd ||
                        session.Start.ToUnixTimeMilliseconds() < target.StartMs || session.End.ToUnixTimeMilliseconds() > target.EndMs ||
                        (long)(session.End - session.Start).TotalMilliseconds != next.DurationMs ||
                        sessions.Values.Any(s => s.Id != session.Id && s.isActive && s.Start < session.End && s.End > session.Start))
                        throw new TileShareScheduleUpdateException("schedule_no_fit");
                    next.StartMs = session.Start.ToUnixTimeMilliseconds(); next.EndMs = session.End.ToUnixTimeMilliseconds();
                }
            }
        }
    }
}
