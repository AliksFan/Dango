using Dango.Protocol;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Accounts;

public interface IAccountProfileCheckpoint
{
    Task ReachedAsync(string name, CancellationToken ct);
}

public sealed class AccountProfileCheckpoint : IAccountProfileCheckpoint
{
    public Task ReachedAsync(string name, CancellationToken ct) => Task.CompletedTask;
}

public static class AccountProfilePolicy
{
    public static AccountProfileSnapshot Describe(DangoUser user)
    {
        if (user.Id == Guid.Empty || user.ProfileVersion < 1) throw InvalidState();
        if (user.Nickname is not null)
        {
            try { if (AccountNickname.Normalize(user.Nickname) != user.Nickname) throw InvalidState(); }
            catch (AccountNicknameException) { throw InvalidState(); }
        }
        return new(user.Id, user.Nickname, user.ProfileVersion);
    }

    private static AccountFault InvalidState() => new("account_profile_invalid_state", 503);
}

public sealed class AccountProfileService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
    IAccountProfileCheckpoint checkpoint)
{
    public async Task<AccountProfileSnapshot> GetAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        // A single fresh statement ties the profile to its current owning session.
        var snapshot = await (from session in db.AuthSessions.AsNoTracking()
                              join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                              where user.Id == userId && session.Id == sessionId
                              select new { User = user, Session = session }).SingleOrDefaultAsync(ct);
        if (snapshot is null) throw AuthenticationRequired();
        RequireSession(snapshot.User, snapshot.Session);
        return AccountProfilePolicy.Describe(snapshot.User);
    }

    public async Task<AccountProfileSnapshot> UpdateAsync(Guid userId, Guid sessionId, AccountProfileUpdate update, CancellationToken ct)
    {
        if (update.ExpectedVersion < 1) throw new AccountFault("expected_version_invalid");
        string nickname;
        try { nickname = AccountNickname.Normalize(update.Nickname); }
        catch (AccountNicknameException fault) { throw new AccountFault(fault.Code); }
        var normalized = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct) ?? throw AuthenticationRequired();
        await checkpoint.ReachedAsync("before_locks", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same order as password changes, login, revocation and pet configuration writes.
        await AccountLocks.EmailAsync(db, normalized, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct) ?? throw AuthenticationRequired();
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalized) throw AuthenticationRequired();
        var session = await db.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM dango.auth_sessions WHERE id = {sessionId} AND user_id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw AuthenticationRequired();
        await db.Entry(session).ReloadAsync(ct);
        RequireSession(user, session);
        _ = AccountProfilePolicy.Describe(user);
        await checkpoint.ReachedAsync("after_locks", ct);
        RequireSession(user, session);
        if (user.ProfileVersion != update.ExpectedVersion) throw Conflict();
        if (user.ProfileVersion == long.MaxValue) throw new AccountFault("account_profile_version_exhausted", 409);
        user.Nickname = nickname;
        user.ProfileVersion++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
        await checkpoint.ReachedAsync("after_profile", ct);
        await checkpoint.ReachedAsync("before_commit", ct);
        // Expiry can pass during a wait. Recheck immediately before committing; failures roll back.
        var currentUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var currentSession = await db.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        RequireSession(currentUser, currentSession);
        var result = AccountProfilePolicy.Describe(currentUser);
        await transaction.CommitAsync(ct);
        return result;
    }

    private void RequireSession(DangoUser user, AuthSession session)
    {
        if (!AccountSessionPolicy.IsValid(user, session, environment.RecoveryEpoch, clock.GetUtcNow()))
            throw AuthenticationRequired();
    }

    private static AccountFault AuthenticationRequired() => new("authentication_required", 401);
    private static AccountFault Conflict() => new("account_profile_conflict", 409);
}
