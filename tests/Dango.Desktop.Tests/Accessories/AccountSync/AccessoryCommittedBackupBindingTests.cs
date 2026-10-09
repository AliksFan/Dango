using Dango.Client.Sync.Accessories;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.AccountSync;
using Dango.Protocol.Accessories;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dango.Desktop.Tests.Accessories.AccountSync;

[TestClass]
public sealed class AccessoryCommittedBackupBindingTests
{
    private string root = null!;

    [TestInitialize]
    public void CreateRoot() => root = Path.Combine(Path.GetTempPath(), "Dango-account-accessory-binding", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void AttachedApplyQueuesAfterFileCommitAndNoopOrDisposedBindingCannotQueueAgain()
    {
        var session = new AccessorySettingsSession(root);
        var repo = new AccessoryAccountLocalRepository(session);
        var sync = new AccessoryAccountSync(repo);
        var transport = new RecordingTransport(root);
        sync.AttachAccount(new("fictional-account", "fictional-pet", 1), transport, 0);
        var binding = new AccessoryCommittedBackupBinding(session, repo, sync);
        try
        {
            Assert.IsTrue(session.TrySave());
            Assert.IsTrue(SpinWait.SpinUntil(() => sync.SafeDiagnostics.State == AccessorySyncState.BackedUp, TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1L, binding.LastQueuedLocalCommitVersion);
            Assert.IsNull(binding.LastCaptureErrorCode);
            Assert.AreEqual(1, transport.Commits);
            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(1, transport.Commits, "No-op Apply is not another backup request.");
            binding.Dispose();
            binding.Dispose();
            session.Stage(AccessoryConfiguration.Empty);
            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(1L, sync.SafeDiagnostics.LastQueuedLocalVersion);
            Assert.AreEqual(1, transport.Commits);
            Assert.AreEqual(AccessoryConfiguration.Empty, new AccessorySettingsSession(root).SavedConfiguration);
        }
        finally
        {
            binding.Dispose();
            sync.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [TestMethod]
    public void AccountRestoreDoesNotQueueTheDownloadedOutfitBackToTheAccount()
    {
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var repo = new AccessoryAccountLocalRepository(session);
        var sync = new AccessoryAccountSync(repo);
        var transport = new RecordingTransport(root);
        sync.AttachAccount(new("fictional-account", "fictional-pet", 1), transport, 0);
        using var binding = new AccessoryCommittedBackupBinding(session, repo, sync);
        try
        {
            Assert.IsTrue(session.TryRestoreSavedConfiguration(session.SavedConfiguration, session.LocalCommitVersion,
                AccessoryConfiguration.Empty, new Dictionary<string, AccessoryImageAsset>()));
            Assert.AreEqual(AccessorySyncState.Idle, sync.SafeDiagnostics.State);
            Assert.IsNull(binding.LastQueuedLocalCommitVersion);
            Assert.AreEqual(0, transport.Commits);
        }
        finally { sync.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    [TestMethod]
    public void DetachedBindingKeepsLocalApplyFreeAndRejectsARepositoryForAnotherSession()
    {
        var session = new AccessorySettingsSession(root);
        var repo = new AccessoryAccountLocalRepository(session);
        var sync = new AccessoryAccountSync(repo);
        using var binding = new AccessoryCommittedBackupBinding(session, repo, sync);
        try
        {
            Assert.IsTrue(session.TrySave());
            Assert.IsNull(binding.LastQueuedLocalCommitVersion);
            Assert.IsNull(binding.LastCaptureErrorCode);
            var other = new AccessorySettingsSession(Path.Combine(root, "other"));
            Assert.ThrowsExactly<ArgumentException>(() => new AccessoryCommittedBackupBinding(other, repo, sync));
            Assert.AreEqual(AccessorySyncState.Detached, sync.SafeDiagnostics.State);
        }
        finally { sync.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    [TestMethod]
    public void SnapshotCaptureFailureKeepsSuccessfulLocalSaveAndShowsWarningWithoutQueueingIncompleteOutfit()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "fictional-image.png");
        var pixels = new byte[] { 0, 0, 0, 0, 33, 90, 150, 255, 99, 77, 30, 128,
            40, 60, 70, 255, 0, 0, 0, 0, 1, 2, 3, 64 };
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12)));
        using (var output = File.Create(source)) encoder.Save(output);
        var session = new AccessorySettingsSession(root);
        var item = session.ImportPng(source);
        Assert.IsTrue(session.TrySave());
        File.WriteAllText(AccessorySettingsStore.GetAssetPath(root, item.AssetId), "damaged committed image");
        var repo = new AccessoryAccountLocalRepository(session);
        var sync = new AccessoryAccountSync(repo);
        var transport = new RecordingTransport(root);
        sync.AttachAccount(new("fictional-account", "fictional-pet", 1), transport, 0);
        using var binding = new AccessoryCommittedBackupBinding(session, repo, sync);
        try
        {
            session.Stage(new(session.SavedConfiguration.Items.Select(entry => entry with { Name = "已保存的新名字" })));
            Assert.IsTrue(session.TrySave(), "A backup capture failure cannot turn a confirmed local write into a failure.");
            Assert.IsFalse(session.IsDirty);
            Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsStore().Load(root).Configuration);
            Assert.AreEqual("local_accessory_snapshot_unavailable", binding.LastCaptureErrorCode);
            Assert.IsNull(binding.LastQueuedLocalCommitVersion);
            Assert.AreEqual(AccessorySyncState.Idle, sync.SafeDiagnostics.State);
            Assert.AreEqual(0, transport.Commits);
            Assert.IsTrue(session.StatusMessage.Contains("通知处理失败", StringComparison.Ordinal));
        }
        finally { sync.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class RecordingTransport(string settingsRoot) : IAccessoryAccountTransport
    {
        private int commits;
        public int Commits => Volatile.Read(ref commits);
        public Uri ServiceOrigin { get; } = new("https://accessory.invalid");

        public Task<UploadedAccessoryAsset> UploadAssetAsync(AccessoryAccountScope scope, AccessoryImageSnapshot image,
            CancellationToken cancellationToken) => throw new InvalidOperationException("This built-in fixture requires no image upload.");

        public Task<StoredAccessoryOutfit> CommitOutfitAsync(AccessoryAccountScope scope, CommitAccessoryOutfitRequest request,
            CancellationToken cancellationToken)
        {
            var committed = new AccessorySettingsStore().Load(settingsRoot);
            Assert.AreEqual(AccessorySettingsLoadState.Valid, committed.State);
            Assert.AreEqual(AccessoryOutfitMapper.FromSavedConfiguration(committed.Configuration), request.Outfit);
            Interlocked.Increment(ref commits);
            return Task.FromResult(new StoredAccessoryOutfit(request.ExpectedRevision + 1, request.Outfit));
        }

        public Task<StoredAccessoryOutfit?> GetCurrentOutfitAsync(AccessoryAccountScope scope,
            CancellationToken cancellationToken) => Task.FromResult<StoredAccessoryOutfit?>(null);

        public Task<AccessoryImageSnapshot> DownloadAssetAsync(AccessoryAccountScope scope, string assetId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected image download.");

        public Task<AccessoryCommitResolution> ResolveCommitAsync(AccessoryAccountScope scope, string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult(new AccessoryCommitResolution(AccessoryCommitResolutionKind.Unknown));
    }
}
