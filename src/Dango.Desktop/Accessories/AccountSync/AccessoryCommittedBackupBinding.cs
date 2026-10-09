using System.IO;
using Dango.Client.Sync.Accessories;

namespace Dango.Desktop.Accessories.AccountSync;

/// <summary>Queues committed local outfits; account attachment and transport are supplied by the account host.</summary>
public sealed class AccessoryCommittedBackupBinding : IDisposable
{
    private readonly AccessorySettingsSession session;
    private readonly AccessoryAccountLocalRepository repository;
    private readonly AccessoryAccountSync sync;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private bool isDisposed;

    public AccessoryCommittedBackupBinding(AccessorySettingsSession session,
        AccessoryAccountLocalRepository repository, AccessoryAccountSync sync)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.sync = sync ?? throw new ArgumentNullException(nameof(sync));
        if (!repository.IsForSession(session))
            throw new ArgumentException("备份仓库须属于同一本地配饰会话。", nameof(repository));
        _ = session.AccountSyncSettingsRoot;
        session.Committed += OnCommitted;
    }

    public string? LastCaptureErrorCode { get; private set; }
    public long? LastQueuedLocalCommitVersion { get; private set; }

    private void OnCommitted(object? sender, AccessorySettingsCommittedEventArgs args)
    {
        if (isDisposed || args.Origin == AccessoryCommitOrigin.AccountRestore) return;
        var attachment = sync.SafeDiagnostics;
        if (attachment.State == AccessorySyncState.Detached) return;
        try
        {
            // Local commit events run on the owner thread. Capture has no await/yield that can attach another account.
            var snapshot = repository.CaptureCommittedAsync().GetAwaiter().GetResult();
            if (sync.QueueLocalCommit(snapshot, attachment.AttachmentVersion))
            {
                LastQueuedLocalCommitVersion = snapshot.LocalCommitVersion;
                LastCaptureErrorCode = null;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            LastCaptureErrorCode = "local_accessory_snapshot_unavailable";
            // The session catches notification errors after confirming the local commit, and keeps an accurate warning.
            throw;
        }
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("配饰备份绑定须在创建线程解除。");
        if (isDisposed) return;
        isDisposed = true;
        session.Committed -= OnCommitted;
    }
}
