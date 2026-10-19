using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Belt;

[TestClass]
public sealed class BeltBodyRuntimeTests
{
    private static async Task Configured(BeltBodyRuntime runtime, long bodyEpoch = 0)
    {
        runtime.SleepEnabled = true;
        await runtime.ConfigureAsync(DangoBodyControls.Default, 0, bodyEpoch).WaitAsync(TimeSpan.FromSeconds(3));
    }
    private static BeltBodySnapshot Take(BeltBodyRuntime runtime)
    {
        Assert.IsTrue(runtime.TryDequeue(out var snapshot)); return snapshot!;
    }
    private static async Task Stable(BeltBodyRuntime runtime)
    {
        for (var i = 0; i <= BeltSleepPolicy.StableTicksRequired && !runtime.Metrics.Sleeping; i++)
        { Assert.IsTrue(await runtime.AdvanceAsync()); Take(runtime); }
        Assert.IsTrue(runtime.Metrics.Sleeping);
    }

    [TestMethod]
    public async Task OneOwnerCreatesReconfiguresStepsAndDisposesOnSameThread()
    {
        var native = new FakeSession();
        var createThread = 0;
        var runtime = new BeltBodyRuntime(_ => { createThread = Environment.CurrentManagedThreadId; return native; }, true);
        await Configured(runtime);
        await runtime.ConfigureAsync(new(110, 20, 80), 1, 0);
        Assert.IsTrue(await runtime.AdvanceAsync());
        var snapshot = Take(runtime);
        Assert.AreEqual(1L, snapshot.ConfigurationRevision);
        await runtime.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(createThread != Environment.CurrentManagedThreadId);
        Assert.IsTrue(native.Threads.All(thread => thread == createThread));
        Assert.AreEqual(1, native.Disposals);
        await runtime.CloseAsync();
        Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.ConfigureAsync(DangoBodyControls.Default, 2, 0));
    }

    [TestMethod]
    public async Task HotEditOnFullFifoPreservesEverySuccessfulOldSnapshotAndCoalescesPendingOnly()
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime); var epoch = runtime.Epoch;
        for (var i = 0; i < 8; i++) Assert.IsTrue(await runtime.AdvanceAsync());
        var superseded = runtime.ConfigureAsync(new(90, 20, 20), 1, 0);
        var newest = runtime.ConfigureAsync(new(110, 80, 80), 2, 0);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await superseded);
        Assert.IsFalse(newest.IsCompleted); Assert.AreEqual(8, runtime.Metrics.QueueDepth);
        var first = Take(runtime); Assert.AreEqual(0L, first.ConfigurationRevision);
        await newest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(await runtime.AdvanceAsync());
        for (var i = 2; i <= 8; i++)
        {
            var previous = Take(runtime); Assert.AreEqual((long)i, previous.Sequence);
            Assert.AreEqual(0L, previous.ConfigurationRevision); Assert.AreEqual(epoch, previous.Epoch);
        }
        var updated = Take(runtime); Assert.AreEqual(9L, updated.Sequence);
        Assert.AreEqual(2L, updated.ConfigurationRevision); Assert.AreEqual(new DangoBodyControls(110, 80, 80), updated.Body);
        Assert.AreEqual(epoch, runtime.Epoch); Assert.AreEqual(0L, runtime.Metrics.InvalidatedSnapshots);
        Assert.AreEqual(1, native.Reconfigurations);
    }

    [TestMethod]
    public async Task ContinuousFullFiveHundredMillisecondsCannotBeRescuedByLateConsumer()
    {
        long clock = 0;
        await using var runtime = new BeltBodyRuntime(_ => new FakeSession(), true, () => Interlocked.Read(ref clock), 1000);
        await Configured(runtime);
        for (var i = 0; i < 8; i++) await runtime.AdvanceAsync();
        Interlocked.Exchange(ref clock, 500);
        for (var i = 0; i < 8; i++) Assert.IsNull(Take(runtime).Error);
        Assert.IsNotNull(Take(runtime).Error);
        Assert.AreEqual(1, runtime.Metrics.BackpressurePauses);
        Assert.IsFalse(await runtime.ResumeAsync());
        Assert.IsFalse(await runtime.AdvanceAsync());
        await runtime.ConfigureAsync(DangoBodyControls.Default, 1, 1);
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.IsNull(Take(runtime).Error);
    }

    [TestMethod]
    public async Task PauseIsImmediateEpochInvalidationAndFiniteInFlightOwnerBarrier()
    {
        var native = new FakeSession { BlockStep = true };
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime); var before = runtime.Epoch;
        var step = runtime.AdvanceAsync();
        Assert.IsTrue(native.Entered.Wait(TimeSpan.FromSeconds(3)));
        var paused = runtime.PauseAsync();
        Assert.AreEqual(before + 1, runtime.Epoch); Assert.IsFalse(paused.IsCompleted);
        native.Release.Set();
        Assert.IsFalse(await step); await paused.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsFalse(runtime.TryDequeue(out _)); Assert.AreEqual(1L, runtime.Metrics.InvalidatedSnapshots);
    }

    [TestMethod]
    public async Task ChangedBodyEpochInvalidatesQueuedAndLateDisplayAndRestoresWhilePaused()
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        await runtime.AdvanceAsync(); var old = Take(runtime);
        await runtime.AdvanceAsync();
        await runtime.ConfigureAsync(new(80, 0, 0), 1, 1);
        Assert.IsFalse(runtime.MarkDisplayed(old)); Assert.AreEqual(1L, runtime.Metrics.InvalidatedSnapshots);
        Assert.IsTrue(await runtime.AdvanceAsync()); var restored = Take(runtime);
        Assert.AreEqual(1L, restored.BodyEpoch); Assert.AreEqual(1L, restored.ConfigurationRevision);
        Assert.IsTrue(runtime.MarkDisplayed(restored)); Assert.IsFalse(runtime.MarkDisplayed(restored));
    }

    [TestMethod]
    public async Task OldFifoDisplayCannotUnlockNewDraftOrCancelledSessionSave()
    {
        var settings = new Dango.Desktop.AppShell.ControlCenter.AppearanceSettingsSession(
            Path.Combine(Path.GetTempPath(), "Dango-belt-runtime-tests", Guid.NewGuid().ToString("N"), "appearance.json"));
        await using var runtime = new BeltBodyRuntime(_ => new FakeSession(), true);
        await Configured(runtime); await runtime.AdvanceAsync();
        settings.StageBody(new(110, 60, 40));
        await runtime.ConfigureAsync(settings.DraftAppearance.Body, settings.BodyConfigurationRevision, settings.BodyEpoch);
        var old = Take(runtime); Assert.IsTrue(runtime.MarkDisplayed(old));
        Assert.IsFalse(settings.MarkBodyApplied(old.Body, old.ModelVersion, old.ConfigurationRevision, old.BodyEpoch));
        Assert.IsTrue(settings.RequiresBodyPreview);
        await runtime.AdvanceAsync(); var current = Take(runtime);
        Assert.IsTrue(settings.MarkBodyApplied(current.Body, current.ModelVersion, current.ConfigurationRevision, current.BodyEpoch));
        Assert.IsFalse(settings.RequiresBodyPreview);
        settings.CancelPreview();
        Assert.IsFalse(settings.MarkBodyApplied(current.Body, current.ModelVersion, current.ConfigurationRevision, current.BodyEpoch));
    }

    [TestMethod]
    public async Task ConfigurationFailureKeepsLastBodyAndDoesNotLatchPhysicsFault()
    {
        var native = new FakeSession { RejectConfiguration = true };
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await runtime.ConfigureAsync(new(120, 0, 0), 1, 0));
        Assert.IsNull(runtime.Metrics.Error); Assert.IsTrue(await runtime.AdvanceAsync());
        Assert.AreEqual(DangoBodyControls.Default, Take(runtime).Body);
        native.RejectConfiguration = false;
        await runtime.ConfigureAsync(new(120, 0, 0), 2, 0);
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.AreEqual(2L, Take(runtime).ConfigurationRevision);
    }

    [TestMethod]
    public async Task StepFailureLatchesUntilExplicitLifecycleRecoveryAndKeepsEvidence()
    {
        var native = new FakeSession { RejectStep = true };
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        Assert.IsFalse(await runtime.AdvanceAsync()); Assert.IsNotNull(Take(runtime).Error);
        Assert.IsFalse(runtime.Metrics.Sleeping); Assert.IsNotNull(runtime.LastFailure);
        native.RejectStep = false;
        await runtime.ConfigureAsync(new(110, 50, 50), 1, 0);
        Assert.IsFalse(await runtime.ResumeAsync()); Assert.IsFalse(await runtime.AdvanceAsync());
        await runtime.ConfigureAsync(new(110, 50, 50), 2, 1);
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.IsNull(Take(runtime).Error);
        Assert.IsNotNull(runtime.LastFailure, "Explicit recovery must retain the prior forensic failure snapshot.");
    }

    [TestMethod]
    public async Task SleepFreezesNativeCallsAndOpposingKeysRemainActive()
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime); await Stable(runtime);
        var calls = native.Calls;
        Assert.IsFalse(await runtime.AdvanceAsync()); Assert.AreEqual(calls, native.Calls);
        runtime.PublishInput(new(InputActive: true));
        for (var i = 0; i < 140; i++) { Assert.IsTrue(await runtime.AdvanceAsync()); Take(runtime); }
        Assert.IsFalse(runtime.Metrics.Sleeping);
        runtime.ClearInput(); await runtime.AdvanceAsync(); Take(runtime); await Stable(runtime);
    }

    [TestMethod]
    public async Task InternalVelocityTrendAndCoreCurveMotionPreventSleepEvenWithQuietCom()
    {
        var native = new FakeSession { VelocityDrift = .00001 };
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        for (var i = 0; i < 150; i++) { await runtime.AdvanceAsync(); Take(runtime); }
        Assert.IsFalse(runtime.Metrics.Sleeping);
        native.VelocityDrift = 0; native.MoveCurve = true;
        for (var i = 0; i < 150; i++) { await runtime.AdvanceAsync(); Take(runtime); }
        Assert.IsFalse(runtime.Metrics.Sleeping);
    }

    [TestMethod]
    [DataRow("internal-q")]
    [DataRow("velocity-window")]
    [DataRow("airborne")]
    [DataRow("no-contact")]
    [DataRow("jump-cooldown")]
    public async Task StrictSleepCounterexamplesRejectQuietCom(string reason)
    {
        var native = new FakeSession
        {
            MoveInternalQ = reason == "internal-q", VelocityDrift = reason == "velocity-window" ? .00001 : 0,
            Airborne = reason == "airborne", NoContact = reason == "no-contact", UnexpiredCooldown = reason == "jump-cooldown",
        };
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        for (var i = 0; i < 180; i++) { Assert.IsTrue(await runtime.AdvanceAsync()); Take(runtime); }
        Assert.IsFalse(runtime.Metrics.Sleeping);
    }

    [TestMethod]
    public async Task ConstantSupportedContactVelocityResidualIsPreservedAndMaySleep()
    {
        var native = new FakeSession { InternalVelocity = .06 };
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime); await Stable(runtime);
        Assert.AreEqual(.06, native.InternalVelocity);
        runtime.PublishInput(new(InputActive: true)); Assert.IsTrue(await runtime.AdvanceAsync());
        Assert.AreEqual(.06, Take(runtime).DebugState![48]);
    }

    [TestMethod]
    public async Task JumpArrivingAfterSleepDecisionBeforeWaitIsRetainedAndConsumedOnce()
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        using var injected = new ManualResetEventSlim(); var count = 0;
        runtime.BeforeSleepWait = () =>
        {
            if (Interlocked.Increment(ref count) != 1) return;
            runtime.PublishInput(new(JumpPressed: true, InputActive: true));
            runtime.PublishInput(default); injected.Set();
        };
        for (var i = 0; i <= 120; i++) { await runtime.AdvanceAsync(); Take(runtime); }
        Assert.IsTrue(injected.Wait(TimeSpan.FromSeconds(3)));
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.IsTrue(Take(runtime).ActualInput.JumpPressed);
        Assert.IsTrue(await runtime.AdvanceAsync()); Assert.IsFalse(Take(runtime).ActualInput.JumpPressed);
        Assert.IsFalse(runtime.Metrics.Sleeping);
    }

    [TestMethod]
    [DataRow("configuration")]
    [DataRow("cancel")]
    [DataRow("bounds")]
    [DataRow("close")]
    public async Task CommandsArrivingBetweenSleepDecisionAndWaitAreNotLost(string command)
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime);
        using var injected = new ManualResetEventSlim(); var count = 0;
        Task? operation = null;
        runtime.BeforeSleepWait = () =>
        {
            if (Interlocked.Increment(ref count) != 1) return;
            operation = command switch
            {
                "configuration" => runtime.ConfigureAsync(new(90, 20, 20), 1, 0),
                "cancel" => runtime.ConfigureAsync(DangoBodyControls.Default, 1, 1),
                "bounds" => runtime.SetBoundsAsync(-3, 3),
                _ => runtime.CloseAsync(),
            };
            injected.Set();
        };
        for (var i = 0; i <= 120; i++)
        {
            Assert.IsTrue(await runtime.AdvanceAsync());
            // Cancel/close can invalidate the just-published final old snapshot immediately.
            runtime.TryDequeue(out _);
        }
        Assert.IsTrue(injected.Wait(TimeSpan.FromSeconds(3)));
        await operation!.WaitAsync(TimeSpan.FromSeconds(3));
        if (command == "close") { Assert.AreEqual(1, native.Disposals); return; }
        Assert.IsFalse(runtime.Metrics.Sleeping);
        Assert.IsTrue(await runtime.AdvanceAsync());
        var snapshot = Take(runtime);
        Assert.AreEqual(command is "configuration" or "cancel" ? 1L : 0L, snapshot.ConfigurationRevision);
        Assert.AreEqual(command == "cancel" ? 1L : 0L, snapshot.BodyEpoch);
    }

    [TestMethod]
    public async Task HotConfigurationBoundsToggleAndLifecycleWakeWithoutCatchup()
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime); await Stable(runtime);
        await runtime.ConfigureAsync(new(90, 30, 20), 1, 0);
        Assert.IsFalse(runtime.Metrics.Sleeping); await runtime.AdvanceAsync();
        Assert.AreEqual(1L, Take(runtime).ConfigurationRevision);
        await Stable(runtime); await runtime.SetBoundsAsync(-2, 2);
        Assert.IsFalse(runtime.Metrics.Sleeping); Assert.AreEqual(1, native.BoundsChanges);
        await Stable(runtime); runtime.SleepEnabled = false;
        for (var i = 0; i < 130; i++) { await runtime.AdvanceAsync(); Take(runtime); }
        Assert.IsFalse(runtime.Metrics.Sleeping);
        runtime.SleepEnabled = true; await Stable(runtime);
        await runtime.PauseAsync(); Assert.IsFalse(runtime.Metrics.Sleeping);
        Assert.IsTrue(await runtime.ResumeAsync());
        Assert.AreEqual(0d, runtime.Metrics.DiscardedSeconds);
    }

    [TestMethod]
    public async Task CloseWhileAsleepFinishesAndReleasesOwnerExactlyOnce()
    {
        var native = new FakeSession();
        var runtime = new BeltBodyRuntime(_ => native, true);
        await Configured(runtime); await Stable(runtime);
        await runtime.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(1, native.Disposals); Assert.IsFalse(runtime.Metrics.Running);
    }

    [TestMethod]
    public async Task AutomaticSleepingOwnerHasNoTimerPollAndWakeDoesNotCatchUpWallTime()
    {
        var native = new FakeSession();
        await using var runtime = new BeltBodyRuntime(_ => native);
        await Configured(runtime);
        long waits = 0; runtime.BeforeSleepWait = () => Interlocked.Increment(ref waits);
        Assert.IsTrue(await runtime.ResumeAsync());
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!runtime.Metrics.Sleeping && System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds < 4)
        {
            while (runtime.TryDequeue(out var frame)) Assert.IsNull(frame!.Error);
            await Task.Delay(8);
        }
        Assert.IsTrue(runtime.Metrics.Sleeping);
        while (runtime.TryDequeue(out _)) { }
        await Task.Delay(20);
        var calls = runtime.Metrics.NativeStepCalls; var entries = Interlocked.Read(ref waits);
        await Task.Delay(150);
        Assert.AreEqual(calls, runtime.Metrics.NativeStepCalls);
        Assert.AreEqual(entries, Interlocked.Read(ref waits), "Sleeping owner must not poll on a timeout.");
        runtime.PublishInput(new(JumpPressed: true, InputActive: true)); runtime.PublishInput(default);
        BeltBodySnapshot? awake = null; started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (awake is null && System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 300)
        { runtime.TryDequeue(out awake); if (awake is null) await Task.Delay(2); }
        Assert.IsNotNull(awake); Assert.IsTrue(awake.ActualInput.JumpPressed);
        Assert.AreEqual(calls + 1, (long)awake.Tick);
        Assert.AreEqual(0d, runtime.Metrics.DiscardedSeconds);
    }

    [TestMethod]
    public async Task ResumeUsesAutomaticFixedStepCadenceWithoutUiAdvanceCalls()
    {
        await using var runtime = new BeltBodyRuntime(_ => new FakeSession());
        await Configured(runtime); Assert.IsTrue(await runtime.ResumeAsync());
        await Task.Delay(85);
        var frames = new List<BeltBodySnapshot>();
        while (runtime.TryDequeue(out var frame)) frames.Add(frame!);
        Assert.IsTrue(frames.Count >= 2 && frames.Count <= 8);
        Assert.IsTrue(frames.All(frame => frame.FixedDt == .0166666 && frame.Error is null));
    }

    internal sealed class FakeSession : IBeltBodySession
    {
        internal readonly List<int> Threads = [];
        internal readonly ManualResetEventSlim Entered = new(), Release = new();
        internal int Disposals, Reconfigurations, Calls, BoundsChanges;
        internal bool BlockStep, RejectConfiguration, RejectStep, MoveCurve, MoveInternalQ, Airborne, NoContact, UnexpiredCooldown;
        internal double InternalVelocity, VelocityDrift;
        public void Reconfigure(DangoBodyControls body)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            if (RejectConfiguration) throw new InvalidOperationException("rejected configuration");
            Reconfigurations++;
        }
        public void SetBounds(double left, double right) { Threads.Add(Environment.CurrentManagedThreadId); BoundsChanges++; }
        public BeltNativeStep Step(DangoBeltInput input)
        {
            Threads.Add(Environment.CurrentManagedThreadId); Calls++; Entered.Set();
            if (BlockStep) Release.Wait(TimeSpan.FromSeconds(3));
            if (RejectStep) throw new InvalidOperationException("rejected physical tick");
            var stats = new float[64]; stats[14] = Airborne ? 0 : 1; stats[25] = NoContact ? 0 : 1;
            var curve = new float[512]; if (MoveCurve) curve[0] = Calls * .00002f;
            var state = new double[200]; state[0] = 1; state[1] = 200; state[38 + 10] = InternalVelocity + Calls * VelocityDrift; state[82] = Airborne ? 0 : 1;
            state[79] = UnexpiredCooldown ? .1 : .45 + Calls * DangoBeltWorld.FixedDt; state[125] = .45;
            if (MoveInternalQ) state[12] = Calls * .00002;
            return new(new(new float[68], curve, stats), state, null, null, null, null, null, null, null, null, 0, 0);
        }
        public void Dispose() { Threads.Add(Environment.CurrentManagedThreadId); Disposals++; }
    }
}
