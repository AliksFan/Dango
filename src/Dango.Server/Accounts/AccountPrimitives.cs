using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Dango.Protocol;

namespace Dango.Server.Accounts;

public sealed record AccountEnvironment(Guid RecoveryEpoch);
public sealed class AccountFault(string code, int status = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public static partial class AccountInput
{
    public static (string Email, string Normalized) Email(string? value)
    {
        if (value is null || value.Length > 254 || value.Length < 3 || value != value.Trim()
            || value.Any(c => c > 127 || char.IsControl(c))) throw new AccountFault("email_invalid");
        if (!MailAddress.TryCreate(value, out var parsed) || parsed.Address != value || parsed.DisplayName.Length != 0
            || !value.Contains('@') || parsed.Host.Length == 0) throw new AccountFault("email_invalid");
        return (value, value.ToUpperInvariant());
    }

    public static void Password(string? password)
    {
        if (password is null || password.Length is < 12 or > 128) throw new AccountFault("password_invalid");
    }

    public static string Digest(string? secret)
    {
        if (secret is null || secret.Length != 43) throw new AccountFault("proof_invalid");
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(secret);
            if (bytes.Length != 32 || WebEncoders.Base64UrlEncode(bytes) != secret) throw new AccountFault("proof_invalid");
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        catch (FormatException) { throw new AccountFault("proof_invalid"); }
    }

    public static bool Matches(string digest, string? secret)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(digest), Convert.FromHexString(Digest(secret))); }
        catch (Exception ex) when (ex is FormatException or AccountFault) { return false; }
    }

    public static string Appearance(JsonElement input)
    {
        try { return AppearanceContract.ValidateAndClone(input).GetRawText(); }
        catch (AppearanceContractException fault) { throw new AccountFault(fault.Code); }
    }
}

// All account writes acquire email -> subject -> invitation/challenge in that order.
// The advisory key serializes distinct requests for one normalized email without exposing it.
public static class AccountLocks
{
    public static Task EmailAsync(DangoDbContext db, string email, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))", ct);

    public static async Task<DangoUser?> UserAsync(DangoDbContext db, Guid id, CancellationToken ct) =>
        await db.Users.FromSqlInterpolated($"SELECT * FROM dango.users WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    public static async Task<PendingRegistration?> RegistrationAsync(DangoDbContext db, Guid id, CancellationToken ct) =>
        await db.PendingRegistrations.FromSqlInterpolated($"SELECT * FROM dango.pending_registrations WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    public static async Task<TestInvitation?> InvitationAsync(DangoDbContext db, Guid id, CancellationToken ct) =>
        await db.TestInvitations.FromSqlInterpolated($"SELECT * FROM dango.test_invitations WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
}

public sealed record EmailProof(Guid ChallengeId, EmailChallengePurpose Purpose, Guid SubjectId, string NormalizedEmail,
    Guid RecoveryEpoch, int Generation, DateTimeOffset ExpiresAt, string? SecurityStamp);

public interface IEmailProofProtector
{
    string Protect(EmailChallenge challenge);
    bool TryUnprotect(string? value, out EmailProof? proof);
}

public interface IRegistrationCheckpoint
{
    Task ReachedAsync(string name, CancellationToken ct);
}

public sealed class RegistrationCheckpoint : IRegistrationCheckpoint
{
    public Task ReachedAsync(string name, CancellationToken ct) => Task.CompletedTask;
}

// Called only while the email and subject are locked in an existing transaction.
public sealed class ChallengeIssuer(DangoDbContext db, AccountEnvironment environment, TimeProvider clock)
{
    public async Task<EmailChallenge> AddAsync(EmailChallengePurpose purpose, string normalizedEmail,
        Guid? pendingId, Guid? userId, string? stamp, int generation, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var recent = await db.EmailChallenges.Where(c => c.NormalizedEmail == normalizedEmail && c.CreatedAt > now.AddHours(-1))
            .OrderByDescending(c => c.CreatedAt).Take(5).ToListAsync(ct);
        if (recent.Count >= 5 || recent.Any(c => c.CreatedAt > now.AddSeconds(-60))) throw new AccountFault("email_rate_limited", 429);
        await db.EmailChallenges.Where(c => c.Purpose == purpose && c.PendingRegistrationId == pendingId && c.UserId == userId
            && c.RevokedAt == null && c.ConsumedAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now), ct);
        var challenge = new EmailChallenge
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            NormalizedEmail = normalizedEmail,
            PendingRegistrationId = pendingId,
            UserId = userId,
            SecurityStamp = stamp,
            RecoveryEpoch = environment.RecoveryEpoch,
            Generation = generation,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
        db.EmailChallenges.Add(challenge);
        db.EmailJobs.Add(new EmailJob { Id = Guid.NewGuid(), ChallengeId = challenge.Id, CreatedAt = now, DueAt = now });
        return challenge;
    }

    public static bool Matches(EmailChallenge challenge, EmailProof proof, Guid epoch, DateTimeOffset now) =>
        challenge.Id == proof.ChallengeId && challenge.Purpose == proof.Purpose
        && (challenge.PendingRegistrationId ?? challenge.UserId) == proof.SubjectId
        && challenge.NormalizedEmail == proof.NormalizedEmail && challenge.RecoveryEpoch == epoch && proof.RecoveryEpoch == epoch
        && challenge.Generation == proof.Generation && challenge.ExpiresAt == proof.ExpiresAt
        && challenge.SecurityStamp == proof.SecurityStamp && challenge.ExpiresAt > now
        && challenge.ConsumedAt is null && challenge.RevokedAt is null;
}
