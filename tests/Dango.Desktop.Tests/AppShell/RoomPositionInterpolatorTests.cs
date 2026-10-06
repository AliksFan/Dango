using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomPositionInterpolatorTests
{
    [TestMethod]
    public void ConstantMotionRemainsContinuousAcrossSixtyOneTwentyAndOneFortyFourHz()
    {
        var step = HostFrameClock.FixedStep.Ticks;
        foreach (var hz in new[] { 60, 120, 144 })
        {
            var samples = new RoomPositionInterpolator();
            samples.Reset(new(0, 200), 0);
            var applied = 0L;
            var previousX = 0d;
            for (var render = 1; render <= hz * 2; render++)
            {
                var now = (long)Math.Round(render * (double)TimeSpan.TicksPerSecond / hz);
                var successful = now / step;
                if (successful > applied)
                {
                    samples.Add(new(successful * 8, 200 + successful * 2), successful * step);
                    applied = successful;
                }
                var time = Math.Max(0, now - step);
                var displayed = samples.At(time);
                Assert.AreEqual(time / (double)step * 8, displayed.X, 1e-8,
                    $"{hz}Hz must use simulation time, including renders with no physics step.");
                Assert.AreEqual(200 + time / (double)step * 2, displayed.Y, 1e-8);
                Assert.IsTrue(displayed.X >= previousX);
                // An ideal same-speed camera cannot introduce the alternating backward sawtooth.
                Assert.AreEqual(0, displayed.X - time / (double)step * 8, 1e-8);
                previousX = displayed.X;
            }
        }
    }

    [TestMethod]
    public void BatchedStepsUseTheActualSampleSpanAndNeverExtrapolate()
    {
        var step = HostFrameClock.FixedStep.Ticks;
        var samples = new RoomPositionInterpolator();
        samples.Add(new(0, 100), step);
        samples.Add(new(48, 112), step * 7);
        Assert.AreEqual(new LogicalPoint(40, 110), samples.At(step * 6));
        Assert.AreEqual(new LogicalPoint(44, 111), samples.At(step * 6 + step / 2));
        Assert.AreEqual(new LogicalPoint(0, 100), samples.At(0));
        Assert.AreEqual(new LogicalPoint(48, 112), samples.At(step * 100));
    }

    [TestMethod]
    public void ResetAndRehostDiscardOldMotionInsteadOfReplayingIt()
    {
        var samples = new RoomPositionInterpolator();
        samples.Add(new(0, 100), 0);
        samples.Add(new(500, 200), 1000);
        samples.Reset(new(700, 150), 2000);
        Assert.AreEqual(new LogicalPoint(700, 150), samples.At(1900));
        samples.Add(new(50, 100), 1);
        Assert.AreEqual(new LogicalPoint(50, 100), samples.At(0));
        samples.Clear();
        Assert.ThrowsExactly<InvalidOperationException>(() => samples.At(0));
    }
}
