using System.Security.Cryptography;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class B1InputTests
{
    internal const string ValidAppearance = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#FFFFFF\",\"eyeColorHex\":\"#123abc\",\"eyeSizePercent\":100,\"eyeOvalityPercent\":0,\"eyeHeightControlPercent\":51,\"eyeSpacingControlPercent\":24.5}";

    [TestMethod]
    public void AppearanceAcceptsSchemaThreeAndEveryInclusiveBoundary()
    {
        Assert.AreEqual(ValidAppearance, AccountInput.Appearance(Parse(ValidAppearance)));
        foreach (var (field, low, high) in new[]
        {
            ("eyeSizePercent", "70", "140"), ("eyeOvalityPercent", "-100", "100"),
            ("eyeHeightControlPercent", "32", "66"), ("eyeSpacingControlPercent", "12", "34")
        })
        {
            AccountInput.Appearance(Replace(field, low));
            AccountInput.Appearance(Replace(field, high));
        }
    }

    [TestMethod]
    public void AppearanceRejectsEveryOutOfRangeNonNumericAndNonFiniteValue()
    {
        foreach (var (field, low, high) in new[]
        {
            ("eyeSizePercent", "69.999", "140.001"), ("eyeOvalityPercent", "-100.001", "100.001"),
            ("eyeHeightControlPercent", "31.999", "66.001"), ("eyeSpacingControlPercent", "11.999", "34.001")
        })
            foreach (var value in new[] { low, high, "null", "\"100\"", "true", "1e400", "-1e400" })
                Fault("appearance_invalid", () => AccountInput.Appearance(Replace(field, value)));
    }

    [TestMethod]
    public void AppearanceRejectsUnknownMissingDuplicateAndOversizedFields()
    {
        foreach (var invalid in new[]
        {
            "[]", "null", "{}", ValidAppearance.Replace("\"eyeOvalityPercent\":0,", "", StringComparison.Ordinal),
            ValidAppearance[..^1] + ",\"unknown\":0}",
            ValidAppearance.Replace("\"eyeSizePercent\":100", "\"eyeSizePercent\":100,\"eyeSizePercent\":100", StringComparison.Ordinal),
            ValidAppearance.Replace("\"eyeSizePercent\":100", "\"EyeSizePercent\":100", StringComparison.Ordinal),
            ValidAppearance[..^1] + new string(' ', 4096) + "}"
        }) Fault("appearance_invalid", () => AccountInput.Appearance(Parse(invalid)));
    }

    [TestMethod]
    public void AppearanceRejectsUnsupportedAndMalformedSchemaWithStableErrors()
    {
        foreach (var value in new[] { "2", "4", "3.1", "2147483648", "null", "\"3\"", "true", "[]", "{}" })
            Fault("appearance_schema_unsupported", () => AccountInput.Appearance(Replace("appearanceSchemaVersion", value)));
    }

    [TestMethod]
    public void AppearanceAllowsOnlyOpaqueRgbColors()
    {
        foreach (var field in new[] { "bodyColorHex", "eyeColorHex" })
            foreach (var value in new[] { "\"#FFF\"", "\"#FFFFFFFF\"", "\"FFFFFF\"", "\"#GGGGGG\"", "\"#123456\\n\"", "null", "123456" })
                Fault("appearance_invalid", () => AccountInput.Appearance(Replace(field, value)));
    }

    [TestMethod]
    public void EmailNormalizationIsStableAndDoesNotSilentlyRepairMalformedInput()
    {
        var parsed = AccountInput.Email("Fictional+test@example.invalid");
        Assert.AreEqual("Fictional+test@example.invalid", parsed.Email);
        Assert.AreEqual("FICTIONAL+TEST@EXAMPLE.INVALID", parsed.Normalized);
        foreach (var value in new string?[]
        {
            null, "", "a", "fictional", " fictional@example.invalid", "fictional@example.invalid ",
            "Fictional <fictional@example.invalid>", "fictional\r\n@example.invalid", "fictional\u0000@example.invalid",
            "团子@example.invalid", "fictional@例.invalid", new string('x', 255) + "@example.invalid"
        }) Fault("email_invalid", () => AccountInput.Email(value));
    }

    [TestMethod]
    public void PasswordLengthIsInclusiveWithoutCharacterClassRequirements()
    {
        AccountInput.Password(new string('a', 12));
        AccountInput.Password(new string('团', 128));
        foreach (var value in new string?[] { null, "", new string('a', 11), new string('a', 129) })
            Fault("password_invalid", () => AccountInput.Password(value));
    }

    [TestMethod]
    public void ResumeProofRequiresCanonicalRandom256BitEncodingAndStoresOnlyDigest()
    {
        var secret = NewProof();
        var digest = AccountInput.Digest(secret);
        Assert.AreEqual(64, digest.Length);
        Assert.IsFalse(string.Equals(secret, digest, StringComparison.Ordinal));
        Assert.IsTrue(AccountInput.Matches(digest, secret));
        Assert.IsFalse(AccountInput.Matches(digest, NewProof()));
        Assert.IsFalse(AccountInput.Matches("malformed", secret));
        foreach (var value in new string?[] { null, "", "abc", secret + "=", secret[..^1], new string('!', 43), new string('A', 42) + "B" })
        {
            Fault("proof_invalid", () => AccountInput.Digest(value));
            Assert.IsFalse(AccountInput.Matches(digest, value));
        }
    }

    [TestMethod]
    public void ChallengeProofCannotChangeSubjectPurposeGenerationEpochEmailStampOrExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var epoch = Guid.NewGuid();
        var challenge = new EmailChallenge
        {
            Id = Guid.NewGuid(), Purpose = EmailChallengePurpose.PasswordReset, UserId = Guid.NewGuid(),
            NormalizedEmail = "FICTIONAL@EXAMPLE.INVALID", SecurityStamp = Guid.NewGuid().ToString("N"),
            RecoveryEpoch = epoch, Generation = 3, CreatedAt = now, ExpiresAt = now.AddMinutes(30)
        };
        var proof = new EmailProof(challenge.Id, challenge.Purpose, challenge.UserId!.Value, challenge.NormalizedEmail,
            epoch, challenge.Generation, challenge.ExpiresAt, challenge.SecurityStamp);
        Assert.IsTrue(ChallengeIssuer.Matches(challenge, proof, epoch, now));
        foreach (var changed in new[]
        {
            proof with { ChallengeId = Guid.NewGuid() }, proof with { Purpose = EmailChallengePurpose.Registration },
            proof with { SubjectId = Guid.NewGuid() }, proof with { NormalizedEmail = "OTHER@EXAMPLE.INVALID" },
            proof with { RecoveryEpoch = Guid.NewGuid() }, proof with { Generation = 2 },
            proof with { ExpiresAt = challenge.ExpiresAt.AddSeconds(1) }, proof with { SecurityStamp = "changed" }
        }) Assert.IsFalse(ChallengeIssuer.Matches(challenge, changed, epoch, now));
        Assert.IsFalse(ChallengeIssuer.Matches(challenge, proof, Guid.NewGuid(), now));
        Assert.IsFalse(ChallengeIssuer.Matches(challenge, proof, epoch, challenge.ExpiresAt));
        challenge.ConsumedAt = now;
        Assert.IsFalse(ChallengeIssuer.Matches(challenge, proof, epoch, now));
        challenge.ConsumedAt = null;
        challenge.RevokedAt = now;
        Assert.IsFalse(ChallengeIssuer.Matches(challenge, proof, epoch, now));
    }

    internal static string NewProof() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    internal static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

    private static JsonElement Replace(string field, string value)
    {
        using var document = JsonDocument.Parse(ValidAppearance);
        var old = document.RootElement.GetProperty(field).GetRawText();
        return Parse(ValidAppearance.Replace($"\"{field}\":{old}", $"\"{field}\":{value}", StringComparison.Ordinal));
    }

    internal static void Fault(string code, Action action)
    {
        var exception = Assert.ThrowsExactly<AccountFault>(action);
        Assert.AreEqual(code, exception.Code);
    }
}
