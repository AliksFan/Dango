using System.Collections.Concurrent;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class AdmissionPostgresTests
{
    [TestInitialize]
    public void RequireB4BeforeCreatingAnyFixture() => RoomAccessTestSupport.RequireB4Approval();

    [TestMethod]
    public async Task IndependentConnectionsCompetingForTheFinalPublicSeatCommitOnlyOneAssignment()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, capacity: 1);
        var code = await RoomAccessTestSupport.CredentialAsync(fixture, room.RoomId);
        var aState = await RoomAccessTestSupport.PersonalAsync(fixture, a); var bState = await RoomAccessTestSupport.PersonalAsync(fixture, b);
        var pids = new ConcurrentBag<int>(); var arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(RoomAccessResult? Result, string? Error)> Compete(PetTestSupport.Account account, PersonalRoomSnapshot state) => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>(); await db.Database.OpenConnectionAsync();
            pids.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            if (Interlocked.Increment(ref arrived) == 2) release.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { return (await RoomAccessTestSupport.Service(services).EnterAsync(account.UserId, account.SessionId, room.RoomId,
                new(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion, code), CancellationToken.None), (string?)null); }
            catch (AccountFault fault) { return ((RoomAccessResult?)null, fault.Code); }
        });
        var results = await Task.WhenAll(Compete(a, aState), Compete(b, bState));
        Assert.AreEqual(2, pids.Distinct().Count());
        var winner = results.Single(r => r.Result is not null).Result!;
        Assert.AreEqual("room_full", results.Single(r => r.Error is not null).Error);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.PetRoomStates.CountAsync(s => s.RoomId == room.RoomId));
            Assert.AreEqual(1, await db.RoomDeviceLeases.CountAsync(l => l.RoomId == room.RoomId && l.RevokedAt == null));
            Assert.AreEqual(winner.PetId, (await db.PetRoomStates.SingleAsync(s => s.RoomId == room.RoomId)).PetId);
        });
    }

    [TestMethod]
    public async Task EnterReplayIsSessionAndInstanceBoundAndReturnsOnlyTheOriginalAcknowledgement()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var state = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        var request = new RoomEnterRequest(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion);
        Task<RoomAccessResult> Enter(RoomEnterRequest body, Guid? session = null) => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .EnterAsync(owner.UserId, session ?? owner.SessionId, room.RoomId, body, CancellationToken.None));
        var original = await Enter(request);
        var replay = await Enter(request);
        Assert.AreEqual(original, replay);
        await RoomAccessTestSupport.FaultAsync("room_request_conflict", 409, () => Enter(request with { InstanceId = Guid.NewGuid() }));
        var secondSession = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().LoginAsync(owner.Email, owner.Password, false, CancellationToken.None));
        await RoomAccessTestSupport.FaultAsync("room_request_conflict", 409, () => Enter(request, secondSession.SessionId));
        var moved = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, owner.RoomId);
        Assert.AreEqual(owner.RoomId, moved.RoomId);
        Assert.AreEqual(original, await Enter(request));
        Assert.AreEqual(owner.RoomId, (await RoomAccessTestSupport.PersonalAsync(fixture, owner)).RoomId);
        await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().RevokeAsync(owner.UserId, owner.SessionId, CancellationToken.None));
        await RoomAccessTestSupport.FaultAsync("authentication_required", 401, () => Enter(request));
        await fixture.InScopeAsync(async services => Assert.AreEqual(1, await services.GetRequiredService<DangoDbContext>()
            .RoomOperationAudits.CountAsync(a => a.ActorUserId == owner.UserId && a.RequestId == request.RequestId)));
    }

    [TestMethod]
    public async Task SameInstanceMigrationAllocatesANewLeaseAndLateEndOfTheOldLeaseCannotClearTheNewRoom()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var a = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, "privateShared");
        var b = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var instance = Guid.NewGuid();
        var first = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, a.RoomId, instance);
        var second = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, b.RoomId, instance);
        Assert.AreNotEqual(first.LeaseId, second.LeaseId);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId);
        await RoomAccessTestSupport.FaultAsync("room_lease_expired", 409, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .EndLeaseAsync(owner.UserId, owner.SessionId, new(Guid.NewGuid(), first.LeaseId!.Value, instance), null, CancellationToken.None)));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var old = await db.RoomDeviceLeases.SingleAsync(l => l.Id == first.LeaseId);
            Assert.IsNotNull(old.RevokedAt); Assert.IsNull(old.RoomId); Assert.IsNull(old.TransportId);
            Assert.AreEqual(second.LeaseId, (await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId)).ControlLeaseId);
        });
        var repeated = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, b.RoomId, instance);
        Assert.AreNotEqual(second.LeaseId, repeated.LeaseId);
    }

    [TestMethod]
    [DataRow("after_old_leases")]
    [DataRow("after_save")]
    [DataRow("before_commit")]
    public async Task MigrationFailureRollsBackBothRoomsAssignmentLeasesAuditAndOutbox(string step)
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var active = await RoomAccessTestSupport.EnterOwnerAsync(fixture, owner, owner.RoomId);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId);
        await Assert.ThrowsExactlyAsync<RoomAccessTestSupport.InjectedFailure>(() => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services,
            new RoomAccessTestSupport.FailureCheckpoint(step)).EnterAsync(owner.UserId, owner.SessionId, room.RoomId,
                new(Guid.NewGuid(), Guid.NewGuid(), "desktop", active.RoomId, active.AssignmentVersion), CancellationToken.None)));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId));
    }

    [TestMethod]
    public async Task CodeExpiryAtCommitRollsBackSavedAdmissionAndDoesNotStoreThePlainCode()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var code = await RoomAccessTestSupport.CredentialAsync(fixture, room.RoomId, lifetimeSeconds: 1);
        var state = await RoomAccessTestSupport.PersonalAsync(fixture, visitor);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId, visitor.PetId);
        var deadline = new RoomAccessTestSupport.ActionCheckpoint("before_commit", () => { clock.Advance(TimeSpan.FromSeconds(1)); return Task.CompletedTask; });
        await RoomAccessTestSupport.FaultAsync("room_code_invalid", 403, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services, deadline)
            .EnterAsync(visitor.UserId, visitor.SessionId, room.RoomId, new(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion, code), CancellationToken.None)));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, owner.PetId, visitor.PetId));
        Assert.IsFalse(before.Contains(code, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ForeignSessionPrivateVisitorAndMobileDesktopAdmissionAreRejectedWithoutMovingThePet()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture); var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, "privateShared");
        var state = await RoomAccessTestSupport.PersonalAsync(fixture, visitor);
        var before = await RoomAccessTestSupport.GraphAsync(fixture, visitor.PetId);
        await RoomAccessTestSupport.FaultAsync("room_access_forbidden", 403, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .EnterAsync(visitor.UserId, visitor.SessionId, room.RoomId, new(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion), CancellationToken.None)));
        await RoomAccessTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .EnterAsync(visitor.UserId, owner.SessionId, room.RoomId, new(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion), CancellationToken.None)));
        await RoomAccessTestSupport.FaultAsync("room_device_forbidden", 403, () => fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services)
            .EnterAsync(visitor.UserId, visitor.SessionId, visitor.RoomId, new(Guid.NewGuid(), Guid.NewGuid(), "mobile", state.RoomId, state.AssignmentVersion), CancellationToken.None)));
        Assert.AreEqual(before, await RoomAccessTestSupport.GraphAsync(fixture, visitor.PetId));
    }

    [TestMethod]
    public async Task PublicCapacityAndSnapshotFilterOtherExpiredLeasesWithoutLockingOrRevivingThosePets()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture); var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, capacity: 1);
        var code = await RoomAccessTestSupport.CredentialAsync(fixture, room.RoomId);
        await RoomAccessTestSupport.EnterVisitorAsync(fixture, a, room.RoomId, code);
        clock.Advance(TimeSpan.FromSeconds(30));
        var instance = Guid.NewGuid(); var current = await RoomAccessTestSupport.EnterVisitorAsync(fixture, b, room.RoomId, code, instance);
        var snapshot = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).SnapshotAsync(b.UserId, b.SessionId, room.RoomId,
            new(current.LeaseId!.Value, instance), null, CancellationToken.None));
        Assert.AreEqual(b.PetId, snapshot.Occupants.Single().PetId); Assert.AreEqual(b.UserId, snapshot.Occupants.Single().UserId);
        await fixture.InScopeAsync(async services => Assert.IsNull((await services.GetRequiredService<DangoDbContext>()
            .PetRoomStates.SingleAsync(s => s.PetId == a.PetId)).RoomId, "Admission's scoped pre-sweep removed only the expired target residence."));
        Assert.IsFalse(await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).ReconcilePetAsync(a.PetId, CancellationToken.None)));
    }

    [TestMethod]
    public async Task TwelveExpiredPublicResidentsWithWorkerDisabledCannotCreateAThirteenthPhysicalAssignment()
    {
        var clock = RoomAccessTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, capacity: 12);
        var code = await RoomAccessTestSupport.CredentialAsync(fixture, room.RoomId);
        var oldPetIds = new List<Guid>();
        for (var index = 0; index < 12; index++)
        {
            var visitor = await PetTestSupport.RegisterAsync(fixture);
            oldPetIds.Add(visitor.PetId);
            await RoomAccessTestSupport.EnterVisitorAsync(fixture, visitor, room.RoomId, code);
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        var newcomer = await PetTestSupport.RegisterAsync(fixture);
        var instance = Guid.NewGuid(); var entered = await RoomAccessTestSupport.EnterVisitorAsync(fixture, newcomer, room.RoomId, code, instance);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.PetRoomStates.CountAsync(s => s.RoomId == room.RoomId));
            Assert.AreEqual(0, await db.PetRoomStates.CountAsync(s => oldPetIds.Contains(s.PetId) && s.RoomId != null));
            Assert.AreEqual(0, await db.RoomDeviceLeases.CountAsync(l => oldPetIds.Contains(l.PetId) && l.RevokedAt == null));
            var management = new Dango.Server.Rooms.Management.RoomManagementService(db, services.GetRequiredService<AccountEnvironment>(), clock,
                new Dango.Server.Rooms.Management.RoomManagementCheckpoint());
            var latest = await management.GetAsync(owner.UserId, owner.SessionId, room.RoomId, CancellationToken.None);
            Assert.AreEqual(1, latest.Occupancy);
            await management.UpdateAsync(owner.UserId, owner.SessionId, room.RoomId, new(Guid.NewGuid(), latest.Version, "one remaining visitor", 1), CancellationToken.None);
        });
        var snapshot = await fixture.InScopeAsync(services => RoomAccessTestSupport.Service(services).SnapshotAsync(newcomer.UserId, newcomer.SessionId,
            room.RoomId, new(entered.LeaseId!.Value, instance), null, CancellationToken.None));
        Assert.AreEqual(1, snapshot.Capacity); Assert.AreEqual(newcomer.PetId, snapshot.Occupants.Single().PetId);
    }

    [TestMethod]
    public async Task SnapshotFailsRatherThanLoadingOrReturningAnOverBoundOccupancyGraph()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, capacity: 12);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var current = await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId);
            current.RoomId = room.RoomId; current.PositionX = current.PositionY = 0; current.IsSleeping = false;
            await db.SaveChangesAsync();
        });
        for (var index = 0; index < 12; index++)
        {
            var other = await PetTestSupport.RegisterAsync(fixture);
            await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().PetRoomStates.Where(s => s.PetId == other.PetId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.RoomId, room.RoomId)));
        }
        await RoomAccessTestSupport.FaultAsync("room_occupancy_invalid", 503, () => fixture.InScopeAsync(async services =>
        {
            var persistent = await services.GetRequiredService<DangoDbContext>().Rooms.AsNoTracking().SingleAsync(r => r.Id == room.RoomId);
            await RoomAccessTestSupport.Service(services).OccupantsAsync(persistent, CancellationToken.None);
        }));
    }
}
