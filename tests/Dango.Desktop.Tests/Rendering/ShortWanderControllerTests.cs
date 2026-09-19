using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class ShortWanderControllerTests
{
    private static WanderSample Sample(double x = 0, double left = 400, double right = 400) => new(x, 0, true, left, right);
    private static void Steps(ShortWanderController controller, int count, bool busy = false)
    {
        for (var i = 0; i < count; i++) controller.Advance(Sample(), .02, true, busy);
    }

    [TestMethod]
    [DataRow(false, 400)]
    [DataRow(true, 750)]
    public void StableGateAndWaitEndpoints(bool maximum, int wait)
    {
        var draws = 0;
        var c = new ShortWanderController { Next = (min, max) => { draws++; return maximum ? max - 1 : min; } };
        Steps(c, 25); Assert.AreEqual(0, draws);
        Steps(c, 1); Assert.AreEqual(1, draws);
        Steps(c, wait - 1); Assert.IsFalse(c.Active);
        Steps(c, 1); Assert.IsTrue(c.Driving);
        Assert.AreEqual(maximum ? 1 : -1, c.Direction);
        Assert.AreEqual(c.Direction * .035, c.Input(.02), 1e-10);
        Assert.AreEqual(3, draws);
    }

    [TestMethod]
    public void EveryMovementDrawsANewDistanceWithinApprovedRange()
    {
        var distanceDraws = 0;
        var c = new ShortWanderController { Next = (min, max) =>
        {
            if (min == 1000) { Assert.AreEqual(2001, max); return ++distanceDraws == 1 ? min : max - 1; }
            return min;
        } };
        Steps(c, 426); Assert.IsTrue(c.Driving);
        c.Advance(Sample(-119d / 80), .02, true, false); Assert.IsTrue(c.Driving);
        c.Advance(Sample(-128d / 80), .02, true, false); Assert.IsFalse(c.Driving);
        c.Cancel(); Steps(c, 426); Assert.IsTrue(c.Driving);
        c.Advance(Sample(-231d / 80), .02, true, false); Assert.IsTrue(c.Driving);
        c.Advance(Sample(-232d / 80), .02, true, false); Assert.IsFalse(c.Driving);
        Assert.AreEqual(2, distanceDraws);
    }

    [TestMethod]
    public void MovementMustNotFinishBeforeOneTenthOfScreenWidth()
    {
        var c = new ShortWanderController { Next = (min, _) => min };
        Steps(c, 426);
        for (var i = 1; i <= 100; i++)
        {
            c.Advance(Sample(-i * .005), .02, true, false);
            Assert.IsTrue(c.Driving, "Only 40 DIP travelled on a 1200 DIP screen; neither old distance nor two-second cap may finish this movement.");
        }
    }

    [TestMethod]
    public void BusyDropsDeadlineAndDoesNotRetryNextFrame()
    {
        var c = new ShortWanderController { Next = (min, _) => min };
        Steps(c, 426, true); Assert.IsFalse(c.Active);
        Steps(c, 399); Assert.IsFalse(c.Active);
        Steps(c, 1); Assert.IsTrue(c.Active);
    }

    [TestMethod]
    public void EdgeChoosesOnlySafeDirectionThenStopsBeforeReserve()
    {
        var c = new ShortWanderController { Next = (min, _) => min };
        for (var i = 0; i < 426; i++) c.Advance(Sample(left: 10), .02, true, false);
        Assert.AreEqual(1, c.Direction);
        c.Advance(Sample(.02, 10, 31), .02, true, false);
        Assert.IsFalse(c.Driving); Assert.IsTrue(c.Active);
        Assert.AreEqual(0, c.Input(.02));
        Steps(c, 30); Assert.IsFalse(c.Active);
        Steps(c, 300); Assert.IsFalse(c.Active);
    }

    [TestMethod]
    public void ManualAndInvalidSampleCancelAndRequireNewWait()
    {
        var c = new ShortWanderController { Next = (min, _) => min };
        Steps(c, 426); Assert.IsTrue(c.Active);
        c.Advance(Sample(), .02, false, false);
        Assert.IsFalse(c.Active); Assert.AreEqual(0, c.Input(.02));
        Steps(c, 300); Assert.IsFalse(c.Active);
        c.Advance(Sample(double.NaN), .02, true, false);
        Steps(c, 425); Assert.IsFalse(c.Active);
        Steps(c, 1); Assert.IsTrue(c.Active);
        c.Cancel(); c.Cancel(); Assert.IsFalse(c.Active);
    }

    [TestMethod]
    public void NoSafeDirectionDropsRequestAndDistanceBudgetStopsDrive()
    {
        var c = new ShortWanderController { Next = (min, _) => min };
        for (var i = 0; i < 426; i++) c.Advance(Sample(left: 150, right: 150), .02, true, false);
        Assert.IsFalse(c.Active);
        Steps(c, 399); Assert.IsFalse(c.Active);
        Steps(c, 1); Assert.IsTrue(c.Driving);
        for (var i = 1; i < 420; i++) c.Advance(Sample(-.003 * i), .02, true, false);
        Assert.IsTrue(c.Driving);
        c.Advance(Sample(-1.26), .02, true, false);
        Assert.IsFalse(c.Driving); Assert.AreEqual(0, c.Input(.02));
        c.Advance(new(-1.38, 0, false, 200, 200), .02, true, false);
        Assert.IsFalse(c.Active);
    }

    [TestMethod]
    public void RealProgressStopsAtTargetAndStallStopsAtHalfSecond()
    {
        var c = new ShortWanderController { Next = (min, _) => min };
        Steps(c, 426);
        c.Advance(Sample(-1.8), .02, true, false);
        Assert.IsFalse(c.Driving); Assert.IsTrue(c.Active);
        c.Cancel(); Steps(c, 426);
        Steps(c, 24); Assert.IsTrue(c.Driving);
        Steps(c, 1); Assert.IsFalse(c.Driving); Assert.IsTrue(c.Active);
    }
}
