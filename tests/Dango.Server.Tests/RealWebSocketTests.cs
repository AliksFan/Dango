using System.Collections.Concurrent;
using System.Diagnostics;
using Dango.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Dango.Server.Tests.RoomBoundaryTests;
using static Dango.Server.Tests.RoomRuntimeTests;

namespace Dango.Server.Tests;

[TestClass]
public sealed class RealWebSocketTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task TwoActualWebSocketsConvergeMoveStopWaveAndResumeSnapshot()
    {
        await using var server = await ServerProcess.StartAsync(TestContext);
        await using var a = server.CreateConnection();
        await using var b = server.CreateConnection();
        var eventsA = new ConcurrentQueue<RoomEvent>();
        var eventsB = new ConcurrentQueue<RoomEvent>();
        a.On<RoomEvent>("RoomEvent", eventsA.Enqueue);
        b.On<RoomEvent>("RoomEvent", eventsB.Enqueue);
        await Task.WhenAll(a.StartAsync(), b.StartAsync());
        var joinA = await server.JoinAsync(a, 0);
        var joinB = await server.JoinAsync(b, 1);
        Assert.AreEqual(joinA.Snapshot.RoomEpoch, joinB.Snapshot.RoomEpoch);
        var clock = Stopwatch.StartNew();
        var time = await b.InvokeAsync<TimeSample>("GetRoomTime");
        TestContext.WriteLine($"Observed loopback time RPC RTT={clock.Elapsed.TotalMilliseconds:F3}ms; sequence={time.LastSequence}.");
        var move = Move(joinA, 1, "right");
        var first = await a.InvokeAsync<CommandResult>("SendCommand", move);
        Assert.AreEqual("accepted", first.Outcome);
        await WaitUntil(() => eventsA.Count == 1 && eventsB.Count == 1);
        Assert.AreEqual(eventsA.Single().EventId, eventsB.Single().EventId);
        Assert.AreEqual(joinA.SelfPetId, eventsB.Single().Payload.PetId);
        var duplicate = await a.InvokeAsync<CommandResult>("SendCommand", move);
        Assert.AreEqual("duplicate", duplicate.Outcome);
        var stopped = await a.InvokeAsync<CommandResult>("SendCommand", Move(joinA, 2, "stopped"));
        Assert.AreEqual(2L, stopped.EventSequence);
        await WaitUntil(() => eventsA.Count == 2 && eventsB.Count == 2);
        await b.StopAsync();
        var wave = await a.InvokeAsync<CommandResult>("SendCommand", Wave(joinA, 3));
        Assert.AreEqual(3L, wave.EventSequence);
        await WaitUntil(() => eventsA.Count == 3);
        await b.StartAsync();
        clock.Restart();
        var resumed = await b.InvokeAsync<JoinRoomResponse>("ResumeRoom", Resume(joinB));
        TestContext.WriteLine($"Observed resume snapshot RPC elapsed={clock.Elapsed.TotalMilliseconds:F3}ms; sequence={resumed.Snapshot.LastSequence}.");
        Assert.AreEqual(joinB.ConnectionGeneration + 1, resumed.ConnectionGeneration);
        Assert.AreEqual(3L, resumed.Snapshot.LastSequence);
        Assert.AreEqual(eventsA.Last().Payload.Action!.ActionId, resumed.Snapshot.Pets[0].Action!.ActionId);
        Assert.AreEqual(eventsA.Last().Payload.AnchorPosition, resumed.Snapshot.Pets[0].AnchorPosition);
        Assert.AreEqual(.7, resumed.Snapshot.Pets[1].AnchorPosition.X, 1e-6);
        await Task.Delay(1250);
        var final = await b.InvokeAsync<RoomSnapshot>("GetSnapshot");
        Assert.IsNull(final.Pets[0].Action);
        Assert.AreEqual(3L, final.LastSequence);
        Assert.AreEqual(3L, (await b.InvokeAsync<TimeSample>("GetRoomTime")).LastSequence);
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, eventsA.Select(e => e.Sequence).ToArray());
    }

    [TestMethod]
    public async Task RealReplacementRejectsOldGenerationAndRestartRequiresNewEpochCredentials()
    {
        JoinRoomResponse old;
        await using (var server = await ServerProcess.StartAsync(TestContext))
        {
            await using var a = server.CreateConnection();
            await using var replacement = server.CreateConnection();
            var superseded = new TaskCompletionSource<SessionSuperseded>(TaskCreationOptions.RunContinuationsAsynchronously);
            a.On<SessionSuperseded>("SessionSuperseded", value => superseded.TrySetResult(value));
            await Task.WhenAll(a.StartAsync(), replacement.StartAsync());
            old = await server.JoinAsync(a, 0);
            var original = Move(old, 1, "right");
            await a.InvokeAsync<CommandResult>("SendCommand", original);
            var current = await replacement.InvokeAsync<JoinRoomResponse>("ResumeRoom", Resume(old));
            Assert.AreEqual(old.ConnectionGeneration, (await superseded.Task.WaitAsync(TimeSpan.FromSeconds(3))).ConnectionGeneration);
            var stale = await a.InvokeAsync<CommandResult>("SendCommand", original);
            Assert.AreEqual("superseded_connection", stale.Error?.Code);
            Assert.IsFalse(stale.OrdinalConsumed);
            Assert.AreEqual(2L, current.NextCommandOrdinal);
            var duplicate = await replacement.InvokeAsync<CommandResult>("SendCommand", original with { ConnectionGeneration = current.ConnectionGeneration });
            Assert.AreEqual("duplicate", duplicate.Outcome);
            await a.StopAsync();
            Assert.AreEqual(2, (await replacement.InvokeAsync<RoomSnapshot>("GetSnapshot")).Pets.Count);
        }
        await using (var restarted = await ServerProcess.StartAsync(TestContext))
        {
            Assert.AreNotEqual(old.Snapshot.RoomEpoch, restarted.Bootstrap.RoomEpoch);
            await using var fresh = restarted.CreateConnection();
            await fresh.StartAsync();
            var reset = await Assert.ThrowsAsync<HubException>(() => fresh.InvokeAsync<JoinRoomResponse>("ResumeRoom", Resume(old)));
            StringAssert.Contains(reset.Message, "room_reset_required");
            var invalid = await Assert.ThrowsAsync<HubException>(() => fresh.InvokeAsync<JoinRoomResponse>("ResumeRoom", Resume(old) with { RoomEpoch = restarted.Bootstrap.RoomEpoch }));
            StringAssert.Contains(invalid.Message, "invalid_resume_token");
            var joined = await restarted.JoinAsync(fresh, 0);
            Assert.AreEqual(0L, joined.Snapshot.LastSequence);
            Assert.AreEqual(1L, joined.NextCommandOrdinal);
            Assert.AreEqual(2, joined.Snapshot.Pets.Count);
        }
    }

    [TestMethod]
    public async Task RealJoinConcurrentWithEventsReturnsSnapshotBarrierAndContiguousSuffix()
    {
        await using var server = await ServerProcess.StartAsync(TestContext);
        await using var a = server.CreateConnection();
        await using var b = server.CreateConnection();
        var received = new ConcurrentQueue<RoomEvent>();
        b.On<RoomEvent>("RoomEvent", received.Enqueue);
        await Task.WhenAll(a.StartAsync(), b.StartAsync());
        var joined = await server.JoinAsync(a, 0);
        var joinTask = server.JoinAsync(b, 1);
        for (var i = 1; i <= 10; i++)
            Assert.AreEqual("accepted", (await a.InvokeAsync<CommandResult>("SendCommand", Move(joined, i, i % 2 == 0 ? "left" : "right"))).Outcome);
        var snapshot = (await joinTask).Snapshot;
        await WaitUntil(() => received.Count(e => e.Sequence > snapshot.LastSequence) == 10 - snapshot.LastSequence);
        CollectionAssert.AreEqual(Enumerable.Range((int)snapshot.LastSequence + 1, 10 - (int)snapshot.LastSequence).Select(i => (long)i).ToArray(),
            received.Where(e => e.Sequence > snapshot.LastSequence).Select(e => e.Sequence).ToArray());
        Assert.AreEqual(10L, (await b.InvokeAsync<RoomSnapshot>("GetSnapshot")).LastSequence);
        TestContext.WriteLine($"Join barrier sequence={snapshot.LastSequence}; contiguous suffix count={10 - snapshot.LastSequence}.");
    }

    private static ResumeRoomRequest Resume(JoinRoomResponse joined) => new(RoomProtocol.Version, RoomProtocol.RoomId,
        joined.Snapshot.RoomEpoch, joined.SelfParticipantId.EndsWith("-1", StringComparison.Ordinal) ? "instance-0" : "instance-1", joined.ResumeToken);
}
