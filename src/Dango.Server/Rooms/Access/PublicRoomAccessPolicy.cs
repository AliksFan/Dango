using System.Security.Cryptography;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.WebUtilities;

namespace Dango.Server.Rooms.Access;

// A reset receipt deliberately cannot carry a plaintext code.
public sealed record PublicRoomCodeReceipt(Guid RoomId, Guid CredentialId, long RoomVersion, long Generation, DateTimeOffset ExpiresAt);

public static class PublicRoomAccessPolicy
{
    public static string NewCode() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string CodeDigest(string? code)
    {
        try { return AccountInput.Digest(code); }
        catch (AccountFault) { throw new AccountFault("room_code_invalid", 404); }
    }

    public static void Request(Guid requestId)
    {
        if (requestId == Guid.Empty) throw new AccountFault("room_request_invalid");
    }

    public static void Lifetime(int hours)
    {
        if (hours is < 1 or > 168) throw new AccountFault("room_code_lifetime_invalid");
    }

    public static void ExpectedVersion(PersistentRoom room, long expectedVersion)
    {
        if (expectedVersion < 1) throw new AccountFault("room_version_invalid");
        if (room.Version != expectedVersion) throw new AccountFault("room_version_conflict", 409);
    }

    public static void OwnedPublic(PersistentRoom room, Guid userId, bool requireActive = true)
    {
        if (room.OwnerId != userId) throw new AccountFault("room_not_found", 404);
        if (room.Kind != PersistentRoomKind.PublicShared) throw new AccountFault("room_public_required", 409);
        if (requireActive && room.Status != PersistentRoomStatus.Active) throw new AccountFault("room_state_conflict", 409);
    }

    public static void Target(Guid ownerId, Guid targetUserId)
    {
        if (targetUserId == Guid.Empty || ownerId == targetUserId) throw new AccountFault("room_moderation_target_invalid");
    }

    public static bool CredentialValid(PersistentRoom room, PublicRoomCredential credential, Guid recoveryEpoch, DateTimeOffset now) =>
        room.Kind == PersistentRoomKind.PublicShared && room.Status == PersistentRoomStatus.Active
        && credential.RoomId == room.Id && credential.Generation == room.AdmissionGeneration
        && credential.RecoveryEpoch == recoveryEpoch && recoveryEpoch != Guid.Empty
        && credential.RevokedAt is null && credential.CreatedAt <= now && credential.ExpiresAt > now;

    public static RoomDoorView Door(PersistentRoom room) => new(room.Id, "publicShared", room.Name, room.Capacity,
        room.Version, room.TemplateId, room.TemplateVersion);

    public static PublicRoomCodeReceipt Receipt(PublicRoomCredential credential, long roomVersion) =>
        new(credential.RoomId, credential.Id, roomVersion, credential.Generation, credential.ExpiresAt);

    public static void ValidateReceipt(PublicRoomCodeReceipt receipt, RoomOperationAudit audit, PublicRoomCredential credential)
    {
        if (audit.Operation != "code_reset" || receipt.RoomId == Guid.Empty || receipt.CredentialId == Guid.Empty
            || receipt.RoomVersion < 1 || receipt.Generation < 1 || audit.RoomId != receipt.RoomId
            || audit.AppliedVersion != receipt.RoomVersion || credential.Id != receipt.CredentialId
            || credential.RoomId != receipt.RoomId || credential.Generation != receipt.Generation
            || credential.ExpiresAt != receipt.ExpiresAt) throw new AccountFault("room_receipt_invalid", 503);
    }
}
