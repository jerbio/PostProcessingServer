using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using TilerFront.Models;
using TilerFront.Services.TileShare;

namespace PostProcessingServer.Services
{
    public static class TileShareActivityCleanupWorker
    {
        private static int started;
        public static void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            HostingEnvironment.QueueBackgroundWorkItem(RunAsync);
        }

        private static async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    int removed = 0;
                    // Bound each pass so a backlog cannot monopolize the application host.
                    for (int batch = 0; batch < 20 && !token.IsCancellationRequested; batch++)
                    {
                        using (var context = new ApplicationDbContext())
                        {
                            int count = await TileShareActivityCleanup.PurgeBatchAsync(context, now, 500, token).ConfigureAwait(false);
                            removed += count;
                            if (count < 500) break;
                        }
                    }
                    if (removed > 0) Trace.TraceInformation("TileShare activity retention removed {0} expired rows.", removed);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception) { Trace.TraceError("TileShare activity retention cleanup failed; a later pass will retry."); }
                try { await Task.Delay(TimeSpan.FromHours(1), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
