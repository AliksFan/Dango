using System.IO;
using System.Windows.Threading;
using Dango.Client.Sync.Accessories;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.AccountSync;
using Dango.Protocol.Accessories;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>One authenticated attachment owns its revision, operations and status. Login is read-only.</summary>
internal sealed class AccessoryAccountController : IDisposable
{
    private readonly AccessoryAccountPanelView panel;
    private readonly AccessorySettingsSession session;
    private readonly AccessoryAccountLocalRepository repository;
    private readonly AccessoryAccountSync sync;
    private readonly AccessoryCommittedBackupBinding binding;
    private readonly Action restored;
    private readonly Action authenticationInvalidated;
    private readonly DispatcherTimer statusTimer;
    private CancellationTokenSource? attachmentLifetime;
    private AccessoryAccountScope? scope;
    private IAccessoryAccountTransport? transport;
    private long generation;
    private bool ready, initializing, disposed, deferredCommit, operationBusy;
    private int? cloudCount;
    private string? localFailure;
    private long? failedCaptureVersion;
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;
    internal Task Disposal { get; private set; } = Task.CompletedTask;

    internal AccessoryAccountController(AccessoryAccountPanelView panel, AccessorySettingsSession session,
        Action restored, Action authenticationInvalidated)
    {
        this.panel = panel;
        this.session = session;
        this.restored = restored;
        this.authenticationInvalidated = authenticationInvalidated;
        repository = new(session, panel.Dispatcher);
        sync = new(repository);
        binding = new(session, repository, sync);
        statusTimer = new(DispatcherPriority.Background, panel.Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        statusTimer.Tick += OnStatusTick;
        panel.Requested += OnRequested;
        session.Committed += OnCommitted;
        UpdatePanel();
    }

    internal AccessorySyncDiagnostics Diagnostics => sync.SafeDiagnostics;

    internal void Attach(AccessoryAccountScope attachedScope, IAccessoryAccountTransport attachedTransport)
    {
        panel.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        attachedScope.Validate();
        ArgumentNullException.ThrowIfNull(attachedTransport);
        Detach();
        scope = attachedScope;
        transport = attachedTransport;
        attachmentLifetime = new();
        statusTimer.Start();
        PendingOperation = ReadBaselineAsync(generation, attachmentLifetime.Token);
    }

    internal void Detach()
    {
        panel.Dispatcher.VerifyAccess();
        generation++;
        sync.DetachAccount();
        attachmentLifetime?.Cancel();
        attachmentLifetime?.Dispose();
        attachmentLifetime = null;
        scope = null;
        transport = null;
        ready = initializing = operationBusy = deferredCommit = false;
        cloudCount = null;
        localFailure = null;
        failedCaptureVersion = null;
        statusTimer.Stop();
        UpdatePanel();
    }

    private async Task ReadBaselineAsync(long epoch, CancellationToken token, long minimumRevision = 0)
    {
        var attachedScope = scope;
        var attachedTransport = transport;
        if (attachedScope is null || attachedTransport is null) return;
        initializing = true; ready = false; localFailure = null; UpdatePanel();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var stored = await attachedTransport.GetCurrentOutfitAsync(attachedScope, timeout.Token).WaitAsync(timeout.Token);
            if (!IsActive(epoch)) return;
            token.ThrowIfCancellationRequested();
            stored?.Validate();
            if (stored is not null && stored.Revision < minimumRevision || stored is null && minimumRevision > 0)
                throw new InvalidDataException("stale_account_outfit");
            sync.AttachAccount(attachedScope, attachedTransport, stored?.Revision ?? 0);
            cloudCount = stored?.Outfit.Items.Count;
            ready = true;
            // Login itself is read-only. Only a real local commit made during this read is carried forward.
            if (deferredCommit)
            {
                deferredCommit = false;
                QueueSavedOutfit();
            }
        }
        catch (AccessoryAccountTransportException error) when (error.Failure == AccessoryTransportFailure.Authentication)
        { if (IsActive(epoch)) InvalidateAuthentication(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { if (IsActive(epoch)) localFailure = "account_outfit_read_failed"; }
        finally { if (IsActive(epoch)) { initializing = false; UpdatePanel(); } }
    }

    private bool IsActive(long epoch) => !disposed && scope is not null && generation == epoch;

    private void OnCommitted(object? sender, AccessorySettingsCommittedEventArgs args)
    {
        if (disposed || scope is null) return;
        localFailure = null;
        if (args.Origin == AccessoryCommitOrigin.AccountRestore)
        { failedCaptureVersion = null; UpdatePanel(); return; }
        failedCaptureVersion = ready && !initializing && binding.LastCaptureErrorCode is not null &&
            binding.LastQueuedLocalCommitVersion != args.LocalCommitVersion ? args.LocalCommitVersion : null;
        if (!ready || initializing) deferredCommit = true;
        UpdatePanel();
    }

    private void OnStatusTick(object? sender, EventArgs args)
    {
        if (scope is not null && sync.SafeDiagnostics.ErrorCode is "authentication_required" or "accessory_authentication_required")
        { InvalidateAuthentication(); return; }
        UpdatePanel();
    }

    private void InvalidateAuthentication()
    {
        Detach();
        authenticationInvalidated();
    }

    private void OnRequested(AccessoryAccountAction action)
    {
        if (disposed || scope is null || attachmentLifetime is null || initializing || operationBusy) return;
        var diagnostic = sync.SafeDiagnostics;
        if (action == AccessoryAccountAction.RefreshAccount)
        {
            if (diagnostic.State is AccessorySyncState.OutcomeUnknown or AccessorySyncState.Reconciling ||
                diagnostic.HasPendingLocalChanges && diagnostic.State is not (AccessorySyncState.RevisionConflict or AccessorySyncState.Failed)) return;
            var observedRevision = diagnostic.ServerRevision ?? 0;
            sync.DetachAccount();
            PendingOperation = ReadBaselineAsync(generation, attachmentLifetime.Token, observedRevision);
            return;
        }
        if (!ready) return;
        if (action == AccessoryAccountAction.BackupLocal)
        {
            if (session.IsDirty || diagnostic.State is AccessorySyncState.OutcomeUnknown or AccessorySyncState.Reconciling
                or AccessorySyncState.RevisionConflict or AccessorySyncState.Restoring) return;
            try
            {
                if (session.IsCompatibilityMode && !session.TrySave()) localFailure = "local_accessory_snapshot_unavailable";
                else QueueSavedOutfit();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { localFailure = "local_accessory_snapshot_unavailable"; }
            UpdatePanel();
            return;
        }
        if (action == AccessoryAccountAction.RetryBackup)
        { sync.RetryLatestBackup(); UpdatePanel(); return; }
        PendingOperation = RunOperationAsync(action, generation, attachmentLifetime.Token);
    }

    private void QueueSavedOutfit()
    {
        try
        {
            var diagnostic = sync.SafeDiagnostics;
            var snapshot = repository.CaptureCommittedAsync().GetAwaiter().GetResult();
            if (!sync.QueueLocalCommit(snapshot, diagnostic.AttachmentVersion) &&
                diagnostic.State == AccessorySyncState.Failed) sync.RetryLatestBackup();
            localFailure = null;
            failedCaptureVersion = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { localFailure = "local_accessory_snapshot_unavailable"; }
    }

    private async Task RunOperationAsync(AccessoryAccountAction action, long epoch, CancellationToken token)
    {
        operationBusy = true; localFailure = null; UpdatePanel();
        try
        {
            if (action == AccessoryAccountAction.RestoreAccount)
            {
                var result = await sync.RestoreAsync(token);
                if (IsActive(epoch) && result == AccessoryRestoreResult.Restored)
                {
                    try { restored(); }
                    catch (Exception) { localFailure = "accessory_presentation_refresh_failed"; }
                }
            }
            else if (action == AccessoryAccountAction.ReconcileCommit) await sync.ReconcileUnknownCommitAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (IsActive(epoch)) localFailure = "accessory_account_operation_failed"; }
        finally { if (IsActive(epoch)) { operationBusy = false; UpdatePanel(); } }
    }
    private void UpdatePanel()
    {
        var diagnostic = sync.SafeDiagnostics;
        if (diagnostic.State == AccessorySyncState.BackedUp && diagnostic.LastBackedUpLocalVersion == session.LocalCommitVersion)
            cloudCount = session.SavedConfiguration.Items.Count(item => item.IsEquipped);
        var unknown = diagnostic.State is AccessorySyncState.OutcomeUnknown or AccessorySyncState.Reconciling;
        var conflict = diagnostic.State == AccessorySyncState.RevisionConflict;
        var busy = initializing || operationBusy || diagnostic.State is AccessorySyncState.Queued or AccessorySyncState.Uploading
            or AccessorySyncState.Committing or AccessorySyncState.Restoring or AccessorySyncState.Reconciling;
        var captureFailed = localFailure == "local_accessory_snapshot_unavailable" || failedCaptureVersion == session.LocalCommitVersion;
        var message = scope is null ? "登录后可将当前穿搭私有备份到账号。本机佩戴不受影响。"
            : initializing ? "正在读取账号穿搭。本机穿搭保持不变。"
            : localFailure == "account_outfit_read_failed" ? "账号穿搭读取失败，本机穿搭保留。请重新读取后再选择。"
            : captureFailed ? "本机穿搭已保存，但所需图片暂时无法读取，尚未备份到账号。请处理图片后重试。"
            : localFailure == "accessory_presentation_refresh_failed" ? "配饰已恢复到本机，但画面暂未刷新。请重新打开控制中心。"
            : localFailure is not null ? "配饰账号操作未完成，本机结果保留。请处理问题后重试。"
            : diagnostic.State switch
            {
                AccessorySyncState.Queued => "本机穿搭已保存，等待账号备份。",
                AccessorySyncState.Uploading => "本机穿搭已保存，正在上传所需配饰图片。",
                AccessorySyncState.Committing => "本机穿搭已保存，正在保存账号穿搭。",
                AccessorySyncState.BackedUp => "当前穿搭已私有备份到账号。",
                AccessorySyncState.Failed => "本机结果保留，账号操作未完成。可重试备份或重新读取。",
                AccessorySyncState.RevisionConflict => "账号穿搭已经变化，本次未覆盖。请重新读取后明确选择。",
                AccessorySyncState.OutcomeUnknown => "账号保存结果尚未确认。请先核对保存结果，本机结果保留。",
                AccessorySyncState.Reconciling => "正在核对账号保存结果。核对完成前不会重复提交。",
                AccessorySyncState.Restoring => "正在完整验证并恢复账号穿搭。本机原穿搭暂时保留。",
                AccessorySyncState.Restored => "账号穿搭已完整恢复到本机。",
                AccessorySyncState.RestoreBlocked => "恢复未覆盖本机配置。请先处理草稿或尚未完成的备份，再重新选择。",
                _ => cloudCount is null ? "账号尚未备份配饰。可备份本机当前穿搭；在编辑器应用后会自动备份。"
                    : $"本机当前佩戴 {session.SavedConfiguration.Items.Count(item => item.IsEquipped)} 件，账号穿搭 {cloudCount} 件。请明确选择来源。",
            };
        if (scope is not null && session.IsDirty) message += " 当前配饰还有未应用草稿，请先应用或取消草稿。";
        panel.Apply(new(message, scope is not null, busy,
            CanBackup: ready && !session.IsDirty && !unknown && !conflict,
            CanRestore: ready && cloudCount is not null && !session.IsDirty && !unknown && !conflict && !diagnostic.HasPendingLocalChanges,
            CanRefresh: !unknown && (!ready || !diagnostic.HasPendingLocalChanges || conflict || diagnostic.State == AccessorySyncState.Failed),
            CanRetry: diagnostic.State == AccessorySyncState.Failed && diagnostic.HasPendingLocalChanges,
            CanReconcile: diagnostic.State == AccessorySyncState.OutcomeUnknown));
    }

    public void Dispose()
    {
        panel.Dispatcher.VerifyAccess();
        if (disposed) return;
        Detach(); disposed = true;
        panel.Requested -= OnRequested;
        session.Committed -= OnCommitted;
        statusTimer.Stop(); statusTimer.Tick -= OnStatusTick;
        binding.Dispose();
        Disposal = sync.DisposeAsync().AsTask();
    }
}
