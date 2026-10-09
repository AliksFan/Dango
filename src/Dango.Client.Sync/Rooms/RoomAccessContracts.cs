using Dango.Protocol;

namespace Dango.Client.Sync.Rooms;

public interface IRoomAccessClient
{
    Uri Origin { get; }
    Guid UserId { get; }
    Guid PetId { get; }
    Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct);
    Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct);
    Task<RoomAccessResult> EnterAsync(Guid roomId, RoomEnterRequest request, CancellationToken ct);
    Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct);
    Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct);
    Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct);
    Task<RoomDoorView> ResolveAsync(RoomCodeResolveRequest request, CancellationToken ct);
    Task<RoomCodeResult> ResetCodeAsync(Guid roomId, RoomCodeResetRequest request, CancellationToken ct);
    IFormalRoomConnection CreateConnection();
}

// This Hub reports authoritative snapshots and leases. It has no Move or Action contract.
// Reconnection and lease recovery belong to the lifecycle owner, using a new connection.
public interface IFormalRoomConnection : IAsyncDisposable
{
    event Action<RoomChangeNotice>? RoomChanged;
    event Action? Closed;
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<RoomAccessResult> AttachAsync(RoomLeaseRequest request, CancellationToken ct);
    Task<RoomAccessResult> HeartbeatAsync(RoomLeaseRequest request, CancellationToken ct);
    Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct);
    Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct);
    Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct);
    Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct);
    Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct);
}

public enum RoomAccessFailure { Authentication, Conflict, Unavailable, InvalidResponse, Rejected }

public sealed class RoomAccessClientException(RoomAccessFailure failure, string code) : Exception(code)
{
    public RoomAccessFailure Failure { get; } = failure;
    public string Code { get; } = code;
}
