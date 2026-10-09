using System.Text.Json;
using Dango.Protocol;

namespace Dango.Client.Sync.Pets;

public class PetConfigurationChoiceException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class LocalPetConfigurationSnapshot
{
    public const int OriginalMaximumBytes = 65536;
    private readonly byte[] originalBytes;
    private readonly JsonElement normalized;

    public LocalPetConfigurationSnapshot(bool exists, ReadOnlyMemory<byte> originalBytes, JsonElement normalizedConfiguration)
    {
        if (originalBytes.Length > OriginalMaximumBytes || !exists && originalBytes.Length != 0)
            throw new PetConfigurationChoiceException("local_configuration_invalid");
        Exists = exists;
        this.originalBytes = originalBytes.ToArray();
        normalized = AppearanceContract.ValidateAndClone(normalizedConfiguration);
    }

    public bool Exists { get; }
    public ReadOnlyMemory<byte> OriginalBytes => originalBytes.ToArray();
    public JsonElement NormalizedConfiguration => normalized.Clone();

    public bool SameOriginal(LocalPetConfigurationSnapshot other) => Exists == other.Exists
        && originalBytes.AsSpan().SequenceEqual(other.originalBytes)
        && AppearanceContract.Equivalent(normalized, other.normalized);
}

public interface ILocalPetConfigurationRepository
{
    Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct);
    Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct);
}

public interface IRemotePetConfigurationClient
{
    Uri Origin { get; }
    Guid UserId { get; }
    Guid PetId { get; }
    Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct);
    Task<PetConfigurationSnapshot> UpdateAsync(long expectedVersion, JsonElement configuration, CancellationToken ct);
}

public enum PetConfigurationChoice { Local, Cloud }
public enum PetChoiceOutcome { LocalApplied, CloudUpdated, TargetSatisfied, Conflict, OutcomeUnknown, Failed }

public sealed record PetConfigurationOriginals(Uri Origin, LocalPetConfigurationSnapshot Local,
    PetConfigurationSnapshot Cloud, DateTimeOffset CapturedAt, PetConfigurationChoice? Choice = null);
public sealed record PetConfigurationReceipt(PetConfigurationChoice Choice, PetChoiceOutcome Outcome,
    DateTimeOffset RecordedAt, long? ObservedCloudVersion, string? FailureCode);
public sealed record PetConfigurationChoiceResult(Guid JournalId, PetChoiceOutcome Outcome,
    PetConfigurationSnapshot? ObservedCloud, string? FailureCode = null, bool ReceiptSaved = true);

public interface IPetConfigurationJournal
{
    Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct);
    Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct);
    Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct);
}

public sealed class PetConfigurationReview
{
    internal PetConfigurationReview(PetConfigurationOriginals originals) => Originals = originals;
    public PetConfigurationOriginals Originals { get; }
}

