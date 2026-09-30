using System.Collections.Concurrent;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Rooms;
using Dango.Server.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Dango.Server.Tests.RoomRuntimeTests;

namespace Dango.Server.Tests;

[TestClass]
public sealed class RoomBoundaryTests
{
    [TestMethod]
    public async Task MovementReverseStopAndWaveUseServerTimeAndRemainFinite()
    {
        var clock = new Clock();
        await using var room = new RoomRuntime(clock);
        var a = await Join(room, "a", 0);
        await room.CommandAsync("a", Move(a, 1, "right"));
        clock.Milliseconds = 400;
        var reverse = await room.CommandAsync("a", Move(a, 2, "left"));
        var snapshot = await room.SnapshotAsync("a");
        Assert.AreEqual(.348, snapshot.Pets[0].Motion!.From.X, 1e-6);
        Assert.AreEqual(2L, reverse.EventSequence);
        clock.Milliseconds = 600;
        await room.CommandAsync("a", Move(a, 3, "stopped"));
        snapshot = await room.SnapshotAsync("a");
        Assert.AreEqual(.324, snapshot.Pets[0].AnchorPosition.X, 1e-6);
        Assert.IsNull(snapshot.Pets[0].Motion);
        var wave = await room.CommandAsync("a", Wave(a, 4));
        Assert.AreEqual("accepted", wave.Outcome);
        var conflict = await room.CommandAsync("a", Move(a, 5, "right"));
        Assert.AreEqual("action_conflict", conflict.Error?.Code);
        Assert.IsTrue(conflict.OrdinalConsumed);
        var noOp = await room.CommandAsync("a", Move(a, 6, "stopped"));
        Assert.AreEqual("accepted", noOp.Outcome);
        Assert.IsFalse(noOp.StateChanged);
        clock.Milliseconds = 1800;
        snapshot = await room.SnapshotAsync("a");
        Assert.IsNull(snapshot.Pets[0].Action);
        Assert.AreEqual(4L, snapshot.LastSequence);
        await room.CommandAsync("a", Move(a, 7, "left"));
        await room.DisconnectAsync("a", a.ConnectionGeneration);
        var b = await Join(room, "b", 1);
        clock.Milliseconds = 20_000;
        snapshot = await room.SnapshotAsync("b");
        Assert.IsNull(snapshot.Pets[0].Motion);
        Assert.AreEqual(.228, snapshot.Pets[0].AnchorPosition.X, 1e-6);
        Assert.AreEqual(b.SelfPetId, snapshot.SelfPetId);
    }

    [TestMethod]
    public async Task BoundariesNoOpAndSnapshotStoreNeverAdvanceSequenceByTimeAlone()
    {
        var clock = new Clock();
        var store = new InMemoryRoomSnapshotStore();
        await using var room = new RoomRuntime(clock, store: store);
        var a = await Join(room, "a", 0);
        for (var i = 1; i <= 4; i++)
        {
            await room.CommandAsync("a", Move(a, i, "left"));
            clock.Milliseconds += 800;
        }
        var snapshot = await room.SnapshotAsync("a");
        Assert.AreEqual(.08, snapshot.Pets[0].AnchorPosition.X, 1e-6);
        Assert.AreEqual(3L, snapshot.LastSequence);
        Assert.IsNull(snapshot.Pets[0].Motion);
        Assert.AreEqual(snapshot, store.Read());
    }

