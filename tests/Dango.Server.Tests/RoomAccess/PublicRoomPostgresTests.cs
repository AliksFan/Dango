using System.Collections.Concurrent;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class PublicRoomPostgresTests
{
    [TestMethod]
    public async Task CodeResetReplaysAcrossCurrentSessionsWithoutPersistingPlaintextOrRevivingOldCode()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var request = Guid.NewGuid();
        var first = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1, request);
        var second = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, first.RoomVersion);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId, visitor.PetId]);
        var newSession = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .LoginAsync(owner.Email, owner.Password, false, CancellationToken.None));
        var replay = await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services)
            .ResetCodeAsync(owner.UserId, newSession.SessionId, room, new(request, 1), CancellationToken.None));
        Assert.IsTrue(first == replay, "A reset replay must return the original acknowledgement.");
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId, visitor.PetId]));
        await PublicRoomTestSupport.FaultAsync("room_request_conflict", 409,
            () => PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1, request, hours: 169));
        await PublicRoomTestSupport.FaultAsync("room_code_invalid", 404,
            () => PublicRoomTestSupport.ResolveAsync(fixture, visitor, first.Code));
        var door = await PublicRoomTestSupport.ResolveAsync(fixture, visitor, second.Code);
        Assert.AreEqual(room, door.RoomId);
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId, visitor.PetId]),
            "Resolve must not write membership, leases or pet assignment.");
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var credentials = await db.PublicRoomCredentials.AsNoTracking().Where(c => c.RoomId == room).ToArrayAsync();
            Assert.AreEqual(2, credentials.Length); Assert.AreEqual(1, credentials.Count(c => c.RevokedAt is null));
            Assert.IsTrue(credentials.All(c => c.RecoveryEpoch == services.GetRequiredService<AccountEnvironment>().RecoveryEpoch));
            var receipts = await db.RoomOperationAudits.AsNoTracking().Where(a => a.RoomId == room && a.Operation == "code_reset").ToArrayAsync();
            Assert.AreEqual(2, receipts.Length);
            foreach (var receipt in receipts)
            {
                Assert.IsNotNull(receipt.ResultJson);
                using var json = JsonDocument.Parse(receipt.ResultJson);
                Assert.IsFalse(json.RootElement.TryGetProperty("code", out _));
                Assert.IsFalse(receipt.ResultJson.Contains(first.Code, StringComparison.Ordinal));
                Assert.IsFalse(receipt.ResultJson.Contains(second.Code, StringComparison.Ordinal));
            }
            Assert.IsFalse(await db.RoomMemberships.AnyAsync(m => m.RoomId == room && m.UserId == visitor.UserId));
        });
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            (await db.AuthSessions.SingleAsync(s => s.Id == owner.SessionId)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        });
        await PublicRoomTestSupport.FaultAsync("authentication_required", 401,
            () => PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1, request));
    }

    [TestMethod]
    public async Task RotationPreservesCurrentVisitorsAssignmentControlAndLease()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        await PublicRoomTestSupport.LeaseAsync(fixture, visitor, room);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [], [visitor.PetId]);
        await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1);
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [], [visitor.PetId]));
        await fixture.InScopeAsync(async services =>
        {
            var notice = await services.GetRequiredService<DangoDbContext>().RoomDomainEvents
                .SingleAsync(e => e.RoomId == room && e.EventType == "code_reset");
            CollectionAssert.AreEqual(new[] { owner.UserId }, notice.TargetUserIds);
        });
    }

    [TestMethod]
    public async Task BanClearsPresentTargetAndUnbanDoesNotRemigrateThem()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var code = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1);
        var leaseId = await PublicRoomTestSupport.LeaseAsync(fixture, visitor, room);
        await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services).BanAsync(owner.UserId, owner.SessionId,
            room, visitor.UserId, new(Guid.NewGuid(), 2), CancellationToken.None));
        await PublicRoomTestSupport.FaultAsync("room_banned", 403, () => PublicRoomTestSupport.ResolveAsync(fixture, visitor, code.Code));
        var next = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 3);
        await PublicRoomTestSupport.FaultAsync("room_banned", 403, () => PublicRoomTestSupport.ResolveAsync(fixture, visitor, next.Code));
        await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services).UnbanAsync(owner.UserId, owner.SessionId,
            room, visitor.UserId, new(Guid.NewGuid(), 4), CancellationToken.None));
        Assert.AreEqual(room, (await PublicRoomTestSupport.ResolveAsync(fixture, visitor, next.Code)).RoomId);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var state = await db.PetRoomStates.SingleAsync(s => s.PetId == visitor.PetId);
            Assert.IsNull(state.RoomId); Assert.IsNull(state.PositionX); Assert.IsNull(state.PositionY); Assert.IsNull(state.ControlLeaseId);
            Assert.AreEqual(8L, state.Version); Assert.AreEqual(5L, state.ControlGeneration); Assert.IsFalse(state.IsSleeping);
            var lease = await db.RoomDeviceLeases.SingleAsync(l => l.Id == leaseId);
            Assert.IsNotNull(lease.RevokedAt); Assert.IsNull(lease.RoomId); Assert.IsNull(lease.TransportId);
            Assert.IsFalse((await db.RoomBans.SingleAsync(b => b.RoomId == room && b.UserId == visitor.UserId)).Active);
        });
    }

    [TestMethod]
    public async Task ExpiredUnrevokedCredentialDoesNotBlockRotationAndOldRecoveryEpochCannotResolveOrReplay()
    {
        var clock = PublicRoomTestSupport.Clock();
        await using var fixture = await PublicRoomTestSupport.FixtureAsync(clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var first = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1, hours: 1);
        clock.Advance(TimeSpan.FromHours(1));
        var request = Guid.NewGuid();
        var next = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 2, request);
        Assert.AreEqual(3L, next.Generation); Assert.AreEqual(3L, next.RoomVersion);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.IsNotNull((await db.PublicRoomCredentials.SingleAsync(c => c.Id == first.CredentialId)).RevokedAt);
            (await db.PublicRoomCredentials.SingleAsync(c => c.Id == next.CredentialId)).RecoveryEpoch = Guid.NewGuid();
            await db.SaveChangesAsync();
        });
        await PublicRoomTestSupport.FaultAsync("room_code_invalid", 404, () => PublicRoomTestSupport.ResolveAsync(fixture, owner, next.Code));
        await PublicRoomTestSupport.FaultAsync("room_code_invalid", 404, () => PublicRoomTestSupport.ResetAsync(fixture, owner, room, 2, request));
    }

    [TestMethod]
    public async Task ResolveRechecksExpiryAfterItsPrecommitCheckpoint()
    {
        var clock = PublicRoomTestSupport.Clock();
        await using var fixture = await PublicRoomTestSupport.FixtureAsync(clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var code = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1, hours: 1);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId]);
        var checkpoint = new PublicRoomTestSupport.CallbackCheckpoint((name, _, _) =>
        {
            if (name == "before_commit") clock.Advance(TimeSpan.FromHours(1));
            return Task.CompletedTask;
        });
        await PublicRoomTestSupport.FaultAsync("room_code_invalid", 403,
            () => PublicRoomTestSupport.ResolveAsync(fixture, owner, code.Code, checkpoint));
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId]));
    }

    [TestMethod]
    public async Task NewlyResetCodeExpiringBeforeCommitRollsBackRotationAndReceipt()
    {
        var clock = PublicRoomTestSupport.Clock();
        await using var fixture = await PublicRoomTestSupport.FixtureAsync(clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId]);
        var checkpoint = new PublicRoomTestSupport.CallbackCheckpoint((name, _, _) =>
        {
            if (name == "before_commit") clock.Advance(TimeSpan.FromHours(1));
            return Task.CompletedTask;
        });
        await PublicRoomTestSupport.FaultAsync("room_code_invalid", 403, () => PublicRoomTestSupport.ResetAsync(
            fixture, owner, room, 2, hours: 1, checkpoint: checkpoint));
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId]));
    }

    [TestMethod]
    public async Task BanSurvivesRotationAndUnbanPreservesHistoryWithoutChangingAnotherRoomsState()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var roomA = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var roomB = await PublicRoomTestSupport.RoomAsync(fixture, visitor);
        await PublicRoomTestSupport.LeaseAsync(fixture, visitor, roomB);
        var beforeB = await PublicRoomTestSupport.GraphAsync(fixture, [roomB], [visitor.PetId]);
        var code = await PublicRoomTestSupport.ResetAsync(fixture, owner, roomA, 1);
        var request = Guid.NewGuid();
        var ban = await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services)
            .BanAsync(owner.UserId, owner.SessionId, roomA, visitor.UserId, new(request, 2), CancellationToken.None));
        Assert.AreEqual(3L, ban.RoomVersion);
        await PublicRoomTestSupport.FaultAsync("room_banned", 403, () => PublicRoomTestSupport.ResolveAsync(fixture, visitor, code.Code));
        var newCode = await PublicRoomTestSupport.ResetAsync(fixture, owner, roomA, 3);
        await PublicRoomTestSupport.FaultAsync("room_banned", 403, () => PublicRoomTestSupport.ResolveAsync(fixture, visitor, newCode.Code));
        var currentSession = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .LoginAsync(owner.Email, owner.Password, false, CancellationToken.None));
        var replay = await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services)
            .BanAsync(owner.UserId, currentSession.SessionId, roomA, visitor.UserId, new(request, 2), CancellationToken.None));
        Assert.AreEqual(ban, replay);
        var unban = await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services)
            .UnbanAsync(owner.UserId, owner.SessionId, roomA, visitor.UserId, new(Guid.NewGuid(), 4), CancellationToken.None));
        Assert.AreEqual(5L, unban.RoomVersion);
        Assert.AreEqual(roomA, (await PublicRoomTestSupport.ResolveAsync(fixture, visitor, newCode.Code)).RoomId);
        Assert.AreEqual(beforeB, await PublicRoomTestSupport.GraphAsync(fixture, [roomB], [visitor.PetId]));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var storedBan = await db.RoomBans.SingleAsync(b => b.RoomId == roomA && b.UserId == visitor.UserId);
            Assert.IsFalse(storedBan.Active); Assert.AreEqual(2L, storedBan.Version);
            var notice = await db.RoomDomainEvents.SingleAsync(e => e.RoomId == roomA && e.EventType == "ban_set");
            CollectionAssert.AreEquivalent(new[] { owner.UserId, visitor.UserId }, notice.TargetUserIds);
        });
        await PublicRoomTestSupport.FaultAsync("room_target_not_present", 409, () => fixture.InScopeAsync(services =>
            PublicRoomTestSupport.Service(services).RemoveAsync(owner.UserId, owner.SessionId, roomA, visitor.UserId,
                new(Guid.NewGuid(), 5), CancellationToken.None)));
        Assert.AreEqual(beforeB, await PublicRoomTestSupport.GraphAsync(fixture, [roomB], [visitor.PetId]));
    }

    [TestMethod]
    public async Task RemoveClearsOnlyActualOccupancyAndControlWhileCodeStillResolves()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var code = await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1);
        var leaseId = await PublicRoomTestSupport.LeaseAsync(fixture, visitor, room);
        var request = Guid.NewGuid();
        var removed = await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services).RemoveAsync(
            owner.UserId, owner.SessionId, room, visitor.UserId, new(request, 2), CancellationToken.None));
        Assert.AreEqual(3L, removed.RoomVersion);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var state = await db.PetRoomStates.SingleAsync(s => s.PetId == visitor.PetId);
            Assert.IsNull(state.RoomId); Assert.IsNull(state.PositionX); Assert.IsNull(state.PositionY); Assert.IsNull(state.ControlLeaseId);
            Assert.AreEqual(8L, state.Version); Assert.AreEqual(5L, state.ControlGeneration); Assert.IsFalse(state.IsSleeping);
            var lease = await db.RoomDeviceLeases.SingleAsync(l => l.Id == leaseId);
            Assert.IsNotNull(lease.RevokedAt); Assert.IsNull(lease.RoomId); Assert.IsNull(lease.TransportId);
            Assert.IsFalse(await db.RoomBans.AnyAsync(b => b.RoomId == room && b.UserId == visitor.UserId));
            Assert.IsTrue(await db.Pets.AnyAsync(p => p.Id == visitor.PetId && p.UserId == visitor.UserId));
            var notice = await db.RoomDomainEvents.SingleAsync(e => e.RoomId == room && e.EventType == "removed");
            CollectionAssert.AreEquivalent(new[] { owner.UserId, visitor.UserId }, notice.TargetUserIds);
        });
        Assert.AreEqual(room, (await PublicRoomTestSupport.ResolveAsync(fixture, visitor, code.Code)).RoomId);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [visitor.PetId]);
        var repeat = await fixture.InScopeAsync(services => PublicRoomTestSupport.Service(services).RemoveAsync(
            owner.UserId, owner.SessionId, room, visitor.UserId, new(request, 2), CancellationToken.None));
        Assert.AreEqual(removed, repeat);
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [visitor.PetId]));
    }

    [TestMethod]
    [DataRow("after_locks")]
    [DataRow("public_after_revoke")]
    [DataRow("public_after_code")]
    [DataRow("public_after_receipt")]
    [DataRow("after_save")]
    [DataRow("before_commit")]
    public async Task ResetFailuresRollBackRevocationGenerationRoomReceiptAndOutbox(string failureAt)
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 1);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId]);
        var request = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<PublicRoomTestSupport.InjectedFailure>(() => PublicRoomTestSupport.ResetAsync(
            fixture, owner, room, 2, request, checkpoint: new PublicRoomTestSupport.FailureCheckpoint(failureAt)));
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [owner.PetId]));
        Assert.AreEqual(3L, (await PublicRoomTestSupport.ResetAsync(fixture, owner, room, 2, request)).RoomVersion);
    }

    [TestMethod]
    [DataRow("public_after_ban")]
    [DataRow("public_after_detach")]
    [DataRow("public_after_receipt")]
    [DataRow("after_save")]
    [DataRow("before_commit")]
    public async Task BanFailuresRollBackBanOccupancyLeasesControlReceiptAndOutbox(string failureAt)
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        await PublicRoomTestSupport.LeaseAsync(fixture, visitor, room);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [visitor.PetId]);
        var request = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<PublicRoomTestSupport.InjectedFailure>(() => fixture.InScopeAsync(services =>
            PublicRoomTestSupport.Service(services, new PublicRoomTestSupport.FailureCheckpoint(failureAt)).BanAsync(
                owner.UserId, owner.SessionId, room, visitor.UserId, new(request, 1), CancellationToken.None)));
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [visitor.PetId]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IndependentResetsAllowOneMutationAndExactRequestReplay(bool sameRequest)
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var requestId = Guid.NewGuid();
        var connections = new ConcurrentBag<int>();
        var arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoint = new PublicRoomTestSupport.CallbackCheckpoint(async (name, _, ct) =>
        {
            if (name != "before_locks") return;
            if (Interlocked.Increment(ref arrived) == 2) release.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        });
        Task<(Dango.Protocol.RoomCodeResult? Result, string? Error)> RunAsync(Guid key) => fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync(); connections.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            try
            {
                return (await PublicRoomTestSupport.Service(services, checkpoint).ResetCodeAsync(owner.UserId, owner.SessionId,
                room, new(key, 1), CancellationToken.None), (string?)null);
            }
            catch (AccountFault fault) { return ((Dango.Protocol.RoomCodeResult?)null, fault.Code); }
        });
        var results = await Task.WhenAll(RunAsync(requestId), RunAsync(sameRequest ? requestId : Guid.NewGuid()));
        Assert.AreEqual(2, connections.Distinct().Count());
        if (sameRequest) { Assert.IsTrue(results[0].Result == results[1].Result); Assert.IsTrue(results.All(r => r.Error is null)); }
        else { Assert.AreEqual(1, results.Count(r => r.Result is not null)); Assert.AreEqual("room_version_conflict", results.Single(r => r.Error is not null).Error); }
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.PublicRoomCredentials.CountAsync(c => c.RoomId == room));
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(a => a.RoomId == room && a.Operation == "code_reset"));
            Assert.AreEqual(1, await db.RoomDomainEvents.CountAsync(e => e.RoomId == room && e.EventType == "code_reset"));
        });
    }

    [TestMethod]
    public async Task FirstBanDoesNotWaitForTargetCredentialsHeldByAnOpposingRoomWaiter()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room], [visitor.PetId]);
        var targetLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var roomLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = new ConcurrentBag<int>();
        var opposing = fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync(); connections.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await RoomAccessPersistence.AuthenticateAsync(db, visitor.UserId, visitor.SessionId,
                services.GetRequiredService<AccountEnvironment>(), services.GetRequiredService<TimeProvider>(), CancellationToken.None);
            targetLocked.TrySetResult();
            await roomLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            targetWaiting.TrySetResult();
            await RoomPersistenceOperations.OwnerAsync(db, owner.UserId, CancellationToken.None);
            await transaction.CommitAsync();
        });
        await targetLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var checkpoint = new PublicRoomTestSupport.CallbackCheckpoint(async (name, _, ct) =>
        {
            if (name != "after_locks") return;
            roomLocked.TrySetResult();
            await targetWaiting.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        });
        try
        {
            await PublicRoomTestSupport.FaultAsync("room_moderation_conflict", 409, () => fixture.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<DangoDbContext>();
                await db.Database.OpenConnectionAsync(); connections.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
                return await PublicRoomTestSupport.Service(services, checkpoint).BanAsync(owner.UserId, owner.SessionId,
                    room, visitor.UserId, new(Guid.NewGuid(), 1), CancellationToken.None);
            }));
        }
        finally { roomLocked.TrySetResult(); await opposing.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.AreEqual(2, connections.Distinct().Count());
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room], [visitor.PetId]));
    }

    [TestMethod]
    public async Task NonownerPrivateRoomAndTamperedOrForeignSessionAreDeniedWithoutChanges()
    {
        await using var fixture = await PublicRoomTestSupport.FixtureAsync();
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await PublicRoomTestSupport.RoomAsync(fixture, owner);
        var privateRoom = await PublicRoomTestSupport.RoomAsync(fixture, owner, PersistentRoomKind.PrivateShared);
        var before = await PublicRoomTestSupport.GraphAsync(fixture, [room, privateRoom], [owner.PetId, visitor.PetId]);
        await PublicRoomTestSupport.FaultAsync("room_not_found", 404, () => PublicRoomTestSupport.ResetAsync(fixture, visitor, room, 1));
        await PublicRoomTestSupport.FaultAsync("room_public_required", 409, () => PublicRoomTestSupport.ResetAsync(fixture, owner, privateRoom, 1));
        await PublicRoomTestSupport.FaultAsync("authentication_required", 401, () => fixture.InScopeAsync(services =>
            PublicRoomTestSupport.Service(services).ResetCodeAsync(owner.UserId, visitor.SessionId, room, new(Guid.NewGuid(), 1), CancellationToken.None)));
        await PublicRoomTestSupport.FaultAsync("room_moderation_target_invalid", 400, () => fixture.InScopeAsync(services =>
            PublicRoomTestSupport.Service(services).BanAsync(owner.UserId, owner.SessionId, room, owner.UserId,
                new(Guid.NewGuid(), 1), CancellationToken.None)));
        Assert.AreEqual(before, await PublicRoomTestSupport.GraphAsync(fixture, [room, privateRoom], [owner.PetId, visitor.PetId]));
    }
}
