using System;
using System.Threading;
using System.Threading.Tasks;
using PluginCoverShuffle.Infrastructure.Logging;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Runs background work items one at a time, in the order they were
    /// queued, off the UI thread. The startup shuffle pass and game-launch
    /// shuffles both go through one queue so they can never run at once:
    /// a shuffle reads the cycle state, picks a cover, then writes the state
    /// back, and two of those interleaving for the same game would
    /// double-advance its cycle.
    ///
    /// Serializing here, rather than locking inside the shuffle itself, is
    /// deliberate: a shuffle ends by updating Playnite's game database, which
    /// can synchronously wait on the UI thread, so holding a lock across it
    /// could deadlock against a UI-thread action waiting on the same lock.
    /// </summary>
    public sealed class SerialBackgroundQueue
    {
        private readonly object _gate = new object();
        private readonly ICoverShuffleLogger _logger;
        private Task _tail = Task.FromResult(0);

        public SerialBackgroundQueue(ICoverShuffleLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Queues <paramref name="work"/> behind everything already queued.
        /// An exception is logged and never stops later items from running.
        /// </summary>
        /// <returns>A task that completes once this item has run; mainly for tests.</returns>
        public Task Enqueue(string description, Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            lock (_gate)
            {
                _tail = _tail.ContinueWith(
                    _ =>
                    {
                        try
                        {
                            work();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"{description} failed unexpectedly.");
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                return _tail;
            }
        }
    }
}
