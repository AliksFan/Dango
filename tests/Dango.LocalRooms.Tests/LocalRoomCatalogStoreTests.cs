using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.LocalRooms.Tests;

[TestClass]
public sealed class LocalRoomCatalogStoreTests
{
    internal static string NewRoot() => Path.Combine(AppContext.BaseDirectory, "artifacts", "test-runs", "room-catalog", Guid.NewGuid().ToString("N"));
    private static RoomCatalogResult Create(LocalRoomCatalogStore store, string name = "虚构小屋") =>
        store.Create(Guid.NewGuid(), Guid.NewGuid(), name, LocalRoomKind.Private, 6);
    [TestMethod]
    public void ThreeRoomBudgetDissolveRestoreAndEmptyCatalogSurviveNewInstances()
    {
        var root = NewRoot(); var store = new LocalRoomCatalogStore(root);
        var first = store.Refresh(); Assert.IsTrue(first.Success, first.Warning);
        Assert.AreEqual(1, first.Catalog!.ActiveCount);
        var original = first.Catalog.Rooms.Single().RoomId;
        Assert.IsTrue(Create(store).Success); Assert.IsTrue(Create(store).Success);
        Assert.IsFalse(Create(store).Success, "A fourth ordinary room must fail.");
        Assert.IsTrue(store.Dissolve(Guid.NewGuid(), original).Success);
        Assert.IsTrue(Create(store).Success);
        Assert.IsFalse(store.Restore(Guid.NewGuid(), original).Success);
        var latest = store.Refresh().Catalog!;
        foreach (var room in latest.Rooms.Where(room => room.Life == LocalRoomLife.Active))
            Assert.IsTrue(store.Dissolve(Guid.NewGuid(), room.RoomId).Success);
        var restarted = new LocalRoomCatalogStore(root).Refresh();
        Assert.AreEqual(0, restarted.Catalog!.ActiveCount, "An intentionally empty catalog must not recreate a default room.");
        var restored = store.Restore(Guid.NewGuid(), original);
        Assert.IsTrue(restored.Success, restored.Warning); Assert.AreEqual(original, restored.Catalog!.Rooms.First().RoomId);
    }
    [TestMethod]
    public void IdempotentRequestKeepsIdentityAndVersionAndRejectsChangedPayload()
    {
        var store = new LocalRoomCatalogStore(NewRoot()); store.Refresh();
        var op = Guid.NewGuid(); var id = Guid.NewGuid();
        var first = store.Create(op, id, "虚构花房", LocalRoomKind.Public, 1);
        Assert.IsTrue(first.Success, first.Warning);
        var retry = store.Create(op, id, "虚构花房", LocalRoomKind.Public, 1);
        Assert.AreEqual(first.Catalog!.CatalogVersion, retry.Catalog!.CatalogVersion);
        Assert.IsFalse(store.Create(op, Guid.NewGuid(), "另一请求", LocalRoomKind.Private, 6).Success);
        Assert.AreEqual(2, store.Refresh().Catalog!.ActiveCount);
    }
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void LegacyImportCopiesAllIdentityAndAnchorsWithoutChangingSource(int schema)
    {
        var root = NewRoot(); var oldPath = Path.Combine(root, LegacyRoomImport.LegacyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        var item = new FurniturePlatformInstance(Guid.NewGuid(), FurnitureLayoutContract.OrdinaryRoom.DefaultAnchor);
        var layout = new FurnitureLayout(7, Guid.NewGuid(), item);
        var json = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom))!.AsObject();
        json["schemaVersion"] = schema;
        var old = Encoding.UTF8.GetBytes(" \r\n" + json.ToJsonString() + "\r\n"); File.WriteAllBytes(oldPath, old);
        var store = new LocalRoomCatalogStore(root); var result = store.Refresh();
        Assert.IsTrue(result.Success, result.Warning); var id = result.Catalog!.Rooms.Single().RoomId;
        Assert.AreEqual(layout, store.ReadLayout(id)); CollectionAssert.AreEqual(old, File.ReadAllBytes(oldPath));
        var restarted = new LocalRoomCatalogStore(root).Refresh();
        Assert.AreEqual(id, restarted.Catalog!.Rooms.Single().RoomId);
        Assert.AreEqual(3, JsonNode.Parse(File.ReadAllBytes(store.LayoutPath(id)))!["schemaVersion"]!.GetValue<int>());
    }
    [TestMethod]
    [DataRow("{bad")]
    [DataRow("{\"schemaVersion\":999}")]
    public void BadOldFileNeverPublishesAnEmptyReplacement(string text)
    {
        var root = NewRoot(); var path = Path.Combine(root, LegacyRoomImport.LegacyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
        Assert.IsFalse(new LocalRoomCatalogStore(root).Refresh().Success);
        Assert.AreEqual(text, File.ReadAllText(path)); Assert.IsFalse(File.Exists(Path.Combine(root, LocalRoomCatalogStore.CatalogPath)));
    }
    [TestMethod]
    public void CatalogUnknownFormatDuplicateIdAndDuplicateJsonFieldAreProtected()
    {
        foreach (var corrupt in new Action<JsonObject>[] {
            json => json["schemaVersion"] = 999,
            json => json["rooms"]!.AsArray().Add(json["rooms"]!.AsArray()[0]!.DeepClone()),
            json => json["unexpected"] = true })
        {
            var root = NewRoot(); var store = new LocalRoomCatalogStore(root); store.Refresh();
            var path = Path.Combine(root, LocalRoomCatalogStore.CatalogPath);
            var json = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject(); corrupt(json);
            var bytes = Encoding.UTF8.GetBytes(json.ToJsonString()); File.WriteAllBytes(path, bytes);
            Assert.IsFalse(store.Refresh().Success); Assert.IsFalse(Create(store).Success);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        var duplicateRoot = NewRoot(); var files = new RoomFileStore(duplicateRoot);
        var duplicatePath = files.PathFor(LocalRoomCatalogStore.CatalogPath); Directory.CreateDirectory(Path.GetDirectoryName(duplicatePath)!);
        File.WriteAllText(duplicatePath, "{\"SchemaVersion\":1,\"SchemaVersion\":1}");
        Assert.IsFalse(new LocalRoomCatalogStore(duplicateRoot).Refresh().Success);
    }
    [TestMethod]
    public void IdentityBoundLayoutRejectsOtherRoomLegacyAndMissingFiles()
    {
        var store = new LocalRoomCatalogStore(NewRoot()); var a = store.Refresh().Catalog!.Rooms.Single().RoomId;
        var b = Create(store).Catalog!.Rooms.Last().RoomId;
        var correct = File.ReadAllBytes(store.LayoutPath(a)); File.Copy(store.LayoutPath(a), store.LayoutPath(b), true);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.CreateLayoutStore(b).Load(store.LayoutPath(b)).State);
        File.WriteAllBytes(store.LayoutPath(b), FurnitureLayoutSerializer.Serialize(FurnitureLayout.Empty, FurnitureLayoutContract.OrdinaryRoom));
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.CreateLayoutStore(b).Load(store.LayoutPath(b)).State);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.CreateLayoutStore(Guid.NewGuid()).Load(store.LayoutPath(Guid.NewGuid())).State);
        CollectionAssert.AreEqual(correct, File.ReadAllBytes(store.LayoutPath(a)));
    }
    [TestMethod]
    public void ImportInterruptedBeforeCatalogPublicationReusesThePreparedRoomId()
    {
        var root = NewRoot(); var writer = new FaultWriter { FailCatalog = true };
        Assert.IsFalse(new LocalRoomCatalogStore(root, writer).Refresh().Success);
        var prepare = RoomJson.Read<LegacyRoomImport>(File.ReadAllBytes(Path.Combine(root, "rooms/legacy-import.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, LocalRoomCatalogStore.LayoutRelativePath(prepare.RoomId))));
        writer.FailCatalog = false;
        var restarted = new LocalRoomCatalogStore(root, writer).Refresh();
        Assert.IsTrue(restarted.Success, restarted.Warning);
        Assert.AreEqual(prepare.RoomId, restarted.Catalog!.Rooms.Single().RoomId);
    }
    [TestMethod]
    public void ReplacementThatSucceededThenThrewIsObservedAsCommitted()
    {
        var store = new LocalRoomCatalogStore(NewRoot(), new FaultWriter { ThrowAfterReplace = true });
        Assert.IsTrue(store.Refresh().Success); Assert.IsTrue(Create(store).Success);
        Assert.AreEqual(2, store.Refresh().Catalog!.ActiveCount);
    }
    [TestMethod]
    public void CatalogUnknownResultBlocksMutationsUntilExplicitRecheck()
    {
        var root = NewRoot(); var writer = new FaultWriter(); var store = new LocalRoomCatalogStore(root, writer); store.Refresh();
        var original = File.ReadAllBytes(Path.Combine(root, LocalRoomCatalogStore.CatalogPath));
        writer.CorruptCatalog = true;
        var result = Create(store); Assert.AreEqual(FurnitureCommitState.Unknown, result.State);
        writer.CorruptCatalog = false; Assert.IsFalse(Create(store).Success);
        Assert.AreEqual("unknown", File.ReadAllText(Path.Combine(root, LocalRoomCatalogStore.CatalogPath)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(root, LocalRoomCatalogStore.CatalogPath + ".previous")),
            "The last known good directory must survive a corrupt/unknown replacement on disk.");
    }
    [TestMethod]
    public void CatalogLeaseSerializesWritersAndDissolvedRoomCannotAcquireLayoutWriteGuard()
    {
        var root = NewRoot(); var store = new LocalRoomCatalogStore(root); var id = store.Refresh().Catalog!.Rooms.Single().RoomId;
        using (new RoomFileStore(root).Acquire(LocalRoomCatalogStore.CatalogPath)) Assert.IsFalse(Create(store).Success);
        Assert.IsTrue(store.Dissolve(Guid.NewGuid(), id).Success);
        Assert.ThrowsExactly<InvalidOperationException>(() => store.AcquireLayoutGuard(id));
        Assert.IsTrue(File.Exists(store.LayoutPath(id)), "Dissolution must not delete content.");
    }
    [TestMethod]
    public void LocalSevenDayExpiryKeepsBytesAndClockRollbackSuspendsRestoration()
    {
        var clock = new TestClock(); var root = NewRoot(); var store = new LocalRoomCatalogStore(root, clock: clock);
        var id = store.Refresh().Catalog!.Rooms.Single().RoomId; store.Dissolve(Guid.NewGuid(), id);
        var bytes = File.ReadAllBytes(store.LayoutPath(id)); clock.Advance(TimeSpan.FromDays(8));
        Assert.IsFalse(store.Restore(Guid.NewGuid(), id).Success);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(store.LayoutPath(id)));
        clock.Jump(TimeSpan.FromDays(-10)); Assert.IsFalse(store.Restore(Guid.NewGuid(), id).Success);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(store.LayoutPath(id)));
    }
    [TestMethod]
    public void OpeningEvidenceNeverUsesCurrentOnlineCountAndUnknownIsReadOnly()
    {
        var evidence = RoomAccessEvidence.Unopened;
        Assert.IsTrue(evidence.CanEditOffline(6));
        Assert.IsFalse((evidence with { PendingInvitation = true }).CanEditOffline(6));
        Assert.IsFalse((evidence with { PublicEntryEnabled = true }).CanEditOffline(1));
        Assert.IsFalse((evidence with { OtherEntitlements = true }).CanEditOffline(1));
        Assert.IsFalse((evidence with { HasEverOpened = true, OnlyOwnerCanEnter = false }).CanEditOffline(6));
        Assert.IsFalse((evidence with { OpeningKnown = false, OnlyOwnerCanEnter = false }).CanEditOffline(6));
        Assert.IsTrue((evidence with { HasEverOpened = true }).CanEditOffline(1));
    }
    [TestMethod]
    public void MissingPreviouslyPublishedCatalogDoesNotResurrectDissolvedRooms()
    {
        var root = NewRoot(); var store = new LocalRoomCatalogStore(root); var id = store.Refresh().Catalog!.Rooms.Single().RoomId;
        store.Dissolve(Guid.NewGuid(), id);
        var originalPath = Path.Combine(root, LocalRoomCatalogStore.CatalogPath);
        var preserved = originalPath + ".simulated-loss"; File.Move(originalPath, preserved);
        var bytes = File.ReadAllBytes(preserved);
        Assert.IsFalse(new LocalRoomCatalogStore(root).Refresh().Success);
        Assert.IsFalse(File.Exists(originalPath)); CollectionAssert.AreEqual(bytes, File.ReadAllBytes(preserved));
        Assert.IsTrue(File.Exists(store.LayoutPath(id)));
    }
    [TestMethod]
    public void RelativePathEscapeAndCatalogDirectoryCannotInitializeData()
    {
        var root = NewRoot(); var files = new RoomFileStore(root);
        Assert.ThrowsExactly<InvalidDataException>(() => files.PathFor("../outside.json"));
        var path = Path.Combine(root, LocalRoomCatalogStore.CatalogPath); Directory.CreateDirectory(path);
        Assert.IsFalse(new LocalRoomCatalogStore(root).Refresh().Success);
        Assert.IsTrue(Directory.Exists(path));
    }
    [TestMethod]
    public void IdentityBoundUnknownLayoutCommitRetainsLastGoodBytesInDurableProtectionCopy()
    {
        var catalog = new LocalRoomCatalogStore(NewRoot()); var id = catalog.Refresh().Catalog!.Rooms.Single().RoomId;
        var path = catalog.LayoutPath(id); var original = File.ReadAllBytes(path);
        var store = new FurnitureLayoutStore(new FaultWriter { CorruptLayout = true }, FurnitureLayoutContract.OrdinaryRoom, id,
            () => catalog.AcquireLayoutGuard(id));
        using (var lease = store.AcquireLease(path))
        using (var stage = lease.Stage(new FurnitureLayout(1, Guid.NewGuid(), new(Guid.NewGuid(), new(1440, 120))), 0))
            Assert.AreEqual(FurnitureCommitState.Unknown, stage.Commit().State);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path + ".previous"));
        Assert.AreEqual(FurnitureLayout.Empty, FurnitureLayoutSerializer.Deserialize(File.ReadAllBytes(path + ".previous"), FurnitureLayoutContract.OrdinaryRoom, id));
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.Load(path).State);
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        private long ticks;
        public override DateTimeOffset GetUtcNow() => now;
        public override long GetTimestamp() => ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(TimeSpan time) { now += time; ticks += time.Ticks; }
        internal void Jump(TimeSpan time) => now += time;
    }
    private sealed class FaultWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter atomic = new();
        internal bool FailCatalog, CorruptCatalog, CorruptLayout, ThrowAfterReplace;
        public void WriteStaged(string path, byte[] bytes) => atomic.WriteStaged(path, bytes);
        public void Commit(string staged, string target, bool existed)
        {
            var catalog = Path.GetFileName(target) == "catalog.json";
            if (catalog && FailCatalog) throw new IOException("injected before catalog replacement");
            atomic.Commit(staged, target, existed);
            if (catalog && CorruptCatalog) File.WriteAllText(target, "unknown");
            if (Path.GetFileName(target) == "furniture-layout.json" && CorruptLayout) File.WriteAllText(target, "unknown");
            if (ThrowAfterReplace) throw new IOException("injected after replacement");
        }
    }
}
