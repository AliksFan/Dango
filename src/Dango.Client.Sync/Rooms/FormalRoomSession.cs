using Dango.Protocol;

namespace Dango.Client.Sync.Rooms;

// One owner per account binding and process instance. All mutations are explicit and serialized.
// This class has no scene, movement protocol, local save writer or automatic gameplay authority.
public sealed class FormalRoomSession : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly HashSet<string> NoticeTypes = new(StringComparer.Ordinal)
        { "created", "updated", "dissolved", "restored", "entered", "left", "control", "lease_ended", "offline", "code_reset" };
    private readonly object sync = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly IRoomAccessClient client;
    private readonly Func<IFormalRoomConnection> connectionFactory;
    private readonly TimeProvider clock;
    private readonly HashSet<Task> background = [];
    private CancellationTokenSource lifetime = new();
    private Transport? transport;
    private PendingCommand? pending;
    private FormalRoomSessionState current;
    private long epoch = 1;
    private long transportGeneration;
    private long personalTimestamp;
    private bool reset;
    private bool disposed;
    private bool recovering;
    private bool refreshingNotice;
    private bool noticePending;
    private bool autoRecoveryAllowed = true;
    private bool requiresReconciliation;

    public FormalRoomSession(IRoomAccessClient client, Guid instanceId, TimeProvider? timeProvider = null,
        Func<IFormalRoomConnection>? connectionFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.UserId == Guid.Empty || client.PetId == Guid.Empty || instanceId == Guid.Empty)
            throw Rejected("room_binding_invalid");
        this.client = client; this.clock = timeProvider ?? TimeProvider.System;
        this.connectionFactory = connectionFactory ?? client.CreateConnection;
        current = new(client.UserId, client.PetId, instanceId, epoch, FormalRoomSessionPhase.Idle,
            null, null, FormalRoomRole.None, false, null, null, null);
    }

    public FormalRoomSessionState Current { get { lock (sync) return current; } }
    public event Action<FormalRoomSessionState>? Changed;

    public Task OpenAsync(CancellationToken ct = default) => ResumeAsync(ct);
    public Task ResumeAsync(CancellationToken ct = default) => RunAsync(async (e, token) =>
    {
        lock (sync) autoRecoveryAllowed = true;
        await ReconcileAsync(e, true, token);
    }, ct, resume: true);
    public Task RefreshAsync(CancellationToken ct = default) => RunAsync((e, token) => ReconcileAsync(e, false, token), ct);

    public Task EnterAsync(Guid roomId, string? code = null, CancellationToken ct = default) => NewMutationAsync(
        FormalRoomOperation.Enter, async (e, token) =>
        {
            if (roomId == Guid.Empty) throw Rejected("room_invalid");
            var personal = await ReadPersonalAsync(e, token);
            return new(FormalRoomOperation.Enter, roomId, new RoomEnterRequest(Guid.NewGuid(), current.InstanceId,
                "desktop", personal.RoomId, personal.AssignmentVersion, code), null, null);
        }, ct);

    public Task TakeControlAsync(CancellationToken ct = default) => ControlMutationAsync(FormalRoomOperation.TakeControl, ct);
    // The outer product UI owns the explicit second confirmation before this method is invoked.
    public Task LeaveAsync(CancellationToken ct = default) => ControlMutationAsync(FormalRoomOperation.Leave, ct);
    public Task EndCurrentLeaseAsync(CancellationToken ct = default) => NewMutationAsync(FormalRoomOperation.EndLease,
        async (e, token) =>
        {
            var personal = await ReadPersonalAsync(e, token);
            var lease = OwnLease(personal) ?? throw Rejected("room_reentry_required");
            return new(FormalRoomOperation.EndLease, personal.RoomId, null, null,
                new RoomLeaseEndRequest(Guid.NewGuid(), lease.LeaseId, current.InstanceId));
        }, ct);

    public Task RetryPendingAsync(CancellationToken ct = default) => RunAsync(async (e, token) =>
    {
        PendingCommand command;
        lock (sync) command = pending ?? throw Rejected("room_no_pending_operation");
        await DispatchMutationAsync(command, e, token);
    }, ct, resume: true);

    public bool ConfirmPresentationReady(FormalRoomPresentationToken token)
    {
        lock (sync)
        {
            if (!ValidPresentationLocked(token)) return false;
            PublishLocked(current with { Phase = FormalRoomSessionPhase.Presented, FailureCode = null });
            return true;
        }
    }

    public Task FailPresentationAsync(FormalRoomPresentationToken token, CancellationToken ct = default) =>
        RunAsync(async (e, cancellation) =>
        {
            PendingCommand command;
            lock (sync)
            {
                if (!ValidPresentationLocked(token)) throw Rejected("room_presentation_stale");
                if (pending is not null) throw Rejected("room_pending_operation");
                PublishLocked(current with { Phase = FormalRoomSessionPhase.Failed, PresentationToken = null,
                    FailureCode = "room_presentation_failed" });
                command = new(FormalRoomOperation.EndLease, token.RoomId, null, null,
                    new RoomLeaseEndRequest(Guid.NewGuid(), token.LeaseId, current.InstanceId));
                pending = command;
            }
            // End only the captured lease. Never restore an old assignment or leave other devices.
            await DispatchMutationAsync(command, e, cancellation);
        }, ct);

    public Task SuspendAsync(CancellationToken ct = default) => InvalidateAsync(false, false, ct);
    public Task ResetAsync(CancellationToken ct = default) => InvalidateAsync(true, false, ct);
    public async ValueTask DisposeAsync() => await InvalidateAsync(true, true, CancellationToken.None);

    private Task ControlMutationAsync(FormalRoomOperation action, CancellationToken ct) => NewMutationAsync(action,
        async (e, token) =>
        {
            var personal = await ReadPersonalAsync(e, token);
            var lease = OwnLease(personal) ?? throw Rejected("room_reentry_required");
            if (personal.RoomId is not { } room) throw Rejected("room_reentry_required");
            if (action == FormalRoomOperation.Leave && (!lease.Controls || personal.ControlLeaseId != lease.LeaseId))
                throw Rejected("room_control_required");
            return new(action, room, null, new RoomControlRequest(Guid.NewGuid(), lease.LeaseId, current.InstanceId,
                room, personal.AssignmentVersion, personal.ControlGeneration), null);
        }, ct);

    private Task NewMutationAsync(FormalRoomOperation action, Func<long, CancellationToken, Task<PendingCommand>> create,
        CancellationToken ct) => RunAsync(async (e, token) =>
    {
        lock (sync)
        {
            if (pending is not null) throw Rejected("room_pending_operation");
            if (requiresReconciliation) throw Rejected("room_reconciliation_required");
        }
        var command = await create(e, token);
        Check(e, token);
        lock (sync) { pending = command; PublishPendingLocked(FormalRoomSessionPhase.Mutating); }
        await DispatchMutationAsync(command, e, token);
    }, ct, resume: true);

    private async Task DispatchMutationAsync(PendingCommand command, long e, CancellationToken ct)
    {
        Check(e, ct);
        lock (sync) PublishPendingLocked(FormalRoomSessionPhase.Mutating);
        // The exact immutable command and request ID survive an uncertain result and explicit retry.
        try
        {
            _ = command.Action switch
            {
                FormalRoomOperation.Enter => await client.EnterAsync(command.RoomId!.Value, command.Enter!, ct),
                FormalRoomOperation.TakeControl => await client.ControlAsync(command.Control!, ct),
                FormalRoomOperation.Leave => await client.LeaveAsync(command.Control!, ct),
                FormalRoomOperation.EndLease => await client.EndLeaseAsync(command.End!, ct),
                _ => throw Rejected("room_request_invalid")
            };
        }
        catch (RoomAccessClientException error) when (error.Failure is RoomAccessFailure.Authentication or RoomAccessFailure.Conflict or RoomAccessFailure.Rejected)
        {
            lock (sync) if (e == epoch && ReferenceEquals(pending, command)) pending = null;
            throw;
        }
        Check(e, ct);
        lock (sync) { if (ReferenceEquals(pending, command)) pending = null; requiresReconciliation = true; }
        // A replay receipt describes a historical commit, not the current lease or authority.
        await ReconcileAsync(e, true, ct);
    }

    private async Task RunAsync(Func<long, CancellationToken, Task> action, CancellationToken ct, bool resume = false)
    {
        long e; CancellationToken ownerToken;
        lock (sync)
        {
            ThrowIfUnavailableLocked();
            if (current.Phase == FormalRoomSessionPhase.Suspended)
            {
                if (!resume) throw Rejected("room_session_suspended");
                lifetime.Dispose(); lifetime = new();
                PublishLocked(current with { Phase = FormalRoomSessionPhase.Idle, FailureCode = null });
            }
            e = epoch; ownerToken = lifetime.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, ownerToken);
        await operationGate.WaitAsync(linked.Token);
        try { Check(e, linked.Token); await action(e, linked.Token); }
        catch (Exception error) when (error is RoomAccessClientException or OperationCanceledException)
        {
            if (error is not RoomAccessClientException { Code: "room_presentation_stale" or "room_no_pending_operation"
                or "room_pending_operation" or "room_control_required" or "room_invalid" or "room_reentry_required"
                or "room_reconciliation_required" })
                await HandleFailureAsync(error, e);
            throw;
        }
        finally { operationGate.Release(); }
    }

    private async Task ReconcileAsync(long e, bool freshTransport, CancellationToken ct)
    {
        var personal = await ReadPersonalAsync(e, ct);
        if (personal.RoomId is null || OwnLease(personal) is null)
        {
            await DetachAsync(); Check(e, ct);
            lock (sync)
            {
                requiresReconciliation = false;
                PublishLocked(current with { Phase = FormalRoomSessionPhase.ReentryRequired,
                Personal = personal, Shared = null, Role = FormalRoomRole.None, ServerAttached = false,
                PresentationToken = null, Pending = PendingView(), FailureCode = null });
            }
            return;
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var lease = OwnLease(personal)!;
            Transport? owner;
            lock (sync) owner = transport;
            if (freshTransport || owner is null || owner.Unusable || owner.LeaseId != lease.LeaseId || owner.RoomId != personal.RoomId)
            {
                await DetachAsync(); Check(e, ct);
                var connection = connectionFactory();
                lock (sync)
                {
                    Check(e, ct);
                    owner = new(connection, ++transportGeneration, lease.LeaseId, personal.RoomId!.Value,
                        () => OnClosed(connection), notice => OnNotice(connection, notice));
                    transport = owner;
                    connection.Closed += owner.Closed; connection.RoomChanged += owner.Notice;
                    PublishLocked(current with { Phase = FormalRoomSessionPhase.Connecting, ServerAttached = false,
                        Shared = null, Role = FormalRoomRole.None, PresentationToken = null, FailureCode = null });
                }
                await connection.StartAsync(ct); CheckTransport(e, owner, ct);
                await connection.AttachAsync(new(lease.LeaseId, current.InstanceId), ct); CheckTransport(e, owner, ct);
            }
            var shared = await owner.Connection.SnapshotAsync(personal.RoomId!.Value, new(lease.LeaseId, current.InstanceId), ct);
            CheckTransport(e, owner, ct);
            var final = await owner.Connection.PersonalAsync(ct); ValidatePersonal(final); CheckTransport(e, owner, ct);
            if (!SameAuthority(personal, final) || OwnLease(final)?.LeaseId != lease.LeaseId
                || shared.RoomId != final.RoomId || shared.Kind != final.RoomKind || !shared.Occupants.Any(p => p.PetId == client.PetId
                    && p.UserId == client.UserId && p.AssignmentVersion == final.AssignmentVersion))
            {
                personal = final; freshTransport = true;
                if (personal.RoomId is null || OwnLease(personal) is null) break;
                continue;
            }
            lock (sync)
            {
                CheckTransport(e, owner, ct);
                requiresReconciliation = false;
                personalTimestamp = clock.GetTimestamp();
                var previous = current.PresentationToken;
                var presentation = previous is not null && previous.Epoch == e && previous.TransportGeneration == owner.Generation
                    && previous.LeaseId == lease.LeaseId && previous.RoomId == shared.RoomId
                    && previous.AssignmentVersion == final.AssignmentVersion && previous.ControlGeneration == final.ControlGeneration
                    && previous.RoomVersion == shared.RoomVersion ? previous : new FormalRoomPresentationToken(e, owner.Generation,
                        lease.LeaseId, shared.RoomId, final.AssignmentVersion, final.ControlGeneration, shared.RoomVersion);
                var presented = ReferenceEquals(previous, presentation) && current.Presented;
                PublishLocked(current with { Phase = presented ? FormalRoomSessionPhase.Presented : FormalRoomSessionPhase.AwaitingPresentation,
                    Personal = final, Shared = shared, ServerAttached = true,
                    Role = final.ControlLeaseId == lease.LeaseId && OwnLease(final)!.Controls ? FormalRoomRole.Control : FormalRoomRole.Observe,
                    PresentationToken = presentation, Pending = PendingView(), FailureCode = null });
                StartHeartbeatLocked(owner, e);
                owner.DeadlineTimer?.Dispose();
                var deadlineGeneration = ++owner.DeadlineGeneration;
                owner.DeadlineTimer = clock.CreateTimer(_ => OnLeaseDeadline(owner, e, deadlineGeneration), null,
                    OwnLease(final)!.ExpiresAt - final.ServerTime, Timeout.InfiniteTimeSpan);
            }
            return;
        }
        await DetachAsync(); Check(e, ct);
        lock (sync) PublishLocked(current with { Personal = personal, Shared = null, ServerAttached = false,
            Role = FormalRoomRole.None, PresentationToken = null, Pending = PendingView(),
            Phase = OwnLease(personal) is null ? FormalRoomSessionPhase.ReentryRequired : FormalRoomSessionPhase.Conflict,
            FailureCode = "room_assignment_changed" });
    }

    private async Task<PersonalRoomSnapshot> ReadPersonalAsync(long e, CancellationToken ct)
    {
        var personal = await client.PersonalAsync(ct); ValidatePersonal(personal); Check(e, ct);
        lock (sync)
        {
            personalTimestamp = clock.GetTimestamp();
            var changed = current.Personal is not { } old || !SameAuthority(old, personal)
                || OwnLease(old)?.LeaseId != OwnLease(personal)?.LeaseId;
            PublishLocked(current with { Personal = personal, Pending = PendingView(),
                Phase = changed ? FormalRoomSessionPhase.Reading : current.Phase,
                ServerAttached = !changed && current.ServerAttached,
                Role = changed ? FormalRoomRole.None : current.Role,
                Shared = changed ? null : current.Shared, PresentationToken = changed ? null : current.PresentationToken });
        }
        return personal;
    }

    private void ValidatePersonal(PersonalRoomSnapshot personal)
    {
        if (personal.UserId != client.UserId || personal.PetId != client.PetId || personal.SchemaVersion != 1
            || personal.AssignmentVersion < 1 || personal.ControlGeneration < 0 || personal.Leases.Length > 8
            || personal.Leases.Count(l => l.InstanceId == current.InstanceId) > 1)
            throw new RoomAccessClientException(RoomAccessFailure.InvalidResponse, "remote_response_invalid");
    }

    private RoomLeaseView? OwnLease(PersonalRoomSnapshot personal) => personal.RoomId is null ? null :
        personal.Leases.SingleOrDefault(l => l.InstanceId == current.InstanceId && l.ExpiresAt > personal.ServerTime);
    private static bool SameAuthority(PersonalRoomSnapshot a, PersonalRoomSnapshot b) => a.RoomId == b.RoomId
        && a.AssignmentVersion == b.AssignmentVersion && a.ControlGeneration == b.ControlGeneration && a.ControlLeaseId == b.ControlLeaseId;

    private bool ValidPresentationLocked(FormalRoomPresentationToken token) => !reset && !disposed && current.ServerAttached
        && ReferenceEquals(token, current.PresentationToken) && token.Epoch == epoch && transport?.Generation == token.TransportGeneration
        && current.Personal is { } personal && OwnLease(personal) is { } lease && lease.LeaseId == token.LeaseId
        && lease.ExpiresAt - personal.ServerTime > clock.GetElapsedTime(personalTimestamp);

    private void StartHeartbeatLocked(Transport owner, long e)
    {
        if (owner.HeartbeatStarted) return;
        owner.HeartbeatStarted = true;
        var heartbeatTimer = new PeriodicTimer(HeartbeatInterval, clock);
        TrackLocked(Task.Run(async () =>
        {
            try
            {
                using var timer = heartbeatTimer;
                while (await timer.WaitForNextTickAsync(owner.Lifetime.Token))
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8), clock);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token, timeout.Token);
                    await RunAsync(async (generation, token) =>
                    {
                        CheckTransport(generation, owner, token);
                        await owner.Connection.HeartbeatAsync(new(owner.LeaseId, current.InstanceId), token);
                        CheckTransport(generation, owner, token);
                        await ReconcileAsync(generation, false, token);
                    }, linked.Token);
                }
            }
            catch (OperationCanceledException) when (owner.Lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is RoomAccessClientException or OperationCanceledException or ObjectDisposedException)
            { ScheduleRecovery(e); }
        }));
    }

    private void OnClosed(IFormalRoomConnection connection)
    {
        long e;
        lock (sync)
        {
            if (reset || disposed || transport?.Connection != connection) return;
            transport.Unusable = true;
            e = epoch;
            PublishLocked(current with { Phase = FormalRoomSessionPhase.Disconnected, ServerAttached = false,
                Role = FormalRoomRole.None, Shared = null, PresentationToken = null, FailureCode = "room_transport_closed" });
        }
        ScheduleRecovery(e);
    }

    private void OnLeaseDeadline(Transport owner, long e, long deadlineGeneration)
    {
        lock (sync)
        {
            if (e != epoch || reset || disposed || !ReferenceEquals(transport, owner)
                || owner.DeadlineGeneration != deadlineGeneration) return;
            owner.Unusable = true;
            PublishLocked(current with { Phase = FormalRoomSessionPhase.Disconnected, ServerAttached = false,
                Role = FormalRoomRole.None, Shared = null, PresentationToken = null,
                FailureCode = "room_lease_deadline_elapsed" });
        }
        ScheduleRecovery(e);
    }

    private void OnNotice(IFormalRoomConnection connection, RoomChangeNotice notice)
    {
        lock (sync)
        {
            if (reset || disposed || transport?.Connection != connection || !current.ServerAttached
                || notice.RoomId != current.Personal?.RoomId || notice.EventId == Guid.Empty || notice.RoomVersion < 1
                || !NoticeTypes.Contains(notice.EventType)) return;
            noticePending = true;
            if (refreshingNotice) return;
            refreshingNotice = true;
            var e = epoch; var token = lifetime.Token;
            TrackLocked(Task.Run(async () =>
            {
                try
                {
                    // At most two coalesced reads per burst. No notice grants authority directly.
                    for (var i = 0; i < 2; i++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), clock, token);
                        lock (sync) { if (!noticePending || e != epoch) return; noticePending = false; }
                        await RefreshAsync(token);
                    }
                }
                catch (RoomAccessClientException error) when (error.Failure == RoomAccessFailure.Unavailable) { ScheduleRecovery(e); }
                catch (Exception error) when (error is RoomAccessClientException or OperationCanceledException or ObjectDisposedException) { }
                finally { lock (sync) { if (e == epoch) { refreshingNotice = false; noticePending = false; } } }
            }));
        }
    }

    private void ScheduleRecovery(long e)
    {
        lock (sync)
        {
            if (reset || disposed || e != epoch || recovering || !autoRecoveryAllowed || lifetime.IsCancellationRequested) return;
            recovering = true;
            var token = lifetime.Token;
            TrackLocked(Task.Run(async () =>
            {
                try
                {
                    for (var i = 0; i < 3; i++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1 << i), clock, token);
                        lock (sync) if (e != epoch || !autoRecoveryAllowed) return;
                        try { await RunAsync((generation, ct) => ReconcileAsync(generation, true, ct), token); return; }
                        catch (RoomAccessClientException error) when (error.Failure == RoomAccessFailure.Unavailable) { }
                    }
                }
                catch (Exception error) when (error is RoomAccessClientException or OperationCanceledException or ObjectDisposedException) { }
                finally { lock (sync) if (e == epoch) recovering = false; }
            }));
        }
    }

    private async Task HandleFailureAsync(Exception error, long e)
    {
        lock (sync) if (e != epoch || reset || disposed) return;
        await DetachAsync();
        lock (sync)
        {
            if (e != epoch || reset || disposed) return;
            var failure = error as RoomAccessClientException;
            var unknown = pending is not null;
            if (failure?.Code == "room_transport_conflict") autoRecoveryAllowed = false;
            PublishLocked(current with { Phase = unknown ? FormalRoomSessionPhase.Unknown
                    : requiresReconciliation ? FormalRoomSessionPhase.ReconciliationRequired
                    : failure?.Failure == RoomAccessFailure.Conflict ? FormalRoomSessionPhase.Conflict
                    : failure?.Failure is RoomAccessFailure.Authentication or RoomAccessFailure.Rejected or RoomAccessFailure.InvalidResponse
                        ? FormalRoomSessionPhase.Failed : FormalRoomSessionPhase.Disconnected,
                Shared = null, Role = FormalRoomRole.None, ServerAttached = false, PresentationToken = null,
                Pending = PendingView(), FailureCode = failure?.Code ?? "room_request_cancelled" });
        }
    }

    private async Task InvalidateAsync(bool clear, bool dispose, CancellationToken ct)
    {
        Task[] jobs;
        lock (sync)
        {
            if (disposed) return;
            epoch++; lifetime.Cancel(); reset |= clear; disposed |= dispose;
            recovering = refreshingNotice = noticePending = false;
            if (clear) { pending = null; requiresReconciliation = false; }
            PublishLocked(current with { Epoch = epoch, Phase = dispose ? FormalRoomSessionPhase.Disposed
                    : clear ? FormalRoomSessionPhase.Reset : FormalRoomSessionPhase.Suspended,
                UserId = clear ? Guid.Empty : current.UserId, PetId = clear ? Guid.Empty : current.PetId,
                Personal = clear ? null : current.Personal, Shared = null, Role = FormalRoomRole.None,
                ServerAttached = false, PresentationToken = null, Pending = PendingView(), FailureCode = null });
            jobs = background.ToArray();
        }
        await DetachAsync();
        await operationGate.WaitAsync(ct); operationGate.Release();
        await Task.WhenAll(jobs);
        if (dispose) lifetime.Dispose();
    }

    private Task DetachAsync()
    {
        lock (sync)
        {
            var owner = transport;
            if (owner is null) return Task.CompletedTask;
            transport = null;
            owner.Connection.Closed -= owner.Closed; owner.Connection.RoomChanged -= owner.Notice;
            owner.DeadlineTimer?.Dispose();
            owner.Lifetime.Cancel();
            return CleanupAsync(owner);
        }
    }

    private static async Task CleanupAsync(Transport owner)
    {
        try { await owner.Connection.StopAsync(CancellationToken.None); }
        catch (Exception error) when (error is RoomAccessClientException or OperationCanceledException) { }
        finally { await owner.Connection.DisposeAsync(); owner.Lifetime.Dispose(); }
    }

    private void TrackLocked(Task task)
    {
        background.Add(task);
        _ = task.ContinueWith(completed => { lock (sync) background.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Check(long e, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync) { if (e != epoch || reset || disposed) throw new OperationCanceledException(ct); }
    }
    private void CheckTransport(long e, Transport owner, CancellationToken ct)
    {
        Check(e, ct);
        lock (sync) if (!ReferenceEquals(transport, owner) || owner.Unusable) throw new OperationCanceledException(ct);
    }
    private void ThrowIfUnavailableLocked()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (reset) throw Rejected("room_binding_reset");
    }
    private FormalRoomPendingOperation? PendingView() => pending is null ? null : new(pending.Action, pending.RequestId);
    private void PublishPendingLocked(FormalRoomSessionPhase phase) => PublishLocked(current with
        { Phase = phase, Pending = PendingView(), ServerAttached = false, Role = FormalRoomRole.None, PresentationToken = null });
    private void PublishLocked(FormalRoomSessionState state)
    {
        state = state with { RequiresReconciliation = requiresReconciliation };
        current = state;
        if (Changed is not { } handlers) return;
        foreach (Action<FormalRoomSessionState> handler in handlers.GetInvocationList())
        {
            try { handler(state); }
            catch (Exception error) when (error is not OutOfMemoryException) { /* A failed view cannot alter authority. */ }
        }
    }
    private static RoomAccessClientException Rejected(string code) => new(RoomAccessFailure.Rejected, code);
    private sealed record PendingCommand(FormalRoomOperation Action, Guid? RoomId, RoomEnterRequest? Enter,
        RoomControlRequest? Control, RoomLeaseEndRequest? End)
    { public Guid RequestId => Enter?.RequestId ?? Control?.RequestId ?? End!.RequestId; }
    private sealed class Transport(IFormalRoomConnection connection, long generation, Guid leaseId, Guid roomId,
        Action closed, Action<RoomChangeNotice> notice)
    {
        public IFormalRoomConnection Connection { get; } = connection;
        public long Generation { get; } = generation;
        public Guid LeaseId { get; } = leaseId;
        public Guid RoomId { get; } = roomId;
        public Action Closed { get; } = closed;
        public Action<RoomChangeNotice> Notice { get; } = notice;
        public CancellationTokenSource Lifetime { get; } = new();
        public bool HeartbeatStarted { get; set; }
        public bool Unusable { get; set; }
        public ITimer? DeadlineTimer { get; set; }
        public long DeadlineGeneration { get; set; }
    }
}
