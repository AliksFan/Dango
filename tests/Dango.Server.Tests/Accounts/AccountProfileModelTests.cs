using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class AccountProfileModelTests
{
    [TestMethod]
    public void LegacyAccountsMayHaveNoNicknameAndProfileVersionHasOwnConcurrencyToken()
    {
        using var db = new DangoDbContextFactory().CreateDbContext([]);
        var user = db.Model.FindEntityType(typeof(DangoUser))!;
        var nickname = user.FindProperty("Nickname");
        Assert.IsNotNull(nickname, "Nickname must be persisted independently of login identity.");
        Assert.IsTrue(nickname.IsNullable);
        Assert.IsTrue(user.FindProperty("ProfileVersion")!.IsConcurrencyToken);
        Assert.IsFalse(user.GetIndexes().Any(i => i.IsUnique && i.Properties.Contains(nickname)),
            "Different accounts may share a nickname.");
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("ck_users_profile_version", sql);
        Assert.Contains("ck_users_nickname", sql);
    }

    [TestMethod]
    public void ForwardMigrationKeepsLegacyRowsAndAddsNoUniqueNicknameIndexWithoutConnecting()
    {
        using var db = new DangoDbContextFactory().CreateDbContext([]);
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        var profile = migrations.Single(m => m.EndsWith("_AccountProfile", StringComparison.Ordinal));
        var sql = db.GetService<IMigrator>().GenerateScript(migrations[^2], profile, MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("ADD nickname", sql);
        Assert.Contains("ADD profile_version bigint NOT NULL DEFAULT 1", sql);
        Assert.Contains("ck_users_nickname", sql);
        Assert.Contains("ck_users_profile_version", sql);
        Assert.DoesNotContain("DELETE", sql);
        Assert.DoesNotContain("DROP", sql);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", sql);
        Assert.IsFalse(db.Database.HasPendingModelChanges());
    }
}
