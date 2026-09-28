using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Tests.Fakes;
using Xunit;

namespace PluginCoverShuffle.Tests.Playnite.Integration
{
    public class SerialBackgroundQueueTests
    {
        [Fact]
        public async Task Enqueue_RunsItemsInOrder_NeverConcurrently()
        {
            var queue = new SerialBackgroundQueue(new FakeCoverShuffleLogger());
            var order = new ConcurrentQueue<int>();
            var running = 0;
            var maxConcurrent = 0;

            var tasks = Enumerable.Range(0, 20).Select(i => queue.Enqueue("item " + i, () =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref maxConcurrent, now);
                Thread.Sleep(2);
                order.Enqueue(i);
                Interlocked.Decrement(ref running);
            })).ToList();

            await Task.WhenAll(tasks);

            Assert.Equal(1, maxConcurrent);
            Assert.Equal(Enumerable.Range(0, 20), order);
        }

        [Fact]
        public async Task Enqueue_AnItemThrowing_IsLoggedAndLaterItemsStillRun()
        {
            var logger = new FakeCoverShuffleLogger();
            var queue = new SerialBackgroundQueue(logger);
            var laterRan = false;

            queue.Enqueue("Startup checks", () => throw new InvalidOperationException("boom"));
            await queue.Enqueue("Launch shuffle", () => laterRan = true);

            Assert.True(laterRan);
            Assert.Contains(logger.ErrorMessages, m => m.Contains("Startup checks"));
        }

        [Fact]
        public async Task Enqueue_RunsOnTheThreadPool_NeverTheCallersUiThread()
        {
            // Playnite calls in from its UI thread, which is never a
            // thread-pool thread; running on the pool is what keeps startup
            // and "Play" responsive. (Comparing thread ids with the caller
            // doesn't work here: xUnit itself runs tests on pool threads.)
            var queue = new SerialBackgroundQueue(new FakeCoverShuffleLogger());
            var ranOnPool = false;

            await queue.Enqueue("work", () => ranOnPool = Thread.CurrentThread.IsThreadPoolThread);

            Assert.True(ranOnPool);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
