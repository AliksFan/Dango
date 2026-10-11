using System.Security.Cryptography;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1cActorEvidence(string Actor, Guid UserId, Guid PetId, Guid DefaultRoomId,
    string? Nickname, long ProfileVersion, long ConfigurationVersion, JsonElement Configuration,
    string[] SessionIds, int Sessions, int Revoked, int Attempts, string MailState, Guid RecoveryEpoch);
internal sealed record AccountSyncB1cRequest(int Sequence, int ClientId, string Role, string? Actor,
    string Method, string Path, Guid? UserId, Guid? PetId, Guid? SessionId, int? PetReadOrdinal,
    long? ExpectedVersion, string? ConfigurationSha256, bool? PreviousPumpSettled, bool Retired,
    int? ResponseStatus, long? ResponseVersion);
internal sealed record AccountSyncB1cPhase(int Sequence, string Phase, int? ClientId, int? RequestSequence);
internal sealed record AccountSyncB1cCase(string Case, bool Passed, string HeldMethod, long HeldResponseVersion,
    long NewBaselineVersion, bool CancellationObserved, bool ReleaseObserved, bool HeldResponseSettled,
    bool OldPumpSettledBeforeNewBaseline, int OldMainRequestsAfterStop, bool CancelledLogoutIntent, bool AOriginalHasReceipt);
internal sealed record AccountSyncB1cArchiveEvidence(string Actor, string Case, string Step, Guid JournalId,
    string Origin, Guid UserId, Guid PetId, string LocalOriginalSha256, string LocalConfigurationSha256,
    string CloudConfigurationSha256, long ConfigurationVersion, int Choice, bool ReceiptPresent,
    int? Outcome, long? ObservedCloudVersion, string? FailureCode);

internal static class AccountSyncB1cEvidenceContract
{
    internal const string NativeRelativePath = "artifacts/main-absorption/9db6fd423e364e488967e0696ecc05d1/native/Release/dango_physics_module.dll";
    internal const string NativeSha256 = "B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E";
    internal static JsonElement Sample(string actor, double size = 103) => AppearanceContract.ValidateAndClone(JsonSerializer.SerializeToElement(new
    {
        appearanceSchemaVersion = 3, bodyColorHex = actor switch { "a" => "#FFE8C2", "b" => "#9ECBEF", _ => throw Reject() },
        eyeColorHex = "#272321", eyeSizePercent = size, eyeOvalityPercent = 0d,
        eyeHeightControlPercent = 51d, eyeSpacingControlPercent = 24.5d
    }));
    internal static string Nickname(string actor) => actor switch { "a" => "旧账号团子", "b" => "新账号团子", _ => throw Reject() };
    internal static string ConfigurationSha256(JsonElement configuration) => AccountSyncB1bEvidenceContract.ConfigurationSha256(configuration);
    internal static string BytesSha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static void VerifyActors(IReadOnlyList<AccountSyncB1cActorEvidence> actors, Guid epoch)
    {
        if (actors.Count != 2 || epoch == Guid.Empty || actors[0].Actor != "a" || actors[1].Actor != "b") throw Reject();
        foreach (var actor in actors)
        {
            var a = actor.Actor == "a";
            if (actor.UserId == Guid.Empty || actor.PetId == Guid.Empty || actor.DefaultRoomId == Guid.Empty
                || actor.Nickname != Nickname(actor.Actor) || actor.ProfileVersion != 2 || actor.ConfigurationVersion != (a ? 2 : 3)
                || !AppearanceContract.Equivalent(actor.Configuration, Sample(actor.Actor, a ? 111 : 130))
                || actor.RecoveryEpoch != epoch || actor.Sessions != 2 || actor.Revoked != 2 || actor.Attempts != 0 || actor.MailState != "Pending"
                || actor.SessionIds.Length != 2 || !actor.SessionIds.SequenceEqual(actor.SessionIds.Order(StringComparer.Ordinal))
                || actor.SessionIds.Any(id => !Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != id)) throw Reject();
        }
        if (actors.Select(a => a.UserId).Distinct().Count() != 2 || actors.Select(a => a.PetId).Distinct().Count() != 2
            || actors.Select(a => a.DefaultRoomId).Distinct().Count() != 2 || actors.SelectMany(a => a.SessionIds).Distinct(StringComparer.Ordinal).Count() != 4) throw Reject();
    }
    internal static void VerifyArchives(IReadOnlyList<AccountSyncB1cArchiveEvidence> archives,
        IReadOnlyList<AccountSyncB1cActorEvidence> actors, Uri origin)
    {
        string[] steps = ["late-get-choice", "late-get-save", "late-patch-unacknowledged", "late-patch-choice", "late-patch-save"];
        if (!origin.IsAbsoluteUri || origin.Scheme != "https" || origin.Host != "127.0.0.1" || origin.Port <= 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0
            || actors.Count != 2 || actors[0].Actor != "a" || actors[1].Actor != "b"
            || archives.Count != 5 || !archives.Select(row => row.Step).SequenceEqual(steps, StringComparer.Ordinal)
            || archives.Count(row => row.ReceiptPresent) != 4 || archives.Select(row => row.JournalId).Distinct().Count() != 5) throw Reject();
        for (var index = 0; index < archives.Count; index++)
        {
            var row = archives[index]; var unacknowledged = index == 2; var choice = index is 0 or 3;
            var actor = actors[unacknowledged ? 0 : 1];
            var local = Sample(index is 0 or 2 or 3 ? "a" : "b", index switch { 0 or 3 => 118, 1 => 125, 2 => 111, _ => 130 });
            var cloud = Sample(unacknowledged ? "a" : "b", index is 3 or 4 ? 125 : 103);
            if (row.Actor != (unacknowledged ? "a" : "b") || row.Case != (index < 2 ? "late-get" : "late-patch")
                || row.JournalId == Guid.Empty || row.UserId == Guid.Empty || row.PetId == Guid.Empty
                || row.UserId != actor.UserId || row.PetId != actor.PetId || row.Origin != origin.AbsoluteUri
                || row.LocalConfigurationSha256 != ConfigurationSha256(local) || row.CloudConfigurationSha256 != ConfigurationSha256(cloud)
                || row.ConfigurationVersion != (index == 4 || index == 3 ? 2 : 1)
                || row.Choice != (int)(choice ? PetConfigurationChoice.Cloud : PetConfigurationChoice.Local)
                || row.ReceiptPresent != !unacknowledged || row.FailureCode is not null
                || row.Outcome != (unacknowledged ? null : (int?)(choice ? PetChoiceOutcome.LocalApplied : PetChoiceOutcome.CloudUpdated))
                || row.ObservedCloudVersion != (unacknowledged ? null : choice ? index == 0 ? 1L : 2L : index == 1 ? 2L : 3L)
                || new[] { row.LocalOriginalSha256, row.LocalConfigurationSha256, row.CloudConfigurationSha256 }.Any(hash => hash.Length != 64
                    || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))) throw Reject();
        }
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c_evidence_rejected");
}
