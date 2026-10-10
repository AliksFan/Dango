using System.Text.Json;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1bEvidence")]
public sealed class AccountSyncB1bEvidenceContractTests
{
    [TestMethod]
    public void ScrambledEnumerationProducesTheExactCanonicalTwelveStepOrder()
    {
        var scrambled = Valid().Reverse().ToArray();
        var ordered = AccountSyncB1bEvidenceContract.CanonicalArchives(scrambled, item => item.Step, item => item.Device);
        CollectionAssert.AreEqual(AccountSyncB1bEvidenceContract.Steps.ToArray(), ordered.Select(item => item.Step).ToArray());
        Assert.AreEqual(12, ordered.Count);
    }

    [TestMethod]
    public void DuplicateStepCannotReplaceAMissingStepEvenWhenTheCountIsTwelve()
    {
        var records = Valid(); records[^1] = records[0];
        Assert.ThrowsExactly<InvalidOperationException>(() => Canonical(records));
    }

    [TestMethod]
    public void MissingStepIsRejected() => Assert.ThrowsExactly<InvalidOperationException>(() => Canonical(Valid().Skip(1)));

    [TestMethod]
    [DataRow("CAS-PRIMARY")]
    [DataRow("cas-primary\0")]
    [DataRow("cas-\u00ADprimary")]
    public void UnknownOrNonOrdinalStepIsRejected(string step)
    {
        var records = Valid(); records[0] = records[0] with { Step = step };
        Assert.ThrowsExactly<InvalidOperationException>(() => Canonical(records));
    }

    [TestMethod]
    public void WrongDeviceForAKnownStepIsRejected()
    {
        var records = Valid(); records[3] = records[3] with { Device = "secondary" };
        Assert.ThrowsExactly<InvalidOperationException>(() => Canonical(records));
    }

    [TestMethod]
    public void SemanticColorNumberAndFieldOrderDifferencesHaveOneConfigurationDigest()
    {
        var canonical = Configuration();
        using var other = JsonDocument.Parse("{\"eyeSpacingControlPercent\":24.50,\"eyeHeightControlPercent\":51.0,\"eyeOvalityPercent\":-0.0,\"eyeSizePercent\":100.00,\"eyeColorHex\":\"#ddeeff\",\"bodyColorHex\":\"#aabbcc\",\"appearanceSchemaVersion\":3}");
        Assert.IsTrue(AppearanceContract.Equivalent(canonical, other.RootElement));
        Assert.AreEqual(AccountSyncB1bEvidenceContract.ConfigurationSha256(canonical),
            AccountSyncB1bEvidenceContract.ConfigurationSha256(other.RootElement));
    }

    [TestMethod]
    [DataRow("bodyColorHex")]
    [DataRow("eyeColorHex")]
    [DataRow("eyeSizePercent")]
    [DataRow("eyeOvalityPercent")]
    [DataRow("eyeHeightControlPercent")]
    [DataRow("eyeSpacingControlPercent")]
    public void EveryMutableConfigurationFieldChangesTheDigest(string field)
    {
        var original = Configuration();
        var fields = original.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        fields[field] = field.EndsWith("Hex", StringComparison.Ordinal)
            ? JsonSerializer.SerializeToElement("#112233")
            : JsonSerializer.SerializeToElement(fields[field].GetDouble() + 1);
        var changed = JsonSerializer.SerializeToElement(fields);
        Assert.IsFalse(AppearanceContract.Equivalent(original, changed));
        Assert.AreNotEqual(AccountSyncB1bEvidenceContract.ConfigurationSha256(original),
            AccountSyncB1bEvidenceContract.ConfigurationSha256(changed));
    }

    [TestMethod]
    public void InvalidSchemaCannotReceiveAnAcceptedDigest()
    {
        var fields = Configuration().EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        fields["appearanceSchemaVersion"] = JsonSerializer.SerializeToElement(4);
        Assert.ThrowsExactly<AppearanceContractException>(() => AccountSyncB1bEvidenceContract.ConfigurationSha256(JsonSerializer.SerializeToElement(fields)));
    }

    private sealed record Projection(string Step, string Device);
    private static Projection[] Valid() => AccountSyncB1bEvidenceContract.Steps.Select(step => new Projection(step, step switch
    {
        "cas-secondary" or "cas-loser-follow" or "lost-accepted-secondary-follow" or "newer-secondary-save"
            or "retried-secondary-follow" or "host-recovery-secondary-follow" => "secondary",
        _ => "primary"
    })).ToArray();
    private static void Canonical(IEnumerable<Projection> records) =>
        _ = AccountSyncB1bEvidenceContract.CanonicalArchives(records, item => item.Step, item => item.Device);
    private static JsonElement Configuration() => JsonSerializer.SerializeToElement(new
    {
        appearanceSchemaVersion = 3, bodyColorHex = "#AABBCC", eyeColorHex = "#DDEEFF", eyeSizePercent = 100d,
        eyeOvalityPercent = 0d, eyeHeightControlPercent = 51d, eyeSpacingControlPercent = 24.5d
    });
}
