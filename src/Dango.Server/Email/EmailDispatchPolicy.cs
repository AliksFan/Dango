using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;

namespace Dango.Server.Email;

public static class EmailDispatchPolicy
{
    public const int BatchSize = 10;
    public const int MaximumAttempts = 5;
    // Covers the sender's maximum 120-second attempt plus bounded cleanup and state persistence.
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);

    public static bool IsEligible(EmailChallenge challenge, PendingRegistration? pending, TestInvitation? invitation,
        DangoUser? user, Guid epoch, DateTimeOffset now)
    {
        if (epoch == Guid.Empty || challenge.Id == Guid.Empty || challenge.RecoveryEpoch != epoch
            || challenge.Generation < 1 || challenge.CreatedAt > now || challenge.ExpiresAt <= now
            || challenge.ExpiresAt > challenge.CreatedAt.AddMinutes(30)
            || challenge.RevokedAt is not null || challenge.ConsumedAt is not null) return false;
        return challenge.Purpose switch
        {
            EmailChallengePurpose.Registration => challenge.UserId is null && challenge.SecurityStamp is null
                && pending is not null && invitation is not null && user is null
                && pending.Id == challenge.PendingRegistrationId && pending.State == RegistrationState.Pending
                && pending.RecoveryEpoch == epoch && pending.Generation == challenge.Generation
                && pending.NormalizedEmail == challenge.NormalizedEmail && ValidRecipient(pending.Email, challenge.NormalizedEmail)
                && pending.ExpiresAt > now && challenge.ExpiresAt <= pending.ExpiresAt
                && pending.InvitationId == invitation.Id && invitation.RecoveryEpoch == epoch
                && invitation.NormalizedEmail == challenge.NormalizedEmail && invitation.ExpiresAt > now
                && challenge.ExpiresAt <= invitation.ExpiresAt && invitation.RevokedAt is null && invitation.ConsumedAt is null,
            EmailChallengePurpose.PasswordReset => challenge.PendingRegistrationId is null
                && user is not null && pending is null && invitation is null && user.Id == challenge.UserId
                && user.EmailConfirmed && user.DisabledAt is null && !user.RecoveryLocked
                && user.NormalizedEmail == challenge.NormalizedEmail && ValidRecipient(user.Email, challenge.NormalizedEmail)
                && user.SecurityStamp is { Length: > 0 } && user.SecurityStamp == challenge.SecurityStamp,
            _ => false
        };
    }

    // Never retain an external server message or an adapter's arbitrary error code in the database.
    public static bool ApplyResult(EmailJob job, Guid leaseId, EmailSendResult result, DateTimeOffset now)
    {
        if (job.State != EmailJobState.Leased || job.LeaseId != leaseId) return false;
        job.LeaseId = null;
        job.LeaseUntil = null;
        job.AcceptedAt = null;
        switch (result.Status)
        {
            case EmailSendStatus.Accepted:
                job.State = EmailJobState.Accepted;
                job.AcceptedAt = now;
                job.FailureCode = null;
                break;
            case EmailSendStatus.NotConfigured:
                job.State = EmailJobState.Pending;
                job.Attempts = Math.Max(0, job.Attempts - 1);
                job.DueAt = now.AddMinutes(5);
                job.FailureCode = "smtp_not_configured";
                break;
            case EmailSendStatus.PermanentFailure:
                job.State = EmailJobState.Failed;
                job.FailureCode = "smtp_permanent";
                break;
            default:
                job.State = job.Attempts >= MaximumAttempts ? EmailJobState.Failed : EmailJobState.Pending;
                job.DueAt = now.AddSeconds(30 * (1 << Math.Clamp(job.Attempts - 1, 0, 4)));
                job.FailureCode = job.State == EmailJobState.Failed ? "smtp_attempt_limit" : "smtp_transient";
                break;
        }
        return true;
    }

    private static bool ValidRecipient(string? email, string normalized)
    {
        try { return AccountInput.Email(email).Normalized == normalized; }
        catch (AccountFault) { return false; }
    }
}
