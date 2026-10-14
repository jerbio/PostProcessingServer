using System.Threading;
using System.Threading.Tasks;
using ClusterManager;
using TilerFront.Models;
using VibeServiceCore;

namespace PostProcessingServer.Services
{
    // Adapter only. All job types use BackgroundJobWorker for hosting and dispatch.
    public sealed class TileShareScheduleJobHandler : IBackgroundJobHandler
    {
        private readonly VibeTileShareScheduleJobProcessor processor;
        private readonly ITileShareSchedulePlanner planner;
        public string Name => "TileShareScheduleUpdate";

        public TileShareScheduleJobHandler() : this(
            new VibeTileShareScheduleJobProcessor(() => new ApplicationDbContext()), new TileShareScheduleUpdatePlanner()) { }

        public TileShareScheduleJobHandler(VibeTileShareScheduleJobProcessor processor, ITileShareSchedulePlanner planner)
        {
            this.processor = processor ?? throw new System.ArgumentNullException(nameof(processor));
            this.planner = planner ?? throw new System.ArgumentNullException(nameof(planner));
        }

        public Task<bool> TryProcessNextAsync(CancellationToken cancellation)
            => processor.ProcessNextScheduleUpdateAsync(planner, cancellation);
    }
}
