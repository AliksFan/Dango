using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;

namespace Dango.Server.Rooms.Access;

public static class RoomAdmissionPolicy
{
    public const int MaximumOccupants = 12;
    public const int MaximumMaintenanceBatch = 50;
    public static void Id(Guid value, string code = "room_request_invalid")
    {
        if (value == Guid.Empty) throw new AccountFault(code);
    }
    public static void Device(string? device)
    {
        if (device is not ("desktop" or "mobile")) throw new AccountFault("room_device_invalid");
    }
    public static void Assignment(Guid? roomId, long version)
    {
        if (roomId == Guid.Empty || version < 1) throw new AccountFault("room_assignment_invalid");
    }
    public static void Enter(RoomEnterRequest request)
    {
        Id(request.RequestId); Id(request.InstanceId, "room_instance_invalid"); Device(request.DeviceKind);
        Assignment(request.ExpectedRoomId, request.ExpectedAssignmentVersion);
        if (request.Code is not null) _ = CodeDigest(request.Code);
    }
    public static void Lease(RoomLeaseRequest request)
    {
        Id(request.LeaseId, "room_lease_invalid"); Id(request.InstanceId, "room_instance_invalid");
    }
    public static void Control(RoomControlRequest request)
    {
        Id(request.RequestId); Lease(new(request.LeaseId, request.InstanceId)); Id(request.ExpectedRoomId, "room_assignment_invalid");
        Assignment(request.ExpectedRoomId, request.ExpectedAssignmentVersion);
        if (request.ExpectedControlGeneration < 0) throw new AccountFault("room_control_invalid");
    }
    public static void End(RoomLeaseEndRequest request)
    {
        Id(request.RequestId); Lease(new(request.LeaseId, request.InstanceId));
    }
    public static string CodeDigest(string? code)
    {
        try { return AccountInput.Digest(code); }
        catch (AccountFault) { throw new AccountFault("room_code_invalid"); }
    }
    public static void Transport(string? id)
    {
        if (id is not null && (id.Length is < 1 or > 128 || id.Any(char.IsControl))) throw new AccountFault("room_transport_invalid");
    }
    public static void RequireAssignment(PetRoomState state, Guid? roomId, long version)
    {
        if (state.RoomId != roomId || state.Version != version) throw new AccountFault("room_assignment_conflict", 409);
    }
    public static void RequireControl(PetRoomState state, RoomControlRequest request, bool requireCurrentController)
    {
        RequireAssignment(state, request.ExpectedRoomId, request.ExpectedAssignmentVersion);
        if (state.ControlGeneration != request.ExpectedControlGeneration || (requireCurrentController && state.ControlLeaseId != request.LeaseId))
            throw new AccountFault("room_control_conflict", 409);
    }
    public static void RequireLeaseIdentity(RoomDeviceLease lease, AccountPet pet)
    {
        if (lease.PetId != pet.Id || lease.UserId != pet.UserId || lease.InstanceId == Guid.Empty || lease.SessionId == Guid.Empty)
            throw new AccountFault("room_lease_invalid_state", 503);
        if (lease.DeviceKind is not ("desktop" or "mobile")) throw new AccountFault("room_lease_invalid_state", 503);
    }
    public static void RequireLeaseActor(RoomDeviceLease lease, AccountPet pet, Guid sessionId, Guid instanceId, string? transport)
    {
        RequireLeaseIdentity(lease, pet);
        if (lease.SessionId != sessionId || lease.InstanceId != instanceId) throw new AccountFault("room_lease_forbidden", 403);
        if (transport is not null && lease.TransportId != transport) throw new AccountFault("room_transport_conflict", 409);
    }
    public static void RequireRoom(PersistentRoom room, Guid userId, string device)
    {
        if (room.Status != PersistentRoomStatus.Active) throw new AccountFault("room_inactive", 409);
        if (room.Kind == PersistentRoomKind.DesktopPet && device != "desktop") throw new AccountFault("room_device_forbidden", 403);
        if (room.Kind != PersistentRoomKind.PublicShared && room.OwnerId != userId) throw new AccountFault("room_access_forbidden", 403);
    }
    public static void Batch(int limit, IReadOnlyCollection<Guid>? scope)
    {
        if (limit is < 1 or > MaximumMaintenanceBatch || scope is not null && (scope.Count is < 1 or > MaximumMaintenanceBatch || scope.Any(id => id == Guid.Empty)))
            throw new AccountFault("room_maintenance_scope_invalid");
    }
}
