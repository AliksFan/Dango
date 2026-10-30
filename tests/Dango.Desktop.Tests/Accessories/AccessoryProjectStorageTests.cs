using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryProjectStorageTests
{
    private string root = null!;
    [TestInitialize] public void Initialize() => root = Path.Combine(Path.GetTempPath(), "Dango-e1-project-tests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestMethod]
    public void LegacyOnlyIsUnloadedAndExplicitNewSavePreservesLegacyBytesAndAssets()
    {
        Directory.CreateDirectory(root);
        var legacy = Encoding.UTF8.GetBytes("{\"accessorySchemaVersion\":1,\"items\":[{\"softness\":83}]}");
        File.WriteAllBytes(AccessorySettingsStore.GetLegacyManifestPath(root), legacy);
        Directory.CreateDirectory(Path.Combine(root, "accessory-assets"));
        var orphan = Path.Combine(root, "accessory-assets", "fictional-old.png");
        File.WriteAllBytes(orphan, [1, 2, 3]);
        var session = new AccessorySettingsSession(root);
        Assert.AreEqual(0, session.DraftConfiguration.Items.Count);
        StringAssert.Contains(session.StatusMessage, "本版未载入");
        Assert.IsFalse(session.IsWriteProtected);
        session.Stage(new([Builtin("new")]));
        Assert.IsTrue(session.TrySave());
        Assert.IsTrue(File.Exists(AccessorySettingsStore.GetManifestPath(root)));
        CollectionAssert.AreEqual(legacy, File.ReadAllBytes(AccessorySettingsStore.GetLegacyManifestPath(root)));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(orphan));
        Assert.AreEqual("new", new AccessorySettingsSession(root).SavedConfiguration.Items.Single().Id);
    }

    [TestMethod]
    [DataRow("{\"accessorySchemaVersion\":99,\"items\":[]}")]
    [DataRow("{bad-json")]
    public void FutureOrBadV2ProtectsOriginalAndNeverFallsBackToLegacy(string invalid)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(AccessorySettingsStore.GetLegacyManifestPath(root), "legacy-fictional");
        File.WriteAllText(AccessorySettingsStore.GetManifestPath(root), invalid);
        var before = SHA256.HashData(File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.IsWriteProtected);
        session.Stage(new([Builtin("draft")]));
        Assert.IsFalse(session.TrySave());
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root))));
        Assert.AreEqual("legacy-fictional", File.ReadAllText(AccessorySettingsStore.GetLegacyManifestPath(root)));
    }

    [TestMethod]
    public void ProjectPngRigAndCommonParametersRoundTripWithoutOldMaterialOrMotionState()
    {
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"));
        var item = session.ImportPng(WritePng("fictional.png"));
        var rig = new AccessoryRigConfiguration([new(.5, .5), new(.6, .8)], .25, 7, 8);
        var changed = item with { Rig = rig, DeformationMode = AccessoryDeformationMode.Rigid, GravityInfluence = 43,
            SizePercent = 230, BaseAngleDegrees = -42, BodyAnchorX = .2, BodyAnchorY = .9,
            ConnectionStrength = 32, MaxConnectionTravelPercent = 140, RotationDamping = 72,
            LockRotation = false, ReturnStrength = 12, MaxSwingDegrees = 112, LimitSwing = false };
        session.Stage(new([changed]));
        var project = session.TryGetProject(changed.Id)!;
        Assert.IsNotNull(project);
        Assert.IsTrue(session.TrySave(), session.StatusMessage);
        var settings = Path.Combine(root, "settings");
        var reopened = new AccessorySettingsSession(settings);
        Assert.AreEqual(changed, reopened.SavedConfiguration.Items.Single());
        var loadedProject = reopened.TryGetProject(changed.Id)!;
        CollectionAssert.AreEqual(project.CopyBytes(), loadedProject.CopyBytes());
        Assert.AreEqual(1, loadedProject.Layers.Count);
        var manifest = File.ReadAllText(AccessorySettingsStore.GetManifestPath(settings));
        Assert.IsFalse(manifest.Contains("maxDeformationPercent")); Assert.IsFalse(manifest.Contains("allowFolding"));
        Assert.IsFalse(manifest.Contains("velocity")); Assert.IsFalse(manifest.Contains("vertices"));
        Assert.IsFalse(manifest.Contains(root));
    }

    [TestMethod]
    public void EveryAssetFailureAndManifestFailurePreservesOldManifestAndRetryableDraft()
    {
        for (var failAt = 1; failAt <= 3; failAt++)
        {
            var settings = Path.Combine(root, "failure-" + failAt);
            new AccessorySettingsStore().Save(settings, new([Builtin("saved")]));
            var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settings));
            var writes = 0; var failing = true;
            var store = new AccessorySettingsStore(new DelegateWriter((path, text) =>
            { if (++writes == failAt && failing) throw new IOException("Injected fictional manifest failure."); new AtomicTextFileWriter().Write(path, text); }),
                (path, bytes) => { if (++writes == failAt && failing) throw new IOException("Injected fictional asset failure."); File.WriteAllBytes(path, bytes); });
            var session = new AccessorySettingsSession(settings, store: store);
            var imported = session.ImportPng(WritePng("failure-source-" + failAt + ".png"));
            var draft = session.DraftConfiguration;
            Assert.IsFalse(session.TrySave(), "Expected failure position " + failAt);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settings)));
            Assert.AreEqual(draft, session.DraftConfiguration);
            Assert.IsNotNull(session.TryGetProject(imported.Id));
            failing = false;
            Assert.IsTrue(session.TrySave(), session.StatusMessage);
        }
    }

    [TestMethod]
    [DataRow(".png")]
    [DataRow(".json")]
    public void TruncatedAssetOutputCannotAdvanceManifestOrDiscardDraft(string extension)
    {
        var settings = Path.Combine(root, "settings");
        new AccessorySettingsStore().Save(settings, new([Builtin("saved")]));
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settings));
        var store = new AccessorySettingsStore(assetWriter: (path, bytes) =>
            File.WriteAllBytes(path, Path.GetExtension(path) == extension ? bytes[..7] : bytes));
        var session = new AccessorySettingsSession(settings, store: store);
        var item = session.ImportPng(WritePng("truncated.png"));
        var draft = session.DraftConfiguration;
        Assert.IsFalse(session.TrySave());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settings)));
        Assert.AreEqual(draft, session.DraftConfiguration);
        Assert.IsNotNull(session.TryGetProject(item.Id));
        Assert.IsNotNull(session.TryGetImage(item.AssetId));
        // Unreferenced failed output stays in place; this layer does not collect or delete assets.
        var failedPath = extension == ".png" ? AccessorySettingsStore.GetAssetPath(settings, item.AssetId) :
            AccessorySettingsStore.GetProjectPath(settings, item.ProjectId!);
        Assert.AreEqual(7, new FileInfo(failedPath).Length);
    }

    [TestMethod]
    public void WriterThrowAfterManifestReplacementIsConfirmedAsCommitted()
    {
        var store = new AccessorySettingsStore(new DelegateWriter((path, text) =>
        { new AtomicTextFileWriter().Write(path, text); throw new IOException("Injected after replace."); }));
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"), store: store);
        session.ImportPng(WritePng("after.png"));
        Assert.IsTrue(session.TrySave(), session.StatusMessage);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public void ExternalRawRevisionChangeIsRejectedBeforeAndDuringAssetPreparation()
    {
        var settings = Path.Combine(root, "settings");
        new AccessorySettingsStore().Save(settings, new([Builtin("saved")]));
        var session = new AccessorySettingsSession(settings);
        session.Stage(new([Builtin("changed")]));
        File.AppendAllText(AccessorySettingsStore.GetManifestPath(settings), " ");
        Assert.IsFalse(session.TrySave());
        Assert.IsFalse(session.TryDeleteItem("saved"));
        Assert.AreEqual("changed", session.DraftConfiguration.Items.Single().Id);

        var mutated = false;
        var store = new AccessorySettingsStore(assetWriter: (path, bytes) =>
        {
            File.WriteAllBytes(path, bytes);
            if (!mutated) { File.AppendAllText(AccessorySettingsStore.GetManifestPath(settings), "\n"); mutated = true; }
        });
        var second = new AccessorySettingsSession(settings, store: store);
        second.ImportPng(WritePng("race.png"));
        Assert.IsFalse(second.TrySave());
        Assert.AreEqual("saved", new AccessorySettingsSession(settings).SavedConfiguration.Items.Single().Id);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExternalWriteAfterOurReplacementCannotBecomeOurBaseline(bool delete)
    {
        var settings = Path.Combine(root, "settings");
        new AccessorySettingsStore().Save(settings, new([Builtin("remove"), Builtin("keep")]));
        var store = new AccessorySettingsStore(new DelegateWriter((path, text) =>
        {
            new AtomicTextFileWriter().Write(path, text);
            var external = JsonNode.Parse(text)!;
            external["items"]![0]!["name"] = "外部虚构修改";
            new AtomicTextFileWriter().Write(path, external.ToJsonString());
        }));
        var session = new AccessorySettingsSession(settings, store: store);
        if (delete) Assert.IsTrue(session.TryDeleteItem("remove"));
        else { session.Stage(new([Builtin("keep")])); Assert.IsTrue(session.TrySave()); }
        var externalBytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settings));
        session.Stage(new([Builtin("later")]));
        Assert.IsFalse(session.TrySave());
        CollectionAssert.AreEqual(externalBytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settings)));
    }

    [TestMethod]
    public void CorruptProjectAndPngRemainIsolatedAndReferencedDuringUnrelatedSave()
    {
        var settings = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settings);
        var item = session.ImportPng(WritePng("isolated.png"));
        Assert.IsTrue(session.TrySave());
        File.WriteAllText(AccessorySettingsStore.GetProjectPath(settings, item.ProjectId!), "broken-fictional");
        File.WriteAllBytes(AccessorySettingsStore.GetAssetPath(settings, item.AssetId), [4, 5, 6]);
        var reopened = new AccessorySettingsSession(settings);
        Assert.IsNull(reopened.TryGetProject(item.Id)); Assert.IsNull(reopened.TryGetImage(item.AssetId));
        reopened.Stage(new(reopened.DraftConfiguration.Items.Select(i => i.Id == item.Id ? i with { Name = "保留的虚构条目" } : i)));
        Assert.IsTrue(reopened.TrySave(), reopened.StatusMessage);
        Assert.IsTrue(new AccessorySettingsSession(settings).SavedConfiguration.Items.Any(i => i.Id == item.Id));
        Assert.AreEqual("broken-fictional", File.ReadAllText(AccessorySettingsStore.GetProjectPath(settings, item.ProjectId!)));
    }

    internal string WritePng(string name, bool opaque = false)
    {
        Directory.CreateDirectory(root);
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 10, 20, 30, 255, 40, 50, 60, opaque ? (byte)255 : (byte)0, 70, 80, 90, 255, 2, 3, 4, 255 }, 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(root, name); using var output = File.Create(path); encoder.Save(output); return path;
    }
    private static AccessoryItem Builtin(string id) => new(id, "虚构条目", "builtin-sprout", 64, 64);
    private sealed class DelegateWriter(Action<string, string> write) : IAtomicTextWriter
    { public void Write(string path, string content) => write(path, content); }
}
