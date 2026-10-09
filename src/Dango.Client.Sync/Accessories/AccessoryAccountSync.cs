using Dango.Protocol.Accessories;

namespace Dango.Client.Sync.Accessories;

public enum AccessorySyncState
{
    Detached, Idle, Queued, Uploading, Committing, BackedUp, Failed, RevisionConflict,
    OutcomeUnknown, Reconciling, Restoring, Restored, RestoreBlocked
}

public enum AccessoryRestoreResult { Restored, NoOutfit, Blocked, Failed, Cancelled, Superseded }

/// <summary>No account identifiers, credentials, remote exception text or image data.</summary>
public sealed record AccessorySyncDiagnostics(AccessorySyncState State, long AttachmentVersion,
    long? ServerRevision, long? LastQueuedLocalVersion, long? LastBackedUpLocalVersion,
    bool HasPendingLocalChanges, string? ErrorCode);

/// <summary>
/// Receives already committed local snapshots. One serial operation loop per attachment owns backup,
/// receipt reconciliation and restore. Switching accounts invalidates old callbacks and local write leases.
/// This coordinator does not choose between conflicting local/cloud data or implement authentication.
/// </summary>
public sealed class AccessoryAccountSync : IAsyncDisposable
{
    private readonly IAccessoryLocalRepository _repository;
    private readonly TimeSpan _requestTimeout;
    private readonly object _gate = new();
    private readonly object _accountChange = new();
    private readonly HashSet<Task> _workers = [];
    private Session? _session;
    private long _attachmentVersion;
    private bool _disposed;

    public AccessoryAccountSync(IAccessoryLocalRepository repository, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public AccessorySyncDiagnostics SafeDiagnostics
    {
        get
        {
            lock (_gate)
            {
                if (_session is not { } session)
                    return new(AccessorySyncState.Detached, _attachmentVersion, null, null, null, false, null);
                return new(session.State, session.AttachmentVersion, session.ServerRevision,
                    NullableVersion(session.LastQueuedVersion), NullableVersion(session.LastBackedUpVersion),
                    HasPendingLocalChanges(session), session.ErrorCode);
            }
        }
    }

    /// <summary>Known revision must come from a verified account read or an explicit conflict choice.</summary>
    public void AttachAccount(AccessoryAccountScope scope, IAccessoryAccountTransport transport, long knownRevision)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(transport);
        scope.Validate();
        if (knownRevision < 0) throw new ArgumentOutOfRangeException(nameof(knownRevision));
        var origin = ValidateOrigin(transport.ServiceOrigin);
        lock (_accountChange)
        {
            Session? old;
            lock (_gate) { ThrowIfDisposed(); old = _session; }
            // Do not hold _gate while waiting for a local atomic commit: the repository may publish a
            // committed event from inside its write, and that event is allowed to read our diagnostics.
            old?.Fence.Invalidate();
            lock (_gate) _session = new(scope, transport, origin, knownRevision, ++_attachmentVersion);
            Cancel(old);
        }
    }

    public void DetachAccount()
    {
        lock (_accountChange)
        {
            Session? old;
            lock (_gate) { ThrowIfDisposed(); old = _session; }
            old?.Fence.Invalidate();
            lock (_gate) _session = null;
            Cancel(old);
        }
    }

    /// <summary>For synchronous local callers. Async captures must use the attachment-version overload.</summary>
    public bool QueueLocalCommit(AccessoryLocalSnapshot snapshot) => QueueLocalCommitCore(snapshot, null);

    /// <summary>Fence a capture against the attachment that was active when local save/capture began.</summary>
    public bool QueueLocalCommit(AccessoryLocalSnapshot snapshot, long expectedAttachmentVersion) =>
        QueueLocalCommitCore(snapshot, expectedAttachmentVersion);

