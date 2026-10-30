using System.Text.Json.Nodes;
using Dango.Core.Animation;
using Dango.Core.Assets;
using Dango.Core.Coordinates;
using Dango.Core.Scene;
using Dango.Core.Tests.Scene.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Assets;

[TestClass]
public sealed class AssetPackageParserTests
{
    [TestMethod]
    public void AuthoredFallbackCandidateCoversEveryVisibleActionWithoutReplacingPlaceholder()
    {
        var candidatePath = Path.Combine(
            TestRepository.Root,
            "assets",
            "preproduction",
            "phase-1-dango",
            "candidate.asset.json");
        var candidateRoot = Path.GetDirectoryName(candidatePath)!;
        var package = AssetPackageParser.Parse(File.ReadAllText(candidatePath));

        var visibleStates = Enum.GetValues<DangoActionState>()
            .Where(state => state != DangoActionState.Hidden)
            .ToArray();

        CollectionAssert.AreEquivalent(visibleStates, package.Actions.Keys.ToArray());
        Assert.IsFalse(package.Actions.ContainsKey(DangoActionState.Hidden));
        Assert.IsTrue(package.Layers.All(layer =>
            File.Exists(Path.Combine(candidateRoot, layer.Source.Replace('/', Path.DirectorySeparatorChar)))));
        Assert.IsFalse(package.Layers.Any(layer => layer.SlotId == "shadow"));

        foreach (var start in package.Fallbacks.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (package.Fallbacks.TryGetValue(current, out var next))
            {
                Assert.IsTrue(visited.Add(current), $"Fallback cycle starts at {start}.");
                current = next;
            }
        }

        var placeholder = AssetPackageParser.Parse(ValidAssetJson());
        Assert.AreEqual("placeholder.txt", placeholder.Layers.Single().Source);
    }

    [TestMethod]
    public void RejectsAnUnsupportedAssetVersion()
    {
        var json = Mutate(root => root["assetVersion"] = 999);

        AssertInvalid(json, "assetVersion");
    }

    [TestMethod]
    public void RejectsAnActionThatReferencesAMissingClip()
    {
        var json = Mutate(root => root["actions"]!["Idle"] = "missing-clip");

        AssertInvalid(json, "missing-clip");
    }

    [TestMethod]
    public void RejectsALayerThatReferencesAnUnknownSlot()
    {
        var json = Mutate(root => root["layers"]![0]!["slot"] = "unknown-slot");

        AssertInvalid(json, "unknown-slot");
    }

    [TestMethod]
    public void RejectsAHitShapeOutsideTheLogicalCanvas()
    {
        var json = Mutate(root => root["hitShapes"]![0]!["bounds"]!["x"] = 1000);

        AssertInvalid(json, "hitShapes");
    }

    [TestMethod]
    public void RejectsAnInvalidCanvasWithoutLeakingAnImplementationException()
    {
        var json = Mutate(root => root["canvas"]!["width"] = 0);

        AssertInvalid(json, "canvas");
    }

    [TestMethod]
    public void RejectsALayerSourceThatEscapesTheAssetPackage()
    {
        var json = Mutate(root => root["layers"]![0]!["source"] = "../private.txt");

        AssertInvalid(json, "source");
    }

    [TestMethod]
    public void RejectsAFallbackCycle()
    {
        var json = Mutate(root =>
        {
            root["fallbacks"] = new JsonArray(
                new JsonObject { ["from"] = "idle", ["to"] = "reacting" },
                new JsonObject { ["from"] = "reacting", ["to"] = "idle" });
        });

        AssertInvalid(json, "fallback");
    }

    [TestMethod]
    public void PlaceholderAssetProducesADeterministicSceneSnapshot()
    {
        var package = AssetPackageParser.Parse(ValidAssetJson());
        var primary = new DisplayWorkArea(
            "primary",
            new LogicalRect(0, 0, 1920, 1040),
            new PixelRect(0, 0, 1920, 1040),
            DpiScale.Identity);
        var layout = new DisplayLayout([primary], primary.Id);

        var first = PlaceholderSceneFactory.Create(package, layout, 42, 1000);
        var second = PlaceholderSceneFactory.Create(package, layout, 42, 1000);

        Assert.AreEqual(first, second);
        Assert.AreEqual(SceneSnapshot.CurrentVersion, first.SnapshotVersion);
        Assert.AreEqual("idle", first.Dango.ClipId);
        Assert.IsTrue(primary.LogicalBounds.Contains(first.Dango.VisualBounds));
        Assert.IsTrue(primary.LogicalBounds.Contains(first.Island.VisualBounds));
    }

    private static void AssertInvalid(string json, string expectedMessageFragment)
    {
        var exception = Assert.ThrowsExactly<AssetValidationException>(
            () => AssetPackageParser.Parse(json));

        StringAssert.Contains(exception.Message, expectedMessageFragment);
    }

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ValidAssetJson())!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static string ValidAssetJson() =>
        File.ReadAllText(Path.Combine(TestRepository.Root, "assets", "placeholder", "phase-1.asset.json"));
}
