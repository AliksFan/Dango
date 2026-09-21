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
public sealed class HostFrameStageTimingTests
{
    private static readonly PhysicsPresentationInput Input = new("stage-fixture", new LogicalRect(400, 500, 160, 120),
        DangoActionState.Idle, 1000, 17, "fixture-ground", new(0, -1), 0, 1, false, true, []);

    private sealed class Source : IHostFrameSource
    {
        private EventHandler? handlers;
        public event EventHandler Rendering { add => handlers += value; remove => handlers -= value; }
        internal static EventArgs Args(long renderTicks) => (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [TimeSpan.FromTicks(renderTicks)], null)!;
        internal void Raise(long renderTicks) => Raise(Args(renderTicks));
        internal void Raise(EventArgs args) => handlers?.Invoke(this, args);
    }

    [TestMethod]
    public void DisabledObservationKeepsTheOriginalTwoClockReadsAndAllocatesNothingPerCallback()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        var source = new Source(); long now = 1; var clockReads = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source,
            () => { clockReads++; return now; }, TimeSpan.TicksPerSecond);
        Assert.IsFalse(scheduler.DetailedTimingEnabled);
        scheduler.Start();
        try
        {
            now = HostFrameClock.FixedStep.Ticks + 1; source.Raise(1);
            now = 3 * HostFrameClock.FixedStep.Ticks + 1; source.Raise(2);
            Assert.AreEqual(5, clockReads, "Start plus two original reads per callback, including multi-step and apply paths.");
            Assert.AreEqual(3L, scheduler.Snapshot.SuccessfulSteps);
            var args = Source.Args(2);
            for (var i = 0; i < 32; i++) source.Raise(args);
            var beforeReads = clockReads;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 512; i++) source.Raise(args);
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.AreEqual(1024, clockReads - beforeReads);
            Assert.AreEqual(0L, allocated);
            Assert.IsNull(scheduler.LastStageTiming);
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    public void EnablingObservationPreservesCountersPhysicsAndAppliedFrames()
    {
        (HostFrameDiagnostics Diagnostics, string Physics, int Applied) Run(bool observed)
        {
            using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
            var source = new Source(); long now = 0; var applied = 0;
            var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => applied++, source, () => now, TimeSpan.TicksPerSecond)
            { DetailedTimingEnabled = observed };
            scheduler.Start();
            try
            {
                foreach (var instant in new[] { 1L, HostFrameClock.FixedStep.Ticks, 3 * HostFrameClock.FixedStep.Ticks, 3 * HostFrameClock.FixedStep.Ticks, 12 * HostFrameClock.FixedStep.Ticks, 2 * TimeSpan.TicksPerSecond })
                { now = instant; source.Raise(now); }
                return (scheduler.Snapshot, JsonSerializer.Serialize(runtime.LastPhysicsFrame), applied);
            }
            finally { scheduler.Stop(); }
        }
        var plain = Run(false); var observed = Run(true);
        Assert.AreEqual(plain.Diagnostics, observed.Diagnostics);
        Assert.AreEqual(plain.Physics, observed.Physics);
        Assert.AreEqual(plain.Applied, observed.Applied);
        Assert.IsTrue(plain.Applied > 0);
        Assert.AreEqual(1L, observed.Diagnostics.DuplicateCallbacks);
        Assert.AreEqual(1L, observed.Diagnostics.Overloads);
        Assert.AreEqual(1L, observed.Diagnostics.LongPauses);
    }

    [TestMethod]
    public void StageBoundariesDescribeZeroOneMultipleAndDuplicateCallbacks()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        var source = new Source(); long now = 100; var ready = true;
        var scheduler = new HostFrameScheduler(runtime, () => { now += 2; return ready; }, () => { now += 3; return Input; },
            _ => now += 5, source, () => now, TimeSpan.TicksPerSecond)
        { DetailedTimingEnabled = true };
        HostFrameStageTiming? delivered = null;
        scheduler.CallbackCompleted += (_, _) => delivered = scheduler.LastStageTiming;
        scheduler.Start();
        try
        {
            now = 100 + HostFrameClock.FixedStep.Ticks / 2; source.Raise(1);
            var zero = delivered!.Value;
            Assert.AreEqual(1L, zero.RenderingTimeTicks); Assert.AreEqual(100L, zero.SchedulerStartedTimestamp);
            Assert.AreEqual(0, zero.RequestedSteps); Assert.AreEqual(0, zero.SuccessfulSteps);
            Assert.AreEqual(2L, zero.PrepareFinishedTimestamp - zero.PrepareStartedTimestamp);
            Assert.IsNull(zero.LoopStartedTimestamp); Assert.IsNull(zero.ApplyStartedTimestamp);
            Assert.IsNull(zero.PreviousFinishedTimestamp);
            Assert.AreEqual(HostFrameClock.FixedStep.Ticks / 2, zero.RemainderAfterTicks);
            now = 100 + HostFrameClock.FixedStep.Ticks; source.Raise(2);
            var one = delivered!.Value;
            Assert.AreEqual(zero.FinishedTimestamp, one.PreviousFinishedTimestamp);
            Assert.AreEqual(zero.RemainderAfterTicks, one.RemainderBeforeTicks);
            Assert.AreEqual(1, one.RequestedSteps); Assert.AreEqual(1, one.SuccessfulSteps);
            Assert.AreEqual(3L, one.LoopFinishedTimestamp - one.LoopStartedTimestamp);
            Assert.AreEqual(5L, one.ApplyFinishedTimestamp - one.ApplyStartedTimestamp);
            Assert.AreEqual(one.StartedTimestamp + 10, one.FinishedTimestamp);
            Assert.AreEqual(0L, one.RemainderAfterTicks); Assert.IsFalse(one.Refresh);
            now = 100 + 3 * HostFrameClock.FixedStep.Ticks; source.Raise(3);
            Assert.AreEqual(2, delivered!.Value.RequestedSteps); Assert.AreEqual(2, delivered.Value.SuccessfulSteps);
            Assert.AreEqual(2L, scheduler.Snapshot.AppliedFrames);
            now++; source.Raise(3);
            Assert.IsNull(delivered!.Value.PrepareStartedTimestamp);
            Assert.IsNull(delivered.Value.LoopStartedTimestamp); Assert.IsNull(delivered.Value.ApplyStartedTimestamp);
            Assert.AreEqual(0, delivered.Value.RequestedSteps);
            ready = false; now++; source.Raise(4);
            Assert.IsNotNull(delivered!.Value.PrepareFinishedTimestamp); Assert.IsNull(delivered.Value.LoopStartedTimestamp);
        }
        finally { scheduler.Stop(); }
    }

    [TestMethod]
    public void FailedPhysicsAndThrownStagesRemainFaultEvidenceRatherThanSuccessfulFrames()
    {
        using var runtime = new StackedDangoSceneRuntime("fictional-missing-stage-module.dll"); runtime.Start();
        var source = new Source(); long now = 0;
        var scheduler = new HostFrameScheduler(runtime, () => true, () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond)
        { DetailedTimingEnabled = true };
        scheduler.Start();
        try
        {
            now = 6 * HostFrameClock.FixedStep.Ticks + 7; source.Raise(1);
            var timing = scheduler.LastStageTiming!.Value;
            Assert.AreEqual(6, timing.RequestedSteps); Assert.AreEqual(0, timing.SuccessfulSteps);
            Assert.IsNotNull(timing.LoopStartedTimestamp); Assert.IsNotNull(timing.LoopFinishedTimestamp);
            Assert.AreEqual(1L, scheduler.Snapshot.Faults); Assert.AreEqual(0L, scheduler.Snapshot.AppliedFrames);
            Assert.AreEqual(0L, timing.RemainderAfterTicks);
        }
        finally { scheduler.Stop(); }

        using var secondRuntime = new StackedDangoSceneRuntime(); secondRuntime.Start();
        var secondSource = new Source();
        var throwing = new HostFrameScheduler(secondRuntime, () => { now += 3; throw new InvalidOperationException("fixture"); },
            () => Input, _ => { }, secondSource, () => now, TimeSpan.TicksPerSecond)
        { DetailedTimingEnabled = true };
        throwing.Start();
        try
        {
            now++; Assert.ThrowsExactly<InvalidOperationException>(() => secondSource.Raise(1));
            var timing = throwing.LastStageTiming!.Value;
            Assert.IsTrue(timing.Threw); Assert.AreEqual(3L, timing.PrepareFinishedTimestamp - timing.PrepareStartedTimestamp);
            Assert.IsNull(timing.LoopStartedTimestamp); Assert.IsNull(timing.ApplyStartedTimestamp);
            Assert.AreEqual(0L, throwing.Snapshot.AppliedFrames);
        }
        finally { throwing.Stop(); }
    }

    [TestMethod]
    public void ReentrantRestartDoesNotAttributeTheNewRemainderOrPreviousExitToTheOldRun()
    {
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        var source = new Source(); long now = 0; var restart = true;
        HostFrameScheduler scheduler = null!;
        scheduler = new(runtime, () => { if (restart) { restart = false; scheduler.Stop(); scheduler.Start(); } return true; },
            () => Input, _ => { }, source, () => now, TimeSpan.TicksPerSecond)
        { DetailedTimingEnabled = true };
        scheduler.Start();
        try
        {
            now = HostFrameClock.FixedStep.Ticks / 2; source.Raise(1);
            var old = scheduler.LastStageTiming!.Value;
            Assert.IsFalse(old.SameGenerationAtExit); Assert.IsNull(old.RemainderAfterTicks);
            now++; source.Raise(2);
            var next = scheduler.LastStageTiming!.Value;
            Assert.IsTrue(next.Generation > old.Generation); Assert.IsNull(next.PreviousFinishedTimestamp);
            Assert.AreEqual(now - 1, next.SchedulerStartedTimestamp);
            Assert.AreEqual(1L, next.RemainderAfterTicks);
        }
        finally { scheduler.Stop(); }
    }
}
