using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Accounts;

public static class AccountCredentialPolicy
{
    public static bool CanReplacePassword(DangoUser user) =>
        user.EmailConfirmed && user.DisabledAt is null && !user.RecoveryLocked && !string.IsNullOrEmpty(user.SecurityStamp);

    public static bool MatchesReset(DangoUser user, EmailChallenge challenge, EmailProof proof,
        Guid recoveryEpoch, DateTimeOffset now) =>
        CanReplacePassword(user) && proof.Purpose == EmailChallengePurpose.PasswordReset
        && challenge.Purpose == EmailChallengePurpose.PasswordReset && challenge.PendingRegistrationId is null
        && challenge.UserId == user.Id && proof.SubjectId == user.Id && user.NormalizedEmail == proof.NormalizedEmail
        && user.SecurityStamp == proof.SecurityStamp && ChallengeIssuer.Matches(challenge, proof, recoveryEpoch, now);
}

public sealed class CredentialService(DangoDbContext db, UserManager<DangoUser> users,
    AccountEnvironment environment, TimeProvider clock, IEmailProofProtector proofs, ChallengeIssuer issuer)
{
    public async Task RequestPasswordResetAsync(string email, CancellationToken ct)
    {
        (string Email, string Normalized) input;
        try { input = AccountInput.Email(email); }
        catch (AccountFault) { return; }
        var normalized = users.NormalizeEmail(input.Email);
        if (normalized is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, normalized, ct);
        var id = await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
        if (id is null) return;
        var user = await AccountLocks.UserAsync(db, id.Value, ct);
        if (user is null) return;
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalized || !AccountCredentialPolicy.CanReplacePassword(user)) return;
        var generation = await db.EmailChallenges.Where(c => c.UserId == user.Id && c.Purpose == EmailChallengePurpose.PasswordReset)
            .OrderByDescending(c => c.Generation).Select(c => c.Generation).FirstOrDefaultAsync(ct);
        if (generation == int.MaxValue) return;
        try
        {
            await issuer.AddAsync(EmailChallengePurpose.PasswordReset, normalized, null, user.Id, user.SecurityStamp,
                generation + 1, clock.GetUtcNow().AddMinutes(30), ct);
        }
        catch (AccountFault fault) when (fault.Code == "email_rate_limited") { return; }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task CompletePasswordResetAsync(string proof, string password, CancellationToken ct)
    {
        if (!proofs.TryUnprotect(proof, out var data) || data is null || data.Purpose != EmailChallengePurpose.PasswordReset
            || data.RecoveryEpoch != environment.RecoveryEpoch) throw ResetProofInvalid();
        AccountInput.Password(password);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, data.NormalizedEmail, ct);
        var user = await AccountLocks.UserAsync(db, data.SubjectId, ct);
        if (user is null) throw ResetProofInvalid();
        await db.Entry(user).ReloadAsync(ct);
        var challenge = await db.EmailChallenges.FromSqlInterpolated(
            $"SELECT * FROM dango.email_challenges WHERE id = {data.ChallengeId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (challenge is null) throw ResetProofInvalid();
        await db.Entry(challenge).ReloadAsync(ct);
        var now = clock.GetUtcNow();
        if (!AccountCredentialPolicy.MatchesReset(user, challenge, data, environment.RecoveryEpoch, now)) throw ResetProofInvalid();
        var lockout = LockoutState.Read(user);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded) throw new AccountFault("password_invalid");
        now = clock.GetUtcNow();
        if (challenge.ExpiresAt <= now) throw ResetProofInvalid();
        // Ordinary reset replaces credentials; it cannot clear a login lock or maintenance state.
        lockout.Restore(user);
        challenge.ConsumedAt = now;
        await RevokeCredentialsAsync(user.Id, now, challenge.Id, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        AccountInput.Password(newPassword);
        if (currentPassword is null || currentPassword.Length > 128) throw new AccountFault("current_password_invalid");
        var normalized = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct)
            ?? throw new AccountFault("account_unavailable", 403);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, normalized, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct);
        if (user is null) throw new AccountFault("account_unavailable", 403);
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalized || !AccountCredentialPolicy.CanReplacePassword(user))
            throw new AccountFault("account_unavailable", 403);
        var lockout = LockoutState.Read(user);
        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            throw new AccountFault(result.Errors.Any(e => e.Code == "PasswordMismatch") ? "current_password_invalid" : "password_invalid", 400);
        lockout.Restore(user);
        await RevokeCredentialsAsync(user.Id, clock.GetUtcNow(), null, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task RevokeCredentialsAsync(Guid userId, DateTimeOffset now, Guid? consumedChallengeId, CancellationToken ct)
    {
        await db.AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), ct);
        await db.EmailChallenges.Where(c => c.UserId == userId && c.Id != consumedChallengeId
                && c.RevokedAt == null && c.ConsumedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.RevokedAt, now), ct);
    }

    private static AccountFault ResetProofInvalid() => new("password_reset_proof_invalid", 403);

    private sealed record LockoutState(DateTimeOffset? End, bool Enabled, int Failed,
        DateTimeOffset? DisabledAt, bool RecoveryLocked)
    {
        public static LockoutState Read(DangoUser user) =>
            new(user.LockoutEnd, user.LockoutEnabled, user.AccessFailedCount, user.DisabledAt, user.RecoveryLocked);
        public void Restore(DangoUser user)
        {
            user.LockoutEnd = End;
            user.LockoutEnabled = Enabled;
            user.AccessFailedCount = Failed;
            user.DisabledAt = DisabledAt;
            user.RecoveryLocked = RecoveryLocked;
        }
    }
}
