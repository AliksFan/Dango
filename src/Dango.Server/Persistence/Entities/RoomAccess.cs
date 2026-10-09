namespace Dango.Server.Persistence.Entities;

public sealed class RoomDeviceLease
{
    public Guid Id { get; set; }
    public Guid PetId { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid? RoomId { get; set; }
    public string DeviceKind { get; set; } = string.Empty;
    public string? TransportId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PublicRoomCredential
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid RecoveryEpoch { get; set; }
    public long Generation { get; set; }
    public string CodeDigest { get; set; } = string.Empty;
    // Only Data Protection ciphertext is persisted, for replay of the owner's reset acknowledgement.
    public string ProtectedCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class RoomBan
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public bool Active { get; set; }
    public long Version { get; set; }
}
