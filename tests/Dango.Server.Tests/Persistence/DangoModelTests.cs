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
        Assert.Contains("ck_rooms_template", sql);
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
        Assert.HasCount(5, migrations.Migrations);
        Assert.Contains("20261008074701_InitialAccountRoomFoundation", migrations.Migrations.Keys.ToArray());
        Assert.IsTrue(migrations.Migrations.Keys.Any(key => key.EndsWith("_AccountLifecycle", StringComparison.Ordinal)));
        Assert.IsTrue(migrations.Migrations.Keys.Any(key => key.EndsWith("_RoomManagement", StringComparison.Ordinal)));
        Assert.IsTrue(migrations.Migrations.Keys.Any(key => key.EndsWith("_RoomAccess", StringComparison.Ordinal)));
        Assert.IsNotNull(migrations.ModelSnapshot);
        Assert.IsFalse(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("__ef_migrations_history", sql);
        Assert.Contains("ux_users_normalized_email", sql);
        Assert.Contains("ck_pet_room_presence", sql);
        Assert.Contains("BEGIN", sql);
        Assert.DoesNotContain("fictional_secret_marker", sql);
        Assert.Contains("UPDATE dango.rooms SET template_id = 'desktop-pet-default' WHERE kind = 0", sql);
        Assert.Contains("WHERE status IN (1, 2)", sql);
    }

    [TestMethod]
    public void LeaseIdentityAndControlOwnershipAreCompositeForeignKeys()
    {
        using var context = new DangoDbContextFactory().CreateDbContext([]);
        var lease = context.Model.FindEntityType(typeof(RoomDeviceLease))!;
        Assert.IsTrue(lease.GetForeignKeys().Any(fk => fk.PrincipalEntityType.ClrType == typeof(AccountPet)
            && fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "PetId", "UserId" })));
        Assert.IsTrue(lease.GetForeignKeys().Any(fk => fk.PrincipalEntityType.ClrType == typeof(AuthSession)
            && fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "SessionId", "UserId" })));
        var state = context.Model.FindEntityType(typeof(PetRoomState))!;
        Assert.IsTrue(state.GetForeignKeys().Any(fk => fk.PrincipalEntityType.ClrType == typeof(RoomDeviceLease)
            && fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "PetId", "ControlLeaseId" })));
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("ck_pet_room_control", sql);
        Assert.Contains("ck_public_code_epoch", sql);
        Assert.Contains("ck_leases_lifetime", sql);
    }

    [TestMethod]
    public void RoomReceiptsAndOutboxAreUniqueDurableAndRemainAfterRoomDeletion()
    {
        using var context = new DangoDbContextFactory().CreateDbContext([]);
        foreach (var type in new[] { typeof(RoomOperationAudit), typeof(RoomDomainEvent) })
            Assert.IsFalse(context.Model.FindEntityType(type)!.GetForeignKeys().Any(), "Deletion must retain stable audit/event identifiers.");
        var audit = context.Model.FindEntityType(typeof(RoomOperationAudit))!;
        var request = audit.GetIndexes().Single(index => index.GetDatabaseName() == "ux_room_audit_request");
        Assert.IsTrue(request.IsUnique);
        CollectionAssert.AreEqual(new[] { "ActorUserId", "RequestId" }, request.Properties.Select(property => property.Name).ToArray());
        Assert.AreEqual("request_id IS NOT NULL", request.GetFilter());
        var outbox = context.Model.FindEntityType(typeof(RoomDomainEvent))!;
        Assert.IsTrue(outbox.GetIndexes().Single(index => index.GetDatabaseName() == "ux_room_outbox_version").IsUnique);
        Assert.AreEqual("published_at IS NULL", outbox.GetIndexes().Single(index => index.GetDatabaseName() == "ix_room_outbox_pending").GetFilter());
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("request_hash IS NOT NULL", sql);
        Assert.Contains("ck_room_audit_request", sql);
        Assert.Contains("ck_room_outbox_event", sql);
    }

    private static IIndex AssertUnique<TEntity>(IModel model, string name, string property)
    {
        var index = model.FindEntityType(typeof(TEntity))!.GetIndexes().Single(item => item.GetDatabaseName() == name);
        Assert.IsTrue(index.IsUnique);
        Assert.AreEqual(property, index.Properties.Single().Name);
        return index;
    }
}
