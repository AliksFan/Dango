using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Dango.Server.Email;

// Persistent key-ring configuration belongs to the formal host. This component never writes keys or tokens.
public sealed class EmailProofProtector(IDataProtectionProvider provider, TimeProvider clock) : IEmailProofProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly ITimeLimitedDataProtector registration = provider.CreateProtector("Dango.EmailChallenge", "v1", "registration").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector passwordReset = provider.CreateProtector("Dango.EmailChallenge", "v1", "password-reset").ToTimeLimitedDataProtector();

    public string Protect(EmailChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var subject = challenge.PendingRegistrationId ?? challenge.UserId ?? Guid.Empty;
        var proof = new EmailProof(challenge.Id, challenge.Purpose, subject, challenge.NormalizedEmail,
            challenge.RecoveryEpoch, challenge.Generation, challenge.ExpiresAt, challenge.SecurityStamp);
        if (!ValidProof(proof, clock.GetUtcNow()) || challenge.CreatedAt > clock.GetUtcNow()
            || challenge.ExpiresAt > challenge.CreatedAt.AddMinutes(30)
            || challenge.ConsumedAt is not null || challenge.RevokedAt is not null
            || (challenge.Purpose == EmailChallengePurpose.Registration
                ? challenge.PendingRegistrationId is null || challenge.UserId is not null
                : challenge.UserId is null || challenge.PendingRegistrationId is not null))
            throw new ArgumentException("email_challenge_invalid", nameof(challenge));
        var protector = proof.Purpose == EmailChallengePurpose.Registration ? registration : passwordReset;
        var prefix = proof.Purpose == EmailChallengePurpose.Registration ? "r." : "p.";
        return prefix + WebEncoders.Base64UrlEncode(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(proof), challenge.ExpiresAt));
    }

    public bool TryUnprotect(string? value, out EmailProof? proof)
    {
        proof = null;
        if (string.IsNullOrEmpty(value) || value.Length is < 3 or > 4096) return false;
        var purpose = value.StartsWith("r.", StringComparison.Ordinal) ? EmailChallengePurpose.Registration
            : value.StartsWith("p.", StringComparison.Ordinal) ? EmailChallengePurpose.PasswordReset : (EmailChallengePurpose)(-1);
        if (purpose == (EmailChallengePurpose)(-1)) return false;
        try
        {
            var protector = purpose == EmailChallengePurpose.Registration ? registration : passwordReset;
            var plaintext = protector.Unprotect(WebEncoders.Base64UrlDecode(value[2..]), out var absoluteExpiration);
            var candidate = JsonSerializer.Deserialize<EmailProof>(plaintext, JsonOptions);
            if (candidate is null || candidate.Purpose != purpose || candidate.ExpiresAt != absoluteExpiration
                || !ValidProof(candidate, clock.GetUtcNow())) return false;
            proof = candidate;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        { return false; }
    }

    private static bool ValidProof(EmailProof proof, DateTimeOffset now)
    {
        if (proof.ChallengeId == Guid.Empty || proof.SubjectId == Guid.Empty || proof.RecoveryEpoch == Guid.Empty
            || proof.Generation < 1 || proof.ExpiresAt <= now) return false;
        try { if (AccountInput.Email(proof.NormalizedEmail).Normalized != proof.NormalizedEmail) return false; }
        catch (AccountFault) { return false; }
        return proof.Purpose switch
        {
            EmailChallengePurpose.Registration => proof.SecurityStamp is null,
            EmailChallengePurpose.PasswordReset => proof.SecurityStamp is { Length: > 0 and <= 256 },
            _ => false
        };
    }
}
