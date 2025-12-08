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
using TilerFront;
using TilerCrossServerResources;

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
            
            // Load incomplete jobs from database before starting processing
            int loadedJobCount = _jobQueue.LoadIncompleteJobsFromDatabase();
            System.Diagnostics.Trace.WriteLine($"AnalysisJobProcessor starting with {loadedJobCount} jobs loaded from database");
            
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
            System.Diagnostics.Trace.WriteLine($"Processing job {job.Id} of type {job.JobType}");

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

                    case AnalysisJobType.ProcessRequest:
                        await ProcessRequestPreviewAsync(job, cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        throw new NotSupportedException($"Job type {job.JobType} is not supported");
                }

                _jobQueue.UpdateJobStatus(job.Id, AnalysisJobStatus.Completed);
                System.Diagnostics.Trace.WriteLine($"Job {job.Id} completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Job {job.Id} failed: {ex.Message}");
                
                // Retry logic
                if (job.RetryCount < 3)
                {
                    job.RetryCount++;
                    job.Status = AnalysisJobStatus.Queued;
                    _jobQueue.Enqueue(job);
                    System.Diagnostics.Trace.WriteLine($"Job {job.Id} requeued for retry {job.RetryCount}");
                }
                else
                {
                    _jobQueue.UpdateJobStatus(job.Id, AnalysisJobStatus.Failed, 
                        errorMessage: $"Failed after {job.RetryCount} retries: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Processes a suggestion analysis job
        /// </summary>
        private async Task ProcessSuggestionAnalysisAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            try
            {
                // Deserialize the request data
                var requestData = Newtonsoft.Json.JsonConvert.DeserializeObject<TilerFront.Models.LocationEnabledUserRequest>(job.RequestData);
                
                if (requestData == null)
                {
                    throw new ArgumentException("Invalid request data for suggestion analysis job");
                }

                // Create database context for this job
                using (var db = new TilerFront.Models.ApplicationDbContext())
                {
                    // Get the TilerUser from the job
                    var tilerUser = db.Users.Find(job.TilerUserId);
                    
                    if (tilerUser == null)
                    {
                        throw new InvalidOperationException($"TilerUser not found: {job.TilerUserId}");
                    }

                    // Initialize LogControl similar to PersonaController
                    var logControl = new TilerFront.LogControl(tilerUser, db);
                    
                    // Get the request location
                    TilerElements.Location requestLocation = requestData.getCurrentLocation();
                    
                    if (requestLocation == null || !requestLocation.isNotNullAndNotDefault)
                    {
                        requestLocation = TilerElements.Utility.defaultLLMLookupLocation.CreateCopy();
                        requestLocation.IsVerified = true;
                    }

                    // Call the actual SuggestionAnalysis method
                    await SuggestionAnalysis(logControl, requestLocation, db).ConfigureAwait(false);

                    // Update job status with success
                    string result = Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        status = "success",
                        jobId = job.Id,
                        type = "suggestion",
                        userId = job.TilerUserId,
                        completedAt = DateTimeOffset.UtcNow
                    });
                    
                    _jobQueue.UpdateJobStatus(job.Id, AnalysisJobStatus.Completed, resultData: result);
                    
                    System.Diagnostics.Trace.WriteLine($"Suggestion analysis completed for job {job.Id}, user {job.TilerUserId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error in ProcessSuggestionAnalysisAsync for job {job.Id}: {ex.Message}\n{ex.StackTrace}");
                throw; // Re-throw to be handled by ProcessJobAsync retry logic
            }
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
            Dictionary<string, CalendarEvent> allScheduleData = calEvents.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
            Dictionary<string, TilerElements.Location> locationCache = res.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
            await logControl.populateScheduleDump(locationCache, allScheduleData, requestLocation).ConfigureAwait(false);
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
        /// </summary>
        private async Task ProcessScheduleAnalysisAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            try
            {
                // Deserialize the request data
                var requestData = Newtonsoft.Json.JsonConvert.DeserializeObject<TilerFront.Models.LocationEnabledUserRequest>(job.RequestData);
                
                if (requestData == null)
                {
                    throw new ArgumentException("Invalid request data for schedule analysis job");
                }

                // Create database context for this job
                using (var db = new TilerFront.Models.ApplicationDbContext())
                {
                    // Get the TilerUser from the job
                    var tilerUser = await db.Users.Include(o=>o.ScheduleProfile_DB)
                        .FirstOrDefaultAsync(o=> o.Id == job.TilerUserId)
                        .ConfigureAwait(false);
                    
                    if (tilerUser == null)
                    {
                        throw new InvalidOperationException($"TilerUser not found: {job.TilerUserId}");
                    }

                    // Initialize LogControl
                    var logControl = new TilerFront.LogControlDirect(tilerUser, db);
                    
                    // Get the request location
                    TilerElements.Location requestLocation = requestData.getCurrentLocation();
                    
                    if (requestLocation == null || !requestLocation.isNotNullAndNotDefault)
                    {
                        requestLocation = TilerElements.Utility.defaultLLMLookupLocation.CreateCopy();
                        requestLocation.IsVerified = true;
                    }

                    // Call the SuggestionAnalysis method (it handles both suggestion and schedule analysis)
                    await SuggestionAnalysis(logControl, requestLocation, db).ConfigureAwait(false);

                    // Update job status with success
                    string result = Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        status = "success",
                        jobId = job.Id,
                        type = "scheduleAnalysis",
                        userId = job.TilerUserId,
                        completedAt = DateTimeOffset.UtcNow
                    });
                    
                    _jobQueue.UpdateJobStatus(job.Id, AnalysisJobStatus.Completed, resultData: result);
                    
                    System.Diagnostics.Trace.WriteLine($"Schedule analysis completed for job {job.Id}, user {job.TilerUserId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error in ProcessScheduleAnalysisAsync for job {job.Id}: {ex.Message}\n{ex.StackTrace}");
                throw; // Re-throw to be handled by ProcessJobAsync retry logic
            }
        }

        /// <summary>
        /// Processes a VibeRequest ProcessRequest job - generates preview by running ProcessRequest
        /// </summary>
        private async Task ProcessRequestPreviewAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            try
            {
                // Parse the request data - expecting a JSON with VibeRequestId and ExecuteVibeModel data
                var requestDataJson = Newtonsoft.Json.Linq.JObject.Parse(job.RequestData);
                string vibeRequestId = requestDataJson["VibeRequestId"]?.ToString();
                var executeVibeData = requestDataJson["ExecuteVibeData"]?.ToObject<TilerFront.Models.ExecuteVibeModel>();

                if (string.IsNullOrWhiteSpace(vibeRequestId))
                {
                    throw new ArgumentException("Invalid request data for ProcessRequest job: VibeRequestId is required");
                }

                ScheduleDump dump = null;
                // Create database context for this job
                using (var db = new TilerFront.Models.ApplicationDbContext())
                {
                    db.setAsReadOnly();
                    // Load the VibeRequest with all necessary includes
                    var vibeRequest = await db.VibeRequests
                        .Include(o => o.TilerUser)
                        .Include(o => o.TilerUser.VibeProfile_DB)
                        .Include(o => o.TilerUser.WorkHoursRestrictionProfile_DB)
                        .Include(o => o.TilerUser.PersonalHoursRestrictionProfile_DB)
                        .Include(o => o.VibeSession)
                        .Include(o => o.TilerUser.ScheduleProfile_DB)
                        .Where(o => o.Id == vibeRequestId && o.TilerUserId == job.TilerUserId)
                        .FirstOrDefaultAsync()
                        .ConfigureAwait(false);

                    if (vibeRequest == null)
                    {
                        throw new InvalidOperationException($"VibeRequest not found: {vibeRequestId} for user {job.TilerUserId}");
                    }

                    var tilerUser = vibeRequest.TilerUser;

                    // Load all actions for this VibeRequest
                    List<TilerElements.VibeAction> preEmittedActions = await db.VibeActions
                        .Where(o => o.VibeRequestId == vibeRequest.Id && o.TilerUser.IsAnonymous_DB == true)
                        .Include(o => o.VibeRequest.VibeSession.TilerUser)
                        .Include(o => o.VibeRequest.VibeSession.TilerUser.VibeProfile_DB)
                        .ToListAsync()
                        .ConfigureAwait(false);
                    
                    vibeRequest.Actions = preEmittedActions;

                    // Get timezone and location
                    var currentTime = executeVibeData != null ? executeVibeData.getRefNow() : DateTimeOffset.UtcNow;
                    if (executeVibeData != null && executeVibeData.TimeZone.isNot_NullEmptyOrWhiteSpace())
                    {
                        tilerUser.updateTimeZone(executeVibeData.TimeZone);
                    }

                    // Initialize ReferenceNow and UserAccount
                    TilerElements.ReferenceNow referenceNow = tilerUser.generateReferenceNow(currentTime);
                    string tilerUserId = tilerUser.Id;
                    TilerFront.UserAccount userAccount = new TilerFront.UserAccountDirect(
                        tilerUserId, 
                        db, 
                        new TilerFront.LogControlDirect(vibeRequest.TilerUser, db, 1));
                    
                    System.Collections.Generic.HashSet<string> calIds = new System.Collections.Generic.HashSet<string>();

                    // Process actions to extract calendar IDs and locations
                    System.Collections.Generic.List<TilerElements.Location> newlyCreatedLocations = new System.Collections.Generic.List<TilerElements.Location>();
                    foreach (var eachAction in preEmittedActions)
                    {
                        if (eachAction.FunctionResult != null)
                        {
                            var tilerIds = eachAction.FunctionResult.TilerIds;
                            if (tilerIds != null)
                            {
                                calIds = new System.Collections.Generic.HashSet<string>(calIds.Concat(tilerIds));
                            }
                        }

                        // Handle location updates
                        if (eachAction.FunctionResult != null ||
                            eachAction.Action == TilerElements.VibeQuery.VibeActionOptions.Update_Home_address ||
                            eachAction.Action == TilerElements.VibeQuery.VibeActionOptions.Update_Work_address)
                        {
                            string locationAddress = eachAction.FunctionResult?.LocationAddress?.Trim();
                            string locationAddressDescription = eachAction.FunctionResult?.LocationName?.Trim();

                            if (eachAction.Action == TilerElements.VibeQuery.VibeActionOptions.Update_Home_address)
                            {
                                locationAddress = eachAction.FunctionResult?.getByJsonKey("address")?.ToString().Trim();
                                locationAddressDescription = TilerElements.Location.HOMEDESCRIPTOR;
                            }
                            else if (eachAction.Action == TilerElements.VibeQuery.VibeActionOptions.Update_Work_address)
                            {
                                locationAddress = eachAction.FunctionResult?.getByJsonKey("address")?.ToString().Trim();
                                locationAddressDescription = TilerElements.Location.WORKDESCRIPTOR;
                            }

                            if (locationAddress.isNot_NullEmptyOrWhiteSpace() && locationAddressDescription.isNot_NullEmptyOrWhiteSpace())
                            {
                                var upsertResult = await LogControl.upsertLocation(
                                    db,
                                    tilerUser,
                                    locationAddressDescription,
                                    locationAddress
                                    )
                                    .ConfigureAwait(false);
                                if (upsertResult != null)
                                {
                                    newlyCreatedLocations.Add(upsertResult.Item1);
                                }
                            }
                        }
                    }

                    string beforeScheduleId = tilerUser.ScheduleProfile.EvaluationUpdateId;

                    // Create DB_Schedule
                    var dataRetrievalSet = TilerElements.DataRetrievalSet.scheduleManipulation;
                    TilerFront.DB_Schedule schedule = new TilerFront.DB_Schedule(
                        userAccount, 
                        referenceNow.constNow, 
                        tilerUser.EndOfDay,
                        TilerElements.Location.getDefaultLocation(), 
                        dataRetrievalSet, 
                        calendarIds: calIds);
                    
                    TilerElements.Location currentLocation = executeVibeData != null 
                        ? executeVibeData.getCurrentLocation() 
                        : null;
                    
                    foreach (var eachUpsertedLocation in newlyCreatedLocations)
                    {
                        await schedule.addLocation(eachUpsertedLocation).ConfigureAwait(false);
                    }
                    
                    if (currentLocation == null || !currentLocation.isNotNullAndNotDefault)
                    {
                        currentLocation = TilerElements.Utility.defaultLLMLookupLocation.CreateCopy();
                        currentLocation.IsVerified = true;
                    }
                    
                    schedule.SetCurrentLocation(currentLocation);

                    // Get default persona locations
                    var defaultPersonaHomeLocation = tilerUser.ScheduleProfile?.HomeLocation_DB;
                    var defaultPersonaWorkLocation = tilerUser.ScheduleProfile?.WorkLocation_DB;

                    if (defaultPersonaHomeLocation != null && !schedule.Locations.ContainsKey(defaultPersonaHomeLocation.Description.ToLower()))
                    {
                        schedule.Locations.Add(defaultPersonaHomeLocation.Description.ToLower(), defaultPersonaHomeLocation);
                    }
                    if (defaultPersonaWorkLocation != null && !schedule.Locations.ContainsKey(defaultPersonaWorkLocation.Description.ToLower()))
                    {
                        schedule.Locations.Add(defaultPersonaWorkLocation.Description.ToLower(), defaultPersonaWorkLocation);
                    }

                    // Create ScheduleService and process the request
                    VibeServiceCore.ScheduleService scheduleService = new VibeServiceCore.ScheduleService(vibeRequest.VibeSession, schedule);
                    await scheduleService.ProcessRequest(vibeRequest, schedule).ConfigureAwait(false);

                    string afterScheduleId = tilerUser.ScheduleProfile.EvaluationUpdateId;

                    // Update VibeRequest with before/after schedule IDs
                    vibeRequest.BeforeScheduleId = beforeScheduleId;
                    vibeRequest.AfterScheduleId = afterScheduleId;
                    
                    db.Users.AddOrUpdate(tilerUser);
                    dump = await schedule.CreateScheduleDump(currentLocation).ConfigureAwait(false);
                    var now = Utility.now();
                    if (dump?.XmlDoc != null)
                    {
                        var bigDataControl = new BigDataTiler.BigDataLogControl();
                        var log = new BigDataTiler.LogChange
                        {
                            UserId = job.TilerUserId,
                            TimeOfCreation = now,
                            JsTimeOfCreation = (ulong)now.ToUnixTimeMilliseconds(),
                            TypeOfEvent = (UserActivity.ActivityType.Preview).ToString()   
                        };
                        log.UpdateTrigger(BigDataTiler.TriggerType.preview);
                        log.Id = log.GenerateId();
                        log.loadXmlFile(dump.XmlDoc);
                        await bigDataControl.AddLogDocument(log).ConfigureAwait(false);

                        using (var writeDb = new TilerFront.Models.ApplicationDbContext())
                        {
                            var vibePreview = new TilerElements.VibePreview
                            {
                                VibeRequestId = vibeRequest.Id,
                                ScheduleDumpId = log.Id,
                                CreationTimeInMs = now.ToUnixTimeMilliseconds(),
                                TilerUserId = job.TilerUserId
                            };
                            vibePreview.Id = vibePreview.GenerateId();
                            writeDb.VibePreviews.Add(vibePreview);
                            await writeDb.SaveChangesAsync().ConfigureAwait(false);
                        }
                    }

                    // Update job status with success
                    string result = Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        status = "success",
                        jobId = job.Id,
                        type = "processRequest",
                        userId = job.TilerUserId,
                        vibeRequestId = vibeRequestId,
                        beforeScheduleId = beforeScheduleId,
                        afterScheduleId = afterScheduleId,
                        completedAt = now
                    });

                    _jobQueue.UpdateJobStatus(job.Id, AnalysisJobStatus.Completed, resultData: result);

                    System.Diagnostics.Trace.WriteLine($"ProcessRequest preview completed for job {job.Id}, VibeRequest {vibeRequestId}, user {job.TilerUserId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error in ProcessRequestPreviewAsync for job {job.Id}: {ex.Message}\n{ex.StackTrace}");
                throw; // Re-throw to be handled by ProcessJobAsync retry logic
            }
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
