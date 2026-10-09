using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Dango.Server.Rooms.Access;

namespace Dango.Server.Rooms.Management;

public interface IRoomManagementCheckpoint
{
    Task ReachedAsync(string name, Guid roomId, CancellationToken ct);
}
public sealed class RoomManagementCheckpoint : IRoomManagementCheckpoint
{
    public Task ReachedAsync(string name, Guid roomId, CancellationToken ct) => Task.CompletedTask;
}

public static class RoomPersistenceOperations
{
    // Account credentials lock first, then this quota key, sorted rooms, and sorted pet states.
    // A system expiry task never locks credentials after taking this key.
    public static Task OwnerAsync(DangoDbContext db, Guid ownerId, CancellationToken ct)
    {
        RequireTransaction(db);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"room-owner:" + ownerId.ToString("D")}, 0))", ct);
    }

    public static async Task<PersistentRoom?> RoomAsync(DangoDbContext db, Guid roomId, CancellationToken ct)
    {
        RequireTransaction(db);
        var room = await db.Rooms.FromSqlInterpolated($"SELECT * FROM dango.rooms WHERE id = {roomId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (room is not null) await db.Entry(room).ReloadAsync(ct);
        return room;
    }

    public static async Task<Guid[]> DetachAsync(DangoDbContext db, Guid roomId, CancellationToken ct,
        DateTimeOffset? timestamp = null)
    {
        RequireTransaction(db);
        var states = await db.PetRoomStates.FromSqlInterpolated(
            $"SELECT * FROM dango.pet_room_states WHERE room_id = {roomId} ORDER BY pet_id LIMIT 13 FOR UPDATE").ToArrayAsync(ct);
        if (states.Length > 12) throw new AccountFault("room_occupancy_invalid", 503);
        var now = timestamp ?? DateTimeOffset.UtcNow;
        foreach (var state in states)
        {
            await db.Entry(state).ReloadAsync(ct);
            if (state.RoomId != roomId) continue;
            if (state.Version == long.MaxValue || state.ControlGeneration == long.MaxValue)
                throw new AccountFault("room_state_version_exhausted", 409);
            state.RoomId = null;
            state.PositionX = state.PositionY = null;
            state.IsSleeping = false;
            state.ControlLeaseId = null;
            state.Version++;
            state.ControlGeneration++;
        }
        var leases = await db.RoomDeviceLeases.FromSqlInterpolated(
            $"SELECT * FROM dango.room_device_leases WHERE room_id = {roomId} AND revoked_at IS NULL ORDER BY id LIMIT 97 FOR UPDATE").ToArrayAsync(ct);
        if (leases.Length > 96) throw new AccountFault("room_devices_invalid", 503);
        foreach (var lease in leases)
        {
            await db.Entry(lease).ReloadAsync(ct);
            if (lease.RoomId == roomId && lease.RevokedAt is null) RoomAccessPersistence.EndLease(lease, now);
        }
        await db.PublicRoomCredentials.Where(code => code.RoomId == roomId && code.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(code => code.RevokedAt, now), ct);
        var petIds = states.Select(state => state.PetId).ToArray();
        return await db.Pets.AsNoTracking().Where(pet => petIds.Contains(pet.Id)).Select(pet => pet.UserId).ToArrayAsync(ct);
    }

    public static void Append(DangoDbContext db, Guid roomId, long version, string operation, DateTimeOffset now,
        Guid? actor = null, Guid? requestId = null, string? requestHash = null, IEnumerable<Guid>? targets = null)
    {
        RequireTransaction(db);
        var id = Guid.NewGuid();
        db.RoomOperationAudits.Add(new()
        {
            Id = id,
            ActorUserId = actor,
            RequestId = requestId,
            RequestHash = requestHash,
            RoomId = roomId,
            AppliedVersion = version,
            Operation = operation,
            OccurredAt = now
        });
        var users = (targets ?? []).Concat(actor is { } user ? [user] : []).Distinct().Order().ToArray();
        if (users.Length > 32) throw new InvalidOperationException("room_event_targets_invalid");
        db.RoomDomainEvents.Add(new() { Id = id, RoomId = roomId, RoomVersion = version, EventType = operation,
            OccurredAt = now, TargetUserIds = users });
    }

    private static void RequireTransaction(DangoDbContext db)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("room_transaction_required");
    }
}
