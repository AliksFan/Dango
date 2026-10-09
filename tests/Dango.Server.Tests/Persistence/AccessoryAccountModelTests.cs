using Dango.Server.Configuration;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Persistence;

[TestClass]
public sealed class AccessoryAccountModelTests
{
    [TestMethod]
    public void BlobIdentityCannotReplacePetAuthorizationOrIdempotency()
    {
        using var db = new DangoDbContextFactory().CreateDbContext([]);
        var reference = db.Model.FindEntityType(typeof(AccessoryAssetReference))!;
        CollectionAssert.AreEqual(new[] { "PetId", "AssetId" }, reference.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { typeof(AccountPet), typeof(AccessoryBlob) }, reference.GetForeignKeys().Select(f => f.PrincipalEntityType.ClrType).ToArray());
        var receipt = db.Model.FindEntityType(typeof(AccessoryCommitReceipt))!;
        CollectionAssert.AreEqual(new[] { "PetId", "IdempotencyKey" }, receipt.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.IsTrue(db.Model.FindEntityType(typeof(AccessoryCurrentOutfit))!.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.IsNull(db.Model.FindEntityType(typeof(AccountPet))!.FindProperty("AccessoryOutfitJson"));
        foreach (var type in new[] { typeof(AccessoryAssetReference), typeof(AccessoryCurrentOutfit), typeof(AccessoryCommitReceipt) })
            foreach (var foreignKey in db.Model.FindEntityType(type)!.GetForeignKeys()) Assert.AreEqual(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [TestMethod]
    public void MigrationCreatesOnlyAccessoryTablesWithoutTouchingExistingRecords()
    {
        using var db = new DangoDbContextFactory().CreateDbContext([]);
        var keys = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        Assert.IsTrue(keys[^1].EndsWith("_AccessoryAccountBackup", StringComparison.Ordinal));
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(keys[^2], keys[^1]);
        foreach (var table in new[] { "accessory_blobs", "accessory_asset_references", "accessory_current_outfits", "accessory_commit_receipts" })
            Assert.Contains("CREATE TABLE dango." + table, sql);
        Assert.Contains("COALESCE", sql, "Missing JSON fields must reject a SQL NULL check.");
        Assert.Contains("normalization_version = 1", sql);
        Assert.Contains("16000000", sql);
        Assert.Contains("33554432", sql);
        Assert.Contains("ix_accessory_references_pet_staging", sql);
        Assert.DoesNotContain("UPDATE ", sql);
        Assert.DoesNotContain("DELETE FROM ", sql);
        Assert.DoesNotContain("DROP ", sql);
        Assert.DoesNotContain("ALTER TABLE dango.pets", sql);
    }

    [TestMethod]
    public void StorageMustBeExplicitExistingAndSeparateFromCheckoutAndKeys()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "dango-c2-config-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(temporary, "checkout");
        var keys = Path.Combine(temporary, "keys");
        var blobs = Path.Combine(temporary, "blobs");
        Directory.CreateDirectory(repo); Directory.CreateDirectory(keys); Directory.CreateDirectory(blobs);
        try
        {
            const string connection = "Host=127.0.0.1;Database=dango_fictional;Username=fictional;Password=fictional_test_only";
            Assert.IsTrue(FormalServerOptions.TryCreate("dango_fictional", connection, keys, "https://127.0.0.1:443/", repo, out var disabled, out _));
            Assert.IsNull(disabled!.AccessoryBlobDirectory);
            Assert.IsTrue(FormalServerOptions.TryCreate("dango_fictional", connection, keys, "https://127.0.0.1:443/", repo, out var enabled, out _, blobs));
            Assert.AreEqual(blobs, enabled!.AccessoryBlobDirectory);
            foreach (var invalid in new[] { repo, keys, temporary, Path.Combine(temporary, "absent"), "relative" })
            {
                Assert.IsFalse(FormalServerOptions.TryCreate("dango_fictional", connection, keys, "https://127.0.0.1:443/", repo, out _, out var code, invalid));
                Assert.AreEqual("formal_accessory_directory_invalid", code);
            }
            Assert.IsFalse(Directory.Exists(Path.Combine(temporary, "absent")));
        }
        finally
        {
            var resolved = Path.GetFullPath(temporary);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), resolved);
            Assert.StartsWith("dango-c2-config-", Path.GetFileName(resolved));
            Directory.Delete(resolved, true);
        }
    }
}
