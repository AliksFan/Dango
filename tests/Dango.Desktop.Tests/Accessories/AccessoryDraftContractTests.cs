using System.Text;
using System.Text.Json.Nodes;
using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryDraftContractTests
{
    private string root = null!;
    [TestInitialize] public void Initialize() => root = Path.Combine(Path.GetTempPath(), "Dango-e1-draft-tests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestMethod]
    public void OpaqueSourceCreatesPendingDraftModalCancelKeepsItAndWholeCancelRemovesIt()
    {
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"));
        var saved = session.SavedConfiguration;
        var imported = session.ImportSource(WritePng("opaque.png", true));
        Assert.IsTrue(imported.IsPendingCompletion);
        Assert.IsNotNull(session.TryGetImage(imported.AssetId));
        Assert.IsFalse(session.TrySave()); StringAssert.Contains(session.StatusMessage, "待完成");
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "settings")));
        var edit = session.BeginEdit(imported.Id);
        session.CancelEdit(edit);
        Assert.AreEqual(imported, session.DraftConfiguration.Items.Single(i => i.Id == imported.Id));
        session.CancelPreview();
        Assert.AreEqual(saved, session.DraftConfiguration);
        Assert.IsNull(session.GetPendingSource(imported.AssetId));
    }

    [TestMethod]
    public void CompleteResultStagesAtomicallyWithoutDiskAndRejectsLateOrWrongRevisionConfirm()
    {
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"));
        var imported = session.ImportSource(WritePng("pending.png", true));
        var edit = session.BeginEdit(imported.Id);
        var image = AccessoryImageAsset.ImportPng(WritePng("final.png", false));
        var result = Result(edit, image);
        session.CancelEdit(edit);
        Assert.IsFalse(session.TryCompleteEdit(edit, result));
        var current = session.BeginEdit(imported.Id);
        Assert.IsFalse(session.TryCompleteEdit(edit, result));
        Assert.IsTrue(session.TryCompleteEdit(current, Result(current, image)));
        Assert.IsFalse(session.DraftConfiguration.Items.Single(i => i.Id == imported.Id).IsPendingCompletion);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "settings")));
        Assert.IsFalse(session.TryCompleteEdit(current, Result(current, image)));
        Assert.IsTrue(session.TrySave(), session.StatusMessage);
    }

    [TestMethod]
    public void ProjectPngCanvasAndRevisionMismatchCannotConstructAnApplicableResult()
    {
        var image = AccessoryImageAsset.ImportPng(WritePng("final.png", false));
        var project = AccessoryEditorProject.FromImage(image, 7);
        var item = new AccessoryItem("fictional", "虚构条目", image.AssetId, image.PixelWidth, image.PixelHeight)
            { ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = 7 };
        _ = new AccessoryEditResult(project, image, item);
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryEditResult(project, image, item with { EditRevision = 6 }));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryEditResult(project, image, item with { AssetId = new string('a', 64) }));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryEditResult(project, image, item with { PixelWidth = 3 }));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryEditResult(project, image, item with { IsPendingCompletion = true }));
        var mismatchedRoot = item with { Rig = new([new(.4, .5), new(.7, .8)]) };
        Assert.ThrowsExactly<InvalidDataException>(() => mismatchedRoot.Validate());
        var requiredRig = item with { DeformationMode = AccessoryDeformationMode.SingleChain };
        Assert.ThrowsExactly<InvalidDataException>(() => requiredRig.Validate());
    }

    [TestMethod]
    public void DuplicateEditHasNewStableIdAndReferencesWhileOriginalAndCancelAreIndependent()
    {
        var settings = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settings);
        var original = session.ImportPng(WritePng("source.png", false));
        session.Stage(new([original])); Assert.IsTrue(session.TrySave());
        var copy = session.Duplicate(original.Id);
        Assert.AreNotEqual(original.Id, copy.Id); Assert.AreEqual(original.ProjectId, copy.ProjectId);
        var edit = session.BeginEdit(copy.Id);
        var image = AccessoryImageAsset.ImportPng(WritePng("changed.png", false, 190));
        Assert.IsTrue(session.TryCompleteEdit(edit, Result(edit, image)));
        var changed = session.DraftConfiguration.Items.Single(i => i.Id == copy.Id);
        Assert.AreNotEqual(original.ProjectId, changed.ProjectId); Assert.AreNotEqual(original.AssetId, changed.AssetId);
        Assert.AreEqual(original, session.DraftConfiguration.Items.Single(i => i.Id == original.Id));
        session.CancelPreview(); Assert.AreEqual(original, session.DraftConfiguration.Items.Single());
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetProjectPath(settings, changed.ProjectId!)));
    }

    [TestMethod]
    public void IndependentSavedDeleteDoesNotCommitOtherIncompleteSourceOrChangedParameters()
    {
        var settings = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settings);
        var original = session.ImportPng(WritePng("source.png", false));
        var keep = original with { Id = "keep-fictional", Name = "保留虚构条目" };
        session.Stage(new([original, keep])); Assert.IsTrue(session.TrySave());
        var pending = session.ImportSource(WritePng("pending.png", true));
        session.Stage(new(session.DraftConfiguration.Items.Select(i => i.Id == keep.Id ? i with { GravityInfluence = 73 } : i)));
        Assert.IsTrue(session.TryDeleteItem(original.Id));
        var disk = new AccessorySettingsSession(settings).SavedConfiguration;
        Assert.AreEqual(1, disk.Items.Count); Assert.AreEqual(0, disk.Items.Single().GravityInfluence);
        Assert.IsTrue(session.DraftConfiguration.Items.Single(i => i.Id == pending.Id).IsPendingCompletion);
        Assert.AreEqual(73, session.DraftConfiguration.Items.Single(i => i.Id == keep.Id).GravityInfluence);
        Assert.IsFalse(session.TrySave());
    }

    [TestMethod]
    public void SupportedImageBrushTextAndNullLayersRoundTripWithoutFlatteningOrMutableLists()
    {
        var image = AccessoryImageAsset.ImportPng(WritePng("final.png", false));
        var source = AccessoryEditorSourceAsset.FromPng(image.CopyPngBytes());
        IReadOnlyList<AccessoryBrushPoint>[] strokes = [new[] { new AccessoryBrushPoint(0, 0, 1), new AccessoryBrushPoint(1, 1, .5) }];
        IReadOnlyList<AccessoryTextSpan>[] lines = [new[] { new AccessoryTextSpan("虚构文字", new("Segoe UI", 18, Bold: true)) }];
        var brush = new AccessoryBrushContent(9, true, strokes);
        var text = new AccessoryTextContent(lines, wrap: "word");
        var project = new AccessoryEditorProject(2, 2, 3, image.AssetId,
            [new("image", "图片", source.AssetId, 0, 0, 2, 2),
             new("brush", "画笔", null, 0, 0, 2, 2, Type: "brush", Brush: brush),
             new("text", "文字", null, 0, 0, 2, 2, Type: "text", Text: text),
             new("empty", "空图层", null, 0, 0, 0, 0, Type: null)], [source]);
        strokes[0] = []; lines[0] = [];
        var restored = AccessoryEditorProject.Read(project.CopyBytes(), project.ProjectId);
        Assert.AreEqual("虚构文字", restored.Layers[2].Text!.Lines[0][0].Text);
        Assert.IsTrue(restored.Layers[2].Text!.Lines[0][0].Meta.Bold);
        Assert.AreEqual(.5, restored.Layers[1].Brush!.Strokes[0][1].Pressure);
        Assert.IsNull(restored.Layers[3].Type);
        CollectionAssert.AreEqual(project.CopyBytes(), restored.CopyBytes());
    }

    [TestMethod]
    public void NativeProjectRejectsExternalReferencesUnknownCodeDuplicateFieldsAndResourceExcess()
    {
        var image = AccessoryImageAsset.ImportPng(WritePng("final.png", false));
        var project = AccessoryEditorProject.FromImage(image);
        var json = JsonNode.Parse(project.CopyBytes())!;
        json["layers"]![0]!["assetId"] = "https://fictional.invalid/a.png";
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorProject.Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
        json = JsonNode.Parse(project.CopyBytes())!; json["layers"]![0]!["render_function"] = "arbitrary-code";
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorProject.Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
        var duplicate = Encoding.UTF8.GetString(project.CopyBytes()).Replace("\"version\":1", "\"version\":1,\"version\":1");
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorProject.Read(Encoding.UTF8.GetBytes(duplicate)));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryEditorProject(2, 2, 0, image.AssetId,
            Enumerable.Range(0, 17).Select(i => new AccessoryProjectLayer("layer-" + i, "虚构图层", image.AssetId, 0, 0, 2, 2)),
            [AccessoryEditorSourceAsset.FromPng(image.CopyPngBytes())]));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryTextContent([[new("虚构", new("https://fictional.invalid/font"))]]));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryBrushContent(9, true, [[new(double.NaN, 0, 1)]]));
    }

    [TestMethod]
    public void AggregatePngHeadersRejectOversizedProjectBeforeAnyPixelDecode()
    {
        var image = AccessoryImageAsset.ImportPng(WritePng("header-budget.png", false));
        var project = AccessoryEditorProject.FromImage(image);
        var json = JsonNode.Parse(project.CopyBytes())!;
        var png = image.CopyPngBytes();
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 4000);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 4000);
        var assets = new JsonArray(); var layers = new JsonArray();
        for (var i = 0; i < 5; i++)
        {
            var id = new string((char)('a' + i), 64);
            assets.Add(new JsonObject { ["id"] = id, ["png"] = Convert.ToBase64String(png) });
            var layer = json["layers"]![0]!.DeepClone(); layer["id"] = "layer-" + i; layer["assetId"] = id; layers.Add(layer);
        }
        json["assets"] = assets; json["layers"] = layers;
        var decodeCount = 0;
        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorProject.Read(
            Encoding.UTF8.GetBytes(json.ToJsonString()), null, bytes => { decodeCount++; return AccessoryEditorSourceAsset.FromPng(bytes); }));
        Assert.AreEqual(0, decodeCount);
        StringAssert.Contains(error.Message, "累计像素");
    }

    [TestMethod]
    [DataRow(AccessoryDeformationMode.Rigid)]
    [DataRow(AccessoryDeformationMode.SingleChain)]
    public void UnbindableRestGeometryCannotBeConfirmedEvenWhileDormant(AccessoryDeformationMode mode)
    {
        var item = new AccessoryItem("fictional", "虚构条目", "builtin-sprout", 64, 64)
        {
            ImagePivotX = .2, ImagePivotY = .2, DeformationMode = mode,
            Rig = new([new(.2, .2), new(.8, .2), new(.8, .8), new(.2, .8)]),
        };
        Assert.ThrowsExactly<InvalidDataException>(() => item.Validate());
    }

    private static AccessoryEditResult Result(AccessoryEditDraft edit, AccessoryImageAsset image)
    {
        var project = AccessoryEditorProject.FromImage(image, edit.Revision);
        return new(project, image, edit.Original with { AssetId = image.AssetId, PixelWidth = image.PixelWidth,
            PixelHeight = image.PixelHeight, ProjectId = project.ProjectId, ProjectVersion = project.Version,
            EditRevision = edit.Revision, IsPendingCompletion = false });
    }

    private string WritePng(string name, bool opaque, byte red = 10)
    {
        Directory.CreateDirectory(root);
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { red, 20, 30, 255, 0, 0, 0, opaque ? (byte)255 : (byte)0, 70, 80, 90, 255, 2, 3, 4, 255 }, 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var path = Path.Combine(root, name); using var stream = File.Create(path); encoder.Save(stream); return path;
    }
}
