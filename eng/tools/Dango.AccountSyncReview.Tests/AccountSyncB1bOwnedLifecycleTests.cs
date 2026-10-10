using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1bLifecycle")]
public sealed class AccountSyncB1bOwnedLifecycleTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromMilliseconds(60);

    [TestMethod]
    public async Task IgnoringCancellationDoesNotLoseOwnershipAndLateSuccessCanBeDrained()
    {
        var tracker = new AccountSyncB1bOwnedLifecycle(Bound);
        var actual = Signal(); var calls = 0;
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => tracker.StartAsync(_ => { calls++; return actual.Task; }));
        Assert.IsFalse(tracker.Snapshot().Single().Settled); Assert.IsFalse(tracker.TasksSettledSuccessfully);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => tracker.DrainAsync());
        actual.SetResult();
        await tracker.StartAsync(_ => { calls++; return Task.CompletedTask; });
        await tracker.StopAsync(_ => Task.CompletedTask); await tracker.DisposeAsync(() => Task.CompletedTask); await tracker.DrainAsync();
        Assert.AreEqual(1, calls); Assert.IsTrue(tracker.TasksSettledSuccessfully);
    }

    [TestMethod]
    public async Task LateFaultIsObservedAndCanNeverBecomeSuccessfulCleanup()
    {
        var tracker = new AccountSyncB1bOwnedLifecycle(Bound); var actual = Signal();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => tracker.StartAsync(_ => actual.Task));
        actual.SetException(new IOException("fictional private message is not projected"));
        await Assert.ThrowsExactlyAsync<IOException>(() => tracker.DrainAsync());
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (!tracker.Snapshot().Single().FaultObserved && DateTime.UtcNow < deadline) await Task.Delay(1);
        var projection = tracker.Snapshot().Single();
        Assert.IsTrue(projection.Settled); Assert.IsTrue(projection.FaultObserved);
        Assert.IsFalse(projection.Successful); Assert.IsFalse(tracker.TasksSettledSuccessfully);
    }

    [TestMethod]
    public async Task SynchronousThrowIsRetainedAsAFaultedActualTask()
    {
        var tracker = new AccountSyncB1bOwnedLifecycle(Bound);
        await Assert.ThrowsExactlyAsync<IOException>(() => tracker.StartAsync(_ => throw new IOException("fictional")));
        Assert.IsTrue(tracker.Snapshot().Single().Settled); Assert.IsTrue(tracker.Snapshot().Single().FaultObserved);
        Assert.IsFalse(tracker.IsSuccessful("Start"));
    }

    [TestMethod]
    public async Task RepeatedDisposeWaitsForTheSameActualOperationAndNeverInvokesItTwice()
    {
        var tracker = new AccountSyncB1bOwnedLifecycle(Bound); var actual = Signal(); var calls = 0;
        var first = tracker.DisposeAsync(() => { calls++; return actual.Task; });
        var second = tracker.DisposeAsync(() => { calls++; return Task.CompletedTask; });
        Assert.IsFalse(first.IsCompleted); Assert.IsFalse(second.IsCompleted); Assert.AreEqual(1, calls);
        actual.SetResult(); await Task.WhenAll(first, second); await tracker.DrainAsync();
        Assert.AreEqual(1, calls); Assert.IsTrue(tracker.IsSuccessful("Dispose"));
    }

    [TestMethod]
    public async Task MissingOrFailedLifecyclePhaseCannotBeReportedAsSettledSuccessfully()
    {
        var tracker = new AccountSyncB1bOwnedLifecycle(Bound);
        await tracker.StartAsync(_ => Task.CompletedTask); Assert.IsFalse(tracker.TasksSettledSuccessfully);
        await tracker.StopAsync(_ => Task.CompletedTask); Assert.IsFalse(tracker.TasksSettledSuccessfully);
        await Assert.ThrowsExactlyAsync<IOException>(() => tracker.DisposeAsync(() => Task.FromException(new IOException("fictional"))));
        Assert.IsFalse(tracker.TasksSettledSuccessfully);
    }

    [TestMethod]
    public async Task CleanupAttemptsEveryIndependentStepAfterBothSyncAndAsyncFailures()
    {
        var called = new List<string>();
        var report = await AccountSyncB1bCleanup.RunAsync(
            ("Faults", () => { called.Add("Faults"); throw new IOException("must not enter evidence"); }),
            ("PrimaryPump", () => { called.Add("PrimaryPump"); return Task.FromException(new InvalidOperationException("must not enter evidence")); }),
            ("SecondaryPump", () => { called.Add("SecondaryPump"); return Task.CompletedTask; }),
            ("DispatcherBarrier", () => { called.Add("DispatcherBarrier"); return Task.CompletedTask; }));
        CollectionAssert.AreEqual(new[] { "Faults", "PrimaryPump", "SecondaryPump", "DispatcherBarrier" }, called);
        Assert.IsFalse(report.Passed); Assert.AreEqual(2, report.Failures.Count);
        Assert.AreEqual("IOException", report.Failures[0].FailureType);
        Assert.AreEqual("InvalidOperationException", report.Failures[1].FailureType);
        Assert.ThrowsExactly<InvalidOperationException>(report.RequireSuccess);
    }

    [TestMethod]
    public async Task TimedOutOwnedOperationDoesNotSkipLaterIndependentCleanup()
    {
        var tracker = new AccountSyncB1bOwnedLifecycle(Bound); var actual = Signal(); var laterRan = false;
        var report = await AccountSyncB1bCleanup.RunAsync(
            ("Stop", () => tracker.StopAsync(_ => actual.Task)),
            ("DispatcherBarrier", () => { laterRan = true; return Task.CompletedTask; }));
        Assert.IsFalse(report.Passed); Assert.IsTrue(laterRan); Assert.IsFalse(tracker.Snapshot().Single().Settled);
        actual.SetResult(); await tracker.DrainAsync();
    }

    [TestMethod]
    public void LifecycleBoundsCannotBeZeroNegativeOrWiderThanTheApprovedTwentySeconds()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new AccountSyncB1bOwnedLifecycle(TimeSpan.Zero));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AccountSyncB1bOwnedLifecycle(TimeSpan.FromSeconds(-1)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AccountSyncB1bOwnedLifecycle(TimeSpan.FromSeconds(21)));
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
