using Dango.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace Dango.Client.Sync;

public sealed record RoomConnectionDiagnostics(ReplicaState State, long LastSequence, long ConnectionFence,
    long ConnectionGeneration, long NextCommandOrdinal, string? LastOutcome, string? ErrorCode,
    double? BestRttMs, int ReconnectCount, int SnapshotCount, int WatermarkRepairCount);

/// <summary>
/// One serial control loop owns sends, synchronization and transport replacement. Network callbacks can
/// only enter the fenced reducer; no UI/native object is called. Credentials remain private and in memory.
/// </summary>
public sealed class RoomConnection : IAsyncDisposable
{
    public const int RpcTimeoutMs = 2000;
    public const int WatermarkPeriodMs = 2000;
    public const int WatchdogMs = 6000;
    public const int ReceiptEventDeadlineMs = 1000;
    private static readonly int[] ReconnectDelays = [500, 1000, 2000, 5000];
    private readonly LocalClientBootstrap _bootstrap;
    private readonly Uri _endpoint;
    private readonly IRoomTransportFactory _factory;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CommandDispatcher _commands = new();
    private readonly object _workerGate = new();
    private IRoomTransport? _transport;
    private Task? _worker;
    private string? _resumeToken;
    private volatile bool _wanted;
    private bool _disposed;
    private long _generation;
    private double _nextConnectMs;
    private double _nextTimePollMs;
    private double _lastResponseMs;
    private double _nextCommandMs;
    private long? _awaitedEventSequence;
    private double _eventDeadlineMs;
    private int _retryStep;
    private int _reconnectCount;
    private int _snapshotCount;
    private int _watermarkRepairCount;
    private string? _errorCode;

