using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryBrowserProjectAdapterTests
{
    private static JsonObject Fixture() => JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepository.Root,
        "tests", "Dango.Desktop.Tests", "Accessories", "Fixtures", "AccessoryBrowserProjectV1.json")))!.AsObject();
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static AccessoryEditDraft Draft() => new("fictional-session", 1, "fictional-item", 7,
        new("fictional-item", "虚构配饰", new string('a', 64), 2, 2) { IsPendingCompletion = true });
    private static AccessoryEditResult Read(AccessoryBrowserProjectAdapter adapter, JsonObject fixture, AccessoryItem? parameters = null)
    {
        var draft = Draft();
        return adapter.ReadResult(Element(fixture["project"]!), Element(fixture["png"]!), draft, parameters ?? draft.Original);
    }

    [TestMethod]
    public void IndependentBrowserFixtureImportsTypedContentAndActualMiniPaintDefaults()
    {
        var result = Read(new(), Fixture(), Draft().Original with { GravityInfluence = 73, ImagePivotX = .25 });
        Assert.AreEqual(7L, result.Project.Revision);
        Assert.AreEqual(result.Image.AssetId, result.Project.FinalAssetId);
        Assert.AreEqual(result.Project.ProjectId, result.Item.ProjectId);
        Assert.AreEqual(73d, result.Item.GravityInfluence);
        Assert.AreEqual(.25, result.Item.ImagePivotX);
        Assert.IsFalse(result.Item.IsPendingCompletion);
        CollectionAssert.AreEqual(new string?[] { "brush", "text", "image", null }, result.Project.Layers.Select(l => l.Type).ToArray());
        Assert.IsTrue(result.Project.Layers.All(l => l.Opacity == .375));
        var brush = result.Project.Layers[0].Brush!;
        Assert.AreEqual(3d, brush.Size); Assert.IsTrue(brush.Pressure);
        Assert.AreEqual(new AccessoryBrushPoint(1, 1, .5), brush.Strokes.Single()[1]);
        var text = result.Project.Layers[1].Text!;
        Assert.AreEqual("Fictional text", text.Lines[0][0].Text);
        Assert.AreEqual(40d, text.Lines[0][0].Meta.Size);
        Assert.AreEqual("#008800", text.Lines[0][0].Meta.FillColor);
        Assert.AreEqual(18d, text.Lines[0][1].Meta.Size);
        Assert.IsTrue(text.Lines[0][1].Meta.Bold);
        Assert.AreEqual("center", text.HorizontalAlign);
        Assert.AreEqual("box", text.Boundary);
        Assert.AreEqual(result.Project.Assets.Keys.Single(), result.Project.Layers[2].AssetId);
    }

    [TestMethod]
    public void NativeOpenUsesBrowserUnitsAndAliasesAndReopenKeepsEditableVectorsAndNativeIds()
    {
        var fixture = Fixture();
        var initial = Read(new(), fixture);
        var native = new AccessoryEditorProject(2, 2, 6, initial.Image.AssetId,
            initial.Project.Layers.Select((l, i) => l with { Id = "native-stable-" + i }), initial.Project.Assets.Values);
        var adapter = new AccessoryBrowserProjectAdapter();
        var opened = adapter.CreateOpenProject(native);
        Assert.AreEqual(1, opened.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(37.5, opened.GetProperty("layers")[0].GetProperty("opacity").GetDouble());
        Assert.AreEqual("asset-1", opened.GetProperty("layers")[2].GetProperty("assetId").GetString());
        Assert.AreEqual("brush", opened.GetProperty("layers")[0].GetProperty("render_function")[0].GetString());
        fixture["project"] = JsonNode.Parse(opened.GetRawText());
        var reopened = Read(adapter, fixture);
        CollectionAssert.AreEqual(native.Layers.Select(l => l.Id).ToArray(), reopened.Project.Layers.Select(l => l.Id).ToArray());
        Assert.AreEqual(native.Layers[0].Brush!.Strokes[0][1], reopened.Project.Layers[0].Brush!.Strokes[0][1]);
        Assert.AreEqual(native.Layers[1].Text!.Lines[0][1], reopened.Project.Layers[1].Text!.Lines[0][1]);
    }

    [TestMethod]
    public void PendingOpaqueSourceOpensButCannotConfirmOpaqueFinal()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 }, 8);
        bitmap.Freeze();
        var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        var adapter = new AccessoryBrowserProjectAdapter();
        var opened = adapter.CreateOpenProject(source);
        Assert.AreEqual(2, opened.GetProperty("width").GetInt32());
        var png = JsonSerializer.SerializeToElement(new { width = 2, height = 2, pngBase64 = Convert.ToBase64String(source.CopyPngBytes()) });
        var draft = Draft();
        Assert.ThrowsExactly<InvalidDataException>(() => adapter.ReadResult(opened, png, draft, draft.Original));
    }

    [TestMethod]
    public void ChangedLayerOrderIsCapturedAndDuplicateBrowserImageAliasesDeduplicateByNativeHash()
    {
        var fixture = Fixture();
        var project = fixture["project"]!.AsObject();
        var image = project["layers"]![0]!.DeepClone(); image["id"] = 9; image["order"] = 1; image["assetId"] = "asset-2";
        project["layers"]!.AsArray().Add(image);
        var asset = project["assets"]![0]!.DeepClone(); asset["id"] = "asset-2";
        project["assets"]!.AsArray().Add(asset);
        var result = Read(new(), fixture);
        Assert.AreEqual("browser-layer-9", result.Project.Layers[0].Id);
        Assert.AreEqual(1, result.Project.Assets.Count);
        Assert.AreEqual(2, result.Project.Layers.Count(l => l.Type == "image"));
    }

    [TestMethod]
    public void EqualBrowserStackOrdersFollowMiniPaintReverseRenderingOrderOnReopen()
    {
        var fixture = Fixture();
        foreach (var layer in fixture["project"]!["layers"]!.AsArray()) layer!["order"] = 1;
        var adapter = new AccessoryBrowserProjectAdapter();
        var imported = Read(adapter, fixture);
        CollectionAssert.AreEqual(new[] { "browser-layer-11", "browser-layer-6", "browser-layer-2", "browser-layer-8" },
            imported.Project.Layers.Select(l => l.Id).ToArray());
        fixture["project"] = JsonNode.Parse(adapter.CreateOpenProject(imported.Project).GetRawText());
        var reopened = Read(adapter, fixture);
        CollectionAssert.AreEqual(imported.Project.Layers.Select(l => l.Id).ToArray(), reopened.Project.Layers.Select(l => l.Id).ToArray());
    }

    [TestMethod]
    public void InvalidBrowserFieldsReferencesDimensionsAndParametersAreRejected()
    {
        Action<JsonObject>[] mutations =
        [
            f => f["project"]!["schemaVersion"] = 2,
            f => f["project"]!["width"] = 3,
            f => f["png"]!["height"] = 3,
            f => f["project"]!["externalUrl"] = "https://example.invalid/image.png",
            f => f["project"]!["layers"]![0]!["link"] = "file:///fictional.png",
            f => f["project"]!["layers"]![0]!["assetId"] = "asset-999",
            f => f["project"]!["layers"]![0]!["id"] = 2,
            f => f["project"]!["layers"]![0]!["opacity"] = 101,
            f => f["project"]!["layers"]![0]!["composition"] = "multiply",
            f => f["project"]!["layers"]![1]!["params"]!["size"] = 0,
            f => f["project"]!["layers"]![2]!["data"]![0]![0]!["meta"]!["family"] = "remote-font",
            f => f["project"]!["layers"]![2]!["data"]![0]![0]!["meta"]!["unknown"] = true,
            f => f["project"]!["layers"]![3]!["width"] = 1,
            f => f["project"]!["assets"]!.AsArray().Clear(),
            f => f["project"]!["layers"]!.AsArray().RemoveAt(0),
        ];
        foreach (var mutate in mutations)
        {
            var fixture = Fixture(); mutate(fixture);
            Assert.ThrowsExactly<InvalidDataException>(() => Read(new(), fixture));
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Read(new(), Fixture(), Draft().Original with { Id = "wrong-item" }));
        Assert.ThrowsExactly<InvalidDataException>(() => Read(new(), Fixture(), Draft().Original with { GravityInfluence = 101 }));
    }

    [TestMethod]
    public void DuplicateNestedFieldsAreRejectedBeforeNativeConstruction()
    {
        var fixture = Fixture();
        var raw = fixture["project"]!.ToJsonString().Replace("\"size\":3", "\"size\":3,\"size\":4", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(raw);
        var draft = Draft();
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryBrowserProjectAdapter().ReadResult(
            document.RootElement, Element(fixture["png"]!), draft, draft.Original));
    }
}
