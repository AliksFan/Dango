using System.Security.Cryptography;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
public sealed class PublicRoomPolicyTests
{
    [TestMethod]
    public void CodesUse32RandomBytesAndOnlyCanonicalBase64Url()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => PublicRoomAccessPolicy.NewCode()).ToArray();
        Assert.AreEqual(100, codes.Distinct(StringComparer.Ordinal).Count());
        foreach (var code in codes)
        {
            Assert.AreEqual(43, code.Length);
            Assert.IsTrue(code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
            var bytes = WebEncoders.Base64UrlDecode(code);
            Assert.AreEqual(32, bytes.Length);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), PublicRoomAccessPolicy.CodeDigest(code));
            Assert.AreEqual(64, PublicRoomAccessPolicy.CodeDigest(code).Length);
        }
    }

    [TestMethod]
    public void CodesRejectPaddingWhitespaceNoncanonicalTrailingBitsAndOversizedInputs()
    {
        var valid = PublicRoomAccessPolicy.NewCode();
        string?[] invalid = [null, "", valid + "=", " " + valid, valid + " ", new string('A', 42), new string('A', 42) + "B",
            new string('!', 43), new string('中', 43), new string('A', 4096)];
        foreach (var code in invalid)
        {
            var fault = Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.CodeDigest(code));
            Assert.AreEqual("room_code_invalid", fault.Code); Assert.AreEqual(404, fault.Status);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(169)]
    [DataRow(int.MaxValue)]
    public void LifetimesOutsideApprovedBoundsAreRejected(int hours) =>
        Assert.AreEqual("room_code_lifetime_invalid", Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.Lifetime(hours)).Code);

    [TestMethod]
    public void DefaultAndBoundaryLifetimesAreAccepted()
    {
        Assert.AreEqual(24, new RoomCodeResetRequest(Guid.NewGuid(), 1).LifetimeHours);
        PublicRoomAccessPolicy.Lifetime(1); PublicRoomAccessPolicy.Lifetime(24); PublicRoomAccessPolicy.Lifetime(168);
    }

    [TestMethod]
    public void ResolveRequiresCurrentEpochGenerationActiveRoomAndUnexpiredCredential()
    {
        var now = DateTimeOffset.UtcNow;
        var epoch = Guid.NewGuid();
        var room = new PersistentRoom
        {
            Id = Guid.NewGuid(),
            Kind = PersistentRoomKind.PublicShared,
            Status = PersistentRoomStatus.Active,
            AdmissionGeneration = 3
        };
        var code = new PublicRoomCredential
        {
            Id = Guid.NewGuid(),
            RoomId = room.Id,
            Generation = 3,
            RecoveryEpoch = epoch,
            CreatedAt = now.AddHours(-1),
            ExpiresAt = now.AddTicks(1)
        };
        Assert.IsTrue(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, now));
        Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, code.ExpiresAt));
        Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, Guid.NewGuid(), now));
        Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, Guid.Empty, now));
        code.RevokedAt = now; Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, now)); code.RevokedAt = null;
        code.Generation--; Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, now)); code.Generation++;
        code.CreatedAt = now.AddTicks(1); Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, now)); code.CreatedAt = now;
        room.Status = PersistentRoomStatus.Dissolved; Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, now));
        room.Status = PersistentRoomStatus.Active; room.Kind = PersistentRoomKind.PrivateShared;
        Assert.IsFalse(PublicRoomAccessPolicy.CredentialValid(room, code, epoch, now));
    }

    [TestMethod]
    public void ReceiptSchemaCarriesNoCodeDigestOrCipherAndStrictlyRejectsSecretFields()
    {
        var secret = PublicRoomAccessPolicy.NewCode();
        var credential = new PublicRoomCredential
        {
            Id = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            Generation = 2,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            CodeDigest = PublicRoomAccessPolicy.CodeDigest(secret),
            ProtectedCode = "fictional-cipher"
        };
        var json = RoomAccessPersistence.Serialize(PublicRoomAccessPolicy.Receipt(credential, 8));
        using var parsed = JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(new[] { "roomId", "credentialId", "roomVersion", "generation", "expiresAt" },
            parsed.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.IsFalse(json.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(credential.CodeDigest, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(credential.ProtectedCode, StringComparison.Ordinal));
        Assert.AreEqual("room_receipt_invalid", Assert.ThrowsExactly<AccountFault>(() =>
            RoomAccessPersistence.Deserialize<PublicRoomCodeReceipt>(json[..^1] + ",\"code\":\"secret\"}")).Code);
    }

    [TestMethod]
    public void ReceiptMetadataMustMatchBothOriginalAuditAndCredential()
    {
        var credential = new PublicRoomCredential
        {
            Id = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            Generation = 3,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };
        var receipt = PublicRoomAccessPolicy.Receipt(credential, 4);
        var audit = new RoomOperationAudit { RoomId = credential.RoomId, AppliedVersion = 4, Operation = "code_reset" };
        PublicRoomAccessPolicy.ValidateReceipt(receipt, audit, credential);
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.ValidateReceipt(receipt with { CredentialId = Guid.NewGuid() }, audit, credential));
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.ValidateReceipt(receipt with { RoomVersion = 5 }, audit, credential));
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.ValidateReceipt(receipt with { Generation = 4 }, audit, credential));
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.ValidateReceipt(receipt with { ExpiresAt = receipt.ExpiresAt.AddSeconds(1) }, audit, credential));
        audit.Operation = "ban_set";
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.ValidateReceipt(receipt, audit, credential));
    }

    [TestMethod]
    public void ModerationConfirmationContainsNoTargetAssignmentOrControlFacts()
    {
        var json = RoomAccessPersistence.Serialize(new PublicRoomModerationResult(Guid.NewGuid(), Guid.NewGuid(), 9, "ban_set"));
        using var parsed = JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(new[] { "roomId", "targetUserId", "roomVersion", "operation" },
            parsed.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.Target(Guid.Empty, Guid.Empty));
        var owner = Guid.NewGuid();
        Assert.ThrowsExactly<AccountFault>(() => PublicRoomAccessPolicy.Target(owner, owner));
    }
}
