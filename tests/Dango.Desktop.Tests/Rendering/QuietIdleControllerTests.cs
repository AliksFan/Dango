using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class QuietIdleControllerTests
{
    private sealed class Fixture(bool maximum = false)
    {
        internal readonly List<(int Min, int Max)> Draws = [];
        internal readonly List<QuietIdleAction> Requests = [];
        internal readonly List<QuietIdleAction> Releases = [];
        internal bool Accept = true;
        internal QuietIdleController Controller = null!;
        internal Fixture Initialize()
        {
            Controller = new((min, max) => { Draws.Add((min, max)); return maximum ? max - 1 : min; });
            return this;
        }
        internal void Steps(int count, bool eligible = true, QuietIdleSample? sample = null)
        {
            for (var i = 0; i < count; i++)
            {
                Assert.IsTrue(Controller.BeforeStep(sample ?? new(true, 0, 0), .02, eligible,
                    action => { Requests.Add(action); return Accept; },
                    action => { Releases.Add(action); return true; }));
                Controller.AfterStep(.02);
            }
        }
    }

    [TestMethod]
    [DataRow(false, 200, 750)]
    [DataRow(true, 400, 1250)]
    public void StableGateAndRandomEndpointsAreNotRedrawnEveryStep(bool maximum, int blinkSteps, int lookSteps)
    {
        var f = new Fixture(maximum).Initialize();
        f.Steps(25);
        Assert.HasCount(0, f.Draws);
        f.Steps(1);
        Assert.HasCount(2, f.Draws);
        f.Steps(blinkSteps - 1);
        Assert.HasCount(0, f.Requests);
        f.Steps(1);
        CollectionAssert.AreEqual(new[] { QuietIdleAction.Blink }, f.Requests);
        f.Steps(lookSteps - blinkSteps);
        Assert.AreEqual(maximum ? QuietIdleAction.LookRight : QuietIdleAction.LookLeft, f.Requests.Last());
        Assert.IsTrue(f.Draws.Count < 12);
    }

    [TestMethod]
    public void LookHoldsOneSecondAndOnlyThenReleases()
    {
        var f = new Fixture().Initialize();
        f.Steps(26 + 750);
        Assert.AreEqual(QuietIdleAction.LookLeft, f.Controller.Active);
        f.Steps(49);
        Assert.HasCount(0, f.Releases);
        f.Steps(1);
        CollectionAssert.AreEqual(new[] { QuietIdleAction.LookLeft }, f.Releases);
        Assert.AreEqual(QuietIdleAction.None, f.Controller.Active);
    }

    [TestMethod]
    public void BusyIsDroppedAndRescheduledWithoutPerFrameRetries()
    {
        var f = new Fixture().Initialize(); f.Accept = false;
        f.Steps(26 + 200);
        Assert.HasCount(1, f.Requests);
        f.Steps(199);
        Assert.HasCount(1, f.Requests);
        f.Steps(1);
        Assert.HasCount(2, f.Requests);
        Assert.AreEqual(QuietIdleAction.None, f.Controller.Active);
    }

    [TestMethod]
    public void IneligibleOrMovingBodyCancelsAndStartsAWholeNewWait()
    {
        var f = new Fixture().Initialize();
        f.Steps(26 + 750);
        f.Steps(1, sample: new(true, 1, 0));
        Assert.AreEqual(QuietIdleAction.None, f.Controller.Active);
        Assert.HasCount(1, f.Releases);
        var requests = f.Requests.Count;
        f.Steps(100, eligible: false);
        f.Steps(25 + 199);
        Assert.HasCount(requests, f.Requests);
        f.Steps(1);
        Assert.HasCount(requests + 1, f.Requests);
    }

    [TestMethod]
    public void CancelFailureReturnsFalseAndStillDropsLocalDeadlines()
    {
        var f = new Fixture().Initialize(); f.Steps(26 + 750);
        Assert.IsFalse(f.Controller.Cancel(_ => false));
        Assert.AreEqual(QuietIdleAction.None, f.Controller.Active);
        Assert.IsTrue(f.Controller.Cancel(_ => throw new AssertFailedException("Repeated cancel submitted twice")));
    }

    [TestMethod]
    public void SpeedBoundaryQualifiesButExcessAndNonFiniteSamplesRestartStability()
    {
        var f = new Fixture().Initialize();
        for (var i = 0; i <= 25; i++) f.Steps(1, sample: new(true, i % 2 == 0 ? 0 : .003, 0));
        Assert.HasCount(2, f.Draws); // .003 / .02 is exactly the allowed .15 speed.
        f.Steps(1, sample: new(true, .0061, 0));
        f.Steps(1, sample: new(true, double.NaN, 0));
        f.Steps(25);
        Assert.HasCount(2, f.Draws);
        f.Steps(1);
        Assert.HasCount(4, f.Draws);
    }

    [TestMethod]
    public void SimultaneousDeadlinesChooseLookAndDropBlink()
    {
        var f = new Fixture().Initialize();
        // 4-second blink deadlines and a 16-second gaze meet at 16 seconds.
        f.Controller = new((min, _) => min == 15000 ? 16000 : min);
        f.Steps(26 + 800);
        Assert.HasCount(4, f.Requests);
        Assert.AreEqual(QuietIdleAction.LookLeft, f.Requests.Last());
        f.Steps(1);
        Assert.HasCount(4, f.Requests);
    }
}
