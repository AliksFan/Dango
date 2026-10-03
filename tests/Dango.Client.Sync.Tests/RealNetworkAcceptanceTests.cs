using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using Dango.Protocol;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class RealNetworkAcceptanceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(0)]
    [DataRow(100)]
    [DataRow(300)]
    public async Task RealWebSocketsRepairSilentTailAndLostReceiptAndResumeWithoutOldInput(int injectedRttMs)
    {
        await using var server = await ServerProcess.StartAsync();
        var fa = new FaultFactory(injectedRttMs);
        var fb = new FaultFactory(injectedRttMs);
        await using var a = new RoomConnection(server.Client("A"), transportFactory: fa);
        await using var b = new RoomConnection(server.Client("B"), transportFactory: fb);
        await Task.WhenAll(a.StartAsync(), b.StartAsync());
        await UntilAsync(() => a.Replica.Frame.State == ReplicaState.Live && b.Replica.Frame.State == ReplicaState.Live);
        Assert.AreEqual(a.Replica.Frame.Snapshot!.RoomEpoch, b.Replica.Frame.Snapshot!.RoomEpoch);
        fa.DropNextEvent = 1; fb.DropNextEvent = 1;
        var repair = Stopwatch.StartNew();
        Assert.IsTrue(a.SetMoveIntent("right"));
        await UntilAsync(() => a.Replica.Frame.LastSequence == 1 && b.Replica.Frame.LastSequence == 1, 8000);
        Assert.IsTrue(a.SafeDiagnostics.SnapshotCount >= 2, "Sender repaired missing event using its receipt.");
        Assert.IsTrue(b.SafeDiagnostics.WatermarkRepairCount >= 1, "Passive client used time watermark despite no subsequent event.");
        var repairMs = repair.Elapsed.TotalMilliseconds;
        await Task.Delay(900);
        var at = Math.Max(a.EstimatedServerTimeMs, b.EstimatedServerTimeMs);
        AssertSame(a, b, at);
        Assert.AreEqual(.396, RoomSampling.At(a.Replica.Frame.Snapshot!.Pets.Single(p => p.PetId == a.Replica.Frame.Snapshot.SelfPetId), at).AnchorPosition.X, 1e-6);

        fa.DropNextReceipt = 1;
        Assert.IsTrue(a.StartWave());
        await UntilAsync(() => a.Replica.Frame.LastSequence == 2 && b.Replica.Frame.LastSequence == 2);
        await UntilAsync(() => a.SafeDiagnostics.NextCommandOrdinal == 3 && a.Replica.Frame.State == ReplicaState.Live, 15000);
        await Task.Delay(1600);
        Assert.AreEqual(2L, a.Replica.Frame.LastSequence, "Lost receipt must not start a second action.");
        AssertSame(a, b, Math.Max(a.EstimatedServerTimeMs, b.EstimatedServerTimeMs));
        await b.DisconnectAsync();
        Assert.IsFalse(b.SetMoveIntent("left"), "Offline input must not queue for replay.");
        Assert.IsTrue(a.SetMoveIntent("left"));
        await UntilAsync(() => a.Replica.Frame.LastSequence == 3);
        await Task.Delay(1000);
        var reconnect = Stopwatch.StartNew();
        await b.ReconnectAsync();
        await UntilAsync(() => b.Replica.Frame.State == ReplicaState.Live && b.Replica.Frame.LastSequence == 3);
        var reconnectMs = reconnect.Elapsed.TotalMilliseconds;
        AssertSame(a, b, Math.Max(a.EstimatedServerTimeMs, b.EstimatedServerTimeMs));
        await Task.Delay(1000);
        Assert.AreEqual(3L, b.Replica.Frame.LastSequence, "Reconnect may not resurrect offline held movement.");
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            serverPid = server.Pid, injectedRttMs, jitterMs = injectedRttMs == 0 ? 0 : 10,
            measuredRepairMs = repairMs, measuredReconnectMs = reconnectMs,
            a = a.SafeDiagnostics, b = b.SafeDiagnostics,
        }, RoomProtocol.Json));
    }

    private static void AssertSame(RoomConnection a, RoomConnection b, long time)
    {
        Assert.AreEqual(a.Replica.Frame.LastSequence, b.Replica.Frame.LastSequence);
        var pa = a.Replica.Frame.Snapshot!.Pets.OrderBy(p => p.PetId).Select(p => RoomSampling.At(p, time)).ToArray();
        var pb = b.Replica.Frame.Snapshot!.Pets.OrderBy(p => p.PetId).Select(p => RoomSampling.At(p, time)).ToArray();
        Assert.AreEqual(JsonSerializer.Serialize(pa, RoomProtocol.Json), JsonSerializer.Serialize(pb, RoomProtocol.Json));
    }

    private static async Task UntilAsync(Func<bool> predicate, int timeoutMs = 10000)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.ElapsedMilliseconds > timeoutMs) Assert.Fail("Real network condition timed out.");
            await Task.Delay(20);
        }
    }

    private sealed class FaultFactory(int rttMs) : IRoomTransportFactory
    {
        public int DropNextEvent;
        public int DropNextReceipt;
        public IRoomTransport Create(Uri endpoint) => new FaultTransport(new SignalRRoomTransportFactory().Create(endpoint), this, rttMs);
    }

    private sealed class FaultTransport : IRoomTransport
    {
        private readonly IRoomTransport inner;
        private readonly FaultFactory owner;
        private readonly int rttMs;
        private int invocations;
        private readonly Channel<RoomEvent> events = Channel.CreateBounded<RoomEvent>(64);
        private readonly CancellationTokenSource lifetime = new();
        private readonly Task delivery;
        public event Action<RoomEvent>? RoomEventReceived;
        public event Action<SessionSuperseded>? SessionSupersededReceived;
        public event Action? Closed;
        public FaultTransport(IRoomTransport inner, FaultFactory owner, int rttMs)
        {
            this.inner = inner; this.owner = owner; this.rttMs = rttMs;
            delivery = DeliverAsync();
            inner.RoomEventReceived += value =>
            {
                if (Interlocked.Exchange(ref owner.DropNextEvent, 0) == 0 && !events.Writer.TryWrite(value))
                    throw new InvalidOperationException("test_event_buffer_full");
            };
            inner.SessionSupersededReceived += value => SessionSupersededReceived?.Invoke(value);
            inner.Closed += () => Closed?.Invoke();
        }
        public Task StartAsync(CancellationToken cancellationToken) => inner.StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => inner.StopAsync(cancellationToken);
        public async Task<T> InvokeAsync<T>(string method, object? argument, CancellationToken cancellationToken)
        {
            var jitter = rttMs == 0 ? 0 : Interlocked.Increment(ref invocations) % 3 * 5;
            await Task.Delay(rttMs / 2 + jitter, cancellationToken);
            var result = await inner.InvokeAsync<T>(method, argument, cancellationToken);
            await Task.Delay(rttMs / 2 + jitter, cancellationToken);
            if (method == "SendCommand" && Interlocked.Exchange(ref owner.DropNextReceipt, 0) != 0)
                throw new TimeoutException("test_receipt_dropped_after_commit");
            return result;
        }
        private async Task DeliverAsync()
        {
            try
            {
                await foreach (var value in events.Reader.ReadAllAsync(lifetime.Token))
                {
                    await Task.Delay(rttMs / 2 + (rttMs == 0 ? 0 : (int)(value.Sequence % 3) * 5), lifetime.Token);
                    RoomEventReceived?.Invoke(value);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync()
        {
            await lifetime.CancelAsync();
            await delivery;
            await inner.DisposeAsync();
            lifetime.Dispose();
        }
    }

    private sealed class ServerProcess : IAsyncDisposable
    {
        private readonly Process process;
        private readonly StreamWriter control;
        private readonly LocalBootstrap bootstrap;
        public int Pid => process.Id;
        private ServerProcess(Process process, StreamWriter control, LocalBootstrap bootstrap)
        { this.process = process; this.control = control; this.bootstrap = bootstrap; }
        public LocalClientBootstrap Client(string role) => new(bootstrap.Endpoint, bootstrap.RoomId, bootstrap.RoomEpoch,
            "real-network-" + role, bootstrap.JoinTickets[role == "A" ? 0 : 1]);
        public static async Task<ServerProcess> StartAsync()
        {
            var path = Environment.GetEnvironmentVariable("DANGO_SYNC_SERVER_PATH");
            Assert.IsTrue(File.Exists(path), "DANGO_SYNC_SERVER_PATH must identify this run's final-build server executable.");
            using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var controlPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var info = new ProcessStartInfo(path!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { "--bootstrap-pipe", pipe.GetClientHandleAsString(), "--control-pipe", controlPipe.GetClientHandleAsString() }) info.ArgumentList.Add(value);
            var process = Process.Start(info)!;
            pipe.DisposeLocalCopyOfClientHandle(); controlPipe.DisposeLocalCopyOfClientHandle();
            using var reader = new StreamReader(pipe);
            var bootstrap = JsonSerializer.Deserialize<LocalBootstrap>((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!, RoomProtocol.Json)!;
            return new(process, new StreamWriter(controlPipe) { AutoFlush = true }, bootstrap);
        }
        public async ValueTask DisposeAsync()
        {
            await control.WriteLineAsync("STOP");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(0, process.ExitCode);
            control.Dispose(); process.Dispose();
        }
    }
}