    [TestMethod]
    public async Task CacheConflictRejectedDuplicatesExpiryAndHighWaterAreIndependent()
    {
        var clock = new Clock();
        await using var room = new RoomRuntime(clock);
        var a = await Join(room, "a", 0);
        var first = Move(a, 1, "right");
        await room.CommandAsync("a", first);
        Assert.AreEqual("command_id_conflict", (await room.CommandAsync("a", first with { Payload = new("left") })).Error?.Code);
        var rejected = await room.CommandAsync("a", Wave(a, 2));
        var repeated = await room.CommandAsync("a", Wave(a, 2));
        Assert.AreEqual("action_conflict", rejected.Error?.Code);
        Assert.AreEqual("duplicate", repeated.Outcome);
        Assert.AreEqual("rejected", repeated.OriginalOutcome);
        Assert.AreEqual(rejected.Error, repeated.Error);
        clock.Milliseconds = 119_999;
        Assert.AreEqual("duplicate", (await room.CommandAsync("a", first)).Outcome);
        clock.Milliseconds = 120_000;
        var expired = await room.CommandAsync("a", first);
        Assert.AreEqual("resync_required", expired.Error?.Code);
        Assert.IsFalse(expired.OrdinalConsumed);
        Assert.AreEqual(3L, expired.NextCommandOrdinal);
        Assert.AreEqual(1L, (await room.SnapshotAsync("a")).LastSequence);
    }

    [TestMethod]
    public async Task FullResultCacheDoesNotConsumeAndOtherIdentityCanContinue()
    {
        await using var room = new RoomRuntime(options: new() { ResultCapacity = 1 });
        var a = await Join(room, "a", 0);
        var b = await Join(room, "b", 1);
        await room.CommandAsync("a", Move(a, 1, "right"));
        var full = await room.CommandAsync("a", Move(a, 2, "left"));
        Assert.AreEqual("rate_limited", full.Error?.Code);
        Assert.IsFalse(full.OrdinalConsumed);
        Assert.AreEqual(2L, full.NextCommandOrdinal);
        Assert.AreEqual("accepted", (await room.CommandAsync("b", Move(b, 1, "left"))).Outcome);
        Assert.AreEqual("duplicate", (await room.CommandAsync("a", Move(a, 1, "right"))).Outcome);
    }

    [TestMethod]
    public async Task BusinessRateRejectionIsCachedAndConsumesExactlyOnce()
    {
        var clock = new Clock();
        await using var room = new RoomRuntime(clock, new() { RateBurst = 1 });
        var a = await Join(room, "a", 0);
        await room.CommandAsync("a", Move(a, 1, "stopped"));
        var rejected = await room.CommandAsync("a", Wave(a, 2));
        Assert.AreEqual("rate_limited", rejected.Error?.Code);
        Assert.IsTrue(rejected.OrdinalConsumed);
        Assert.AreEqual("rejected", (await room.CommandAsync("a", Wave(a, 2))).OriginalOutcome);
        clock.Milliseconds = 100;
        Assert.AreEqual("accepted", (await room.CommandAsync("a", Wave(a, 3))).Outcome);
    }

    [TestMethod]
    public async Task OutboundReservationFailureCannotCommitStateOrOrdinal()
    {
        var sink = new BlockedSink();
        await using var room = new RoomRuntime(options: new() { OutboundCapacity = 1 }, sink: sink);
        var a = await Join(room, "a", 0);
        var b = await Join(room, "b", 1);
        await room.CommandAsync("a", Move(a, 1, "right"));
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var rejected = await room.CommandAsync("b", Move(b, 1, "left"));
        Assert.AreEqual("rate_limited", rejected.Error?.Code);
        Assert.IsFalse(rejected.OrdinalConsumed);
        Assert.AreEqual(1L, rejected.NextCommandOrdinal);
        var snapshot = await room.SnapshotAsync("b");
        Assert.AreEqual(1L, snapshot.LastSequence);
        Assert.IsNull(snapshot.Pets[1].Motion);
        sink.Release.TrySetResult();
    }

