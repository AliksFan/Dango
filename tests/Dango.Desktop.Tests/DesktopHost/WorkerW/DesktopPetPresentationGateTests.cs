using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class DesktopPetPresentationGateTests
{
    [TestMethod]
    public void CachedRefreshLoadedAndDuplicateCallbacksCannotDeclareFreshPresentation()
    {
        var gate = new DesktopPetPresentationGate();
        gate.Begin(7, 20, 30);
        Assert.IsFalse(gate.ObserveApplied(7, 20, 30, true));
        Assert.IsFalse(gate.ObserveApplied(7, 20, 31, true));
        Assert.IsFalse(gate.ObserveApplied(7, 21, 30, true));
        Assert.IsFalse(gate.ConfirmRendered(7, true));
        Assert.IsFalse(gate.Confirm(7));
        Assert.IsFalse(gate.Ready);
        Assert.IsFalse(gate.InputEnabled);
    }

    [TestMethod]
    public void HealthyFreshFrameNeedsLaterRenderAndApplicationConfirmationBeforeInput()
    {
        var gate = new DesktopPetPresentationGate();
        gate.Begin(7, 20, 30);
        Assert.IsFalse(gate.ObserveApplied(7, 21, 31, false));
        Assert.IsTrue(gate.ObserveApplied(7, 22, 32, true));
        Assert.IsFalse(gate.Ready);
        Assert.IsFalse(gate.Confirm(7));
        Assert.IsFalse(gate.ConfirmRendered(7, false));
        Assert.IsTrue(gate.ConfirmRendered(7, true));
        Assert.IsTrue(gate.Ready);
        Assert.IsFalse(gate.InputEnabled);
        Assert.IsTrue(gate.Confirm(7));
        Assert.IsTrue(gate.InputEnabled);
    }

    [TestMethod]
    public void HidingOrStartingAnotherGenerationInvalidatesPendingRenderAndConfirmation()
    {
        var gate = new DesktopPetPresentationGate();
        gate.Begin(7, 20, 30);
        Assert.IsTrue(gate.ObserveApplied(7, 21, 31, true));
        gate.Cancel();
        Assert.IsFalse(gate.ConfirmRendered(7, true));
        Assert.IsFalse(gate.Confirm(7));
        gate.Begin(8, 21, 31);
        Assert.IsFalse(gate.ObserveApplied(7, 22, 32, true));
        Assert.IsFalse(gate.ConfirmRendered(7, true));
        Assert.IsFalse(gate.Confirm(7));
        Assert.IsTrue(gate.ObserveApplied(8, 22, 32, true));
        Assert.IsTrue(gate.ConfirmRendered(8, true));
        Assert.IsFalse(gate.Confirm(7));
        Assert.IsTrue(gate.Confirm(8));
    }

    [TestMethod]
    public void EveryEntryTakesANewCounterBaselineAndReportsReadyOnlyOnce()
    {
        var gate = new DesktopPetPresentationGate();
        gate.Begin(7, 20, 30);
        Assert.IsTrue(gate.ObserveApplied(7, 21, 31, true));
        Assert.IsTrue(gate.ConfirmRendered(7, true));
        Assert.IsFalse(gate.ConfirmRendered(7, true));
        gate.Begin(8, 21, 31);
        Assert.IsFalse(gate.Ready);
        Assert.IsFalse(gate.InputEnabled);
        Assert.IsFalse(gate.ObserveApplied(8, 21, 31, true));
        Assert.IsFalse(gate.ConfirmRendered(8, true));
    }
}
