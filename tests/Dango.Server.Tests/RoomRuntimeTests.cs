using Dango.Protocol;
using Dango.Server.Rooms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests;

[TestClass]
public sealed class RoomRuntimeTests
{
    [TestMethod]
    public async Task DuplicateIsResolvedBeforeOrdinalAndProducesOneEvent()
    {
        await using var room = new RoomRuntime();
        var a = await Join(room, "a", 0);
        var command = Move(a, 1, "right");
        var first = await room.CommandAsync("a", command);
        var stop = await room.CommandAsync("a", Move(a, 2, "stopped"));
        var duplicate = await room.CommandAsync("a", command);
        Assert.AreEqual("accepted", first.Outcome);
        Assert.AreEqual("accepted", stop.Outcome);
        Assert.AreEqual("duplicate", duplicate.Outcome);
        Assert.AreEqual(first.EventId, duplicate.EventId);
        Assert.AreEqual(2L, (await room.SnapshotAsync("a")).LastSequence);
    }

    [TestMethod]
    public async Task SupersededGenerationCannotReadCacheOrClearNewSession()
    {
        await using var room = new RoomRuntime();
        var a = await Join(room, "a", 0);
        var command = Move(a, 1, "right");
        Assert.AreEqual("accepted", (await room.CommandAsync("a", command)).Outcome);
        var replacement = await room.ResumeAsync("a2", new(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", a.ResumeToken));
        var stale = await room.CommandAsync("a", command);
        Assert.AreEqual("superseded_connection", stale.Error?.Code);
        Assert.IsFalse(stale.OrdinalConsumed);
        await room.DisconnectAsync("a", a.ConnectionGeneration);
        Assert.AreEqual(2L, replacement.NextCommandOrdinal);
        Assert.AreEqual("accepted", (await room.CommandAsync("a2", Move(replacement, 2, "stopped"))).Outcome);
    }

    [TestMethod]
    public async Task CurrentTransportWithStaleGenerationCannotReadCachedResult()
    {
        await using var room = new RoomRuntime();
        var a = await Join(room, "a", 0);
        var command = Move(a, 1, "right");
        await room.CommandAsync("a", command);
        var current = await room.ResumeAsync("new-a", new(RoomProtocol.Version, RoomProtocol.RoomId, room.Epoch, "instance-0", a.ResumeToken));
        var stale = await room.CommandAsync("new-a", command);
        Assert.AreEqual("superseded_connection", stale.Error?.Code);
        Assert.IsFalse(stale.OrdinalConsumed);
        Assert.AreEqual("duplicate", (await room.CommandAsync("new-a", command with { ConnectionGeneration = current.ConnectionGeneration })).Outcome);
    }

    [TestMethod]
    public async Task DuplicateCannotGenerateSecondEvent()
    {
        await using var room = new RoomRuntime();
        var a = await Join(room, "a", 0);
        var command = Move(a, 1, "right");
        var first = await room.CommandAsync("a", command);
        Assert.IsTrue(first.StateChanged);
        await room.CommandAsync("a", command);
        Assert.AreEqual(1L, (await room.SnapshotAsync("a")).LastSequence, "A duplicate must not produce a second event.");
    }

    internal static Task<JoinRoomResponse> Join(RoomRuntime room, string connection, int index) => room.JoinAsync(connection,
        new(RoomProtocol.Version, RoomProtocol.RoomId, $"instance-{index}", room.JoinTickets[index]));
    internal static RoomCommand Move(JoinRoomResponse join, long ordinal, string direction, long sequence = 0) =>
        new(RoomProtocol.Version, RoomProtocol.RoomId, join.Snapshot.RoomEpoch, join.ConnectionGeneration,
            $"command-{ordinal}", ordinal, sequence, RoomProtocol.Move, new(direction));
    internal sealed class Clock : IRoomClock { public long Milliseconds { get; set; } }
}
