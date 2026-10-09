using System.IO;
using System.Windows.Threading;
using Dango.Client.Sync.Accessories;

namespace Dango.Desktop.Accessories.AccountSync;

/// <summary>Owner-thread commits with fully decoded staging; no account identity is stored in local paths.</summary>
public sealed class AccessoryAccountLocalRepository : IAccessoryLocalRepository
{
    private readonly AccessorySettingsSession session;
    private readonly Dispatcher dispatcher;
    private readonly string settingsRoot;
    private readonly AccessorySettingsStore store = new();
    private readonly Func<string, Stream> createStageOutput;
    internal bool IsForSession(AccessorySettingsSession candidate) => ReferenceEquals(session, candidate);

    public AccessoryAccountLocalRepository(AccessorySettingsSession session, Dispatcher? ownerDispatcher = null)
        : this(session, ownerDispatcher, path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    { }

    internal AccessoryAccountLocalRepository(AccessorySettingsSession session, Dispatcher? ownerDispatcher,
        Func<string, Stream> createStageOutput)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        settingsRoot = session.AccountSyncSettingsRoot;
        dispatcher = ownerDispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.VerifyAccess();
        this.createStageOutput = createStageOutput ?? throw new ArgumentNullException(nameof(createStageOutput));
    }

    public Task<AccessoryLocalSnapshot> CaptureCommittedAsync(CancellationToken cancellationToken = default) =>
        OnOwnerThreadAsync(() =>
        {
            var loaded = store.Load(settingsRoot);
            if (session.IsCompatibilityMode || loaded.State != AccessorySettingsLoadState.Valid ||
                !loaded.Configuration.Equals(session.SavedConfiguration))
                throw new InvalidDataException("只有确认已保存到本机的穿搭才能备份；请先应用或重新读取。");
            var outfit = AccessoryOutfitMapper.FromSavedConfiguration(session.SavedConfiguration);
            var images = outfit.Items.Where(item => !AccessoryItem.IsBuiltInAsset(item.AssetId))
                .GroupBy(item => item.AssetId, StringComparer.Ordinal).Select(group =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Always reread committed bytes. The presentation cache can hide a later disk corruption.
                    var image = store.LoadImage(settingsRoot, group.Key);
                    var item = group.First();
                    if (image.PixelWidth != item.PixelWidth || image.PixelHeight != item.PixelHeight)
                        throw new InvalidDataException("已保存配饰图片尺寸与清单不一致。");
                    return new AccessoryImageSnapshot(image.AssetId, image.PixelWidth, image.PixelHeight, image.CopyPngBytes());
                }).ToArray();
            var afterRead = store.Load(settingsRoot);
            if (afterRead.State != AccessorySettingsLoadState.Valid || !afterRead.Configuration.Equals(loaded.Configuration))
                throw new InvalidDataException("配饰清单在读取图片时变化；请重新读取后备份。");
            return new AccessoryLocalSnapshot(outfit, images, session.LocalCommitVersion);
        }, cancellationToken);

    public Task<AccessoryRestoreGuard> CaptureRestoreGuardAsync(CancellationToken cancellationToken) =>
        OnOwnerThreadAsync(() => new AccessoryRestoreGuard(session.LocalCommitVersion, session.IsDirty), cancellationToken);

    public Task<IAccessoryRestoreStage> StageRestoreAsync(AccessoryLocalSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(Path.GetTempPath(), "Dango-accessory-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var stage = new RestoreStage(this, snapshot, directory);
        try
        {
            foreach (var image in snapshot.Images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(directory, image.AssetId + ".png");
                using (var output = createStageOutput(path))
                {
                    stage.Paths.Add(path);
                    output.Write(image.PngBytes.Span);
                }
                var asset = AccessoryImageAsset.ReadStored(path, image.AssetId);
                if (asset.PixelWidth != image.PixelWidth || asset.PixelHeight != image.PixelHeight)
                    throw new InvalidDataException("下载图片尺寸与账号穿搭不一致。");
                stage.Assets.Add(asset.AssetId, asset);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IAccessoryRestoreStage>(stage);
        }
        catch (Exception error)
        {
            try { stage.DisposeAsync().GetAwaiter().GetResult(); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // A cleanup failure must not conceal the validation/write failure that prevented restoration.
                error.Data["AccessoryStageCleanupFailure"] = cleanup.GetType().Name;
            }
            throw;
        }
    }

    public Task<bool> TryCommitRestoreAsync(IAccessoryRestoreStage stage, AccessoryRestoreGuard guard,
        AccessoryRestoreAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(authorization);
        if (stage is not RestoreStage staged || !ReferenceEquals(staged.Owner, this))
            throw new ArgumentException("恢复暂存不属于此本机会话。", nameof(stage));
        return OnOwnerThreadAsync(() => authorization.TryCommit(() =>
        {
            lock (staged.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (staged.IsDisposed || staged.IsCommitted || guard.HasDirtyDraft || session.IsDirty ||
                    guard.LocalCommitVersion != session.LocalCommitVersion) return false;
                AccessoryConfiguration restored;
                try { restored = AccessoryOutfitMapper.MergeRestoredOutfit(session.SavedConfiguration, staged.Snapshot.Outfit); }
                catch (InvalidDataException) { return false; }
                if (!session.TryRestoreSavedConfiguration(session.SavedConfiguration, guard.LocalCommitVersion,
                    restored, staged.Assets)) return false;
                staged.IsCommitted = true;
                return true;
            }
        }), cancellationToken);
    }

    private Task<T> OnOwnerThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dispatcher.CheckAccess()) return Task.FromResult(action());
        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
    }

    private sealed class RestoreStage(AccessoryAccountLocalRepository owner, AccessoryLocalSnapshot snapshot,
        string directory) : IAccessoryRestoreStage
    {
        public object Gate { get; } = new();
        public AccessoryAccountLocalRepository Owner { get; } = owner;
        public AccessoryLocalSnapshot Snapshot { get; } = snapshot;
        public Dictionary<string, AccessoryImageAsset> Assets { get; } = new(StringComparer.Ordinal);
        public List<string> Paths { get; } = [];
        public bool IsDisposed { get; private set; }
        public bool IsCommitted { get; set; }
        private bool cleanupComplete;

        public ValueTask DisposeAsync()
        {
            lock (Gate)
            {
                if (cleanupComplete) return ValueTask.CompletedTask;
                IsDisposed = true;
                Assets.Clear();
                // Remove only files created by this stage. Unexpected files never justify recursive deletion.
                foreach (var path in Paths) File.Delete(path);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
                cleanupComplete = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
