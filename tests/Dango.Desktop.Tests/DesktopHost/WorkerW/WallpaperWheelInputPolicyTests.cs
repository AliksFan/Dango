using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WallpaperWheelInputPolicyTests
{
    private static WallpaperWheelInputContext Context => new(
        new WorkerWDesktopTarget(10, 11, 12, 13, true), new LogicalRect(-1920, -100, 1920, 1080), 7, true, false);

    [TestMethod]
    public void ReadyDesktopBackgroundAcceptsDevicePixelWheel()
    {
        Assert.IsTrue(WallpaperWheelInputPolicy.ShouldApply(Context, Context, new WallpaperWheelPoint(-100, 50),
            new WallpaperWheelInputEnvironment(true, true, false, true, false)));
    }

    [TestMethod]
    [DataRow(false, true, false, true, false)]
    [DataRow(true, false, false, true, false)]
    [DataRow(true, true, true, true, false)]
    [DataRow(true, true, false, false, false)]
    [DataRow(true, true, false, true, true)]
    public void UnknownFocusFullscreenOtherWindowAndIconEachReject(bool known, bool foreground,
        bool fullscreen, bool desktopPoint, bool icon)
    {
        Assert.IsFalse(WallpaperWheelInputPolicy.ShouldApply(Context, Context, new WallpaperWheelPoint(-100, 50),
            new WallpaperWheelInputEnvironment(known, foreground, fullscreen, desktopPoint, icon)));
    }

    [TestMethod]
    public void TargetBoundsGenerationAndUnavailableContextsReject()
    {
        var allowed = new WallpaperWheelInputEnvironment(true, true, false, true, false);
        WallpaperWheelInputContext?[] changed = [null, Context with { Generation = 8 }, Context with { IsReady = false },
            Context with { IsBlocked = true }, Context with { Target = Context.Target with { ParentHandle = 99 } },
            Context with { ScreenBounds = new LogicalRect(-1920, 0, 1920, 1080) }];
        foreach (var current in changed)
            Assert.IsFalse(WallpaperWheelInputPolicy.ShouldApply(Context, current, new WallpaperWheelPoint(-100, 50), allowed));
    }

    [TestMethod]
    public void DeviceRectangleIncludesNegativeOriginAndExcludesRightBottomAdjacentPixels()
    {
        Assert.IsTrue(WallpaperWheelInputPolicy.ContainsDevicePoint(Context.ScreenBounds, new WallpaperWheelPoint(-1920, -100)));
        Assert.IsTrue(WallpaperWheelInputPolicy.ContainsDevicePoint(Context.ScreenBounds, new WallpaperWheelPoint(-1, 979)));
        Assert.IsFalse(WallpaperWheelInputPolicy.ContainsDevicePoint(Context.ScreenBounds, new WallpaperWheelPoint(0, 50)));
        Assert.IsFalse(WallpaperWheelInputPolicy.ContainsDevicePoint(Context.ScreenBounds, new WallpaperWheelPoint(-10, 980)));
        Assert.IsFalse(WallpaperWheelInputPolicy.ContainsDevicePoint(Context.ScreenBounds, new WallpaperWheelPoint(-1921, 50)));
    }

    [TestMethod]
    public void FullscreenOnSecondNegativeMonitorBlocksWhileWorkAreaMaximizeDoesNot()
    {
        DesktopPixelRect[] monitors = [new(0, 0, 1920, 1080), new(-1920, -100, 0, 980)];
        var application = new WallpaperWheelWindowState(true, false, false, false, new DesktopPixelRect(-1920, -100, 0, 980));
        Assert.IsTrue(monitors.Any(monitor => WallpaperWheelInputPolicy.IsBlockingFullscreen(application, monitor)));
        Assert.IsFalse(monitors.Any(monitor => WallpaperWheelInputPolicy.IsBlockingFullscreen(
            application with { Bounds = new DesktopPixelRect(-1920, -100, 0, 940) }, monitor)));
    }

    [TestMethod]
    [DataRow(false, false, false, false)]
    [DataRow(true, true, false, false)]
    [DataRow(true, false, true, false)]
    [DataRow(true, false, false, true)]
    public void InvisibleMinimizedCloakedAndShellWindowsDoNotBlock(bool visible, bool minimized, bool cloaked, bool shell)
    {
        var bounds = new DesktopPixelRect(0, 0, 1920, 1080);
        Assert.IsFalse(WallpaperWheelInputPolicy.IsBlockingFullscreen(
            new WallpaperWheelWindowState(visible, minimized, cloaked, shell, bounds), bounds));
    }

    [TestMethod]
    [DataRow(120)]
    [DataRow(-120)]
    [DataRow(60)]
    [DataRow(-60)]
    public void NativeSignedHighWordPreservesRawDelta(int delta)
    {
        Assert.AreEqual(delta, WallpaperWheelInputPolicy.DecodeRawDelta(unchecked((uint)(ushort)(short)delta << 16)));
    }

    [TestMethod]
    public void DesktopClassWhitelistExcludesBothTaskbarsMenusAndApplicationHosts()
    {
        foreach (var className in new[] { "Progman", "WorkerW", "SHELLDLL_DefView", "SysListView32" })
            Assert.IsTrue(WallpaperWheelInputPolicy.IsDesktopShellClass(className));
        foreach (var className in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "HwndWrapper", "" })
            Assert.IsFalse(WallpaperWheelInputPolicy.IsDesktopShellClass(className));
    }

    [TestMethod]
    public void StrictForegroundAllowsLegacyShellViewRootButRejectsUnknownAndRoomChildren()
    {
        var legacy = Context.Target with { ParentHandle = 10, WallpaperLayerHandle = 10, IsRaisedDesktop = false };
        Assert.IsTrue(WallpaperWheelInputPolicy.IsDesktopForeground(legacy, 20, 13, 20, "SHELLDLL_DefView", true));
        Assert.IsTrue(WallpaperWheelInputPolicy.IsDesktopForeground(legacy, 20, 10, 10, "WorkerW", true));
        Assert.IsFalse(WallpaperWheelInputPolicy.IsDesktopForeground(legacy, 20, 0, 20, "Progman", true));
        Assert.IsFalse(WallpaperWheelInputPolicy.IsDesktopForeground(legacy, 20, 99, 20, "HwndWrapper", true));
        Assert.IsFalse(WallpaperWheelInputPolicy.IsDesktopForeground(legacy, 20, 99, 20, "WorkerW", false));
        Assert.IsFalse(WallpaperWheelInputPolicy.IsDesktopForeground(legacy, 20, 99, 30, "WorkerW", true));
    }
}
