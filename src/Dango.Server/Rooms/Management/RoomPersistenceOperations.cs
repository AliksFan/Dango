using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

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

    public static async Task DetachAsync(DangoDbContext db, Guid roomId, CancellationToken ct)
    {
        RequireTransaction(db);
        var states = await db.PetRoomStates.FromSqlInterpolated(
            $"SELECT * FROM dango.pet_room_states WHERE room_id = {roomId} ORDER BY pet_id FOR UPDATE").ToArrayAsync(ct);
        foreach (var state in states)
        {
            await db.Entry(state).ReloadAsync(ct);
            if (state.RoomId != roomId) continue;
            if (state.Version == long.MaxValue || state.ControlGeneration == long.MaxValue)
                throw new AccountFault("room_state_version_exhausted", 409);
            state.RoomId = null;
            state.PositionX = state.PositionY = null;
            state.IsSleeping = false;
            state.Version++;
            state.ControlGeneration++;
        }
    }

    public static void Append(DangoDbContext db, Guid roomId, long version, string operation, DateTimeOffset now,
        Guid? actor = null, Guid? requestId = null, string? requestHash = null)
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
        db.RoomDomainEvents.Add(new() { Id = id, RoomId = roomId, RoomVersion = version, EventType = operation, OccurredAt = now });
    }

    private static void RequireTransaction(DangoDbContext db)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("room_transaction_required");
    }
}
