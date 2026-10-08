using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Accounts;

// Maintenance-only service. No public HTTP route may invoke this authority.
public sealed class InvitationService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock)
{
    public async Task<Guid> IssueAsync(string email, string proof, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var normalized = AccountInput.Email(email).Normalized;
        var digest = AccountInput.Digest(proof);
        var now = clock.GetUtcNow();
        if (expiresAt <= now || expiresAt > now.AddDays(7)) throw new AccountFault("invitation_expiry_invalid");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, normalized, ct);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) throw new AccountFault("invitation_not_eligible", 409);
        var invitation = new TestInvitation
        {
            Id = Guid.NewGuid(), NormalizedEmail = normalized, SecretDigest = digest, RecoveryEpoch = environment.RecoveryEpoch,
            CreatedAt = now, ExpiresAt = expiresAt
        };
        db.TestInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invitation.Id;
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await db.TestInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new AccountFault("invitation_not_found", 404);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, snapshot.NormalizedEmail, ct);
        var invitation = (await AccountLocks.InvitationAsync(db, id, ct))!;
        invitation.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
