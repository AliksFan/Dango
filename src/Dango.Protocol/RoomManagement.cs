namespace Dango.Protocol;

public sealed record RoomCreateRequest(Guid RequestId, string Kind, string Name, int Capacity = 6);
public sealed record RoomUpdateRequest(Guid RequestId, long ExpectedVersion, string Name, int Capacity);
public sealed record RoomDissolveRequest(Guid RequestId, long ExpectedVersion, bool Confirmed);
public sealed record RoomRestoreRequest(Guid RequestId, long ExpectedVersion);

// A replay acknowledges the original committed operation, not the room's current state. Read the detail again.
public sealed record RoomOperationResult(Guid RoomId, long AppliedVersion, string Operation);
public sealed record RoomManagementView(Guid RoomId, string Kind, string Name, int Capacity, string Status,
    long Version, int ReservedSeats, int Occupancy, DateTimeOffset? RecoverUntil, bool Recoverable,
    string TemplateId, int TemplateVersion);
public sealed record RoomManagementPage(RoomManagementView[] Rooms, Guid? Next);
