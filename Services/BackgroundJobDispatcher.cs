using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PostProcessingServer.Services
{
    public interface IBackgroundJobHandler
    {
        string Name { get; }
        // Nonblocking when idle. A claimed job must finish or leave durable recovery
        // state before returning. Never launch untracked work from a handler.
        Task<bool> TryProcessNextAsync(CancellationToken cancellation);
    }

    // Shared by all registered job types: fair dispatch, bounded concurrency,
    // polling, error isolation and draining. Persistence stays behind each handler
    // so transactional jobs can commit their receipt with their domain writes.
    public sealed class BackgroundJobDispatcher
    {
        private readonly IBackgroundJobHandler[] handlers;
        private readonly int concurrency;
        private readonly TimeSpan pollInterval;
        private readonly Action<string, Exception> onError;
        private long nextHandler = -1;
        private readonly long[] retryAfter;

        public BackgroundJobDispatcher(IEnumerable<IBackgroundJobHandler> handlers, int concurrency,
            TimeSpan pollInterval, Action<string, Exception> onError = null)
        {
            this.handlers = handlers?.ToArray() ?? throw new ArgumentNullException(nameof(handlers));
            if (this.handlers.Length == 0 || this.handlers.Any(h => h == null)) throw new ArgumentException("Provide job handlers.", nameof(handlers));
            if (concurrency < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
            if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
            retryAfter = new long[this.handlers.Length];
            this.concurrency = concurrency;
            this.pollInterval = pollInterval;
            this.onError = onError ?? ((name, error) => System.Diagnostics.Trace.TraceError(
                "jobs.dispatch.failed Handler={0} ExceptionType={1}", name, error.GetType().Name));
        }

        public Task RunAsync(CancellationToken cancellation)
            => Task.WhenAll(Enumerable.Range(0, concurrency).Select(slot => Task.Run(() => RunSlotAsync(cancellation))));

        private async Task RunSlotAsync(CancellationToken cancellation)
        {
            int idle = 0;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var index = (int)((ulong)Interlocked.Increment(ref nextHandler) % (ulong)handlers.Length);
                    var handler = handlers[index];
                    bool processed = false;
                    try
                    {
                        if (DateTime.UtcNow.Ticks >= Interlocked.Read(ref retryAfter[index]))
                            processed = await handler.TryProcessNextAsync(cancellation).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        Interlocked.Exchange(ref retryAfter[index], DateTime.UtcNow.Add(pollInterval).Ticks);
                        onError(handler.Name, error);
                    }
                    if (processed) idle = 0;
                    else idle++;
                    if (idle >= handlers.Length)
                    {
                        idle = 0;
                        await Task.Delay(pollInterval, cancellation).ConfigureAwait(false);
                    }
                    else await Task.Yield();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }
}
