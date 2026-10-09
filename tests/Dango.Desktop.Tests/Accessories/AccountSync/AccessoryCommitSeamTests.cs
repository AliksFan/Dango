using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.AccountSync;
using Dango.Desktop.Verification;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dango.Desktop.Tests.Accessories.AccountSync;

[TestClass]
public sealed class AccessoryCommitSeamTests
{
    private string root = null!;

    [TestInitialize]
    public void CreateRoot() => root = Path.Combine(Path.GetTempPath(), "Dango-account-accessory-commit", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void OnlyRealSuccessfulCommitsNotifyAfterTheManifestAndSessionAgree()
    {
        var session = new AccessorySettingsSession(root);
        var events = new List<AccessorySettingsCommittedEventArgs>();
        session.Committed += (_, args) =>
        {
            Assert.AreEqual(args.Configuration, new AccessorySettingsStore().Load(root).Configuration);
            Assert.AreEqual(args.Configuration, session.SavedConfiguration);
            events.Add(args);
        };
        session.Stage(AccessoryConfiguration.Empty);
        Assert.AreEqual(0, events.Count);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(AccessoryCommitOrigin.Apply, events[0].Origin);
        Assert.AreEqual(1L, session.LocalCommitVersion);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(1, events.Count, "No-op Apply must not queue another backup.");
    }

    [TestMethod]
    public void FailedSaveDoesNotNotifyOrAdvanceVersion()
    {
        var session = new AccessorySettingsSession(root, store: new AccessorySettingsStore(new FailingWriter()));
        var calls = 0;
        session.Committed += (_, _) => calls++;
        session.Stage(AccessoryConfiguration.Empty);
        Assert.IsFalse(session.TrySave());
        Assert.AreEqual(0, calls);
        Assert.AreEqual(0L, session.LocalCommitVersion);
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public void SavedDeletionNotifiesCommittedConfigurationWithoutOtherDraftEdits()
    {
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration;
        var selected = saved.Items.First();
        session.Stage(new(saved.Items.Select(item => item with { Name = "未保存的其他修改" })));
        AccessorySettingsCommittedEventArgs? notification = null;
        session.Committed += (_, args) => notification = args;
        Assert.IsTrue(session.TryDeleteItem(selected.Id));
        Assert.IsNotNull(notification);
        Assert.AreEqual(AccessoryCommitOrigin.Delete, notification.Origin);
        Assert.IsFalse(notification.Configuration.Items.Any(item => item.Id == selected.Id));
        Assert.IsTrue(notification.Configuration.Items.All(item => item.Name != "未保存的其他修改"));
        Assert.IsTrue(session.IsDirty);
        Assert.AreEqual(2L, session.LocalCommitVersion);
    }

    [TestMethod]
    public void NotificationFailureCannotUndoConfirmedLocalSaveOrHideItFromOtherSubscribers()
    {
        var session = new AccessorySettingsSession(root);
        var laterCalled = false;
        session.Committed += (_, _) => throw new IOException("Injected backup capture failure.");
        session.Committed += (_, _) => laterCalled = true;
        session.Stage(AccessoryConfiguration.Empty);
        Assert.IsTrue(session.TrySave());
        Assert.IsTrue(laterCalled);
        Assert.IsFalse(session.IsDirty);
        Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsStore().Load(root).Configuration);
        Assert.IsTrue(session.StatusMessage.Contains("通知", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RestoreCommitsWithoutDraftAndDirtyOrStaleGuardNeverOverwritesLocalState()
    {
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var expected = session.SavedConfiguration;
        var version = session.LocalCommitVersion;
        var assets = new Dictionary<string, AccessoryImageAsset>();
        session.Stage(AccessoryConfiguration.Empty);
        Assert.IsFalse(session.TryRestoreSavedConfiguration(expected, version, AccessoryConfiguration.Empty, assets));
        Assert.AreEqual(expected, new AccessorySettingsStore().Load(root).Configuration);
        session.CancelPreview();
        AccessorySettingsCommittedEventArgs? notification = null;
        session.Committed += (_, args) => notification = args;
        Assert.IsTrue(session.TryRestoreSavedConfiguration(expected, version, AccessoryConfiguration.Empty, assets));
        Assert.AreEqual(AccessoryConfiguration.Empty, session.SavedConfiguration);
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.AreEqual(AccessoryCommitOrigin.AccountRestore, notification!.Origin);
        Assert.IsFalse(session.TryRestoreSavedConfiguration(expected, version, expected, assets));
        Assert.AreEqual(AccessoryConfiguration.Empty, new AccessorySettingsStore().Load(root).Configuration);
    }

    [TestMethod]
    public void RestoreImageOrManifestFailureLeavesSessionAndSavedManifestUnchangedAndCanRetry()
    {
        var normal = new AccessorySettingsStore();
        var old = AccessoryConfiguration.FromLegacyHat("sprout");
        normal.Save(root, old);
        var writer = new ToggleWriter { Fail = true };
        var session = new AccessorySettingsSession(root, store: new AccessorySettingsStore(writer));
        var asset = WriteAsset();
        var restored = new AccessoryConfiguration([new AccessoryItem("fictional-restored", "虚构恢复", asset.AssetId, 3, 2)]);
        var version = session.LocalCommitVersion;
        var before = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        Assert.IsFalse(session.TryRestoreSavedConfiguration(old, version, restored, new Dictionary<string, AccessoryImageAsset>()));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        var assets = new Dictionary<string, AccessoryImageAsset> { [asset.AssetId] = asset };
        Assert.IsFalse(session.TryRestoreSavedConfiguration(old, version, restored, assets));
        Assert.AreEqual(old, session.SavedConfiguration);
        Assert.AreEqual(old, session.DraftConfiguration);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        writer.Fail = false;
        Assert.IsTrue(session.TryRestoreSavedConfiguration(old, version, restored, assets));
        Assert.AreEqual(restored, new AccessorySettingsSession(root).SavedConfiguration);
    }

    [TestMethod]
    public void OwnerThreadAndExternalDiskGuardsRejectBeforeRestoreChangesSessionOrFiles()
    {
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var expected = session.SavedConfiguration;
        var version = session.LocalCommitVersion;
        var before = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var assets = new Dictionary<string, AccessoryImageAsset>();
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try { session.TryRestoreSavedConfiguration(expected, version, AccessoryConfiguration.Empty, assets); }
            catch (Exception error) { workerError = error; }
        });
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<InvalidOperationException>(workerError);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
        new AccessorySettingsStore().Save(root, AccessoryConfiguration.Empty);
        Assert.IsFalse(session.TryRestoreSavedConfiguration(expected, version, expected, assets));
        Assert.AreEqual(expected, session.SavedConfiguration);
        Assert.AreEqual(AccessoryConfiguration.Empty, new AccessorySettingsStore().Load(root).Configuration);
    }

    private AccessoryImageAsset WriteAsset()
    {
        var path = Path.Combine(root, "fictional-source.png");
        var pixels = new byte[] { 0, 0, 0, 0, 19, 90, 150, 255, 99, 77, 30, 128,
            40, 60, 70, 255, 0, 0, 0, 0, 1, 2, 3, 64 };
        var bitmap = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(path)) encoder.Save(output);
        return AccessoryImageAsset.ImportPng(path);
    }

    private sealed class ToggleWriter : IAtomicTextWriter
    {
        public bool Fail { get; set; }
        public void Write(string path, string content)
        {
            if (Fail) throw new IOException("Injected restore manifest failure.");
            new AtomicTextFileWriter().Write(path, content);
        }
    }

    private sealed class FailingWriter : IAtomicTextWriter
    {
        public void Write(string path, string content) => throw new IOException("Injected manifest failure.");
    }
}