    public RoomConnection(LocalClientBootstrap bootstrap, TimeProvider? timeProvider = null,
        IRoomTransportFactory? transportFactory = null)
    {
        _bootstrap = bootstrap;
        if (!Uri.TryCreate(bootstrap.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0 ||
            endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0 ||
            (endpoint.Scheme == "http" && !endpoint.IsLoopback)) throw new ArgumentException("unsafe_endpoint");
        if (!RoomProtocol.IsId(bootstrap.RoomId) || !RoomProtocol.IsId(bootstrap.RoomEpoch) ||
            !RoomProtocol.IsId(bootstrap.ClientInstanceId) || string.IsNullOrWhiteSpace(bootstrap.JoinTicket))
            throw new ArgumentException("invalid_bootstrap");
        _endpoint = endpoint.AbsolutePath == "/" ? new(endpoint, "room") : endpoint;
        _factory = transportFactory ?? new SignalRRoomTransportFactory();
        Clock = new(timeProvider);
    }

    public RoomReplica Replica { get; } = new();
    public ServerClockEstimator Clock { get; }
    public long EstimatedServerTimeMs => Clock.EstimatedServerTimeMs;
    public RoomConnectionDiagnostics SafeDiagnostics
    {
        get
        {
            var frame = Replica.Frame;
            return new(frame.State, frame.LastSequence, frame.ConnectionFence, Interlocked.Read(ref _generation),
                _commands.NextCommandOrdinal, _commands.LastOutcome, _errorCode ?? frame.Reason ?? _commands.LastError,
                Clock.BestRttMs, Volatile.Read(ref _reconnectCount), Volatile.Read(ref _snapshotCount), Volatile.Read(ref _watermarkRepairCount));
        }
    }

    public bool SetMoveIntent(string direction) => Replica.Frame.State == ReplicaState.Live && _commands.EnqueueMove(direction);
    public bool StartWave() => Replica.Frame.State == ReplicaState.Live && _commands.EnqueueWave();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Replica.Frame.State == ReplicaState.ResetRequired) return;
            _wanted = true;
            if (_transport is null || Replica.Frame.State == ReplicaState.Disconnected)
                await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
            EnsureWorker();
        }
        finally { _operation.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _wanted = false;
        var fence = Replica.Frame.ConnectionFence;
        Replica.Disconnect(fence);
        _commands.Disconnect(fence);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopTransportAsync().ConfigureAwait(false); }
        finally { _operation.Release(); }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Use on window visibility return: no old frames are replayed while this barrier is pending.</summary>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_transport is not null && _wanted && Replica.Frame.State is ReplicaState.Live or ReplicaState.Resyncing)
                await SynchronizeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _operation.Release(); }
    }

    private void EnsureWorker()
    {
        lock (_workerGate) _worker ??= Task.Run(WorkAsync);
    }

    private async Task WorkAsync()
    {
        var cancellationToken = _lifetime.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!_wanted) continue;
                    var now = Clock.MonotonicMilliseconds;
                    var frame = Replica.Frame;
                    if (frame.State is ReplicaState.ResetRequired or ReplicaState.Faulted or ReplicaState.Incompatible)
                    {
                        _wanted = false;
                        _commands.ClearForReset();
                        await StopTransportAsync().ConfigureAwait(false);
                        continue;
                    }
                    if (frame.State == ReplicaState.Disconnected || _transport is null)
                    {
                        if (now >= _nextConnectMs) await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    _ = Clock.EstimatedServerTimeMs; // Detect a suspended UI/process using the same monotonic clock.
                    if (now - _lastResponseMs >= WatchdogMs)
                    {
                        MarkDisconnected(frame.ConnectionFence, "response_watchdog");
                        continue;
                    }
                    if (_commands.NeedsResume)
                    {
                        MarkDisconnected(frame.ConnectionFence, "command_resync_required");
                        continue;
                    }
                    if (frame.State == ReplicaState.Resyncing || Clock.NeedsResync ||
                        (_awaitedEventSequence is { } sequence && frame.LastSequence < sequence && now >= _eventDeadlineMs))
                    {
                        await SynchronizeCoreAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    if (_awaitedEventSequence <= frame.LastSequence) _awaitedEventSequence = null;
                    if (now >= _nextTimePollMs)
                    {
                        await PollTimeAsync(frame.ConnectionFence, cancellationToken).ConfigureAwait(false);
                        _nextTimePollMs = Clock.MonotonicMilliseconds + WatermarkPeriodMs;
                    }
                    if (Replica.Frame.State == ReplicaState.Live && now >= _nextCommandMs)
                        await SendNextAsync(frame.ConnectionFence, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) { HandleFailure(Replica.Frame.ConnectionFence, exception); }
                finally { _operation.Release(); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        var old = Replica.Frame.ConnectionFence;
        _commands.Disconnect(old);
        Replica.Disconnect(old);
        await StopTransportAsync().ConfigureAwait(false);
        if (!_wanted) return;
        var fence = Replica.BeginConnection(_bootstrap.RoomId, _bootstrap.RoomEpoch);
        var syncFence = Replica.BeginSynchronization(fence);
        var transport = _factory.Create(_endpoint);
        _transport = transport;
        transport.RoomEventReceived += value => Replica.Receive(fence, value);
        transport.Closed += () => MarkDisconnected(fence, "transport_closed");
        transport.SessionSupersededReceived += value =>
        {
            if (Replica.Frame.ConnectionFence != fence || value.ConnectionGeneration < Interlocked.Read(ref _generation)) return;
            _wanted = false;
            MarkDisconnected(fence, "superseded_connection");
        };
        try
        {
            using (var timeout = RpcCancellation(cancellationToken))
                await transport.StartAsync(timeout.Token).ConfigureAwait(false);
            var started = Clock.MonotonicMilliseconds;
            var response = _resumeToken is null
                ? await InvokeAsync<JoinRoomResponse>("JoinRoom", new JoinRoomRequest(RoomProtocol.Version,
                    _bootstrap.RoomId, _bootstrap.ClientInstanceId, _bootstrap.JoinTicket), cancellationToken).ConfigureAwait(false)
                : await InvokeAsync<JoinRoomResponse>("ResumeRoom", new ResumeRoomRequest(RoomProtocol.Version,
                    _bootstrap.RoomId, _bootstrap.RoomEpoch, _bootstrap.ClientInstanceId, _resumeToken), cancellationToken).ConfigureAwait(false);
            if (!_wanted || Replica.Frame.ConnectionFence != fence) return;
            if (response.ProtocolVersion != RoomProtocol.Version || response.ConnectionGeneration < 1 || response.NextCommandOrdinal < 1 ||
                !RoomProtocol.IsId(response.SelfParticipantId) || !RoomProtocol.IsId(response.SelfPetId) ||
                string.IsNullOrWhiteSpace(response.ResumeToken) || response.Snapshot.SelfPetId != response.SelfPetId ||
                !response.Snapshot.Pets.Any(p => p.PetId == response.SelfPetId && p.ParticipantId == response.SelfParticipantId))
                throw new InvalidOperationException("invalid_join_response");
            var applied = Replica.ApplySnapshot(syncFence, response.Snapshot);
            if (applied is ReplicaUpdate.Invalid or ReplicaUpdate.Ignored or ReplicaUpdate.ResetRequired) return;
            _generation = response.ConnectionGeneration;
            _resumeToken = response.ResumeToken;
            var resumed = _commands.BeginSession(fence, _bootstrap.RoomId, _bootstrap.RoomEpoch,
                response.ConnectionGeneration, response.NextCommandOrdinal);
            if (resumed == ResumeDisposition.LedgerMismatch) throw new InvalidOperationException("command_ledger_mismatch");
            var rtt = Clock.MonotonicMilliseconds - started;
            Clock.Rebase(response.Snapshot.ServerTimeMs + (long)(rtt <= ServerClockEstimator.MaximumRttMs ? rtt / 2 : 0));
            _lastResponseMs = Clock.MonotonicMilliseconds;
            _retryStep = 0;
            _errorCode = null;
            _awaitedEventSequence = null;
            _nextCommandMs = 0;
            Interlocked.Increment(ref _snapshotCount);
            if (fence > 1) Interlocked.Increment(ref _reconnectCount);
            for (var index = 0; index < 5 && _wanted; index++) await PollTimeAsync(fence, cancellationToken).ConfigureAwait(false);
            _nextTimePollMs = Clock.MonotonicMilliseconds + WatermarkPeriodMs;
        }
        catch (Exception exception) { HandleFailure(fence, exception); }
    }

    private async Task SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        var fence = Replica.Frame.ConnectionFence;
        var attempt = Replica.BeginSynchronization(fence);
        try
        {
            var snapshot = await InvokeAsync<RoomSnapshot>("GetSnapshot", null, cancellationToken).ConfigureAwait(false);
            var result = Replica.ApplySnapshot(attempt, snapshot);
            if (result is ReplicaUpdate.Applied or ReplicaUpdate.ResyncRequired)
            {
                _lastResponseMs = Clock.MonotonicMilliseconds;
                Clock.Rebase(snapshot.ServerTimeMs);
                Interlocked.Increment(ref _snapshotCount);
                await PollTimeAsync(fence, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) { HandleFailure(fence, exception); }
    }

    private async Task PollTimeAsync(long fence, CancellationToken cancellationToken)
    {
        var sent = Clock.MonotonicMilliseconds;
        var sample = await InvokeAsync<TimeSample>("GetRoomTime", null, cancellationToken).ConfigureAwait(false);
        if (Replica.Frame.ConnectionFence != fence || !_wanted) return;
        var update = Replica.ObserveWatermark(fence, sample);
        if (update == ReplicaUpdate.ResyncRequired) Interlocked.Increment(ref _watermarkRepairCount);
        if (update is ReplicaUpdate.Invalid or ReplicaUpdate.ResetRequired) return;
        _lastResponseMs = Clock.MonotonicMilliseconds;
        Clock.AddSample(sent, _lastResponseMs, sample.ServerTimeMs);
    }

    private async Task SendNextAsync(long fence, CancellationToken cancellationToken)
    {
        var command = _commands.Prepare(fence, Replica.Frame.LastSequence, Clock.MonotonicMilliseconds);
        if (command is null) return;
        try
        {
            var result = await InvokeAsync<CommandResult>("SendCommand", command, cancellationToken).ConfigureAwait(false);
            var disposition = _commands.ApplyResult(fence, result);
            if (disposition == CommandDisposition.Ignored) return;
            _lastResponseMs = Clock.MonotonicMilliseconds;
            if (disposition == CommandDisposition.ResetRequired)
            {
                Replica.ResetRequired(fence);
                _wanted = false;
            }
            else if (disposition == CommandDisposition.ResumeRequired) MarkDisconnected(fence, "command_resync_required");
            else if (disposition == CommandDisposition.Retry)
                _nextCommandMs = Clock.MonotonicMilliseconds + Math.Clamp(result.Error?.RetryAfterMs ?? 250, 25, 5000);
            else if (result.EventSequence is { } sequence && Replica.Frame.LastSequence < sequence)
            {
                if (_awaitedEventSequence is null) _eventDeadlineMs = Clock.MonotonicMilliseconds + ReceiptEventDeadlineMs;
                _awaitedEventSequence = Math.Max(_awaitedEventSequence ?? 0, sequence);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The result is unknown; preserve ID/ordinal and retry, never synthesize a rejection or new command.
            _nextCommandMs = Clock.MonotonicMilliseconds + 500;
            _errorCode = "command_timeout";
        }
        catch (Exception exception) { HandleFailure(fence, exception); }
    }

    private async Task<T> InvokeAsync<T>(string method, object? argument, CancellationToken cancellationToken)
    {
        using var timeout = RpcCancellation(cancellationToken);
        return await _transport!.InvokeAsync<T>(method, argument, timeout.Token).ConfigureAwait(false);
    }

    private CancellationTokenSource RpcCancellation(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        source.CancelAfter(RpcTimeoutMs);
        return source;
    }

    private void HandleFailure(long fence, Exception exception)
    {
        if (Replica.Frame.ConnectionFence != fence) return;
        // Only allow-listed codes escape. Hub exception text, credentials, endpoints and complete packets never do.
        var code = exception is HubException && exception.Message.Contains("room_reset_required", StringComparison.Ordinal)
            ? "room_reset_required" : exception is OperationCanceledException ? "rpc_timeout" : "transport_failure";
        if (code == "room_reset_required")
        {
            _wanted = false;
            _commands.ClearForReset();
            Replica.ResetRequired(fence);
            _errorCode = code;
        }
        else MarkDisconnected(fence, code);
    }

    private void MarkDisconnected(long fence, string code)
    {
        var frame = Replica.Frame;
        if (frame.ConnectionFence != fence || frame.State is ReplicaState.ResetRequired or ReplicaState.Faulted or ReplicaState.Incompatible) return;
        Replica.Disconnect(fence, code);
        _commands.Disconnect(fence);
        _errorCode = code;
        _nextConnectMs = Clock.MonotonicMilliseconds + ReconnectDelays[Math.Min(_retryStep++, ReconnectDelays.Length - 1)];
    }

    private async Task StopTransportAsync()
    {
        var transport = _transport;
        _transport = null;
        if (transport is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(RpcTimeoutMs);
            await transport.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception) { _errorCode ??= "transport_stop_failed"; }
        try { await transport.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { _errorCode ??= "transport_dispose_failed"; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _wanted = false;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_worker is not null) await _worker.ConfigureAwait(false);
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            var fence = Replica.Frame.ConnectionFence;
            Replica.Disconnect(fence);
            _commands.Disconnect(fence);
            await StopTransportAsync().ConfigureAwait(false);
        }
        finally { _operation.Release(); }
        _lifetime.Dispose();
    }
}
