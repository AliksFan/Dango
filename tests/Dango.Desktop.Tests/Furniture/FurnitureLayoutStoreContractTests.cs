using Dango.Core.Furniture;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.Tests.Furniture;

[TestClass]
public sealed class FurnitureLayoutStoreContractTests
{
    private static readonly Guid Instance = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Operation = Guid.Parse("20000000-0000-4000-8000-000000000002");

    [TestMethod]
    public void OrdinaryRoomPathIsExplicitAndDefaultKeepsTheLegacyPath()
    {
        var root = NewRoot();
        var legacy = FurnitureLayoutStore.GetLayoutPath(root);
        var room = FurnitureLayoutStore.GetLayoutPath(root, FurnitureLayoutContract.OrdinaryRoom);
        Assert.AreEqual(Path.Combine(root, "furniture-layout.json"), legacy);
        Assert.AreEqual(Path.Combine(root, "rooms", "local-ordinary-room", "furniture-layout.json"), room);
        Assert.AreNotEqual(legacy, room);
        Assert.AreSame(FurnitureLayoutContract.Trial, new FurnitureLayoutStore().Contract);
        Assert.AreSame(FurnitureLayoutContract.OrdinaryRoom, new FurnitureLayoutStore(contract: FurnitureLayoutContract.OrdinaryRoom).Contract);
    }

    [TestMethod]
    public void RoomSaveAndReloadNeverChangeTheLegacyLayout()
    {
        var root = NewRoot();
        var legacy = FurnitureLayoutStore.GetLayoutPath(root);
        var room = FurnitureLayoutStore.GetLayoutPath(root, FurnitureLayoutContract.OrdinaryRoom);
        var legacyBytes = FurnitureLayoutSerializer.Serialize(Layout(FurnitureLayoutContract.Trial));
        Write(legacy, legacyBytes);
        var store = new FurnitureLayoutStore(contract: FurnitureLayoutContract.OrdinaryRoom);
        using (var lease = store.AcquireLease(room))
        using (var stage = lease.Stage(Layout(FurnitureLayoutContract.OrdinaryRoom), 0))
            Assert.AreEqual(FurnitureCommitState.Committed, stage.Commit().State);
        CollectionAssert.AreEqual(legacyBytes, File.ReadAllBytes(legacy));
        Assert.AreEqual(Layout(FurnitureLayoutContract.Trial), new FurnitureLayoutStore().Load(legacy).Layout);
        Assert.AreEqual(Layout(FurnitureLayoutContract.OrdinaryRoom), store.Load(room).Layout);
        using var reopened = store.AcquireLease(room);
        Assert.AreEqual(Layout(FurnitureLayoutContract.OrdinaryRoom), reopened.Baseline.Layout);
        Assert.ThrowsExactly<InvalidOperationException>(() => reopened.Stage(Layout(FurnitureLayoutContract.OrdinaryRoom), 0));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TrialFilePlacedAtRoomPathIsProtectedEvenWhenEmpty(bool occupied)
    {
        var path = FurnitureLayoutStore.GetLayoutPath(NewRoot(), FurnitureLayoutContract.OrdinaryRoom);
        var bytes = FurnitureLayoutSerializer.Serialize(occupied ? Layout(FurnitureLayoutContract.Trial) : FurnitureLayout.Empty);
        Write(path, bytes);
        var store = new FurnitureLayoutStore(contract: FurnitureLayoutContract.OrdinaryRoom);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.Load(path).State);
        using var lease = store.AcquireLease(path);
        Assert.IsNull(lease.Baseline.Layout);
        Assert.IsFalse(lease.Baseline.IsWritable);
        Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(Layout(FurnitureLayoutContract.OrdinaryRoom), 0));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RoomFilePlacedAtLegacyPathCannotBeLoadedOrOverwrittenByTrial(bool occupied)
    {
        var path = FurnitureLayoutStore.GetLayoutPath(NewRoot());
        var bytes = FurnitureLayoutSerializer.Serialize(occupied ? Layout(FurnitureLayoutContract.OrdinaryRoom) : FurnitureLayout.Empty,
            FurnitureLayoutContract.OrdinaryRoom);
        Write(path, bytes);
        using var lease = new FurnitureLayoutStore().AcquireLease(path);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, lease.Baseline.State);
        Assert.IsNull(lease.Baseline.Layout);
        Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(Layout(FurnitureLayoutContract.Trial), 0));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    private static FurnitureLayout Layout(FurnitureLayoutContract contract) => new(1, Operation, new(Instance, contract.DefaultAnchor));
    private static string NewRoot() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "room-furniture-contract", Guid.NewGuid().ToString("N"));
    private static void Write(string path, byte[] bytes)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
}
