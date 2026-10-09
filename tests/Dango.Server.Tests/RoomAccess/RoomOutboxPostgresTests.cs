using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Dango.Server.Tests.RoomsManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomOutboxPostgresTests
{
    [TestMethod]
    public async Task SendSucceededButPublishFailedRetriesSameEventIdWithoutLosingSnapshot()
    {
        RoomAccessHttpHubTests.RequireApproval();
        var sender = new RecordingSender(); var checkpoint = new FailOnce();
        await using var fixture = await FormalTestFixture.CreateAsync(configure: services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IRoomNoticeSender>(sender));
            services.Replace(ServiceDescriptor.Singleton<IRoomOutboxCheckpoint>(checkpoint));
        });
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var eventId = await RoomAccessHttpHubTests.LatestEventAsync(fixture, room.RoomId);
        await AddPersonalConnectionAsync(fixture, owner, "fictional-transport");
        var first = await DispatchAsync(fixture, eventId);
        Assert.AreEqual(1, first.Failed); Assert.AreEqual(0, first.Published);
        await fixture.InScopeAsync(async s => Assert.IsNull((await s.GetRequiredService<DangoDbContext>()
            .RoomDomainEvents.AsNoTracking().SingleAsync(e => e.Id == eventId)).PublishedAt));
        var second = await DispatchAsync(fixture, eventId);
        Assert.AreEqual(1, second.Published); Assert.AreEqual(0, second.Failed);
        Assert.HasCount(2, sender.Notices);
        Assert.IsTrue(sender.Notices.All(n => n.EventId == eventId));
        Assert.AreEqual(0, (await DispatchAsync(fixture, eventId)).Examined);
        Assert.AreEqual(room.RoomId, (await RoomManagementTestSupport.GetAsync(fixture, owner, room.RoomId)).RoomId);
    }

    [TestMethod]
    public async Task RevokedCandidateReceivesNoNoticeEvenWhenRegistryStillContainsIt()
    {
        RoomAccessHttpHubTests.RequireApproval();
        var sender = new RecordingSender();
        await using var fixture = await FormalTestFixture.CreateAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IRoomNoticeSender>(sender)));
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var eventId = await RoomAccessHttpHubTests.LatestEventAsync(fixture, room.RoomId);
        await AddPersonalConnectionAsync(fixture, owner, "revoked-transport");
        await fixture.InScopeAsync(s => s.GetRequiredService<SessionService>().RevokeAsync(owner.UserId, owner.SessionId, CancellationToken.None));
        Assert.AreEqual(1, (await DispatchAsync(fixture, eventId)).Published);
        Assert.HasCount(0, sender.Notices);
    }

    [TestMethod]
    public async Task FailureCursorLetsLaterEventsPublishBeforeRetryingEarlierFailure()
    {
        RoomAccessHttpHubTests.RequireApproval();
        var sender = new ConditionalSender();
        await using var fixture = await FormalTestFixture.CreateAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IRoomNoticeSender>(sender)));
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var early = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var later = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var earlyId = await RoomAccessHttpHubTests.LatestEventAsync(fixture, early.RoomId);
        var laterId = await RoomAccessHttpHubTests.LatestEventAsync(fixture, later.RoomId);
        sender.FailId = earlyId;
        await AddPersonalConnectionAsync(fixture, owner, "cursor-transport");
        var first = await fixture.InScopeAsync(s => s.GetRequiredService<RoomOutboxDispatcher>()
            .DispatchBatchAsync(1, [earlyId, laterId], CancellationToken.None));
        Assert.AreEqual(1, first.Failed); Assert.IsNotNull(first.NextCursor);
        var second = await fixture.InScopeAsync(s => s.GetRequiredService<RoomOutboxDispatcher>()
            .DispatchBatchAsync(1, [earlyId, laterId], CancellationToken.None, first.NextCursor));
        Assert.AreEqual(1, second.Published);
        CollectionAssert.AreEqual(new[] { laterId }, sender.Published.ToArray());
        var empty = await fixture.InScopeAsync(s => s.GetRequiredService<RoomOutboxDispatcher>()
            .DispatchBatchAsync(1, [earlyId, laterId], CancellationToken.None, second.NextCursor));
        Assert.AreEqual(0, empty.Examined); Assert.IsNull(empty.NextCursor);
    }

    [TestMethod]
    public async Task RevokeCannotCommitBetweenRecipientAuthorizationAndSend()
    {
        RoomAccessHttpHubTests.RequireApproval();
        var sender = new RecordingSender(); var gate = new RecipientGate();
        await using var fixture = await FormalTestFixture.CreateAsync(configure: services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IRoomNoticeSender>(sender));
            services.Replace(ServiceDescriptor.Singleton<IRoomOutboxCheckpoint>(gate));
        });
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomManagementTestSupport.CreateAsync(fixture, owner);
        var eventId = await RoomAccessHttpHubTests.LatestEventAsync(fixture, room.RoomId);
        await AddPersonalConnectionAsync(fixture, owner, "race-transport");
        var dispatch = DispatchAsync(fixture, eventId);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var revokePid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var revoke = fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync();
            revokePid.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            await s.GetRequiredService<SessionService>().RevokeAsync(owner.UserId, owner.SessionId, CancellationToken.None);
        });
        try
        {
            var pid = await revokePid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var blocked = false;
            for (var i = 0; i < 50 && !blocked; i++)
            {
                blocked = await fixture.InScopeAsync(async s => await s.GetRequiredService<DangoDbContext>().Database
                    .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'").SingleAsync() == 1);
                if (!blocked) await Task.Delay(20);
            }
            Assert.IsTrue(blocked, "An independent PostgreSQL connection must actually wait on the recipient gate.");
            Assert.IsFalse(revoke.IsCompleted);
            Assert.HasCount(0, sender.Notices);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.AreEqual(1, (await dispatch.WaitAsync(TimeSpan.FromSeconds(5))).Published);
        await revoke.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.HasCount(1, sender.Notices);
        var updated = await RoomManagementTestSupport.CreateAsync(fixture, owner with
        { SessionId = (await fixture.InScopeAsync(s => s.GetRequiredService<SessionService>().LoginAsync(owner.Email, owner.Password, false, CancellationToken.None))).SessionId });
        await DispatchAsync(fixture, await RoomAccessHttpHubTests.LatestEventAsync(fixture, updated.RoomId));
        Assert.HasCount(1, sender.Notices, "The stale registry entry must fail fresh authorization after revoke committed.");
    }

    private static Task AddPersonalConnectionAsync(FormalTestFixture fixture, PetTestSupport.Account owner, string id) =>
        fixture.InScopeAsync(s =>
        { Assert.IsTrue(s.GetRequiredService<RoomConnectionRegistry>().Add(id, owner.UserId, owner.SessionId)); return Task.CompletedTask; });
    private static Task<RoomDispatchResult> DispatchAsync(FormalTestFixture fixture, Guid id) => fixture.InScopeAsync(s =>
        s.GetRequiredService<RoomOutboxDispatcher>().DispatchBatchAsync(1, [id], CancellationToken.None));
    private sealed class RecordingSender : IRoomNoticeSender
    {
        public List<RoomChangeNotice> Notices { get; } = [];
        public Task SendAsync(string connectionId, RoomChangeNotice notice, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Notices.Add(notice); return Task.CompletedTask; }
    }
    private sealed class ConditionalSender : IRoomNoticeSender
    {
        public Guid FailId { get; set; }
        public List<Guid> Published { get; } = [];
        public Task SendAsync(string connectionId, RoomChangeNotice notice, CancellationToken ct)
        {
            if (notice.EventId == FailId) throw new InvalidOperationException("fictional_recipient_failure");
            Published.Add(notice.EventId); return Task.CompletedTask;
        }
    }
    private sealed class FailOnce : IRoomOutboxCheckpoint
    {
        private bool failed;
        public Task ReachedAsync(string step, Guid eventId, CancellationToken ct)
        {
            if (step == "after_send_before_publish" && !failed) { failed = true; throw new InvalidOperationException("fictional_publish_failure"); }
            return Task.CompletedTask;
        }
    }
    private sealed class RecipientGate : IRoomOutboxCheckpoint
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ReachedAsync(string step, Guid eventId, CancellationToken ct)
        {
            if (step != "recipient_after_locks") return;
            Reached.TrySetResult(); await Release.Task.WaitAsync(ct);
        }
    }
}
