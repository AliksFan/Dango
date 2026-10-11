using System.Text;
using System.Text.Json.Nodes;
using Dango.Core.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Furniture;

[TestClass]
public sealed class FurnitureLayoutTests
{
    private static readonly Guid Instance = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Operation = Guid.Parse("20000000-0000-4000-8000-000000000002");

    [TestMethod]
    public void EmptyAndSinglePlatformRoundTripWithoutRuntimeFields()
    {
        foreach (var expected in new[] { FurnitureLayout.Empty, ValidLayout(), new FurnitureLayout(2, Operation, null) })
        {
            var bytes = FurnitureLayoutSerializer.Serialize(expected);
            Assert.AreEqual(expected, FurnitureLayoutSerializer.Deserialize(bytes));
            var json = Encoding.UTF8.GetString(bytes);
            foreach (var forbidden in new[] { "body", "velocity", "camera", "physicsStep", "sceneRevision", "friction", "width", "height" })
                Assert.IsFalse(json.Contains('"' + forbidden + '"', StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void TopCenterAndBoundsAreFrozenInWorldUnits()
    {
        Assert.AreEqual(1.2, LocalFurnitureSceneGeometry.DefaultAnchor.Y - LocalFurnitureSceneGeometry.PlatformHeight / 2, 1e-12);
        LocalFurnitureSceneGeometry.ValidateAnchor(new(6.3, 6));
        LocalFurnitureSceneGeometry.ValidateAnchor(new(0, LocalFurnitureSceneGeometry.Ground + LocalFurnitureSceneGeometry.PlatformHeight));
        LocalFurnitureSceneGeometry.ValidateAnchor(new(-2, 1)); // Touching the right of the spawn zone.
        Assert.ThrowsExactly<InvalidDataException>(() => LocalFurnitureSceneGeometry.ValidateAnchor(new(-2.000001, 1)));
        Assert.ThrowsExactly<InvalidDataException>(() => LocalFurnitureSceneGeometry.ValidateAnchor(new(6.300001, 6)));
        Assert.ThrowsExactly<InvalidDataException>(() => LocalFurnitureSceneGeometry.ValidateAnchor(new(0, 6.000001)));
        Assert.ThrowsExactly<InvalidDataException>(() => LocalFurnitureSceneGeometry.ValidateAnchor(new(0, -.370001)));
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDocuments))]
    public void StrictSchemaRejectsTheWholeInvalidLayout(string text)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(Encoding.UTF8.GetBytes(text)));
    }

    public static IEnumerable<object[]> InvalidDocuments()
    {
        var valid = Encoding.UTF8.GetString(FurnitureLayoutSerializer.Serialize(ValidLayout()));
        foreach (var field in new[] { "schemaVersion", "sceneId", "coordinateSpace", "sceneGeometryRevision", "layoutVersion", "lastOperationId", "items" })
        {
            var removed = JsonNode.Parse(valid)!.AsObject();
            removed.Remove(field);
            yield return [removed.ToJsonString()];
            var changed = JsonNode.Parse(valid)!.AsObject();
            changed[field] = true;
            yield return [changed.ToJsonString()];
        }
        foreach (var changed in new Action<JsonObject>[]
        {
            root => root["future"] = 1,
            root => root["schemaVersion"] = 2,
            root => root["coordinateSpace"] = "ball-q-v1",
            root => root["sceneGeometryRevision"] = 2,
            root => root["layoutVersion"] = -1,
            root => root["layoutVersion"] = 0,
            root => root["lastOperationId"] = null,
            root => root["lastOperationId"] = Guid.Empty.ToString("D"),
            root => root["items"]!.AsArray().Add(root["items"]![0]!.DeepClone()),
            root => root["items"]![0]!["instanceId"] = Guid.Empty.ToString("D"),
            root => root["items"]![0]!["presetId"] = "official-ball",
            root => root["items"]![0]!["presetRevision"] = 2,
            root => root["items"]![0]!["size"] = 2,
            root => root["items"]![0]!["anchor"]!["x"] = "0",
            root => root["items"]![0]!["anchor"]!["y"] = 6.1,
            root => root["items"]![0]!["anchor"]!["x"] = -5,
            root => root["items"]![0]!["anchor"]!["z"] = 0,
        })
        {
            var root = JsonNode.Parse(valid)!.AsObject();
            changed(root);
            yield return [root.ToJsonString()];
        }
        yield return [valid[..^1] + ",\"layoutVersion\":1}"];
        yield return [valid.Replace("\"x\": 0", "\"x\": 0, \"x\": 1", StringComparison.Ordinal)];
        yield return [valid.Replace("\"x\": 0", "\"x\": 1e999", StringComparison.Ordinal)];
        yield return ["null"];
        yield return ["[]"];
        yield return ["{}"];
    }

    [TestMethod]
    public void SizeBudgetAndNonFiniteCoordinatesCannotBeWrittenOrLoaded()
    {
        var valid = FurnitureLayoutSerializer.Serialize(ValidLayout());
        var tooLarge = new byte[FurnitureLayoutSerializer.MaximumBytes + 1];
        Array.Fill(tooLarge, (byte)' ');
        valid.CopyTo(tooLarge, 0);
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize(tooLarge));
        Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Deserialize([]));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.ThrowsExactly<InvalidDataException>(() => FurnitureLayoutSerializer.Serialize(
                new(1, Operation, new(Instance, new(value, 1)))));
    }

    private static FurnitureLayout ValidLayout() => new(1, Operation, new(Instance, LocalFurnitureSceneGeometry.DefaultAnchor));
}
