using PostProcessingServer.Models;
using PostProcessingServer.Services;
using PostProcessingServer.Utilities;
using ScheduleAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Migrations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using TilerElements;
//using TilerFront;
//using TilerFront.Controllers;
//using TilerFront.Models;
//using AnalysisJobStatus = PostProcessingServer.Models.AnalysisJobStatus;
//using AnalysisJobType = PostProcessingServer.Models.AnalysisJobType;

namespace PostProcessingServer.Services
{
    /// <summary>
    /// Background service that processes analysis jobs from the queue
    /// Implements IRegisteredObject for graceful shutdown in ASP.NET
    /// </summary>
    public class AnalysisJobProcessor : IRegisteredObject
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private Task _processingTask;
        private readonly AnalysisJobQueue _jobQueue = AnalysisJobQueue.Instance;
        private readonly int _maxConcurrentJobs;
        private readonly SemaphoreSlim _concurrencySemaphore;

        private static readonly Lazy<AnalysisJobProcessor> _instance = new Lazy<AnalysisJobProcessor>(() => new AnalysisJobProcessor());
        public static AnalysisJobProcessor Instance => _instance.Value;

        private AnalysisJobProcessor()
        {
            // Load max concurrent jobs from configuration
            _maxConcurrentJobs = ConfigurationUtility.GetInt(
                ConfigurationUtility.MaxConcurrentJobsKey,
                ConfigurationUtility.DefaultMaxConcurrentJobs);
            
            _concurrencySemaphore = new SemaphoreSlim(_maxConcurrentJobs, _maxConcurrentJobs);
        }

