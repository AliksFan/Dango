using Dango.Server.Email;
using Dango.Server.Persistence.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Email;

[TestClass]
public sealed class EmailDispatchPolicyTests
{
    [TestMethod]
    public void LeaseCoversTheLargestConfiguredSendDeadlineAndCleanup()
    {
        var options = SmtpOptions.ForLoopbackTest(2525, timeoutMilliseconds: 60_000, attemptTimeoutMilliseconds: 120_000);
        Assert.IsTrue(EmailDispatchPolicy.LeaseDuration > TimeSpan.FromMilliseconds(options.AttemptTimeoutMilliseconds + 3_000));
    }

    [TestMethod]
    public void RegistrationRequiresCurrentPendingInvitationAndChallenge()
    {
        var fixture = Registration();
        Assert.IsTrue(fixture.Eligible());
        fixture.Invitation.RevokedAt = fixture.Now;
        Assert.IsFalse(fixture.Eligible());
        fixture.Invitation.RevokedAt = null;
        fixture.Invitation.ConsumedAt = fixture.Now;
        Assert.IsFalse(fixture.Eligible());
        fixture.Invitation.ConsumedAt = null;
        fixture.Pending.Generation++;
        Assert.IsFalse(fixture.Eligible());
        fixture.Pending.Generation--;
        fixture.Pending.State = RegistrationState.Verified;
        Assert.IsFalse(fixture.Eligible());
        fixture.Pending.State = RegistrationState.Pending;
        fixture.Pending.RecoveryEpoch = Guid.NewGuid();
        Assert.IsFalse(fixture.Eligible());
    }

    [TestMethod]
    public void EveryExpiredOrWrongSubjectRegistrationIsRejected()
    {
        var fixture = Registration();
        fixture.Invitation.ExpiresAt = fixture.Now;
        Assert.IsFalse(fixture.Eligible());
        fixture = Registration();
        fixture.Challenge.PendingRegistrationId = Guid.NewGuid();
        Assert.IsFalse(fixture.Eligible());
        fixture = Registration();
        fixture.Challenge.ExpiresAt = fixture.Now;
        Assert.IsFalse(fixture.Eligible());
        fixture = Registration();
        fixture.Challenge.ConsumedAt = fixture.Now;
        Assert.IsFalse(fixture.Eligible());
        fixture = Registration();
        fixture.Pending.Email = "different@example.invalid";
        Assert.IsFalse(fixture.Eligible());
    }

