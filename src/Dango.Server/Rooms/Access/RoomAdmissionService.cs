using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Pets;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Rooms.Access;

public sealed class RoomAdmissionService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock, IRoomAccessCheckpoint checkpoint)
{
    public async Task<RoomAccessResult> EnterAsync(Guid userId, Guid sessionId, Guid roomId, RoomEnterRequest request, CancellationToken ct)
    {
        RoomAdmissionPolicy.Id(roomId, "room_invalid"); RoomAdmissionPolicy.Enter(request);
        var hash = RoomAccessPersistence.Hash(new { operation = "entered", roomId, request.InstanceId, request.DeviceKind,
            request.ExpectedRoomId, request.ExpectedAssignmentVersion, codeDigest = request.Code is null ? null : RoomAdmissionPolicy.CodeDigest(request.Code) });
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Authenticate and authorize this exact target before any independent lifecycle work.
            // Main admission repeats those checks; no actor pet or unrelated new room is swept.
            var preparation = await RoomAccessPersistence.ActorAsync(db, environment, clock, new RoomAccessCheckpoint(), userId, sessionId, roomId, null, async context =>
            {
                var original = await ReplayAsync(context, request.RequestId, hash, ct);
                if (original is not null) return new Preparation(original, []);
                RoomAdmissionPolicy.RequireAssignment(context.State, request.ExpectedRoomId, request.ExpectedAssignmentVersion);
                var target = await AuthorizeTargetAsync(context, roomId, request, ct);
                var petIds = target.Kind == PersistentRoomKind.PublicShared
                    ? await db.PetRoomStates.AsNoTracking().Where(s => s.RoomId == roomId && s.PetId != context.Pet.Id)
                        .OrderBy(s => s.PetId).Select(s => s.PetId).Take(13).ToArrayAsync(ct) : [];
                if (petIds.Length > 12) throw new AccountFault("room_occupancy_invalid", 503);
                return new Preparation(null, petIds);
            }, ct);
            if (preparation.Receipt is not null) return preparation.Receipt;
            foreach (var petId in preparation.PetIds) await ReconcileTargetPetAsync(petId, roomId, ct);
            try { return await EnterCoreAsync(userId, sessionId, roomId, request, hash, ct); }
            catch (AccountFault fault) when (fault.Code == "room_presence_retry" && attempt < 2) { }
        }
        throw new AccountFault("room_presence_retry", 409);
    }

    private Task<RoomAccessResult> EnterCoreAsync(Guid userId, Guid sessionId, Guid roomId, RoomEnterRequest request, string hash, CancellationToken ct) =>
        RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, roomId, null, async context =>
        {
            var replay = await ReplayAsync(context, request.RequestId, hash, ct);
            if (replay is not null) return replay;
            RoomAdmissionPolicy.RequireAssignment(context.State, request.ExpectedRoomId, request.ExpectedAssignmentVersion);
            var target = await AuthorizeTargetAsync(context, roomId, request, ct);
            var physical = await db.PetRoomStates.AsNoTracking().Where(s => s.RoomId == roomId).OrderBy(s => s.PetId)
                .Select(s => s.PetId).Take(13).ToArrayAsync(ct);
            if (physical.Length > 12) throw new AccountFault("room_occupancy_invalid", 503);
            if (target.Kind == PersistentRoomKind.PublicShared)
            {
                var occupancy = await OccupantsAsync(target, ct);
                if (context.State.RoomId != roomId && physical.Length >= target.Capacity)
                    throw new AccountFault(occupancy.Length < physical.Length ? "room_presence_retry" : "room_full", 409);
            }
            else if (context.State.RoomId != roomId && physical.Length >= target.Capacity) throw new AccountFault("room_full", 409);
            var sourceId = context.State.RoomId;
            await ReconcileAsync(context, ct);
            var now = clock.GetUtcNow();
            var observers = sourceId != roomId
                ? (await EffectiveAsync(context, ct)).Where(l => !(l.SessionId == sessionId && l.InstanceId == request.InstanceId)
                    && (target.Kind != PersistentRoomKind.DesktopPet || l.DeviceKind == "desktop")).ToArray() : [];
            if (observers.Length >= RoomAccessPersistence.MaximumDevices) throw new AccountFault("room_device_limit", 409);
            if (sourceId != roomId)
            {
                foreach (var lease in context.Leases.Where(l => l.RevokedAt == null)) RoomAccessPersistence.EndLease(lease, now);
            }
            else
            {
                foreach (var lease in context.Leases.Where(l => l.RevokedAt == null && l.SessionId == sessionId && l.InstanceId == request.InstanceId))
                    RoomAccessPersistence.EndLease(lease, now);
            }
            if (context.Leases.Count(l => l.RevokedAt == null) >= RoomAccessPersistence.MaximumDevices) throw new AccountFault("room_device_limit", 409);
            // Persist old instance revocation before inserting its replacement under the partial unique index.
            await db.SaveChangesAsync(ct);
            await checkpoint.ReachedAsync("after_old_leases", context.Pet.Id, ct);
            now = clock.GetUtcNow();
            foreach (var observer in observers.Where(l => l.ExpiresAt > now))
            {
                // Copy the old absolute deadline, never the old ID or transport. Other valid
                // devices recover their replacement lease via personal snapshot and attach.
                var replacement = new RoomDeviceLease
                {
                    Id = Guid.NewGuid(), PetId = context.Pet.Id, UserId = observer.UserId, SessionId = observer.SessionId,
                    InstanceId = observer.InstanceId, DeviceKind = observer.DeviceKind, RoomId = roomId,
                    CreatedAt = now, LastSeenAt = now, ExpiresAt = observer.ExpiresAt
                };
                context.Leases.Add(replacement); db.RoomDeviceLeases.Add(replacement);
            }
            var fresh = new RoomDeviceLease
            {
                Id = Guid.NewGuid(), PetId = context.Pet.Id, UserId = userId, SessionId = sessionId, InstanceId = request.InstanceId,
                RoomId = roomId, DeviceKind = request.DeviceKind, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.Add(RoomAccessPersistence.LeaseDuration)
            };
            context.Leases.Add(fresh); db.RoomDeviceLeases.Add(fresh);
            context.State.RoomId = roomId;
            if (sourceId != roomId) context.State.PositionX = context.State.PositionY = 0;
            context.State.PositionX ??= 0; context.State.PositionY ??= 0;
            context.State.IsSleeping = false; context.State.ControlLeaseId = fresh.Id;
            RoomAccessPersistence.AdvanceState(context.State);
            if (sourceId is { } source && source != roomId && context.Rooms.TryGetValue(source, out var old))
                await EventAsync(context, old, "left", ct);
            RoomAccessPersistence.AdvanceRoom(target);
            context.RequiredLeaseAtCommit = fresh.Id;
            var result = Result(context, target, "entered", fresh);
            await ReceiptAsync(context, target, "entered", request.RequestId, hash, result, ct);
            return result;
        }, ct);

    private async Task<PersistentRoom> AuthorizeTargetAsync(RoomAccessContext context, Guid roomId, RoomEnterRequest request, CancellationToken ct)
    {
        var room = context.Room(roomId);
        RoomAdmissionPolicy.RequireRoom(room, context.Pet.UserId, request.DeviceKind);
        if (room.Kind != PersistentRoomKind.PublicShared) return room;
        if (await db.RoomBans.AnyAsync(b => b.RoomId == roomId && b.UserId == context.Pet.UserId && b.Active, ct)) throw new AccountFault("room_banned", 403);
        if (room.OwnerId == context.Pet.UserId) return room;
        var digest = request.Code is null ? throw new AccountFault("room_code_required", 403) : RoomAdmissionPolicy.CodeDigest(request.Code);
        var credential = await db.PublicRoomCredentials.AsNoTracking().SingleOrDefaultAsync(c => c.RoomId == roomId && c.CodeDigest == digest
            && c.Generation == room.AdmissionGeneration && c.RecoveryEpoch == environment.RecoveryEpoch
            && c.RevokedAt == null && c.ExpiresAt > clock.GetUtcNow(), ct) ?? throw new AccountFault("room_code_rejected", 403);
        context.RequiredCredentialAtCommit = credential.Id;
        return room;
    }

    private Task<bool> ReconcileTargetPetAsync(Guid petId, Guid expectedRoomId, CancellationToken ct) =>
        RoomAccessPersistence.SystemAsync(db, environment, clock, new RoomAccessCheckpoint(), petId,
            context => context.State.RoomId == expectedRoomId ? ReconcileAsync(context, ct) : Task.FromResult(false), ct);

    public Task<RoomAccessResult> LeaveAsync(Guid userId, Guid sessionId, RoomControlRequest request, string? trustedTransportId, CancellationToken ct) =>
        ControlAsync(userId, sessionId, request, trustedTransportId, true, ct);

    public Task<RoomAccessResult> TakeoverAsync(Guid userId, Guid sessionId, RoomControlRequest request, string? trustedTransportId, CancellationToken ct) =>
        ControlAsync(userId, sessionId, request, trustedTransportId, false, ct);

    private async Task<RoomAccessResult> ControlAsync(Guid userId, Guid sessionId, RoomControlRequest request, string? transport, bool leave, CancellationToken ct)
    {
        RoomAdmissionPolicy.Control(request); RoomAdmissionPolicy.Transport(transport);
        var operation = leave ? "left" : "control";
        var hash = RoomAccessPersistence.Hash(new { operation, request });
        var outcome = await RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, null, null, async context =>
        {
            var replay = await ReplayAsync(context, request.RequestId, hash, ct);
            if (replay is not null) return Outcome<RoomAccessResult>.Success(replay);
            await ReconcileAsync(context, ct);
            RoomDeviceLease lease;
            try
            {
                lease = await RequiredLeaseAsync(context, new(request.LeaseId, request.InstanceId), transport, ct);
                RoomAdmissionPolicy.RequireControl(context.State, request, leave);
            }
            catch (AccountFault fault) when (fault.Status is 403 or 409) { return Outcome<RoomAccessResult>.Failure(fault); }
            // Only rejection of the stale command may commit lifecycle reconciliation. A failure
            // during the requested mutation must escape and roll the entire transaction back.
            var room = context.Room(request.ExpectedRoomId);
            if (leave) RoomAccessPersistence.ClearAssignment(context, clock.GetUtcNow());
            else { context.State.ControlLeaseId = lease.Id; RoomAccessPersistence.AdvanceState(context.State); context.RequiredLeaseAtCommit = lease.Id; }
            RoomAccessPersistence.AdvanceRoom(room);
            var result = Result(context, room, operation, leave ? null : lease);
            await ReceiptAsync(context, room, operation, request.RequestId, hash, result, ct);
            return Outcome<RoomAccessResult>.Success(result);
        }, ct);
        return outcome.Get();
    }

    public Task<RoomAccessResult> AttachAsync(Guid userId, Guid sessionId, RoomLeaseRequest request, string? transportId, CancellationToken ct) =>
        RefreshAsync(userId, sessionId, request, transportId, true, ct);

    public Task<RoomAccessResult> HeartbeatAsync(Guid userId, Guid sessionId, RoomLeaseRequest request, string? transportId, CancellationToken ct) =>
        RefreshAsync(userId, sessionId, request, transportId, false, ct);

    private async Task<RoomAccessResult> RefreshAsync(Guid userId, Guid sessionId, RoomLeaseRequest request, string? transport, bool attach, CancellationToken ct)
    {
        RoomAdmissionPolicy.Lease(request); RoomAdmissionPolicy.Transport(transport);
        var outcome = await RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, null, null, async context =>
        {
            await ReconcileAsync(context, ct);
            try
            {
                var lease = await RequiredLeaseAsync(context, request, attach ? null : transport, ct);
                if (attach && transport is not null) lease.TransportId = transport;
                var now = clock.GetUtcNow(); lease.LastSeenAt = now; lease.ExpiresAt = now.Add(RoomAccessPersistence.LeaseDuration);
                context.RequiredLeaseAtCommit = lease.Id;
                return Outcome<RoomAccessResult>.Success(Result(context, context.Room(lease.RoomId!.Value), attach ? "attached" : "heartbeat", lease));
            }
            catch (AccountFault fault) when (fault.Status is 403 or 409) { return Outcome<RoomAccessResult>.Failure(fault); }
        }, ct);
        return outcome.Get();
    }

    public async Task<RoomAccessResult> EndLeaseAsync(Guid userId, Guid sessionId, RoomLeaseEndRequest request, string? trustedTransportId, CancellationToken ct)
    {
        RoomAdmissionPolicy.End(request); RoomAdmissionPolicy.Transport(trustedTransportId);
        var hash = RoomAccessPersistence.Hash(new { operation = "lease_ended", request });
        var outcome = await RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, null, null, async context =>
        {
            var replay = await ReplayAsync(context, request.RequestId, hash, ct);
            if (replay is not null) return Outcome<RoomAccessResult>.Success(replay);
            var lease = context.Leases.SingleOrDefault(l => l.Id == request.LeaseId) ?? throw new AccountFault("room_lease_expired", 409);
            RoomAdmissionPolicy.RequireLeaseActor(lease, context.Pet, sessionId, request.InstanceId, trustedTransportId);
            var roomId = lease.RoomId ?? throw new AccountFault("room_lease_expired", 409);
            RoomAccessPersistence.EndLease(lease, clock.GetUtcNow());
            await ReconcileAsync(context, ct);
            var room = context.Room(roomId);
            RoomAccessPersistence.AdvanceRoom(room);
            var result = Result(context, room, "lease_ended", null);
            await ReceiptAsync(context, room, "lease_ended", request.RequestId, hash, result, ct);
            return Outcome<RoomAccessResult>.Success(result);
        }, ct);
        return outcome.Get();
    }

    public Task<PersonalRoomSnapshot> PersonalSnapshotAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, null, null, async context =>
        {
            await ReconcileAsync(context, ct);
            var effective = await EffectiveAsync(context, ct);
            var kind = context.State.RoomId is { } id ? Management.RoomManagementPolicy.Kind(context.Room(id).Kind) : null;
            return new PersonalRoomSnapshot(1, userId, context.Pet.Id, context.State.RoomId, kind, context.State.Version,
                context.State.ControlGeneration, context.State.ControlLeaseId, context.State.IsSleeping,
                effective.Where(l => l.SessionId == sessionId).Select(l => new RoomLeaseView(l.Id, l.InstanceId, l.DeviceKind,
                    context.State.ControlLeaseId == l.Id, l.ExpiresAt)).ToArray(), clock.GetUtcNow());
        }, ct);

    public async Task<SharedRoomSnapshot> SnapshotAsync(Guid userId, Guid sessionId, Guid roomId, RoomLeaseRequest request, string? transportId, CancellationToken ct)
    {
        RoomAdmissionPolicy.Id(roomId, "room_invalid"); RoomAdmissionPolicy.Lease(request); RoomAdmissionPolicy.Transport(transportId);
        var outcome = await RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, roomId, null, async context =>
        {
            await ReconcileAsync(context, ct);
            try
            {
                var lease = await RequiredLeaseAsync(context, request, transportId, ct);
                if (lease.RoomId != roomId) throw new AccountFault("room_access_forbidden", 403);
                var room = context.Room(roomId);
                RoomAdmissionPolicy.RequireRoom(room, userId, lease.DeviceKind);
                context.RequiredLeaseAtCommit = lease.Id;
                return Outcome<SharedRoomSnapshot>.Success(new(1, room.Id, room.Version, Management.RoomManagementPolicy.Kind(room.Kind), room.Name,
                    room.Capacity, room.TemplateId, room.TemplateVersion, await OccupantsAsync(room, ct), clock.GetUtcNow()));
            }
            catch (AccountFault fault) when (fault.Status is 403 or 409) { return Outcome<SharedRoomSnapshot>.Failure(fault); }
        }, ct);
        return outcome.Get();
    }

    public Task<bool> ReconcilePetAsync(Guid petId, CancellationToken ct)
    {
        RoomAdmissionPolicy.Id(petId, "pet_invalid");
        return RoomAccessPersistence.SystemAsync(db, environment, clock, checkpoint, petId, context => ReconcileAsync(context, ct), ct);
    }

    private async Task<bool> ReconcileAsync(RoomAccessContext context, CancellationToken ct)
    {
        var effective = await EffectiveAsync(context, ct);
        var now = clock.GetUtcNow();
        var changed = false;
        foreach (var lease in context.Leases.Where(l => l.RevokedAt == null && !effective.Contains(l)))
        { RoomAccessPersistence.EndLease(lease, now); changed = true; }
        if (context.State.RoomId is not { } roomId)
        {
            if (context.State.ControlLeaseId is not null) throw new AccountFault("room_lease_invalid_state", 503);
            return changed;
        }
        var room = context.Room(roomId);
        var visible = false;
        if (room.Status != PersistentRoomStatus.Active || effective.Count == 0 && room.Kind == PersistentRoomKind.PublicShared)
        { RoomAccessPersistence.ClearAssignment(context, now); visible = true; }
        else if (effective.Count == 0)
        {
            if (!context.State.IsSleeping || context.State.ControlLeaseId is not null)
            {
                context.State.IsSleeping = true; context.State.ControlLeaseId = null;
                RoomAccessPersistence.AdvanceState(context.State); visible = true;
            }
        }
        else if (context.State.ControlLeaseId is { } control && !effective.Any(l => l.Id == control))
        { context.State.ControlLeaseId = null; RoomAccessPersistence.AdvanceState(context.State); visible = true; }
        if (visible) await EventAsync(context, room, "offline", ct);
        return changed || visible;
    }

    private async Task<List<RoomDeviceLease>> EffectiveAsync(RoomAccessContext context, CancellationToken ct)
    {
        foreach (var lease in context.Leases) RoomAdmissionPolicy.RequireLeaseIdentity(lease, context.Pet);
        var sessions = context.Leases.Select(l => l.SessionId).Distinct().ToArray();
        var related = await db.AuthSessions.AsNoTracking().Where(s => sessions.Contains(s.Id)).ToArrayAsync(ct);
        if (context.Leases.Any(l => !related.Any(s => s.Id == l.SessionId && s.UserId == context.Pet.UserId)))
            throw new AccountFault("room_lease_invalid_state", 503);
        return await RoomAccessPersistence.EffectiveLeasesAsync(db, context, environment, clock, ct);
    }

    private async Task<RoomDeviceLease> RequiredLeaseAsync(RoomAccessContext context, RoomLeaseRequest request, string? transport, CancellationToken ct)
    {
        var lease = context.Leases.SingleOrDefault(l => l.Id == request.LeaseId) ?? throw new AccountFault("room_lease_expired", 409);
        RoomAdmissionPolicy.RequireLeaseActor(lease, context.Pet, context.SessionId!.Value, request.InstanceId, transport);
        if (!(await EffectiveAsync(context, ct)).Contains(lease) || context.State.IsSleeping) throw new AccountFault("room_lease_expired", 409);
        return lease;
    }

    internal async Task<RoomOccupantView[]> OccupantsAsync(PersistentRoom room, CancellationToken ct)
    {
        var rows = await (from state in db.PetRoomStates.AsNoTracking()
                          join pet in db.Pets.AsNoTracking() on state.PetId equals pet.Id
                          where state.RoomId == room.Id
                          orderby state.PetId
                          select new { State = state, Pet = pet }).Take(RoomAdmissionPolicy.MaximumOccupants + 1).ToArrayAsync(ct);
        if (rows.Length > RoomAdmissionPolicy.MaximumOccupants) throw new AccountFault("room_occupancy_invalid", 503);
        var perPetLeases = new Dictionary<Guid, List<RoomDeviceLease>>();
        var sessionFacts = new Dictionary<Guid, (AuthSession Session, DangoUser User)>();
        foreach (var row in rows)
        {
            var leases = await db.RoomDeviceLeases.AsNoTracking().Where(l => l.PetId == row.Pet.Id && l.RevokedAt == null)
                .OrderBy(l => l.Id).Take(RoomAccessPersistence.MaximumDevices + 1).ToListAsync(ct);
            if (leases.Count > RoomAccessPersistence.MaximumDevices) throw new AccountFault("room_device_limit", 409);
            foreach (var lease in leases) RoomAdmissionPolicy.RequireLeaseIdentity(lease, row.Pet);
            var ids = leases.Select(l => l.SessionId).Distinct().ToArray();
            var sessions = await (from session in db.AuthSessions.AsNoTracking()
                                  join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                                  where ids.Contains(session.Id)
                                  select new { Session = session, User = user }).ToArrayAsync(ct);
            if (leases.Any(l => !sessions.Any(s => s.Session.Id == l.SessionId && s.User.Id == row.Pet.UserId))) throw new AccountFault("room_lease_invalid_state", 503);
            perPetLeases.Add(row.Pet.Id, leases);
            foreach (var session in sessions) sessionFacts[session.Session.Id] = (session.Session, session.User);
        }
        // All rows use the clock after the bounded reads, rather than preserving an earlier
        // visitor as present when its lease expires while later occupants are being read.
        var now = clock.GetUtcNow();
        var output = new List<RoomOccupantView>();
        foreach (var row in rows)
        {
            var alive = perPetLeases[row.Pet.Id].Any(l => l.RoomId == room.Id && l.ExpiresAt > now && (room.Kind != PersistentRoomKind.DesktopPet || l.DeviceKind == "desktop")
                && sessionFacts.TryGetValue(l.SessionId, out var session) && AccountSessionPolicy.IsValid(session.User, session.Session, environment.RecoveryEpoch, now));
            if (room.Kind == PersistentRoomKind.PublicShared && !alive) continue;
            if (row.State.PositionX is not { } x || row.State.PositionY is not { } y || !double.IsFinite(x) || !double.IsFinite(y))
                throw new AccountFault("room_assignment_invalid_state", 503);
            output.Add(new(row.Pet.Id, row.Pet.UserId, row.Pet.ConfigurationVersion, PetConfigurationPolicy.ReadPersisted(row.Pet.Configuration, row.Pet.ConfigurationVersion),
                x, y, !alive || row.State.IsSleeping, row.State.Version));
        }
        if (output.Count > room.Capacity) throw new AccountFault("room_occupancy_invalid", 503);
        return output.ToArray();
    }

    private Task EventAsync(RoomAccessContext context, PersistentRoom room, string operation, CancellationToken ct)
    {
        RoomAccessPersistence.AdvanceRoom(room);
        // Personal refresh authorization is only for the pet affected by this operation.
        // General observers must pass the dispatcher's current room/lease authorization.
        RoomAccessPersistence.Append(db, room, operation, clock.GetUtcNow(), [context.Pet.UserId]);
        return Task.CompletedTask;
    }
    private Task ReceiptAsync(RoomAccessContext context, PersistentRoom room, string operation, Guid requestId, string hash, RoomAccessResult result, CancellationToken ct)
    {
        RoomAccessPersistence.Append(db, room, operation, clock.GetUtcNow(), [context.Pet.UserId], context, requestId, hash, RoomAccessPersistence.Serialize(result));
        return Task.CompletedTask;
    }
    private Task<RoomAccessResult?> ReplayAsync(RoomAccessContext context, Guid requestId, string hash, CancellationToken ct) => ReplayCoreAsync(context, requestId, hash, ct);
    private async Task<RoomAccessResult?> ReplayCoreAsync(RoomAccessContext context, Guid requestId, string hash, CancellationToken ct)
    {
        var receipt = await RoomAccessPersistence.ReplayAsync(db, context, requestId, hash, ct);
        return receipt is null ? null : RoomAccessPersistence.Deserialize<RoomAccessResult>(receipt.ResultJson);
    }
    private RoomAccessResult Result(RoomAccessContext context, PersistentRoom room, string operation, RoomDeviceLease? lease) =>
        new(context.Pet.Id, context.State.RoomId, lease?.Id, context.State.Version, context.State.ControlGeneration, room.Version, operation,
            clock.GetUtcNow(), lease?.ExpiresAt);
    private sealed record Outcome<T>(T? Value, AccountFault? Fault)
    {
        public static Outcome<T> Success(T value) => new(value, null);
        public static Outcome<T> Failure(AccountFault fault) => new(default, fault);
        public T Get() => Fault is not null ? throw Fault : Value!;
    }
    private sealed record Preparation(RoomAccessResult? Receipt, Guid[] PetIds);
}
