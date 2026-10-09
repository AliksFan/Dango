using Microsoft.AspNetCore.Identity;

namespace Dango.Server.Persistence.Entities;

public sealed class DangoUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public bool RecoveryLocked { get; set; }
}

public sealed class AccountPet
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Configuration { get; set; } = string.Empty;
    public long ConfigurationVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum PersistentRoomKind { DesktopPet = 0, PrivateShared = 1, PublicShared = 2 }
public enum PersistentRoomStatus { Active = 0, Dissolved = 1, Expired = 2 }
public enum PersistentRoomRole { Owner = 0, Member = 1 }

public sealed class PersistentRoom
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public PersistentRoomKind Kind { get; set; }
    public PersistentRoomStatus Status { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string TemplateId { get; set; } = "ordinary-default";
    public int TemplateVersion { get; set; } = 1;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DissolvedAt { get; set; }
    public DateTimeOffset? RecoverUntil { get; set; }
}

public sealed class PersistentRoomMembership
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public PersistentRoomRole Role { get; set; }
}

// Sole authority for assignment. A login or a connection is not a second occupancy record.
public sealed class PetRoomState
{
    public Guid PetId { get; set; }
    public Guid? RoomId { get; set; }
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public bool IsSleeping { get; set; }
    public long Version { get; set; }
    public long ControlGeneration { get; set; }
}
