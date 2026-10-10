using Dango.Server.Persistence.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.BackendManualReview.Tests;

[TestClass]
public sealed class ManualReviewConfirmationTests
{
    [TestMethod]
    [DataRow("pending_id")]
    [DataRow("invitation")]
    [DataRow("email")]
    [DataRow("normalized")]
    [DataRow("epoch")]
    [DataRow("state")]
    [DataRow("pending_expired")]
    [DataRow("challenge_id")]
    [DataRow("challenge_subject")]
    [DataRow("challenge_user")]
    [DataRow("challenge_purpose")]
    [DataRow("challenge_email")]
    [DataRow("challenge_epoch")]
    [DataRow("challenge_generation")]
    [DataRow("challenge_stamp")]
    [DataRow("challenge_future")]
    [DataRow("challenge_expired")]
    [DataRow("challenge_after_pending")]
    [DataRow("challenge_consumed")]
    [DataRow("challenge_revoked")]
    public void MaintenanceConfirmationRejectsAnyUnprovenSubject(string fault)
    {
        var (pending, challenge, now) = Valid();
        var id = pending.Id; var invitation = pending.InvitationId; var email = pending.Email; var epoch = pending.RecoveryEpoch;
        switch (fault)
        {
            case "pending_id": pending.Id = Guid.NewGuid(); break;
            case "invitation": pending.InvitationId = Guid.NewGuid(); break;
            case "email": pending.Email = "other@review.invalid"; break;
            case "normalized": pending.NormalizedEmail = "OTHER@REVIEW.INVALID"; break;
            case "epoch": pending.RecoveryEpoch = Guid.NewGuid(); break;
            case "state": pending.State = RegistrationState.Verified; break;
            case "pending_expired": pending.ExpiresAt = now; break;
            case "challenge_id": challenge.Id = Guid.Empty; break;
            case "challenge_subject": challenge.PendingRegistrationId = Guid.NewGuid(); break;
            case "challenge_user": challenge.UserId = Guid.NewGuid(); break;
            case "challenge_purpose": challenge.Purpose = EmailChallengePurpose.PasswordReset; break;
            case "challenge_email": challenge.NormalizedEmail = "OTHER@REVIEW.INVALID"; break;
            case "challenge_epoch": challenge.RecoveryEpoch = Guid.NewGuid(); break;
            case "challenge_generation": challenge.Generation = 2; break;
            case "challenge_stamp": challenge.SecurityStamp = "unrelated"; break;
            case "challenge_future": challenge.CreatedAt = now.AddTicks(1); break;
            case "challenge_expired": challenge.ExpiresAt = now; break;
            case "challenge_after_pending": challenge.ExpiresAt = pending.ExpiresAt.AddTicks(1); break;
            case "challenge_consumed": challenge.ConsumedAt = now; break;
            case "challenge_revoked": challenge.RevokedAt = now; break;
        }
        var error = Assert.ThrowsExactly<ManualReviewRejected>(() =>
            ManualReviewRuntime.ValidateConfirmation(pending, challenge, id, email, invitation, epoch, now));
        Assert.AreEqual("manual_review_confirmation_subject_rejected", error.Code);
    }

    [TestMethod]
    public void OnlyExactUnconsumedRegistrationChallengeIsEligible()
    {
        var (pending, challenge, now) = Valid();
        ManualReviewRuntime.ValidateConfirmation(pending, challenge, pending.Id, pending.Email, pending.InvitationId, pending.RecoveryEpoch, now);
    }

    [TestMethod]
    public void SameNormalizedEmailRemainsEligibleRegardlessOfDisplayCase()
    {
        var (pending, challenge, now) = Valid();
        var email = pending.Email;
        pending.Email = email.ToUpperInvariant();
        ManualReviewRuntime.ValidateConfirmation(pending, challenge, pending.Id, email, pending.InvitationId, pending.RecoveryEpoch, now);
    }

    private static (PendingRegistration, EmailChallenge, DateTimeOffset) Valid()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new PendingRegistration
        {
            Id = Guid.NewGuid(), InvitationId = Guid.NewGuid(), Email = "b5c-static@review.invalid", NormalizedEmail = "B5C-STATIC@REVIEW.INVALID",
            RecoveryEpoch = Guid.NewGuid(), State = RegistrationState.Pending, ExpiresAt = now.AddMinutes(30), Generation = 1
        };
        var challenge = new EmailChallenge
        {
            Id = Guid.NewGuid(), PendingRegistrationId = pending.Id, NormalizedEmail = pending.NormalizedEmail, RecoveryEpoch = pending.RecoveryEpoch,
            Purpose = EmailChallengePurpose.Registration, Generation = 1, CreatedAt = now, ExpiresAt = now.AddMinutes(20)
        };
        return (pending, challenge, now);
    }
}
