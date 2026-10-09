using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomCameraSaveTests
{
    [TestMethod]
    public void ManualAnchorRestoresAfterFirstFocusAndDoesNotFollowThePet()
    {
        var camera = NewCamera();
        camera.RestoreSaveData(new("manual", "custom", 100, 900, -100));
        camera.Update(new LogicalPoint(600, 220), 0);
        Assert.AreEqual(900d, camera.CameraX, 1e-8);
        Assert.AreEqual(-100d, camera.CameraY, 1e-8);
        camera.Update(new LogicalPoint(1800, 10), 16);
        Assert.AreEqual(900d, camera.CameraX, 1e-8);
        Assert.AreEqual(-100d, camera.CameraY, 1e-8);
        var loaded = NewCamera();
        loaded.RestoreSaveData(camera.CaptureSaveData());
        loaded.Update(new LogicalPoint(600, 220), 0);
        Assert.AreEqual(camera.Projection, loaded.Projection);
    }

    [TestMethod]
    public void RequestedZoomSurvivesHeightClampAndRestoresWhenViewportGrows()
    {
        var camera = NewCamera();
        camera.Resize(900, 300, 296);
        camera.RestoreSaveData(new("edgeFollow", "near", 130, 0, 0));
        camera.Update(new LogicalPoint(1000, 220), 0);
        Assert.IsTrue(camera.ZoomPercent < 130);
        Assert.AreEqual(130d, camera.CaptureSaveData().RequestedZoomPercent);
        var larger = NewCamera();
        larger.RestoreSaveData(camera.CaptureSaveData());
        larger.Update(new LogicalPoint(1000, 220), 0);
        Assert.AreEqual(130d, larger.ZoomPercent, 1e-8);
        Assert.AreEqual(RoomCameraMode.EdgeFollow, larger.CameraMode);
    }

    [TestMethod]
    public void ChangedViewportClampsManualAnchorAndFollowDoesNotPersistJumpElevation()
    {
        var camera = NewCamera();
        camera.RestoreSaveData(new("manual", "standard", 100, 99999, -99999));
        camera.Update(new LogicalPoint(1000, 220), 0);
        Assert.IsTrue(camera.CameraX <= camera.WorldWidth - 900 / camera.Scale);
        Assert.IsTrue(camera.CameraY > -99999);
        camera.SelectCameraMode(RoomCameraMode.Follow);
        camera.Update(new LogicalPoint(1000, -200), 100, reduceMotion: true);
        Assert.AreEqual(0d, camera.CaptureSaveData().ManualY);
    }

    private static RoomViewController NewCamera()
    {
        var result = new RoomViewController();
        result.EnableWindowCamera(); result.Resize(900, 700, 296);
        return result;
    }

    [TestMethod]
    public void CalibratedMaximumZoomNormalizesFloatingPointRoundingBeforeStrictPersistence()
    {
        var camera = new RoomViewController();
        camera.EnableWindowCamera(); camera.CalibrateStandardScale(94.5); camera.Resize(900, 700, 296);
        Assert.IsTrue(camera.ApplyZoomSteps(10));
        var saved = camera.CaptureSaveData();
        Assert.AreEqual(130d, saved.RequestedZoomPercent);
        var state = LocalRoomSaveState.CreateDefault();
        (state with { OrdinaryRoom = state.OrdinaryRoom with { Camera = saved } }).Validate();
    }
}
