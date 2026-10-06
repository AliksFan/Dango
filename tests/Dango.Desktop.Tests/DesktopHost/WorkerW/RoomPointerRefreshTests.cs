using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class RoomPointerRefreshTests
{
    private static readonly LogicalRect Screen = new(-1920, -100, 1920, 1080);
    private static readonly WallpaperWheelPoint Previous = new(-100, 50);

    [TestMethod]
    public void CursorMovingOnePixelOnSafeDesktopBackgroundRemainsQualified()
    {
        var refreshedPoint = new WallpaperWheelPoint(Previous.X + 1, Previous.Y);
        var calls = 0;
        var result = NativeMethods.RefreshRoomPointerQualification(Screen, refreshedPoint, true, false,
            point =>
            {
                calls++;
                Assert.AreEqual(refreshedPoint, point);
                return new(true, true, false);
            });

        Assert.AreEqual(new RoomPointerQualification(true, true, false), result);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void StationaryCursorStillQueriesFinalDesktopAndIconQualificationOnce()
    {
        var calls = 0;
        var result = NativeMethods.RefreshRoomPointerQualification(Screen, Previous, true, false,
            point =>
            {
                calls++;
                Assert.AreEqual(Previous, point);
                return new(true, true, false);
            });

        Assert.AreEqual(new RoomPointerQualification(true, true, false), result);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(0, 50)]
    [DataRow(-1921, 50)]
    [DataRow(-100, -101)]
    [DataRow(-100, 980)]
    public void RefreshedPointOutsideTargetRejectsWithoutPointQuery(int x, int y)
    {
        var calls = 0;
        var result = NativeMethods.RefreshRoomPointerQualification(Screen, new(x, y), true, false,
            _ =>
            {
                calls++;
                return new(true, true, false);
            });

        Assert.IsFalse(IsQualified(result));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    [DataRow(false, false, false)]
    public void MovedFinalPointOnApplicationTaskbarIconOrUnknownRejects(bool known, bool background, bool icon)
    {
        var refreshedPoint = new WallpaperWheelPoint(Previous.X + 1, Previous.Y);
        var calls = 0;
        var result = NativeMethods.RefreshRoomPointerQualification(Screen, refreshedPoint, true, false,
            point =>
            {
                calls++;
                Assert.AreEqual(refreshedPoint, point);
                return new(known, background, icon);
            });

        Assert.IsFalse(IsQualified(result));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    [DataRow(false, false, false)]
    public void StationaryFinalPointBecomingUnsafeOrUnknownRejects(bool known, bool background, bool icon)
    {
        var calls = 0;
        var result = NativeMethods.RefreshRoomPointerQualification(Screen, Previous, true, false,
            _ =>
            {
                calls++;
                return new(known, background, icon);
            });

        Assert.IsFalse(IsQualified(result));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public void SafeFinalPointPreservesEarlierBackgroundAndIconRejections(bool background, bool icon)
    {
        var result = NativeMethods.RefreshRoomPointerQualification(Screen, Previous, background, icon,
            _ => new(true, true, false));

        Assert.AreEqual(new RoomPointerQualification(true, background, icon), result);
        Assert.IsFalse(IsQualified(result));
    }

    private static bool IsQualified(RoomPointerQualification result) =>
        result.Known && result.Background && !result.Icon;
}