        /// <summary>
        /// Starts the background job processor
        /// </summary>
        public void Start()
        {
            HostingEnvironment.RegisterObject(this);
            _processingTask = Task.Run(() => ProcessJobsAsync(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// Main processing loop
        /// </summary>
        private async Task ProcessJobsAsync(CancellationToken cancellationToken)
        {
            System.Diagnostics.Trace.WriteLine("AnalysisJobProcessor started");

            // Start cleanup task
            var cleanupTask = Task.Run(() => CleanupLoopAsync(cancellationToken), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for available slot
                    await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                    // Dequeue next job
                    var job = await _jobQueue.DequeueAsync(cancellationToken).ConfigureAwait(false);

                    if (job != null)
                    {
                        // Process job in background task
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessJobAsync(job, cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                _concurrencySemaphore.Release();
                            }
                        }, cancellationToken);
                    }
                    else
                    {
                        _concurrencySemaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Error in processing loop: {ex.Message}");
                    _concurrencySemaphore.Release();
                    
                    // Use configurable poll interval for error retry
                    int pollIntervalMs = ConfigurationUtility.GetInt(
                        ConfigurationUtility.JobPollIntervalMsKey,
                        ConfigurationUtility.DefaultJobPollIntervalMs);
                    await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
                }
            }

            System.Diagnostics.Trace.WriteLine("AnalysisJobProcessor stopped");
        }

        /// <summary>
        /// Processes a single job
        /// </summary>
        private async Task ProcessJobAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            System.Diagnostics.Trace.WriteLine($"Processing job {job.JobId} of type {job.JobType}");

            try
            {
                // Process based on job type
                switch (job.JobType)
                {
                    case AnalysisJobType.Suggestion:
                        await ProcessSuggestionAnalysisAsync(job, cancellationToken).ConfigureAwait(false);
                        break;

                    case AnalysisJobType.ScheduleAnalysis:
                        await ProcessScheduleAnalysisAsync(job, cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        throw new NotSupportedException($"Job type {job.JobType} is not supported");
                }

                _jobQueue.UpdateJobStatus(job.JobId, AnalysisJobStatus.Completed);
                System.Diagnostics.Trace.WriteLine($"Job {job.JobId} completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Job {job.JobId} failed: {ex.Message}");
                
                // Retry logic
                if (job.RetryCount < 3)
                {
                    job.RetryCount++;
                    job.Status = AnalysisJobStatus.Queued;
                    _jobQueue.Enqueue(job);
                    System.Diagnostics.Trace.WriteLine($"Job {job.JobId} requeued for retry {job.RetryCount}");
                }
                else
                {
                    _jobQueue.UpdateJobStatus(job.JobId, AnalysisJobStatus.Failed, 
                        errorMessage: $"Failed after {job.RetryCount} retries: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Processes a suggestion analysis job
        /// TODO: Implement actual analysis logic from TilerFront.AnalysisController.SuggestionAnalysis
        /// </summary>
        private async Task ProcessSuggestionAnalysisAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            // Placeholder implementation
            // In the next step, we'll move the actual SuggestionAnalysis logic here
            
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            
            string result = $"{{\"status\": \"success\", \"jobId\": \"{job.JobId}\", \"type\": \"suggestion\"}}";
            _jobQueue.UpdateJobStatus(job.JobId, AnalysisJobStatus.Completed, resultData: result);
        }

        private async Task SuggestionAnalysis(TilerFront.LogControl logControl, TilerElements.Location requestLocation, TilerFront.Models.ApplicationDbContext db)
        {
#if liveDebugging
            return;
#endif

            if (logControl.Now == null)
            {
                logControl.Now = new ReferenceNow(DateTimeOffset.UtcNow, logControl.getTilerRetrievedUser().EndOfDay, logControl.getTilerRetrievedUser(), logControl.getTilerRetrievedUser().TimeZone_DB);
            }
            else
            {
                logControl.Now = new ReferenceNow(logControl.Now.constNow, logControl.getTilerRetrievedUser().EndOfDay, logControl.getTilerRetrievedUser(), logControl.getTilerRetrievedUser().TimeZone_DB);
            }

            DateTimeOffset nowTime = logControl.Now.constNow;
#if disableAnalysisController
            TimeLine notificationTimeline = new TimeLine(logControl.Now.constNow.AddDays(ScheduleSuggestionsAnalysis.defaultBeginDay), logControl.Now.constNow.AddDays(ScheduleSuggestionsAnalysis.defaultEndDay));
            List<ThirdPartyCalendarAuthentication> notificationAllIndexedThirdParty = await ScheduleController.getAllThirdPartyAuthentication(logControl.LoggedUserID, db).ConfigureAwait(false);
            List<GoogleTilerEventControl> notificationAllGoogleTilerEvents = notificationAllIndexedThirdParty.Select(obj => new GoogleTilerEventControl(obj as GoogleThirdPartyCalendarAuthentication, db)).ToList();
            var notificationtupleOfSubEventsAndAnalysis = await logControl.getSubCalendarEventForAnalysis(notificationTimeline, logControl.getTilerRetrievedUser()).ConfigureAwait(false);
            List<SubCalendarEvent> notificationSubEvents = notificationtupleOfSubEventsAndAnalysis.Item1.ToList();
            List<CalendarEvent> notificationCalEvents = new HashSet<CalendarEvent>(notificationSubEvents.Select(o => o.ParentCalendarEvent)).ToList();
            await UpdateNotifications(logControl, false).ConfigureAwait(false);
            await logControl.Commit(notificationCalEvents, null, "0", null, logControl.Now, null, requestLocation).ConfigureAwait(false);
            return;
#endif
            TimeLine timeline = new TimeLine(logControl.Now.constNow.AddDays(ScheduleSuggestionsAnalysis.defaultBeginDay), logControl.Now.constNow.AddDays(ScheduleSuggestionsAnalysis.defaultEndDay));
            var AllIndexedThirdPartyTask = TilerFront.Controllers.ScheduleController.getAllThirdPartyAuthentication(logControl.LoggedUserID, db);
            List<ThirdPartyCalendarAuthentication> AllIndexedThirdParty = await AllIndexedThirdPartyTask.ConfigureAwait(false);
            var tupleOfSubEventsAndAnalysis = await logControl.getSubCalendarEventForAnalysis(timeline, logControl.getTilerRetrievedUser()).ConfigureAwait(false);
            List<TilerFront.GoogleTilerEventControl> AllGoogleTilerEvents = AllIndexedThirdParty.Where(o => o is TilerFront.Models.GoogleThirdPartyCalendarAuthentication).Select(obj => new TilerFront.GoogleTilerEventControl(obj as TilerFront.Models.GoogleThirdPartyCalendarAuthentication, db)).ToList();
            List<SubCalendarEvent> subEvents = tupleOfSubEventsAndAnalysis.Item1.ToList();
            Analysis analysis = tupleOfSubEventsAndAnalysis.Item2;
            var simulacra = tupleOfSubEventsAndAnalysis.Item3;

            if (simulacra == null)
            {
                simulacra = new Simulacra(logControl.Now, logControl.getTilerRetrievedUser());
                logControl.Database.Simulacras.AddOrUpdate(simulacra);
            }
            Task<ConcurrentBag<CalendarEvent>> GoogleCalEventsTask = TilerFront.GoogleTilerEventControl.getAllCalEvents(AllGoogleTilerEvents, timeline, logControl.Now, deleteBrokenTokenIssues: true);
            //var UpdateNotificationsTask = UpdateNotifications(logControl, false);
            IEnumerable<CalendarEvent> GoogleCalEvents = await GoogleCalEventsTask.ConfigureAwait(false);

            subEvents.AddRange(GoogleCalEvents.SelectMany(o => o.AllSubEvents));

            foreach (CalendarEvent calEvent in new HashSet<CalendarEvent>(subEvents.Select(o => o.ParentCalendarEvent)))
            {
                calEvent.backUpAndresetDeadlineSuggestions();
                calEvent.resetSleepDeadlineSuggestion();
            }

            ScheduleSuggestionsAnalysis scheduleSuggestion = new ScheduleSuggestionsAnalysis(subEvents, logControl.Now, logControl.getTilerRetrievedUser(), analysis);
            var overoccupiedTimelines = scheduleSuggestion.getOverLoadedWeeklyTimelines(nowTime, scheduleSuggestion.getActiveRatio());
            List<TimeLine> weeklySeparationsOfoveroccupiedTimelines = generated7DaySeparations(overoccupiedTimelines);
            var suggestion = scheduleSuggestion.suggestScheduleChange(weeklySeparationsOfoveroccupiedTimelines, scheduleSuggestion.getActiveRatio());
            //var suggestion = scheduleSuggestion.suggestScheduleChange(overoccupiedTimelines, scheduleSuggestion.getActiveRatio());
            suggestion.updateDeadlineSuggestions();


            var overoccupiedSleepTimelines = scheduleSuggestion.getOverLoadedWeeklyTimelines(nowTime, scheduleSuggestion.getSleepRatio());
            List<TimeLine> weeklySeparationsOfOveroccupiedSleepTimelines = generated7DaySeparations(overoccupiedSleepTimelines);
            var sleepSuggestionDeadlines = scheduleSuggestion.suggestScheduleChange(weeklySeparationsOfOveroccupiedSleepTimelines, scheduleSuggestion.getSleepRatio());
            //var sleepSuggestionDeadlines = scheduleSuggestion.suggestScheduleChange(overoccupiedSleepTimelines, scheduleSuggestion.getSleepRatio());
            foreach (KeyValuePair<CalendarEvent, TimeLine> kvp in sleepSuggestionDeadlines.DeadlineUpdates)
            {
                kvp.Key.updateSleepDeadlineSuggestion(kvp.Value.End);
            }
            List<CalendarEvent> calEvents = new HashSet<CalendarEvent>(subEvents.Select(o => o.ParentCalendarEvent)).ToList();

            Dictionary<string, LocationAndEventNameMap> locationsAndNames = new Dictionary<string, LocationAndEventNameMap>();

            uint count = TilerFront.ServerContants.simulacraCacheTileNameCountSize;
            foreach (var eachCalEvent in calEvents.OrderByDescending(o => o.TimeCreated))
            {
                if (eachCalEvent.NameId != null && eachCalEvent.LocationId.isNot_NullEmptyOrWhiteSpace())
                {
                    LocationAndEventNameMap locationAndEventNameMap = LocationAndEventNameMap.fromTilerEvent(logControl.Now, eachCalEvent, simulacra);
                    locationAndEventNameMap.TileName = eachCalEvent.Name;
                    if (!locationsAndNames.ContainsKey(locationAndEventNameMap.Id))
                    {
                        locationsAndNames.Add(locationAndEventNameMap.Id, locationAndEventNameMap);
                        if (locationsAndNames.Count >= count)
                        {
                            break;
                        }
                    }
                }
            }

            var locationLookupMapping = locationsAndNames.Values.ToLookup(o => o.LocationId, o => o);
            var locationIds = new HashSet<string>(locationsAndNames.Values.Select(o => o.LocationId)).ToList();


            var res = await logControl.Database.Locations.Join(
                locationIds, location => location.Id, locationId => locationId,
                (location, locationId) => location).ToListAsync().ConfigureAwait(false);

            List<LocationAndEventNameMap> nonEmptyLocationTiles = new List<LocationAndEventNameMap>();
            foreach (Location location in res)
            {
                if (location != null && location.isNotNullAndNotDefault)
                {
                    var locationAndEventNameMap = locationLookupMapping[location.Id];
                    foreach (var eachLocationAndEventNameMap in locationAndEventNameMap)
                    {
                        eachLocationAndEventNameMap.Location = location;
                    }
                    nonEmptyLocationTiles.AddRange(locationAndEventNameMap);
                }
            }



            simulacra.PresetLocationQAndA = simulacra.generateQandAFromLocationAndEventNameMappings(nonEmptyLocationTiles, logControl.Now);

            //await UpdateNotificationsTask.ConfigureAwait(false);
            await logControl.Commit(calEvents, null, "0", null, logControl.Now, null, requestLocation).ConfigureAwait(false);
        }


        private static List<TimeLine> generated7DaySeparations(List<TimeLine> overoccupiedTimelines)
        {
            List<TimeLine> weeklySeparations = new List<TimeLine>();
            if (overoccupiedTimelines.Count > 0)
            {
                List<TimeLine> orderdOveroccupiedTimelines = overoccupiedTimelines.OrderBy(o => o.Start).ThenBy(o => o.End).ToList();
                weeklySeparations.Add(orderdOveroccupiedTimelines.First());
                TimeSpan extensionSpan = TimeSpan.FromDays(Utility.OneWeekTimeSpan.TotalDays * 4);
                DateTimeOffset nextBound = weeklySeparations.Last().End.Add(extensionSpan);
                foreach (TimeLine eachTimeLine in orderdOveroccupiedTimelines)
                {
                    if (eachTimeLine.End >= nextBound)
                    {
                        nextBound = nextBound.Add(extensionSpan);
                        weeklySeparations.Add(eachTimeLine);
                    }
                }
            }

            return weeklySeparations;
        }

        /// <summary>
        /// Processes a schedule analysis job
        /// TODO: Implement actual analysis logic from TilerFront.AnalysisController.SuggestionAnalysis
        /// </summary>
        private async Task ProcessScheduleAnalysisAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            // Placeholder implementation
            // In the next step, we'll move the actual SuggestionAnalysis logic here
            
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            
            string result = $"{{\"status\": \"success\", \"jobId\": \"{job.JobId}\", \"type\": \"scheduleAnalysis\"}}";
            _jobQueue.UpdateJobStatus(job.JobId, AnalysisJobStatus.Completed, resultData: result);
        }

        /// <summary>
        /// Periodic cleanup of old jobs
        /// </summary>
        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Use configurable cleanup interval
                    int cleanupIntervalHours = ConfigurationUtility.GetInt(
                        ConfigurationUtility.CleanupIntervalHoursKey,
                        ConfigurationUtility.DefaultCleanupIntervalHours);
                    await Task.Delay(TimeSpan.FromHours(cleanupIntervalHours), cancellationToken).ConfigureAwait(false);
                    
                    // Use configurable cleanup age
                    int cleanupAgeHours = ConfigurationUtility.GetInt(
                        ConfigurationUtility.JobCleanupAgeHoursKey,
                        ConfigurationUtility.DefaultJobCleanupAgeHours);
                    int removed = _jobQueue.CleanupOldJobs(TimeSpan.FromHours(cleanupAgeHours));
                    if (removed > 0)
                    {
                        System.Diagnostics.Trace.WriteLine($"Cleaned up {removed} old jobs");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Error in cleanup loop: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Graceful shutdown
        /// </summary>
        public void Stop(bool immediate)
        {
            System.Diagnostics.Trace.WriteLine("AnalysisJobProcessor stopping...");
            _cancellationTokenSource.Cancel();
            
            if (!immediate && _processingTask != null)
            {
                try
                {
                    _processingTask.Wait(TimeSpan.FromSeconds(30));
                }
                catch
                {
                    // Ignore
                }
            }

            HostingEnvironment.UnregisterObject(this);
        }
    }
}
