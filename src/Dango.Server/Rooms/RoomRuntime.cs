using System.Threading.Channels;
using Dango.Protocol;
using Dango.Server.Sessions;
using Dango.Server.Storage;

namespace Dango.Server.Rooms;

public sealed class RoomRuntime : IAsyncDisposable
{
    private readonly IRoomClock clock;
    private readonly RoomOptions options;
    private readonly IRoomEventSink sink;
    private readonly IRoomSnapshotStore store;
    private readonly RoomSession[] sessions;
    private readonly PetState[] pets;
    private readonly Channel<Action> inbox;
    private readonly Channel<RoomEvent> outbound;
    private readonly SemaphoreSlim outboundSlots;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task reader;
    private readonly Task sender;
    private readonly HashSet<string> supersededConnections = new(StringComparer.Ordinal);
    private readonly Queue<string> supersededOrder = new();
    private long sequence = 0;
    private bool disposed;

    public string Epoch { get; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<string> JoinTickets => sessions.Select(s => s.JoinTicket).ToArray();

    public RoomRuntime(IRoomClock? clock = null, RoomOptions? options = null, IRoomEventSink? sink = null, IRoomSnapshotStore? store = null)
    {
        this.clock = clock ?? new MonotonicRoomClock();
        this.options = options ?? new RoomOptions();
        this.options.Validate();
        this.sink = sink ?? new NullRoomEventSink();
        this.store = store ?? new InMemoryRoomSnapshotStore();
        sessions = [new(0, this.options), new(1, this.options)];
        pets = sessions.Select((s, i) => new PetState(s.PetId, s.ParticipantId, new(i == 0 ? .3 : .7, .12),
            "center", null, null, PresetRef.Default, PresetRef.Default)).ToArray();
        inbox = Channel.CreateBounded<Action>(new BoundedChannelOptions(this.options.RoomQueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        outbound = Channel.CreateBounded<RoomEvent>(new BoundedChannelOptions(this.options.OutboundCapacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        outboundSlots = new(this.options.OutboundCapacity, this.options.OutboundCapacity);
        reader = ReadAsync();
        sender = SendAsync();
    }

    public async Task<JoinRoomResponse> JoinAsync(string connectionId, JoinRoomRequest request, Func<Task>? subscribe = null)
    {
        ValidateJoin(request.ProtocolVersion, request.RoomId, request.ClientInstanceId);
        var session = sessions.FirstOrDefault(s => SecretEquals(s.JoinTicket, request.JoinTicket))
            ?? throw new RoomRejectedException("invalid_ticket");
        await session.JoinGate.WaitAsync(shutdown.Token);
        try
        {
            await Enqueue(() => { CheckJoin(session, connectionId, request.ClientInstanceId); return true; });
            if (subscribe is not null) await subscribe();
            return await BindAsync(session, connectionId, request.ClientInstanceId);
        }
        finally { session.JoinGate.Release(); }
    }

    public async Task<JoinRoomResponse> ResumeAsync(string connectionId, ResumeRoomRequest request, Func<Task>? subscribe = null)
    {
        ValidateJoin(request.ProtocolVersion, request.RoomId, request.ClientInstanceId);
        if (request.RoomEpoch != Epoch) throw new RoomRejectedException("room_reset_required");
        var session = sessions.FirstOrDefault(s => SecretEquals(s.ResumeToken, request.ResumeToken))
            ?? throw new RoomRejectedException("invalid_resume_token");
        await session.JoinGate.WaitAsync(shutdown.Token);
        try
        {
            await Enqueue(() =>
            {
                if (session.ClientInstanceId != request.ClientInstanceId) throw new RoomRejectedException("invalid_resume_token");
                CheckConnectionSlot(session, connectionId);
                return true;
            });
            if (subscribe is not null) await subscribe();
            return await BindAsync(session, connectionId, request.ClientInstanceId);
        }
        finally { session.JoinGate.Release(); }
    }

    public Task DisconnectAsync(string connectionId, long generation) => Enqueue(() =>
    {
        var session = sessions.FirstOrDefault(s => s.ConnectionId == connectionId && s.Generation == generation);
        if (session is not null) session.ConnectionId = null;
        return true;
    });

    public Task<RoomSnapshot> SnapshotAsync(string connectionId) => Enqueue(() => Snapshot(RequireSession(connectionId), Now()));
    public Task<TimeSample> TimeAsync(string connectionId) => Enqueue(() =>
    {
        _ = RequireSession(connectionId);
        return new TimeSample(RoomProtocol.Version, RoomProtocol.RoomId, Epoch, Now(), sequence);
    });

    public async Task<CommandResult> CommandAsync(string connectionId, RoomCommand command)
    {
        if (command is null) throw new RoomRejectedException("invalid_command");
        // Lookup only selects an admission budget; all authorization is repeated inside the single reader.
        var session = sessions.FirstOrDefault(s => s.ConnectionId == connectionId);
        if (session is not null && !session.PendingSlots.Wait(0))
            return Rejected(command, "rate_limited", session.NextOrdinal, Now());
        try
        {
            return await Enqueue(() => Apply(connectionId, command));
        }
        catch (RoomRejectedException ex) when (ex.Code == "rate_limited")
        {
            return Rejected(command, ex.Code, session?.NextOrdinal ?? 1, Now());
        }
        finally { session?.PendingSlots.Release(); }
    }

    private CommandResult Apply(string connectionId, RoomCommand command)
    {
        var now = Now();
        Normalize(now);
        var session = sessions.FirstOrDefault(s => s.ConnectionId == connectionId);
        var invalid = RoomProtocol.Validate(command);
        if (invalid is not null) return Rejected(command, invalid, session?.NextOrdinal ?? 1, now);
        if (command.RoomId != RoomProtocol.RoomId) return Rejected(command, "forbidden", session?.NextOrdinal ?? 1, now);
        if (command.RoomEpoch != Epoch) return Rejected(command, "room_epoch_mismatch", session?.NextOrdinal ?? 1, now);
        if (session is null) return Rejected(command, supersededConnections.Contains(connectionId) ? "superseded_connection" : "not_joined", 1, now);
        if (session.Generation != command.ConnectionGeneration)
            return Rejected(command, "superseded_connection", session.NextOrdinal, now);

        foreach (var expired in session.Results.Where(p => now - p.Value.CreatedAt >= options.ResultRetentionMs).Select(p => p.Key).ToArray())
            session.Results.Remove(expired);
        var fingerprint = command.Type + ":" + command.Payload.Direction;
        if (session.Results.TryGetValue(command.CommandId, out var cached))
            return cached.Fingerprint == fingerprint ? cached.Result with { Outcome = "duplicate", OriginalOutcome = cached.Result.Outcome }
                : Rejected(command, "command_id_conflict", session.NextOrdinal, now);
        if (command.CommandOrdinal < session.NextOrdinal)
            return Rejected(command, "resync_required", session.NextOrdinal, now);
        if (command.CommandOrdinal != session.NextOrdinal)
            return Rejected(command, "command_order_mismatch", session.NextOrdinal, now);
        if (session.Results.Count >= options.ResultCapacity)
            return Rejected(command, "rate_limited", session.NextOrdinal, now);

        var petIndex = Array.IndexOf(sessions, session);
        var current = pets[petIndex];
        var proposed = current;
        string? error = null;
        var eventType = RoomProtocol.MotionChanged;
        session.Tokens = Math.Min(options.RateBurst, session.Tokens + Math.Max(0, now - session.TokenTime) * options.RatePerSecond / 1000);
        session.TokenTime = now;
        if (command.LastAppliedSequence > sequence || sequence - command.LastAppliedSequence > options.MaximumSequenceLag)
            error = "resync_required";
        else if (session.Tokens < 1) error = "rate_limited";
        else if (command.Type == RoomProtocol.Wave)
        {
            if (current.Motion is not null || current.Action is not null) error = "action_conflict";
            else
            {
                eventType = RoomProtocol.ActionStarted;
                proposed = current with { Action = new(Guid.NewGuid().ToString("N"), "wave", [current.PetId], now,
                    now + RoomOptions.WaveDurationMs, System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue), 1, "wave-nod") };
            }
        }
        else if (command.Payload.Direction == "stopped") proposed = current with { Motion = null };
        else if (current.Action is not null) error = "action_conflict";
        else
        {
            var direction = command.Payload.Direction!;
            var sign = direction == "left" ? -1 : 1;
            var distance = RoomOptions.Speed * RoomOptions.MovementDurationMs / 1000;
            var end = Math.Clamp(current.AnchorPosition.X + sign * distance, CoordinateSpace.Default.Left, CoordinateSpace.Default.Right);
            var duration = Math.Max(1, (long)Math.Ceiling(Math.Abs(end - current.AnchorPosition.X) / RoomOptions.Speed * 1000));
            proposed = current with
            {
                Facing = direction,
                Motion = end == current.AnchorPosition.X ? null : new(Guid.NewGuid().ToString("N"), current.AnchorPosition,
                    new(end, CoordinateSpace.Default.GroundY), now, now + Math.Min(RoomOptions.MovementDurationMs, duration), direction, 1),
            };
        }
        var changed = error is null && proposed != current;
        // The single writer reserves before consuming ordinal or changing state; unexpired results are never evicted.
        if (changed && !outboundSlots.Wait(0)) return Rejected(command, "rate_limited", session.NextOrdinal, now);
        session.NextOrdinal++;
        if (session.Tokens >= 1) session.Tokens--;
        RoomEvent? roomEvent = null;
        if (changed)
        {
            roomEvent = new(RoomProtocol.Version, Guid.NewGuid().ToString("N"), RoomProtocol.RoomId, Epoch,
                ++sequence, now, command.CommandId, eventType, proposed);
            pets[petIndex] = proposed;
        }
        var result = error is not null ? Rejected(command, error, session.NextOrdinal, now, true)
            : new CommandResult(RoomProtocol.Version, RoomProtocol.RoomId, Epoch, command.CommandId, "accepted", changed,
                sequence, true, session.NextOrdinal, null, roomEvent?.EventId, roomEvent?.Sequence, now, null);
        session.Results.Add(command.CommandId, new(fingerprint, now, result));
        if (roomEvent is not null && !outbound.Writer.TryWrite(roomEvent))
            throw new InvalidOperationException("Reserved outbound capacity unavailable.");
        return result;
    }

    private async Task<JoinRoomResponse> BindAsync(RoomSession session, string connectionId, string clientInstanceId)
    {
        var result = await Enqueue(() =>
        {
            CheckJoin(session, connectionId, clientInstanceId);
            var old = session.ConnectionId is { } previous && previous != connectionId ? new RoomRecipient(previous, session.Generation) : null;
            if (old is not null)
            {
                supersededConnections.Add(old.ConnectionId);
                supersededOrder.Enqueue(old.ConnectionId);
                if (supersededOrder.Count > 128) supersededConnections.Remove(supersededOrder.Dequeue());
            }
            if (session.ConnectionId != connectionId) session.Generation++;
            session.ConnectionId = connectionId;
            session.ClientInstanceId = clientInstanceId;
            var reply = new JoinRoomResponse(RoomProtocol.Version, session.Generation, session.ParticipantId, session.PetId,
                session.ResumeToken, session.NextOrdinal, Snapshot(session, Now()));
            return (Reply: reply, Old: old);
        });
        if (result.Old is not null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { await sink.SupersedeAsync(result.Old, timeout.Token); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* Old transport cannot block the current identity. */ }
        }
        return result.Reply;
    }

    private void CheckJoin(RoomSession session, string connectionId, string clientInstanceId)
    {
        if (session.ClientInstanceId is not null && session.ClientInstanceId != clientInstanceId)
            throw new RoomRejectedException("invalid_ticket");
        CheckConnectionSlot(session, connectionId);
    }

    private void CheckConnectionSlot(RoomSession session, string connectionId)
    {
        if (!RoomProtocol.IsId(connectionId) || sessions.Any(s => s != session && s.ConnectionId == connectionId))
            throw new RoomRejectedException("forbidden");
    }

    private static void ValidateJoin(string protocolVersion, string roomId, string clientInstanceId)
    {
        if (protocolVersion != RoomProtocol.Version) throw new RoomRejectedException("unsupported_protocol");
        if (roomId != RoomProtocol.RoomId || !RoomProtocol.IsId(clientInstanceId)) throw new RoomRejectedException("invalid_command");
    }

    private static bool SecretEquals(string expected, string? actual) => actual is not null &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(actual));

    private RoomSession RequireSession(string connectionId) => sessions.FirstOrDefault(s => s.ConnectionId == connectionId)
        ?? throw new RoomRejectedException("not_joined");

    private long Now() => Math.Max(0, clock.Milliseconds);
    private void Normalize(long now)
    {
        for (var i = 0; i < pets.Length; i++) pets[i] = RoomSampling.At(pets[i], now);
    }
    private RoomSnapshot Snapshot(RoomSession session, long now)
    {
        Normalize(now);
        var snapshot = new RoomSnapshot(RoomProtocol.Version, 1, RoomProtocol.RoomId, Epoch, sequence, now,
            session.PetId, CoordinateSpace.Default, pets.ToArray());
        store.Save(snapshot);
        return snapshot;
    }
    private CommandResult Rejected(RoomCommand command, string code, long nextOrdinal, long now, bool consumed = false) =>
        new(RoomProtocol.Version, RoomProtocol.RoomId, Epoch, command.CommandId, "rejected", false, sequence, consumed,
            nextOrdinal, null, null, null, now, new(code, code == "rate_limited" ? 100 : null));

    private Task<T> Enqueue<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (disposed || !inbox.Writer.TryWrite(() =>
            {
                try { result.SetResult(action()); }
                catch (Exception ex) { result.SetException(ex); }
            })) result.SetException(new RoomRejectedException("rate_limited"));
        return result.Task;
    }
    private async Task ReadAsync()
    {
        await foreach (var action in inbox.Reader.ReadAllAsync()) action();
    }
    private async Task SendAsync()
    {
        await foreach (var roomEvent in outbound.Reader.ReadAllAsync())
        {
            try
            {
                var recipients = await Enqueue(() => sessions.Where(s => s.ConnectionId is not null)
                    .Select(s => new RoomRecipient(s.ConnectionId!, s.Generation)).ToArray());
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await sink.SendAsync(roomEvent, recipients, timeout.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* Bounded sender; watermarks repair missed delivery. */ }
            finally { outboundSlots.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        shutdown.Cancel();
        inbox.Writer.TryComplete();
        await reader;
        outbound.Writer.TryComplete();
        await sender;
        shutdown.Dispose();
        outboundSlots.Dispose();
        foreach (var session in sessions) { session.JoinGate.Dispose(); session.PendingSlots.Dispose(); }
    }
}
