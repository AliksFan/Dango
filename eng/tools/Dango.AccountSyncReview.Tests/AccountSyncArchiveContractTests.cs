using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncBoundary")]
public sealed class AccountSyncArchiveContractTests
{
    private static readonly Uri Origin = new("https://127.0.0.1:62123/");
    private static JsonElement Configuration(double size) => JsonSerializer.SerializeToElement(new
    {
        appearanceSchemaVersion = 3, bodyColorHex = "#FFFFFF", eyeColorHex = "#000000",
        eyeSizePercent = size, eyeOvalityPercent = 0d, eyeHeightControlPercent = 50d, eyeSpacingControlPercent = 24d
    });
    private static AccountSyncArchiveExpectation Expected() => new("upload", Origin,
        new(true, new byte[] { 1, 2, 3 }, Configuration(111)),
        new(Guid.Parse("d0a3b34c-b40c-47e5-bd19-3b403186ef33"), Guid.Parse("4500292c-722d-41e1-87be-7bf386b747b5"), 1, Configuration(103)),
        PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 2);

    [TestMethod]
    public void ExactOriginalBytesConfigurationIdentityAndReceiptAreAccepted()
    {
        var e = Expected(); var at = DateTimeOffset.UtcNow;
        AccountSyncArchiveContract.Verify(new(e.Origin, e.Local, e.Cloud, at, e.Choice),
            new(e.Choice, e.Outcome, at.AddSeconds(1), e.ObservedVersion, null), e);
    }

    [TestMethod]
    [DataRow("bytes")]
    [DataRow("exists")]
    [DataRow("localConfiguration")]
    [DataRow("cloudConfiguration")]
    [DataRow("origin")]
    [DataRow("user")]
    [DataRow("pet")]
    [DataRow("version")]
    [DataRow("originalChoice")]
    [DataRow("receiptChoice")]
    [DataRow("outcome")]
    [DataRow("observedVersion")]
    [DataRow("failure")]
    [DataRow("time")]
    public void ReadableButIncorrectArchiveIsRejected(string mutation)
    {
        var e = Expected(); var at = DateTimeOffset.UtcNow;
        var originals = new PetConfigurationOriginals(e.Origin, e.Local, e.Cloud, at, e.Choice);
        var receipt = new PetConfigurationReceipt(e.Choice, e.Outcome, at.AddSeconds(1), e.ObservedVersion, null);
        originals = mutation switch
        {
            "bytes" => originals with { Local = new(true, new byte[] { 1, 2, 4 }, Configuration(111)) },
            "exists" => originals with { Local = new(false, Array.Empty<byte>(), Configuration(111)) },
            "localConfiguration" => originals with { Local = new(true, new byte[] { 1, 2, 3 }, Configuration(112)) },
            "cloudConfiguration" => originals with { Cloud = e.Cloud with { Configuration = Configuration(104) } },
            "origin" => originals with { Origin = new("https://127.0.0.1:62124/") },
            "user" => originals with { Cloud = e.Cloud with { UserId = Guid.NewGuid() } },
            "pet" => originals with { Cloud = e.Cloud with { PetId = Guid.NewGuid() } },
            "version" => originals with { Cloud = e.Cloud with { ConfigurationVersion = 2 } },
            "originalChoice" => originals with { Choice = PetConfigurationChoice.Cloud },
            _ => originals
        };
        receipt = mutation switch
        {
            "receiptChoice" => receipt with { Choice = PetConfigurationChoice.Cloud },
            "outcome" => receipt with { Outcome = PetChoiceOutcome.TargetSatisfied },
            "observedVersion" => receipt with { ObservedCloudVersion = 3 },
            "failure" => receipt with { FailureCode = "remote_request_rejected" },
            "time" => receipt with { RecordedAt = at.AddSeconds(-1) },
            _ => receipt
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => AccountSyncArchiveContract.Verify(originals, receipt, e));
    }
}
