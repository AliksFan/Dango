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
            try
            {
                return (await RoomAccessTestSupport.Service(services).EnterAsync(account.UserId, account.SessionId, room.RoomId,
                new(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion, code), CancellationToken.None), (string?)null);
            }
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

    [TestMethod]
    public async Task SameRequestOnIndependentConnectionsCommitsOneLeaseAndOneReceipt()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var state = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        var request = new RoomEnterRequest(Guid.NewGuid(), Guid.NewGuid(), "desktop", state.RoomId, state.AssignmentVersion);
        var results = await RaceAsync(fixture, (services, _) => RoomAccessTestSupport.Service(services)
            .EnterAsync(owner.UserId, owner.SessionId, room.RoomId, request, CancellationToken.None));
        Assert.IsTrue(results.All(r => r.Error is null)); Assert.AreEqual(results[0].Result, results[1].Result);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.RoomDeviceLeases.CountAsync(l => l.PetId == owner.PetId && l.RevokedAt == null));
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(a => a.ActorUserId == owner.UserId && a.RequestId == request.RequestId));
            Assert.AreEqual(1, await db.RoomDomainEvents.CountAsync(e => e.RoomId == room.RoomId && e.EventType == "entered"));
            Assert.AreEqual(results[0].Result!.LeaseId, (await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId)).ControlLeaseId);
        });
    }

    [TestMethod]
    public async Task IndependentMigrationsUsingOneSourceVersionCannotBothCommit()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var a = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner, "privateShared");
        var b = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var source = await RoomAccessTestSupport.PersonalAsync(fixture, owner);
        var targets = new[] { a.RoomId, b.RoomId };
        var results = await RaceAsync(fixture, (services, index) => RoomAccessTestSupport.Service(services).EnterAsync(owner.UserId, owner.SessionId,
            targets[index], new(Guid.NewGuid(), Guid.NewGuid(), "desktop", source.RoomId, source.AssignmentVersion), CancellationToken.None));
        var winner = results.Single(r => r.Result is not null).Result!;
        Assert.AreEqual("room_assignment_conflict", results.Single(r => r.Error is not null).Error);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var current = await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId);
            Assert.AreEqual(winner.RoomId, current.RoomId); Assert.AreEqual(winner.AssignmentVersion, current.Version);
            Assert.AreEqual(1, await db.RoomDeviceLeases.CountAsync(l => l.PetId == owner.PetId && l.RevokedAt == null));
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(audit => audit.ActorUserId == owner.UserId && audit.Operation == "entered"));
            Assert.AreEqual(1, await db.PetRoomStates.CountAsync(s => targets.Contains(s.RoomId ?? Guid.Empty)));
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AdmissionAndDissolveUseIndependentLockWaitsAndOnlyTheLatestVersionCanDissolve(bool admissionFirst)
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture); var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        var code = await RoomAccessTestSupport.CredentialAsync(fixture, room.RoomId);
        var source = await RoomAccessTestSupport.PersonalAsync(fixture, visitor);
        var enterRequest = new RoomEnterRequest(Guid.NewGuid(), Guid.NewGuid(), "desktop", source.RoomId, source.AssignmentVersion, code);
        var gate = new CompetitionGate();
        var winningPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(RoomAccessResult? Result, string? Error)> Enter(bool hold) => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>(); await db.Database.OpenConnectionAsync();
            (hold ? winningPid : waitingPid).SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            try
            {
                return (await RoomAccessTestSupport.Service(services, hold ? gate : null).EnterAsync(visitor.UserId, visitor.SessionId,
                room.RoomId, enterRequest, CancellationToken.None), (string?)null);
            }
            catch (AccountFault fault) { return ((RoomAccessResult?)null, fault.Code); }
        });
        async Task<string?> Dissolve(bool hold) => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>(); await db.Database.OpenConnectionAsync();
            (hold ? winningPid : waitingPid).SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            var service = new Dango.Server.Rooms.Management.RoomManagementService(db, services.GetRequiredService<AccountEnvironment>(),
                services.GetRequiredService<TimeProvider>(), hold ? gate : new Dango.Server.Rooms.Management.RoomManagementCheckpoint());
            try { await service.DissolveAsync(owner.UserId, owner.SessionId, room.RoomId, new(Guid.NewGuid(), room.AppliedVersion, true), CancellationToken.None); return null; }
            catch (AccountFault fault) { return fault.Code; }
        });
        Task<(RoomAccessResult? Result, string? Error)> enter;
        Task<string?> dissolve;
        if (admissionFirst)
        {
            enter = Enter(true);
            await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            dissolve = Dissolve(false);
        }
        else
        {
            dissolve = Dissolve(true);
            await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            enter = Enter(false);
        }
        try
        {
            var pid = await waitingPid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreNotEqual(await winningPid.Task, pid);
            await RequirePostgresLockWaitAsync(fixture, pid);
        }
        finally { gate.Release.TrySetResult(); }
        var entered = await enter; var dissolved = await dissolve;
        if (admissionFirst)
        {
            Assert.IsNull(entered.Error); Assert.IsNotNull(entered.Result);
            Assert.AreEqual("room_version_conflict", dissolved);
            // The concurrent stale command is rejected. Re-read the committed version and make
            // an explicit new owner request; do not guess a version or lower the CAS gate.
            var latest = await fixture.InScopeAsync(async services => await services.GetRequiredService<DangoDbContext>().Rooms.AsNoTracking().SingleAsync(r => r.Id == room.RoomId));
            Assert.AreEqual(entered.Result.RoomVersion, latest.Version);
            await fixture.InScopeAsync(services => new Dango.Server.Rooms.Management.RoomManagementService(services.GetRequiredService<DangoDbContext>(),
                services.GetRequiredService<AccountEnvironment>(), services.GetRequiredService<TimeProvider>(), new Dango.Server.Rooms.Management.RoomManagementCheckpoint())
                .DissolveAsync(owner.UserId, owner.SessionId, room.RoomId, new(Guid.NewGuid(), latest.Version, true), CancellationToken.None));
        }
        else { Assert.IsNull(dissolved); Assert.AreEqual("room_inactive", entered.Error); Assert.IsNull(entered.Result); }
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(PersistentRoomStatus.Dissolved, (await db.Rooms.SingleAsync(r => r.Id == room.RoomId)).Status);
            var current = await db.PetRoomStates.SingleAsync(s => s.PetId == visitor.PetId);
            if (admissionFirst)
            {
                Assert.IsNull(current.RoomId); Assert.IsNull(current.ControlLeaseId); Assert.IsNull(current.PositionX); Assert.IsNull(current.PositionY);
                Assert.IsFalse(current.IsSleeping);
                Assert.AreEqual(0, await db.RoomDeviceLeases.CountAsync(l => l.PetId == visitor.PetId && l.RevokedAt == null));
            }
            else { Assert.AreEqual(source.RoomId, current.RoomId); Assert.AreEqual(source.AssignmentVersion, current.Version); }
        });
    }

    private static async Task<(RoomAccessResult? Result, string? Error)[]> RaceAsync(FormalTestFixture fixture,
        Func<IServiceProvider, int, Task<RoomAccessResult>> operation)
    {
        var pids = new ConcurrentBag<int>(); var arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(RoomAccessResult? Result, string? Error)> Compete(int index) => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>(); await db.Database.OpenConnectionAsync();
            pids.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            if (Interlocked.Increment(ref arrived) == 2) release.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { return (await operation(services, index), (string?)null); }
            catch (AccountFault fault) { return ((RoomAccessResult?)null, fault.Code); }
        });
        var results = await Task.WhenAll(Compete(0), Compete(1));
        Assert.AreEqual(2, pids.Distinct().Count(), "Separate PostgreSQL backends must actually compete.");
        return results;
    }

    private static Task RequirePostgresLockWaitAsync(FormalTestFixture fixture, int pid) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS \"Value\"").SingleAsync()) return;
            await Task.Delay(50);
        }
        Assert.Fail("The independently opened operation never waited on the winner's PostgreSQL locks.");
    });

    private sealed class CompetitionGate : Dango.Server.Rooms.Access.IRoomAccessCheckpoint, Dango.Server.Rooms.Management.IRoomManagementCheckpoint
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ReachedAsync(string name, Guid subjectId, CancellationToken ct)
        {
            if (name != "after_locks") return;
            Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }
}
