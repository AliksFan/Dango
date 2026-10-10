using Dango.Client.Sync.Pets;
using Dango.Protocol;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncArchiveExpectation(string Step, Uri Origin, LocalPetConfigurationSnapshot Local,
    PetConfigurationSnapshot Cloud, PetConfigurationChoice Choice, PetChoiceOutcome Outcome, long ObservedVersion);

internal static class AccountSyncArchiveContract
{
    internal static void Verify(PetConfigurationOriginals originals, PetConfigurationReceipt receipt,
        AccountSyncArchiveExpectation expected)
    {
        if (originals.Origin != expected.Origin || !originals.Local.SameOriginal(expected.Local)
            || originals.Cloud.UserId != expected.Cloud.UserId || originals.Cloud.PetId != expected.Cloud.PetId
            || originals.Cloud.ConfigurationVersion != expected.Cloud.ConfigurationVersion
            || !AppearanceContract.Equivalent(originals.Cloud.Configuration, expected.Cloud.Configuration)
            || originals.Choice != expected.Choice || receipt.Choice != expected.Choice
            || receipt.Outcome != expected.Outcome || receipt.ObservedCloudVersion != expected.ObservedVersion
            || receipt.FailureCode is not null || receipt.RecordedAt < originals.CapturedAt)
            throw new InvalidOperationException("accountsync_archive_evidence_rejected");
    }
}
