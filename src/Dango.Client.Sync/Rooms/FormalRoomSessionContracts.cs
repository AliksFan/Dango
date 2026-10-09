using Dango.Protocol;

namespace Dango.Client.Sync.Rooms;

public enum FormalRoomSessionPhase
{
    Idle, Reading, Mutating, Connecting, AwaitingPresentation, Presented,
    Disconnected, ReentryRequired, ReconciliationRequired, Conflict, Unknown, Failed, Suspended, Reset, Disposed
}

public enum FormalRoomRole { None, Observe, Control }
public enum FormalRoomOperation { Enter, TakeControl, Leave, EndLease }
public sealed record FormalRoomPendingOperation(FormalRoomOperation Operation, Guid RequestId);

// Only a session can issue this capability. The real scene host must confirm its healthy
// composition frame; a successful request or an attached transport cannot confirm it.
public sealed class FormalRoomPresentationToken
{
    internal FormalRoomPresentationToken(long epoch, long transportGeneration, Guid leaseId, Guid roomId,
        long assignmentVersion, long controlGeneration, long roomVersion)
    {
        Epoch = epoch; TransportGeneration = transportGeneration; LeaseId = leaseId; RoomId = roomId;
        AssignmentVersion = assignmentVersion; ControlGeneration = controlGeneration; RoomVersion = roomVersion;
    }

    public long Epoch { get; }
    public long TransportGeneration { get; }
    public Guid LeaseId { get; }
    public Guid RoomId { get; }
    public long AssignmentVersion { get; }
    public long ControlGeneration { get; }
    public long RoomVersion { get; }
}

public sealed record FormalRoomSessionState(Guid UserId, Guid PetId, Guid InstanceId, long Epoch,
    FormalRoomSessionPhase Phase, PersonalRoomSnapshot? Personal, SharedRoomSnapshot? Shared,
    FormalRoomRole Role, bool ServerAttached, FormalRoomPresentationToken? PresentationToken,
    FormalRoomPendingOperation? Pending, string? FailureCode)
{
    public bool Presented => Phase == FormalRoomSessionPhase.Presented && ServerAttached;
    public bool RequiresReconciliation { get; init; }
}
