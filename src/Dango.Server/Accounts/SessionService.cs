using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Accounts;

public sealed record SessionLogin(Guid UserId, Guid SessionId, DateTimeOffset ExpiresAt, bool Remembered);
public sealed record SessionSummary(Guid SessionId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt, bool Remembered);

public static class AccountSessionPolicy
{
    public const int MaxActiveSessions = 20;
    public const int MaxSessionSummaries = 50;
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static DateTimeOffset ExpiresAt(DateTimeOffset createdAt, bool remembered) =>
        createdAt.AddHours(remembered ? 168 : 8);

    public static bool CanAuthenticate(DangoUser user, DateTimeOffset now) =>
        user.EmailConfirmed && user.DisabledAt is null && !user.RecoveryLocked
        && !string.IsNullOrEmpty(user.SecurityStamp) && (user.LockoutEnd is null || user.LockoutEnd <= now);

    public static bool IsValid(DangoUser user, AuthSession session, Guid recoveryEpoch, DateTimeOffset now) =>
        CanAuthenticate(user, now) && session.UserId == user.Id && session.RecoveryEpoch == recoveryEpoch
        && session.SecurityStamp == user.SecurityStamp && session.RevokedAt is null
        && session.CreatedAt <= now && session.ExpiresAt > now && session.ExpiresAt > session.CreatedAt
        && session.ExpiresAt <= ExpiresAt(session.CreatedAt, session.Remembered);
}

public sealed class SessionService(DangoDbContext db, UserManager<DangoUser> users,
    AccountEnvironment environment, TimeProvider clock)
{
    public async Task<SessionLogin> LoginAsync(string email, string password, bool remembered, CancellationToken ct)
    {
        (string Email, string Normalized) input;
        try { input = AccountInput.Email(email); }
        catch (AccountFault) { throw LoginFailed(); }
        if (password is null || password.Length > 128) throw LoginFailed();
        if (users.Options.Lockout.MaxFailedAccessAttempts != AccountSessionPolicy.MaxFailedAccessAttempts
            || users.Options.Lockout.DefaultLockoutTimeSpan != AccountSessionPolicy.LockoutDuration)
            throw new InvalidOperationException("Identity lockout options do not match the account policy.");
        var normalized = users.NormalizeEmail(input.Email) ?? throw LoginFailed();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, normalized, ct);
        var id = await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
        if (id is null) throw LoginFailed();
        var user = await AccountLocks.UserAsync(db, id.Value, ct);
        if (user is null) throw LoginFailed();
        // A scoped context may already track this user; the lock must protect fresh state.
        await db.Entry(user).ReloadAsync(ct);
        var now = clock.GetUtcNow();
        if (user.NormalizedEmail != normalized || !AccountSessionPolicy.CanAuthenticate(user, now)) throw LoginFailed();
        var failedBefore = user.AccessFailedCount;
        if (!await users.CheckPasswordAsync(user, password))
        {
            RequireIdentityUpdate(await users.AccessFailedAsync(user));
            if (failedBefore >= AccountSessionPolicy.MaxFailedAccessAttempts - 1)
            {
                // Identity uses its system clock. Keep the persisted lockout aligned with our clock.
                user.LockoutEnd = clock.GetUtcNow().Add(AccountSessionPolicy.LockoutDuration);
                RequireIdentityUpdate(await users.UpdateAsync(user));
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            // Throw only after committing the failed-attempt counter and any new lockout.
            throw LoginFailed();
        }
        RequireIdentityUpdate(await users.ResetAccessFailedCountAsync(user));
        now = clock.GetUtcNow();
        var active = await db.AuthSessions.Where(s => s.UserId == user.Id && s.RecoveryEpoch == environment.RecoveryEpoch
                && s.SecurityStamp == user.SecurityStamp && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => s.Id).Take(AccountSessionPolicy.MaxActiveSessions).CountAsync(ct);
        if (active >= AccountSessionPolicy.MaxActiveSessions) throw new AccountFault("session_limit_reached", 409);
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp!,
            RecoveryEpoch = environment.RecoveryEpoch,
            CreatedAt = now,
            ExpiresAt = AccountSessionPolicy.ExpiresAt(now, remembered),
            Remembered = remembered
        };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(user.Id, session.Id, session.ExpiresAt, session.Remembered);
    }

    public async Task<bool> ValidateAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var snapshot = await (from session in db.AuthSessions.AsNoTracking()
                              join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                              where session.Id == sessionId && user.Id == userId
                              select new { User = user, Session = session }).SingleOrDefaultAsync(ct);
        return snapshot is not null && AccountSessionPolicy.IsValid(snapshot.User, snapshot.Session,
            environment.RecoveryEpoch, clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<SessionSummary>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.AuthSessions.AsNoTracking().Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Take(AccountSessionPolicy.MaxSessionSummaries)
            .Select(s => new SessionSummary(s.Id, s.CreatedAt, s.ExpiresAt, s.RevokedAt, s.Remembered)).ToListAsync(ct);

    public async Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var normalized = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct)
            ?? throw new AccountFault("session_not_found", 404);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, normalized, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct);
        if (user is null) throw new AccountFault("session_not_found", 404);
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalized) throw new AccountFault("session_not_found", 404);
        var session = await db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new AccountFault("session_not_found", 404);
        if (session.RevokedAt is null)
        {
            var now = clock.GetUtcNow();
            await db.AuthSessions.Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), ct);
        }
        await transaction.CommitAsync(ct);
    }

    private static AccountFault LoginFailed() => new("login_failed", 401);
    private static void RequireIdentityUpdate(IdentityResult result)
    {
        if (!result.Succeeded) throw new AccountFault("account_update_conflict", 409);
    }
}
