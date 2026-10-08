using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class SessionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Epoch = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public void AbsoluteExpiryHasNoActivityBasedExtension()
    {
        Assert.AreEqual(Now.AddHours(8), AccountSessionPolicy.ExpiresAt(Now, false));
        Assert.AreEqual(Now.AddHours(168), AccountSessionPolicy.ExpiresAt(Now, true));
        var (user, session) = Valid();
        Assert.IsTrue(AccountSessionPolicy.IsValid(user, session, Epoch, session.ExpiresAt.AddTicks(-1)));
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, session.ExpiresAt));
    }

    [TestMethod]
    public void RevocationAndCredentialOrRecoveryChangesImmediatelyInvalidateSession()
    {
        var (user, session) = Valid();
        Assert.IsTrue(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        session.RevokedAt = Now;
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        session.RevokedAt = null;
        user.SecurityStamp = "replacement-stamp";
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        user.SecurityStamp = session.SecurityStamp;
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Guid.NewGuid(), Now));
    }

    [TestMethod]
    public void AnotherAccountCannotUseAnOtherwiseValidSession()
    {
        var (user, session) = Valid();
        user.Id = Guid.NewGuid();
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
    }

    [TestMethod]
    public void DisabledRecoveryLockedAndTemporarilyLockedAccountsCannotAuthenticate()
    {
        var (user, session) = Valid();
        user.DisabledAt = Now;
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        user.DisabledAt = null;
        user.RecoveryLocked = true;
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        user.RecoveryLocked = false;
        user.LockoutEnd = Now.AddMinutes(15);
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        Assert.IsTrue(AccountSessionPolicy.IsValid(user, session, Epoch, user.LockoutEnd.Value));
        user.EmailConfirmed = false;
        Assert.IsFalse(AccountSessionPolicy.CanAuthenticate(user, user.LockoutEnd.Value));
    }

    [TestMethod]
    public void SessionWithInvalidOrExcessiveLifetimeFailsClosed()
    {
        var (user, session) = Valid();
        session.ExpiresAt = session.CreatedAt;
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        session.ExpiresAt = Now.AddHours(8).AddTicks(1);
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        session.Remembered = true;
        session.ExpiresAt = Now.AddHours(168);
        Assert.IsTrue(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
        session.ExpiresAt = session.ExpiresAt.AddTicks(1);
        Assert.IsFalse(AccountSessionPolicy.IsValid(user, session, Epoch, Now));
    }

    private static (DangoUser User, AuthSession Session) Valid()
    {
        var user = new DangoUser { Id = Guid.NewGuid(), EmailConfirmed = true, SecurityStamp = "test-stamp", LockoutEnabled = true };
        var session = new AuthSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp,
            RecoveryEpoch = Epoch, CreatedAt = Now, ExpiresAt = Now.AddHours(8)
        };
        return (user, session);
    }
}
