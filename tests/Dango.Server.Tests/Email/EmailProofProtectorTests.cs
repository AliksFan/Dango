using Dango.Server.Email;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Email;

[TestClass]
public sealed class EmailProofProtectorTests
{
    [TestMethod]
    public void ProofBindsEveryChallengeFieldAndCannotSwitchPurpose()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var protector = new EmailProofProtector(new EphemeralDataProtectionProvider(), clock);
        var challenge = Challenge(clock.GetUtcNow());
        var value = protector.Protect(challenge);
        Assert.IsTrue(protector.TryUnprotect(value, out var proof));
        Assert.IsNotNull(proof);
        Assert.AreEqual(challenge.Id, proof.ChallengeId);
        Assert.AreEqual(challenge.PendingRegistrationId, proof.SubjectId);
        Assert.AreEqual(challenge.RecoveryEpoch, proof.RecoveryEpoch);
        Assert.AreEqual(challenge.NormalizedEmail, proof.NormalizedEmail);
        Assert.AreEqual(challenge.Generation, proof.Generation);
        Assert.AreEqual(challenge.ExpiresAt, proof.ExpiresAt);
        Assert.IsNull(proof.SecurityStamp);
        Assert.IsFalse(protector.TryUnprotect(value.Replace("r.", "p.", StringComparison.Ordinal), out _));
        var middle = value.Length / 2;
        var altered = value[..middle] + (value[middle] == 'A' ? 'B' : 'A') + value[(middle + 1)..];
        Assert.IsFalse(protector.TryUnprotect(altered, out _));
    }

    [TestMethod]
    public void RepeatedProtectionDoesNotExtendTheAbsoluteDeadline()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var protector = new EmailProofProtector(new EphemeralDataProtectionProvider(), clock);
        var challenge = Challenge(clock.GetUtcNow());
        var original = protector.Protect(challenge);
        clock.Now = challenge.ExpiresAt.AddSeconds(-1);
        var repeated = protector.Protect(challenge);
        clock.Now = challenge.ExpiresAt;
        Assert.IsFalse(protector.TryUnprotect(original, out _));
        Assert.IsFalse(protector.TryUnprotect(repeated, out _));
    }

    [TestMethod]
    public void SameKeyProviderCanVerifyAcrossInstancesButDifferentKeysCannot()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var keys = new EphemeralDataProtectionProvider();
        var value = new EmailProofProtector(keys, clock).Protect(Challenge(clock.GetUtcNow()));
        Assert.IsTrue(new EmailProofProtector(keys, clock).TryUnprotect(value, out _));
        Assert.IsFalse(new EmailProofProtector(new EphemeralDataProtectionProvider(), clock).TryUnprotect(value, out _));
    }

    [TestMethod]
    public void ResetProofRetainsSecurityStampAndRejectsMalformedOrOversizedInputs()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var protector = new EmailProofProtector(new EphemeralDataProtectionProvider(), clock);
        var challenge = Challenge(clock.GetUtcNow());
        challenge.Purpose = EmailChallengePurpose.PasswordReset;
        challenge.UserId = challenge.PendingRegistrationId;
        challenge.PendingRegistrationId = null;
        challenge.SecurityStamp = "fictional-security-stamp";
        Assert.IsTrue(protector.TryUnprotect(protector.Protect(challenge), out var proof));
        Assert.AreEqual(challenge.SecurityStamp, proof!.SecurityStamp);
        foreach (var invalid in new[] { null, "", "r.not-a-proof", "unknown.proof", new string('x', 8193) })
            Assert.IsFalse(protector.TryUnprotect(invalid, out _));
    }

    [TestMethod]
    public void ExpiredOrAmbiguousSubjectsCannotBeProtected()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var protector = new EmailProofProtector(new EphemeralDataProtectionProvider(), clock);
        var challenge = Challenge(clock.GetUtcNow());
        challenge.UserId = Guid.NewGuid();
        Assert.ThrowsExactly<ArgumentException>(() => protector.Protect(challenge));
        challenge.UserId = null;
        clock.Now = challenge.ExpiresAt;
        Assert.ThrowsExactly<ArgumentException>(() => protector.Protect(challenge));
    }

    private static EmailChallenge Challenge(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Purpose = EmailChallengePurpose.Registration,
        PendingRegistrationId = Guid.NewGuid(),
        NormalizedEmail = "FICTIONAL@EXAMPLE.INVALID",
        RecoveryEpoch = Guid.NewGuid(),
        Generation = 1,
        CreatedAt = now,
        ExpiresAt = now.AddMinutes(30)
    };

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
