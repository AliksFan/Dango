using System.Windows;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomRevisionTests
{
    [TestMethod]
    [DataRow(1920d, 1080d, 0d, 0d, 0d, 40d)]
    [DataRow(1280d, 800d, 48d, 0d, 0d, 0d)]
    [DataRow(1440d, 900d, 0d, 56d, 0d, 0d)]
    [DataRow(1600d, 900d, 0d, 0d, 60d, 0d)]
    public void DefaultRoomScaleIsExactlyOnePointFourTimesThePreviousWorkAreaScale(
        double width, double height, double leftInset, double topInset, double rightInset, double bottomInset)
    {
        var mapping = DesktopPetRoomMapping.Create(width, height,
            new(leftInset, topInset, rightInset, bottomInset), 360);
        var previousScale = (width - leftInset - rightInset - 32) / 2112;
        Assert.AreEqual(previousScale * 1.4, mapping.Projection.Scale, 1e-12,
            "The complete shared projection must enlarge the same pet by exactly 1.4 relative to the previous default.");
    }

    [TestMethod]
    public void SafeSceneEdgesEqualUsableDisplayEdgesWithoutAnExtraSixteenDipMargin()
    {
        var mapping = DesktopPetRoomMapping.Create(1920, 1080, new Thickness(48, 32, 60, 40), 360);
        var independentlyUsable = new LogicalRect(48, 32, 1920 - 48 - 60, 1080 - 32 - 40);
        Assert.AreEqual(independentlyUsable.X, mapping.SafeSceneBounds.X, 1e-9);
        Assert.AreEqual(independentlyUsable.Y, mapping.SafeSceneBounds.Y, 1e-9);
        Assert.AreEqual(independentlyUsable.Right, mapping.SafeSceneBounds.Right, 1e-9);
        Assert.AreEqual(independentlyUsable.Bottom, mapping.SafeSceneBounds.Bottom, 1e-9,
            "Only the taskbar/work-area insets exclude display space; the prior sixteen-DIP inset is removed.");
    }

    [TestMethod]
    public void EnlargedProjectionKeepsTheWorldCenteredAndInvertsTheVisibleHorizontalEdges()
    {
        const double width = 1920, height = 1080, left = 48, right = 60, floor = 360;
        var mapping = DesktopPetRoomMapping.Create(width, height, new Thickness(left, 32, right, 40), floor);
        var expectedScale = (width - left - right - 32) / 2112 * 1.4;
        var workWidth = width - left - right;
        var expectedLeftWorld = 1080 - workWidth / (2 * expectedScale);
        var expectedRightWorld = 1080 + workWidth / (2 * expectedScale);
        Assert.AreEqual(left + workWidth / 2, mapping.Projection.WorldToScreen(new(1080, floor)).X, 1e-9,
            "Zoom must remain centered on the same world rather than pinning its previous left wall.");
        Assert.AreEqual(left, mapping.Projection.WorldToScreen(new(expectedLeftWorld, floor)).X, 1e-9);
        Assert.AreEqual(width - right, mapping.Projection.WorldToScreen(new(expectedRightWorld, floor)).X, 1e-9);
        foreach (var world in new[]
        {
            new LogicalPoint(expectedLeftWorld, -180), new(1080, floor), new(expectedRightWorld, floor - 90),
        })
        {
            var displayed = mapping.Projection.WorldToScreen(world);
            var input = mapping.Projection.ScreenToWorld(displayed);
            Assert.AreEqual(world.X, input.X, 1e-9);
            Assert.AreEqual(world.Y, input.Y, 1e-9,
                "Environment, independent pet and pointer must use the same invertible zoomed projection.");
        }
    }

    [TestMethod]
    public void PhysicalRoomHorizontalExtentMatchesTheCloserVisibleWorldInsteadOfOldInvisibleWalls()
    {
        const double floor = 360;
        var mapping = DesktopPetRoomMapping.Create(1920, 1080, new Thickness(0, 0, 0, 40), floor);
        Assert.AreEqual(mapping.SafeWorldBounds.X, mapping.Surface.X, 1e-9);
        Assert.AreEqual(mapping.SafeWorldBounds.Right, mapping.Surface.Right, 1e-9);
        Assert.AreEqual(mapping.SafeWorldBounds.Width, mapping.Surface.Width, 1e-9,
            "The physical surface must follow the world that actually reaches the work-area edges.");
        Assert.IsTrue(mapping.SafeWorldBounds.Width < 2112,
            "Increasing the same default pet scale must provide a closer view with less visible world width.");
        Assert.AreEqual(floor, mapping.Surface.Bottom, 1e-9, "Zoom changes the view without inventing a different native floor.");
    }

    [TestMethod]
    public void NativeFloorProjectsToUsableDisplayBottomWithoutThePreviousFortyEightDipGap()
    {
        const double nativeFloor = 360;
        var mapping = DesktopPetRoomMapping.Create(1920, 1080, new Thickness(48, 32, 60, 40), nativeFloor);
        var physicalFloor = mapping.Projection.WorldToScreen(new(1080, nativeFloor));
        Assert.AreEqual(1080 - 40, physicalFloor.Y, 1e-9,
            "The existing native room floor must reach the usable display bottom instead of retaining a forty-eight-DIP gap.");
    }
}
