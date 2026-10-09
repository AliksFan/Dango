using System.Threading.Channels;
using Dango.Server.Rooms.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomExpiry;

[TestClass]
public sealed class RoomExpiryWorkerTests
{
    [TestMethod]
    public async Task StartupWaitsOneMinuteAndStoppingDuringThatWaitCreatesNoScope()
    {
        var clock = new ManualClock();
        var scopes = new BlockScopeFactory();
        using var worker = new RoomExpiryWorker(scopes, clock: clock);
        await worker.StartAsync(CancellationToken.None);
        var timer = await clock.NextTimerAsync();
        Assert.AreEqual(TimeSpan.FromMinutes(1), timer.DueTime);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, timer.Period);
        Assert.AreEqual(0, scopes.Created);
        await worker.StopAsync(CancellationToken.None);
        Assert.AreEqual(0, scopes.Created);
        Assert.IsTrue(timer.IsDisposed);
    }

    [TestMethod]
    public async Task IntervalAllowsOneScopeAttemptAndFailureWaitsAnotherMinute()
    {
        var clock = new ManualClock();
        var scopes = new BlockScopeFactory();
        using var worker = new RoomExpiryWorker(scopes, clock: clock);
        await worker.StartAsync(CancellationToken.None);
        var first = await clock.NextTimerAsync();
        Assert.AreEqual(0, scopes.Created);
        first.Fire();
        var next = await clock.NextTimerAsync();
        Assert.AreEqual(1, scopes.Created);
        Assert.AreEqual(TimeSpan.FromMinutes(1), next.DueTime);
        await worker.StopAsync(CancellationToken.None);
        Assert.AreEqual(1, scopes.Created);
        Assert.IsTrue(next.IsDisposed);
    }

    // A scope attempt is observable, but can never resolve a DbContext or an expiry service.
    private sealed class BlockScopeFactory : IServiceScopeFactory
    {
        private int created;
        public int Created => Volatile.Read(ref created);
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref created);
            throw new InvalidOperationException("test_scope_blocked");
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly Channel<ManualTimer> timers = Channel.CreateUnbounded<ManualTimer>();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime, period);
            Assert.IsTrue(timers.Writer.TryWrite(timer));
            return timer;
        }
        public Task<ManualTimer> NextTimerAsync() => timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        private int disposed;
        public TimeSpan DueTime { get; private set; } = dueTime;
        public TimeSpan Period { get; private set; } = period;
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;
        public bool Change(TimeSpan nextDueTime, TimeSpan nextPeriod)
        {
            if (IsDisposed) return false;
            DueTime = nextDueTime;
            Period = nextPeriod;
            return true;
        }
        public void Fire()
        {
            if (!IsDisposed) callback(state);
        }
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
