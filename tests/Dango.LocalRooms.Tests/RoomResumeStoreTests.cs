using System.IO;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.LocalRooms.Tests;

[TestClass]
public sealed class RoomResumeStoreTests
{
    private static string Root() => Path.Combine(AppContext.BaseDirectory, "artifacts", "test-runs", "room-resume", Guid.NewGuid().ToString("N"));
    [TestMethod]
    public void RoomsRetainDistinctSafePointsCameraModesAndVersions()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var a = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var b = Guid.NewGuid(); catalog.Create(Guid.NewGuid(), b, "虚构B房", LocalRoomKind.Private, 6);
        var store = new RoomResumeStore(catalog);
        var cameraA = new RoomCameraSnapshot(RoomCameraMode.Manual, RoomViewPreset.Custom, 120, 400, 75);
        var cameraB = new RoomCameraSnapshot(RoomCameraMode.EdgeFollow, RoomViewPreset.Whole, 100, 20, 0);
        Assert.AreEqual(FurnitureCommitState.Committed, store.Save(a, 0, new(400, 0), cameraA).State);
        Assert.AreEqual(FurnitureCommitState.Committed, store.Save(b, 0, new(1700, 0), cameraB).State);
        var restarted = new RoomResumeStore(new LocalRoomCatalogStore(catalog.DataRoot));
        Assert.AreEqual(cameraA, restarted.Load(a).Value!.Camera); Assert.AreEqual(400, restarted.Load(a).Value!.Position!.X);
        Assert.AreEqual(cameraB, restarted.Load(b).Value!.Camera); Assert.AreEqual(1700, restarted.Load(b).Value!.Position!.X);
        Assert.AreEqual(1, restarted.Load(a).Value!.ResumeVersion);
        Assert.AreEqual(FurnitureCommitState.Committed, restarted.Save(a, 0, new(500, 0), cameraA).State);
        Assert.AreEqual(2, restarted.Load(a).Value!.ResumeVersion); Assert.AreEqual(1, restarted.Load(b).Value!.ResumeVersion);
    }
    [TestMethod]
    public void BadFutureOrWrongRoomCheckpointIsPreservedAndDoesNotDamageFurniture()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var id = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var store = new RoomResumeStore(catalog); var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        store.Save(id, 0, new(1080, 0), camera);
        var path = Path.Combine(catalog.DataRoot, $"device/rooms/{id:D}/resume.json");
        var json = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); json["roomId"] = Guid.NewGuid();
        var bytes = RoomJson.Write(json); File.WriteAllBytes(path, bytes);
        Assert.IsFalse(store.Load(id).Valid);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.Save(id, 0, new(1000, 0), camera).State);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path)); Assert.AreEqual(FurnitureLayout.Empty, catalog.ReadLayout(id));
    }
    [TestMethod]
    public void StaleLayoutVersionAndDissolvedRoomCannotOverwriteCheckpoint()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var id = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var store = new RoomResumeStore(catalog); var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.Save(id, 1, new(1080, 0), camera).State);
        Assert.IsFalse(store.Load(id).Valid);
        store.Save(id, 0, new(1080, 0), camera); var before = store.Load(id).Snapshot.Bytes!;
        catalog.Dissolve(Guid.NewGuid(), id);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.Save(id, 0, new(500, 0), camera).State);
        CollectionAssert.AreEqual(before, store.Load(id).Snapshot.Bytes!);
    }
    [TestMethod]
    public void SelectionOnlyReferencesAnActiveRoomAndNeverCreatesAdmission()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var id = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var store = new RoomResumeStore(catalog); Assert.IsNull(store.LoadSelection(out _));
        Assert.AreEqual(FurnitureCommitState.Committed, store.SaveSelection(id).State);
        Assert.AreEqual(id, new RoomResumeStore(catalog).LoadSelection(out _));
        catalog.Dissolve(Guid.NewGuid(), id); Assert.IsNull(store.LoadSelection(out _));
    }
    [TestMethod]
    public void SafePlacementRejectsWallOutliersUnsupportedPlatformsAndBallAnchors()
    {
        Assert.IsTrue(RoomResumeSafety.CanRestore(new(1080, 0), FurnitureLayout.Empty));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(20, 0), FurnitureLayout.Empty));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(1080, 120), FurnitureLayout.Empty));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(double.NaN, 0), FurnitureLayout.Empty));
        var platform = new FurnitureItemInstance(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1440, 120));
        var ball = new FurnitureItemInstance(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800, 66));
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(), [platform, ball]);
        Assert.IsTrue(RoomResumeSafety.CanRestore(new(1440, 120), layout));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(1440, 0), layout));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(1800, 0), layout));
    }
    [TestMethod]
    public void ManualCameraRestoresClampedViewAndFollowUsesNewBodyInsteadOfOldRoom()
    {
        var camera = new RoomViewController(); camera.EnableWindowCamera(); camera.Resize(900, 560, 360);
        camera.Update(new LogicalPoint(1700, 200), 0);
        camera.RestoreRoomState(new(RoomCameraMode.Manual, RoomViewPreset.Custom, 120, 400, 100));
        var restored = camera.CaptureState(); camera.Update(new LogicalPoint(500, 200), 200);
        Assert.AreEqual(restored.X, camera.CameraX); Assert.AreEqual(RoomCameraMode.Manual, camera.CameraMode);
        camera.RestoreRoomState(new(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 1900, -100));
        camera.Update(new LogicalPoint(300, 200), 0);
        Assert.IsTrue(camera.CameraX < 300, "A different room's old camera must not frame the new body off screen.");
    }
}
