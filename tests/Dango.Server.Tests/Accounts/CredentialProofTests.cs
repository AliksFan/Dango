using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class CredentialProofTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Epoch = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [TestMethod]
    public void ResetRequiresCurrentIdentityBoundUnconsumedProof()
    {
        var (user, challenge, proof) = Valid();
        Assert.IsTrue(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
        challenge.ConsumedAt = Now;
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
        challenge.ConsumedAt = null;
        challenge.RevokedAt = Now;
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
        challenge.RevokedAt = null;
        user.SecurityStamp = "changed-password-stamp";
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
    }

    [TestMethod]
    public void WrongPurposeAccountEmailGenerationOrEpochCannotResetPassword()
    {
        var (user, challenge, proof) = Valid();
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof with { Purpose = EmailChallengePurpose.Registration }, Epoch, Now));
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof with { SubjectId = Guid.NewGuid() }, Epoch, Now));
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof with { NormalizedEmail = "OTHER@EXAMPLE.INVALID" }, Epoch, Now));
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof with { Generation = 2 }, Epoch, Now));
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Guid.NewGuid(), Now));
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, challenge.ExpiresAt));
    }

    [TestMethod]
    public void PublicResetCannotBypassDisableOrRecoveryLock()
    {
        var (user, challenge, proof) = Valid();
        user.DisabledAt = Now;
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
        user.DisabledAt = null;
        user.RecoveryLocked = true;
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
        user.RecoveryLocked = false;
        user.EmailConfirmed = false;
        Assert.IsFalse(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
    }

    [TestMethod]
    public void TemporaryLoginLockDoesNotRemoveAbilityToReplaceForgottenPassword()
    {
        var (user, challenge, proof) = Valid();
        user.LockoutEnd = Now.AddMinutes(15);
        Assert.IsTrue(AccountCredentialPolicy.MatchesReset(user, challenge, proof, Epoch, Now));
        Assert.IsFalse(AccountSessionPolicy.CanAuthenticate(user, Now));
    }

    private static (DangoUser User, EmailChallenge Challenge, EmailProof Proof) Valid()
    {
        var user = new DangoUser
        {
            Id = Guid.NewGuid(), Email = "fictional@example.invalid", NormalizedEmail = "FICTIONAL@EXAMPLE.INVALID",
            EmailConfirmed = true, SecurityStamp = "test-reset-stamp"
        };
        var challenge = new EmailChallenge
        {
            Id = Guid.NewGuid(), Purpose = EmailChallengePurpose.PasswordReset, UserId = user.Id,
            NormalizedEmail = user.NormalizedEmail, SecurityStamp = user.SecurityStamp, RecoveryEpoch = Epoch,
            Generation = 1, CreatedAt = Now, ExpiresAt = Now.AddMinutes(30)
        };
        var proof = new EmailProof(challenge.Id, challenge.Purpose, user.Id, challenge.NormalizedEmail,
            Epoch, challenge.Generation, challenge.ExpiresAt, challenge.SecurityStamp);
        return (user, challenge, proof);
    }
}
