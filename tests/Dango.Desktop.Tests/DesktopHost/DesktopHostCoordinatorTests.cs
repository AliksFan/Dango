using Dango.Desktop.AppShell;
using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class DesktopHostCoordinatorTests
{
    [TestMethod]
    public void SwitchingHostPreservesConfirmedVisibilityAndInteraction()
    {
        var overlay = new FakeHost("Overlay", isVisible: true, isInteractionEnabled: false);
        var workerW = new FakeHost("WorkerW", isVisible: false, isInteractionEnabled: true);
        var coordinator = new DesktopHostCoordinator(overlay, workerW);

        coordinator.SetMode(DesktopHostMode.WorkerW);

        var snapshot = coordinator.GetSnapshot();
        Assert.AreEqual("WorkerW", snapshot.Mode);
        Assert.IsTrue(snapshot.IsVisible);
        Assert.IsFalse(snapshot.IsInteractionEnabled);
        Assert.IsFalse(overlay.GetSnapshot().IsVisible);
    }

    [TestMethod]
    public void FailedWorkerWActivationRollsBackToVisibleOverlay()
    {
        var overlay = new FakeHost("Overlay", isVisible: true, isInteractionEnabled: true);
        var workerW = new FakeHost("WorkerW", isVisible: false, isInteractionEnabled: true)
        {
            RefuseShow = true,
        };
        var coordinator = new DesktopHostCoordinator(overlay, workerW);

        var exception = Assert.ThrowsExactly<DesktopHostSwitchException>(
            () => coordinator.SetMode(DesktopHostMode.WorkerW));

        Assert.AreEqual("目标宿主未确认切换后的显示或交互状态。", exception.SafeReason);
        Assert.AreEqual("Overlay", coordinator.GetSnapshot().Mode);
        Assert.IsTrue(overlay.GetSnapshot().IsVisible);
        Assert.IsFalse(workerW.GetSnapshot().IsVisible);
    }

    private sealed class FakeHost : IDesktopHostControl
    {
        private DesktopHostSnapshot snapshot;

        public FakeHost(string mode, bool isVisible, bool isInteractionEnabled)
        {
            snapshot = new DesktopHostSnapshot(mode, isVisible, isInteractionEnabled, "测试", null);
        }

        public bool RefuseShow { get; init; }

        public DesktopHostSnapshot GetSnapshot() => snapshot;

        public void SetVisibility(bool isVisible)
        {
            if (!(isVisible && RefuseShow))
            {
                snapshot = snapshot with { IsVisible = isVisible };
            }
        }

        public void SetInteractionEnabled(bool isEnabled) =>
            snapshot = snapshot with { IsInteractionEnabled = isEnabled };

        public string CreateDiagnosticSummary() => snapshot.Mode;
    }
}
