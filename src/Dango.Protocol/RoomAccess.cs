using System.Text.Json;

namespace Dango.Protocol;

public sealed record RoomEnterRequest(Guid RequestId, Guid InstanceId, string DeviceKind,
    Guid? ExpectedRoomId, long ExpectedAssignmentVersion, string? Code = null);
public sealed record RoomLeaseRequest(Guid LeaseId, Guid InstanceId);
public sealed record RoomControlRequest(Guid RequestId, Guid LeaseId, Guid InstanceId,
    Guid ExpectedRoomId, long ExpectedAssignmentVersion, long ExpectedControlGeneration);
public sealed record RoomLeaseEndRequest(Guid RequestId, Guid LeaseId, Guid InstanceId);
public sealed record RoomCodeResolveRequest(string Code);
public sealed record RoomCodeResetRequest(Guid RequestId, long ExpectedVersion, int LifetimeHours = 24);
public sealed record RoomModerationRequest(Guid RequestId, long ExpectedVersion);
public sealed record RoomAccessResult(Guid PetId, Guid? RoomId, Guid? LeaseId, long AssignmentVersion,
    long ControlGeneration, long RoomVersion, string Operation, DateTimeOffset ServerTime, DateTimeOffset? LeaseExpiresAt);
public sealed record RoomCodeResult(Guid RoomId, Guid CredentialId, long RoomVersion, long Generation,
    string Code, DateTimeOffset ExpiresAt);
public sealed record RoomDoorView(Guid RoomId, string Kind, string Name, int Capacity, long Version,
    string TemplateId, int TemplateVersion);
public sealed record RoomLeaseView(Guid LeaseId, Guid InstanceId, string DeviceKind,
    bool Controls, DateTimeOffset ExpiresAt);
public sealed record PersonalRoomSnapshot(int SchemaVersion, Guid UserId, Guid PetId, Guid? RoomId,
    string? RoomKind, long AssignmentVersion, long ControlGeneration, Guid? ControlLeaseId, bool IsSleeping,
    RoomLeaseView[] Leases, DateTimeOffset ServerTime);
public sealed record RoomOccupantView(Guid PetId, Guid UserId, long ConfigurationVersion, JsonElement Configuration,
    double PositionX, double PositionY, bool IsSleeping, long AssignmentVersion);
public sealed record SharedRoomSnapshot(int SchemaVersion, Guid RoomId, long RoomVersion, string Kind,
    string Name, int Capacity, string TemplateId, int TemplateVersion, RoomOccupantView[] Occupants,
    DateTimeOffset ServerTime);
public sealed record RoomChangeNotice(Guid EventId, Guid RoomId, long RoomVersion, string EventType);
