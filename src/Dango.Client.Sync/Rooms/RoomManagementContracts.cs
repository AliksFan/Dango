using Dango.Protocol;

namespace Dango.Client.Sync.Rooms;

// Management receipts acknowledge the original operation. ReadAsync supplies current detail.
// The caller owns request IDs and reconciliation; this client never retries a mutation.
public interface IRoomManagementClient
{
    Uri Origin { get; }
    Guid UserId { get; }
    Guid PetId { get; }
    Task<RoomManagementPage> ListAsync(string state, Guid? after, int limit, CancellationToken ct);
    Task<RoomManagementView> ReadAsync(Guid roomId, CancellationToken ct);
    Task<RoomOperationResult> CreateAsync(RoomCreateRequest request, CancellationToken ct);
    Task<RoomOperationResult> UpdateAsync(Guid roomId, RoomUpdateRequest request, CancellationToken ct);
    Task<RoomOperationResult> DissolveAsync(Guid roomId, RoomDissolveRequest request, CancellationToken ct);
    Task<RoomOperationResult> RestoreAsync(Guid roomId, RoomRestoreRequest request, CancellationToken ct);
}

public enum RoomManagementFailure { Authentication, Conflict, Unavailable, InvalidResponse, Rejected }

public sealed class RoomManagementClientException(RoomManagementFailure failure, string code) : Exception(code)
{
    public RoomManagementFailure Failure { get; } = failure;
    public string Code { get; } = code;
}