    [TestMethod]
    public void ResetCannotSendForDisabledRecoveryLockedOrChangedStampAccounts()
    {
        var now = DateTimeOffset.UtcNow;
        var epoch = Guid.NewGuid();
        var user = new DangoUser { Id = Guid.NewGuid(), Email = "fictional@example.invalid", NormalizedEmail = "FICTIONAL@EXAMPLE.INVALID", EmailConfirmed = true, SecurityStamp = "fictional-stamp" };
        var challenge = new EmailChallenge { Id = Guid.NewGuid(), Purpose = EmailChallengePurpose.PasswordReset, UserId = user.Id, RecoveryEpoch = epoch, NormalizedEmail = user.NormalizedEmail, SecurityStamp = user.SecurityStamp, Generation = 1, CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
        bool Eligible() => EmailDispatchPolicy.IsEligible(challenge, null, null, user, epoch, now);
        Assert.IsTrue(Eligible());
        user.DisabledAt = now;
        Assert.IsFalse(Eligible());
        user.DisabledAt = null;
        user.RecoveryLocked = true;
        Assert.IsFalse(Eligible());
        user.RecoveryLocked = false;
        user.SecurityStamp = "changed-stamp";
        Assert.IsFalse(Eligible());
        user.SecurityStamp = challenge.SecurityStamp;
        challenge.RecoveryEpoch = Guid.NewGuid();
        Assert.IsFalse(Eligible());
    }

    [TestMethod]
    public void RetryLimitAndLeaseIdentityPreventUnlimitedOrLateWrites()
    {
        var now = DateTimeOffset.UtcNow;
        var lease = Guid.NewGuid();
        var job = new EmailJob { State = EmailJobState.Leased, LeaseId = lease, LeaseUntil = now.AddMinutes(2), Attempts = 4 };
        Assert.IsFalse(EmailDispatchPolicy.ApplyResult(job, Guid.NewGuid(), new(EmailSendStatus.Accepted), now));
        Assert.AreEqual(EmailJobState.Leased, job.State);
        Assert.IsTrue(EmailDispatchPolicy.ApplyResult(job, lease, new(EmailSendStatus.TransientFailure, "secret-like-server-text"), now));
        Assert.AreEqual(EmailJobState.Pending, job.State);
        Assert.AreEqual("smtp_transient", job.FailureCode);
        Assert.IsNull(job.LeaseId);
        Assert.IsTrue(job.DueAt > now);
        job.State = EmailJobState.Leased;
        job.LeaseId = lease;
        job.LeaseUntil = now.AddMinutes(2);
        job.Attempts = 5;
        EmailDispatchPolicy.ApplyResult(job, lease, new(EmailSendStatus.TransientFailure), now);
        Assert.AreEqual(EmailJobState.Failed, job.State);
        Assert.AreEqual("smtp_attempt_limit", job.FailureCode);
    }

    [TestMethod]
    public void AcceptedIsPreservedAndUnconfiguredDoesNotSpendASmtpAttempt()
    {
        var now = DateTimeOffset.UtcNow;
        var lease = Guid.NewGuid();
        var job = new EmailJob { State = EmailJobState.Leased, LeaseId = lease, LeaseUntil = now.AddMinutes(2), Attempts = 1 };
        EmailDispatchPolicy.ApplyResult(job, lease, new(EmailSendStatus.NotConfigured, "sensitive-config"), now);
        Assert.AreEqual(0, job.Attempts);
        Assert.AreEqual(EmailJobState.Pending, job.State);
        Assert.AreEqual("smtp_not_configured", job.FailureCode);
        job.State = EmailJobState.Leased;
        job.LeaseId = lease;
        job.LeaseUntil = now.AddMinutes(2);
        job.Attempts = 1;
        EmailDispatchPolicy.ApplyResult(job, lease, new(EmailSendStatus.Accepted, "server-response"), now);
        Assert.AreEqual(EmailJobState.Accepted, job.State);
        Assert.AreEqual(now, job.AcceptedAt);
        Assert.IsNull(job.FailureCode);
        Assert.IsFalse(EmailDispatchPolicy.ApplyResult(job, lease, new(EmailSendStatus.PermanentFailure), now));
        Assert.AreEqual(EmailJobState.Accepted, job.State);
    }

    private static RegistrationFixture Registration()
    {
        var now = DateTimeOffset.UtcNow;
        var epoch = Guid.NewGuid();
        var invitation = new TestInvitation { Id = Guid.NewGuid(), NormalizedEmail = "FICTIONAL@EXAMPLE.INVALID", RecoveryEpoch = epoch, CreatedAt = now, ExpiresAt = now.AddDays(7) };
        var pending = new PendingRegistration { Id = Guid.NewGuid(), InvitationId = invitation.Id, Email = "fictional@example.invalid", NormalizedEmail = invitation.NormalizedEmail, RecoveryEpoch = epoch, Generation = 1, State = RegistrationState.Pending, CreatedAt = now, ExpiresAt = now.AddHours(1) };
        var challenge = new EmailChallenge { Id = Guid.NewGuid(), Purpose = EmailChallengePurpose.Registration, PendingRegistrationId = pending.Id, NormalizedEmail = pending.NormalizedEmail, RecoveryEpoch = epoch, Generation = 1, CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
        return new(now, epoch, invitation, pending, challenge);
    }

    private sealed record RegistrationFixture(DateTimeOffset Now, Guid Epoch, TestInvitation Invitation, PendingRegistration Pending, EmailChallenge Challenge)
    {
        public bool Eligible() => EmailDispatchPolicy.IsEligible(Challenge, Pending, Invitation, null, Epoch, Now);
    }
}
