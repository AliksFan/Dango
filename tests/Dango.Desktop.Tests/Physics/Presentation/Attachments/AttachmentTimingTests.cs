using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class AttachmentTimingTests
{
    [TestMethod]
    public void PhaseBoundariesSplitExactlyAndNoSliceExceedsMaximum()
    {
        var clock = new AttachmentStepClock();
        var slices = clock.Advance(0, 0.1, new[] { 0.013, 0.07, 0.083 });
        Assert.IsTrue(slices.Count >= 24);
        foreach (var boundary in new[] { 0.013, 0.07, 0.083 }) Assert.IsTrue(slices.Any(s => s.End == boundary));
        Assert.AreEqual(0.1, slices.Sum(s => s.Seconds), 1e-12);
        Assert.IsTrue(slices.All(s => s.Seconds > 0 && s.Seconds <= 1.0 / 240 + 1e-12));
        for (var i = 1; i < slices.Count; i++) Assert.AreEqual(slices[i - 1].End, slices[i].Start);
    }

    [TestMethod]
    public void PauseDuplicateStepAndResetDoNotAccumulateHiddenTime()
    {
        var clock = new AttachmentStepClock();
        clock.Advance(10, 0.01);
        Assert.AreEqual(0.01, clock.Elapsed);
        Assert.AreEqual(0, clock.Advance(10, 0.01).Count);
        clock.Paused = true;
        Assert.AreEqual(0, clock.Advance(11, 0.1).Count);
        Assert.AreEqual(0.01, clock.Elapsed);
        clock.Paused = false;
        clock.Advance(100, 0.01);
        Assert.AreEqual(0.02, clock.Elapsed);
        Assert.AreEqual(0, clock.Advance(99, 0.01).Count);
        clock.Reset();
        Assert.AreEqual(0, clock.Elapsed); Assert.AreEqual(-1L, clock.StepIndex);
        Assert.IsTrue(clock.Advance(0, 0.01).Count > 0);
    }

    [TestMethod]
    public void InvalidTimeAndBoundaryLeaveClockUnchanged()
    {
        var clock = new AttachmentStepClock(); clock.Advance(0, 0.01);
        foreach (var bad in new[] { 0, -1, double.NaN, double.PositiveInfinity, 0.101 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => clock.Advance(1, bad));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => clock.Advance(1, 0.01, new[] { double.NaN }));
        Assert.AreEqual(0.01, clock.Elapsed); Assert.AreEqual(0L, clock.StepIndex);
    }

    [TestMethod]
    public void SameTimedTargetAndImpulseAgreeAtThirtySixtyAndOneTwentyHz()
    {
        SpringState Run(int hz)
        {
            var clock = new AttachmentStepClock(); var state = new SpringState(0, 0);
            var fired = false;
            for (var step = 0; step < hz; step++)
                foreach (var slice in clock.Advance(step, 1.0 / hz, new[] { 0.113, 0.337 }))
                {
                    if (!fired && slice.Start >= 0.113 - 1e-12) { state = DampedSpring.Impulse(state, 0.2, 0.3); fired = true; }
                    state = DampedSpring.AdvanceBounded(state, slice.Start < 0.337 - 1e-12 ? 0.03 : -0.02, 3.5, 0.8, slice.Seconds, -0.08, 0.08);
                }
            Assert.IsTrue(fired); Assert.IsTrue(state.Position < -0.019);
            return state;
        }
        var reference = Run(120);
        foreach (var hz in new[] { 30, 60 })
        {
            var actual = Run(hz);
            Assert.AreEqual(reference.Position, actual.Position, 0.002);
            Assert.AreEqual(reference.Velocity, actual.Velocity, 0.002);
        }
    }
}
