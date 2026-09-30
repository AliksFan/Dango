using System.Collections.Concurrent;
using Dango.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class ConnectionTests
{
    private static LocalClientBootstrap Bootstrap => new("http://127.0.0.1:12345", RoomProtocol.RoomId, "epoch", "test-client", "fictional-ticket");

    [TestMethod]
    public async Task ConnectionBuffersEarlyEventAndDoesNotModifyAnchorForPendingCommand()
    {
        var factory = new FakeFactory { EventBeforeJoin = true };
        await using var connection = new RoomConnection(Bootstrap, factory.Time, factory);
        await connection.StartAsync();
        Assert.AreEqual(ReplicaState.Live, connection.Replica.Frame.State);
        Assert.AreEqual(11L, connection.Replica.Frame.LastSequence);
        Assert.IsTrue(connection.SetMoveIntent("right"));
        Assert.AreEqual(.4, connection.Replica.Frame.Snapshot!.Pets[0].AnchorPosition.X);
        await Until(() => factory.Commands.Count == 1 && connection.Replica.Frame.LastSequence == 12);
        Assert.AreEqual(2L, connection.SafeDiagnostics.NextCommandOrdinal);
        Assert.AreEqual("accepted", connection.SafeDiagnostics.LastOutcome);
    }

    [TestMethod]
    public async Task LostReceiptRetriesIdenticalIdAndOrdinalWithoutSecondEvent()
    {
        var factory = new FakeFactory { DropFirstReply = true };
        await using var connection = new RoomConnection(Bootstrap, factory.Time, factory);
        await connection.StartAsync();
        connection.StartWave();
        await Until(() => factory.Commands.Count == 1);
        factory.Time.Milliseconds = 600;
        await Until(() => connection.SafeDiagnostics.NextCommandOrdinal == 2);
        Assert.AreEqual(2, factory.Commands.Count);
        var commands = factory.Commands.ToArray();
        Assert.AreEqual(commands[0].CommandId, commands[1].CommandId);
        Assert.AreEqual(commands[0].CommandOrdinal, commands[1].CommandOrdinal);
        Assert.AreEqual(11L, connection.Replica.Frame.LastSequence);
        Assert.AreEqual(1, factory.PublicEventCount);
    }

    [TestMethod]
    public async Task PassiveTailLossUsesTimeWatermarkAndReceiptLossUsesDeadline()
    {
        var factory = new FakeFactory { DropEvents = true };
        await using var connection = new RoomConnection(Bootstrap, factory.Time, factory);
        await connection.StartAsync();
        factory.SharedSnapshot = ReplicaTests.Snapshot(11, .6);
        factory.Time.Milliseconds = 2100;
        await Until(() => connection.Replica.Frame.LastSequence == 11);
        Assert.IsGreaterThanOrEqualTo(1, connection.SafeDiagnostics.WatermarkRepairCount);
        connection.SetMoveIntent("left");
        await Until(() => connection.SafeDiagnostics.NextCommandOrdinal == 2);
        Assert.AreEqual(11L, connection.Replica.Frame.LastSequence);
        factory.Time.Milliseconds = 3200; // Receipt deadline, before the next scheduled watermark.
        await Until(() => connection.Replica.Frame.LastSequence == 12);
        Assert.IsGreaterThanOrEqualTo(3, connection.SafeDiagnostics.SnapshotCount);
    }

    [TestMethod]
    public async Task ReconnectDiscardsOfflineInputAndOldCallbacksCannotMutateNewFrame()
    {
        var factory = new FakeFactory();
        await using var connection = new RoomConnection(Bootstrap, factory.Time, factory);
        await connection.StartAsync();
        var old = factory.Transports[0];
        await connection.DisconnectAsync();
        Assert.IsFalse(connection.SetMoveIntent("right"));
        factory.SharedSnapshot = ReplicaTests.Snapshot(12, .6);
        await connection.ReconnectAsync();
        Assert.AreEqual(ReplicaState.Live, connection.Replica.Frame.State);
        old.Emit(ReplicaTests.Event(13, .9));
        old.EmitClosed();
        old.EmitSuperseded(1);
        Assert.AreEqual(12L, connection.Replica.Frame.LastSequence);
        Assert.AreEqual(ReplicaState.Live, connection.Replica.Frame.State);
        Assert.AreEqual(0, factory.Commands.Count);
        Assert.AreEqual(1, factory.ResumeCount);
    }

    [TestMethod]
    public async Task EpochResetStopsRecoveryAndCannotReuseOldTicketThroughReconnect()
    {
        var factory = new FakeFactory();
        await using var connection = new RoomConnection(Bootstrap, factory.Time, factory);
        await connection.StartAsync();
        factory.Reset = true;
        factory.Time.Milliseconds = 2100;
        await Until(() => connection.Replica.Frame.State == ReplicaState.ResetRequired);
        await connection.DisconnectAsync();
        await connection.ReconnectAsync();
        Assert.AreEqual(ReplicaState.ResetRequired, connection.Replica.Frame.State);
        Assert.AreEqual(1, factory.Transports.Count);
        Assert.IsNull(connection.Replica.Frame.Snapshot);
        var diagnostic = System.Text.Json.JsonSerializer.Serialize(connection.SafeDiagnostics);
        Assert.IsFalse(diagnostic.Contains("fictional-ticket", StringComparison.Ordinal));
        Assert.IsFalse(diagnostic.Contains("fictional-resume-token", StringComparison.Ordinal));
        Assert.AreEqual("room_reset_required", connection.SafeDiagnostics.ErrorCode);
    }

    [TestMethod]
    public void NonLoopbackPlaintextAndCredentialedEndpointAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new RoomConnection(Bootstrap with { Endpoint = "http://192.0.2.1/room" }));
        Assert.Throws<ArgumentException>(() => new RoomConnection(Bootstrap with { Endpoint = "https://user:secret@example.test/room" }));
        Assert.Throws<ArgumentException>(() => new RoomConnection(Bootstrap with { Endpoint = "http://127.0.0.1/room?ticket=unsafe" }));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeFactory : IRoomTransportFactory
    {
        public ManualTime Time { get; } = new();
        public RoomSnapshot SharedSnapshot { get; set; } = ReplicaTests.Snapshot();
        public List<FakeTransport> Transports { get; } = [];
        public ConcurrentQueue<RoomCommand> Commands { get; } = [];
        public Dictionary<string, CommandResult> Results { get; } = [];
        public bool EventBeforeJoin { get; init; }
        public bool DropFirstReply { get; init; }
        public bool DropEvents { get; init; }
        public bool Reset { get; set; }
        public int ResumeCount;
        public int PublicEventCount;
        public IRoomTransport Create(Uri endpoint)
        {
            Assert.AreEqual("/room", endpoint.AbsolutePath);
            var transport = new FakeTransport(this, Transports.Count + 1);
            Transports.Add(transport);
            return transport;
        }
    }

    private sealed class FakeTransport(FakeFactory owner, long generation) : IRoomTransport
    {
        public event Action<RoomEvent>? RoomEventReceived;
        public event Action<SessionSuperseded>? SessionSupersededReceived;
        public event Action? Closed;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) { Closed?.Invoke(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Emit(RoomEvent value) => RoomEventReceived?.Invoke(value);
        public void EmitClosed() => Closed?.Invoke();
        public void EmitSuperseded(long value) => SessionSupersededReceived?.Invoke(new(value));
        public Task<T> InvokeAsync<T>(string method, object? argument, CancellationToken cancellationToken)
        {
            if (owner.Reset) throw new HubException("room_reset_required");
            object result;
            if (method is "JoinRoom" or "ResumeRoom")
            {
                if (method == "ResumeRoom") owner.ResumeCount++;
                var snapshot = owner.SharedSnapshot with { ServerTimeMs = (long)owner.Time.Milliseconds + 1000 };
                if (owner.EventBeforeJoin && snapshot.LastSequence == 10)
                {
                    Emit(ReplicaTests.Event(11));
                    owner.SharedSnapshot = ReplicaTests.Snapshot(11, .4);
                }
                result = new JoinRoomResponse(RoomProtocol.Version, generation, "participant-a", "pet-a",
                    "fictional-resume-token", owner.Results.Count + 1, snapshot);
            }
            else if (method == "GetSnapshot") result = owner.SharedSnapshot with { ServerTimeMs = (long)owner.Time.Milliseconds + 1000 };
            else if (method == "GetRoomTime") result = new TimeSample(RoomProtocol.Version, RoomProtocol.RoomId,
                "epoch", (long)owner.Time.Milliseconds + 1000, owner.SharedSnapshot.LastSequence);
            else if (method == "SendCommand")
            {
                var command = (RoomCommand)argument!;
                owner.Commands.Enqueue(command);
                if (owner.Results.TryGetValue(command.CommandId, out var cached))
                    result = cached with { Outcome = "duplicate", OriginalOutcome = cached.Outcome };
                else
                {
                    var sequence = owner.SharedSnapshot.LastSequence + 1;
                    var value = ReplicaTests.Event(sequence, .5);
                    owner.SharedSnapshot = ReplicaTests.Snapshot(sequence, .5);
                    owner.PublicEventCount++;
                    if (!owner.DropEvents) Emit(value);
                    var response = new CommandResult(RoomProtocol.Version, RoomProtocol.RoomId, "epoch", command.CommandId,
                        "accepted", true, sequence, true, command.CommandOrdinal + 1, null, value.EventId, sequence,
                        (long)owner.Time.Milliseconds + 1000, null);
                    owner.Results.Add(command.CommandId, response);
                    if (owner.DropFirstReply) throw new OperationCanceledException();
                    result = response;
                }
            }
            else throw new InvalidOperationException("unexpected_method");
            return Task.FromResult((T)result);
        }
    }
}
