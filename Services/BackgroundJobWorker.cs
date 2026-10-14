using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using PostProcessingServer.Utilities;

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
                var dispatcher = new BackgroundJobDispatcher(new IBackgroundJobHandler[] {
                    AnalysisJobProcessor.Instance, new TileShareScheduleJobHandler(), new TileShareAccessJobHandler()
                }, Math.Max(1, ConfigurationUtility.GetInt(ConfigurationUtility.MaxConcurrentJobsKey, ConfigurationUtility.DefaultMaxConcurrentJobs)),
                    TimeSpan.FromMilliseconds(Math.Max(100, ConfigurationUtility.GetInt(ConfigurationUtility.JobPollIntervalMsKey, ConfigurationUtility.DefaultJobPollIntervalMs))));
                HostingEnvironment.RegisterObject(this);
                running = RunAsync(dispatcher);
            }
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
