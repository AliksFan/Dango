using System.Security.Cryptography;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Access;

public interface IRoomAccessCheckpoint
{
    Task ReachedAsync(string name, Guid petId, CancellationToken ct);
}
public sealed class RoomAccessCheckpoint : IRoomAccessCheckpoint
{
    public Task ReachedAsync(string name, Guid petId, CancellationToken ct) => Task.CompletedTask;
}

public sealed class RoomAccessContext(Guid? actorId, Guid? sessionId, AccountPet pet, PetRoomState state,
    Dictionary<Guid, PersistentRoom> rooms, List<RoomDeviceLease> leases)
{
    public Guid? ActorId { get; } = actorId;
    public Guid? SessionId { get; } = sessionId;
    public AccountPet Pet { get; } = pet;
    public PetRoomState State { get; } = state;
    public Dictionary<Guid, PersistentRoom> Rooms { get; } = rooms;
    public List<RoomDeviceLease> Leases { get; } = leases;
    public Guid? RequiredLeaseAtCommit { get; set; }
    public PersistentRoom Room(Guid id) => Rooms.TryGetValue(id, out var room) ? room : throw new AccountFault("room_not_found", 404);
}

public static class RoomAccessPersistence
{
    public const int MaximumDevices = 8;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 16
    };

    public static Task<T> ActorAsync<T>(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
        IRoomAccessCheckpoint checkpoint, Guid userId, Guid sessionId, Guid? targetRoomId, Guid? targetPetId,
        Func<RoomAccessContext, Task<T>> action, CancellationToken ct) =>
        ExecuteAsync(db, environment, clock, checkpoint, userId, sessionId, targetRoomId, targetPetId, action, ct);

    public static Task<T> SystemAsync<T>(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
        IRoomAccessCheckpoint checkpoint, Guid petId, Func<RoomAccessContext, Task<T>> action, CancellationToken ct) =>
        ExecuteAsync(db, environment, clock, checkpoint, null, null, null, petId, action, ct);

    private static async Task<T> ExecuteAsync<T>(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
        IRoomAccessCheckpoint checkpoint, Guid? actorId, Guid? sessionId, Guid? targetRoomId, Guid? targetPetId,
        Func<RoomAccessContext, Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) throw new InvalidOperationException("room_access_context_not_clean");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await checkpoint.ReachedAsync("before_locks", targetPetId ?? Guid.Empty, ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (actorId is not null) await AuthenticateAsync(db, actorId.Value, sessionId!.Value, environment, clock, ct);
                var pet = await db.Pets.AsNoTracking().SingleOrDefaultAsync(p => targetPetId != null ? p.Id == targetPetId : p.UserId == actorId, ct)
                    ?? throw new AccountFault("pet_not_found", 404);
                var source = await db.PetRoomStates.AsNoTracking().Where(s => s.PetId == pet.Id).Select(s => s.RoomId).SingleAsync(ct);
                var roomIds = new[] { source, targetRoomId }.Where(id => id != null).Select(id => id!.Value).Distinct().Order().ToArray();
                var preloaded = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToArrayAsync(ct);
                foreach (var owner in preloaded.Select(r => r.OwnerId).Distinct().Order())
                    await Management.RoomPersistenceOperations.OwnerAsync(db, owner, ct);
                var rooms = new Dictionary<Guid, PersistentRoom>();
                foreach (var id in roomIds)
                {
                    var room = await Management.RoomPersistenceOperations.RoomAsync(db, id, ct);
                    if (room is not null) rooms.Add(id, room);
                }
                var state = await db.PetRoomStates.FromSqlInterpolated($"SELECT * FROM dango.pet_room_states WHERE pet_id = {pet.Id} FOR UPDATE").SingleAsync(ct);
                await db.Entry(state).ReloadAsync(ct);
                if (state.RoomId != source)
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    continue;
                }
                var leases = await db.RoomDeviceLeases.FromSqlInterpolated(
                    $"SELECT * FROM dango.room_device_leases WHERE pet_id = {pet.Id} AND revoked_at IS NULL ORDER BY id LIMIT 9 FOR UPDATE").ToListAsync(ct);
                if (leases.Count > MaximumDevices) throw new AccountFault("room_device_limit", 409);
                foreach (var lease in leases) await db.Entry(lease).ReloadAsync(ct);
                var context = new RoomAccessContext(actorId, sessionId, pet, state, rooms, leases);
                await checkpoint.ReachedAsync("after_locks", pet.Id, ct);
                var result = await action(context);
                await db.SaveChangesAsync(ct);
                await checkpoint.ReachedAsync("after_save", pet.Id, ct);
                await checkpoint.ReachedAsync("before_commit", pet.Id, ct);
                if (actorId is not null && !await SessionValidAsync(db, actorId.Value, sessionId!.Value, environment, clock, ct))
                    throw new AccountFault("authentication_required", 401);
                if (context.RequiredLeaseAtCommit is { } required)
                {
                    var effective = await EffectiveLeasesAsync(db, context, environment, clock, ct);
                    if (!effective.Any(lease => lease.Id == required)) throw new AccountFault("room_lease_expired", 409);
                }
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                // The clean-entry guard ensures these changes belong only to this operation.
                // A failed operation must not leave writes available to a later scoped command.
                db.ChangeTracker.Clear();
                throw;
            }
        }
        throw new AccountFault("room_assignment_conflict", 409);
    }

    public static async Task AuthenticateAsync(DangoDbContext db, Guid userId, Guid sessionId,
        AccountEnvironment environment, TimeProvider clock, CancellationToken ct)
    {
        var email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct)
            ?? throw new AccountFault("authentication_required", 401);
        await AccountLocks.EmailAsync(db, email, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct) ?? throw new AccountFault("authentication_required", 401);
        await db.Entry(user).ReloadAsync(ct);
        var session = await db.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM dango.auth_sessions WHERE id = {sessionId} AND user_id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (session is not null) await db.Entry(session).ReloadAsync(ct);
        if (user.NormalizedEmail != email || session is null || !AccountSessionPolicy.IsValid(user, session, environment.RecoveryEpoch, clock.GetUtcNow()))
            throw new AccountFault("authentication_required", 401);
    }

    public static async Task<bool> SessionValidAsync(DangoDbContext db, Guid userId, Guid sessionId,
        AccountEnvironment environment, TimeProvider clock, CancellationToken ct)
    {
        var current = await (from s in db.AuthSessions.AsNoTracking()
                             join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                             where u.Id == userId && s.Id == sessionId
                             select new { User = u, Session = s }).SingleOrDefaultAsync(ct);
        return current is not null && AccountSessionPolicy.IsValid(current.User, current.Session, environment.RecoveryEpoch, clock.GetUtcNow());
    }

    public static async Task<List<RoomDeviceLease>> EffectiveLeasesAsync(DangoDbContext db, RoomAccessContext context,
        AccountEnvironment environment, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (context.State.RoomId is not { } roomId || !context.Rooms.TryGetValue(roomId, out var room)
            || room.Status != PersistentRoomStatus.Active) return [];
        var ids = context.Leases.Where(l => l.RevokedAt == null && l.ExpiresAt > now && l.RoomId == context.State.RoomId
            && l.UserId == context.Pet.UserId && l.PetId == context.Pet.Id).Select(l => l.SessionId).Distinct().ToArray();
        var sessions = await (from s in db.AuthSessions.AsNoTracking()
                              join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                              where ids.Contains(s.Id) && u.Id == context.Pet.UserId
                              select new { User = u, Session = s }).ToArrayAsync(ct);
        now = clock.GetUtcNow();
        var valid = sessions.Where(s => AccountSessionPolicy.IsValid(s.User, s.Session, environment.RecoveryEpoch, now)).Select(s => s.Session.Id).ToHashSet();
        return context.Leases.Where(l => l.RevokedAt == null && l.ExpiresAt > now && l.RoomId != null && l.RoomId == context.State.RoomId
            && l.UserId == context.Pet.UserId && l.PetId == context.Pet.Id && valid.Contains(l.SessionId)
            && (room.Kind != PersistentRoomKind.DesktopPet || l.DeviceKind == "desktop")).ToList();
    }

    public static long AdvanceRoom(PersistentRoom room)
    {
        if (room.Version == long.MaxValue) throw new AccountFault("room_version_exhausted", 409);
        return ++room.Version;
    }

    public static void AdvanceState(PetRoomState state)
    {
        if (state.Version == long.MaxValue || state.ControlGeneration == long.MaxValue) throw new AccountFault("room_state_version_exhausted", 409);
        state.Version++;
        state.ControlGeneration++;
    }

    public static void EndLease(RoomDeviceLease lease, DateTimeOffset now)
    {
        lease.RevokedAt = now;
        lease.RoomId = null;
        lease.TransportId = null;
    }

    public static void ClearAssignment(RoomAccessContext context, DateTimeOffset now)
    {
        foreach (var lease in context.Leases.Where(l => l.RevokedAt == null)) EndLease(lease, now);
        context.State.RoomId = null;
        context.State.PositionX = context.State.PositionY = null;
        context.State.IsSleeping = false;
        context.State.ControlLeaseId = null;
        AdvanceState(context.State);
    }

    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    public static string Serialize<T>(T result) => JsonSerializer.Serialize(result, Json);
    public static T Deserialize<T>(string? json)
    {
        if (json is null) throw new AccountFault("room_receipt_invalid", 503);
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new AccountFault("room_receipt_invalid", 503); }
        catch (JsonException) { throw new AccountFault("room_receipt_invalid", 503); }
    }

    public static async Task<RoomOperationAudit?> ReplayAsync(DangoDbContext db, RoomAccessContext context,
        Guid requestId, string hash, CancellationToken ct, bool bindSession = true)
    {
        if (requestId == Guid.Empty) throw new AccountFault("room_request_invalid");
        var receipt = await db.RoomOperationAudits.AsNoTracking().SingleOrDefaultAsync(a => a.ActorUserId == context.ActorId && a.RequestId == requestId, ct);
        if (receipt is not null && (receipt.RequestHash != hash || (bindSession && receipt.BoundSessionId != context.SessionId)))
            throw new AccountFault("room_request_conflict", 409);
        return receipt;
    }

    public static void Append(DangoDbContext db, PersistentRoom room, string operation, DateTimeOffset now,
        IEnumerable<Guid> targets, RoomAccessContext? actor = null, Guid? requestId = null, string? hash = null, string? resultJson = null)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("room_transaction_required");
        var recipients = targets.Distinct().Order().ToArray();
        if (recipients.Length > 32 || recipients.Contains(Guid.Empty)) throw new InvalidOperationException("room_event_targets_invalid");
        var id = Guid.NewGuid();
        db.RoomDomainEvents.Add(new()
        {
            Id = id,
            RoomId = room.Id,
            RoomVersion = room.Version,
            EventType = operation,
            OccurredAt = now,
            TargetUserIds = recipients
        });
        if (requestId != null || operation == "offline") db.RoomOperationAudits.Add(new()
        {
            Id = id,
            RoomId = room.Id,
            AppliedVersion = room.Version,
            Operation = operation,
            OccurredAt = now,
            ActorUserId = requestId == null ? null : actor?.ActorId,
            RequestId = requestId,
            RequestHash = hash,
            BoundSessionId = requestId == null ? null : actor?.SessionId,
            ResultJson = resultJson
        });
    }
}
