using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Persistence;

[TestClass]
public sealed class DangoModelTests
{
    [TestMethod]
    public void UniqueIndexesAndAssignmentAuthorityCannotBeRemovedWithoutFailingTheGate()
    {
        using var context = new DangoDbContextFactory().CreateDbContext([]);
        var model = context.Model;
        AssertUnique<DangoUser>(model, "ux_users_normalized_email", nameof(DangoUser.NormalizedEmail));
        AssertUnique<AccountPet>(model, "ux_pets_user_id", nameof(AccountPet.UserId));
        var desktop = AssertUnique<PersistentRoom>(model, "ux_rooms_desktop_owner", nameof(PersistentRoom.OwnerId));
        Assert.AreEqual("kind = 0", desktop.GetFilter());
        var member = model.FindEntityType(typeof(PersistentRoomMembership))!;
        CollectionAssert.AreEqual(new[] { nameof(PersistentRoomMembership.RoomId), nameof(PersistentRoomMembership.UserId) },
            member.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        var state = model.FindEntityType(typeof(PetRoomState))!;
        Assert.AreEqual(nameof(PetRoomState.PetId), state.FindPrimaryKey()!.Properties.Single().Name);
        Assert.IsTrue(state.FindProperty(nameof(PetRoomState.RoomId))!.IsNullable);
        Assert.IsNull(model.FindEntityType(typeof(AccountPet))!.FindProperty("CurrentRoomId"));
    }

    [TestMethod]
    public void AllRelationshipsRestrictDeletionAndVersionsParticipateInConcurrency()
    {
        using var context = new DangoDbContextFactory().CreateDbContext([]);
        foreach (var foreignKey in context.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
            Assert.AreEqual(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.IsTrue(context.Model.FindEntityType(typeof(AccountPet))!.FindProperty(nameof(AccountPet.ConfigurationVersion))!.IsConcurrencyToken);
        Assert.IsTrue(context.Model.FindEntityType(typeof(PersistentRoom))!.FindProperty(nameof(PersistentRoom.Version))!.IsConcurrencyToken);
        Assert.IsTrue(context.Model.FindEntityType(typeof(PetRoomState))!.FindProperty(nameof(PetRoomState.Version))!.IsConcurrencyToken);
    }

    [TestMethod]
    public void PostgresSqlContainsSchemaFinitePresenceAndLifecycleConstraintsWithoutConnecting()
    {
        using var context = new DangoDbContextFactory().CreateDbContext([]);
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("CREATE SCHEMA dango", sql);
        Assert.Contains("ck_users_verified", sql);
        Assert.Contains("ck_pets_configuration", sql);
        Assert.Contains("appearanceSchemaVersion", sql);
        Assert.Contains("ck_rooms_capacity", sql);
        Assert.Contains("ck_rooms_lifecycle", sql);
        Assert.Contains("INTERVAL '168 hours'", sql);
        Assert.Contains("ck_pet_room_presence", sql);
        Assert.Contains("'-Infinity'::double precision", sql);
        Assert.Contains("'Infinity'::double precision", sql);
        Assert.Contains("CREATE UNIQUE INDEX ux_rooms_desktop_owner", sql);
        Assert.Contains("WHERE kind = 0", sql);
        Assert.DoesNotContain("ON DELETE CASCADE", sql);
    }

    [TestMethod]
    public void InitialMigrationIsPresentMatchesModelAndGeneratesIdempotentScript()
    {
        using var context = new DangoDbContextFactory().CreateDbContext([]);
        var migrations = context.GetService<IMigrationsAssembly>();
        Assert.HasCount(1, migrations.Migrations);
        Assert.IsNotNull(migrations.ModelSnapshot);
        Assert.IsFalse(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("__ef_migrations_history", sql);
        Assert.Contains("ux_users_normalized_email", sql);
        Assert.Contains("ck_pet_room_presence", sql);
        Assert.Contains("BEGIN", sql);
        Assert.DoesNotContain("fictional_secret_marker", sql);
    }

    private static IIndex AssertUnique<TEntity>(IModel model, string name, string property)
    {
        var index = model.FindEntityType(typeof(TEntity))!.GetIndexes().Single(item => item.GetDatabaseName() == name);
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual(property, index.Properties.Single().Name);
        return index;
    }
}
