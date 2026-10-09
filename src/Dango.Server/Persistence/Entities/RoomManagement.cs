namespace Dango.Server.Persistence.Entities;

// No room FK: audit/acknowledgements must survive permanent removal of the business row.
public sealed class RoomOperationAudit
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? RequestId { get; set; }
    public string? RequestHash { get; set; }
    public Guid RoomId { get; set; }
    public long AppliedVersion { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? BoundSessionId { get; set; }
    public string? ResultJson { get; set; }
}

// Durable notification intent only. B4 implements dispatch and recipient reauthorization.
public sealed class RoomDomainEvent
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public long RoomVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid[] TargetUserIds { get; set; } = [];
}