public sealed class PetConfigurationChoiceCoordinator(IRemotePetConfigurationClient remote,
    ILocalPetConfigurationRepository local, IPetConfigurationJournal journal, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim choosing = new(1, 1);

    // Loading a review never writes either configuration and never resumes an archived operation.
    public async Task<PetConfigurationReview> BeginAsync(CancellationToken ct = default)
    {
        var localSnapshot = await local.CaptureAsync(ct);
        var cloud = Validate(await remote.ReadAsync(ct));
        return new(new(remote.Origin, localSnapshot, cloud, clock.GetUtcNow()));
    }

    public async Task<PetConfigurationChoiceResult> ChooseAsync(PetConfigurationReview review,
        PetConfigurationChoice choice, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(choice)) throw new PetConfigurationChoiceException("configuration_choice_invalid");
        await choosing.WaitAsync(ct);
        try
        {
            var originals = review.Originals;
            if (originals.Origin != remote.Origin) throw new PetConfigurationChoiceException("pet_binding_changed");
            Validate(originals.Cloud);
            if (!originals.Local.SameOriginal(await local.CaptureAsync(ct)))
                throw new PetConfigurationChoiceException("local_configuration_conflict");
            var id = await journal.SaveOriginalsAsync(originals with { Choice = choice }, ct);
            PetConfigurationChoiceResult result;
            try
            {
                var current = Validate(await remote.ReadAsync(ct));
                if (choice == PetConfigurationChoice.Cloud)
                {
                    if (!AppearanceContract.Equivalent(current.Configuration, originals.Cloud.Configuration))
                        result = new(id, PetChoiceOutcome.Conflict, current, "pet_configuration_conflict");
                    else
                    {
                        await local.ApplyAsync(originals.Local, originals.Cloud.Configuration, ct);
                        result = new(id, PetChoiceOutcome.LocalApplied, current);
                    }
                }
                else if (AppearanceContract.Equivalent(current.Configuration, originals.Local.NormalizedConfiguration))
                    result = new(id, PetChoiceOutcome.TargetSatisfied, current);
                else if (current.ConfigurationVersion != originals.Cloud.ConfigurationVersion)
                    result = new(id, PetChoiceOutcome.Conflict, current, "pet_configuration_conflict");
                else
                    result = await UpdateAsync(id, originals, ct);
            }
            catch (PetConfigurationChoiceException fault) { result = new(id, PetChoiceOutcome.Failed, null, fault.Code); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { result = new(id, PetChoiceOutcome.Failed, null, "local_storage_failed"); }
            return await RecordAsync(result, choice, ct);
        }
        finally { choosing.Release(); }
    }

    private async Task<PetConfigurationChoiceResult> UpdateAsync(Guid id, PetConfigurationOriginals originals, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // The local file may change during archive/network I/O. Never upload a stale selection.
            if (!originals.Local.SameOriginal(await local.CaptureAsync(ct)))
                throw new PetConfigurationChoiceException("local_configuration_conflict");
            try
            {
                var updated = Validate(await remote.UpdateAsync(originals.Cloud.ConfigurationVersion, originals.Local.NormalizedConfiguration, ct));
                if (updated.ConfigurationVersion != originals.Cloud.ConfigurationVersion + 1
                    || !AppearanceContract.Equivalent(updated.Configuration, originals.Local.NormalizedConfiguration))
                    return new(id, PetChoiceOutcome.OutcomeUnknown, null, "remote_response_invalid");
                return new(id, PetChoiceOutcome.CloudUpdated, updated);
            }
            catch (PetConfigurationHttpException fault) when (fault.Failure == PetHttpFailure.Conflict)
            {
                var observed = Validate(await remote.ReadAsync(ct));
                return AppearanceContract.Equivalent(observed.Configuration, originals.Local.NormalizedConfiguration)
                    ? new(id, PetChoiceOutcome.TargetSatisfied, observed)
                    : new(id, PetChoiceOutcome.Conflict, observed, "pet_configuration_conflict");
            }
            catch (PetConfigurationHttpException fault) when (fault.Failure == PetHttpFailure.Unavailable)
            {
                if (attempt == 1) return new(id, PetChoiceOutcome.OutcomeUnknown, null, "cloud_outcome_unknown");
            }
            catch (PetConfigurationChoiceException fault) when (fault.Code is "remote_response_invalid" or "pet_binding_changed")
            { return new(id, PetChoiceOutcome.OutcomeUnknown, null, fault.Code); }
        }
        throw new InvalidOperationException("The bounded update attempt budget was exhausted.");
    }

    private PetConfigurationSnapshot Validate(PetConfigurationSnapshot snapshot)
    {
        if (snapshot.UserId != remote.UserId || snapshot.PetId != remote.PetId)
            throw new PetConfigurationChoiceException("pet_binding_changed");
        if (snapshot.ConfigurationVersion < 1) throw new PetConfigurationChoiceException("remote_response_invalid");
        try { return snapshot with { Configuration = AppearanceContract.ValidateAndClone(snapshot.Configuration) }; }
        catch (AppearanceContractException) { throw new PetConfigurationChoiceException("remote_response_invalid"); }
    }

    private async Task<PetConfigurationChoiceResult> RecordAsync(PetConfigurationChoiceResult result, PetConfigurationChoice choice, CancellationToken ct)
    {
        try
        {
            await journal.WriteReceiptAsync(result.JournalId, new(choice, result.Outcome, clock.GetUtcNow(),
                result.ObservedCloud?.ConfigurationVersion, result.FailureCode), ct);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PetConfigurationChoiceException)
        { return result with { ReceiptSaved = false }; }
    }
}
