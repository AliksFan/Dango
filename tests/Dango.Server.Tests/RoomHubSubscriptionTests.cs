using System.Security.Claims;
using System.Collections.Concurrent;
using Dango.Protocol;
using Dango.Server.Realtime;
using Dango.Server.Rooms;
using Dango.Server.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests;

[TestClass]
public sealed class RoomHubSubscriptionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedBindAfterGroupAddRemovesOnlyItsNewSubscription(bool resume)
    {
        var store = new BlockingStore();
        await using var room = new RoomRuntime(options: new() { RoomQueueCapacity = 1 }, store: store);
        await RoomRuntimeTests.Join(room, "b", 1);
        var original = resume ? await RoomRuntimeTests.Join(room, "a", 0) : null;
        var groups = new RecordingGroups();
        var hub = MakeHub(room, "new-a", groups);
        Task<RoomSnapshot>? blocked = null;
        Task<TimeSample>? queued = null;
        groups.AfterAdd = async () =>
        {
            store.Armed = true;
            blocked = room.SnapshotAsync("b");
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            queued = room.TimeAsync("b"); // Occupy the sole queue slot before BindAsync tries to enter.
        };
        try
        {
            var error = await Assert.ThrowsAsync<HubException>(async () =>
            {
                if (resume)
                    await hub.ResumeRoom(new(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", original!.ResumeToken));
                else await hub.JoinRoom(JoinRequest(room));
            });
            Assert.AreEqual("rate_limited", error.Message);
            Assert.AreEqual(1, groups.AddCount);
            Assert.AreEqual(1, groups.RemoveCount, "A newly added group subscription must be rolled back when the room bind is rejected.");
            Assert.IsFalse(groups.Members.Contains("new-a"));
        }
        finally
        {
            store.Release.Set();
            if (blocked is not null) await blocked;
            if (queued is not null) await queued;
        }
        await Assert.ThrowsAsync<RoomRejectedException>(() => room.TimeAsync("new-a"));
        if (original is not null) Assert.AreEqual(original.SelfPetId, (await room.SnapshotAsync("a")).SelfPetId);
        groups.AfterAdd = null;
        var accepted = await hub.JoinRoom(JoinRequest(room));
        Assert.AreEqual(original is null ? 1L : 2L, accepted.ConnectionGeneration);
        Assert.AreEqual(1L, accepted.NextCommandOrdinal);
        Assert.IsTrue(groups.Members.Contains("new-a"));
    }

    [TestMethod]
    public async Task PartialAddFailureIsRolledBackWithoutBindingIdentity()
    {
        await using var room = new RoomRuntime();
        var groups = new RecordingGroups { AfterAdd = () => throw new IOException("test subscription failure") };
        var hub = MakeHub(room, "new-a", groups);
        await Assert.ThrowsAsync<IOException>(() => hub.JoinRoom(JoinRequest(room)));
        Assert.AreEqual(1, groups.RemoveCount);
        Assert.IsFalse(groups.Members.Contains("new-a"));
        await Assert.ThrowsAsync<RoomRejectedException>(() => room.TimeAsync("new-a"));
    }

    [TestMethod]
    public async Task SameConnectionRetryKeepsExistingMembershipAndGeneration()
    {
        var sink = new RecordingSink();
        await using var room = new RoomRuntime(sink: sink);
        var groups = new RecordingGroups();
        var hub = MakeHub(room, "a", groups);
        var first = await hub.JoinRoom(JoinRequest(room));
        groups.AfterAdd = () => throw new IOException("An existing membership must not be added again.");
        var second = await hub.ResumeRoom(new(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", first.ResumeToken));
        Assert.AreEqual(first.ConnectionGeneration, second.ConnectionGeneration);
        Assert.AreEqual(1, groups.AddCount);
        var rejected = await Assert.ThrowsAsync<HubException>(() => hub.JoinRoom(JoinRequest(room) with { JoinTicket = "invalid" }));
        Assert.AreEqual("invalid_ticket", rejected.Message);
        Assert.AreEqual(0, groups.RemoveCount);
        Assert.IsTrue(groups.Members.Contains("a"));
        Assert.AreEqual(first.SelfPetId, (await room.SnapshotAsync("a")).SelfPetId);
        Assert.AreEqual("accepted", (await hub.SendCommand(RoomRuntimeTests.Move(second, 1, "right"))).Outcome);
        await RoomBoundaryTests.WaitUntil(() => !sink.Deliveries.IsEmpty);
        Assert.IsTrue(sink.Deliveries.Single().Any(r => r.ConnectionId == "a" && r.Generation == first.ConnectionGeneration),
            "Failed retries must retain the current connection's event-recipient qualification.");
    }

    [TestMethod]
    public async Task FailedCleanupAbortsOnlyTheNewTransport()
    {
        await using var room = new RoomRuntime();
        var old = await RoomRuntimeTests.Join(room, "old-a", 0);
        var groups = new RecordingGroups { AfterAdd = () => throw new IOException("test add failure"), FailRemove = true };
        var hub = MakeHub(room, "new-a", groups);
        await Assert.ThrowsAsync<IOException>(() => hub.ResumeRoom(new(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", old.ResumeToken)));
        Assert.IsTrue(hub.Context.ConnectionAborted.IsCancellationRequested);
        Assert.AreEqual(1, groups.RemoveCount);
        Assert.AreEqual(old.SelfPetId, (await room.SnapshotAsync("old-a")).SelfPetId);
    }

    [TestMethod]
    public async Task SupersededContextHistoryDoesNotSuppressNewSubscriptionRollback()
    {
        await using var room = new RoomRuntime();
        var groups = new RecordingGroups();
        var hub = MakeHub(room, "old-a", groups);
        var old = await hub.JoinRoom(JoinRequest(room));
        var current = await room.ResumeAsync("current-a", new(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", old.ResumeToken));
        groups.Members.Remove("old-a"); // SignalRRoomEventSink performs this removal on the real transport.
        groups.AfterAdd = () => throw new IOException("test add failure");
        await Assert.ThrowsAsync<IOException>(() => hub.JoinRoom(JoinRequest(room)));
        Assert.AreEqual(2, groups.AddCount);
        Assert.AreEqual(1, groups.RemoveCount);
        Assert.IsFalse(groups.Members.Contains("old-a"));
        Assert.AreEqual(current.SelfPetId, (await room.SnapshotAsync("current-a")).SelfPetId);
    }

    private static JoinRoomRequest JoinRequest(RoomRuntime room) => new(RoomProtocol.Version, RoomProtocol.RoomId, "instance-0", room.JoinTickets[0]);
    private static RoomHub MakeHub(RoomRuntime room, string connectionId, RecordingGroups groups) =>
        new(room, new ConnectionAdmission()) { Context = new Caller(connectionId), Groups = groups };

    private sealed class Caller(string connectionId) : HubCallerContext
    {
        private readonly CancellationTokenSource disconnected = new();
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => disconnected.Token;
        public override void Abort() => disconnected.Cancel();
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);
        public Func<Task>? AfterAdd { get; set; }
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool FailRemove { get; set; }
        public async Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddCount++;
            Members.Add(connectionId);
            if (AfterAdd is not null) await AfterAdd();
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            if (FailRemove) throw new IOException("test remove failure");
            Members.Remove(connectionId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSink : IRoomEventSink
    {
        public ConcurrentQueue<IReadOnlyList<RoomRecipient>> Deliveries { get; } = new();
        public Task SendAsync(RoomEvent roomEvent, IReadOnlyList<RoomRecipient> recipients, CancellationToken cancellationToken)
        { Deliveries.Enqueue(recipients); return Task.CompletedTask; }
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
