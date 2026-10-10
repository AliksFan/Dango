using System.Text.Json;
using Dango.Protocol;

namespace Dango.Client.Sync.Pets;

public enum PetLiveSyncStatus { Synchronized, Pending, Synchronizing, PausedForDraft, Offline, Failed, Stopped }

// Synchronized describes current local/cloud parity. The last local-save outcome separately
// distinguishes an acknowledged upload from a local edit superseded by a later server commit.
public sealed record PetLiveSyncState(PetLiveSyncStatus Status, long HighestCloudVersion,
    bool HasPendingLocalSave, string? FailureCode = null,
    long? LastLocalSaveSerial = null, PetChoiceOutcome? LastLocalSaveOutcome = null);

/// <summary>
/// One authenticated session. The caller supplies a confirmed login baseline, successful-save
/// notifications and a bounded/backed-off polling schedule. No timer or archived replay is started here.
/// A submitted edit never gets a newer expected version. Only another successful local save is new work.
/// </summary>
public sealed class PetConfigurationLiveSyncCoordinator : IDisposable
{
    private const int MaximumWriteAttempts = 2;
    private const int MaximumReceiptAttempts = 2;
    private readonly IRemotePetConfigurationClient remote;
    private readonly ILocalPetConfigurationRepository local;
    private readonly IPetConfigurationJournal journal;
    private readonly TimeProvider clock;
    private readonly Uri origin;
    private readonly Guid userId, petId;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim pumping = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private PetConfigurationSnapshot highest;
    private PetLiveSyncState state;
    private long savedSerial;
    private long? pendingSerial;
    private Attempt? attempt;
    private PendingReceipt? pendingReceipt;
    private long? lastLocalSaveSerial;
    private PetChoiceOutcome? lastLocalSaveOutcome;
    private bool stopped;

    public PetConfigurationLiveSyncCoordinator(IRemotePetConfigurationClient remote,
        ILocalPetConfigurationRepository local, IPetConfigurationJournal journal,
        PetConfigurationSnapshot confirmedCloudBaseline, TimeProvider? clock = null)
    {
        this.remote = remote; this.local = local; this.journal = journal;
        this.clock = clock ?? TimeProvider.System;
        origin = PetConfigurationHttpClient.ValidateOrigin(remote.Origin);
        userId = remote.UserId; petId = remote.PetId;
        if (userId == Guid.Empty || petId == Guid.Empty) throw new PetConfigurationChoiceException("pet_binding_changed");
        highest = Validate(confirmedCloudBaseline);
        state = new(PetLiveSyncStatus.Synchronized, highest.ConfigurationVersion, false);
        lifetimeToken = lifetime.Token;
    }

    public PetLiveSyncState State { get { lock (stateLock) return state; } }

    /// <summary>Call only after an explicit local edit has successfully reached durable storage.</summary>
    public void NotifyLocalSaved()
    {
        lock (stateLock)
        {
            if (stopped) return;
            pendingSerial = checked(++savedSerial);
            Publish(PetLiveSyncStatus.Pending);
        }
    }

