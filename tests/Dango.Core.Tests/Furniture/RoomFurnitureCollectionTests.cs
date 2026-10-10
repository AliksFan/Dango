using System.Text;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Furniture;

[TestClass]
public sealed class RoomFurnitureCollectionTests
{
    private static readonly Guid Operation = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid Later = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static FurnitureLayoutContract Room => FurnitureLayoutContract.OrdinaryRoom;
    private static FurnitureItemInstance Item(int index, FurnitureItemKind kind, double x, double y) =>
        new(Guid.Parse($"10000000-0000-4000-8000-{index:000000000000}"), kind, new(x, y));
    private static FurnitureLayout Layout(IEnumerable<FurnitureItemInstance> items) => FurnitureLayout.ForItems(1, Operation, items);

    public static FurnitureItemInstance[] PackedPlatforms(int count = 30) => Enumerable.Range(0, count)
        .Select(index => Item(index + 1, FurnitureItemKind.StaticPlatform,
            new[] { 160d, 432, 704, 1396, 1668, 1940 }[index % 6], 21 * (index / 6 + 1))).ToArray();

    public static FurnitureItemInstance[] PackedBalls(int count = 30)
    {
        var items = new List<FurnitureItemInstance>();
        foreach (var x in new[] { 90d, 224, 358, 492, 626, 760, 1326, 1460, 1594, 1728, 1862, 1996 })
            items.Add(Item(items.Count + 1, FurnitureItemKind.RoundBall, x, 66));
        foreach (var x in new[] { 157d, 291, 425, 559, 693, 827, 1393, 1527, 1661, 1795, 1929, 2063 })
            items.Add(Item(items.Count + 1, FurnitureItemKind.RoundBall, x, 180));
        foreach (var x in new[] { 90d, 224, 358, 492, 626, 760, 894, 1028, 1162, 1326, 1460, 1594, 1728, 1862, 1996 })
            items.Add(Item(items.Count + 1, FurnitureItemKind.RoundBall, x, 294));
        return items.Take(count).ToArray();
    }

