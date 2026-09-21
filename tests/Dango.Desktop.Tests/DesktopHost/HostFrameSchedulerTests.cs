using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class HostFrameSchedulerTests
{
    private static readonly PhysicsPresentationInput Input = new("clock-fixture", new LogicalRect(400, 500, 160, 120),
        DangoActionState.Idle, 1000, 17, "fixture-ground", new(0, -1), 0, 1, false, true, []);

    private sealed class Source : IHostFrameSource
    {
        internal EventHandler? Handlers;
        internal int Count => Handlers?.GetInvocationList().Length ?? 0;
        public event EventHandler Rendering { add => Handlers += value; remove => Handlers -= value; }
        internal static EventArgs Args(long renderTicks) => (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [TimeSpan.FromTicks(renderTicks)], null)!;
        internal void Raise(long renderTicks) => Handlers?.Invoke(this, Args(renderTicks));
    }

    [TestMethod]
    public void MultiplePhysicsStepsApplyOnlyTheLastFrameAndInputEdgeOnce()
    {
        using var runtime = new StackedDangoSceneRuntime();
        using var reference = new StackedDangoSceneRuntime();
        runtime.Start(); reference.Start();
        // Establish a body before exercising a held jump across several substeps.
        runtime.Tick(Input, HostFrameClock.FixedStep); reference.Tick(Input, HostFrameClock.FixedStep);
        runtime.SetDirectionalControl(new(false, true, true, false));
        reference.SetDirectionalControl(new(false, true, true, false));
        var source = new Source(); long now = 0; var applied = 0; var prepared = 0;
        var scheduler = new HostFrameScheduler(runtime, () => { prepared++; return true; }, () => Input,
            _ => applied++, source, () => now, TimeSpan.TicksPerSecond);
        scheduler.Start();
        try
        {
            for (var i = 1; i <= 30; i++)
            {
                now = i * 2 * HostFrameClock.FixedStep.Ticks;
                source.Raise(now);
                reference.Tick(Input, HostFrameClock.FixedStep); reference.Tick(Input, HostFrameClock.FixedStep);
            }
            Assert.AreEqual(30, prepared); Assert.AreEqual(30, applied);
            Assert.AreEqual(60L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(30L, scheduler.Snapshot.AppliedFrames);
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastPhysicsFrame), JsonSerializer.Serialize(runtime.LastPhysicsFrame));
            var before = runtime.LastFrame;
            source.Raise(now); // Same WPF presentation event must be ignored.
            now++; source.Raise(now); // New event but not enough elapsed time for a step.
            Assert.AreEqual(1L, scheduler.Snapshot.DuplicateCallbacks);
            Assert.AreSame(before, runtime.LastFrame);
            Assert.AreEqual(30, applied);
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    public void SingleOwnerRepeatedStartAndStaleCallbacksCannotAdvanceSharedRuntime()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        var source = new Source(); long now = 0;
        var first = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        var second = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        first.Start();
        try
        {
            now = HostFrameClock.FixedStep.Ticks / 2; source.Raise(now);
            first.Start();
            Assert.AreEqual(1, source.Count);
            now = HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(1L, first.Snapshot.SuccessfulSteps, "Repeated Start must preserve partial elapsed time.");
            Assert.Throws<InvalidOperationException>(second.Start);
            var stale = source.Handlers;
            first.Stop();
            Assert.AreEqual(0, source.Count);
            second.Start();
            first.Stop(); // An inactive owner cannot revoke the active one.
            Assert.IsTrue(second.OwnsRuntime);
            now += HostFrameClock.FixedStep.Ticks;
            stale?.Invoke(source, Source.Args(now));
            Assert.AreEqual(1L, first.Snapshot.SuccessfulSteps);
            source.Raise(now);
            Assert.AreEqual(1L, second.Snapshot.SuccessfulSteps);
            for (var i = 0; i < 20; i++)
            {
                second.Stop(); runtime.Stop();
                var completed = second.Snapshot.SuccessfulSteps;
                now += TimeSpan.FromSeconds(3).Ticks; source.Raise(now);
                Assert.AreEqual(completed, second.Snapshot.SuccessfulSteps);
                runtime.Start(); second.Start();
                now += HostFrameClock.FixedStep.Ticks; source.Raise(now);
                Assert.AreEqual(completed + 1, second.Snapshot.SuccessfulSteps);
                Assert.AreEqual(1, source.Count);
            }
        }
        finally { first.Stop(); second.Stop(); }
        Assert.AreEqual(0, source.Count);
    }

    [TestMethod]
    public void ShortOverloadKeepsDozeButLongPauseCancelsWithoutReplayingPhysics()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize()); runtime.Start();
        runtime.SetViewport(new(0, 0, 1920, 1200));
        var owner = new object(); runtime.AttachQuietIdleHost(owner, true); runtime.UpdateWanderScreenWidth(owner, 1920);
        runtime.Tick(Input, HostFrameClock.FixedStep);
        runtime.Doze.Next = (min, _) => min;
        for (var i = 0; i < 10; i++) runtime.Doze.Advance(new(true, 0, 0), .1, true, true, _ => true);
        var idle = typeof(IdleDozeController).GetField("idle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var source = new Source(); long now = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        scheduler.Start();
        try
        {
            now = TimeSpan.FromMilliseconds(200).Ticks; source.Raise(now);
            Assert.AreEqual(6L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(1L, scheduler.Snapshot.Overloads);
            Assert.IsGreaterThanOrEqualTo(1d, (double)idle.GetValue(runtime.Doze)!);
            var physical = JsonSerializer.Serialize(runtime.LastPhysicsFrame);
            now += TimeSpan.TicksPerSecond; source.Raise(now);
            Assert.AreEqual(6L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(1L, scheduler.Snapshot.LongPauses);
            Assert.AreEqual(0d, (double)idle.GetValue(runtime.Doze)!);
            Assert.AreEqual(physical, JsonSerializer.Serialize(runtime.LastPhysicsFrame));
            now += HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(7L, scheduler.Snapshot.SuccessfulSteps);
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    public void FirstPhysicsFailureDropsUnexecutedDebtAndNeverCountsASuccessfulFrame()
    {
        using var runtime = new StackedDangoSceneRuntime("fictional-missing-clock-module.dll"); runtime.Start();
        var source = new Source(); long now = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        scheduler.Start();
        try
        {
            now = 6 * HostFrameClock.FixedStep.Ticks + 7; source.Raise(now);
            Assert.IsNotNull(runtime.LastError);
            Assert.AreEqual(1L, scheduler.Snapshot.Faults);
            Assert.AreEqual(0L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(0L, scheduler.Snapshot.AppliedFrames);
            Assert.AreEqual(now, scheduler.Snapshot.FaultDroppedTicks);
            Assert.AreEqual(0L, scheduler.Snapshot.RemainderTicks);
            now += HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(1L, scheduler.Snapshot.Faults, "Existing fault is not a new fault each callback.");
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CancellationMayStopOrRestartWithoutApplyingTheOldCallback(bool restart)
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        runtime.Tick(Input, HostFrameClock.FixedStep);
        var source = new Source(); long now = 0; var applied = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => applied++, source, () => now, TimeSpan.TicksPerSecond);
        scheduler.Start();
        void Cancelled() { scheduler.Stop(); if (restart) scheduler.Start(); }
        runtime.InputCancelled += Cancelled;
        try
        {
            now = TimeSpan.TicksPerSecond; source.Raise(now);
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(restart ? 1 : 0, source.Count);
            now += HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(restart ? 1 : 0, applied);
        }
        finally { runtime.InputCancelled -= Cancelled; scheduler.Stop(); }
    }

    [TestMethod]
    public void EachNewAttachmentFaultStopsOnceEvenWhenFirstErrorStringIsUnchanged()
    {
        using var runtime = StackedDangoSceneRuntime.CreateEyeHatReview(); runtime.Start();
        runtime.Tick(Input, HostFrameClock.FixedStep); Assert.IsTrue(runtime.AddDango());
        runtime.Tick(Input, HostFrameClock.FixedStep);
        var attachments = typeof(StackedDangoSceneRuntime).GetField("attachments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var entries = (IDictionary)attachments.GetType().GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attachments)!;
        void Damage(int index)
        {
            var entry = entries[runtime.EntityIds[index]]!;
            var motion = entry.GetType().GetProperty("Motion")!.GetValue(entry)!;
            var field = motion.GetType().GetField("tilt", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(motion, Activator.CreateInstance(field.FieldType, double.NaN, 0d));
        }
        var source = new Source(); long now = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        scheduler.Start();
        try
        {
            Damage(0); now += 6 * HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(1L, scheduler.Snapshot.Faults);
            Assert.AreEqual(1L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(0L, scheduler.Snapshot.AppliedFrames);
            var firstError = runtime.AttachmentError;
            now += 6 * HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(7L, scheduler.Snapshot.SuccessfulSteps);
            Damage(1); now += 6 * HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(firstError, runtime.AttachmentError);
            Assert.AreEqual(2L, scheduler.Snapshot.Faults);
            Assert.AreEqual(8L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(10 * HostFrameClock.FixedStep.Ticks, scheduler.Snapshot.FaultDroppedTicks);
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    public void NewCancellationFaultDuringInputPreparationCannotExecuteTheBudget()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize()); runtime.Start(); runtime.Tick(Input, HostFrameClock.FixedStep);
        runtime.Doze.Next = (min, _) => min;
        for (var i = 0; i < 3000; i++) runtime.Doze.Advance(new(true, 0, 0), .1, true, false, _ => true);
        Assert.IsTrue(runtime.Doze.Active);
        var attachments = typeof(StackedDangoSceneRuntime).GetField("attachments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var router = attachments.GetType().GetField("router", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attachments)!;
        router.GetType().GetMethod("SetPaused")!.Invoke(router, [true]);
        var source = new Source(); long now = 0;
        var scheduler = new HostFrameScheduler(runtime, () => { runtime.CancelInput(); return true; }, () => Input,
            _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        scheduler.Start();
        try
        {
            now = 6 * HostFrameClock.FixedStep.Ticks + 10; source.Raise(now);
            Assert.IsNotNull(runtime.Doze.Error);
            Assert.AreEqual(1L, scheduler.Snapshot.Faults);
            Assert.AreEqual(0L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(0L, scheduler.Snapshot.AppliedFrames);
            Assert.AreEqual(now, scheduler.Snapshot.FaultDroppedTicks);
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    public void ReentrantRestartInsidePhysicsStepStopsTheOldBatchBeforeBinding()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start(); runtime.Tick(Input, HostFrameClock.FixedStep);
        var source = new Source(); long now = 0; var applied = 0;
        var moved = new PhysicsPresentationInput("clock-fixture", new LogicalRect(500, 500, 160, 120),
            DangoActionState.Idle, 1000, 17, "fixture-ground", new(0, -1), 0, 1, false, true, []);
        var scheduler = new HostFrameScheduler(runtime, () => true, () => moved, _ => applied++, source, () => now, TimeSpan.TicksPerSecond);
        void Cancelled() { scheduler.Stop(); scheduler.Start(); }
        runtime.InputCancelled += Cancelled;
        scheduler.Start();
        try
        {
            now = 6 * HostFrameClock.FixedStep.Ticks + 7; source.Raise(now);
            Assert.AreEqual(1L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(0, applied);
            Assert.AreEqual(5 * HostFrameClock.FixedStep.Ticks + 7, scheduler.Snapshot.LifecycleDroppedTicks);
            now += HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(2L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(1, applied);
            now += 13; scheduler.Stop();
            Assert.AreEqual(5 * HostFrameClock.FixedStep.Ticks + 20, scheduler.Snapshot.LifecycleDroppedTicks);
        }
        finally { runtime.InputCancelled -= Cancelled; scheduler.Stop(); }
    }

    [STATestMethod]
    public void ClosingInactiveHostsDoesNotStopOrCancelTheActiveOwner()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start(); runtime.Tick(Input, HostFrameClock.FixedStep);
        var scene = new DesktopSceneSession();
        var window = new Dango.Desktop.DesktopHost.StandardWindow.DangoSceneWindow(scene, runtime);
        var wallpaper = new Dango.Desktop.DesktopHost.WorkerW.WorkerWWindow(scene, runtime);
        var source = new Source(); long now = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond);
        var cancellations = 0; runtime.InputCancelled += () => cancellations++;
        scheduler.Start();
        try
        {
            window.PrepareForExit(); window.Close(); wallpaper.Close();
            Assert.IsTrue(runtime.IsRunning);
            Assert.IsTrue(scheduler.OwnsRuntime);
            Assert.AreEqual(0, cancellations);
            now = HostFrameClock.FixedStep.Ticks; source.Raise(now);
            Assert.AreEqual(1L, scheduler.Snapshot.SuccessfulSteps);
        }
        finally { scheduler.Stop(); }
    }
}
