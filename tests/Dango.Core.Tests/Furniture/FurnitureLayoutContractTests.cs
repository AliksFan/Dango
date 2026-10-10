using System.Text;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Furniture;

[TestClass]
public sealed class FurnitureLayoutContractTests
{
    private static readonly Guid Instance = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Operation = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid Later = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static FurnitureLayoutContract Room => FurnitureLayoutContract.OrdinaryRoom;

    [TestMethod]
    public void DefaultContractPreservesTheTrialBytesAndGeometry()
    {
        var trial = FurnitureLayoutContract.Trial;
        var layout = Layout(trial);
        CollectionAssert.AreEqual(FurnitureLayoutSerializer.Serialize(layout), FurnitureLayoutSerializer.Serialize(layout, trial));
        Assert.AreEqual(FurnitureLayoutSerializer.SceneId, trial.SceneId);
        Assert.AreEqual(FurnitureLayoutSerializer.CoordinateSpace, trial.CoordinateSpace);
        Assert.AreEqual(FurnitureLayoutSerializer.SceneGeometryRevision, trial.SceneGeometryRevision);
        Assert.AreEqual(LocalFurnitureSceneGeometry.PlatformWidth, trial.PlatformWidth);
        Assert.AreEqual(LocalFurnitureSceneGeometry.PlatformHeight, trial.PlatformHeight);
        Assert.AreEqual(LocalFurnitureSceneGeometry.DefaultAnchor, trial.DefaultAnchor);
        Assert.AreEqual("furniture-layout.json", trial.LayoutRelativePath);
        Assert.ThrowsExactly<InvalidDataException>(() => Layout(Room).Validate());
    }

    [TestMethod]
    public void OrdinaryRoomGeometryIsInFixedFloorHeightUnits()
    {
        Assert.AreEqual("local-ordinary-room", Room.SceneId);
        Assert.AreEqual("room-floor-height-v1", Room.CoordinateSpace);
        Assert.AreEqual(1, Room.SceneGeometryRevision);
        Assert.AreEqual("rooms/local-ordinary-room/furniture-layout.json", Room.LayoutRelativePath);
        Assert.AreEqual(2160d, Room.SurfaceWidth);
        Assert.AreEqual(360d, Room.SurfaceHeight);
        Assert.AreEqual(272d, Room.PlatformWidth);
        Assert.AreEqual(21d, Room.PlatformHeight);
        Assert.AreEqual(.72, Room.PlatformFriction);
        Assert.AreEqual(new FurnitureAnchor(1440, 120), Room.DefaultAnchor);
        Assert.AreEqual(4d, Room.NudgeStep);
        Assert.AreEqual(20, Room.MaxNudgePerSecond);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EachContractRoundTripsEmptyAndOccupiedAndRejectsTheOther(bool occupied)
    {
        foreach (var contract in new[] { FurnitureLayoutContract.Trial, Room })
        {
            var layout = occupied ? Layout(contract) : FurnitureLayout.Empty;
            var bytes = FurnitureLayoutSerializer.Serialize(layout, contract);
            Assert.AreEqual(layout, FurnitureLayoutSerializer.Deserialize(bytes, contract));
            var other = ReferenceEquals(contract, Room) ? FurnitureLayoutContract.Trial : Room;
            Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(bytes, other));
            var json = Encoding.UTF8.GetString(bytes);
            foreach (var forbidden in new[] { "surfaceWidth", "surfaceHeight", "body", "velocity", "camera", "dpi", "origin" })
                Assert.IsFalse(json.Contains('"' + forbidden + '"', StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow("sceneId")]
    [DataRow("coordinateSpace")]
    [DataRow("sceneGeometryRevision")]
    public void RoomIdentifiersAreStrictEvenForEmptyFiles(string field)
    {
        var root = JsonNode.Parse(FurnitureLayoutSerializer.Serialize(FurnitureLayout.Empty, Room))!.AsObject();
        root[field] = field == "sceneGeometryRevision" ? JsonValue.Create(2) : JsonValue.Create("unknown");
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()), Room));
    }

