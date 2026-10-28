using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using PostProcessingServer.Utilities;
using TilerCrossServerResources;
using TilerElements;
using TilerFront.Services.CalendarSync;

namespace PostProcessingServer.Services
{
    public sealed class BackgroundJobWorker : IRegisteredObject
    {
        public static BackgroundJobWorker Instance { get; } = new BackgroundJobWorker();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object gate = new object();
        private Task running;

        private BackgroundJobWorker() { }

        public void Start()
        {
            lock (gate)
            {
                if (running != null || cancellation.IsCancellationRequested) return;
                var handlers = new List<IBackgroundJobHandler> {
                    AnalysisJobProcessor.Instance,
                    new TileShareScheduleJobHandler(), new TileShareAccessJobHandler()
                };
                try
                {
                    // Resolve environment settings at the host boundary. The handler factory
                    // accepts values directly, so a vault or secure store can replace this source.
                    //var settings = ConfigurationManager.AppSettings;
                    bool googleEnabled, maintenanceEnabled;
                    if (bool.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncGoogleEnabled"), out googleEnabled) && googleEnabled &&
                        bool.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncGoogleWatchMaintenanceEnabled"), out maintenanceEnabled) && maintenanceEnabled)
                    {
                        Uri callback;
                        if (!Uri.TryCreate(getCalendarSyncGoogleCallbackUrl(), UriKind.Absolute, out callback))
                            throw new GoogleCalendarCredentialException(GoogleCalendarCredentialFailure.ConfigurationRequired);
                        handlers.Add(GoogleCalendarPushRuntime.CreateMaintenanceHandler(
                            () => new TilerFront.Models.ApplicationDbContext(), ConfigurationHelper.getGoogleClientId(), ConfigurationHelper.getGoogleClientSecret(), callback));
                    }
                }
                catch (Exception error)
                {
                    // A disabled or misconfigured integration must not stop unrelated jobs.
                    Trace.TraceError("calendarsync.watch.startup.disabled ExceptionType={0}", error.GetType().Name);
                }
                try
                {
                    var settings = ConfigurationManager.AppSettings;
                    bool notificationsEnabled, processingEnabled, googleEnabled;
                    var deliver = bool.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncNotificationsEnabled"), out notificationsEnabled) && notificationsEnabled;
                    var process = bool.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncGoogleEnabled"), out googleEnabled) && googleEnabled &&
                        bool.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncProcessingEnabled"), out processingEnabled) && processingEnabled;
                    // Delivery can drain committed notifications even after Google intake/calculation is disabled.
                    // Do not start new calculations unless the notification transport is configured too.
                    if (deliver)
                    {
                        Uri endpoint;
                        if (!Uri.TryCreate((settings[ConfigurationUtility.TilerFrontBaseUrlKey] ?? "").TrimEnd('/') +
                            "/api/internal/jobs/calendar-sync", UriKind.Absolute, out endpoint))
                            throw new InvalidOperationException("Calendar notification configuration is required.");
                        var sender = new CalendarSyncNotificationSender(endpoint, settings[ConfigurationUtility.InternalJobApiKeyKey]);
                        var store = new CalendarSyncStore(() => new TilerFront.Models.ApplicationDbContext());
                        handlers.Add(new CalendarSyncNotificationWorker(store, sender.SendAsync));
                        if (process)
                        {
                            int recoveryIntervalMinutes;
                            if (!int.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncGoogleRecoveryIntervalMinutes"),
                                System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
                                out recoveryIntervalMinutes) || recoveryIntervalMinutes <= 0)
                                recoveryIntervalMinutes = 360;
                            var recoveryInterval = TimeSpan.FromMinutes(recoveryIntervalMinutes);
                            handlers.Add(new CalendarSyncWorker(store, GoogleCalendarPushRuntime.CreateNativeSyncHandler(
                                () => new TilerFront.Models.ApplicationDbContext(), ConfigurationHelper.getGoogleClientId(), ConfigurationHelper.getGoogleClientSecret())));
                            bool recoveryEnabled;
                            if (bool.TryParse(ConfigurationHelper.GetConfigValue("calendarSyncGoogleRecoveryEnabled"), out recoveryEnabled) && recoveryEnabled)
                            {
                                handlers.Add(GoogleCalendarPushRuntime.CreateRecoveryHandler(
                                    () => new TilerFront.Models.ApplicationDbContext(), recoveryInterval));
                            }
                        }
                    }
                    else if (process) throw new InvalidOperationException("Calendar notification delivery must be enabled before calculation.");
                }
                catch (Exception error)
                {
                    Trace.TraceError("calendarsync.processing.startup.disabled ExceptionType={0}", error.GetType().Name);
                }
                var dispatcher = new BackgroundJobDispatcher(handlers, Math.Max(1, ConfigurationUtility.GetInt(ConfigurationUtility.MaxConcurrentJobsKey, ConfigurationUtility.DefaultMaxConcurrentJobs)),
                    TimeSpan.FromMilliseconds(Math.Max(100, ConfigurationUtility.GetInt(ConfigurationUtility.JobPollIntervalMsKey, ConfigurationUtility.DefaultJobPollIntervalMs))));
                HostingEnvironment.RegisterObject(this);
                running = RunAsync(dispatcher);
            }
        }


        public string getCalendarSyncGoogleCallbackUrl()
        {
            string tilerFrontDomain = ConfigurationHelper.getTilerDomain();
            return (tilerFrontDomain ?? "") + "/api/GoogleNotification/Trigger";
        }
        private async Task RunAsync(BackgroundJobDispatcher dispatcher)
        {
            try { await dispatcher.RunAsync(cancellation.Token).ConfigureAwait(false); }
            finally { HostingEnvironment.UnregisterObject(this); }
        }

        public void Stop(bool immediate)
        {
            Task drain;
            lock (gate) { cancellation.Cancel(); drain = running; }
            if (!immediate && drain != null)
            {
                try { drain.Wait(TimeSpan.FromSeconds(30)); }
                catch (AggregateException error) { Trace.TraceError("jobs.shutdown.failed ExceptionType={0}", error.GetBaseException().GetType().Name); }
            }
            // Stay registered until the tracked tasks exit. Interrupted jobs retain
            // their durable records/leases for recovery on the next PPS startup.
        }
    }
}