    [TestMethod]
    public async Task TwoIdentityConcurrentHundredRoundsHaveContiguousUniqueEvents()
    {
        var sink = new RecordingSink();
        await using var room = new RoomRuntime(options: new() { RateBurst = 1000 }, sink: sink);
        var a = await Join(room, "a", 0);
        var b = await Join(room, "b", 1);
        for (var i = 1; i <= 100; i++)
        {
            var results = await Task.WhenAll(room.CommandAsync("a", Move(a, i, i % 2 == 0 ? "left" : "right")),
                room.CommandAsync("b", Move(b, i, i % 2 == 0 ? "right" : "left")));
            Assert.IsTrue(results.All(r => r.Outcome == "accepted"));
        }
        await WaitUntil(() => sink.Events.Count == 200);
        CollectionAssert.AreEqual(Enumerable.Range(1, 200).Select(i => (long)i).ToArray(), sink.Events.Select(e => e.Sequence).ToArray());
        Assert.AreEqual(200, sink.Events.Select(e => e.EventId).Distinct().Count());
        Assert.AreEqual(200L, (await room.SnapshotAsync("a")).LastSequence);
    }

    [TestMethod]
    public async Task InvalidIdentityProtocolSpoofAndOrdinalDoNotConsume()
    {
        await using var room = new RoomRuntime();
        var a = await Join(room, "a", 0);
        var normal = Move(a, 1, "right");
        var cases = new (RoomCommand Command, string Code)[]
        {
            (normal with { ProtocolVersion = "99" }, "unsupported_protocol"),
            (normal with { RoomEpoch = "wrong" }, "room_epoch_mismatch"),
            (normal with { RoomId = "other" }, "forbidden"),
            (normal with { ConnectionGeneration = a.ConnectionGeneration + 1 }, "superseded_connection"),
            (normal with { CommandOrdinal = 2 }, "command_order_mismatch"),
            (normal with { Payload = new("up") }, "invalid_command"),
            (normal with { Extra = new() { ["petId"] = JsonSerializer.SerializeToElement("fictional-pet-2") } }, "forbidden"),
        };
        foreach (var item in cases)
        {
            var reply = await room.CommandAsync("a", item.Command);
            Assert.AreEqual(item.Code, reply.Error?.Code);
            Assert.IsFalse(reply.OrdinalConsumed);
            Assert.AreEqual(1L, reply.NextCommandOrdinal);
        }
        Assert.AreEqual("not_joined", (await room.CommandAsync("unknown", normal)).Error?.Code);
        Assert.AreEqual(0L, (await room.SnapshotAsync("a")).LastSequence);
        Assert.AreEqual("accepted", (await room.CommandAsync("a", normal)).Outcome);
    }

