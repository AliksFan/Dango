using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.LocalRooms.Tests.Furniture;

[TestClass]
public sealed class FurnitureLayoutStoreCollectionTests
{
    private static readonly Guid Operation = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static FurnitureLayoutContract Room => FurnitureLayoutContract.OrdinaryRoom;
    private static string NewPath()
    {
        var root = System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts", "test-runs", "furniture-collection", Guid.NewGuid().ToString("N"));
        var path = FurnitureLayoutStore.GetLayoutPath(root, Room);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        return path;
    }
    private static FurnitureItemInstance[] Platforms(int count) => Enumerable.Range(0, count).Select(index =>
        new FurnitureItemInstance(Guid.Parse($"10000000-0000-4000-8000-{index + 1:000000000000}"), FurnitureItemKind.StaticPlatform,
            new(new[] { 160d, 432, 704, 1396, 1668, 1940 }[index % 6], 21 * (index / 6 + 1)))).ToArray();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyRoomReadLeaseAndCanceledEditPreserveExactBytesUntilConfirmedEdit(bool occupied)
    {
        var path = NewPath();
        var layout = occupied ? new FurnitureLayout(7, Operation, new(Guid.NewGuid(), Room.DefaultAnchor)) : FurnitureLayout.Empty;
        var document = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(layout, Room))!.AsObject();
        document["schemaVersion"] = 1;
        var bytes = Encoding.UTF8.GetBytes(" \r\n" + document.ToJsonString() + "\r\n ");
        File.WriteAllBytes(path, bytes);
        var store = new FurnitureLayoutStore(contract: Room);
        Assert.AreEqual(layout, store.Load(path).Layout);
        CollectionAssert.AreEqual(bytes, store.Load(path).CopyRawBytes());
        var added = new FurnitureItemInstance(Guid.NewGuid(), FurnitureItemKind.RoundBall, new(1800, 220));
        var operation = new RoomFurnitureEditOperation(Guid.NewGuid(), layout.LayoutVersion, FurnitureEditKind.Insert, null, added);
        var candidate = operation.BuildCandidate(layout);
        using (var lease = store.AcquireLease(path))
        {
            CollectionAssert.AreEqual(bytes, lease.GetBaselineBytes());
            using var canceled = lease.Stage(candidate, layout.LayoutVersion);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        using (var lease = store.AcquireLease(path))
        using (var stage = lease.Stage(candidate, layout.LayoutVersion))
            Assert.AreEqual(FurnitureCommitState.Committed, stage.Commit().State);
        Assert.AreEqual(2, JsonNode.Parse(File.ReadAllBytes(path))!["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual(candidate, store.Load(path).Layout);
        Assert.AreEqual(layout.LayoutVersion + 1, store.Load(path).Layout!.LayoutVersion);
        Assert.AreEqual(occupied ? 2 : 1, store.Load(path).Layout!.Items.Count);
        if (occupied) Assert.AreEqual(layout.Platform!.InstanceId, store.Load(path).Layout!.Items[0].InstanceId);
    }

    [TestMethod]
    public void ThirtyItemsPassRealStageReReadAndAtomicCommitWithValueEquality()
    {
        var path = NewPath();
        var input = Platforms(30);
        var candidate = FurnitureLayout.ForItems(1, Operation, input);
        input[0] = input[0] with { Anchor = new(1080, 120) };
        var store = new FurnitureLayoutStore(contract: Room);
        using (var lease = store.AcquireLease(path))
        using (var stage = lease.Stage(candidate, 0))
            Assert.AreEqual(FurnitureCommitState.Committed, stage.Commit().State);
        var loaded = store.Load(path).Layout!;
        Assert.AreEqual(30, loaded.Items.Count);
        Assert.AreEqual(candidate, loaded);
        Assert.AreEqual(candidate.GetHashCode(), loaded.GetHashCode());
        Assert.AreEqual(new FurnitureAnchor(160, 21), loaded.Items[0].Anchor);
        using var reopened = store.AcquireLease(path);
        Assert.AreEqual(candidate, reopened.Baseline.Layout);
    }

    [TestMethod]
    public void ThirtyFirstItemIsRejectedBeforeStagingAndLeavesFileBytesAndVersionIntact()
    {
        var path = NewPath();
        var full = FurnitureLayout.ForItems(1, Operation, Platforms(30));
        var bytes = FurnitureLayoutSerializer.Serialize(full, Room);
        File.WriteAllBytes(path, bytes);
        var writer = new CountingWriter();
        var store = new FurnitureLayoutStore(writer, Room);
        using var lease = store.AcquireLease(path);
        var overCapacity = FurnitureLayout.ForItems(2, Guid.NewGuid(), Platforms(31));
        Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(overCapacity, 1));
        Assert.AreEqual(0, writer.StageCalls);
        Assert.AreEqual(0, writer.CommitCalls);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(full, store.Load(path).Layout);
    }

    [TestMethod]
    public void LegacyMultiItemAndFutureSchemaFilesRemainProtectedAndNeverStage()
    {
        foreach (var schema in new[] { 1, 3 })
        {
            var path = NewPath();
            var document = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(FurnitureLayout.ForItems(1, Operation, Platforms(2)), Room))!.AsObject();
            document["schemaVersion"] = schema;
            var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
            File.WriteAllBytes(path, bytes);
            var store = new FurnitureLayoutStore(contract: Room);
            Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.Load(path).State);
            using var lease = store.AcquireLease(path);
            Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(new FurnitureLayout(1, Guid.NewGuid(), null), 0));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
    }

    [TestMethod]
    public void LoadedThirtyOneDistinctNonoverlappingItemsRemainProtectedRatherThanBeingTruncated()
    {
        var path = NewPath();
        var document = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(FurnitureLayout.ForItems(1, Operation, Platforms(30)), Room))!.AsObject();
        var extra = document["items"]![0]!.DeepClone()!;
        extra["instanceId"] = Platforms(31)[30].InstanceId.ToString("D");
        extra["anchor"]!["y"] = 126;
        document["items"]!.AsArray().Add(extra);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        File.WriteAllBytes(path, bytes);
        var writer = new CountingWriter();
        var store = new FurnitureLayoutStore(writer, Room);
        Assert.AreEqual(FurnitureLayoutLoadState.Protected, store.Load(path).State);
        Assert.IsNull(store.Load(path).Layout);
        using var lease = store.AcquireLease(path);
        Assert.ThrowsExactly<InvalidDataException>(() => lease.Stage(new FurnitureLayout(1, Guid.NewGuid(), null), 0));
        Assert.AreEqual(0, writer.StageCalls);
        Assert.AreEqual(0, writer.CommitCalls);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    private sealed class CountingWriter : IFurnitureFileWriter
    {
        private readonly FurnitureAtomicFileWriter writer = new();
        internal int StageCalls, CommitCalls;
        public void WriteStaged(string stagedPath, byte[] bytes) { StageCalls++; writer.WriteStaged(stagedPath, bytes); }
        public void Commit(string stagedPath, string targetPath, bool targetExisted) { CommitCalls++; writer.Commit(stagedPath, targetPath, targetExisted); }
    }
}
