using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Management;

public sealed class RoomManagementService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
    IRoomManagementCheckpoint checkpoint)
{
    public Task<RoomOperationResult> CreateAsync(Guid userId, Guid sessionId, RoomCreateRequest request, CancellationToken ct)
        => ValidateThenExecuteAsync(userId, sessionId, request.RequestId, () =>
    {
        var kind = RoomManagementPolicy.CreateKind(request.Kind);
        var name = RoomManagementPolicy.Name(request.Name);
        RoomManagementPolicy.Capacity(kind, request.Capacity);
        var hash = RoomManagementPolicy.RequestHash("created", null, kind: kind, name: name, capacity: request.Capacity);
        return ExecuteAsync(userId, sessionId, request.RequestId, Guid.Empty, hash, "created", async () =>
        {
            await checkpoint.ReachedAsync("after_locks", Guid.Empty, ct);
            await RequireQuotaAsync(userId, ct);
            var room = new PersistentRoom
            {
                Id = Guid.NewGuid(), OwnerId = userId, Kind = kind, Name = name, Capacity = request.Capacity,
                TemplateId = "ordinary-default", TemplateVersion = 1,
                Status = PersistentRoomStatus.Active, Version = 1, CreatedAt = clock.GetUtcNow()
            };
            db.Rooms.Add(room);
            await SaveAsync(ct);
            await checkpoint.ReachedAsync("after_room", room.Id, ct);
            // Public owner identity does not reserve capacity or grant B4 admission.
            db.RoomMemberships.Add(new() { RoomId = room.Id, UserId = userId, Role = PersistentRoomRole.Owner });
            await SaveAsync(ct);
            await checkpoint.ReachedAsync("after_membership", room.Id, ct);
            return new Applied(room);
        }, ct);
    }, ct);

    public Task<RoomOperationResult> UpdateAsync(Guid userId, Guid sessionId, Guid roomId, RoomUpdateRequest request, CancellationToken ct)
        => ValidateThenExecuteAsync(userId, sessionId, request.RequestId, () =>
    {
        RoomManagementPolicy.ExpectedVersion(request.ExpectedVersion);
        var name = RoomManagementPolicy.Name(request.Name);
        var hash = RoomManagementPolicy.RequestHash("updated", roomId, request.ExpectedVersion, name: name, capacity: request.Capacity);
        return ExecuteAsync(userId, sessionId, request.RequestId, roomId, hash, "updated", async () =>
        {
            var room = await OwnedRoomAsync(userId, roomId, ct);
            RoomManagementPolicy.Active(room);
            var version = RoomManagementPolicy.NextVersion(room.Version, request.ExpectedVersion);
            var reserved = await db.RoomMemberships.CountAsync(m => m.RoomId == roomId, ct);
            var occupancy = await db.PetRoomStates.CountAsync(s => s.RoomId == roomId, ct);
            RoomManagementPolicy.Seats(room.Kind, request.Capacity, reserved, occupancy);
            room.Name = name;
            room.Capacity = request.Capacity;
            room.Version = version;
            await SaveAsync(ct);
            await checkpoint.ReachedAsync("after_room", roomId, ct);
            return new Applied(room);
        }, ct);
    }, ct);

    public Task<RoomOperationResult> DissolveAsync(Guid userId, Guid sessionId, Guid roomId, RoomDissolveRequest request, CancellationToken ct)
        => ValidateThenExecuteAsync(userId, sessionId, request.RequestId, () =>
    {
        RoomManagementPolicy.ExpectedVersion(request.ExpectedVersion);
        if (!request.Confirmed) throw new AccountFault("room_confirmation_required");
        var hash = RoomManagementPolicy.RequestHash("dissolved", roomId, request.ExpectedVersion, confirmed: request.Confirmed);
        return ExecuteAsync(userId, sessionId, request.RequestId, roomId, hash, "dissolved", async () =>
        {
            var room = await OwnedRoomAsync(userId, roomId, ct);
            RoomManagementPolicy.Shared(room);
            RoomManagementPolicy.Active(room);
            var version = RoomManagementPolicy.NextVersion(room.Version, request.ExpectedVersion);
            await RoomPersistenceOperations.DetachAsync(db, roomId, ct);
            await SaveAsync(ct);
            await checkpoint.ReachedAsync("after_detach", roomId, ct);
            var now = clock.GetUtcNow();
            room.Status = PersistentRoomStatus.Dissolved;
            room.DissolvedAt = now;
            room.RecoverUntil = now.Add(RoomManagementPolicy.RecoveryWindow);
            room.Version = version;
            await SaveAsync(ct);
            await checkpoint.ReachedAsync("after_room", roomId, ct);
            return new Applied(room);
        }, ct);
    }, ct);

    public Task<RoomOperationResult> RestoreAsync(Guid userId, Guid sessionId, Guid roomId, RoomRestoreRequest request, CancellationToken ct)
        => ValidateThenExecuteAsync(userId, sessionId, request.RequestId, () =>
    {
        RoomManagementPolicy.ExpectedVersion(request.ExpectedVersion);
        var hash = RoomManagementPolicy.RequestHash("restored", roomId, request.ExpectedVersion);
        return ExecuteAsync(userId, sessionId, request.RequestId, roomId, hash, "restored", async () =>
        {
            var room = await OwnedRoomAsync(userId, roomId, ct);
            RoomManagementPolicy.Shared(room);
            if (room.Status != PersistentRoomStatus.Dissolved) throw new AccountFault("room_state_conflict", 409);
            var version = RoomManagementPolicy.NextVersion(room.Version, request.ExpectedVersion);
            var until = room.RecoverUntil;
            RoomManagementPolicy.RecoveryDeadline(until, clock.GetUtcNow());
            await RequireQuotaAsync(userId, ct);
            room.Status = PersistentRoomStatus.Active;
            room.DissolvedAt = room.RecoverUntil = null;
            room.Version = version;
            await SaveAsync(ct);
            await checkpoint.ReachedAsync("after_room", roomId, ct);
            // Restore configuration and existing qualifications only; no pet assignment is changed.
            return new Applied(room, until);
        }, ct);
    }, ct);

    public async Task<RoomManagementView> GetAsync(Guid userId, Guid sessionId, Guid roomId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSessionAsync(userId, sessionId, ct);
        var snapshot = await DetailQuery(userId, roomId).SingleOrDefaultAsync(ct)
            ?? throw NotFound();
        await RequireFreshSessionAsync(userId, sessionId, ct);
        var result = Describe(snapshot, clock.GetUtcNow());
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<RoomManagementPage> ListAsync(Guid userId, Guid sessionId, string state, int limit, Guid? after, CancellationToken ct)
    {
        var query = ListQuery(userId, state, limit, after);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSessionAsync(userId, sessionId, ct);
        var snapshots = await query.ToArrayAsync(ct);
        await RequireFreshSessionAsync(userId, sessionId, ct);
        var now = clock.GetUtcNow();
        var rooms = snapshots.Take(limit).Select(r => Describe(r, now)).ToArray();
        var next = snapshots.Length > limit ? rooms[^1].RoomId : (Guid?)null;
        await transaction.CommitAsync(ct);
        return new(rooms, next);
    }

    private Task<RoomOperationResult> ValidateThenExecuteAsync(Guid userId, Guid sessionId, Guid requestId,
        Func<Task<RoomOperationResult>> prepare, CancellationToken ct)
    {
        RoomManagementPolicy.Request(requestId);
        try { return prepare(); }
        catch (AccountFault fault) when (fault.Status == 400) { return RejectInvalidRequestAsync(userId, sessionId, requestId, fault, ct); }
    }

    private async Task<RoomOperationResult> RejectInvalidRequestAsync(Guid userId, Guid sessionId, Guid requestId,
        AccountFault inputFault, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSessionAsync(userId, sessionId, ct);
        await RoomPersistenceOperations.OwnerAsync(db, userId, ct);
        // Invalid input cannot equal a previously accepted canonical request. Check the actor's
        // receipt before returning 400, so malformed parameter changes cannot evade key conflicts.
        if (await db.RoomOperationAudits.AsNoTracking().AnyAsync(a => a.ActorUserId == userId && a.RequestId == requestId, ct))
            throw new AccountFault("room_request_conflict", 409);
        throw inputFault;
    }

    private async Task<RoomOperationResult> ExecuteAsync(Guid userId, Guid sessionId, Guid requestId, Guid roomId,
        string hash, string operation, Func<Task<Applied>> apply, CancellationToken ct)
    {
        await checkpoint.ReachedAsync("before_locks", roomId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSessionAsync(userId, sessionId, ct);
        await RoomPersistenceOperations.OwnerAsync(db, userId, ct);
        // Actor locking serializes this actor's request IDs even when they target different rooms.
        var original = await db.RoomOperationAudits.AsNoTracking()
            .SingleOrDefaultAsync(a => a.ActorUserId == userId && a.RequestId == requestId, ct);
        if (original is not null)
        {
            if (!string.Equals(original.RequestHash, hash, StringComparison.Ordinal)) throw new AccountFault("room_request_conflict", 409);
            await RequireFreshSessionAsync(userId, sessionId, ct);
            await transaction.CommitAsync(ct);
            return new(original.RoomId, original.AppliedVersion, original.Operation);
        }
        var applied = await apply();
        RoomPersistenceOperations.Append(db, applied.Room.Id, applied.Room.Version, operation, clock.GetUtcNow(), userId, requestId, hash);
        await SaveAsync(ct);
        await checkpoint.ReachedAsync("after_audit", applied.Room.Id, ct);
        await checkpoint.ReachedAsync("before_commit", applied.Room.Id, ct);
        await RequireFreshSessionAsync(userId, sessionId, ct);
        if (applied.RestoreUntil is not null) RoomManagementPolicy.RecoveryDeadline(applied.RestoreUntil, clock.GetUtcNow());
        await transaction.CommitAsync(ct);
        return new(applied.Room.Id, applied.Room.Version, operation);
    }

    private async Task LockSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var normalized = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.NormalizedEmail)
            .SingleOrDefaultAsync(ct) ?? throw AuthenticationRequired();
        await AccountLocks.EmailAsync(db, normalized, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct) ?? throw AuthenticationRequired();
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalized) throw AuthenticationRequired();
        var session = await db.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM dango.auth_sessions WHERE id = {sessionId} AND user_id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw AuthenticationRequired();
        await db.Entry(session).ReloadAsync(ct);
        RequireSession(user, session);
    }

    private async Task RequireFreshSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var snapshot = await (from session in db.AuthSessions.AsNoTracking()
                              join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                              where session.Id == sessionId && user.Id == userId
                              select new { User = user, Session = session }).SingleOrDefaultAsync(ct)
            ?? throw AuthenticationRequired();
        RequireSession(snapshot.User, snapshot.Session);
    }

    private void RequireSession(DangoUser user, AuthSession session)
    {
        if (!AccountSessionPolicy.IsValid(user, session, environment.RecoveryEpoch, clock.GetUtcNow())) throw AuthenticationRequired();
    }

    private async Task<PersistentRoom> OwnedRoomAsync(Guid ownerId, Guid roomId, CancellationToken ct)
    {
        var room = await RoomPersistenceOperations.RoomAsync(db, roomId, ct);
        if (room is null || room.OwnerId != ownerId) throw NotFound();
        await checkpoint.ReachedAsync("after_locks", roomId, ct);
        return room;
    }

    private async Task RequireQuotaAsync(Guid ownerId, CancellationToken ct) => RoomManagementPolicy.Quota(
        await db.Rooms.Where(r => r.OwnerId == ownerId && r.Kind != PersistentRoomKind.DesktopPet
            && r.Status == PersistentRoomStatus.Active).CountAsync(ct));

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AccountFault("room_version_conflict", 409); }
    }

    private IQueryable<Snapshot> Snapshots(IQueryable<PersistentRoom> rooms) => rooms.Select(room => new Snapshot(room,
        db.RoomMemberships.Count(m => m.RoomId == room.Id), db.PetRoomStates.Count(s => s.RoomId == room.Id)));

    internal IQueryable<Snapshot> DetailQuery(Guid ownerId, Guid roomId) =>
        Snapshots(db.Rooms.AsNoTracking().Where(r => r.Id == roomId && r.OwnerId == ownerId));

    internal IQueryable<Snapshot> ListQuery(Guid ownerId, string state, int limit, Guid? after)
    {
        var status = RoomManagementPolicy.ListState(state);
        RoomManagementPolicy.PageSize(limit);
        if (after == Guid.Empty) throw new AccountFault("room_cursor_invalid");
        var rooms = db.Rooms.AsNoTracking().Where(r => r.OwnerId == ownerId
            && (r.Status == PersistentRoomStatus.Active || r.Status == PersistentRoomStatus.Dissolved));
        if (status is not null) rooms = rooms.Where(r => r.Status == status.Value);
        if (after is not null) rooms = rooms.Where(r => r.Id.CompareTo(after.Value) > 0);
        return Snapshots(rooms.OrderBy(r => r.Id).Take(limit + 1));
    }

    private static RoomManagementView Describe(Snapshot snapshot, DateTimeOffset now)
    {
        var room = snapshot.Room;
        var status = room.Status switch
        {
            PersistentRoomStatus.Active => "active",
            PersistentRoomStatus.Dissolved => "dissolved",
            _ => throw new AccountFault("room_invalid_state", 503)
        };
        return new(room.Id, RoomManagementPolicy.Kind(room.Kind), room.Name, room.Capacity, status, room.Version,
            room.Kind == PersistentRoomKind.PublicShared ? 0 : snapshot.ReservedSeats, snapshot.Occupancy, room.RecoverUntil,
            room.Status == PersistentRoomStatus.Dissolved && room.RecoverUntil > now, room.TemplateId, room.TemplateVersion);
    }

    private static AccountFault AuthenticationRequired() => new("authentication_required", 401);
    private static AccountFault NotFound() => new("room_not_found", 404);
    private sealed record Applied(PersistentRoom Room, DateTimeOffset? RestoreUntil = null);
    internal sealed record Snapshot(PersistentRoom Room, int ReservedSeats, int Occupancy);
}
