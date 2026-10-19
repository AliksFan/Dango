using Dango.PhysicsLab;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class LabWindowCadenceTests
{
    private const double FrameMilliseconds = 1000d / 60;

    [TestMethod]
    public void ExactlyFiftySevenHertzKeepsFiftySevenInLeftOpenWindows()
    {
        var frames = Enumerable.Range(0, 600).Select(index => new LabWindowCadenceFrame(
            index * (1000d / 57), 1000d / 57, index, 1, true, 0)).ToArray();
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsTrue(result.Passed);
        Assert.AreEqual(57, result.MinimumRolling1sCallbacks);
        Assert.AreEqual(57, result.MinimumRolling1sAdvancedPhysicsFrames);
    }

    [TestMethod]
    public void ExpiryBeforeTheNextCallbackCatchesAWindowThatArrivalOnlyChecksMiss()
    {
        // A 0.1 ms delay followed by recovery leaves arrival-time windows at 57,
        // but expiry at 1000ms after the previous callback temporarily leaves 56.
        var frames = Enumerable.Range(0, 600).Select(index => new LabWindowCadenceFrame(
            index * (1000d / 57) + (index == 400 ? .1 : 0),
            1000d / 57 + (index == 400 ? .1 : index == 401 ? -.1 : 0), index, 1, true, 0)).ToArray();
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual(56, result.MinimumRolling1sCallbacks);
        Assert.IsTrue(result.CallbackIntervalP95Milliseconds <= 1000d / 57);
    }

    [TestMethod]
    public void SixtyHertzCallbacksWithNewPhysicsTicksPass()
    {
        var result = LabWindowCadence.Summarize(Frames(600), 0);
        Assert.IsTrue(result.Passed);
        Assert.AreEqual(300, result.WarmupRestSteps);
        Assert.AreEqual(301, result.Samples);
        Assert.IsTrue(result.MinimumRolling1sCallbacks >= 59);
        Assert.IsTrue(result.MinimumRolling1sAdvancedPhysicsFrames >= 59);
        Assert.AreEqual(0d, result.DiscardedSeconds);
    }

    [TestMethod]
    public void FreshCallbacksShowingTheSameOldPhysicsTickFail()
    {
        var frames = Frames(600).Select(f => f.Step < 300 ? f : f with { Step = 300, StepsSinceCallback = 0 }).ToArray();
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.MinimumRolling1sCallbacks >= 59);
        Assert.AreEqual(0, result.MinimumRolling1sAdvancedPhysicsFrames);
    }

    [TestMethod]
    public void FourPhysicsStepsBatchedEveryFourthCallbackFailDespiteSixtyTicksPerSecond()
    {
        var frames = Frames(600).Select((f, index) => f with
        {
            Step = index / 4 * 4,
            StepsSinceCallback = index % 4 == 0 ? 4 : 0,
        }).ToArray();
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.MinimumRolling1sCallbacks >= 59);
        Assert.IsTrue(result.MinimumRolling1sAdvancedPhysicsFrames is >= 14 and <= 16);
    }

    [TestMethod]
    public void HighAverageCadenceCannotHideOneLongPause()
    {
        var frames = Enumerable.Range(0, 1501).Select(index => new LabWindowCadenceFrame(
            index * 10d + (index >= 800 ? 500 : 0), index == 800 ? 510 : 10,
            index, 1, true, 0)).ToArray();
        Assert.IsTrue((frames.Length - 1) * 1000d / (frames[^1].ElapsedMilliseconds - frames[0].ElapsedMilliseconds) > 57);
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.CallbackIntervalP95Milliseconds <= 1000d / 57);
        Assert.IsTrue(result.MinimumRolling1sCallbacks < 57);
    }

    [TestMethod]
    public void AGapLongerThanOneSecondHasAnEmptyRollingWindowBeforeResumption()
    {
        var frames = Enumerable.Range(0, 1001).Select(index => new LabWindowCadenceFrame(
            index * 10d + (index >= 400 ? 2000 : 0), index == 400 ? 2010 : 10,
            index, 1, true, 0)).ToArray();
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual(0, result.MinimumRolling1sCallbacks);
        Assert.AreEqual(0, result.MinimumRolling1sAdvancedPhysicsFrames);
    }

    [TestMethod]
    public void DiscardedSimulationBacklogFailsAnOtherwiseSmoothRun()
    {
        var frames = Frames(600).Select(f => f.Step >= 450 ? f with { DiscardedSeconds = .001 } : f).ToArray();
        var result = LabWindowCadence.Summarize(frames, 0);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual(.001, result.DiscardedSeconds);
        Assert.IsTrue(result.MinimumRolling1sAdvancedPhysicsFrames >= 59);
    }

    [TestMethod]
    public void ThreeHundredRestStepsAreExcludedRelativeToTheCapturedStartStep()
    {
        const int startStep = 17;
        var frames = Frames(600).Select(f => f with
        {
            Step = f.Step + startStep,
            IntervalMilliseconds = f.Step < 300 ? 1000 : f.IntervalMilliseconds,
            StepsSinceCallback = f.Step < 300 ? 0 : 1,
        }).ToArray();
        var result = LabWindowCadence.Summarize(frames, startStep);
        Assert.IsTrue(result.Passed);
        Assert.AreEqual(301, result.Samples);
        Assert.AreEqual(FrameMilliseconds, result.CallbackIntervalP95Milliseconds);
    }

    [TestMethod]
    public void LessThanOneCompleteSecondAfterWarmupCannotPass()
    {
        foreach (var samples in new[] { Array.Empty<LabWindowCadenceFrame>(), Frames(299), Frames(359) })
        {
            var result = LabWindowCadence.Summarize(samples, 0);
            Assert.IsFalse(result.Passed);
            Assert.AreEqual(0, result.MinimumRolling1sCallbacks);
            Assert.AreEqual(0, result.MinimumRolling1sAdvancedPhysicsFrames);
        }
    }

    private static LabWindowCadenceFrame[] Frames(int finalStep) => Enumerable.Range(0, finalStep + 1)
        .Select(step => new LabWindowCadenceFrame(step * FrameMilliseconds, FrameMilliseconds, step, 1, true, 0)).ToArray();
}