    /// <summary>At most two reads and one PATCH. Overlapping calls do not build an unbounded queue.</summary>
    public async Task<PetLiveSyncState> PumpAsync(CancellationToken ct = default)
    {
        if (State.Status == PetLiveSyncStatus.Stopped) return State;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
        var token = linked.Token;
        var entered = false;
        try
        {
            entered = await pumping.WaitAsync(0, token);
            if (!entered) return State;
            Check(token); SetStatus(PetLiveSyncStatus.Synchronizing);
            var observed = await ReadCloudAsync(token);
            PendingReceipt? receipt;
            lock (stateLock) receipt = pendingReceipt;
            if (receipt is not null && !await TryReceiptAsync(receipt, token)) return State;
            Attempt? current;
            lock (stateLock) current = attempt;
            if (current is not null)
            {
                if (observed.ConfigurationVersion > current.ExpectedVersion)
                {
                    var receiptSaved = await FinishAsync(current, AppearanceContract.Equivalent(observed.Configuration, current.Configuration)
                        ? PetChoiceOutcome.TargetSatisfied : PetChoiceOutcome.Conflict, observed, token);
                    if (!receiptSaved) return State;
                    await ReconcileLocalAsync(token);
                    return State;
                }
                // Drafts are never captured/uploaded. An already submitted saved edit keeps its immutable intent.
                _ = await local.CaptureAsync(token); Check(token);
                if (!current.RetryAllowed || current.WriteAttempts >= MaximumWriteAttempts)
                {
                    var newer = Pending();
                    if (newer is null || newer <= current.Serial)
                    {
                        SetStatus(PetLiveSyncStatus.Failed, "cloud_outcome_unknown");
                        return State; // Only another explicit successful save can be a new write intent.
                    }
                    // Do not revive this edit or reset its budget. Preserve its unresolved outcome
                    // before allowing the separately numbered, newly saved edit to be submitted.
                    if (!await FinishAsync(current, PetChoiceOutcome.OutcomeUnknown, observed, token)) return State;
                }
                else return await SubmitAsync(current, token);
            }

            var serial = Pending();
            if (serial is null) return await ReconcileLocalAsync(token);
            var captured = await local.CaptureAsync(token); Check(token);
            if (AppearanceContract.Equivalent(captured.NormalizedConfiguration, observed.Configuration))
            {
                AcknowledgeEquivalent(serial.Value);
                return State;
            }
            var id = await journal.SaveOriginalsAsync(new(origin, captured, observed,
                clock.GetUtcNow(), PetConfigurationChoice.Local), token);
            Check(token);
            var fresh = await local.CaptureAsync(token); Check(token);
            if (Pending() != serial || !fresh.SameOriginal(captured))
            {
                await WriteReceiptAsync(id, PetConfigurationChoice.Local, PetChoiceOutcome.Failed,
                    observed.ConfigurationVersion, "local_configuration_conflict", token);
                SetStatus(PetLiveSyncStatus.Pending);
                return State;
            }
            current = new(serial.Value, observed.ConfigurationVersion, captured.NormalizedConfiguration, id);
            lock (stateLock)
            {
                Check(token);
                if (pendingSerial != serial) { Publish(PetLiveSyncStatus.Pending); return state; }
                attempt = current; pendingSerial = null;
            }
            return await SubmitAsync(current, token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation after transmission cannot prove that the server rolled back.
            if (!lifetimeToken.IsCancellationRequested) SetStatus(PetLiveSyncStatus.Offline,
                HasAttempt() ? "cloud_outcome_unknown" : "operation_cancelled");
        }
        catch (PetConfigurationHttpException fault) when (fault.Failure == PetHttpFailure.Authentication)
        { Stop("authentication_required"); }
        catch (PetConfigurationChoiceException fault) when (fault.Code == "pet_binding_changed")
        { Stop(fault.Code); }
        catch (PetConfigurationChoiceException fault) when (fault.Code == "local_unsaved_changes")
        { SetStatus(PetLiveSyncStatus.PausedForDraft, fault.Code); }
        catch (PetConfigurationHttpException fault) when (fault.Failure == PetHttpFailure.Unavailable)
        { SetStatus(PetLiveSyncStatus.Offline, HasAttempt() ? "cloud_outcome_unknown" : "service_unavailable"); }
        catch (PetConfigurationHttpException fault) when (fault.Failure == PetHttpFailure.InvalidResponse)
        { SetStatus(PetLiveSyncStatus.Failed, HasAttempt() ? "cloud_outcome_unknown" : "remote_response_invalid"); }
        catch (PetConfigurationHttpException) { SetStatus(PetLiveSyncStatus.Failed, "remote_request_rejected"); }
        catch (PetConfigurationChoiceException fault) { SetStatus(PetLiveSyncStatus.Failed, fault.Code); }
        catch (AppearanceContractException) { SetStatus(PetLiveSyncStatus.Failed, "remote_response_invalid"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { SetStatus(PetLiveSyncStatus.Failed, "local_storage_failed"); }
        finally { if (entered) pumping.Release(); }
        return State;
    }

    private async Task<PetLiveSyncState> SubmitAsync(Attempt current, CancellationToken ct)
    {
        Check(ct); current.WriteAttempts++;
        try
        {
            var updated = Validate(await remote.UpdateAsync(current.ExpectedVersion, current.Configuration, ct));
            Check(ct);
            if (current.ExpectedVersion == long.MaxValue || updated.ConfigurationVersion != current.ExpectedVersion + 1
                || !AppearanceContract.Equivalent(updated.Configuration, current.Configuration))
                throw new PetConfigurationChoiceException("remote_response_invalid");
            Observe(updated); // An acknowledged response cannot lower the already observed version.
            var receiptSaved = await FinishAsync(current, PetChoiceOutcome.CloudUpdated, updated, ct);
            if (!receiptSaved) return State;
            await ReconcileLocalAsync(ct);
            return State;
        }
        catch (PetConfigurationHttpException fault) when (fault.Failure is PetHttpFailure.Conflict or PetHttpFailure.Unavailable)
        {
            if (fault.Failure == PetHttpFailure.Conflict) current.RetryAllowed = false;
            // A second read resolves a committed-but-lost response and observes another device's later commit.
            var observed = await ReadCloudAsync(ct);
            if (observed.ConfigurationVersion > current.ExpectedVersion)
            {
                var receiptSaved = await FinishAsync(current, AppearanceContract.Equivalent(observed.Configuration, current.Configuration)
                    ? PetChoiceOutcome.TargetSatisfied : PetChoiceOutcome.Conflict, observed, ct);
                if (!receiptSaved) return State;
                await ReconcileLocalAsync(ct);
                return State;
            }
            // A 409 without a newer version is not a successful mutation or a usable new baseline.
            SetStatus(fault.Failure == PetHttpFailure.Unavailable ? PetLiveSyncStatus.Offline : PetLiveSyncStatus.Failed,
                "cloud_outcome_unknown");
            return State;
        }
        catch (PetConfigurationHttpException fault) when (fault.Failure == PetHttpFailure.Rejected)
        {
            PetConfigurationSnapshot observed;
            lock (stateLock) observed = highest;
            await FinishAsync(current, PetChoiceOutcome.Failed, observed, ct);
            SetStatus(PetLiveSyncStatus.Failed, "remote_request_rejected");
            return State;
        }
    }

    private async Task<bool> FinishAsync(Attempt completed, PetChoiceOutcome outcome,
        PetConfigurationSnapshot observed, CancellationToken ct)
    {
        var receipt = new PendingReceipt(completed.JournalId, new(PetConfigurationChoice.Local, outcome,
            clock.GetUtcNow(), observed.ConfigurationVersion, outcome switch
            {
                PetChoiceOutcome.Conflict => "pet_configuration_conflict",
                PetChoiceOutcome.OutcomeUnknown => "cloud_outcome_unknown",
                PetChoiceOutcome.Failed => "remote_request_rejected",
                _ => null
            }));
        lock (stateLock)
        {
            Check(lifetimeToken);
            if (pendingReceipt is not null) throw new PetConfigurationChoiceException("archive_write_failed");
            pendingReceipt = receipt;
            if (ReferenceEquals(attempt, completed)) attempt = null;
            lastLocalSaveSerial = completed.Serial; lastLocalSaveOutcome = outcome;
            Publish(PetLiveSyncStatus.Synchronizing);
        }
        return await TryReceiptAsync(receipt, ct);
    }

    private async Task<PetLiveSyncState> ReconcileLocalAsync(CancellationToken ct)
    {
        Check(ct);
        var serial = Serial();
        var captured = await local.CaptureAsync(ct); Check(ct);
        PetConfigurationSnapshot cloud;
        lock (stateLock) cloud = highest;
        if (AppearanceContract.Equivalent(captured.NormalizedConfiguration, cloud.Configuration))
        {
            AcknowledgeEquivalent(serial);
            return State;
        }
        if (Pending() is not null || Serial() != serial)
        { SetStatus(PetLiveSyncStatus.Pending); return State; }
        var id = await journal.SaveOriginalsAsync(new(origin, captured, cloud,
            clock.GetUtcNow(), PetConfigurationChoice.Cloud), ct);
        Check(ct);
        if (Pending() is not null || Serial() != serial)
        { SetStatus(PetLiveSyncStatus.Pending); return State; }
        await local.ApplyAsync(captured, cloud.Configuration, ct); Check(lifetimeToken);
        var saved = await WriteReceiptAsync(id, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied,
            cloud.ConfigurationVersion, null, ct);
        Check(ct);
        // A save notification caused by applying the cloud is merely equal content. A real edit during
        // asynchronous application is retained, never suppressed by a broad 'applying' flag.
        var afterSerial = Serial();
        var after = await local.CaptureAsync(ct); Check(ct);
        if (AppearanceContract.Equivalent(after.NormalizedConfiguration, cloud.Configuration))
            AcknowledgeEquivalent(afterSerial);
        else SetStatus(PetLiveSyncStatus.Pending);
        if (!saved) SetStatus(PetLiveSyncStatus.Failed, "archive_write_failed");
        return State;
    }

    private async Task<bool> WriteReceiptAsync(Guid id, PetConfigurationChoice choice, PetChoiceOutcome outcome,
        long version, string? failure, CancellationToken ct)
    {
        var receipt = new PendingReceipt(id, new(choice, outcome, clock.GetUtcNow(), version, failure));
        lock (stateLock)
        {
            Check(lifetimeToken);
            if (pendingReceipt is not null) throw new PetConfigurationChoiceException("archive_write_failed");
            pendingReceipt = receipt;
        }
        return await TryReceiptAsync(receipt, ct);
    }

    private async Task<bool> TryReceiptAsync(PendingReceipt receipt, CancellationToken ct)
    {
        Check(ct);
        if (receipt.WriteAttempts >= MaximumReceiptAttempts)
        { SetStatus(PetLiveSyncStatus.Failed, "archive_write_failed"); return false; }
        receipt.WriteAttempts++;
        try
        {
            await journal.WriteReceiptAsync(receipt.JournalId, receipt.Receipt, ct);
            Check(lifetimeToken);
            lock (stateLock) if (ReferenceEquals(pendingReceipt, receipt)) pendingReceipt = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PetConfigurationChoiceException)
        { receipt.HadFailure = true; SetStatus(PetLiveSyncStatus.Failed, "archive_write_failed"); return false; }
    }

    private async Task<PetConfigurationSnapshot> ReadCloudAsync(CancellationToken ct)
    {
        Check(ct);
        var observed = Validate(await remote.ReadAsync(ct)); Check(ct);
        Observe(observed);
        return observed;
    }

    private PetConfigurationSnapshot Validate(PetConfigurationSnapshot snapshot)
    {
        if (remote.Origin != origin || remote.UserId != userId || remote.PetId != petId
            || snapshot.UserId != userId || snapshot.PetId != petId)
            throw new PetConfigurationChoiceException("pet_binding_changed");
        if (snapshot.ConfigurationVersion < 1) throw new PetConfigurationChoiceException("remote_response_invalid");
        try { return snapshot with { Configuration = AppearanceContract.ValidateAndClone(snapshot.Configuration) }; }
        catch (AppearanceContractException) { throw new PetConfigurationChoiceException("remote_response_invalid"); }
    }

    private void Observe(PetConfigurationSnapshot snapshot)
    {
        lock (stateLock)
        {
            Check(lifetimeToken);
            if (snapshot.ConfigurationVersion < highest.ConfigurationVersion)
                throw new PetConfigurationChoiceException("remote_version_regressed");
            if (snapshot.ConfigurationVersion == highest.ConfigurationVersion
                && !AppearanceContract.Equivalent(snapshot.Configuration, highest.Configuration))
                throw new PetConfigurationChoiceException("remote_response_invalid");
            highest = snapshot;
        }
    }

    private long? Pending() { lock (stateLock) return pendingSerial; }
    private long Serial() { lock (stateLock) return savedSerial; }
    private bool HasAttempt() { lock (stateLock) return attempt is not null; }
    private void AcknowledgeEquivalent(long throughSerial)
    {
        lock (stateLock)
        {
            if (stopped) return;
            if (pendingSerial is { } pending && pending <= throughSerial)
            {
                lastLocalSaveSerial = pending; lastLocalSaveOutcome = PetChoiceOutcome.TargetSatisfied;
                pendingSerial = null;
            }
            Publish(pendingSerial is null && attempt is null ? PetLiveSyncStatus.Synchronized : PetLiveSyncStatus.Pending);
        }
    }
    private void Check(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (stateLock) if (stopped) throw new OperationCanceledException(lifetimeToken);
    }
    private void SetStatus(PetLiveSyncStatus status, string? failure = null)
    { lock (stateLock) if (!stopped) Publish(status, failure); }
    private void Publish(PetLiveSyncStatus status, string? failure = null)
    {
        if (pendingReceipt is not null && status != PetLiveSyncStatus.Stopped
            && (pendingReceipt.HadFailure || status == PetLiveSyncStatus.Synchronized))
        { status = PetLiveSyncStatus.Failed; failure = "archive_write_failed"; }
        state = new(status, highest.ConfigurationVersion, pendingSerial is not null || attempt is not null,
            failure, lastLocalSaveSerial, lastLocalSaveOutcome);
    }

    public void Stop() => Stop(null);
    private void Stop(string? failure)
    {
        lock (stateLock)
        {
            if (stopped) return;
            stopped = true; attempt = null; pendingSerial = null; pendingReceipt = null;
            Publish(PetLiveSyncStatus.Stopped, failure);
        }
        lifetime.Cancel();
    }
    public void Dispose() { Stop(); lifetime.Dispose(); }

    private sealed class Attempt(long serial, long expectedVersion, JsonElement configuration, Guid journalId)
    {
        public long Serial { get; } = serial;
        public long ExpectedVersion { get; } = expectedVersion;
        public JsonElement Configuration { get; } = configuration.Clone();
        public Guid JournalId { get; } = journalId;
        public int WriteAttempts { get; set; }
        public bool RetryAllowed { get; set; } = true;
    }

    private sealed class PendingReceipt(Guid journalId, PetConfigurationReceipt receipt)
    {
        public Guid JournalId { get; } = journalId;
        public PetConfigurationReceipt Receipt { get; } = receipt;
        public int WriteAttempts { get; set; }
        public bool HadFailure { get; set; }
    }
}