    [TestMethod]
    public void RoomStrictSchemaRejectsUnknownDuplicateInvalidNumbersAndExcessItems()
    {
        var valid = Encoding.UTF8.GetString(FurnitureLayoutSerializer.Serialize(Layout(Room), Room));
        foreach (var change in new Action<JsonObject>[]
        {
            root => root["schemaVersion"] = 2,
            root => root["unexpected"] = 1,
            root => root.Remove("lastOperationId"),
            root => root["layoutVersion"] = "1",
            root => root["lastOperationId"] = Guid.Empty.ToString("D"),
            root => root["items"]!.AsArray().Add(root["items"]![0]!.DeepClone()),
            root => root["items"]![0]!["presetRevision"] = 2,
            root => root["items"]![0]!["anchor"]!["x"] = "1440",
            root => root["items"]![0]!["anchor"]!["y"] = 20,
            root => root["items"]![0]!["anchor"]!["z"] = 0,
        })
        {
            var root = JsonNode.Parse(valid)!.AsObject();
            change(root);
            Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()), Room));
        }
        foreach (var invalid in new[]
        {
            valid[..^1] + ",\"layoutVersion\":1}",
            valid.Replace("\"x\": 1440", "\"x\": 1440, \"x\": 1441", StringComparison.Ordinal),
            valid.Replace("\"x\": 1440", "\"x\": 1e999", StringComparison.Ordinal),
        })
            Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(invalid), Room));
    }

    [TestMethod]
    [DataRow(160d, 21d)] // Complete platform touches the left and floor.
    [DataRow(2000d, 360d)] // Complete platform touches the right and ceiling.
    [DataRow(764d, 120d)] // Touches the left of the fixed spawn protection.
    [DataRow(1396d, 120d)] // Touches the right of the fixed spawn protection.
    [DataRow(1080d, 221d)] // Touches the top of the fixed spawn protection.
    public void RoomEdgesAllowTouchingWithNoPositiveAreaOverlap(double x, double y) => Room.ValidateAnchor(new(x, y));

    [TestMethod]
    [DataRow(159.999999, 21d)]
    [DataRow(2000.000001, 360d)]
    [DataRow(1440d, 20.999999)]
    [DataRow(1440d, 360.000001)]
    [DataRow(764.000001, 120d)]
    [DataRow(1395.999999, 120d)]
    [DataRow(1080d, 220.999999)]
    public void RoomBoundsAndSpawnOverlapRejectTheWholePlatform(double x, double y) =>
        Assert.ThrowsExactly<InvalidDataException>(() => Room.ValidateAnchor(new(x, y)));

    [TestMethod]
    public void RoomRejectsEveryNonFiniteCoordinateAndInvalidInstance()
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsExactly<InvalidDataException>(() => Room.ValidateAnchor(new(value, 120)));
            Assert.ThrowsExactly<InvalidDataException>(() => Room.ValidateAnchor(new(1440, value)));
        }
        Assert.ThrowsExactly<InvalidDataException>(() => new FurniturePlatformInstance(Guid.Empty, Room.DefaultAnchor).Validate(Room));
    }

    [TestMethod]
    public void RoomOperationsValidateBothBaselineAndCandidateWithTheSameContract()
    {
        var platform = Layout(Room).Platform!;
        var insert = new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, null, platform);
        var inserted = insert.BuildCandidate(FurnitureLayout.Empty, Room);
        Assert.AreEqual(Layout(Room), inserted);
        Assert.ThrowsExactly<InvalidDataException>(() => insert.BuildCandidate(FurnitureLayout.Empty));
        var moved = platform with { Anchor = new(1640, 180) };
        var move = new FurnitureEditOperation(Later, 1, FurnitureEditKind.Move, Instance, moved);
        Assert.AreEqual(new FurnitureLayout(2, Later, moved), move.BuildCandidate(inserted, Room));
        var delete = new FurnitureEditOperation(Later, 1, FurnitureEditKind.Delete, Instance, null);
        Assert.AreEqual(new FurnitureLayout(2, Later, null), delete.BuildCandidate(inserted, Room));
        Assert.AreEqual(platform, inserted.Platform);
        Assert.ThrowsExactly<InvalidDataException>(() => delete.BuildCandidate(Layout(FurnitureLayoutContract.Trial), Room));
        Assert.ThrowsExactly<InvalidDataException>(() => (move with { Candidate = moved with { Anchor = new(1080, 120) } }).BuildCandidate(inserted, Room));
    }

    [TestMethod]
    public void RoomOperationsRejectStaleWrongTargetDuplicateAndOverflow()
    {
        var baseline = Layout(Room);
        var move = new FurnitureEditOperation(Later, 1, FurnitureEditKind.Move, Instance, baseline.Platform);
        foreach (var invalid in new[]
        {
            move with { ExpectedVersion = 0 }, move with { TargetInstanceId = Later },
            move with { Candidate = baseline.Platform! with { InstanceId = Later } }, move with { OperationId = Operation },
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => invalid.BuildCandidate(baseline, Room));
        Assert.ThrowsExactly<OverflowException>(() => (move with { ExpectedVersion = long.MaxValue })
            .BuildCandidate(baseline with { LayoutVersion = long.MaxValue }, Room));
        Assert.AreEqual(1L, baseline.LayoutVersion);
    }

    private static FurnitureLayout Layout(FurnitureLayoutContract contract) => new(1, Operation, new(Instance, contract.DefaultAnchor));
}