    [TestMethod]
    public void CollectionCopiesInputAndComparesValuesAcrossSerializationAndWithCopies()
    {
        var input = PackedBalls(3);
        var expectedFirst = input[0];
        var layout = Layout(input);
        input[0] = input[0] with { Anchor = new(1080, 66) };
        Assert.AreEqual(expectedFirst, layout.Items[0]);
        Assert.IsFalse((object)layout.Items is IList<FurnitureItemInstance>);
        var reread = FurnitureLayoutSerializer.Deserialize(FurnitureLayoutSerializer.Serialize(layout, Room), Room);
        Assert.AreEqual(layout, reread);
        Assert.AreEqual(layout.GetHashCode(), reread.GetHashCode());
        Assert.AreEqual(layout, layout with { LayoutVersion = 1 });
        Assert.IsNull(layout.Platform, "The legacy view must never silently select the first of many items.");
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = layout with { Platform = null }; });
        Assert.AreEqual(expectedFirst, layout.Find(expectedFirst.InstanceId));
        Assert.IsNull(layout.Find(Guid.NewGuid()));
    }

    [TestMethod]
    public void ThirtyOfEitherKindOrMixedItemsRoundTripButThirtyOneCannotBeWrittenOrEdited()
    {
        var mixed = Enumerable.Range(0, 20).Select(index => Item(index + 1, FurnitureItemKind.StaticPlatform,
            new[] { 160d, 432, 704 }[index % 3], 21 * (index / 3 + 1)))
            .Concat(Enumerable.Range(0, 10).Select(index => Item(index + 21, FurnitureItemKind.RoundBall,
                1326 + 132 * (index % 6), 66 + 132 * (index / 6)))).ToArray();
        foreach (var items in new[] { PackedPlatforms(), PackedBalls(), mixed })
        {
            foreach (var count in new[] { 0, 1, 29, 30 })
            {
                var layout = Layout(items.Take(count));
                Assert.AreEqual(layout, FurnitureLayoutSerializer.Deserialize(FurnitureLayoutSerializer.Serialize(layout, Room), Room));
                Assert.AreEqual(count, layout.Items.Count);
                var root = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(layout, Room))!.AsObject();
                Assert.AreEqual(2, root["schemaVersion"]!.GetValue<int>());
            }
        }
        var full = Layout(PackedPlatforms());
        var extra = PackedPlatforms(31)[30];
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Serialize(Layout(PackedPlatforms(31)), Room));
        Assert.ThrowsExactly<InvalidDataException>(() => new RoomFurnitureEditOperation(Later, 1, FurnitureEditKind.Insert, null, extra).BuildCandidate(full));
        Assert.AreEqual(1L, full.LayoutVersion);
        Assert.AreEqual(30, full.Items.Count);
    }

    [TestMethod]
    public void RealCircleGeometryAcceptsDiagonalAabbOverlapAndTangencyButRejectsPositivePenetration()
    {
        var ball = Item(1, FurnitureItemKind.RoundBall, 300, 100);
        var diagonal = Item(2, FurnitureItemKind.RoundBall, 400, 200);
        Assert.IsFalse(Room.ItemsOverlap(ball, diagonal)); // AABBs overlap by 32 in both axes.
        Layout([ball, diagonal]).Validate(Room);
        var tangent = diagonal with { Anchor = new(432, 100) };
        Assert.IsFalse(Room.ItemsOverlap(ball, tangent));
        Assert.IsTrue(Room.ItemsOverlap(ball, tangent with { Anchor = new(431.999999, 100) }));
        var platform = Item(3, FurnitureItemKind.StaticPlatform, 400, 150);
        var corner = ball with { Anchor = new(214, 200) }; // 50/50 from top-left, outside radius 66.
        Assert.IsFalse(Room.ItemsOverlap(corner, platform));
        Assert.IsFalse(Room.ItemsOverlap(platform, corner));
        Layout([corner, platform]).Validate(Room);
        Assert.IsTrue(Room.ItemsOverlap(corner with { Anchor = new(220, 190) }, platform));
        Assert.IsFalse(Room.ItemsOverlap(ball with { Anchor = new(400, 216) }, platform));
        Assert.IsTrue(Room.ItemsOverlap(ball with { Anchor = new(400, 215.999999) }, platform));
        var secondPlatform = platform with { InstanceId = Guid.NewGuid(), Anchor = new(672, 150) };
        Assert.IsFalse(Room.ItemsOverlap(platform, secondPlatform));
        Assert.IsTrue(Room.ItemsOverlap(platform, secondPlatform with { Anchor = new(671.999999, 150) }));
    }

    [TestMethod]
    public void CircleSpawnCornerUsesCircleDistanceAndCompleteCircleBounds()
    {
        Item(1, FurnitureItemKind.RoundBall, 90, 66).Validate(Room);
        Item(1, FurnitureItemKind.RoundBall, 2070, 294).Validate(Room);
        Item(1, FurnitureItemKind.RoundBall, 850, 250).Validate(Room); // 50/50 from spawn corner.
        Item(1, FurnitureItemKind.RoundBall, 1080, 266).Validate(Room); // Touches top edge.
        foreach (var anchor in new[] { new FurnitureAnchor(89.999999, 66), new(2070.000001, 294),
            new(90, 65.999999), new(2070, 294.000001), new(860, 240), new(1080, 265.999999) })
            Assert.ThrowsExactly<InvalidDataException>(() => Item(1, FurnitureItemKind.RoundBall, anchor.X, anchor.Y).Validate(Room));
    }

    [TestMethod]
    public void LayoutRejectsDuplicateIdsUnsupportedKindsBadCoordinatesAndOverlap()
    {
        var valid = PackedBalls(2);
        foreach (var items in new[] { new[] { valid[0], valid[1] with { InstanceId = valid[0].InstanceId } },
            new[] { valid[0] with { Kind = (FurnitureItemKind)100 } },
            new[] { valid[0] with { InstanceId = Guid.Empty } },
            new[] { valid[0] with { Anchor = new(double.NaN, 66) } },
            new[] { valid[0], valid[1] with { Anchor = new(221.999999, 66) } } })
            Assert.ThrowsExactly<InvalidDataException>(() => Layout(items).Validate(Room));
        Assert.ThrowsExactly<InvalidDataException>(() => Layout(valid).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => Layout([valid[0]]).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => Layout([null!]).Validate(Room));
    }

    [TestMethod]
    public void PerItemOperationsPreserveOthersAndIdentityAndNoopDoesNotInventACommit()
    {
        var baseline = Layout(PackedBalls(3));
        var target = baseline.Items[1];
        var moved = target with { Anchor = new(222, 220) };
        var operation = new RoomFurnitureEditOperation(Later, 1, FurnitureEditKind.Move, target.InstanceId, moved);
        var result = operation.BuildCandidate(baseline);
        Assert.AreEqual(2L, result.LayoutVersion);
        Assert.AreEqual(Later, result.LastOperationId);
        Assert.AreEqual(moved, result.Items[1]);
        Assert.AreEqual(baseline.Items[0], result.Items[0]);
        Assert.AreEqual(baseline.Items[2], result.Items[2]);
        Assert.AreEqual(target, baseline.Items[1]);
        var ballAtSavedAnchor = (operation with { Candidate = target }).BuildCandidate(baseline);
        Assert.AreEqual(2L, ballAtSavedAnchor.LayoutVersion, "A moved live ball can be placed back at its saved anchor.");
        Assert.AreEqual(target, ballAtSavedAnchor.Items[1]);
        var platformBaseline = Layout(PackedPlatforms(3));
        var platform = platformBaseline.Items[1];
        Assert.AreSame(platformBaseline, new RoomFurnitureEditOperation(Later, 1, FurnitureEditKind.Move,
            platform.InstanceId, platform).BuildCandidate(platformBaseline));
        var deleted = (operation with { Kind = FurnitureEditKind.Delete, Candidate = null }).BuildCandidate(baseline);
        Assert.AreEqual(2, deleted.Items.Count);
        Assert.IsNull(deleted.Find(target.InstanceId));
        CollectionAssert.AreEqual(new[] { baseline.Items[0], baseline.Items[2] }, deleted.Items.ToArray());
        var insert = new RoomFurnitureEditOperation(Later, 1, FurnitureEditKind.Insert, null, Item(4, FurnitureItemKind.StaticPlatform, 1600, 300));
        var inserted = insert.BuildCandidate(baseline);
        Assert.AreEqual(4, inserted.Items.Count);
        CollectionAssert.AreEqual(baseline.Items.ToArray(), inserted.Items.Take(3).ToArray());
    }

    [TestMethod]
    public void InvalidTargetsTypeChangesStaleRequestsAndDuplicateOperationsCannotModifyItems()
    {
        var baseline = Layout(PackedBalls(3));
        var target = baseline.Items[1];
        var move = new RoomFurnitureEditOperation(Later, 1, FurnitureEditKind.Move, target.InstanceId, target with { Anchor = new(222, 220) });
        foreach (var invalid in new[] { move with { OperationId = Guid.Empty }, move with { OperationId = Operation },
            move with { ExpectedVersion = 0 }, move with { TargetInstanceId = null }, move with { TargetInstanceId = Guid.NewGuid() },
            move with { Candidate = target with { Kind = FurnitureItemKind.StaticPlatform } },
            move with { Candidate = target with { InstanceId = Guid.NewGuid() } }, move with { Candidate = null },
            move with { Kind = FurnitureEditKind.Delete }, move with { Kind = (FurnitureEditKind)999 },
            move with { Kind = FurnitureEditKind.Insert, TargetInstanceId = null, Candidate = target } })
            Assert.ThrowsExactly<InvalidOperationException>(() => invalid.BuildCandidate(baseline));
        Assert.ThrowsExactly<InvalidDataException>(() => (move with { Candidate = target with { Anchor = baseline.Items[0].Anchor } }).BuildCandidate(baseline));
        Assert.ThrowsExactly<OverflowException>(() => (move with { ExpectedVersion = long.MaxValue }).BuildCandidate(baseline with { LayoutVersion = long.MaxValue }));
        Assert.ThrowsExactly<InvalidOperationException>(() => new FurnitureEditOperation(Later, 1, FurnitureEditKind.Insert, null,
            new(Guid.NewGuid(), Room.DefaultAnchor)).BuildCandidate(baseline, Room));
        Assert.AreEqual(1L, baseline.LayoutVersion);
    }

    [TestMethod]
    public void SchemaTwoStrictnessCannotAdmitUnknownRuntimeFieldsThirtyOneOrLegacyMultiItems()
    {
        var bytes = FurnitureLayoutSerializer.Serialize(Layout(PackedBalls(2)), Room);
        foreach (var edit in new Action<JsonObject>[] {
            root => root["schemaVersion"] = 3, root => root["schemaVersion"] = 1,
            root => root["items"]![0]!["presetId"] = "unknown", root => root["items"]![0]!["presetRevision"] = 2,
            root => root["items"]![0]!["velocity"] = 0, root => root["items"]![0]!["anchor"]!["x"] = "90",
            root => root["items"]![1]!["instanceId"] = root["items"]![0]!["instanceId"]!.DeepClone(),
            root => root["items"]![0]!["anchor"]!["z"] = 0, root => root["items"] = null })
        {
            var document = JsonNode.Parse(bytes)!.AsObject(); edit(document);
            Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(document.ToJsonString()), Room));
        }
        var full = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(Layout(PackedPlatforms()), Room))!.AsObject();
        var extra = full["items"]![0]!.DeepClone()!;
        extra["instanceId"] = PackedPlatforms(31)[30].InstanceId.ToString("D");
        extra["anchor"]!["y"] = 126;
        full["items"]!.AsArray().Add(extra);
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(full.ToJsonString()), Room));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(text.Replace("\"x\": 90", "\"x\": 90, \"x\": 91", StringComparison.Ordinal)), Room));
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(text.Replace("\"x\": 90", "\"x\": 1e999", StringComparison.Ordinal)), Room));
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(bytes));
    }

    [TestMethod]
    public void LegacyRoomZeroAndOnePlatformLoadWithoutChangingIdentityVersionOrAnchors()
    {
        foreach (var layout in new[] { FurnitureLayout.Empty, new FurnitureLayout(7, Operation, new(Guid.NewGuid(), Room.DefaultAnchor)) })
        {
            var document = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(layout, Room))!.AsObject();
            document["schemaVersion"] = 1;
            var legacy = Encoding.UTF8.GetBytes(document.ToJsonString());
            var loaded = FurnitureLayoutSerializer.Deserialize(legacy, Room);
            Assert.AreEqual(layout, loaded);
            Assert.AreEqual(2, JsonNode.Parse(FurnitureLayoutSerializer.Serialize(loaded, Room))!["schemaVersion"]!.GetValue<int>());
            Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(legacy));
        }
    }
}
