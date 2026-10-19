using System.Collections.Concurrent;
using Dango.PhysicsLab;

namespace Dango.Desktop.Tests.PhysicsLab;

[TestClass]
public sealed class LabBeltPhysicsWorkerTests
{
    private static readonly LabInputSnapshot HeldJump = new(1, 1, 1, true, true, true, false);
    private static readonly LabBeltInput HeldDrag = new(Horizontal: 1, Stretch: 1, Downward: 1, JumpPressed: true,
        DragActive: true, DragTargetX: 12, DragTargetY: -7, InputActive: true);

    [TestMethod]
    public void CreationCapabilitiesStepReadResetAndDisposeHaveOneDedicatedOwner()
    {
        using var rig = new Rig();
        var caller = Environment.CurrentManagedThreadId;
        var first = rig.Configure();
        Complete(rig.Worker.AdvanceAsync());
        Complete(rig.Worker.ReadStateAsync());
        var second = rig.Configure();
        Complete(rig.Worker.AdvanceAsync());
        Complete(rig.Worker.CloseAsync());
        Complete(rig.Worker.CloseAsync());
        var operations = first.Operations.Concat(second.Operations).ToArray();
        Assert.IsTrue(operations.Any(x => x.Name == "create"));
        Assert.IsTrue(operations.Any(x => x.Name == "capabilities"));
        Assert.IsTrue(operations.Any(x => x.Name == "step"));
        Assert.IsTrue(operations.Any(x => x.Name == "read"));
        Assert.AreEqual(1, operations.Select(x => x.Thread).Distinct().Count());
        Assert.AreNotEqual(caller, operations[0].Thread);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.DisposeCount);
    }

    [TestMethod]
    [DataRow(0d, 0d, 17)]
    [DataRow(0d, .012d, 5)]
    [DataRow(0d, .020d, 0)]
    [DataRow(.010d, .005d, 2)]
    [DataRow(.010d, .012d, 0)]
    public void SchedulerWaitSubtractsBothAccumulatedTimeAndCurrentNativeWork(double accumulatedSeconds, double workSeconds, int expectedMilliseconds)
    {
        Assert.AreEqual(expectedMilliseconds, LabBeltPhysicsWorker.SchedulerWaitMilliseconds(accumulatedSeconds, workSeconds));
    }

    [TestMethod]
    public void SchedulerNeverAddsAMinimumSleepAtOrBeyondTheFixedTickDeadline()
    {
        Assert.AreEqual(0, LabBeltPhysicsWorker.SchedulerWaitMilliseconds(0, LabWindow.FixedDt));
        Assert.AreEqual(0, LabBeltPhysicsWorker.SchedulerWaitMilliseconds(LabWindow.FixedDt, 0));
        Assert.AreEqual(0, LabBeltPhysicsWorker.SchedulerWaitMilliseconds(LabWindow.FixedDt / 2, LabWindow.FixedDt / 2));
        Assert.AreEqual(1, LabBeltPhysicsWorker.SchedulerWaitMilliseconds(LabWindow.FixedDt - .0004, 0), "A positive sub-millisecond remainder must round up, not become a busy loop.");
    }

    [TestMethod]
    public void ClearSessionInvalidatesQueuedFramesDisposesOnOwnerAndCannotResumeOrStep()
    {
        using var rig = new Rig(); var session = rig.Configure();
        var oldEpoch = rig.Worker.Epoch;
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 1, 0, 1);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.AreEqual(1, rig.Worker.Metrics.QueueDepth);
        var clearedEpoch = Complete(rig.Worker.ClearSessionAsync());
        Assert.IsTrue(clearedEpoch > oldEpoch);
        Assert.AreEqual(clearedEpoch, rig.Worker.Epoch);
        Assert.IsNull(rig.Worker.Capabilities);
        Assert.IsFalse(rig.Worker.Metrics.Running);
        Assert.AreEqual(0, rig.Worker.Metrics.QueueDepth);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
        Assert.AreEqual(1, session.DisposeCount);
        Assert.AreEqual(session.Operations.Single(x => x.Name == "create").Thread, session.Operations.Single(x => x.Name == "dispose").Thread);
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 2, 0, 2);
        Assert.IsFalse(Complete(rig.Worker.ResumeAsync()));
        Assert.IsFalse(Complete(rig.Worker.AdvanceAsync()));
        Assert.AreEqual(1, session.Operations.Count(x => x.Name == "step"), "A detached world must not receive another native Step.");
        Complete(rig.Worker.CloseAsync());
        Assert.AreEqual(1, session.DisposeCount, "Closing after clear must not dispose the detached world twice.");
    }

    [TestMethod]
    public void ExplicitConfigureAfterClearRestoresAFreshSessionWithoutOldInput()
    {
        using var rig = new Rig(); var old = rig.Configure();
        var oldEpoch = rig.Worker.Epoch;
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 1, 0, 1);
        Complete(rig.Worker.ClearSessionAsync());
        var current = rig.Configure();
        Assert.IsNotNull(rig.Worker.Capabilities);
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 2, 0, 2);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var fresh = rig.Drain().Single();
        Assert.AreEqual(1, fresh.Tick);
        Assert.AreEqual(rig.Worker.Epoch, fresh.Epoch);
        AssertCleared(fresh);
        Assert.IsFalse(fresh.ActualInput.JumpPressed);
        Assert.IsFalse(fresh.Input.InteractPressed);
        Assert.AreEqual(0, old.Operations.Count(x => x.Name == "step"));
        Assert.AreEqual(1, current.Operations.Count(x => x.Name == "step"));
        Assert.AreEqual(1, old.DisposeCount);
    }

    [TestMethod]
    public void ClearDuringBlockedNativeCallWaitsForOwnerAndRejectsItsOldSnapshot()
    {
        using var rig = new Rig(); var session = rig.Configure();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        session.BeforeStep = () => { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); };
        var step = rig.Worker.AdvanceAsync();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        var clear = rig.Worker.ClearSessionAsync();
        Assert.IsFalse(clear.IsCompleted, "Clear must not release a native handle while its owner is still using it.");
        Assert.AreEqual(0, session.DisposeCount);
        release.Set();
        Assert.IsFalse(Complete(step));
        Complete(clear);
        Assert.IsNull(rig.Worker.Capabilities);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
        Assert.AreEqual(1, session.DisposeCount);
        Assert.AreEqual(session.Operations.Single(x => x.Name == "step").Thread, session.Operations.Single(x => x.Name == "dispose").Thread);
        Assert.IsFalse(Complete(rig.Worker.AdvanceAsync()));
    }

    [TestMethod]
    public void RepeatedLatestSnapshotAndCatchupTicksConsumeEachSpaceSequenceOnce()
    {
        using var rig = new Rig(); rig.Configure();
        var epoch = rig.Worker.Epoch;
        for (var i = 0; i < 4; i++) rig.Worker.PublishInput(epoch, HeldJump, HeldDrag, 1, 0, 1);
        for (var i = 0; i < 4; i++) Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var frames = rig.Drain();
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, frames.Select(x => x.Tick).ToArray());
        Assert.AreEqual(1, frames.Count(x => x.Input.JumpPressed));
        Assert.AreEqual(1, frames.Count(x => x.ActualInput.JumpPressed));
        Assert.AreEqual(1, frames.Count(x => x.Input.InteractPressed));
        rig.Worker.PublishInput(epoch, HeldJump, HeldDrag, 2, 0, 2);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.IsTrue(rig.Drain().Single().ActualInput.JumpPressed, "A distinct second press must survive even when no release sample reached the worker.");
    }

    [TestMethod]
    public void TwoPressesBeforeSamplingAreNotLostAndAnOlderSequenceCannotReplaceLatestInput()
    {
        using var rig = new Rig(); rig.Configure();
        var epoch = rig.Worker.Epoch;
        rig.Worker.PublishInput(epoch, HeldJump, HeldDrag, 1, 0, 1);
        rig.Worker.PublishInput(epoch, HeldJump with { Horizontal = -1 }, HeldDrag with { Horizontal = -1 }, 2, 0, 2);
        rig.Worker.PublishInput(epoch, HeldJump, HeldDrag, 1, 0, 1);
        for (var i = 0; i < 3; i++) Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var frames = rig.Drain();
        Assert.AreEqual(2, frames.Count(x => x.ActualInput.JumpPressed));
        Assert.AreEqual(2, frames.Count(x => x.Input.InteractPressed));
        Assert.IsTrue(frames.All(x => x.ActualInput.Horizontal == -1 && x.Input.Horizontal == -1), "An older sequence packet must not roll back the latest held input.");
    }

    [TestMethod]
    public void CancelDiscardsPendingSpaceAndClearsHeldDirectionsAndDragImmediately()
    {
        using var rig = new Rig(); rig.Configure();
        var epoch = rig.Worker.Epoch;
        rig.Worker.PublishInput(epoch, HeldJump, HeldDrag, 7, 0, 9);
        rig.Worker.PublishInput(epoch, new(0, 0, 0, false, false, false, true), new(CancelForces: true), 7, 1, 9);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var canceled = rig.Drain().Single();
        Assert.IsFalse(canceled.Input.JumpPressed);
        Assert.IsFalse(canceled.Input.InteractPressed);
        Assert.IsTrue(canceled.ActualInput.CancelForces);
        AssertCleared(canceled);
        rig.Worker.PublishInput(epoch, default, default, 7, 1, 9);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var next = rig.Drain().Single();
        AssertCleared(next);
        Assert.IsFalse(next.ActualInput.JumpPressed);
        Assert.IsFalse(next.ActualInput.CancelForces, "Cancellation is an edge, not a held action.");
    }

    [TestMethod]
    public void HeldWSConflictKeepsCancellationActiveAcrossEveryCatchupTick()
    {
        using var rig = new Rig(); rig.Configure();
        var conflicting = new LabInputSnapshot(1, 0, 0, true, false, false, true);
        var actual = new LabBeltInput(Horizontal: 1, InputActive: true, CancelForces: true);
        rig.Worker.PublishInput(rig.Worker.Epoch, conflicting, actual, 0, 1);
        for (var i = 0; i < 3; i++) Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var frames = rig.Drain();
        Assert.AreEqual(3, frames.Length);
        Assert.IsTrue(frames.All(x => x.Input.CancelForces && x.ActualInput.CancelForces), "One published W+S conflict is a held cancellation level for all sampled ticks.");
        Assert.IsTrue(frames.All(x => x.ActualInput.Horizontal == 1), "The worker must preserve the exact input given to native cancellation.");
    }

    [TestMethod]
    public void HeldWSConflictDoesNotDiscardANewIndependentSpacePress()
    {
        using var rig = new Rig(); rig.Configure();
        var conflicting = new LabInputSnapshot(1, 0, 0, true, false, false, true);
        var actual = new LabBeltInput(Horizontal: 1, InputActive: true, CancelForces: true);
        rig.Worker.PublishInput(rig.Worker.Epoch, conflicting, actual, 0, 0);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync())); rig.Drain();
        rig.Worker.PublishInput(rig.Worker.Epoch, conflicting with { JumpPressed = true }, actual with { JumpPressed = true }, 1, 0);
        for (var i = 0; i < 3; i++) Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var frames = rig.Drain();
        Assert.AreEqual(1, frames.Count(x => x.ActualInput.JumpPressed), "A W+S drive cancellation level cannot erase the independent Space edge.");
        Assert.IsTrue(frames.All(x => x.ActualInput.CancelForces));
    }

    [TestMethod]
    public void PauseInvalidatesHeldInputPendingSpaceAndQueuedFrames()
    {
        using var rig = new Rig(); rig.Configure();
        var oldEpoch = rig.Worker.Epoch;
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 5, 0, 2);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var newEpoch = Complete(rig.Worker.PauseAsync());
        Assert.IsTrue(newEpoch > oldEpoch);
        Assert.IsFalse(rig.Worker.TryDequeue(out _), "A paused window cannot apply its old epoch's queued frame.");
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 6, 0, 3);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var next = rig.Drain().Single();
        Assert.AreEqual(newEpoch, next.Epoch);
        AssertCleared(next);
        Assert.IsFalse(next.ActualInput.JumpPressed);
        rig.Worker.MarkApplied(oldEpoch);
        Assert.AreEqual(0, rig.Worker.Metrics.AppliedTicks);
    }

    [TestMethod]
    public void AutonomousResumeSamplesClearedInputAfterPause()
    {
        using var rig = new Rig(); var session = rig.Configure();
        var oldEpoch = rig.Worker.Epoch;
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 1, 0, 1);
        Complete(rig.Worker.PauseAsync());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        LabBeltStepSnapshot? observed = null;
        session.SnapshotFactory = (epoch, tick, input, actual) =>
        {
            observed = Snapshot(epoch, tick, input, actual); entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
            return observed;
        };
        Assert.IsTrue(Complete(rig.Worker.ResumeAsync()));
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)), "The resumed autonomous scheduler did not sample an input.");
        var paused = rig.Worker.PauseAsync();
        release.Set(); Complete(paused);
        Assert.IsNotNull(observed);
        AssertCleared(observed);
        Assert.IsFalse(observed.ActualInput.JumpPressed);
        Assert.IsFalse(observed.Input.InteractPressed);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
    }

    [TestMethod]
    public void ResetWhileNativeStepIsBlockedRejectsOldResultAndLateInput()
    {
        using var rig = new Rig();
        var first = rig.Configure();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        first.BeforeStep = () => { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)), "Test did not release the finite native call."); };
        var oldEpoch = rig.Worker.Epoch;
        var oldStep = rig.Worker.AdvanceAsync();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        var second = new FakeSession();
        var configure = rig.Worker.ConfigureAsync(() => { second.Record("create"); return second; });
        rig.Worker.PublishInput(oldEpoch, HeldJump, HeldDrag, 99, 0, 99);
        Assert.IsFalse(configure.IsCompleted, "Reset must wait for the in-flight owner call.");
        Assert.AreEqual(0, first.DisposeCount);
        release.Set();
        Assert.IsFalse(Complete(oldStep), "The in-flight old generation must not publish a success.");
        Complete(configure);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var fresh = rig.Drain().Single();
        Assert.AreEqual(1, fresh.Tick);
        Assert.AreEqual(rig.Worker.Epoch, fresh.Epoch);
        AssertCleared(fresh);
        Assert.IsFalse(fresh.ActualInput.JumpPressed);
    }

    [TestMethod]
    public void CloseWhileNativeStepIsBlockedDisposesOnlyAfterOwnerReturns()
    {
        using var rig = new Rig(); var session = rig.Configure();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        session.BeforeStep = () => { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); };
        var step = rig.Worker.AdvanceAsync();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        var close = rig.Worker.CloseAsync();
        Assert.IsFalse(close.IsCompleted);
        Assert.AreEqual(0, session.DisposeCount);
        release.Set();
        Assert.IsFalse(Complete(step));
        Complete(close);
        Assert.AreEqual(1, session.DisposeCount);
        Assert.AreEqual(session.Operations.Single(x => x.Name == "step").Thread, session.Operations.Single(x => x.Name == "dispose").Thread);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
    }

    [TestMethod]
    public void AQueuedObsoleteReconfigurationNeverCreatesItsNativeSession()
    {
        using var rig = new Rig(); var initial = rig.Configure();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        initial.BeforeStep = () => { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); };
        var inFlight = rig.Worker.AdvanceAsync();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        var obsoleteFactoryCalls = 0;
        var obsolete = rig.Worker.ConfigureAsync(() => { Interlocked.Increment(ref obsoleteFactoryCalls); return new FakeSession(); });
        var current = new FakeSession();
        var latest = rig.Worker.ConfigureAsync(() => { current.Record("create"); return current; });
        release.Set();
        Assert.IsFalse(Complete(inFlight));
        Assert.ThrowsExactly<OperationCanceledException>(() => Complete(obsolete));
        Complete(latest);
        Assert.AreEqual(0, obsoleteFactoryCalls);
        Assert.AreEqual(1, initial.DisposeCount);
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.AreEqual(1, rig.Drain().Single().Tick);
    }

    [TestMethod]
    public void SupersedingAnInProgressFactoryDisposesItsReplacementOnTheOwner()
    {
        using var rig = new Rig(); rig.Configure();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var obsolete = new FakeSession();
        var configuring = rig.Worker.ConfigureAsync(() =>
        {
            obsolete.Record("create"); entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10)));
            return obsolete;
        });
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        var current = new FakeSession();
        var latest = rig.Worker.ConfigureAsync(() => { current.Record("create"); return current; });
        release.Set();
        Assert.ThrowsExactly<OperationCanceledException>(() => Complete(configuring));
        Complete(latest);
        Assert.AreEqual(1, obsolete.DisposeCount);
        Assert.AreEqual(obsolete.Operations.Single(x => x.Name == "create").Thread, obsolete.Operations.Single(x => x.Name == "dispose").Thread);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.AreEqual(1, rig.Drain().Single().Tick);
    }

    [TestMethod]
    public void ManualStepReturningAfterPauseCannotDeliverAnOldGenerationSnapshot()
    {
        using var rig = new Rig(); var session = rig.Configure();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        session.BeforeStep = () => { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); };
        var manual = rig.Worker.StepAsync(HeldJump, HeldDrag);
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        var paused = rig.Worker.PauseAsync();
        release.Set();
        Assert.ThrowsExactly<OperationCanceledException>(() => Complete(manual));
        Complete(paused);
        Assert.IsFalse(rig.Worker.TryDequeue(out _));
    }

    [TestMethod]
    public void FailedFactoryReportsItsExceptionAndAllowsAnExplicitNewConfiguration()
    {
        using var rig = new Rig(); var initial = rig.Configure();
        var failure = new InvalidOperationException("native configuration rejected");
        var actual = Assert.ThrowsExactly<InvalidOperationException>(() => Complete(rig.Worker.ConfigureAsync(() => throw failure)));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(1, initial.DisposeCount);
        Assert.IsNull(rig.Worker.Capabilities);
        rig.Configure();
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.AreEqual(1, rig.Drain().Single().Tick);
    }

    [TestMethod]
    public void FullFifoPreservesEverySuccessfulTickAndReturnsAnExplicitTerminalFault()
    {
        using var rig = new Rig(2); var session = rig.Configure();
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        Assert.IsFalse(Complete(rig.Worker.AdvanceAsync()));
        Assert.AreEqual(2, session.Operations.Count(x => x.Name == "step"), "Backpressure must be detected before attempting an unpublishable native step.");
        var frames = rig.Drain();
        Assert.AreEqual(3, frames.Length);
        CollectionAssert.AreEqual(new[] { 1, 2 }, frames.Take(2).Select(x => x.Tick).ToArray());
        Assert.IsTrue(frames.Take(2).All(x => x.Error is null));
        Assert.IsNotNull(frames[2].Error);
        StringAssert.Contains(frames[2].Error!.Message, "FIFO");
        Assert.AreEqual(2, rig.Worker.Metrics.ProducedTicks);
        Assert.AreEqual(2, rig.Worker.Metrics.MaximumQueueDepth);
        Assert.AreEqual(1, rig.Worker.Metrics.BackpressurePauses);
        Assert.IsFalse(rig.Worker.Metrics.Running);
        Assert.AreEqual(0L, rig.Worker.Metrics.RejectedSnapshots, "Queue pressure cannot silently replace successful frames.");
    }

    [TestMethod]
    public void PublishedArraysAreOwnedEvenWhenTheInjectedSessionReusesItsScratchBuffers()
    {
        using var rig = new Rig(); var session = rig.Configure();
        var reused = Snapshot(0, 0, default, default);
        session.SnapshotFactory = (epoch, tick, input, actual) =>
        {
            foreach (var array in FloatArrays(reused)) Array.Fill(array, (float)tick);
            foreach (var array in DoubleArrays(reused)) Array.Fill(array, (double)tick);
            return reused with { Epoch = epoch, Tick = tick, Input = input, ActualInput = actual };
        };
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var first = rig.Drain().Single();
        Assert.IsTrue(Complete(rig.Worker.AdvanceAsync()));
        var second = rig.Drain().Single();
        foreach (var array in FloatArrays(first)) Assert.IsTrue(array.All(x => x == 1), "A later read rewrote an owned float array.");
        foreach (var array in DoubleArrays(first)) Assert.IsTrue(array.All(x => x == 1), "A later read rewrote an owned double array.");
        Assert.AreNotSame(first.Frame, second.Frame);
        first.Frame!.Curve[0] = 12345;
        Assert.AreEqual(2f, reused.Frame!.Curve[0], "A consumer must not mutate the session's scratch buffer.");
    }

    [TestMethod]
    public void FailurePreservesTheOriginalErrorPreStateActualInputAndDiagnosticArrays()
    {
        using var rig = new Rig(); var session = rig.Configure();
        var failure = new InvalidOperationException("native rejected at frozen solver site 24");
        session.SnapshotFactory = (epoch, tick, input, actual) => Snapshot(epoch, tick, input, actual) with { Error = failure, PreState = [200, -0d, 87], SolverFailure = [24, 4, 7] };
        rig.Worker.PublishInput(rig.Worker.Epoch, HeldJump, HeldDrag, 1, 0, 1);
        Assert.IsFalse(Complete(rig.Worker.AdvanceAsync()));
        var frame = rig.Drain().Single();
        Assert.AreSame(failure, frame.Error);
        Assert.AreEqual(HeldDrag, frame.ActualInput);
        Assert.AreEqual(HeldJump, frame.Input);
        Assert.AreEqual(LabWindow.FixedDt, frame.FixedDt);
        CollectionAssert.AreEqual(new[] { 200d, -0d, 87d }, frame.PreState!);
        CollectionAssert.AreEqual(new[] { 24d, 4d, 7d }, frame.SolverFailure!);
        Assert.AreEqual(0, rig.Worker.Metrics.ProducedTicks);
        Assert.IsFalse(rig.Worker.Metrics.Running);
    }

    [TestMethod]
    public void AnUnexpectedSessionExceptionIsReportedWithoutKillingCommandCompletion()
    {
        using var rig = new Rig(); var session = rig.Configure();
        var failure = new InvalidOperationException("readback failed");
        session.BeforeStep = () => throw failure;
        rig.Worker.PublishInput(rig.Worker.Epoch, HeldJump, HeldDrag, 1, 0, 1);
        Assert.IsFalse(Complete(rig.Worker.AdvanceAsync()));
        var frame = rig.Drain().Single();
        Assert.AreSame(failure, frame.Error);
        Assert.AreEqual(HeldDrag, frame.ActualInput);
        Complete(rig.Worker.PauseAsync());
        Assert.IsFalse(rig.Worker.Metrics.Running);
    }

    private static void AssertCleared(LabBeltStepSnapshot snapshot)
    {
        Assert.AreEqual(0d, snapshot.Input.Horizontal); Assert.AreEqual(0d, snapshot.Input.Downward); Assert.AreEqual(0d, snapshot.Input.Stretch);
        Assert.IsFalse(snapshot.Input.InputActive);
        Assert.AreEqual(0d, snapshot.ActualInput.Horizontal); Assert.AreEqual(0d, snapshot.ActualInput.Downward); Assert.AreEqual(0d, snapshot.ActualInput.Stretch);
        Assert.IsFalse(snapshot.ActualInput.DragActive); Assert.IsFalse(snapshot.ActualInput.InputActive);
    }

    private static IEnumerable<float[]> FloatArrays(LabBeltStepSnapshot frame) => [frame.Frame!.Scaffold, frame.Frame.Curve, frame.Frame.Stats, frame.Guard!, frame.Diagnostics!];
    private static IEnumerable<double[]> DoubleArrays(LabBeltStepSnapshot frame) => [frame.Speed!, frame.Radial!, frame.Rear!, frame.Ledger!, frame.PreState!, frame.SolverFailure!];
    private static LabBeltStepSnapshot Snapshot(long epoch, int tick, LabInputSnapshot input, LabBeltInput actual) => new(epoch, tick, input, actual,
        new([1, 2], [3, 4], [5, 6]), [7, 8], [9, 10], [11, 12], [13, 14], [15, 16], [17, 18], [19, 20], [21, 22], null, 0, 0, 1);
    private static T Complete<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    private static void Complete(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

    private sealed class Rig(int capacity = 8) : IDisposable
    {
        internal LabBeltPhysicsWorker Worker { get; } = new(capacity);
        internal FakeSession Configure()
        {
            var session = new FakeSession();
            Complete(Worker.ConfigureAsync(() => { session.Record("create"); return session; }));
            return session;
        }
        internal LabBeltStepSnapshot[] Drain()
        {
            var frames = new List<LabBeltStepSnapshot>();
            while (Worker.TryDequeue(out var frame)) frames.Add(frame!);
            return frames.ToArray();
        }
        public void Dispose() => Complete(Worker.CloseAsync());
    }

    private sealed class FakeSession : ILabBeltWorkerSession
    {
        internal ConcurrentQueue<(string Name, int Thread)> Operations { get; } = new();
        internal Action? BeforeStep { get; set; }
        internal Func<long, int, LabInputSnapshot, LabBeltInput, LabBeltStepSnapshot>? SnapshotFactory { get; set; }
        internal int DisposeCount;
        internal void Record(string name) => Operations.Enqueue((name, Environment.CurrentManagedThreadId));
        public LabBeltCapabilities Capabilities { get { Record("capabilities"); return new(true, true, true, true, true, true, true, true, false, true, true, true); } }
        public LabBeltStepSnapshot Step(long epoch, int tick, LabInputSnapshot input, LabBeltInput actualInput)
        {
            Record("step"); BeforeStep?.Invoke();
            return SnapshotFactory?.Invoke(epoch, tick, input, actualInput) ?? Snapshot(epoch, tick, input, actualInput);
        }
        public Dictionary<string, byte[]> ReadState() { Record("read"); return new() { ["fake"] = [1, 2, 3] }; }
        public void Dispose() { Record("dispose"); Interlocked.Increment(ref DisposeCount); }
    }
}
