using Dango.Protocol;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Dango.Server.Tests.RoomsManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomAccessLifecyclePostgresTests
{
    [TestMethod]
    public async Task B3DissolveRestoreAndScopedExpiryTerminateNewDependenciesWithoutDeletingIdentity()
    {
        RoomAccessHttpHubTests.RequireApproval();
        var clock = new Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var visitor = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner, "publicShared");
        var code = await fixture.InScopeAsync(s => s.GetRequiredService<PublicRoomAccessService>().ResetCodeAsync(owner.UserId,
            owner.SessionId, created.RoomId, new(Guid.NewGuid(), created.AppliedVersion), CancellationToken.None));
        var entered = await fixture.InScopeAsync(async s =>
        {
            var service = s.GetRequiredService<RoomAdmissionService>();
            var before = await service.PersonalSnapshotAsync(visitor.UserId, visitor.SessionId, CancellationToken.None);
            return await service.EnterAsync(visitor.UserId, visitor.SessionId, created.RoomId,
                new(Guid.NewGuid(), Guid.NewGuid(), "desktop", before.RoomId, before.AssignmentVersion, code.Code), CancellationToken.None);
        });
        await fixture.InScopeAsync(s => s.GetRequiredService<RoomManagementService>().DissolveAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(Guid.NewGuid(), entered.RoomVersion, true), CancellationToken.None));
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            var state = await db.PetRoomStates.AsNoTracking().SingleAsync(p => p.PetId == visitor.PetId);
            Assert.IsNull(state.RoomId); Assert.IsNull(state.ControlLeaseId);
            var lease = await db.RoomDeviceLeases.AsNoTracking().SingleAsync(l => l.Id == entered.LeaseId);
            Assert.IsNotNull(lease.RevokedAt); Assert.IsNull(lease.RoomId); Assert.IsNull(lease.TransportId);
            Assert.IsNotNull((await db.PublicRoomCredentials.AsNoTracking().SingleAsync(c => c.Id == code.CredentialId)).RevokedAt);
            var notice = await db.RoomDomainEvents.AsNoTracking().SingleAsync(e => e.RoomId == created.RoomId && e.EventType == "dissolved");
            CollectionAssert.Contains(notice.TargetUserIds, visitor.UserId);
        });
        var dissolved = await RoomManagementTestSupport.GetAsync(fixture, owner, created.RoomId);
        var restored = await fixture.InScopeAsync(s => s.GetRequiredService<RoomManagementService>().RestoreAsync(owner.UserId,
            owner.SessionId, created.RoomId, new(Guid.NewGuid(), dissolved.Version), CancellationToken.None));
        var invalid = await Assert.ThrowsExactlyAsync<Dango.Server.Accounts.AccountFault>(() => fixture.InScopeAsync(s =>
            s.GetRequiredService<PublicRoomAccessService>().ResolveAsync(visitor.UserId, visitor.SessionId, new(code.Code), CancellationToken.None)));
        Assert.AreEqual("room_code_invalid", invalid.Code);
        await fixture.InScopeAsync(s => s.GetRequiredService<RoomManagementService>().DissolveAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(Guid.NewGuid(), restored.AppliedVersion, true), CancellationToken.None));
        clock.Now = clock.Now.AddDays(7);
        var purged = await fixture.InScopeAsync(s => s.GetRequiredService<RoomExpiryService>().PurgeBatchAsync(1, null, [created.RoomId], CancellationToken.None));
        Assert.AreEqual(1, purged.Deleted); Assert.AreEqual(0, purged.Failed);
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            Assert.IsFalse(await db.Rooms.AnyAsync(r => r.Id == created.RoomId));
            Assert.IsFalse(await db.PublicRoomCredentials.AnyAsync(c => c.RoomId == created.RoomId));
            Assert.IsFalse(await db.RoomBans.AnyAsync(b => b.RoomId == created.RoomId));
            Assert.IsTrue(await db.Pets.AnyAsync(p => p.Id == visitor.PetId && p.UserId == visitor.UserId));
            Assert.IsTrue(await db.RoomDeviceLeases.AnyAsync(l => l.Id == entered.LeaseId && l.RoomId == null));
        });
    }

    [TestMethod]
    public async Task PostgreSqlCompositeForeignKeysRejectMismatchedPetSessionAndController()
    {
        RoomAccessHttpHubTests.RequireApproval();
        await using var fixture = await FormalTestFixture.CreateAsync();
        var first = await PetTestSupport.RegisterAsync(fixture); var second = await PetTestSupport.RegisterAsync(fixture);
        foreach (var sessionMismatch in new[] { false, true })
        {
            await fixture.InScopeAsync(async s =>
            {
                var db = s.GetRequiredService<DangoDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                var now = DateTimeOffset.UtcNow;
                db.RoomDeviceLeases.Add(new()
                {
                    Id = Guid.NewGuid(), PetId = first.PetId, UserId = sessionMismatch ? first.UserId : second.UserId,
                    SessionId = second.SessionId, InstanceId = Guid.NewGuid(), RoomId = first.RoomId, DeviceKind = "desktop",
                    CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddSeconds(30)
                });
                var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)error.InnerException!).SqlState);
                await transaction.RollbackAsync();
            });
        }
        var lease = await fixture.InScopeAsync(async s =>
        {
            var service = s.GetRequiredService<RoomAdmissionService>();
            var before = await service.PersonalSnapshotAsync(second.UserId, second.SessionId, CancellationToken.None);
            return await service.EnterAsync(second.UserId, second.SessionId, second.RoomId,
                new(Guid.NewGuid(), Guid.NewGuid(), "desktop", before.RoomId, before.AssignmentVersion), CancellationToken.None);
        });
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var state = await db.PetRoomStates.SingleAsync(p => p.PetId == first.PetId);
            state.RoomId = first.RoomId; state.PositionX = state.PositionY = 0; state.IsSleeping = false;
            state.ControlLeaseId = lease.LeaseId;
            var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)error.InnerException!).SqlState);
            await transaction.RollbackAsync();
        });
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