    [TestMethod]
    public async Task JoinBindingRetriesCredentialsAndFailedPreparationAreSafe()
    {
        await using var room = new RoomRuntime();
        var request = new JoinRoomRequest(RoomProtocol.Version, RoomProtocol.RoomId, "instance-0", room.JoinTickets[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => room.JoinAsync("failed", request, () => throw new InvalidOperationException("subscription failed")));
        var a = await room.JoinAsync("a", request);
        Assert.AreEqual(1L, a.ConnectionGeneration);
        Assert.AreEqual(a.ConnectionGeneration, (await room.JoinAsync("a", request)).ConnectionGeneration);
        await Reject("invalid_ticket", () => room.JoinAsync("b", request with { ClientInstanceId = "stolen-instance" }));
        await Reject("invalid_ticket", () => room.JoinAsync("b", request with { JoinTicket = "wrong" }));
        await Reject("forbidden", () => Join(room, "a", 1));
        var resume = new ResumeRoomRequest(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", a.ResumeToken);
        await Reject("invalid_resume_token", () => room.ResumeAsync("new", resume with { ResumeToken = "wrong" }));
        await Reject("room_reset_required", () => room.ResumeAsync("new", resume with { RoomEpoch = "old" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => room.ResumeAsync("failed-resume", resume, () => throw new InvalidOperationException("subscription failed")));
        Assert.AreEqual(a.SelfPetId, (await room.SnapshotAsync("a")).SelfPetId);
        var replies = await Task.WhenAll(room.ResumeAsync("next-1", resume), room.ResumeAsync("next-2", resume));
        Assert.AreEqual(3L, replies.Max(r => r.ConnectionGeneration));
        var winner = replies[0].ConnectionGeneration > replies[1].ConnectionGeneration ? "next-1" : "next-2";
        await room.DisconnectAsync("a", a.ConnectionGeneration);
        Assert.AreEqual(2, (await room.SnapshotAsync(winner)).Pets.Count);
    }

    [TestMethod]
    public async Task FullRoomQueueRejectsBeforeProcessingAndRecovers()
    {
        var store = new BlockingStore();
        await using var room = new RoomRuntime(options: new() { RoomQueueCapacity = 2 }, store: store);
        await Join(room, "a", 0);
        store.Armed = true;
        var blocked = room.SnapshotAsync("a");
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var queued1 = room.TimeAsync("a");
            var queued2 = room.TimeAsync("a");
            var error = await Assert.ThrowsAsync<RoomRejectedException>(() => room.TimeAsync("a"));
            Assert.AreEqual("rate_limited", error.Code);
            store.Release.Set();
            await Task.WhenAll(queued1, queued2);
            await blocked;
            Assert.AreEqual(0L, (await room.TimeAsync("a")).LastSequence);
        }
        finally { store.Release.Set(); }
    }

    [TestMethod]
    public async Task FullIdentityPendingBudgetDoesNotStarveOtherIdentity()
    {
        var store = new BlockingStore();
        await using var room = new RoomRuntime(options: new() { PerIdentityPendingCapacity = 2 }, store: store);
        var a = await Join(room, "a", 0);
        var b = await Join(room, "b", 1);
        store.Armed = true;
        var blocked = room.SnapshotAsync("a");
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var queued1 = room.CommandAsync("a", Move(a, 1, "stopped"));
            var queued2 = room.CommandAsync("a", Move(a, 2, "stopped"));
            var saturated = await room.CommandAsync("a", Move(a, 3, "right"));
            Assert.AreEqual("rate_limited", saturated.Error?.Code);
            Assert.IsFalse(saturated.OrdinalConsumed);
            var other = room.CommandAsync("b", Move(b, 1, "left"));
            store.Release.Set();
            await blocked;
            Assert.AreEqual("accepted", (await queued1).Outcome);
            Assert.AreEqual("accepted", (await queued2).Outcome);
            Assert.AreEqual("accepted", (await other).Outcome);
            Assert.AreEqual("accepted", (await room.CommandAsync("a", Move(a, 3, "right"))).Outcome);
        }
        finally { store.Release.Set(); }
    }

    private static async Task Reject(string code, Func<Task<JoinRoomResponse>> action)
    {
        var error = await Assert.ThrowsAsync<RoomRejectedException>(action);
        Assert.AreEqual(code, error.Code);
    }
    internal static RoomCommand Wave(JoinRoomResponse join, long ordinal) => Move(join, ordinal, "stopped") with { Type = RoomProtocol.Wave, Payload = new() };
    internal static async Task WaitUntil(Func<bool> test)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!test()) await Task.Delay(10, timeout.Token);
    }
    private sealed class BlockedSink : IRoomEventSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SendAsync(RoomEvent roomEvent, IReadOnlyList<RoomRecipient> recipients, CancellationToken cancellationToken)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
        public Task SupersedeAsync(RoomRecipient oldRecipient, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class RecordingSink : IRoomEventSink
    {
        public ConcurrentQueue<RoomEvent> Events { get; } = new();
        public Task SendAsync(RoomEvent roomEvent, IReadOnlyList<RoomRecipient> recipients, CancellationToken cancellationToken)
        { Events.Enqueue(roomEvent); return Task.CompletedTask; }
        public Task SupersedeAsync(RoomRecipient oldRecipient, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class BlockingStore : IRoomSnapshotStore
    {
        public volatile bool Armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public RoomSnapshot? Read() => null;
        public void Save(RoomSnapshot snapshot)
        {
            if (!Armed) return;
            Armed = false;
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("test release missing");
        }
    }
}
