using System.Collections.Concurrent;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.RoomExpiry;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomExpiryPostgresTests
{
    [TestMethod]
    public async Task ExactDeadlinePurgesOnlyScopedDueRoomAndPreservesIdentitiesAndEarlierIntents()
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var due = await RoomExpiryTestSupport.SeedRoomAsync(fixture, owner, clock.GetUtcNow());
        var outsideScope = await RoomExpiryTestSupport.SeedRoomAsync(fixture, owner, clock.GetUtcNow(),
            kind: PersistentRoomKind.PublicShared);
        var future = await RoomExpiryTestSupport.SeedRoomAsync(fixture, owner, clock.GetUtcNow().AddSeconds(1));
        var active = await RoomExpiryTestSupport.SeedRoomAsync(fixture, owner, clock.GetUtcNow(), PersistentRoomStatus.Active);
        await RoomExpiryTestSupport.AssignAsync(fixture, owner.PetId, due);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            // A retained membership is not proof of current pet assignment.
            db.RoomMemberships.Add(new() { RoomId = due, UserId = visitor.UserId, Role = PersistentRoomRole.Member });
            await db.SaveChangesAsync();
        });
        var protectedRooms = new[] { outsideScope, future, active, owner.RoomId, visitor.RoomId };
        var beforeProtected = await RoomExpiryTestSupport.GraphAsync(fixture, protectedRooms, [visitor.PetId]);

        var result = await RoomExpiryTestSupport.PurgeAsync(fixture, [due, future, active, owner.RoomId]);
        Assert.AreEqual(1, result.Attempted);
        Assert.AreEqual(1, result.Deleted);
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(new RoomExpiryCursor(clock.GetUtcNow(), due), result.NextCursor);
        Assert.AreEqual(beforeProtected, await RoomExpiryTestSupport.GraphAsync(fixture, protectedRooms, [visitor.PetId]));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.IsFalse(await db.Rooms.AnyAsync(r => r.Id == due));
            Assert.IsFalse(await db.RoomMemberships.AnyAsync(m => m.RoomId == due));
            Assert.IsTrue(await db.Users.AnyAsync(u => u.Id == owner.UserId));
            Assert.IsTrue(await db.Pets.AnyAsync(p => p.Id == owner.PetId && p.UserId == owner.UserId));
            var state = await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId);
            Assert.IsNull(state.RoomId); Assert.IsNull(state.PositionX); Assert.IsNull(state.PositionY);
            Assert.IsFalse(state.IsSleeping); Assert.AreEqual(10L, state.Version); Assert.AreEqual(4L, state.ControlGeneration);
            var old = await db.RoomOperationAudits.SingleAsync(a => a.RoomId == due && a.Operation == "dissolved");
            Assert.AreEqual(4L, old.AppliedVersion); Assert.AreEqual(owner.UserId, old.ActorUserId);
            Assert.IsNotNull(old.RequestId); Assert.IsNotNull(old.RequestHash);
            var expiry = await db.RoomOperationAudits.SingleAsync(a => a.RoomId == due && a.Operation == "expired");
            Assert.AreEqual(5L, expiry.AppliedVersion); Assert.IsNull(expiry.ActorUserId);
            Assert.IsNull(expiry.RequestId); Assert.IsNull(expiry.RequestHash);
            var outbox = await db.RoomDomainEvents.SingleAsync(e => e.RoomId == due && e.EventType == "expired");
            Assert.AreEqual(expiry.Id, outbox.Id); Assert.AreEqual(5L, outbox.RoomVersion); Assert.IsNull(outbox.PublishedAt);
            Assert.AreEqual(2, await db.RoomOperationAudits.CountAsync(a => a.RoomId == due));
            Assert.AreEqual(2, await db.RoomDomainEvents.CountAsync(e => e.RoomId == due));
        });
        var after = await RoomExpiryTestSupport.GraphAsync(fixture, [due], [owner.PetId]);
        var repeat = await RoomExpiryTestSupport.PurgeAsync(fixture, [due]);
        Assert.AreEqual(0, repeat.Attempted); Assert.AreEqual(0, repeat.Deleted); Assert.AreEqual(0, repeat.Failed);
        Assert.IsNull(repeat.NextCursor);
        Assert.AreEqual(after, await RoomExpiryTestSupport.GraphAsync(fixture, [due], [owner.PetId]));
    }

    [TestMethod]
    [DataRow("expiry_before_locks")]
    [DataRow("expiry_after_locks")]
    [DataRow("expiry_after_detach")]
    [DataRow("expiry_after_memberships")]
    [DataRow("expiry_after_room")]
    [DataRow("expiry_after_audit")]
    [DataRow("expiry_before_commit")]
    public async Task EveryFailureCheckpointRollsBackAssignmentMembersRoomAuditAndOutbox(string failureAt)
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow());
        await RoomExpiryTestSupport.AssignAsync(fixture, account.PetId, room);
        var before = await RoomExpiryTestSupport.GraphAsync(fixture, [room], [account.PetId]);
        var failed = await RoomExpiryTestSupport.PurgeAsync(fixture, [room], checkpoint: new RoomExpiryTestSupport.FailureCheckpoint(failureAt));
        Assert.AreEqual(1, failed.Attempted); Assert.AreEqual(0, failed.Deleted); Assert.AreEqual(1, failed.Failed);
        Assert.AreEqual("room_expiry_failed", failed.Failures.Single().Code);
        Assert.AreEqual(room, failed.Failures.Single().RoomId);
        Assert.AreEqual(before, await RoomExpiryTestSupport.GraphAsync(fixture, [room], [account.PetId]));
        var retry = await RoomExpiryTestSupport.PurgeAsync(fixture, [room]);
        Assert.AreEqual(1, retry.Deleted); Assert.AreEqual(0, retry.Failed);
    }

    [TestMethod]
    [DataRow("room")]
    [DataRow("assignment")]
    [DataRow("control")]
    public async Task ExhaustedVersionsRejectTheWholeTargetWithoutWrapping(string exhausted)
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow());
        await RoomExpiryTestSupport.AssignAsync(fixture, account.PetId, room);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            if (exhausted == "room") (await db.Rooms.SingleAsync(r => r.Id == room)).Version = long.MaxValue;
            else
            {
                var state = await db.PetRoomStates.SingleAsync(s => s.PetId == account.PetId);
                if (exhausted == "assignment") state.Version = long.MaxValue;
                else state.ControlGeneration = long.MaxValue;
            }
            await db.SaveChangesAsync();
        });
        var before = await RoomExpiryTestSupport.GraphAsync(fixture, [room], [account.PetId]);
        var result = await RoomExpiryTestSupport.PurgeAsync(fixture, [room]);
        Assert.AreEqual(0, result.Deleted); Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(before, await RoomExpiryTestSupport.GraphAsync(fixture, [room], [account.PetId]));
    }

    [TestMethod]
    public async Task FailedTargetAdvancesStableCursorAndDoesNotStarveLaterRooms()
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var first = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow(),
            id: Guid.Parse($"10000000-0000-0000-0000-{suffix}"));
        var second = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow(),
            id: Guid.Parse($"20000000-0000-0000-0000-{suffix}"));
        var third = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow().AddSeconds(-1),
            id: Guid.Parse($"30000000-0000-0000-0000-{suffix}"));
        Guid[] scope = [first, second, third];
        // Time precedes ID in the cursor, even when the earlier room has a larger ID.
        var early = await RoomExpiryTestSupport.PurgeAsync(fixture, scope, limit: 1);
        Assert.AreEqual(third, early.NextCursor!.RoomId); Assert.AreEqual(1, early.Deleted);
        var poison = await RoomExpiryTestSupport.PurgeAsync(fixture, scope, limit: 1, after: early.NextCursor,
            checkpoint: new RoomExpiryTestSupport.FailureCheckpoint("expiry_after_audit", first));
        Assert.AreEqual(first, poison.NextCursor!.RoomId); Assert.AreEqual(1, poison.Failed);
        var later = await RoomExpiryTestSupport.PurgeAsync(fixture, scope, limit: 1, after: poison.NextCursor);
        Assert.AreEqual(second, later.NextCursor!.RoomId); Assert.AreEqual(1, later.Deleted);
        var empty = await RoomExpiryTestSupport.PurgeAsync(fixture, scope, limit: 1, after: later.NextCursor);
        Assert.AreEqual(0, empty.Attempted); Assert.IsNull(empty.NextCursor);
        var retry = await RoomExpiryTestSupport.PurgeAsync(fixture, scope, limit: 1, after: empty.NextCursor);
        Assert.AreEqual(first, retry.NextCursor!.RoomId); Assert.AreEqual(1, retry.Deleted);
    }

    [TestMethod]
    public async Task PoisonTargetRollsBackAndContinuesOtherTargetsInTheSameContext()
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var first = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow().AddSeconds(-1));
        var second = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow());
        await RoomExpiryTestSupport.AssignAsync(fixture, account.PetId, first);
        var before = await RoomExpiryTestSupport.GraphAsync(fixture, [first], [account.PetId]);
        var result = await RoomExpiryTestSupport.PurgeAsync(fixture, [first, second], checkpoint:
            new RoomExpiryTestSupport.FailureCheckpoint("expiry_after_audit", first));
        Assert.AreEqual(2, result.Attempted); Assert.AreEqual(1, result.Deleted); Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(before, await RoomExpiryTestSupport.GraphAsync(fixture, [first], [account.PetId]));
        await fixture.InScopeAsync(async services => Assert.IsFalse(await services.GetRequiredService<DangoDbContext>()
            .Rooms.AnyAsync(r => r.Id == second)));
    }

    [TestMethod]
    public async Task FreshLockedLifecycleProtectsAChangedRoomAfterCandidateSelection()
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow());
        var changed = false;
        var checkpoint = new CallbackCheckpoint(async (name, _, ct) =>
        {
            if (name != "expiry_before_locks") return;
            await fixture.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<DangoDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await RoomPersistenceOperations.OwnerAsync(db, account.UserId, ct);
                var current = (await RoomPersistenceOperations.RoomAsync(db, room, ct))!;
                current.Status = PersistentRoomStatus.Active; current.DissolvedAt = current.RecoverUntil = null;
                current.Version++;
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            });
            changed = true;
        });
        var result = await RoomExpiryTestSupport.PurgeAsync(fixture, [room], checkpoint: checkpoint);
        Assert.IsTrue(changed); Assert.AreEqual(1, result.Attempted); Assert.AreEqual(0, result.Deleted); Assert.AreEqual(0, result.Failed);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(PersistentRoomStatus.Active, (await db.Rooms.SingleAsync(r => r.Id == room)).Status);
            Assert.AreEqual(1, await db.RoomMemberships.CountAsync(m => m.RoomId == room));
            Assert.IsFalse(await db.RoomOperationAudits.AnyAsync(a => a.RoomId == room && a.Operation == "expired"));
        });
    }

    [TestMethod]
    public async Task TwoIndependentPurgeConnectionsCommitOnlyOneExpiry()
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow(), PersistentRoomStatus.Expired);
        var arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = new ConcurrentBag<int>();
        var checkpoint = new CallbackCheckpoint(async (name, _, ct) =>
        {
            if (name != "expiry_before_locks") return;
            if (Interlocked.Increment(ref arrived) == 2) release.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        });
        Task<RoomExpiryBatchResult> RunAsync() => fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync();
            connections.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            return await RoomExpiryTestSupport.Service(services, checkpoint).PurgeBatchAsync(1, null, [room], CancellationToken.None);
        });
        var results = await Task.WhenAll(RunAsync(), RunAsync());
        Assert.AreEqual(2, connections.Distinct().Count());
        Assert.AreEqual(1, results.Sum(r => r.Deleted)); Assert.AreEqual(0, results.Sum(r => r.Failed));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.IsFalse(await db.Rooms.AnyAsync(r => r.Id == room));
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(a => a.RoomId == room && a.Operation == "expired"));
            Assert.AreEqual(1, await db.RoomDomainEvents.CountAsync(e => e.RoomId == room && e.EventType == "expired"));
        });
    }

    [TestMethod]
    public async Task CancellationAfterWritesPropagatesAndRollsBack()
    {
        var clock = new MutableTestClock(RoomExpiryTestSupport.Now());
        await using var fixture = await RoomExpiryTestSupport.FixtureAsync(clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomExpiryTestSupport.SeedRoomAsync(fixture, account, clock.GetUtcNow());
        await RoomExpiryTestSupport.AssignAsync(fixture, account.PetId, room);
        var before = await RoomExpiryTestSupport.GraphAsync(fixture, [room], [account.PetId]);
        using var cancellation = new CancellationTokenSource();
        var checkpoint = new CallbackCheckpoint((name, _, ct) =>
        {
            if (name == "expiry_after_audit") { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.InScopeAsync(services =>
            RoomExpiryTestSupport.Service(services, checkpoint).PurgeBatchAsync(1, null, [room], cancellation.Token)));
        Assert.AreEqual(before, await RoomExpiryTestSupport.GraphAsync(fixture, [room], [account.PetId]));
    }

    private sealed class CallbackCheckpoint(Func<string, Guid, CancellationToken, Task> callback) : IRoomManagementCheckpoint
    {
        public Task ReachedAsync(string name, Guid roomId, CancellationToken ct) => callback(name, roomId, ct);
    }
}
