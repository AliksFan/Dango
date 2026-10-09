using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryDeletionTests
{
    private string root = null!;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void CreateEvidenceRoot() => root = Path.Combine(AppContext.BaseDirectory,
        "accessory-deletion-evidence", TestContext.TestName!, Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void SavedDeletionCommitsOnlyRemovalAndPreservesOtherDraftChangesAndPendingImages()
    {
        var writer = new ControlledWriter();
        var assetWrites = 0;
        var store = new AccessorySettingsStore(writer, (path, bytes) =>
        {
            assetWrites++;
            File.WriteAllBytes(path, bytes);
        });
        var settingsRoot = Path.Combine(root, "settings");
        var saved = new AccessoryConfiguration([BuiltIn("remove"), BuiltIn("keep")]);
        store.Save(settingsRoot, saved);
        var session = new AccessorySettingsSession(settingsRoot, store: store);
        var pending = session.ImportPng(WriteSource("pending.png"));
        var pendingCopy = session.Duplicate(pending.Id);
        session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(item =>
            item.Id == "keep" ? item with { Name = "未应用名称", BodyAnchorX = .23, Softness = 72 } : item)));
        var expectedDraft = Without(session.DraftConfiguration, "remove");
        var expectedSaved = Without(saved, "remove");
        var pendingBytes = session.PendingPngBytes;
        var writesBefore = writer.WriteCount;

        Assert.IsTrue(session.TryDeleteItem("remove"));

        Assert.AreEqual(expectedSaved, store.Load(settingsRoot).Configuration,
            "Deleting one saved item must not apply another item's draft parameters or new imports.");
        Assert.AreEqual(expectedSaved, session.SavedConfiguration);
        Assert.AreEqual(expectedDraft, session.DraftConfiguration);
        Assert.AreEqual(writesBefore + 1, writer.WriteCount);
        Assert.AreEqual(0, assetWrites, "A deletion transaction must not write pending image assets.");
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetAssetPath(settingsRoot, pending.AssetId)));
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.AreEqual(pendingBytes, session.PendingPngBytes);
        Assert.IsNotNull(session.TryGetImage(pendingCopy.AssetId));
        Assert.IsTrue(session.IsDirty);
        Assert.IsTrue(session.StatusMessage.Contains("删除", StringComparison.Ordinal));

        var reopened = new AccessorySettingsSession(settingsRoot);
        Assert.AreEqual(expectedSaved, reopened.SavedConfiguration);
        session.CancelPreview();
        Assert.AreEqual(expectedSaved, session.DraftConfiguration);
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.IsFalse(session.DraftConfiguration.Items.Any(item => item.Id == "remove"),
            "Canceling unrelated draft changes must not resurrect a committed deletion.");
    }

    [TestMethod]
    public void DraftOnlyDeletionDoesNotWriteAndPrunesPendingImageOnlyAfterItsLastReference()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var writer = new ControlledWriter();
        var session = new AccessorySettingsSession(settingsRoot, store: new AccessorySettingsStore(writer));
        var saved = session.SavedConfiguration;
        var imported = session.ImportPng(WriteSource("shared.png"));
        var copy = session.Duplicate(imported.Id);

        Assert.IsTrue(session.TryDeleteItem(imported.Id));
        Assert.AreEqual(saved, session.SavedConfiguration);
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.IsNotNull(session.TryGetImage(copy.AssetId));
        Assert.IsFalse(session.DraftConfiguration.Items.Any(item => item.Id == imported.Id));
        Assert.IsTrue(session.DraftConfiguration.Items.Any(item => item.Id == copy.Id));
        Assert.AreEqual(0, writer.WriteCount);
        Assert.IsFalse(Directory.Exists(settingsRoot));

        Assert.IsTrue(session.TryDeleteItem(copy.Id));
        Assert.AreEqual(saved, session.SavedConfiguration);
        Assert.AreEqual(saved, session.DraftConfiguration);
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.AreEqual(0L, session.PendingPixelCount);
        Assert.AreEqual(0L, session.PendingPngBytes);
        Assert.AreEqual(0, writer.WriteCount);
        Assert.IsTrue(session.IsCompatibilityMode);
        Assert.IsFalse(Directory.Exists(settingsRoot));
    }

    [TestMethod]
    public void DeletingAllLegacyItemsWritesAValidEmptyManifestAndCannotRestoreLegacyHat()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot, "top-hat");
        var originalIds = session.SavedConfiguration.Items.Select(item => item.Id).ToArray();
        Assert.IsFalse(Directory.Exists(settingsRoot));

        foreach (var id in originalIds)
        {
            Assert.IsTrue(session.TryDeleteItem(id));
            Assert.IsFalse(session.IsCompatibilityMode);
            var loaded = new AccessorySettingsStore().Load(settingsRoot);
            Assert.AreEqual(AccessorySettingsLoadState.Valid, loaded.State);
            Assert.AreEqual(session.SavedConfiguration, loaded.Configuration);
            Assert.IsFalse(loaded.Configuration.Items.Any(item => item.Id == id));
        }

        Assert.AreEqual(AccessoryConfiguration.Empty, session.SavedConfiguration);
        Assert.AreEqual(AccessoryConfiguration.Empty, session.DraftConfiguration);
        var reopened = new AccessorySettingsSession(settingsRoot, "bowler");
        Assert.IsFalse(reopened.IsCompatibilityMode);
        Assert.AreEqual(0, reopened.SavedConfiguration.Items.Count);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public void SavedDeletionKeepsStoredOriginalAndSharedImageBytesEvenAfterLastEntry()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        var source = WriteSource("original.png");
        var originalBytes = File.ReadAllBytes(source);
        var imported = session.ImportPng(source);
        var copy = session.Duplicate(imported.Id);
        session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Where(item =>
            item.Id == imported.Id || item.Id == copy.Id)));
        Assert.IsTrue(session.TrySave());
        var assetPath = AccessorySettingsStore.GetAssetPath(settingsRoot, imported.AssetId);
        var storedBytes = File.ReadAllBytes(assetPath);

        Assert.IsTrue(session.TryDeleteItem(imported.Id));
        CollectionAssert.AreEqual(storedBytes, File.ReadAllBytes(assetPath));
        var reopened = new AccessorySettingsSession(settingsRoot);
        Assert.AreEqual(copy, reopened.SavedConfiguration.Items.Single());
        Assert.IsNotNull(reopened.TryGetImage(copy.AssetId));
        Assert.IsTrue(session.TryDeleteItem(copy.Id));
        CollectionAssert.AreEqual(storedBytes, File.ReadAllBytes(assetPath),
            "This version retains immutable stored images; deletion is not orphan cleanup.");
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(source));
        Assert.AreEqual(AccessoryConfiguration.Empty, new AccessorySettingsSession(settingsRoot).SavedConfiguration);
    }

    [TestMethod]
    public void ProtectedManifestRejectsDeletionWithoutChangingDraftPendingOrDisk()
    {
        var (session, settingsRoot, _) = CreateSavedSessionWithPending();
        var path = AccessorySettingsStore.GetManifestPath(settingsRoot);
        File.WriteAllText(path, "{\"accessorySchemaVersion\":999,\"items\":[]}");
        var original = File.ReadAllBytes(path);
        var snapshot = Snapshot(session);

        Assert.IsFalse(session.TryDeleteItem("remove"));

        AssertUnchanged(snapshot, session);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsTrue(session.IsWriteProtected);
        Assert.IsTrue(session.StatusMessage.Contains("保护", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TransientWindowsFileLockRejectsDeletionAndUnlockPermitsRetry()
    {
        var (session, settingsRoot, _) = CreateSavedSessionWithPending();
        var path = AccessorySettingsStore.GetManifestPath(settingsRoot);
        var original = File.ReadAllBytes(path);
        var snapshot = Snapshot(session);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.IsFalse(session.TryDeleteItem("remove"));
            AssertUnchanged(snapshot, session);
            Assert.IsFalse(session.IsWriteProtected);
            Assert.IsTrue(session.StatusMessage.Contains("暂时无法读取", StringComparison.Ordinal));
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsTrue(session.TryDeleteItem("remove"));
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.AreEqual(Without(snapshot.Saved, "remove"), new AccessorySettingsStore().Load(settingsRoot).Configuration);
    }

    [TestMethod]
    public void AtomicWriteFailureRejectsDeletionWithoutChangingMemoryAndCanRetry()
    {
        var (session, settingsRoot, writer) = CreateSavedSessionWithPending();
        var path = AccessorySettingsStore.GetManifestPath(settingsRoot);
        var original = File.ReadAllBytes(path);
        var snapshot = Snapshot(session);
        writer.FailBeforeCommit = true;

        Assert.IsFalse(session.TryDeleteItem("remove"));

        AssertUnchanged(snapshot, session);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsFalse(session.IsWriteProtected);
        Assert.IsTrue(session.StatusMessage.Contains("未删除", StringComparison.Ordinal));
        Assert.IsTrue(session.StatusMessage.Contains("IOException", StringComparison.Ordinal));
        writer.FailBeforeCommit = false;
        Assert.IsTrue(session.TryDeleteItem("remove"));
        Assert.AreEqual(Without(snapshot.Saved, "remove"), session.SavedConfiguration);
        Assert.AreEqual(Without(snapshot.Draft, "remove"), session.DraftConfiguration);
    }

    [TestMethod]
    public void ValidExternalManifestChangeRejectsDeletionRatherThanOverwritingIt()
    {
        var (session, settingsRoot, writer) = CreateSavedSessionWithPending();
        var snapshot = Snapshot(session);
        var external = new AccessoryConfiguration(snapshot.Saved.Items.Select(item =>
            item.Id == "keep" ? item with { Name = "外部已保存名称", SizePercent = 137 } : item));
        new AccessorySettingsStore().Save(settingsRoot, external);
        var path = AccessorySettingsStore.GetManifestPath(settingsRoot);
        var original = File.ReadAllBytes(path);
        var writesBefore = writer.WriteCount;

        Assert.IsFalse(session.TryDeleteItem("remove"));

        AssertUnchanged(snapshot, session);
        Assert.AreEqual(writesBefore, writer.WriteCount);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.AreEqual(external, new AccessorySettingsStore().Load(settingsRoot).Configuration);
        Assert.IsTrue(session.StatusMessage.Contains("已在外部更改", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ManifestAppearingDuringLegacyPreviewRejectsDeletionAndPreservesExternalFile()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        var pending = session.ImportPng(WriteSource("pending.png"));
        var snapshot = Snapshot(session);
        new AccessorySettingsStore().Save(settingsRoot, AccessoryConfiguration.Empty);
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot));

        Assert.IsFalse(session.TryDeleteItem(snapshot.Saved.Items[0].Id));

        AssertUnchanged(snapshot, session);
        Assert.IsTrue(session.IsCompatibilityMode);
        Assert.IsNotNull(session.TryGetImage(pending.AssetId));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot)));
    }

    [TestMethod]
    public void ProtectedManifestAlsoRejectsDraftOnlyDeletionWithoutDiscardingItsPendingImage()
    {
        var (session, settingsRoot, writer) = CreateSavedSessionWithPending();
        var pending = session.DraftConfiguration.Items.Single(item => !session.SavedConfiguration.Items.Any(saved => saved.Id == item.Id));
        var path = AccessorySettingsStore.GetManifestPath(settingsRoot);
        File.WriteAllText(path, "invalid fictional manifest");
        var original = File.ReadAllBytes(path);
        var snapshot = Snapshot(session);
        var writesBefore = writer.WriteCount;

        Assert.IsFalse(session.TryDeleteItem(pending.Id));

        AssertUnchanged(snapshot, session);
        Assert.AreEqual(writesBefore, writer.WriteCount);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsNotNull(session.TryGetImage(pending.AssetId));
        Assert.IsTrue(session.StatusMessage.Contains("保护", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ManifestMovedOutsideSessionRejectsDeletionInsteadOfRecreatingIt()
    {
        var (session, settingsRoot, writer) = CreateSavedSessionWithPending();
        var path = AccessorySettingsStore.GetManifestPath(settingsRoot);
        var movedPath = Path.Combine(root, "externally-moved-manifest.json");
        File.Move(path, movedPath);
        var original = File.ReadAllBytes(movedPath);
        var snapshot = Snapshot(session);
        var writesBefore = writer.WriteCount;

        Assert.IsFalse(session.TryDeleteItem("remove"));

        AssertUnchanged(snapshot, session);
        Assert.AreEqual(writesBefore, writer.WriteCount);
        Assert.IsFalse(File.Exists(path));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(movedPath));
        Assert.IsTrue(session.StatusMessage.Contains("已在外部更改", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WriterExceptionAfterConfirmedCommitIsSuccessfulDeletionAndCancelDoesNotRestoreIt()
    {
        var (session, settingsRoot, writer) = CreateSavedSessionWithPending();
        var snapshot = Snapshot(session);
        writer.ThrowAfterCommit = true;

        Assert.IsTrue(session.TryDeleteItem("remove"));

        Assert.AreEqual(Without(snapshot.Saved, "remove"), session.SavedConfiguration);
        Assert.AreEqual(Without(snapshot.Draft, "remove"), session.DraftConfiguration);
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsSession(settingsRoot).SavedConfiguration);
        Assert.IsTrue(session.StatusMessage.Contains("已删除", StringComparison.Ordinal));
        session.CancelPreview();
        Assert.IsFalse(session.DraftConfiguration.Items.Any(item => item.Id == "remove"));
    }

    [TestMethod]
    public void MissingIdAndCrossThreadDeletionDoNotChangeStateOrWriteDisk()
    {
        var (session, settingsRoot, writer) = CreateSavedSessionWithPending();
        var snapshot = Snapshot(session);
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot));
        var writesBefore = writer.WriteCount;
        Assert.IsFalse(session.TryDeleteItem("nonexistent"));
        Assert.IsTrue(session.StatusMessage.Contains("不存在", StringComparison.Ordinal));
        Exception? error = null;
        var worker = new Thread(() =>
        {
            try { session.TryDeleteItem("remove"); }
            catch (Exception caught) { error = caught; }
        })
        { IsBackground = true };
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.IsInstanceOfType<InvalidOperationException>(error);
        AssertUnchanged(snapshot, session);
        Assert.AreEqual(writesBefore, writer.WriteCount);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot)));
    }

    private (AccessorySettingsSession Session, string SettingsRoot, ControlledWriter Writer) CreateSavedSessionWithPending()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var writer = new ControlledWriter();
        var store = new AccessorySettingsStore(writer);
        store.Save(settingsRoot, new AccessoryConfiguration([BuiltIn("remove"), BuiltIn("keep")]));
        var session = new AccessorySettingsSession(settingsRoot, store: store);
        session.ImportPng(WriteSource("pending.png"));
        session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(item =>
            item.Id == "keep" ? item with { BodyAnchorY = .62, RotationDamping = 34 } : item)));
        return (session, settingsRoot, writer);
    }

    private static AccessoryItem BuiltIn(string id) => new(id, "虚构配饰-" + id, "builtin-sprout", 64, 64);
    private static AccessoryConfiguration Without(AccessoryConfiguration configuration, string id) =>
        new(configuration.Items.Where(item => item.Id != id));
    private static SessionSnapshot Snapshot(AccessorySettingsSession session) => new(session.SavedConfiguration,
        session.DraftConfiguration, session.PendingImageCount, session.PendingPixelCount, session.PendingPngBytes);
    private static void AssertUnchanged(SessionSnapshot before, AccessorySettingsSession session)
    {
        Assert.AreEqual(before.Saved, session.SavedConfiguration);
        Assert.AreEqual(before.Draft, session.DraftConfiguration);
        Assert.AreEqual(before.PendingImages, session.PendingImageCount);
        Assert.AreEqual(before.PendingPixels, session.PendingPixelCount);
        Assert.AreEqual(before.PendingBytes, session.PendingPngBytes);
    }

    private string WriteSource(string name)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        var pixels = new byte[] { 0, 0, 0, 0, 22, 90, 150, 255, 99, 77, 30, 128,
            40, 60, 70, 255, 0, 0, 0, 0, 1, 2, 3, 64 };
        var bitmap = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private sealed record SessionSnapshot(AccessoryConfiguration Saved, AccessoryConfiguration Draft,
        int PendingImages, long PendingPixels, long PendingBytes);

    private sealed class ControlledWriter : IAtomicTextWriter
    {
        public int WriteCount { get; private set; }
        public bool FailBeforeCommit { get; set; }
        public bool ThrowAfterCommit { get; set; }
        public void Write(string path, string content)
        {
            WriteCount++;
            if (FailBeforeCommit) throw new IOException("Injected deletion failure before manifest commit.");
            new AtomicTextFileWriter().Write(path, content);
            if (ThrowAfterCommit) throw new IOException("Injected deletion failure after manifest commit.");
        }
    }
}
