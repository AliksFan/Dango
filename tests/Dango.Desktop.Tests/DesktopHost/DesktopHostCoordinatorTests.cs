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
        var wallpaper = new FakeHost("Wallpaper", isVisible: true, isInteractionEnabled: false);
        var window = new FakeHost("Window", isVisible: false, isInteractionEnabled: true);
        var coordinator = new DesktopHostCoordinator(wallpaper, window);

        coordinator.SetMode(DesktopDisplayMode.Window);

        var snapshot = coordinator.GetSnapshot();
        Assert.AreEqual("Window", snapshot.Mode);
        Assert.IsTrue(snapshot.IsVisible);
        Assert.IsFalse(snapshot.IsInteractionEnabled);
        Assert.IsFalse(wallpaper.GetSnapshot().IsVisible);
    }

    [TestMethod]
    public void FailedWindowActivationRollsBackToVisibleWallpaper()
    {
        var wallpaper = new FakeHost("Wallpaper", isVisible: true, isInteractionEnabled: true);
        var window = new FakeHost("Window", isVisible: false, isInteractionEnabled: true)
        {
            RefuseShow = true,
        };
        var coordinator = new DesktopHostCoordinator(wallpaper, window);

        var exception = Assert.ThrowsExactly<DesktopHostSwitchException>(
            () => coordinator.SetMode(DesktopDisplayMode.Window));

        Assert.AreEqual("目标宿主未确认切换后的显示或交互状态。", exception.SafeReason);
        Assert.AreEqual("Wallpaper", coordinator.GetSnapshot().Mode);
        Assert.IsTrue(wallpaper.GetSnapshot().IsVisible);
        Assert.IsFalse(window.GetSnapshot().IsVisible);
    }

    [TestMethod]
    public void ActiveDragBlocksSwitchBeforeEitherSurfaceChanges()
    {
        var wallpaper = new FakeHost("Wallpaper", isVisible: true, isInteractionEnabled: true)
        {
            SwitchBlockReason = "团子拖动结束后才能切换显示模式。",
        };
        var window = new FakeHost("Window", isVisible: false, isInteractionEnabled: true);
        var coordinator = new DesktopHostCoordinator(wallpaper, window);

        var exception = Assert.ThrowsExactly<DesktopHostSwitchException>(
            () => coordinator.SetMode(DesktopDisplayMode.Window));

        Assert.AreEqual("团子拖动结束后才能切换显示模式。", exception.SafeReason);
        Assert.IsTrue(wallpaper.GetSnapshot().IsVisible);
        Assert.IsFalse(window.GetSnapshot().IsVisible);
    }

    private sealed class FakeHost : IDesktopHostControl, IDesktopHostSwitchGuard
    {
        private DesktopHostSnapshot snapshot;

        public FakeHost(string mode, bool isVisible, bool isInteractionEnabled)
        {
            snapshot = new DesktopHostSnapshot(mode, isVisible, isInteractionEnabled, "测试", null);
        }

        public bool RefuseShow { get; init; }

        public string? SwitchBlockReason { get; init; }

        public string? GetSwitchBlockReason() => SwitchBlockReason;

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
