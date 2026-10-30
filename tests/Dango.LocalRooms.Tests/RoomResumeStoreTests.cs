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
    public void IdentitiesKeepSeparatePointsAndShareLatestCameraWithoutChangingLegacyBytes()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var room = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var store = new RoomResumeStore(catalog); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var first = new RoomCameraSnapshot(RoomCameraMode.Manual, RoomViewPreset.Custom, 120, 400, 50);
        var latest = new RoomCameraSnapshot(RoomCameraMode.Manual, RoomViewPreset.Custom, 140, 700, 70);
        Assert.AreEqual(FurnitureCommitState.Committed, store.Save(room, 0, new(300, 0), first).State);
        var path = Path.Combine(catalog.DataRoot, $"device/rooms/{room:D}/resume.json");
        var original = File.ReadAllBytes(path);
        Assert.AreEqual(300, store.LoadForDango(room, a, a).Value!.Position!.X);
        Assert.IsNull(store.LoadForDango(room, b, a).Value!.Position);
        Assert.AreEqual(first, store.LoadForDango(room, b, a).Value!.Camera);
        Assert.AreEqual(FurnitureCommitState.Committed, store.SaveForDango(room, a, 0, new(500, 0), first).State);
        Assert.AreEqual(FurnitureCommitState.Committed, store.SaveForDango(room, b, 0, new(1700, 0), latest).State);
        store = new RoomResumeStore(new LocalRoomCatalogStore(catalog.DataRoot));
        Assert.AreEqual(500, store.LoadForDango(room, a, a).Value!.Position!.X);
        Assert.AreEqual(1700, store.LoadForDango(room, b, a).Value!.Position!.X);
        Assert.AreEqual(latest, store.LoadForDango(room, a, a).Value!.Camera);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }
    [TestMethod]
    public void WrongIdentityOrDamagedPointIsProtectedWithoutBlockingAnotherIdentity()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var room = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var store = new RoomResumeStore(catalog); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        store.SaveForDango(room, a, 0, new(500, 0), camera);
        var path = Path.Combine(catalog.DataRoot, $"device/rooms/{room:D}/dangos/{a:D}/resume.json");
        var json = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); json["dangoId"] = b;
        var original = RoomJson.Write(json); File.WriteAllBytes(path, original);
        Assert.AreEqual(RoomFileState.Protected, store.LoadForDango(room, a).Snapshot.State);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveForDango(room, a, 0, new(700, 0), camera).State);
        Assert.AreEqual(FurnitureCommitState.Committed, store.SaveForDango(room, b, 0, new(1500, 0), camera).State);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        File.WriteAllText(path, "broken");
        Assert.AreEqual(RoomFileState.Protected, store.LoadForDango(room, a).Snapshot.State);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveForDango(room, a, 0, new(700, 0), camera).State);
        Assert.AreEqual("broken", File.ReadAllText(path));
    }
    [TestMethod]
    public void FutureSharedCameraProtectsBothOriginalFilesAndNewIdentitiesCannotInheritOldPoint()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var room = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        var store = new RoomResumeStore(catalog);
        store.Save(room, 0, new(300, 0), camera);
        store.SaveForDango(room, a, 0, new(500, 0), camera);
        var pointPath = Path.Combine(catalog.DataRoot, $"device/rooms/{room:D}/dangos/{a:D}/resume.json");
        var point = File.ReadAllBytes(pointPath);
        var cameraPath = Path.Combine(catalog.DataRoot, $"device/rooms/{room:D}/camera.json");
        var json = JsonNode.Parse(File.ReadAllBytes(cameraPath))!.AsObject(); json["schemaVersion"] = 99;
        var original = RoomJson.Write(json); File.WriteAllBytes(cameraPath, original);
        Assert.AreEqual(RoomFileState.Protected, store.LoadForDango(room, a, a).Snapshot.State);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveForDango(room, a, 0, new(700, 0), camera).State);
        CollectionAssert.AreEqual(point, File.ReadAllBytes(pointPath));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(cameraPath));
        // A never becomes B: even after the caller stops displaying A, the explicit legacy owner stays A.
        var otherRoom = Guid.NewGuid(); catalog.Create(Guid.NewGuid(), otherRoom, "虚构新房", LocalRoomKind.Private, 6);
        store.Save(otherRoom, 0, new(300, 0), camera);
        Assert.IsNull(store.LoadForDango(otherRoom, b, a).Value!.Position);
    }
    [TestMethod]
    public void CameraFailureAfterPositionCommitCannotReportCompleteSuccess()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var room = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var a = Guid.NewGuid(); var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        var store = new RoomResumeStore(catalog, new CameraFailWriter());
        Assert.AreEqual(FurnitureCommitState.Unknown, store.SaveForDango(room, a, 0, new(500, 0), camera).State);
        Assert.AreEqual(500, new RoomResumeStore(catalog).LoadForDango(room, a).Value!.Position!.X);
        Assert.IsFalse(File.Exists(Path.Combine(catalog.DataRoot, $"device/rooms/{room:D}/camera.json")));
    }
    [TestMethod]
    public void UnknownPointOutcomeLocksOnlyThatIdentityUntilExplicitValidReload()
    {
        var catalog = new LocalRoomCatalogStore(Root()); var room = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var writer = new UnknownPointWriter();
        var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        var store = new RoomResumeStore(catalog, writer);
        Assert.AreEqual(FurnitureCommitState.Unknown, store.SaveForDango(room, a, 0, new(500, 0), camera).State);
        var path = Path.Combine(catalog.DataRoot, $"device/rooms/{room:D}/dangos/{a:D}/resume.json");
        File.WriteAllBytes(path, writer.Candidate!); // Simulated external recovery; no production file is touched.
        writer.Corrupt = false;
        Assert.AreEqual(FurnitureCommitState.Unknown, store.SaveForDango(room, a, 0, new(700, 0), camera).State);
        Assert.AreEqual(FurnitureCommitState.Committed, store.SaveForDango(room, b, 0, new(1500, 0), camera).State);
        Assert.IsTrue(store.LoadForDango(room, a).Valid);
        Assert.AreEqual(FurnitureCommitState.Committed, store.SaveForDango(room, a, 0, new(700, 0), camera).State);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveForDango(room, a, 1, new(900, 0), camera).State);
    }
    private sealed class UnknownPointWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter atomic = new();
        internal bool Corrupt = true;
        internal byte[]? Candidate;
        public void WriteStaged(string path, byte[] bytes) => atomic.WriteStaged(path, bytes);
        public void Commit(string staged, string target, bool existed)
        {
            if (Corrupt && Path.GetFileName(target) == "resume.json")
            {
                Candidate = File.ReadAllBytes(staged); File.WriteAllText(target, "injected unknown");
                throw new IOException("injected external change");
            }
            atomic.Commit(staged, target, existed);
        }
    }
    private sealed class CameraFailWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter atomic = new();
        public void WriteStaged(string path, byte[] bytes) => atomic.WriteStaged(path, bytes);
        public void Commit(string staged, string target, bool existed)
        {
            if (Path.GetFileName(target) == "camera.json") throw new IOException("injected camera failure");
            atomic.Commit(staged, target, existed);
        }
    }
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
