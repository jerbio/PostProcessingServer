using System.Threading;
using System.Threading.Tasks;
using ClusterManager;
using TilerCrossServerResources;
using TilerFront.Models;

namespace PostProcessingServer.Services
{
    public sealed class TileShareAccessJobHandler : IBackgroundJobHandler
    {
        private readonly TileShareAccessJobProcessor processor;
        public string Name => "TileShareAccessReconciliation";
        public TileShareAccessJobHandler() : this(new TileShareAccessJobProcessor(() => new ApplicationDbContext())) { }
        public TileShareAccessJobHandler(TileShareAccessJobProcessor processor) { this.processor = processor; }
        public Task<bool> TryProcessNextAsync(CancellationToken cancellation) => processor.ProcessNextAsync(cancellation);
    }
}
