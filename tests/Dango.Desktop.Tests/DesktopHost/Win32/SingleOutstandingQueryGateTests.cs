using Dango.Desktop.DesktopHost.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Win32;

[TestClass]
public sealed class SingleOutstandingQueryGateTests
{
    [TestMethod]
    public void CallerTimeoutKeepsTheReceiverResourceAndSlotUntilCompletion()
    {
        using var receiver = new DelayedReceiver();
        using var gate = new SingleOutstandingQueryGate<int, int>(receiver.Query);
        Assert.IsFalse(gate.TryQuery(17, 1, out _));
        Assert.IsTrue(receiver.Started.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(receiver.ResourceAlive);
        Assert.AreEqual(0, receiver.CleanupCount);
        Assert.IsTrue(gate.IsBusy, "Caller timeout must not reopen the slot while the receiver owns its resource.");
        receiver.Release.Set();
        Assert.IsTrue(SpinWait.SpinUntil(() => !gate.IsBusy, TimeSpan.FromSeconds(5)));
        Assert.IsFalse(receiver.ResourceAlive);
        Assert.AreEqual(1, receiver.CleanupCount);
    }

    [TestMethod]
    public void BusyRequestsCannotStartAnotherReceiverOrReuseTheTimedOutResult()
    {
        using var receiver = new DelayedReceiver();
        using var gate = new SingleOutstandingQueryGate<int, int>(receiver.Query);
        Assert.IsFalse(gate.TryQuery(17, 1, out _));
        Assert.IsTrue(receiver.Started.Wait(TimeSpan.FromSeconds(5)));
        for (var index = 0; index < 3; index++)
        {
            Assert.IsFalse(gate.TryQuery(23, 10, out _));
        }

        Assert.AreEqual(1, receiver.QueryCount, "A busy slot must reject work before task/resource creation.");
        receiver.Release.Set();
        Assert.IsTrue(SpinWait.SpinUntil(() => !gate.IsBusy, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, receiver.CleanupCount);
        Assert.IsTrue(gate.TryQuery(29, 5000, out var current));
        Assert.AreEqual(29, current, "The late result 17 belongs only to the caller that timed out.");
        Assert.AreEqual(2, receiver.QueryCount);
        Assert.AreEqual(2, receiver.CleanupCount);
    }

    [TestMethod]
    public void DisposeReturnsWhileReceiverOwnsResourceAndNeverReopensTheGate()
    {
        using var receiver = new DelayedReceiver();
        using var gate = new SingleOutstandingQueryGate<int, int>(receiver.Query);
        Assert.IsFalse(gate.TryQuery(17, 1, out _));
        Assert.IsTrue(receiver.Started.Wait(TimeSpan.FromSeconds(5)));
        var disposed = Task.Run(gate.Dispose);
        Assert.IsTrue(disposed.Wait(TimeSpan.FromSeconds(1)), "Dispose must not wait for a blocked receiver.");
        Assert.IsTrue(gate.IsBusy);
        Assert.IsTrue(receiver.ResourceAlive);
        Assert.AreEqual(0, receiver.CleanupCount);
        Assert.IsFalse(gate.TryQuery(23, 5000, out _));
        receiver.Release.Set();
        Assert.IsTrue(SpinWait.SpinUntil(() => !gate.IsBusy, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, receiver.CleanupCount);
        Assert.IsFalse(gate.TryQuery(29, 5000, out _));
        Assert.AreEqual(1, receiver.QueryCount);
    }

    [TestMethod]
    public void SlotRemainsBusyDuringTheOperationsCleanup()
    {
        SingleOutstandingQueryGate<int, int>? gate = null;
        var busyDuringCleanup = false;
        gate = new(request =>
        {
            try { return new(true, request); }
            finally { busyDuringCleanup = gate!.IsBusy; }
        });
        using (gate)
        {
            Assert.IsTrue(gate.TryQuery(31, 5000, out var result));
            Assert.AreEqual(31, result);
            Assert.IsTrue(busyDuringCleanup);
            Assert.IsFalse(gate.IsBusy);
        }
    }

    [TestMethod]
    public void WorkerExceptionIsUnknownAndReleasesItsResourceAndSlot()
    {
        var count = 0;
        var cleanupCount = 0;
        using var gate = new SingleOutstandingQueryGate<int, int>(request =>
        {
            try
            {
                if (Interlocked.Increment(ref count) == 1) throw new InvalidOperationException("Controlled failure.");
                return new(true, request);
            }
            finally { Interlocked.Increment(ref cleanupCount); }
        });
        Assert.IsFalse(gate.TryQuery(37, 5000, out _));
        Assert.IsFalse(gate.IsBusy);
        Assert.AreEqual(1, cleanupCount);
        Assert.IsTrue(gate.TryQuery(41, 5000, out var result));
        Assert.AreEqual(41, result);
        Assert.AreEqual(2, cleanupCount);
    }

    [TestMethod]
    public void ExpiredBudgetRejectsBeforeStartingAnOperation()
    {
        var count = 0;
        using var gate = new SingleOutstandingQueryGate<int, int>(request =>
        {
            Interlocked.Increment(ref count);
            return new(true, request);
        });
        Assert.IsFalse(gate.TryQuery(43, 0, out _));
        Assert.IsFalse(gate.IsBusy);
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void KnownZeroIsSuccessfulAndUnknownResultIsRejected()
    {
        using var gate = new SingleOutstandingQueryGate<bool, nint>(known => new(known, 0));
        Assert.IsTrue(gate.TryQuery(true, 5000, out var result));
        Assert.AreEqual(nint.Zero, result, "List-view item index zero is a valid hit result.");
        Assert.IsFalse(gate.TryQuery(false, 5000, out _));
    }

    private sealed class DelayedReceiver : IDisposable
    {
        private int queryCount;
        private int cleanupCount;
        private int resources;
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int QueryCount => Volatile.Read(ref queryCount);
        public int CleanupCount => Volatile.Read(ref cleanupCount);
        public bool ResourceAlive => Volatile.Read(ref resources) != 0;

        public OutstandingQueryResult<int> Query(int value)
        {
            Interlocked.Increment(ref queryCount);
            Interlocked.Increment(ref resources);
            try
            {
                Started.Set();
                return Release.Wait(TimeSpan.FromSeconds(10)) ? new(true, value) : default;
            }
            finally
            {
                Interlocked.Decrement(ref resources);
                Interlocked.Increment(ref cleanupCount);
            }
        }

        public void Dispose()
        {
            Release.Set();
            if (!SpinWait.SpinUntil(() => QueryCount == CleanupCount, TimeSpan.FromSeconds(5))) return;
            Started.Dispose();
            Release.Dispose();
        }
    }
}
