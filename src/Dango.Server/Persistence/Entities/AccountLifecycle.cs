namespace Dango.Server.Persistence.Entities;

public enum RegistrationState { Pending, Verified, Completed }
public enum EmailChallengePurpose { Registration, PasswordReset }
public enum EmailJobState { Pending, Leased, Accepted, Failed, Cancelled }

public sealed class TestInvitation
{
    public Guid Id { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public string SecretDigest { get; set; } = string.Empty;
    public Guid RecoveryEpoch { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? UserId { get; set; }
}

public sealed class PendingRegistration
{
    public Guid Id { get; set; }
    public Guid RequestKey { get; set; }
    public string ResumeDigest { get; set; } = string.Empty;
    public Guid InvitationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public Guid RecoveryEpoch { get; set; }
    public RegistrationState State { get; set; }
    public int Generation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSentAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? CompletionExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletionKey { get; set; }
    public Guid? UserId { get; set; }
    public Guid? PetId { get; set; }
    public Guid? RoomId { get; set; }
}

public sealed class EmailChallenge
{
    public Guid Id { get; set; }
    public EmailChallengePurpose Purpose { get; set; }
    public Guid? PendingRegistrationId { get; set; }
    public Guid? UserId { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? SecurityStamp { get; set; }
    public Guid RecoveryEpoch { get; set; }
    public int Generation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class EmailJob
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public EmailJobState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? FailureCode { get; set; }
}

public sealed class AuthSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public Guid RecoveryEpoch { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool Remembered { get; set; }
}
