using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class LeasePostgresTests
{
    [TestInitialize]
    public void RequireB4BeforeAnyFixtureConnection() => RoomAccessTestSupport.RequireB4Approval();

    [TestMethod]
    [DataRow("publicShared", false)]
    [DataRow("privateShared", true)]
    public async Task ExpiredAttachCannotReviveAResidenceAndSnapshotAndPersonalFactsMatchOfflineRules(string kind, bool sleeping)
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, kind);
        var instance = Guid.NewGuid(); var entered = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, instance);
        clock.Advance(TimeSpan.FromSeconds(30));
        await RoomAccessTestSupport.FaultAsync("room_lease_expired", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .AttachAsync(owner.UserId, owner.SessionId, new(entered.LeaseId!.Value, instance), "late-connection", CancellationToken.None)));
        var current = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        Assert.AreEqual(sleeping ? room.RoomId : (Guid?)null, current.RoomId);
        Assert.AreEqual(sleeping, current.IsSleeping); Assert.IsNull(current.ControlLeaseId); Assert.AreEqual(0, current.Leases.Length);
        await RoomAccessTestSupport.FaultAsync("room_lease_expired", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .SnapshotAsync(owner.UserId, owner.SessionId, room.RoomId, new(entered.LeaseId!.Value, instance), null, CancellationToken.None)));
        var same = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        Assert.AreEqual(current.AssignmentVersion, same.AssignmentVersion); Assert.AreEqual(current.ControlGeneration, same.ControlGeneration);
        Assert.AreEqual(current.RoomId, same.RoomId); Assert.AreEqual(current.IsSleeping, same.IsSleeping);
    }

    [TestMethod]
    public async Task ObserversKeepThePetOnlineAfterControllerExpiryButMustExplicitlyTakeControlWithCurrentGeneration()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var observer = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, a);
        clock.Advance(TimeSpan.FromSeconds(10));
        var controller = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, b);
        clock.Advance(TimeSpan.FromSeconds(10));
        await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).HeartbeatAsync(owner.UserId, owner.SessionId,
            new(observer.LeaseId!.Value, a), null, CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(20));
        var current = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        Assert.AreEqual(room.RoomId, current.RoomId); Assert.IsFalse(current.IsSleeping); Assert.IsNull(current.ControlLeaseId);
        Assert.AreEqual(observer.LeaseId, current.Leases.Single().LeaseId);
        Assert.IsTrue(current.ControlGeneration > controller.ControlGeneration);
        await RoomAccessTestSupport.FaultAsync("room_assignment_conflict", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .TakeoverAsync(owner.UserId, owner.SessionId, new(Guid.NewGuid(), observer.LeaseId!.Value, a, room.RoomId, observer.AssignmentVersion, observer.ControlGeneration), null, CancellationToken.None)));
        var acquired = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).TakeoverAsync(owner.UserId, owner.SessionId,
            new(Guid.NewGuid(), observer.LeaseId!.Value, a, room.RoomId, current.AssignmentVersion, current.ControlGeneration), null, CancellationToken.None));
        Assert.AreEqual(observer.LeaseId, (await RoomAccessTestSupport.PersonalAsync(fixture, owner)).ControlLeaseId);
        Assert.IsTrue(acquired.ControlGeneration > current.ControlGeneration);
    }

    [TestMethod]
    public async Task HeartbeatIsPureAndRebindingRejectsTheOldTransportAndWrongInstance()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var instance = Guid.NewGuid(); var entered = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, instance);
        var lease = new RoomLeaseRequest(entered.LeaseId!.Value, instance);
        await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).AttachAsync(owner.UserId, owner.SessionId, lease, "first", CancellationToken.None));
        await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).AttachAsync(owner.UserId, owner.SessionId, lease, "second", CancellationToken.None));
        var before = await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            return ((await db.Rooms.SingleAsync(r => r.Id == room.RoomId)).Version, await db.RoomDomainEvents.CountAsync(e => e.RoomId == room.RoomId));
        });
        await RoomAccessTestSupport.FaultAsync("room_transport_conflict", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .HeartbeatAsync(owner.UserId, owner.SessionId, lease, "first", CancellationToken.None)));
        await RoomAccessTestSupport.FaultAsync("room_lease_forbidden", 403, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .HeartbeatAsync(owner.UserId, owner.SessionId, lease with { InstanceId = Guid.NewGuid() }, "second", CancellationToken.None)));
        clock.Advance(TimeSpan.FromSeconds(10));
        var heartbeat = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).HeartbeatAsync(owner.UserId, owner.SessionId, lease, "second", CancellationToken.None));
        Assert.AreEqual(clock.GetUtcNow().AddSeconds(30), heartbeat.LeaseExpiresAt);
        Assert.AreEqual(entered.AssignmentVersion, heartbeat.AssignmentVersion);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(before.Version, (await db.Rooms.SingleAsync(r => r.Id == room.RoomId)).Version);
            Assert.AreEqual(before.Item2, await db.RoomDomainEvents.CountAsync(e => e.RoomId == room.RoomId));
        });
    }

    [TestMethod]
    public async Task EighthDeviceIsAllowedNinthRejectedAndExpiredRowsAreTerminatedBeforeNewAdmission()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        for (var index = 0; index < 8; index++) await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId);
        var state = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        await RoomAccessTestSupport.FaultAsync("room_device_limit", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).EnterAsync(owner.UserId, owner.SessionId,
            room.RoomId, new(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion), CancellationToken.None)));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId));
        clock.Advance(TimeSpan.FromSeconds(30));
        await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId);
        await fixture.InScopeAsync(async services => Assert.AreEqual(1, await services.GetRequiredService<DangoDbContext>().RoomDeviceLeases.CountAsync(l => l.PetId == owner.PetId && l.RevokedAt == null)));
    }

    [TestMethod]
    public async Task ExplicitEndOfAnObserverPreservesControllerAndEndOfLastLeaseAppliesOfflineWithOneRequestReceipt()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture); var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var observer = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, a);
        var controller = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, b);
        var request = new RoomLeaseEndRequest(Guid.NewGuid(), observer.LeaseId!.Value, a);
        var ended = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).EndLeaseAsync(owner.UserId, owner.SessionId, request, null, CancellationToken.None));
        Assert.AreEqual(room.RoomId, ended.RoomId);
        Assert.AreEqual(controller.LeaseId, (await RoomAccessTestSupport.PersonalAsync(fixture, owner)).ControlLeaseId);
        Assert.AreEqual(ended, await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).EndLeaseAsync(owner.UserId, owner.SessionId, request, null, CancellationToken.None)));
        var final = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).EndLeaseAsync(owner.UserId, owner.SessionId,
            new(Guid.NewGuid(), controller.LeaseId!.Value, b), null, CancellationToken.None));
        Assert.IsNull(final.RoomId);
        await fixture.InScopeAsync(async services => Assert.AreEqual(1, await services.GetRequiredService<DangoDbContext>().RoomOperationAudits.CountAsync(audit => audit.ActorUserId == owner.UserId && audit.RequestId == request.RequestId)));
    }

    [TestMethod]
    public async Task LeaseExpiryAtCommitRollsBackAndScopedMaintenanceDoesNotTouchUnlistedPets()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var other = await PetTestSupport.RegisterAsync(fixture);
        var a = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, owner.RoomId);
        await RoomAccessTestSupport.EnterOwnerAsync(fixture, other, other.RoomId);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId, other.PetId);
        var deadline = new RoomAccessTestSupport.ActionCheckpoint("before_commit", () => { clock.Advance(TimeSpan.FromSeconds(30)); return Task.CompletedTask; });
        var current = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        await RoomAccessTestSupport.FaultAsync("room_lease_expired", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services, deadline).TakeoverAsync(owner.UserId, owner.SessionId,
            new(Guid.NewGuid(), a.LeaseId!.Value, current.Leases.Single().InstanceId, owner.RoomId, current.AssignmentVersion, current.ControlGeneration), null, CancellationToken.None)));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId, other.PetId));
        var result = await fixture.InScopeAsync(services => new RoomLeaseMaintenanceService(services.GetRequiredService<DangoDbContext>(), services.GetRequiredService<AccountEnvironment>(),
            services.GetRequiredService<TimeProvider>(), new RoomAccessCheckpoint()).RunBatchAsync(50, [owner.PetId], CancellationToken.None));
        Assert.AreEqual(1, result.Examined); Assert.AreEqual(1, result.Changed); Assert.AreEqual(0, result.Failed);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.IsTrue((await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId)).IsSleeping);
            Assert.IsFalse((await db.PetRoomStates.SingleAsync(s => s.PetId == other.PetId)).IsSleeping);
        });
    }

    [TestMethod]
    public async Task FailedFirstPetCannotStarveLaterDuePetsAndAnEmptyCursorPageCanWrap()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var accounts = new List<PetTestSupport.Account>();
        for (var index = 0; index < 3; index++)
        {
            var account = await PetTestSupport.RegisterAsync(fixture); accounts.Add(account);
            await RoomAccessTestSupport.EnterOwnerAsync(fixture, account, account.RoomId);
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        var ordered = accounts.OrderBy(a => a.PetId).ToArray(); var scope = ordered.Select(a => a.PetId).ToArray();
        var fail = new PetFailureCheckpoint(ordered[0].PetId);
        Task<RoomLeaseSweepResult> Batch(Guid? cursor) => fixture.InScopeAsync(services => new RoomLeaseMaintenanceService(services.GetRequiredService<DangoDbContext>(),
            services.GetRequiredService<AccountEnvironment>(), clock, fail).RunBatchAsync(1, scope, CancellationToken.None, cursor));
        var first = await Batch(null); Assert.AreEqual(1, first.Failed); Assert.AreEqual(ordered[0].PetId, first.NextCursor);
        var second = await Batch(first.NextCursor); Assert.AreEqual(1, second.Changed); Assert.AreEqual(ordered[1].PetId, second.NextCursor);
        var third = await Batch(second.NextCursor); Assert.AreEqual(1, third.Changed); Assert.IsNull(third.NextCursor);
        var empty = await Batch(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.AreEqual(0, empty.Examined); Assert.IsNull(empty.NextCursor);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.IsFalse((await db.PetRoomStates.SingleAsync(s => s.PetId == ordered[0].PetId)).IsSleeping);
            foreach (var account in ordered.Skip(1)) Assert.IsTrue((await db.PetRoomStates.SingleAsync(s => s.PetId == account.PetId)).IsSleeping);
        });
    }

    [TestMethod]
    public async Task CrossSessionObserversReceiveNewIdsWithoutExtendingDeadlinesAndMobileIsEndedWhenMovingToDesktop()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var source = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var target = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, "privateShared");
        var second = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().LoginAsync(owner.Email, owner.Password, false, CancellationToken.None));
        var third = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().LoginAsync(owner.Email, owner.Password, false, CancellationToken.None));
        var desktopObserver = owner with { SessionId = second.SessionId }; var mobileObserver = owner with { SessionId = third.SessionId };
        var actorInstance = Guid.NewGuid(); var desktopInstance = Guid.NewGuid(); var mobileInstance = Guid.NewGuid();
        await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, source.RoomId, actorInstance);
        var oldDesktop = await RoomAccessTestSupport.EnterOwnerAsync(fixture, desktopObserver, source.RoomId, desktopInstance);
        await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).AttachAsync(owner.UserId, second.SessionId,
            new(oldDesktop.LeaseId!.Value, desktopInstance), "old-observer-transport", CancellationToken.None));
        var oldMobile = await RoomAccessTestSupport.EnterOwnerAsync(fixture, mobileObserver, source.RoomId, mobileInstance, "mobile");
        clock.Advance(TimeSpan.FromSeconds(5));
        await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, target.RoomId, actorInstance);
        var migratedDesktop = (await RoomAccessTestSupport.PersonalAsync(fixture, desktopObserver)).Leases.Single();
        Assert.AreNotEqual(oldDesktop.LeaseId, migratedDesktop.LeaseId); Assert.AreEqual(oldDesktop.LeaseExpiresAt, migratedDesktop.ExpiresAt); Assert.IsFalse(migratedDesktop.Controls);
        var migratedMobile = (await RoomAccessTestSupport.PersonalAsync(fixture, mobileObserver)).Leases.Single();
        Assert.AreNotEqual(oldMobile.LeaseId, migratedMobile.LeaseId); Assert.AreEqual(oldMobile.LeaseExpiresAt, migratedMobile.ExpiresAt);
        await RoomAccessTestSupport.FaultAsync("room_lease_expired", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .EndLeaseAsync(owner.UserId, second.SessionId, new(Guid.NewGuid(), oldDesktop.LeaseId!.Value, desktopInstance), "old-observer-transport", CancellationToken.None)));
        await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).AttachAsync(owner.UserId, second.SessionId,
            new(migratedDesktop.LeaseId, desktopInstance), "new-observer-transport", CancellationToken.None));
        var snapshot = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).SnapshotAsync(owner.UserId, second.SessionId, target.RoomId,
            new(migratedDesktop.LeaseId, desktopInstance), "new-observer-transport", CancellationToken.None));
        Assert.AreEqual(owner.PetId, snapshot.Occupants.Single().PetId);
        await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, owner.RoomId, actorInstance);
        Assert.AreEqual(0, (await RoomAccessTestSupport.PersonalAsync(fixture, mobileObserver)).Leases.Length);
        await RoomAccessTestSupport.FaultAsync("room_lease_expired", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .AttachAsync(owner.UserId, third.SessionId, new(migratedMobile.LeaseId, mobileInstance), "mobile-cannot-revive", CancellationToken.None)));
        var desktop = (await RoomAccessTestSupport.PersonalAsync(fixture, desktopObserver)).Leases.Single();
        Assert.AreNotEqual(migratedDesktop.LeaseId, desktop.LeaseId); Assert.AreEqual("desktop", desktop.DeviceKind);
    }

    [TestMethod]
    [DataRow("leave", "after_save")]
    [DataRow("leave", "before_commit")]
    [DataRow("takeover", "after_save")]
    [DataRow("takeover", "before_commit")]
    [DataRow("end", "after_save")]
    [DataRow("end", "before_commit")]
    public async Task RequestedControlMutationFailureRollsBackEverySavedFact(string operation, string step)
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture); var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var instance = Guid.NewGuid(); var entered = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, room.RoomId, instance);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId);
        var request = new RoomControlRequest(Guid.NewGuid(), entered.LeaseId!.Value, instance, room.RoomId, entered.AssignmentVersion, entered.ControlGeneration);
        await Assert.ThrowsExactlyAsync<RoomAccessTestSupport.InjectedFailure>(() => fixture.InScopeAsync(services =>
        {
            var service = RoomAccessTestSupport.Service(services, new RoomAccessTestSupport.FailureCheckpoint(step));
            return operation switch
            {
                "leave" => service.LeaveAsync(owner.UserId, owner.SessionId, request, null, CancellationToken.None),
                "takeover" => service.TakeoverAsync(owner.UserId, owner.SessionId, request, null, CancellationToken.None),
                _ => service.EndLeaseAsync(owner.UserId, owner.SessionId, new(request.RequestId, request.LeaseId, instance), null, CancellationToken.None)
            };
        }));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId));
    }

    private sealed class PetFailureCheckpoint(Guid failedPet) : IRoomAccessCheckpoint
    {
        public Task ReachedAsync(string name, Guid petId, CancellationToken ct) => name == "after_locks" && petId == failedPet
            ? throw new RoomAccessTestSupport.InjectedFailure() : Task.CompletedTask;
    }
}
