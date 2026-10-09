using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Access;

public sealed record RoomLeaseSweepResult(int Examined, int Changed, int Failed, Guid? NextCursor);

public sealed class RoomLeaseMaintenanceService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock, IRoomAccessCheckpoint checkpoint)
{
    public async Task<RoomLeaseSweepResult> RunBatchAsync(int limit, IReadOnlyCollection<Guid>? permittedPetIds, CancellationToken ct, Guid? afterPetId = null)
    {
        var ids = await CandidatesQuery(limit, permittedPetIds, afterPetId).ToArrayAsync(ct);
        var changed = 0; var failed = 0;
        var service = new RoomAdmissionService(db, environment, clock, checkpoint);
        foreach (var id in ids.Take(limit))
        {
            ct.ThrowIfCancellationRequested();
            try { if (await service.ReconcilePetAsync(id, ct)) changed++; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { failed++; }
        }
        return new(Math.Min(ids.Length, limit), changed, failed, ids.Length > limit ? ids[limit - 1] : null);
    }

    internal IQueryable<Guid> CandidatesQuery(int limit, IReadOnlyCollection<Guid>? permittedPetIds, Guid? afterPetId)
    {
        RoomAdmissionPolicy.Batch(limit, permittedPetIds);
        var now = clock.GetUtcNow();
        var due = from lease in db.RoomDeviceLeases.AsNoTracking()
                  join session in db.AuthSessions.AsNoTracking() on lease.SessionId equals session.Id
                  join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                  where lease.RevokedAt == null && (lease.ExpiresAt <= now || session.ExpiresAt <= now || session.RevokedAt != null
                      || session.RecoveryEpoch != environment.RecoveryEpoch || session.SecurityStamp != user.SecurityStamp
                      || user.DisabledAt != null || user.RecoveryLocked || !user.EmailConfirmed || user.LockoutEnd > now)
                  select lease.PetId;
        if (permittedPetIds is not null) due = due.Where(id => permittedPetIds.Contains(id));
        if (afterPetId == Guid.Empty) throw new AccountFault("room_cursor_invalid");
        if (afterPetId is not null) due = due.Where(id => id.CompareTo(afterPetId.Value) > 0);
        return due.Distinct().Order().Take(limit + 1);
    }
}
