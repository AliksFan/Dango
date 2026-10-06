using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomCameraVerticalFramingTests
{
    private static RoomViewController Create()
    {
        var camera = new RoomViewController();
        camera.EnableWindowCamera();
        camera.Resize(960, 600, 360);
        camera.Update(new LogicalPoint(1080, 300), 0);
        return camera;
    }

    [TestMethod]
    public void FollowDoesNotMoveVerticallyWhileThePetStaysBelowTheMidline()
    {
        var camera = Create();
        var resting = camera.CameraY;
        for (var frame = 1; frame <= 60; frame++)
        {
            var y = 300 - frame * 100d / 60;
            var screenY = (y - resting) * camera.Scale;
            Assert.IsTrue(screenY >= 300, "This trajectory never crosses the unshifted screen midline.");
            camera.Update(new LogicalPoint(1080, y), 1000d / 60);
            Assert.AreEqual(resting, camera.CameraY, 1e-8,
                "A rise confined to the lower half must not move the vertical camera.");
        }
    }

    [TestMethod]
    public void FallingAfterAnAscentKeepsTheWholeBodyAboveTheBottomMargin()
    {
        var camera = Create();
        for (var i = 0; i < 60; i++) camera.Update(new LogicalPoint(1080, 60), 1000d / 60);
        for (var frame = 1; frame <= 36; frame++)
        {
            var y = 60 + frame * 240d / 36;
            camera.Update(new LogicalPoint(1080, y), 1000d / 60, bodyWorldBottom: y + 60);
            var screen = camera.Projection.WorldToScreen(new(1080, y + 60));
            Assert.IsTrue(screen.Y <= 600 - 32,
                $"The falling body bottom reached {screen.Y:0.00} DIP, leaving no lower screen margin.");
        }
        for (var i = 0; i < 240; i++) camera.Update(new LogicalPoint(1080, 300), 1000d / 60, bodyWorldBottom: 360);
        Assert.AreEqual(536, camera.Projection.WorldToScreen(new(1080, 360)).Y, .001);
    }

    [TestMethod]
    public void HighRiseBeginsSoftlyAboveTheMidlineAndSettlesThere()
    {
        var camera = Create();
        var resting = camera.CameraY;
        var firstAbove = resting + 300 / camera.Scale - 1;
        camera.Update(new LogicalPoint(1080, firstAbove), 1000d / 60, bodyWorldBottom: firstAbove + 60);
        Assert.IsTrue(camera.CameraY < resting);
        Assert.IsTrue(resting - camera.CameraY < 1, "Crossing must ease into movement instead of snapping to the pet.");
        for (var i = 0; i < 240; i++) camera.Update(new LogicalPoint(1080, 60), 1000d / 60, bodyWorldBottom: 120);
        Assert.AreEqual(300, camera.Projection.WorldToScreen(new(1080, 60)).Y, .001);
    }

    [TestMethod]
    public void SmallOscillationAroundTheMidlineDoesNotProduceVisibleCameraChatter()
    {
        var camera = Create();
        var resting = camera.CameraY;
        var midline = resting + 300 / camera.Scale;
        var last = resting;
        for (var frame = 0; frame < 240; frame++)
        {
            var y = midline + (frame % 2 == 0 ? .2 : -.2);
            camera.Update(new LogicalPoint(1080, y), 1000d / 120, bodyWorldBottom: y + 60);
            Assert.IsTrue(Math.Abs(camera.CameraY - last) * camera.Scale < .01);
            last = camera.CameraY;
        }
    }

    [TestMethod]
    public void ChangingTheInitialElevationDoesNotChangeTheMidlineOrRestingFloor()
    {
        foreach (var initial in new[] { 300d, 350d })
        {
            var camera = new RoomViewController();
            camera.EnableWindowCamera(); camera.Resize(960, 600, 360);
            camera.Update(new LogicalPoint(1080, initial), 0);
            for (var i = 0; i < 240; i++) camera.Update(new LogicalPoint(1080, 300), 1000d / 60, bodyWorldBottom: 360);
            Assert.AreEqual(536, camera.Projection.WorldToScreen(new(1080, 360)).Y, .001,
                "An earlier airborne or differently sized body sample must not become the permanent vertical composition.");
        }
    }
}
