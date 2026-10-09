using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Client.Sync.Accessories;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.AccountSync;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol.Accessories;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class AccessoryAccountControllerTests
{
    private string root = null!;

    [TestInitialize]
    public void CreateRoot() => root = Path.Combine(Path.GetTempPath(), "Dango-accessory-account-controller", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [STATestMethod]
    public void LoginReadsIndependentOutfitRevisionWithoutAutomaticallyWritingEitherSide()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var original = session.SavedConfiguration;
            var panel = new AccessoryAccountPanelView();
            var transport = new ControlledTransport { Current = new(17, RemoteBuiltin()) };
            using var controller = new AccessoryAccountController(panel, session, () => Assert.Fail("Login cannot restore."), () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            Assert.AreEqual(17L, controller.Diagnostics.ServerRevision);
            Assert.AreEqual(1, transport.Reads);
            Assert.AreEqual(0, transport.Commits.Count);
            Assert.AreEqual(original, session.SavedConfiguration);
            Assert.IsTrue(GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
        });
    }

    [STATestMethod]
    public void SuccessfulLocalApplyUploadsRealCommittedPngThenBacksUpAndLogoutKeepsLocalResult()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            var transport = new ControlledTransport { VerifyLocalCommit = () =>
                Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsStore().Load(root).Configuration) };
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            var item = session.ImportPng(WritePng());
            Assert.IsTrue(session.TrySave());
            PumpUntil(() => controller.Diagnostics.LastBackedUpLocalVersion == session.LocalCommitVersion);
            Assert.AreEqual(1, transport.Uploads.Count);
            Assert.AreEqual(item.AssetId, transport.Uploads.Single().AssetId);
            Assert.AreEqual(1, transport.Commits.Count);
            PumpUntil(() => panel.Message.Contains("已私有备份", StringComparison.Ordinal));
            Assert.IsTrue(GetButton(panel, "AccessoryRestoreAccount").IsEnabled, "A confirmed first backup must become restorable without an extra refresh.");
            var saved = session.SavedConfiguration;
            controller.Detach();
            Assert.AreEqual(AccessorySyncState.Detached, controller.Diagnostics.State);
            Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
            session.Stage(AccessoryConfiguration.Empty);
            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(1, transport.Commits.Count);
        });
    }

    [STATestMethod]
    public void ApplyDuringInitialReadIsDeferredUntilBaselineAndNeverUsesAssumedZeroRevision()
    {
        WithDispatcher(() =>
        {
            var heldRead = new TaskCompletionSource<StoredAccessoryOutfit?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ControlledTransport { ReadHandler = _ => heldRead.Task };
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), transport);
            session.Stage(AccessoryConfiguration.Empty);
            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(0, transport.Commits.Count);
            heldRead.SetResult(new(9, RemoteBuiltin()));
            PumpUntil(() => controller.Diagnostics.LastBackedUpLocalVersion == session.LocalCommitVersion);
            Assert.AreEqual(9L, transport.Commits.Single().ExpectedRevision);
            Assert.AreEqual(0, transport.Commits.Single().Outfit.Items.Count);
        });
    }

    [STATestMethod]
    public void ExplicitRestoreChangesSavedOutfitOnlyAfterVerificationAndCannotLoopBackAsBackup()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView(); var refreshed = 0;
            var transport = new ControlledTransport { Current = new(7, RemoteBuiltin()) };
            using var controller = new AccessoryAccountController(panel, session, () => refreshed++, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            Assert.AreEqual(0, refreshed);
            GetButton(panel, "AccessoryRestoreAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.Restored);
            Assert.AreEqual(1, refreshed);
            Assert.AreEqual("remote-hat", session.SavedConfiguration.Items.Single(item => item.IsEquipped).Id);
            Assert.AreEqual(0, transport.Commits.Count);
            Assert.AreEqual(0, transport.Uploads.Count);
            Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
        });
    }

    [STATestMethod]
    public void OldLoginReadIgnoringCancellationCannotAttachOrShowStatusForTheNextAccount()
    {
        WithDispatcher(() =>
        {
            var heldRead = new TaskCompletionSource<StoredAccessoryOutfit?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = new ControlledTransport { ReadHandler = _ => heldRead.Task };
            var next = new ControlledTransport { Current = new(21, RemoteBuiltin()) };
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), old);
            controller.Attach(Scope("fictional-next", 2), next);
            PumpUntil(() => controller.Diagnostics.ServerRevision == 21);
            heldRead.SetResult(new(3, AccessoryOutfitManifest.Empty));
            PumpUntil(() => controller.PendingOperation.IsCompleted);
            Assert.AreEqual(21L, controller.Diagnostics.ServerRevision);
            Assert.AreEqual(0, old.Commits.Count);
            Assert.AreEqual(0, next.Commits.Count);
            Assert.IsTrue(GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
        });
    }

    [STATestMethod]
    public void DirtyDraftDisablesBothExplicitBackupAndRestoreWithoutDiscardingIt()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            var transport = new ControlledTransport { Current = new(1, RemoteBuiltin()) };
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            session.Stage(AccessoryConfiguration.Empty);
            PumpUntil(() => !GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            Assert.IsFalse(GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            Assert.AreEqual(AccessoryConfiguration.Empty, session.DraftConfiguration);
            Assert.IsTrue(session.IsDirty);
            Assert.AreEqual(0, transport.Commits.Count);
        });
    }

    [STATestMethod]
    public void FailedInitialReadRetainsAppliedOutfitAndRefreshUsesActualRevisionBeforeBackup()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            var first = new TaskCompletionSource<StoredAccessoryOutfit?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ControlledTransport { ReadHandler = _ => first.Task };
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => Assert.Fail("503 is not an authentication failure."));
            controller.Attach(Scope(), transport);
            session.Stage(AccessoryConfiguration.Empty); Assert.IsTrue(session.TrySave());
            var saved = session.SavedConfiguration;
            first.SetException(new AccessoryAccountTransportException(AccessoryTransportFailure.Unavailable));
            PumpUntil(() => panel.Message.Contains("读取失败", StringComparison.Ordinal));
            Assert.AreEqual(saved, new AccessorySettingsSession(root).SavedConfiguration);
            Assert.AreEqual(0, transport.Commits.Count);
            Assert.IsTrue(GetButton(panel, "AccessoryRefreshAccount").IsEnabled);
            transport.ReadHandler = null; transport.Current = new(14, RemoteBuiltin());
            GetButton(panel, "AccessoryRefreshAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.LastBackedUpLocalVersion == session.LocalCommitVersion);
            Assert.AreEqual(14L, transport.Commits.Single().ExpectedRevision);
            Assert.AreEqual(saved, session.SavedConfiguration);
        });
    }

    [STATestMethod]
    public void KnownUploadFailureKeepsLocalSaveAndOnlyExplicitRetryResubmits()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            var transport = new ControlledTransport { UploadFailure = AccessoryTransportFailure.Unavailable };
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => Assert.Fail("Unavailable storage is not logout."));
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            session.ImportPng(WritePng()); Assert.IsTrue(session.TrySave());
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.Failed);
            PumpUntil(() => GetButton(panel, "AccessoryRetryBackup").IsEnabled);
            Assert.IsTrue(panel.Visibility == Visibility.Visible);
            Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
            Assert.AreEqual(0, transport.Commits.Count);
            transport.UploadFailure = null;
            GetButton(panel, "AccessoryRetryBackup").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.LastBackedUpLocalVersion == session.LocalCommitVersion);
            Assert.AreEqual(1, transport.Commits.Count);
        });
    }

    [STATestMethod]
    public void DefinitiveFailureCanBeExplicitlyRefreshedThenRestoredWithoutAutomaticallyRetrying()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView(); var restored = 0;
            var transport = new ControlledTransport { Current = new(5, RemoteBuiltin()), CommitFailure = AccessoryTransportFailure.Rejected };
            using var controller = new AccessoryAccountController(panel, session, () => restored++, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            session.Stage(AccessoryConfiguration.Empty); Assert.IsTrue(session.TrySave());
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.Failed);
            PumpUntil(() => GetButton(panel, "AccessoryRefreshAccount").IsEnabled);
            var saved = session.SavedConfiguration;
            GetButton(panel, "AccessoryRefreshAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            Assert.AreEqual(saved, session.SavedConfiguration, "Refresh only reads; the saved local source must remain available.");
            Assert.AreEqual(1, transport.Commits.Count, "Explicit refresh must not retry the rejected local queue.");
            GetButton(panel, "AccessoryRestoreAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => restored == 1);
            Assert.AreEqual("remote-hat", session.SavedConfiguration.Items.Single(item => item.IsEquipped).Id);
            Assert.AreEqual(1, transport.Commits.Count);
        });
    }

    [STATestMethod]
    public void DeletingAnAppliedAccessoryBacksUpTheActualSavedRemovalWithoutItsImage()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var imported = session.ImportPng(WritePng()); Assert.IsTrue(session.TrySave());
            var panel = new AccessoryAccountPanelView(); var transport = new ControlledTransport();
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            Assert.IsTrue(session.TryDeleteItem(imported.Id));
            PumpUntil(() => controller.Diagnostics.LastBackedUpLocalVersion == session.LocalCommitVersion);
            Assert.AreEqual(0, transport.Uploads.Count, "A removed equipped image is not part of the backup.");
            Assert.IsFalse(transport.Commits.Single().Outfit.Items.Any(item => item.Id == imported.Id));
            Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsSession(root).SavedConfiguration);
        });
    }

    [STATestMethod]
    public void CommittedImageDisappearingBeforeCaptureReportsBackupFailureAndPreservesTheLocalManifest()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var imported = session.ImportPng(WritePng()); Assert.IsTrue(session.TrySave());
            var path = AccessorySettingsStore.GetAssetPath(root, imported.AssetId);
            session.Committed += (_, args) => { if (args.Origin != AccessoryCommitOrigin.AccountRestore) File.Delete(path); };
            var panel = new AccessoryAccountPanelView(); var transport = new ControlledTransport();
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            session.Stage(new AccessoryConfiguration(session.SavedConfiguration.Items.Select(item => item.Id == imported.Id ? item with { Name = "虚构修改" } : item)));
            Assert.IsTrue(session.TrySave());
            var saved = session.SavedConfiguration;
            PumpUntil(() => panel.Message.Contains("所需图片暂时无法读取", StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual("虚构修改", saved.Items.Single(item => item.Id == imported.Id).Name);
            Assert.AreEqual(0, transport.Uploads.Count);
            Assert.AreEqual(0, transport.Commits.Count);
            Assert.IsTrue(File.Exists(AccessorySettingsStore.GetManifestPath(root)));
            File.Copy(WritePng(), path);
            transport.Current = new(4, RemoteBuiltin());
            GetButton(panel, "AccessoryRefreshAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            GetButton(panel, "AccessoryRestoreAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.Restored);
            StringAssert.Contains(panel.Message, "账号穿搭已完整恢复");
            Assert.AreEqual(0, transport.Commits.Count, "Recovery cannot create a restore-to-backup loop.");
        });
    }

    [STATestMethod]
    public void UnknownCommitCannotBeRefreshedOrRetriedAndUsesReceiptReconciliation()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            var transport = new ControlledTransport { CommitFailure = AccessoryTransportFailure.Unavailable };
            using var controller = new AccessoryAccountController(panel, session, () => Assert.Fail("Reconcile cannot restore."), () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            session.Stage(AccessoryConfiguration.Empty); Assert.IsTrue(session.TrySave());
            PumpUntil(() => GetButton(panel, "AccessoryReconcileCommit").IsEnabled);
            Assert.IsFalse(GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            Assert.IsFalse(GetButton(panel, "AccessoryRefreshAccount").IsEnabled);
            Assert.IsFalse(GetButton(panel, "AccessoryRetryBackup").IsEnabled);
            var reads = transport.Reads;
            GetButton(panel, "AccessoryBackupLocal").RaiseEvent(new(Button.ClickEvent));
            GetButton(panel, "AccessoryRefreshAccount").RaiseEvent(new(Button.ClickEvent));
            Assert.AreEqual(reads, transport.Reads);
            Assert.AreEqual(1, transport.Commits.Count);
            transport.Resolution = new(AccessoryCommitResolutionKind.Committed, new(1, transport.Commits.Single().Outfit));
            GetButton(panel, "AccessoryReconcileCommit").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.BackedUp);
            Assert.AreEqual(1, transport.Commits.Count, "Receipt confirmation cannot duplicate the write.");
        });
    }

    [STATestMethod]
    public void RevisionConflictRefreshRequiresANewExplicitSourceChoiceAndNeverAutoOverwrites()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView();
            var transport = new ControlledTransport { Current = new(4, RemoteBuiltin()), CommitFailure = AccessoryTransportFailure.RevisionConflict };
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => { });
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            session.Stage(AccessoryConfiguration.Empty); Assert.IsTrue(session.TrySave());
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.RevisionConflict);
            PumpUntil(() => GetButton(panel, "AccessoryRefreshAccount").IsEnabled);
            var saved = session.SavedConfiguration;
            Assert.IsFalse(GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            Assert.IsFalse(GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            transport.CommitFailure = null; transport.Current = new(8, RemoteBuiltin());
            GetButton(panel, "AccessoryRefreshAccount").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.ServerRevision == 8);
            Assert.AreEqual(1, transport.Commits.Count);
            Assert.AreEqual(saved, session.SavedConfiguration);
            GetButton(panel, "AccessoryBackupLocal").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.Diagnostics.State == AccessorySyncState.BackedUp);
            Assert.AreEqual(8L, transport.Commits.Last().ExpectedRevision);
        });
    }

    [STATestMethod]
    public void OldRestoreIgnoringCancellationCannotCommitAfterAccountSwitch()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var original = session.SavedConfiguration; var panel = new AccessoryAccountPanelView(); var restored = 0;
            var old = new ControlledTransport { Current = new(3, RemoteBuiltin()) };
            var next = new ControlledTransport { Current = new(18, AccessoryOutfitManifest.Empty) };
            using var controller = new AccessoryAccountController(panel, session, () => restored++, () => { });
            controller.Attach(Scope(), old);
            PumpUntil(() => GetButton(panel, "AccessoryRestoreAccount").IsEnabled);
            var held = new TaskCompletionSource<StoredAccessoryOutfit?>(TaskCreationOptions.RunContinuationsAsynchronously);
            old.ReadHandler = _ => held.Task;
            GetButton(panel, "AccessoryRestoreAccount").RaiseEvent(new(Button.ClickEvent));
            var oldOperation = controller.PendingOperation;
            controller.Attach(Scope("fictional-next", 2), next);
            PumpUntil(() => controller.Diagnostics.ServerRevision == 18);
            held.SetResult(old.Current);
            PumpUntil(() => oldOperation.IsCompleted);
            Assert.AreEqual(original, session.SavedConfiguration);
            Assert.AreEqual(original, new AccessorySettingsSession(root).SavedConfiguration);
            Assert.AreEqual(0, restored);
            Assert.AreEqual(0, next.Commits.Count);
        });
    }

    [STATestMethod]
    public void AuthenticationFailureFencesAttachmentWhileLocalApplicationRemainsAvailable()
    {
        WithDispatcher(() =>
        {
            var session = Saved(); var panel = new AccessoryAccountPanelView(); var invalidated = 0;
            var transport = new ControlledTransport { CommitFailure = AccessoryTransportFailure.Authentication };
            using var controller = new AccessoryAccountController(panel, session, () => { }, () => invalidated++);
            controller.Attach(Scope(), transport);
            PumpUntil(() => GetButton(panel, "AccessoryBackupLocal").IsEnabled);
            session.Stage(AccessoryConfiguration.Empty); Assert.IsTrue(session.TrySave());
            PumpUntil(() => invalidated == 1);
            Assert.AreEqual(AccessorySyncState.Detached, controller.Diagnostics.State);
            Assert.AreEqual(Visibility.Collapsed, panel.Visibility);
            Assert.AreEqual(AccessoryConfiguration.Empty, new AccessorySettingsSession(root).SavedConfiguration);
            Assert.IsTrue(session.TrySave());
            Assert.AreEqual(1, transport.Commits.Count);
        });
    }

    private AccessorySettingsSession Saved()
    {
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        return session;
    }

    private string WritePng()
    {
        var path = Path.Combine(root, "fictional-accessory.png");
        var pixels = new byte[] { 0, 0, 0, 0, 33, 90, 150, 255, 99, 77, 30, 128,
            40, 60, 70, 255, 0, 0, 0, 0, 1, 2, 3, 64 };
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12)));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return path;
    }

    private static AccessoryAccountScope Scope(string id = "fictional-account", long generation = 1) => new(id, "fictional-pet", generation);
    private static AccessoryOutfitManifest RemoteBuiltin() => new(1, [new("remote-hat", "虚构远端帽子", "builtin-top-hat", 64, 64)]);
    private static Button GetButton(AccessoryAccountPanelView panel, string name) => (Button)panel.FindName(name);

    private static void WithDispatcher(Action action)
    {
        var old = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(old); }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var timeout = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow > timeout) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Bounded accessory account transition did not complete.");
    }

    private sealed class ControlledTransport : IAccessoryAccountTransport
    {
        private int reads;
        internal int Reads => Volatile.Read(ref reads);
        internal StoredAccessoryOutfit? Current;
        internal Func<CancellationToken, Task<StoredAccessoryOutfit?>>? ReadHandler;
        internal Action? VerifyLocalCommit;
        internal AccessoryTransportFailure? UploadFailure, CommitFailure;
        internal AccessoryCommitResolution Resolution = new(AccessoryCommitResolutionKind.Unknown);
        internal ConcurrentQueue<AccessoryImageSnapshot> Uploads { get; } = new();
        internal ConcurrentQueue<CommitAccessoryOutfitRequest> Commits { get; } = new();
        public Uri ServiceOrigin { get; } = new("https://accessory.dango.invalid/");
        public Task<UploadedAccessoryAsset> UploadAssetAsync(AccessoryAccountScope scope, AccessoryImageSnapshot image, CancellationToken ct)
        {
            VerifyLocalCommit?.Invoke(); Uploads.Enqueue(image);
            if (UploadFailure is { } failure) throw new AccessoryAccountTransportException(failure);
            return Task.FromResult(new UploadedAccessoryAsset(image.AssetId, image.AssetId, image.PixelWidth, image.PixelHeight, 1));
        }
        public Task<StoredAccessoryOutfit> CommitOutfitAsync(AccessoryAccountScope scope, CommitAccessoryOutfitRequest request, CancellationToken ct)
        {
            VerifyLocalCommit?.Invoke(); Commits.Enqueue(request);
            if (CommitFailure is { } failure) throw new AccessoryAccountTransportException(failure);
            Current = new(request.ExpectedRevision + 1, request.Outfit);
            return Task.FromResult(Current);
        }
        public Task<StoredAccessoryOutfit?> GetCurrentOutfitAsync(AccessoryAccountScope scope, CancellationToken ct)
        {
            Interlocked.Increment(ref reads);
            return ReadHandler?.Invoke(ct) ?? Task.FromResult(Current);
        }
        public Task<AccessoryImageSnapshot> DownloadAssetAsync(AccessoryAccountScope scope, string assetId, CancellationToken ct) =>
            Task.FromResult(Uploads.Single(image => image.AssetId == assetId));
        public Task<AccessoryCommitResolution> ResolveCommitAsync(AccessoryAccountScope scope, string key, CancellationToken ct) =>
            Task.FromResult(Resolution);
    }
}
