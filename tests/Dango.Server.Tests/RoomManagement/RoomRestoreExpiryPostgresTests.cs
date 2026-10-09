using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Dango.Server.Tests.RoomExpiry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dango.Server.Tests.RoomManagement;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomRestoreExpiryPostgresTests
{
    [TestMethod]
    public async Task RestoreCrossingExactDeadlineRollsBackWhileIndependentExpiryDeletesOnlyItsTarget()
    {
        RoomManagementHttpTests.RequireApproval();
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var request = new RoomCreateRequest(Guid.NewGuid(), "privateShared", "Fictional restoration race");
        var created = await fixture.InScopeAsync(services => Service(services).CreateAsync(owner.UserId, owner.SessionId, request, default));
        await fixture.InScopeAsync(services => Service(services).DissolveAsync(owner.UserId, owner.SessionId, created.RoomId,
            new RoomDissolveRequest(Guid.NewGuid(), 1, true), default));
        var desktopBefore = await RoomExpiryTestSupport.GraphAsync(fixture, [owner.RoomId], [owner.PetId]);
        clock.Advance(TimeSpan.FromHours(168) - TimeSpan.FromSeconds(1));
        var freshLogin = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .LoginAsync(owner.Email, owner.Password, false, default));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCheckpoint = new CallbackCheckpoint(async (name, _, ct) =>
        {
            if (name != "before_commit") return;
            ready.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        });
        var expiryCheckpoint = new CallbackCheckpoint((name, _, _) =>
        {
            if (name == "expiry_before_locks") selected.TrySetResult();
            return Task.CompletedTask;
        });
        var restoreConnection = 0;
        var expiryConnection = 0;
        var restoreKey = Guid.NewGuid();
        var restore = fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync();
            restoreConnection = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
            return await Service(services, restoreCheckpoint).RestoreAsync(owner.UserId, freshLogin.SessionId, created.RoomId,
                new RoomRestoreRequest(restoreKey, 2), default);
        });
        Task<RoomExpiryBatchResult>? expiry = null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(1));
            expiry = fixture.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<DangoDbContext>();
                await db.Database.OpenConnectionAsync();
                expiryConnection = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
                return await new RoomExpiryService(db, clock, expiryCheckpoint).PurgeBatchAsync(1, null, [created.RoomId], default);
            });
            // A candidate was read on a second connection while restoration holds the owner/room locks.
            await selected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.TrySetResult(); }
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(async () => await restore);
        Assert.AreEqual("room_recovery_expired", fault.Code);
        var result = await expiry!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreNotEqual(restoreConnection, expiryConnection);
        Assert.AreEqual(1, result.Deleted);
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(desktopBefore, await RoomExpiryTestSupport.GraphAsync(fixture, [owner.RoomId], [owner.PetId]));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.IsFalse(await db.Rooms.AnyAsync(r => r.Id == created.RoomId));
            Assert.IsFalse(await db.RoomMemberships.AnyAsync(m => m.RoomId == created.RoomId));
            Assert.IsFalse(await db.RoomOperationAudits.AnyAsync(a => a.RequestId == restoreKey));
            Assert.IsFalse(await db.RoomDomainEvents.AnyAsync(e => e.RoomId == created.RoomId && e.EventType == "restored"));
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(a => a.RoomId == created.RoomId && a.Operation == "expired" && a.AppliedVersion == 3));
            Assert.AreEqual(3, await db.RoomDomainEvents.CountAsync(e => e.RoomId == created.RoomId));
        });
        Assert.AreEqual(0, (await RoomExpiryTestSupport.PurgeAsync(fixture, [created.RoomId])).Attempted);
        // Original acknowledgement remains durable even after permanent expiry; it cannot recreate a room.
        Assert.AreEqual(created, await fixture.InScopeAsync(services => Service(services)
            .CreateAsync(owner.UserId, freshLogin.SessionId, request, default)));
        await fixture.InScopeAsync(async services => Assert.IsFalse(await services.GetRequiredService<DangoDbContext>()
            .Rooms.AnyAsync(r => r.Id == created.RoomId)));
    }

    [TestMethod]
    public async Task ActualRestoreBeforeDeadlineSurvivesLaterScopedExpiryAndDoesNotMovePet()
    {
        RoomManagementHttpTests.RequireApproval();
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var created = await fixture.InScopeAsync(services => Service(services).CreateAsync(owner.UserId, owner.SessionId,
            new RoomCreateRequest(Guid.NewGuid(), "privateShared", "Fictional restored room"), default));
        await fixture.InScopeAsync(services => Service(services).DissolveAsync(owner.UserId, owner.SessionId, created.RoomId,
            new RoomDissolveRequest(Guid.NewGuid(), 1, true), default));
        clock.Advance(TimeSpan.FromHours(168) - TimeSpan.FromSeconds(1));
        var fresh = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().LoginAsync(owner.Email, owner.Password, false, default));
        var restored = await fixture.InScopeAsync(services => Service(services).RestoreAsync(owner.UserId, fresh.SessionId, created.RoomId,
            new RoomRestoreRequest(Guid.NewGuid(), 2), default));
        Assert.AreEqual(3L, restored.AppliedVersion);
        var before = await RoomExpiryTestSupport.GraphAsync(fixture, [created.RoomId, owner.RoomId], [owner.PetId]);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, (await RoomExpiryTestSupport.PurgeAsync(fixture, [created.RoomId])).Attempted);
        Assert.AreEqual(before, await RoomExpiryTestSupport.GraphAsync(fixture, [created.RoomId, owner.RoomId], [owner.PetId]));
        var room = await fixture.InScopeAsync(services => Service(services).GetAsync(owner.UserId, fresh.SessionId, created.RoomId, default));
        Assert.AreEqual("active", room.Status);
        Assert.AreEqual(0, room.Occupancy);
    }

    private static RoomManagementService Service(IServiceProvider services, IRoomManagementCheckpoint? checkpoint = null) =>
        new(services.GetRequiredService<DangoDbContext>(), services.GetRequiredService<AccountEnvironment>(),
            services.GetRequiredService<TimeProvider>(), checkpoint ?? new RoomManagementCheckpoint());

    private sealed class CallbackCheckpoint(Func<string, Guid, CancellationToken, Task> callback) : IRoomManagementCheckpoint
    {
        public Task ReachedAsync(string name, Guid roomId, CancellationToken ct) => callback(name, roomId, ct);
    }
}
