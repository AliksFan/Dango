using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class AccountProfilePolicyTests
{
    [TestMethod]
    public void LegacyNullNicknameAndDuplicateNicknamesKeepDistinctImmutableAccountIds()
    {
        var first = new DangoUser { Id = Guid.NewGuid() };
        var second = new DangoUser { Id = Guid.NewGuid(), Nickname = "小团子" };
        Assert.IsNull(AccountProfilePolicy.Describe(first).Nickname);
        Assert.AreEqual(1L, AccountProfilePolicy.Describe(first).ProfileVersion);
        first.Nickname = second.Nickname;
        Assert.AreEqual(AccountProfilePolicy.Describe(first).Nickname, AccountProfilePolicy.Describe(second).Nickname);
        Assert.AreNotEqual(first.Id, second.Id);
    }

    [TestMethod]
    public void PersistedCorruptionFailsClosedRatherThanRepairingOrDefaulting()
    {
        foreach (var user in new[] { new DangoUser(), new DangoUser { Id = Guid.NewGuid(), ProfileVersion = 0 },
            new DangoUser { Id = Guid.NewGuid(), Nickname = " " }, new DangoUser { Id = Guid.NewGuid(), Nickname = "e\u0301" },
            new DangoUser { Id = Guid.NewGuid(), Nickname = "团子\n" } })
        {
            var fault = Assert.ThrowsExactly<AccountFault>(() => AccountProfilePolicy.Describe(user));
            Assert.AreEqual("account_profile_invalid_state", fault.Code);
            Assert.AreEqual(503, fault.Status);
        }
    }

    [TestMethod]
    public async Task InvalidUpdateNeverOpensAnUnconfiguredDatabase()
    {
        await using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var service = new AccountProfileService(db, new(Guid.NewGuid()), TimeProvider.System, new AccountProfileCheckpoint());
        var version = await Assert.ThrowsExactlyAsync<AccountFault>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new(0, "团子"), CancellationToken.None));
        Assert.AreEqual("expected_version_invalid", version.Code);
        var nickname = await Assert.ThrowsExactlyAsync<AccountFault>(() => service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new(1, "团子\n"), CancellationToken.None));
        Assert.AreEqual("nickname_invalid", nickname.Code);
    }
}
