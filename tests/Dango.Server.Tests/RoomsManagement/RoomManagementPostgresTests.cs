using System.Collections.Concurrent;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.RoomsManagement;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomManagementPostgresTests
{
    [TestInitialize]
    public void RequireSeparateB3AuthorizationBeforeTheFixtureCanConnect() => RoomManagementTestSupport.RequireB3Approval();

    [TestMethod]
    public async Task TwoIndependentConnectionsCompetingForTheThirdRoomCommitOnlyOneGraph()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var races = await RaceAsync(fixture, async (services, index) =>
            await RoomManagementTestSupport.Service(services).CreateAsync(owner.UserId, owner.SessionId,
                new(Guid.NewGuid(), index == 0 ? "privateShared" : "publicShared", $"fictional race {index}"), CancellationToken.None));
        Assert.AreEqual(1, races.Count(r => r.Result is not null));
        Assert.AreEqual("room_quota_reached", races.Single(r => r.Error is not null).Error);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var rooms = await db.Rooms.Where(r => r.OwnerId == owner.UserId && r.Kind != PersistentRoomKind.DesktopPet).ToArrayAsync();
            Assert.AreEqual(3, rooms.Length);
            var ids = rooms.Select(r => r.Id).ToArray();
            Assert.AreEqual(3, await db.RoomMemberships.CountAsync(m => ids.Contains(m.RoomId) && m.UserId == owner.UserId && m.Role == PersistentRoomRole.Owner));
            Assert.AreEqual(3, await db.RoomOperationAudits.CountAsync(a => a.ActorUserId == owner.UserId));
            Assert.AreEqual(3, await db.RoomDomainEvents.CountAsync(e => ids.Contains(e.RoomId)));
            Assert.AreEqual(owner.RoomId, (await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId)).RoomId);
        });
    }

    [TestMethod]
    public async Task SameCreateRequestOnIndependentConnectionsHasOneReceiptAndReplayReturnsTheOriginalVersion()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var key = Guid.NewGuid();
        var races = await RaceAsync(fixture, (services, index) => RoomManagementTestSupport.Service(services)
            .CreateAsync(owner.UserId, owner.SessionId, new(key, "privateShared", index == 0 ? " é " : "e\u0301"), CancellationToken.None));
        Assert.IsTrue(races.All(r => r.Error is null));
        Assert.AreEqual(races[0].Result, races[1].Result);
        var original = races[0].Result!;
        await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).UpdateAsync(owner.UserId, owner.SessionId,
            original.RoomId, new(Guid.NewGuid(), 1, "changed", 5), CancellationToken.None));
        var replay = await RoomManagementTestSupport.CreateAsync(fixture, owner, name: "é", requestId: key);
        Assert.AreEqual(original, replay);
        var current = await RoomManagementTestSupport.GetAsync(fixture, owner, original.RoomId);
        Assert.AreEqual(2L, current.Version);
        Assert.AreEqual("changed", current.Name);
        Assert.AreEqual("ordinary-default", current.TemplateId);
        Assert.AreEqual(1, current.TemplateVersion);
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => RoomManagementTestSupport.CreateAsync(fixture, owner, name: "different", requestId: key));
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => RoomManagementTestSupport.CreateAsync(fixture, owner, capacity: 0, requestId: key));
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => RoomManagementTestSupport.CreateAsync(fixture, owner, name: "invalid\nname", requestId: key));
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => RoomManagementTestSupport.CreateAsync(fixture, owner, kind: "desktopPet", requestId: key));
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .UpdateAsync(owner.UserId, owner.SessionId, original.RoomId, new(key, 0, "é", 6), CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .DissolveAsync(owner.UserId, owner.SessionId, original.RoomId, new(key, 1, false), CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_request_conflict", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .RestoreAsync(owner.UserId, owner.SessionId, original.RoomId, new(key, 2), CancellationToken.None)));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(a => a.ActorUserId == owner.UserId && a.RequestId == key));
            Assert.AreEqual(2, await db.RoomDomainEvents.CountAsync(e => e.RoomId == original.RoomId));
            Assert.AreEqual(1, await db.Rooms.CountAsync(r => r.OwnerId == owner.UserId && r.Kind != PersistentRoomKind.DesktopPet));
        });
        var other = await PetTestSupport.RegisterAsync(fixture);
        var otherResult = await RoomManagementTestSupport.CreateAsync(fixture, other, name: "é", requestId: key);
        Assert.AreNotEqual(original.RoomId, otherResult.RoomId, "The request namespace is per actor.");
    }

    [TestMethod]
    public async Task SameExpectedVersionOnIndependentConnectionsAllowsOnlyOneUpdate()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var races = await RaceAsync(fixture, (services, index) => RoomManagementTestSupport.Service(services).UpdateAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(Guid.NewGuid(), 1, $"winner {index}", 5 + index), CancellationToken.None));
        var winner = races.Single(r => r.Result is not null).Result!;
        Assert.AreEqual("room_version_conflict", races.Single(r => r.Error is not null).Error);
        Assert.AreEqual(2L, winner.AppliedVersion);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(2L, (await db.Rooms.SingleAsync(r => r.Id == created.RoomId)).Version);
            Assert.AreEqual(2, await db.RoomOperationAudits.CountAsync(a => a.RoomId == created.RoomId));
            Assert.AreEqual(2, await db.RoomDomainEvents.CountAsync(e => e.RoomId == created.RoomId));
        });
    }

    [TestMethod]
    public async Task PrivateOfflineMembersAndPublicActualAssignmentsPreventShrinkingBelowCapacity()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var privateRoom = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var publicRoom = await RoomManagementTestSupport.CreateAsync(fixture, owner, "publicShared");
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            db.RoomMemberships.Add(new() { RoomId = privateRoom.RoomId, UserId = visitor.UserId, Role = PersistentRoomRole.Member });
            await db.SaveChangesAsync();
        });
        var privateView = await RoomManagementTestSupport.GetAsync(fixture, owner, privateRoom.RoomId);
        Assert.AreEqual(2, privateView.ReservedSeats);
        Assert.AreEqual(0, privateView.Occupancy);
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner, visitor.PetId);
        await RoomManagementTestSupport.FaultAsync("room_capacity_conflict", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .UpdateAsync(owner.UserId, owner.SessionId, privateRoom.RoomId, new(Guid.NewGuid(), 1, "shrink", 1), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner, visitor.PetId));
        await RoomManagementTestSupport.PlaceAsync(fixture, owner.PetId, publicRoom.RoomId);
        await RoomManagementTestSupport.PlaceAsync(fixture, visitor.PetId, publicRoom.RoomId);
        var publicView = await RoomManagementTestSupport.GetAsync(fixture, owner, publicRoom.RoomId);
        Assert.AreEqual(0, publicView.ReservedSeats);
        Assert.AreEqual(2, publicView.Occupancy);
        before = await RoomManagementTestSupport.GraphAsync(fixture, owner, visitor.PetId);
        await RoomManagementTestSupport.FaultAsync("room_capacity_conflict", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .UpdateAsync(owner.UserId, owner.SessionId, publicRoom.RoomId, new(Guid.NewGuid(), 1, "shrink", 1), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner, visitor.PetId));
        await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).UpdateAsync(owner.UserId, owner.SessionId,
            privateRoom.RoomId, new(Guid.NewGuid(), 1, "private boundary", 2), CancellationToken.None));
        await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).UpdateAsync(owner.UserId, owner.SessionId,
            publicRoom.RoomId, new(Guid.NewGuid(), 1, "public boundary", 2), CancellationToken.None));
    }

    [TestMethod]
    public async Task DissolveOnlyDetachesActualOccupantsAndRestorePreservesMembershipWithoutMovingPets()
    {
        var clock = Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var sleeper = await PetTestSupport.RegisterAsync(fixture);
        var awayMember = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            foreach (var userId in new[] { sleeper.UserId, awayMember.UserId })
                db.RoomMemberships.Add(new() { RoomId = created.RoomId, UserId = userId, Role = PersistentRoomRole.Member });
            await db.SaveChangesAsync();
        });
        await RoomManagementTestSupport.PlaceAsync(fixture, owner.PetId, created.RoomId);
        await RoomManagementTestSupport.PlaceAsync(fixture, sleeper.PetId, created.RoomId, sleeping: true);
        await RoomManagementTestSupport.PlaceAsync(fixture, awayMember.PetId, awayMember.RoomId, sleeping: true);
        var dissolveKey = Guid.NewGuid();
        var dissolved = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).DissolveAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(dissolveKey, 1, true), CancellationToken.None));
        Assert.AreEqual(2L, dissolved.AppliedVersion);
        var view = await RoomManagementTestSupport.GetAsync(fixture, owner, created.RoomId);
        Assert.AreEqual(clock.GetUtcNow().AddHours(168), view.RecoverUntil);
        Assert.IsTrue(view.Recoverable);
        Assert.AreEqual(3, view.ReservedSeats);
        Assert.AreEqual(0, view.Occupancy);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            foreach (var pet in new[] { owner.PetId, sleeper.PetId })
            {
                var state = await db.PetRoomStates.AsNoTracking().SingleAsync(s => s.PetId == pet);
                Assert.IsNull(state.RoomId); Assert.IsNull(state.PositionX); Assert.IsNull(state.PositionY); Assert.IsFalse(state.IsSleeping);
                Assert.AreEqual(8L, state.Version); Assert.AreEqual(5L, state.ControlGeneration);
                Assert.IsTrue(await db.Pets.AnyAsync(p => p.Id == pet));
            }
            var away = await db.PetRoomStates.AsNoTracking().SingleAsync(s => s.PetId == awayMember.PetId);
            Assert.AreEqual(awayMember.RoomId, away.RoomId); Assert.IsTrue(away.IsSleeping);
            Assert.AreEqual(7L, away.Version); Assert.AreEqual(4L, away.ControlGeneration);
        });
        var restored = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).RestoreAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(Guid.NewGuid(), 2), CancellationToken.None));
        Assert.AreEqual(3L, restored.AppliedVersion);
        view = await RoomManagementTestSupport.GetAsync(fixture, owner, created.RoomId);
        Assert.AreEqual("active", view.Status); Assert.AreEqual(3, view.ReservedSeats); Assert.AreEqual(0, view.Occupancy);
        Assert.IsNull(view.RecoverUntil);
        var replay = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).DissolveAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(dissolveKey, 1, true), CancellationToken.None));
        Assert.AreEqual(dissolved, replay);
        Assert.AreEqual("active", (await RoomManagementTestSupport.GetAsync(fixture, owner, created.RoomId)).Status);
    }

    [TestMethod]
    [DataRow("create", "after_room")]
    [DataRow("create", "after_membership")]
    [DataRow("create", "after_audit")]
    [DataRow("create", "before_commit")]
    [DataRow("update", "after_room")]
    [DataRow("update", "after_audit")]
    [DataRow("update", "before_commit")]
    [DataRow("dissolve", "after_detach")]
    [DataRow("dissolve", "after_room")]
    [DataRow("dissolve", "after_audit")]
    [DataRow("dissolve", "before_commit")]
    [DataRow("restore", "after_room")]
    [DataRow("restore", "after_audit")]
    [DataRow("restore", "before_commit")]
    public async Task InjectedFailureRollsBackRoomMembershipAssignmentAuditAndOutbox(string operation, string failureAt)
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await RoomManagementTestSupport.PlaceAsync(fixture, owner.PetId, created.RoomId, sleeping: true);
        if (operation == "restore")
            await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).DissolveAsync(owner.UserId, owner.SessionId,
                created.RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None));
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        var key = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<RoomManagementTestSupport.InjectedFailure>(() => fixture.InScopeAsync(services =>
        {
            var service = RoomManagementTestSupport.Service(services, new RoomManagementTestSupport.FailureCheckpoint(failureAt));
            return operation switch
            {
                "create" => service.CreateAsync(owner.UserId, owner.SessionId, new(key, "privateShared", "rollback"), CancellationToken.None),
                "update" => service.UpdateAsync(owner.UserId, owner.SessionId, created.RoomId, new(key, 1, "rollback", 5), CancellationToken.None),
                "dissolve" => service.DissolveAsync(owner.UserId, owner.SessionId, created.RoomId, new(key, 1, true), CancellationToken.None),
                _ => service.RestoreAsync(owner.UserId, owner.SessionId, created.RoomId, new(key, 2), CancellationToken.None)
            };
        }));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
        await fixture.InScopeAsync(async services => Assert.IsFalse(await services.GetRequiredService<DangoDbContext>().RoomOperationAudits.AnyAsync(a => a.ActorUserId == owner.UserId && a.RequestId == key)));
    }

    [TestMethod]
    public async Task RestoreRechecksQuotaAndAnExactDeadlineAndRollsBackIfDeadlinePassesAtCommit()
    {
        var clock = Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).DissolveAsync(owner.UserId, owner.SessionId,
            created.RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None));
        foreach (var index in Enumerable.Range(0, 3)) await RoomManagementTestSupport.CreateAsync(fixture, owner, name: $"quota {index}");
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        await RoomManagementTestSupport.FaultAsync("room_quota_reached", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .RestoreAsync(owner.UserId, owner.SessionId, created.RoomId, new(Guid.NewGuid(), 2), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
        var anotherOwner = await PetTestSupport.RegisterAsync(fixture);
        var recover = await RoomManagementTestSupport.CreateAsync(fixture, anotherOwner);
        await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).DissolveAsync(anotherOwner.UserId, anotherOwner.SessionId,
            recover.RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None));
        clock.Advance(TimeSpan.FromHours(168).Subtract(TimeSpan.FromMinutes(1)));
        var login = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().LoginAsync(anotherOwner.Email, anotherOwner.Password, false, CancellationToken.None));
        anotherOwner = anotherOwner with { SessionId = login.SessionId };
        before = await RoomManagementTestSupport.GraphAsync(fixture, anotherOwner);
        var checkpoint = new RoomManagementTestSupport.ActionCheckpoint("before_commit", () => { clock.Advance(TimeSpan.FromMinutes(1)); return Task.CompletedTask; });
        await RoomManagementTestSupport.FaultAsync("room_recovery_expired", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services, checkpoint)
            .RestoreAsync(anotherOwner.UserId, anotherOwner.SessionId, recover.RoomId, new(Guid.NewGuid(), 2), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, anotherOwner));
        await RoomManagementTestSupport.FaultAsync("room_recovery_expired", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .RestoreAsync(anotherOwner.UserId, anotherOwner.SessionId, recover.RoomId, new(Guid.NewGuid(), 2), CancellationToken.None)));
        Assert.IsFalse((await RoomManagementTestSupport.GetAsync(fixture, anotherOwner, recover.RoomId)).Recoverable);
    }

    [TestMethod]
    public async Task DesktopRenameIsAllowedButDissolveAndCapacityChangeAreRejectedWithoutSideEffects()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var changed = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).UpdateAsync(owner.UserId, owner.SessionId,
            owner.RoomId, new(Guid.NewGuid(), 1, "renamed desktop", 2), CancellationToken.None));
        Assert.AreEqual(2L, changed.AppliedVersion);
        var view = await RoomManagementTestSupport.GetAsync(fixture, owner, owner.RoomId);
        Assert.AreEqual("desktop-pet-default", view.TemplateId);
        Assert.AreEqual(1, view.TemplateVersion);
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        await RoomManagementTestSupport.FaultAsync("room_capacity_invalid", 400, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .UpdateAsync(owner.UserId, owner.SessionId, owner.RoomId, new(Guid.NewGuid(), 2, "invalid desktop", 3), CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_desktop_protected", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .DissolveAsync(owner.UserId, owner.SessionId, owner.RoomId, new(Guid.NewGuid(), 2, true), CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_confirmation_required", 400, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .DissolveAsync(owner.UserId, owner.SessionId, owner.RoomId, new(Guid.NewGuid(), 2, false), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
    }

    [TestMethod]
    public async Task AnotherOwnerAndForeignSessionCannotReadModifyDissolveOrRestoreAndDirectoryNeverLeaks()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var other = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .GetAsync(owner.UserId, other.SessionId, created.RoomId, CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_not_found", 404, () => RoomManagementTestSupport.GetAsync(fixture, other, created.RoomId));
        await RoomManagementTestSupport.FaultAsync("room_not_found", 404, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .UpdateAsync(other.UserId, other.SessionId, created.RoomId, new(Guid.NewGuid(), 1, "unauthorized", 1), CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_not_found", 404, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .DissolveAsync(other.UserId, other.SessionId, created.RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("room_not_found", 404, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .RestoreAsync(other.UserId, other.SessionId, created.RoomId, new(Guid.NewGuid(), 1), CancellationToken.None)));
        var list = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).ListAsync(other.UserId, other.SessionId, "all", 50, null, CancellationToken.None));
        Assert.AreEqual(1, list.Rooms.Length); Assert.AreEqual(other.RoomId, list.Rooms[0].RoomId);
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("disabled")]
    [DataRow("recovery_locked")]
    [DataRow("security_stamp")]
    [DataRow("recovery_epoch")]
    [DataRow("expired")]
    public async Task EarlierAuthenticationAndCommittedReceiptDoNotBypassCurrentSessionState(string invalidation)
    {
        var clock = Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var key = Guid.NewGuid();
        var original = await RoomManagementTestSupport.CreateAsync(fixture, owner, requestId: key);
        Assert.IsTrue(await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().ValidateAsync(owner.UserId, owner.SessionId, CancellationToken.None)));
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == owner.UserId);
            var session = await db.AuthSessions.SingleAsync(s => s.Id == owner.SessionId);
            switch (invalidation)
            {
                case "revoked": session.RevokedAt = clock.GetUtcNow(); break;
                case "disabled": user.DisabledAt = clock.GetUtcNow(); break;
                case "recovery_locked": user.RecoveryLocked = true; break;
                case "security_stamp": user.SecurityStamp = Guid.NewGuid().ToString("N"); break;
                case "recovery_epoch": session.RecoveryEpoch = Guid.NewGuid(); break;
                case "expired": clock.Advance(TimeSpan.FromHours(8)); break;
            }
            await db.SaveChangesAsync();
        });
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => RoomManagementTestSupport.CreateAsync(fixture, owner, requestId: key));
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => RoomManagementTestSupport.GetAsync(fixture, owner, original.RoomId));
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .ListAsync(owner.UserId, owner.SessionId, "all", 20, null, CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .UpdateAsync(owner.UserId, owner.SessionId, original.RoomId, new(Guid.NewGuid(), 1, "invalid session", 5), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
    }

    [TestMethod]
    public async Task RevocationBeforeLocksAndSessionExpiryAtCommitBothRejectWithoutLeavingPartialWrites()
    {
        var clock = Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        var revoke = new RoomManagementTestSupport.ActionCheckpoint("before_locks", () => fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .RevokeAsync(owner.UserId, owner.SessionId, CancellationToken.None)));
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services, revoke)
            .UpdateAsync(owner.UserId, owner.SessionId, created.RoomId, new(Guid.NewGuid(), 1, "revoked", 5), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
        var login = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().LoginAsync(owner.Email, owner.Password, false, CancellationToken.None));
        owner = owner with { SessionId = login.SessionId };
        var expiry = new RoomManagementTestSupport.ActionCheckpoint("before_commit", () => { clock.Advance(TimeSpan.FromHours(8)); return Task.CompletedTask; });
        await RoomManagementTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services, expiry)
            .UpdateAsync(owner.UserId, owner.SessionId, created.RoomId, new(Guid.NewGuid(), 1, "expired", 5), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
    }

    [TestMethod]
    public async Task StableCursorPaginationIncludesOnlyOwnedActiveOrDissolvedRoomsWithoutDuplicates()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var first = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await RoomManagementTestSupport.CreateAsync(fixture, owner, "publicShared");
        await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).DissolveAsync(owner.UserId, owner.SessionId,
            first.RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None));
        await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var seen = new List<Guid>();
        Guid? cursor = null;
        for (var page = 0; page < 4; page++)
        {
            var result = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).ListAsync(owner.UserId, owner.SessionId, "all", 2, cursor, CancellationToken.None));
            Assert.IsTrue(result.Rooms.Length <= 2);
            seen.AddRange(result.Rooms.Select(r => r.RoomId));
            cursor = result.Next;
            if (cursor is null) break;
        }
        Assert.IsNull(cursor);
        Assert.AreEqual(5, seen.Count);
        Assert.AreEqual(5, seen.Distinct().Count());
        var dissolved = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).ListAsync(owner.UserId, owner.SessionId, "dissolved", 50, null, CancellationToken.None));
        Assert.AreEqual(first.RoomId, dissolved.Rooms.Single().RoomId);
        var active = await fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services).ListAsync(owner.UserId, owner.SessionId, "active", 50, null, CancellationToken.None));
        Assert.AreEqual(4, active.Rooms.Length);
        Assert.IsFalse(active.Rooms.Any(r => r.RoomId == first.RoomId));
    }

    [TestMethod]
    public async Task RoomAndPetStateVersionExhaustionRefuseTheWholeDissolve()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var created = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        await RoomManagementTestSupport.PlaceAsync(fixture, owner.PetId, created.RoomId);
        await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().PetRoomStates.Where(s => s.PetId == owner.PetId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ControlGeneration, long.MaxValue)));
        var before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        await RoomManagementTestSupport.FaultAsync("room_state_version_exhausted", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .DissolveAsync(owner.UserId, owner.SessionId, created.RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
        await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().Rooms.Where(r => r.Id == created.RoomId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Version, long.MaxValue)));
        before = await RoomManagementTestSupport.GraphAsync(fixture, owner);
        await RoomManagementTestSupport.FaultAsync("room_version_exhausted", 409, () => fixture.InScopeAsync(services => RoomManagementTestSupport.Service(services)
            .DissolveAsync(owner.UserId, owner.SessionId, created.RoomId, new(Guid.NewGuid(), long.MaxValue, true), CancellationToken.None)));
        Assert.AreEqual(before, await RoomManagementTestSupport.GraphAsync(fixture, owner));
    }

    // PostgreSQL timestamptz stores microseconds; keep exact-boundary assertions at that precision.
    private static MutableTestClock Clock() => new(new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero));

    private static async Task<(RoomOperationResult? Result, string? Error)[]> RaceAsync(FormalTestFixture fixture,
        Func<IServiceProvider, int, Task<RoomOperationResult>> operation)
    {
        var connectionIds = new ConcurrentBag<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<(RoomOperationResult?, string?)> Run(int index) => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync();
            connectionIds.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            if (Interlocked.Increment(ref arrived) == 2) release.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { return (await operation(services, index), (string?)null); }
            catch (AccountFault fault) { return ((RoomOperationResult?)null, fault.Code); }
        });
        var results = await Task.WhenAll(Run(0), Run(1));
        Assert.AreEqual(2, connectionIds.Distinct().Count(), "The competing operations must use independent PostgreSQL backends.");
        return results;
    }
}
