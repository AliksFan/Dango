using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Management;

public sealed record RoomExpiryCursor(DateTimeOffset RecoverUntil, Guid RoomId);
public sealed record RoomExpiryFailure(Guid RoomId, string Code);
public sealed record RoomExpiryBatchResult(int Attempted, int Deleted, int Failed, RoomExpiryCursor? NextCursor,
    IReadOnlyList<RoomExpiryFailure> Failures);

public static class RoomExpiryPolicy
{
    public const int MaximumBatchSize = 50;

    public static Guid[]? ValidateBatch(int limit, RoomExpiryCursor? after, IReadOnlyCollection<Guid>? roomIds)
    {
        if (limit is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if (after?.RoomId == Guid.Empty) throw new ArgumentException("A room cursor requires a nonempty ID.", nameof(after));
        if (roomIds is null) return null;
        if (roomIds.Count is < 1 or > MaximumBatchSize)
            throw new ArgumentException("An expiry scope requires 1 to 50 distinct room IDs.", nameof(roomIds));
        var scope = roomIds.ToArray();
        if (scope.Any(id => id == Guid.Empty) || scope.Distinct().Count() != scope.Length)
            throw new ArgumentException("An expiry scope requires distinct, nonempty room IDs.", nameof(roomIds));
        return scope;
    }

    public static bool IsDue(PersistentRoom room, DateTimeOffset now) =>
        room.Kind is PersistentRoomKind.PrivateShared or PersistentRoomKind.PublicShared
        && room.Status is PersistentRoomStatus.Dissolved or PersistentRoomStatus.Expired
        && room.RecoverUntil is { } deadline && deadline <= now;
}

public sealed class RoomExpiryService(DangoDbContext db, TimeProvider clock, IRoomManagementCheckpoint checkpoint)
{
    public async Task<RoomExpiryBatchResult> PurgeBatchAsync(int limit, RoomExpiryCursor? after,
        IReadOnlyCollection<Guid>? roomIds, CancellationToken ct)
    {
        var scope = RoomExpiryPolicy.ValidateBatch(limit, after, roomIds);
        ct.ThrowIfCancellationRequested();
        // Each target has its own transaction and tracking lifetime. Never discard pending caller writes.
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Expiry requires a context without pending writes.");
        var now = clock.GetUtcNow();
        var query = db.Rooms.AsNoTracking().Where(room =>
            (room.Kind == PersistentRoomKind.PrivateShared || room.Kind == PersistentRoomKind.PublicShared)
            && (room.Status == PersistentRoomStatus.Dissolved || room.Status == PersistentRoomStatus.Expired)
            && room.RecoverUntil != null && room.RecoverUntil <= now);
        if (scope is not null) query = query.Where(room => scope.Contains(room.Id));
        if (after is not null)
        {
            var deadline = after.RecoverUntil.ToUniversalTime();
            var roomId = after.RoomId;
            query = query.Where(room => room.RecoverUntil > deadline
                || (room.RecoverUntil == deadline && room.Id.CompareTo(roomId) > 0));
        }
        var candidates = await query.OrderBy(room => room.RecoverUntil).ThenBy(room => room.Id).Take(limit)
            .Select(room => new { room.Id, room.OwnerId, RecoverUntil = room.RecoverUntil!.Value }).ToArrayAsync(ct);
        var failures = new List<RoomExpiryFailure>();
        var deleted = 0;
        RoomExpiryCursor? cursor = null;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            cursor = new(candidate.RecoverUntil, candidate.Id);
            try
            {
                if (await PurgeOneAsync(candidate.Id, candidate.OwnerId, ct)) deleted++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // No raw exception, row contents, credentials or input is returned to the worker.
                failures.Add(new(candidate.Id, "room_expiry_failed"));
            }
            finally { db.ChangeTracker.Clear(); }
        }
        return new(candidates.Length, deleted, failures.Count, cursor, failures.ToArray());
    }

    private async Task<bool> PurgeOneAsync(Guid roomId, Guid ownerId, CancellationToken ct)
    {
        await checkpoint.ReachedAsync("expiry_before_locks", roomId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RoomPersistenceOperations.OwnerAsync(db, ownerId, ct);
        var room = await RoomPersistenceOperations.RoomAsync(db, roomId, ct);
        if (room is null) return false;
        if (room.OwnerId != ownerId) throw new AccountFault("room_expiry_owner_changed", 409);
        await checkpoint.ReachedAsync("expiry_after_locks", roomId, ct);
        if (!RoomExpiryPolicy.IsDue(room, clock.GetUtcNow())) return false;
        if (room.Version == long.MaxValue) throw new AccountFault("room_version_exhausted", 409);
        var appliedVersion = room.Version + 1;

        await RoomPersistenceOperations.DetachAsync(db, roomId, ct);
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("expiry_after_detach", roomId, ct);
        // Delete dependants explicitly: every FK is restrictive, and identities must survive.
        await db.RoomMemberships.Where(member => member.RoomId == roomId).ExecuteDeleteAsync(ct);
        await checkpoint.ReachedAsync("expiry_after_memberships", roomId, ct);
        db.Rooms.Remove(room);
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("expiry_after_room", roomId, ct);
        RoomPersistenceOperations.Append(db, roomId, appliedVersion, "expired", clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("expiry_after_audit", roomId, ct);
        await checkpoint.ReachedAsync("expiry_before_commit", roomId, ct);
        if (!RoomExpiryPolicy.IsDue(room, clock.GetUtcNow())) return false;
        await transaction.CommitAsync(ct);
        return true;
    }
}
