using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using TilerFront.Models;
using TilerFront.DatabaseModels;
using TilerFront.Services.TileShare;

namespace PostProcessingServer.Services
{
    public static class TileShareScheduleCleanupWorker
    {
        private static int started;
        public static void Start()
        {
            if (!TileShareScheduleOperations.Enabled || Interlocked.Exchange(ref started, 1) != 0) return;
            HostingEnvironment.QueueBackgroundWorkItem(RunAsync);
        }

        private static async Task RunAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using (var cleanup = new ApplicationDbContext())
                    {
                        var now = DateTimeOffset.UtcNow;
                        var retention = now.AddDays(-7);
                        var expired = await cleanup.TileShareScheduleRequests.Where(r =>
                            r.State == TileShareScheduleRequestState.Preview && r.ExpiresAt < now
                            || (r.State == TileShareScheduleRequestState.Completed || r.State == TileShareScheduleRequestState.Rejected
                                || r.State == TileShareScheduleRequestState.NeedsReview) && r.CreatedAt < retention)
                            .OrderBy(r => r.CreatedAt).Take(100).ToListAsync(cancellation).ConfigureAwait(false);
                        cleanup.TileShareScheduleRequests.RemoveRange(expired);
                        await cleanup.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception) { Trace.TraceError("TileShare schedule request cleanup failed."); }
                try { await Task.Delay(TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

    }
}