    private bool QueueLocalCommitCore(AccessoryLocalSnapshot snapshot, long? expectedAttachmentVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_session is not { } session || !IsCurrent(session) ||
                (expectedAttachmentVersion is { } expected && session.AttachmentVersion != expected) ||
                snapshot.LocalCommitVersion <= session.LastQueuedVersion)
                return false;
            session.LatestSnapshot = snapshot;
            session.LastQueuedVersion = snapshot.LocalCommitVersion;
            if (session.State is not (AccessorySyncState.OutcomeUnknown or AccessorySyncState.RevisionConflict or
                AccessorySyncState.Reconciling or AccessorySyncState.Restoring))
            {
                if (!session.WorkerRunning || session.State == AccessorySyncState.Failed)
                {
                    session.FailedSnapshot = null;
                    session.State = AccessorySyncState.Queued;
                }
                StartWorker(session);
            }
            return true;
        }
    }

    /// <summary>Explicit retry for a known failure. Unknown outcomes and revision conflicts stay blocked.</summary>
    public bool RetryLatestBackup()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_session is not { State: AccessorySyncState.Failed } session || !IsCurrent(session) ||
                (session.LatestSnapshot is null && session.FailedSnapshot is null)) return false;
            session.LatestSnapshot ??= session.FailedSnapshot;
            session.FailedSnapshot = null;
            session.State = AccessorySyncState.Queued;
            session.ErrorCode = null;
            StartWorker(session);
            return true;
        }
    }

    public async Task<AccessoryCommitResolutionKind> ReconcileUnknownCommitAsync(CancellationToken cancellationToken = default)
    {
        var session = CurrentSession();
        if (session is null) return AccessoryCommitResolutionKind.Unknown;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
        var entered = false;
        try
        {
            await session.Operation.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            CommitAttempt? attempt;
            lock (_gate)
            {
                if (!IsCurrent(session) || session.UnknownAttempt is null) return AccessoryCommitResolutionKind.Unknown;
                attempt = session.UnknownAttempt;
                session.State = AccessorySyncState.Reconciling;
            }
            var resolution = await InvokeAsync(session, token => session.Transport.ResolveCommitAsync(
                session.Scope, attempt.Request.IdempotencyKey, token), linked.Token).ConfigureAwait(false);
            ThrowIfInactive(session);
            if (resolution is null || !Enum.IsDefined(resolution.Kind)) throw new SafeFailure("invalid_commit_resolution");
            if (resolution.Kind == AccessoryCommitResolutionKind.Committed)
            {
                ValidateReceipt(attempt.Request, resolution.StoredOutfit);
                AcceptCommit(session, attempt, resolution.StoredOutfit!);
            }
            else
            {
                if (resolution.StoredOutfit is not null) throw new SafeFailure("invalid_commit_resolution");
                lock (_gate)
                {
                    if (!IsCurrent(session)) return AccessoryCommitResolutionKind.Unknown;
                    if (resolution.Kind == AccessoryCommitResolutionKind.NotCommitted)
                    {
                        session.UnknownAttempt = null;
                        session.FailedSnapshot = attempt.Source;
                        session.State = AccessorySyncState.Failed;
                        session.ErrorCode = "commit_not_applied";
                    }
                    else
                    {
                        session.State = AccessorySyncState.OutcomeUnknown;
                        session.ErrorCode = "commit_outcome_unknown";
                    }
                }
            }
            return resolution.Kind;
        }
        catch (Exception)
        {
            lock (_gate)
            {
                if (IsCurrent(session) && session.UnknownAttempt is not null)
                {
                    session.State = AccessorySyncState.OutcomeUnknown;
                    session.ErrorCode = linked.IsCancellationRequested ? "commit_reconciliation_cancelled" : "commit_reconciliation_failed";
                }
            }
            return AccessoryCommitResolutionKind.Unknown;
        }
        finally
        {
            if (entered) session.Operation.Release();
            lock (_gate)
                if (IsCurrent(session) && session.State == AccessorySyncState.Queued) StartWorker(session);
        }
    }

    public async Task<AccessoryRestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var session = CurrentSession();
        if (session is null) return AccessoryRestoreResult.Superseded;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
        var entered = false;
        IAccessoryRestoreStage? stage = null;
        try
        {
            await session.Operation.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            lock (_gate)
            {
                if (!IsCurrent(session)) return AccessoryRestoreResult.Superseded;
                if (HasPendingLocalChanges(session))
                {
                    if (!BlocksBackup(session.State)) session.State = AccessorySyncState.RestoreBlocked;
                    session.ErrorCode = "pending_local_backup";
                    return AccessoryRestoreResult.Blocked;
                }
                session.State = AccessorySyncState.Restoring;
                session.ErrorCode = null;
            }
            var guard = await _repository.CaptureRestoreGuardAsync(linked.Token).ConfigureAwait(false);
            ThrowIfInactive(session);
            if (guard is null || guard.LocalCommitVersion < 0) throw new SafeFailure("invalid_restore_guard");
            if (guard.HasDirtyDraft) return BlockRestore(session, "dirty_local_draft");

            var stored = await InvokeAsync(session, token => session.Transport.GetCurrentOutfitAsync(session.Scope, token), linked.Token)
                .ConfigureAwait(false);
            ThrowIfInactive(session);
            if (stored is null)
            {
                SetRestoreState(session, AccessorySyncState.Idle, null);
                return AccessoryRestoreResult.NoOutfit;
            }
            stored.Validate();
            var images = new List<AccessoryImageSnapshot>();
            foreach (var item in stored.Outfit.Items.Where(item => !AccessoryOutfitItem.IsBuiltInAsset(item.AssetId))
                .DistinctBy(item => item.AssetId, StringComparer.Ordinal))
            {
                var image = await InvokeAsync(session, token => session.Transport.DownloadAssetAsync(session.Scope, item.AssetId, token), linked.Token)
                    .ConfigureAwait(false);
                ThrowIfInactive(session);
                if (image is null || image.AssetId != item.AssetId || image.PixelWidth != item.PixelWidth || image.PixelHeight != item.PixelHeight)
                    throw new SafeFailure("invalid_download_response");
                images.Add(image);
            }
            var snapshot = new AccessoryLocalSnapshot(stored.Outfit, images, guard.LocalCommitVersion);
            stage = await _repository.StageRestoreAsync(snapshot, linked.Token).ConfigureAwait(false);
            ThrowIfInactive(session);
            if (stage is null) throw new SafeFailure("invalid_restore_stage");
            lock (_gate)
                if (HasPendingLocalChanges(session)) return BlockRestore(session, "pending_local_backup");
            var authorization = new AccessoryRestoreAuthorization(session.Fence, linked.Token);
            var committed = await _repository.TryCommitRestoreAsync(stage, guard, authorization, linked.Token).ConfigureAwait(false);
            if (!IsCurrentThreadSafe(session)) return AccessoryRestoreResult.Superseded;
            if (!committed)
            {
                if (linked.IsCancellationRequested)
                {
                    SetRestoreState(session, AccessorySyncState.Idle, "restore_cancelled");
                    return AccessoryRestoreResult.Cancelled;
                }
                return BlockRestore(session, "local_restore_guard_changed");
            }
            lock (_gate)
            {
                if (!IsCurrent(session)) return AccessoryRestoreResult.Superseded;
                session.ServerRevision = stored.Revision;
                session.State = AccessorySyncState.Restored;
                session.ErrorCode = null;
            }
            return AccessoryRestoreResult.Restored;
        }
        catch (OperationCanceledException) when (!IsCurrentThreadSafe(session)) { return AccessoryRestoreResult.Superseded; }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            SetRestoreState(session, AccessorySyncState.Idle, "restore_cancelled");
            return AccessoryRestoreResult.Cancelled;
        }
        catch (Exception exception)
        {
            if (!IsCurrentThreadSafe(session)) return AccessoryRestoreResult.Superseded;
            SetRestoreState(session, AccessorySyncState.Failed,
                exception is SafeFailure failure ? failure.Code : "restore_failed");
            return AccessoryRestoreResult.Failed;
        }
        finally
        {
            if (stage is not null)
            {
                try { await stage.DisposeAsync().ConfigureAwait(false); }
                catch (Exception)
                {
                    lock (_gate)
                        if (IsCurrent(session)) session.ErrorCode = "restore_stage_cleanup_failed";
                }
            }
            if (entered) session.Operation.Release();
            lock (_gate)
                if (IsCurrent(session) && session.LatestSnapshot is not null && !BlocksBackup(session.State))
                {
                    session.State = AccessorySyncState.Queued;
                    StartWorker(session);
                }
        }
    }

    private void StartWorker(Session session)
    {
        if (session.WorkerRunning || !IsCurrent(session) || BlocksBackup(session.State) || session.LatestSnapshot is null) return;
        session.WorkerRunning = true;
        var worker = Task.Run(() => WorkAsync(session));
        _workers.Add(worker);
        _ = worker.ContinueWith(completed => { lock (_gate) _workers.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task WorkAsync(Session session)
    {
        try
        {
            while (true)
            {
                await session.Operation.WaitAsync(session.Lifetime.Token).ConfigureAwait(false);
                try
                {
                    AccessoryLocalSnapshot source;
                    lock (_gate)
                    {
                        if (!IsCurrent(session) || BlocksBackup(session.State) || session.LatestSnapshot is null) return;
                        source = session.LatestSnapshot;
                        session.LatestSnapshot = null;
                        session.ActiveSnapshot = source;
                        session.FailedSnapshot = null;
                        session.State = AccessorySyncState.Uploading;
                        session.ErrorCode = null;
                    }
                    await BackupAsync(session, source).ConfigureAwait(false);
                }
                finally { session.Operation.Release(); }
                lock (_gate)
                    if (!IsCurrent(session) || BlocksBackup(session.State) || session.LatestSnapshot is null) return;
            }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested || !IsCurrentThreadSafe(session)) { }
        finally
        {
            lock (_gate)
            {
                session.WorkerRunning = false;
                session.ActiveSnapshot = null;
                if (IsCurrent(session) && session.State == AccessorySyncState.Queued) StartWorker(session);
            }
        }
    }

    private async Task BackupAsync(Session session, AccessoryLocalSnapshot source)
    {
        CommitAttempt? attempt = null;
        try
        {
            var mappings = new Dictionary<string, UploadedAccessoryAsset>(StringComparer.Ordinal);
            foreach (var image in source.Images)
            {
                var uploaded = await InvokeAsync(session, token => session.Transport.UploadAssetAsync(session.Scope, image, token))
                    .ConfigureAwait(false);
                ThrowIfInactive(session);
                try
                {
                    if (uploaded is null) throw new InvalidDataException();
                    uploaded.Validate();
                    if (uploaded.LocalAssetId != image.AssetId || uploaded.PixelWidth != image.PixelWidth || uploaded.PixelHeight != image.PixelHeight)
                        throw new InvalidDataException();
                }
                catch (InvalidDataException) { throw new SafeFailure("invalid_upload_response"); }
                mappings.Add(image.AssetId, uploaded);
            }
            lock (_gate)
            {
                if (!IsCurrent(session)) return;
                // Supersede a still-unsent outfit after image upload. An already sent commit must first
                // finish or be explicitly reconciled, so its revision remains an authoritative baseline.
                if (session.LatestSnapshot is { } latest && latest.LocalCommitVersion > source.LocalCommitVersion)
                {
                    session.ActiveSnapshot = null;
                    session.State = AccessorySyncState.Queued;
                    return;
                }
                if (session.ServerRevision == long.MaxValue) throw new SafeFailure("accessory_revision_exhausted");
                var mapped = new AccessoryOutfitManifest(source.Outfit.SchemaVersion, source.Outfit.Items.Select(item =>
                    mappings.TryGetValue(item.AssetId, out var mapping) ? item with { AssetId = mapping.AssetId } : item).ToArray());
                var request = new CommitAccessoryOutfitRequest(session.ServerRevision, Guid.NewGuid().ToString("N"), mapped);
                request.Validate();
                attempt = new(source, request);
                session.UnknownAttempt = attempt;
                session.State = AccessorySyncState.Committing;
            }
            var stored = await InvokeAsync(session, token => session.Transport.CommitOutfitAsync(session.Scope, attempt.Request, token))
                .ConfigureAwait(false);
            ThrowIfInactive(session);
            ValidateReceipt(attempt.Request, stored);
            AcceptCommit(session, attempt, stored);
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!IsCurrent(session)) return;
                session.ActiveSnapshot = null;
                session.FailedSnapshot = source;
                if (attempt is not null && exception is not AccessoryAccountTransportException
                    { Failure: AccessoryTransportFailure.Rejected or AccessoryTransportFailure.RevisionConflict } &&
                    exception is not SafeFailure { Code: "service_origin_changed" })
                {
                    session.State = AccessorySyncState.OutcomeUnknown;
                    session.ErrorCode = exception is SafeFailure failure ? failure.Code : "commit_outcome_unknown";
                }
                else
                {
                    session.UnknownAttempt = null;
                    session.State = exception is AccessoryAccountTransportException { Failure: AccessoryTransportFailure.RevisionConflict }
                        ? AccessorySyncState.RevisionConflict : AccessorySyncState.Failed;
                    session.ErrorCode = exception switch
                    {
                        SafeFailure failure => failure.Code,
                        AccessoryAccountTransportException { Failure: AccessoryTransportFailure.RevisionConflict } => "accessory_revision_conflict",
                        AccessoryAccountTransportException { Failure: AccessoryTransportFailure.Rejected } => "accessory_commit_rejected",
                        _ => "asset_upload_failed"
                    };
                }
            }
        }
    }

    private void AcceptCommit(Session session, CommitAttempt attempt, StoredAccessoryOutfit stored)
    {
        lock (_gate)
        {
            if (!IsCurrent(session)) return;
            session.ServerRevision = stored.Revision;
            session.LastBackedUpVersion = attempt.Source.LocalCommitVersion;
            session.UnknownAttempt = null;
            session.FailedSnapshot = null;
            session.ActiveSnapshot = null;
            session.State = session.LatestSnapshot is null ? AccessorySyncState.BackedUp : AccessorySyncState.Queued;
            session.ErrorCode = null;
        }
    }

    private static void ValidateReceipt(CommitAccessoryOutfitRequest request, StoredAccessoryOutfit? stored)
    {
        try
        {
            if (stored is null) throw new InvalidDataException();
            stored.Validate();
            if (stored.Revision != request.ExpectedRevision + 1 || !stored.Outfit.Equals(request.Outfit))
                throw new InvalidDataException();
        }
        catch (InvalidDataException) { throw new SafeFailure("invalid_commit_response"); }
    }

    private async Task<T> InvokeAsync<T>(Session session, Func<CancellationToken, Task<T>> invoke,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInactive(session);
        if (ValidateOrigin(session.Transport.ServiceOrigin) != session.ServiceOrigin) throw new SafeFailure("service_origin_changed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token, cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        return await invoke(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
    }

    private static string ValidateOrigin(Uri origin)
    {
        if (origin is null || !origin.IsAbsoluteUri || origin.Scheme is not ("https" or "http") ||
            (origin.Scheme == "http" && !origin.IsLoopback) || origin.UserInfo.Length != 0 ||
            origin.Query.Length != 0 || origin.Fragment.Length != 0) throw new ArgumentException("unsafe_accessory_service_origin");
        return origin.AbsoluteUri;
    }

    private Session? CurrentSession()
    {
        lock (_gate) { ThrowIfDisposed(); return _session; }
    }

    private AccessoryRestoreResult BlockRestore(Session session, string errorCode)
    {
        SetRestoreState(session, AccessorySyncState.RestoreBlocked, errorCode);
        return AccessoryRestoreResult.Blocked;
    }

    private void SetRestoreState(Session session, AccessorySyncState state, string? errorCode)
    {
        lock (_gate)
        {
            if (!IsCurrent(session)) return;
            session.State = state;
            session.ErrorCode = errorCode;
        }
    }

    private void ThrowIfInactive(Session session)
    {
        lock (_gate)
            if (!IsCurrent(session)) throw new OperationCanceledException(session.Lifetime.Token);
    }

    private bool IsCurrentThreadSafe(Session session) { lock (_gate) return IsCurrent(session); }
    private bool IsCurrent(Session session) => !_disposed && ReferenceEquals(_session, session) && session.Fence.IsActive;
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static bool HasPendingLocalChanges(Session session) => session.LastQueuedVersion > session.LastBackedUpVersion;
    private static long? NullableVersion(long value) => value < 0 ? null : value;
    private static bool BlocksBackup(AccessorySyncState state) => state is AccessorySyncState.Failed or
        AccessorySyncState.RevisionConflict or AccessorySyncState.OutcomeUnknown or AccessorySyncState.Reconciling;

    private static void Cancel(Session? session)
    {
        if (session is null) return;
        // A transport's cancellation callback cannot prevent the attachment fence from being invalidated.
        try { session.Lifetime.Cancel(); }
        catch (AggregateException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] workers;
        lock (_accountChange)
        {
            Session? old;
            lock (_gate) { if (_disposed) return; old = _session; }
            old?.Fence.Invalidate();
            lock (_gate) { _disposed = true; _session = null; workers = _workers.ToArray(); }
            Cancel(old);
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private sealed class Session(AccessoryAccountScope scope, IAccessoryAccountTransport transport, string serviceOrigin,
        long serverRevision, long attachmentVersion)
    {
        internal AccessoryAccountScope Scope { get; } = scope;
        internal IAccessoryAccountTransport Transport { get; } = transport;
        internal string ServiceOrigin { get; } = serviceOrigin;
        internal long AttachmentVersion { get; } = attachmentVersion;
        internal CancellationTokenSource Lifetime { get; } = new();
        internal SemaphoreSlim Operation { get; } = new(1, 1);
        internal AccessorySessionFence Fence { get; } = new();
        internal long ServerRevision { get; set; } = serverRevision;
        internal long LastQueuedVersion { get; set; } = -1;
        internal long LastBackedUpVersion { get; set; } = -1;
        internal AccessoryLocalSnapshot? LatestSnapshot { get; set; }
        internal AccessoryLocalSnapshot? ActiveSnapshot { get; set; }
        internal AccessoryLocalSnapshot? FailedSnapshot { get; set; }
        internal CommitAttempt? UnknownAttempt { get; set; }
        internal bool WorkerRunning { get; set; }
        internal AccessorySyncState State { get; set; } = AccessorySyncState.Idle;
        internal string? ErrorCode { get; set; }
    }

    private sealed record CommitAttempt(AccessoryLocalSnapshot Source, CommitAccessoryOutfitRequest Request);
    private sealed class SafeFailure(string code) : Exception("accessory_sync_failure")
    {
        internal string Code { get; } = code;
    }
}
