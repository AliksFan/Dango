using Dango.Desktop.Rendering.Visuals;
using MotionSample = Dango.Desktop.Rendering.Visuals.GaitSample;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class GaitControllerTests
{
    private static MotionSample Sample(double x, int direction = 0) => new(x, 0, true, 100, 100, 8000, direction);
    private static GaitController Ready(double step = .02)
    {
        var controller = new GaitController();
        for (var i = 0; i < 1 / step; i++) controller.Advance(Sample(0), step);
        return controller;
    }

    [TestMethod]
    [DataRow(.01)] [DataRow(.02)] [DataRow(.04)]
    public void RealProgressProducesCompressionExtensionAndStopsWithoutAnotherStep(double step)
    {
        var controller = Ready(step);
        var values = new List<double>();
        double x = 0;
        for (var i = 0; i < 6 / step; i++) values.Add(controller.Advance(Sample(x += 40 * step, 1), step));
        Assert.IsTrue(values.Min() < -.005, "Real walking must visibly compress; identity is not implementation.");
        Assert.IsTrue(values.Max() > .003, "Real walking must also extend.");
        var last = Math.Abs(controller.Strain);
        var phase = controller.Phase;
        for (var i = 0; i < .4 / step; i++)
        {
            var next = Math.Abs(controller.Advance(Sample(x += 5 * step), step));
            Assert.IsTrue(next <= last + 1e-10, "Release must monotonically recover the displayed strain.");
            Assert.AreEqual(phase, controller.Phase, 1e-12);
            last = next;
        }
        Assert.AreEqual(0, controller.Strain, 1e-9);
    }

    [TestMethod]
    public void HeldKeysWithoutProgressAndUnownedMotionNeverStartGait()
    {
        var controller = Ready();
        for (var i = 0; i < 100; i++) Assert.AreEqual(0, controller.Advance(Sample(0, 1), .02));
        for (var i = 0; i < 100; i++) Assert.AreEqual(0, controller.Advance(Sample(i), .02));
        Assert.AreEqual(0, controller.Phase);
    }

    [TestMethod]
    public void UnsafeStateAndResetCannotRetainGaitOrCatchUpHiddenDistance()
    {
        var controller = Ready();
        for (var i = 0; i < 40; i++) controller.Advance(Sample(i * .8, 1), .02);
        Assert.IsTrue(Math.Abs(controller.Strain) > .001);
        Assert.AreEqual(0, controller.Advance(Sample(32, 1) with { Unsafe = true }, .02));
        controller.InvalidateSampling();
        Assert.AreEqual(0, controller.Advance(Sample(900, 1), .02));
        controller.Fail();
        Assert.IsNotNull(controller.Error);
        Assert.AreEqual(0, controller.Advance(Sample(901, 1), .02));
        controller.Reset();
        Assert.IsNull(controller.Error);
    }

    [TestMethod]
    [DataRow(.01)] [DataRow(.02)] [DataRow(.04)]
    public void DistanceClockIsStepInvariantCappedAndNeverCatchesUpAfterStopping(double step)
    {
        var controller = Ready(step);
        controller.Advance(Sample(0, 1), step); // New owner baseline.
        double x = 0;
        for (var i = 0; i < 1 / step; i++) controller.Advance(Sample(x += 30 * step, 1), step);
        Assert.AreEqual(.5, controller.Phase, 1e-8);
        var before = controller.Phase;
        controller.Advance(Sample(x += 1000, 1), step);
        Assert.AreEqual((before + step * 2) % 1, controller.Phase, 1e-8);
        before = controller.Phase;
        for (var i = 0; i < 1 / step; i++) controller.Advance(Sample(x, 1), step);
        Assert.AreEqual(before, controller.Phase);
        Assert.AreEqual(0, controller.Strain);
    }

    [TestMethod]
    [DataRow(.01)] [DataRow(.02)] [DataRow(.04)]
    public void RapidRestartReverseAndFastReleaseKeepOrdinaryAddedMotionContinuous(double step)
    {
        var controller = Ready(step);
        double x = 0, old = 0;
        for (var i = 0; i < 4 / step; i++)
        {
            var time = i * step;
            var direction = time < 1 ? 1 : time < 1.08 ? 0 : time < 2 ? 1 : -1;
            // Old positive inertia must not be counted as negative-direction walking.
            var delta = time is >= 2 and < 2.12 ? 40 : direction * 40;
            var beforePhase = controller.Phase;
            var current = controller.Advance(Sample(x += delta * step, direction), step);
            if (time is >= 2 and < 2.12) Assert.AreEqual(beforePhase, controller.Phase);
            Assert.IsTrue(Math.Abs(current - old) * 160 < .75, "Fixed geometry at 160 DIP must not jump.");
            old = current;
        }
        var phase = controller.Phase;
        for (var i = 0; i < Math.Ceiling(.12 / step); i++)
            controller.Advance(Sample(x, -1) with { FastRelease = true }, step);
        Assert.AreEqual(phase, controller.Phase);
        Assert.AreEqual(0, controller.Strain, 1e-9);
    }

    [TestMethod]
    public void StableReferencePressureVerticalMotionAndOwnerHandoverHaveDistinctBoundaries()
    {
        var cold = new GaitController();
        for (var i = 0; i < 100; i++) Assert.AreEqual(0, cold.Advance(Sample(i, 1), .02));
        var controller = Ready();
        for (var i = 0; i < 40; i++) controller.Advance(Sample(i * .8, 1), .02);
        Assert.IsTrue(Math.Abs(controller.Strain) > .001);
        var phase = controller.Phase;
        var old = controller.Strain;
        controller.Advance(Sample(32, 1) with { Autonomous = true }, .02);
        Assert.AreEqual(phase, controller.Phase);
        Assert.IsTrue(Math.Abs(controller.Strain) < Math.Abs(old));
        Assert.AreEqual(0, controller.Advance(Sample(32.8, 1) with { Height = 91 }, .02));
        for (var i = 0; i < 200; i++) controller.Advance(Sample(40, 0) with { Height = 91 }, .02);
        Assert.AreEqual(0, controller.Advance(Sample(41, 1) with { Height = 91 }, .02), "Compression cannot become the neutral reference.");
        Assert.AreEqual(0, controller.Advance(Sample(42, 1) with { Y = 10 }, .02));
        for (var i = 0; i < 100; i++) controller.Advance(Sample(43 + i * .8, 1), .02);
        Assert.IsTrue(Math.Abs(controller.Strain) > .001, "Stable motion can reenter after safety suppression.");
        controller.Fail(); controller.ClearMotion();
        Assert.IsNotNull(controller.Error, "Entity changes cannot clear a gait fault.");
    }
}
