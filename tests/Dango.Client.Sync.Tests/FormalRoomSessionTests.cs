using System.Text.Json;
using System.Threading.Channels;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class FormalRoomSessionTests
{
    [TestMethod]
    public async Task OpenWithoutOwnLeaseRequiresExplicitAdmissionAndNeverEnters()
    {
        var client = new Receiver();
        await using var session = client.Session();
        await session.OpenAsync();
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, session.Current.Phase);
        Assert.IsFalse(session.Current.ServerAttached);
        Assert.AreEqual(0, client.Enters.Count);
        Assert.AreEqual(0, client.Connections.Count);
    }

    [TestMethod]
    public async Task AdmissionReceiptCannotPublishAttachedBeforeSnapshotAndFinalAuthority()
    {
        var client = new Receiver();
        var snapshotStarted = Signal(); var release = Signal();
        client.ConfigureConnection = connection => connection.SnapshotHook = async () =>
        { snapshotStarted.TrySetResult(); await release.Task; return client.Shared(); };
        await using var session = client.Session();
        var enter = session.EnterAsync(client.RoomId);
        await snapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(session.Current.ServerAttached);
        Assert.IsFalse(session.Current.Presented);
        Assert.IsNull(session.Current.PresentationToken);
        release.TrySetResult(); await enter;
        Assert.IsTrue(session.Current.ServerAttached);
        Assert.AreEqual(FormalRoomSessionPhase.AwaitingPresentation, session.Current.Phase);
        Assert.IsFalse(session.Current.Presented);
        Assert.IsTrue(session.ConfirmPresentationReady(session.Current.PresentationToken!));
        Assert.IsTrue(session.Current.Presented);
        CollectionAssert.AreEqual(new[] { "start", "attach", "snapshot", "personal" }, client.Connections.Single().Calls);
    }

    [TestMethod]
    public async Task ResumeObserversDoesNotEnterOrTakeControl()
    {
        var client = new Receiver(); client.Assign(controls: false);
        await using var session = client.Session();
        await session.ResumeAsync();
        Assert.AreEqual(FormalRoomRole.Observe, session.Current.Role);
        Assert.AreEqual(0, client.Enters.Count);
        Assert.AreEqual(0, client.Controls.Count);
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.LeaveAsync());
        Assert.AreEqual(0, client.Leaves.Count);
        Assert.IsTrue(session.Current.ServerAttached);
        Assert.AreEqual(FormalRoomRole.Observe, session.Current.Role);
    }

    [TestMethod]
    public async Task UnknownEnterKeepsExactBodyAndHistoricalReplayAttachesCurrentRoom()
    {
        var client = new Receiver(); var first = true;
        client.EnterHook = (_, request) =>
        {
            if (first) { first = false; throw Unavailable(); }
            client.Assign(Guid.NewGuid());
            return client.Receipt("entered") with { RoomId = client.RoomId, LeaseId = Guid.NewGuid() };
        };
        await using var session = client.Session();
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.EnterAsync(client.RoomId, "fictional-code"));
        var pending = session.Current.Pending!;
        Assert.AreEqual(FormalRoomSessionPhase.Unknown, session.Current.Phase);
        Assert.AreEqual(1, client.Enters.Count);
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.EnterAsync(Guid.NewGuid()));
        Assert.AreEqual(pending, session.Current.Pending);
        await session.RetryPendingAsync();
        Assert.AreEqual(2, client.Enters.Count);
        Assert.AreSame(client.Enters[0].Request, client.Enters[1].Request);
        Assert.AreEqual("fictional-code", client.Enters[1].Request.Code);
        Assert.AreEqual(client.Personal.RoomId, session.Current.Shared!.RoomId);
        Assert.AreNotEqual(client.RoomId, session.Current.Shared.RoomId);
        Assert.IsNull(session.Current.Pending);
        Assert.IsFalse(session.Current.Presented);
    }

    [TestMethod]
    public async Task UnknownLeaveRetriesExactCasWithoutConstructingNewOperation()
    {
        var client = new Receiver(); client.Assign(); var first = true;
        client.LeaveHook = request =>
        {
            if (first) { first = false; throw Unavailable(); }
            client.Clear(); return client.Receipt("left");
        };
        await using var session = client.Session(); await session.ResumeAsync();
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.LeaveAsync());
        await session.RetryPendingAsync();
        Assert.AreSame(client.Leaves[0], client.Leaves[1]);
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, session.Current.Phase);
        Assert.IsNull(session.Current.Personal!.RoomId);
    }

    [TestMethod]
    public async Task SuspendStopsTransportWithoutLeavingOrEndingAndResumeKeepsInstanceLease()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        var token = session.Current.PresentationToken!; var lease = token.LeaseId;
        await session.SuspendAsync();
        Assert.AreEqual(FormalRoomSessionPhase.Suspended, session.Current.Phase);
        Assert.AreEqual(client.Personal.RoomId, session.Current.Personal!.RoomId);
        Assert.IsFalse(session.ConfirmPresentationReady(token));
        Assert.IsTrue(client.Connections[0].Stopped); Assert.IsTrue(client.Connections[0].Disposed);
        Assert.AreEqual(0, client.Leaves.Count); Assert.AreEqual(0, client.Ends.Count);
        await session.ResumeAsync();
        Assert.AreEqual(lease, session.Current.PresentationToken!.LeaseId);
        Assert.AreEqual(0, client.Enters.Count);
    }

    [TestMethod]
    public async Task ResetCancelsAndDrainsDelayedOldAccountResponse()
    {
        var client = new Receiver(); var started = Signal(); var release = Signal();
        client.PersonalHook = async () => { started.TrySetResult(); await release.Task; return client.Personal; };
        await using var session = client.Session();
        var read = session.OpenAsync(); await started.Task;
        var reset = session.ResetAsync();
        Assert.AreEqual(Guid.Empty, session.Current.UserId);
        Assert.AreEqual(FormalRoomSessionPhase.Reset, session.Current.Phase);
        release.TrySetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => read); await reset;
        Assert.IsNull(session.Current.Personal); Assert.IsNull(session.Current.Pending);
        Assert.AreEqual(0, client.Connections.Count);
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.OpenAsync());
    }

    [TestMethod]
    public async Task LateDisposedConnectionClosedCannotDestroyReplacementTransport()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        var oldClosed = client.Connections[0].CaptureClosed();
        await session.SuspendAsync(); await session.ResumeAsync();
        var token = session.Current.PresentationToken;
        oldClosed();
        Assert.IsTrue(session.Current.ServerAttached);
        Assert.AreSame(token, session.Current.PresentationToken);
        Assert.IsFalse(client.Connections[1].Stopped);
    }

    [TestMethod]
    public async Task RenderingFailureEndsOnlyCapturedLeaseAndUnknownCompensationKeepsKey()
    {
        var client = new Receiver(); client.Assign(); var first = true;
        client.EndHook = request =>
        {
            if (first) { first = false; throw Unavailable(); }
            client.Clear(); return client.Receipt("lease_ended");
        };
        await using var session = client.Session(); await session.ResumeAsync();
        var captured = session.Current.PresentationToken!;
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.FailPresentationAsync(captured));
        Assert.IsFalse(session.Current.Presented);
        Assert.AreEqual(captured.LeaseId, client.Ends.Single().LeaseId);
        Assert.AreEqual(client.InstanceId, client.Ends.Single().InstanceId);
        Assert.AreEqual(0, client.Leaves.Count); Assert.AreEqual(0, client.Enters.Count);
        await session.RetryPendingAsync();
        Assert.AreSame(client.Ends[0], client.Ends[1]);
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, session.Current.Phase);
    }

    [TestMethod]
    public async Task StalePresentationCannotConfirmOrCompensateNewLease()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        var old = session.Current.PresentationToken!;
        client.Assign(Guid.NewGuid()); await session.RefreshAsync();
        Assert.IsFalse(session.ConfirmPresentationReady(old));
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.FailPresentationAsync(old));
        Assert.AreEqual(0, client.Ends.Count); Assert.AreEqual(0, client.Leaves.Count);
        Assert.IsTrue(session.Current.ServerAttached, "A late old host failure must not stop the new transport.");
    }

    [TestMethod]
    public async Task CrossAccountPersonalResponseIsRejectedBeforeTransport()
    {
        var client = new Receiver(); client.Assign();
        client.Personal = client.Personal with { UserId = Guid.NewGuid() };
        await using var session = client.Session();
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.ResumeAsync());
        Assert.AreEqual(RoomAccessFailure.InvalidResponse, error.Failure);
        Assert.IsFalse(session.Current.ServerAttached); Assert.AreEqual(0, client.Connections.Count);
    }

    [TestMethod]
    public async Task FinalPersonalLeaseRevocationCannotPublishAttachedSnapshot()
    {
        var client = new Receiver(); client.Assign();
        client.ConfigureConnection = connection => connection.PersonalHook = () => { client.Clear(); return client.Personal; };
        await using var session = client.Session(); await session.ResumeAsync();
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, session.Current.Phase);
        Assert.IsFalse(session.Current.ServerAttached); Assert.IsNull(session.Current.PresentationToken);
    }

    [TestMethod]
    public async Task TransportConflictCannotAutomaticallyReattachAndStealTransport()
    {
        var client = new Receiver(); client.Assign();
        client.ConfigureConnection = connection => connection.SnapshotHook = () =>
            throw new RoomAccessClientException(RoomAccessFailure.Conflict, "room_transport_conflict");
        await using var session = client.Session();
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.ResumeAsync());
        Assert.AreEqual(FormalRoomSessionPhase.Conflict, session.Current.Phase);
        Assert.AreEqual(1, client.Connections.Count); Assert.AreEqual(0, client.Enters.Count);
        Assert.IsFalse(session.Current.ServerAttached);
        Assert.IsFalse(client.Clock.TryTimer(out _), "A transport conflict must not schedule a reconnect timer.");
    }

    [TestMethod]
    public async Task HeartbeatIsSingleLimitedAndDoesNotPromotePresentation()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        var timer = await client.Clock.TimerAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(TimeSpan.FromSeconds(10), timer.Due);
        Assert.AreEqual(TimeSpan.FromSeconds(10), timer.Period);
        Assert.AreEqual(0, client.Connections[0].Heartbeats);
        var updated = NextState(session, s => s.ServerAttached && client.Connections[0].Heartbeats == 1);
        timer.Fire(); await updated;
        Assert.AreEqual(1, client.Connections[0].Heartbeats);
        Assert.IsFalse(session.Current.Presented);
        await session.SuspendAsync(); timer.Fire();
        Assert.AreEqual(1, client.Connections[0].Heartbeats);
    }

    [TestMethod]
    public async Task PresentationConfirmationUsesServerLeaseDeadlineAndMonotonicElapsedTime()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        var token = session.Current.PresentationToken!;
        client.Clock.Milliseconds = 31000;
        Assert.IsFalse(session.ConfirmPresentationReady(token));
        Assert.IsFalse(session.Current.Presented);
        var expiry = await client.Clock.TimerAsync(TimeSpan.FromSeconds(30));
        expiry.Fire();
        Assert.IsFalse(session.Current.ServerAttached);
        Assert.AreEqual(FormalRoomRole.None, session.Current.Role);
    }

    [TestMethod]
    public async Task NotificationsOnlyRefreshCurrentRoomWhitelistedEventsAndCoalesceBurst()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        _ = await client.Clock.TimerAsync(TimeSpan.FromSeconds(10));
        _ = await client.Clock.TimerAsync(TimeSpan.FromSeconds(30));
        var connection = client.Connections.Single();
        connection.Notify(new(Guid.NewGuid(), Guid.NewGuid(), 4, "entered"));
        connection.Notify(new(Guid.NewGuid(), client.RoomId, 4, "granted_control"));
        Assert.IsFalse(client.Clock.TryTimer(out _));
        var personalReads = client.PersonalReads;
        for (var i = 0; i < 100; i++) connection.Notify(new(Guid.NewGuid(), client.RoomId, 4, "control"));
        var refreshTimer = await client.Clock.TimerAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(TimeSpan.FromSeconds(1), refreshTimer.Due);
        var updated = NextState(session, s => s.ServerAttached && client.PersonalReads > personalReads);
        refreshTimer.Fire(); await updated;
        Assert.AreEqual(personalReads + 1, client.PersonalReads);
        Assert.AreEqual(FormalRoomRole.Control, session.Current.Role);
        Assert.AreEqual(0, client.Controls.Count);
    }

    [TestMethod]
    public async Task DisconnectRecoversSameInstanceLeaseWithoutEnterOrTakeover()
    {
        var client = new Receiver(); client.Assign(controls: false);
        await using var session = client.Session(); await session.ResumeAsync();
        _ = await client.Clock.TimerAsync(TimeSpan.FromSeconds(10));
        var lease = session.Current.PresentationToken!.LeaseId;
        client.Connections.Single().Close();
        Assert.IsFalse(session.Current.ServerAttached);
        var reconnect = await client.Clock.TimerAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(TimeSpan.FromSeconds(1), reconnect.Due);
        var restored = NextState(session, s => s.ServerAttached);
        reconnect.Fire(); await restored;
        Assert.AreEqual(lease, session.Current.PresentationToken!.LeaseId);
        Assert.AreEqual(FormalRoomRole.Observe, session.Current.Role);
        Assert.AreEqual(0, client.Enters.Count); Assert.AreEqual(0, client.Controls.Count);
        Assert.AreEqual(2, client.Connections.Count);
    }

    [TestMethod]
    public async Task ExpiredLeaseOnReconnectRequiresUserAndDoesNotEnter()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        _ = await client.Clock.TimerAsync(TimeSpan.FromSeconds(10));
        client.Connections.Single().Close();
        var reconnect = await client.Clock.TimerAsync(TimeSpan.FromSeconds(1));
        client.Personal = client.Personal with { Leases = [], ControlLeaseId = null, IsSleeping = true };
        var restored = NextState(session, s => s.Phase == FormalRoomSessionPhase.ReentryRequired);
        reconnect.Fire(); await restored;
        Assert.IsFalse(session.Current.ServerAttached);
        Assert.AreEqual(0, client.Enters.Count);
    }

    [TestMethod]
    public async Task ClosedDuringAttachCannotPublishLateSnapshotAsAttached()
    {
        var client = new Receiver(); client.Assign();
        client.ConfigureConnection = connection => connection.AttachHook = () => connection.Close();
        await using var session = client.Session();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => session.ResumeAsync());
        Assert.IsFalse(session.Current.ServerAttached);
        Assert.IsNull(session.Current.PresentationToken);
        Assert.IsFalse(client.Connections.Single().Calls.Contains("snapshot"));
    }

    [TestMethod]
    public async Task LateOldDeadlineTimerCannotExpireRefreshedLease()
    {
        var client = new Receiver(); client.Assign();
        await using var session = client.Session(); await session.ResumeAsync();
        var heartbeat = await client.Clock.TimerAsync(TimeSpan.FromSeconds(10));
        var oldDeadline = await client.Clock.TimerAsync(TimeSpan.FromSeconds(30));
        var updated = NextState(session, s => s.ServerAttached && client.Connections[0].Heartbeats == 1);
        heartbeat.Fire(); await updated;
        var newDeadline = await client.Clock.TimerAsync(TimeSpan.FromSeconds(30));
        Assert.AreNotSame(oldDeadline, newDeadline);
        oldDeadline.FireEvenIfDisposed();
        Assert.IsTrue(session.Current.ServerAttached);
        Assert.AreEqual(FormalRoomRole.Control, session.Current.Role);
    }

    [TestMethod]
    public async Task SuspendPreservesUnknownBodyAndResetClearsIt()
    {
        var client = new Receiver(); client.EnterHook = (_, _) => throw Unavailable();
        await using var session = client.Session();
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.EnterAsync(client.RoomId));
        var pending = session.Current.Pending;
        await session.SuspendAsync();
        Assert.AreEqual(pending, session.Current.Pending);
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.RetryPendingAsync());
        Assert.AreSame(client.Enters[0].Request, client.Enters[1].Request);
        await session.ResetAsync();
        Assert.IsNull(session.Current.Pending);
        Assert.AreEqual(Guid.Empty, session.Current.UserId);
    }

    [TestMethod]
    public async Task KnownReceiptWithFailedCurrentReadRequiresReconciliationWithoutFreshEnter()
    {
        var client = new Receiver();
        client.EnterHook = (room, _) =>
        {
            client.Assign(room); client.PersonalHook = () => throw Unavailable();
            return client.Receipt("entered");
        };
        await using var session = client.Session();
        await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.EnterAsync(client.RoomId));
        Assert.IsNull(session.Current.Pending);
        Assert.IsTrue(session.Current.RequiresReconciliation);
        Assert.AreEqual(FormalRoomSessionPhase.ReconciliationRequired, session.Current.Phase);
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => session.EnterAsync(Guid.NewGuid()));
        Assert.AreEqual("room_reconciliation_required", error.Code);
        Assert.AreEqual(1, client.Enters.Count);
        client.PersonalHook = null;
        await session.RefreshAsync();
        Assert.IsFalse(session.Current.RequiresReconciliation);
        Assert.IsTrue(session.Current.ServerAttached);
        Assert.AreEqual(1, client.Enters.Count);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static RoomAccessClientException Unavailable() => new(RoomAccessFailure.Unavailable, "service_unavailable");
    private static Task NextState(FormalRoomSession session, Func<FormalRoomSessionState, bool> predicate)
    {
        var signal = Signal();
        void Handler(FormalRoomSessionState state)
        { if (predicate(state)) { session.Changed -= Handler; signal.TrySetResult(); } }
        session.Changed += Handler;
        return signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Receiver : IRoomAccessClient
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public Uri Origin => new("https://formal.example.invalid/");
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        public Guid InstanceId { get; } = Guid.NewGuid();
        public Guid RoomId { get; } = Guid.NewGuid();
        public Clock Clock { get; } = new();
        public PersonalRoomSnapshot Personal { get; set; }
        public List<(Guid RoomId, RoomEnterRequest Request)> Enters { get; } = [];
        public List<RoomControlRequest> Controls { get; } = [];
        public List<RoomControlRequest> Leaves { get; } = [];
        public List<RoomLeaseEndRequest> Ends { get; } = [];
        public List<Connection> Connections { get; } = [];
        public Func<Task<PersonalRoomSnapshot>>? PersonalHook { get; set; }
        public Func<Guid, RoomEnterRequest, RoomAccessResult>? EnterHook { get; set; }
        public Func<RoomControlRequest, RoomAccessResult>? LeaveHook { get; set; }
        public Func<RoomLeaseEndRequest, RoomAccessResult>? EndHook { get; set; }
        public Action<Connection>? ConfigureConnection { get; set; }
        public int PersonalReads { get; private set; }
        public Receiver() => Personal = new(1, UserId, PetId, null, null, 1, 0, null, false, [], Now);
        public FormalRoomSession Session() => new(this, InstanceId, Clock);
        public void Assign(Guid? room = null, bool controls = true)
        {
            var lease = Guid.NewGuid();
            Personal = new(1, UserId, PetId, room ?? RoomId, "privateShared", Personal.AssignmentVersion + 1,
                Personal.ControlGeneration + 1, controls ? lease : Guid.NewGuid(), false,
                [new(lease, InstanceId, "desktop", controls, Now.AddSeconds(30))], Now);
        }
        public void Clear() => Personal = Personal with { RoomId = null, RoomKind = null, Leases = [], ControlLeaseId = null,
            AssignmentVersion = Personal.AssignmentVersion + 1, ControlGeneration = Personal.ControlGeneration + 1 };
        public SharedRoomSnapshot Shared() => new(1, Personal.RoomId!.Value, Personal.AssignmentVersion, "privateShared",
            "fictional room", 6, "ordinary-default", 1,
            [new(PetId, UserId, 1, JsonSerializer.SerializeToElement(new { schemaVersion = 3 }), 0, 0, false, Personal.AssignmentVersion)], Now);
        public RoomAccessResult Receipt(string operation) => new(PetId, Personal.RoomId, Personal.Leases.FirstOrDefault()?.LeaseId,
            Personal.AssignmentVersion, Personal.ControlGeneration, 1, operation, Now, Now.AddSeconds(30));
        public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct)
        { PersonalReads++; return PersonalHook?.Invoke() ?? Task.FromResult(Personal); }
        public Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct) => Task.FromResult(Shared());
        public Task<RoomAccessResult> EnterAsync(Guid roomId, RoomEnterRequest request, CancellationToken ct)
        { Enters.Add((roomId, request)); if (EnterHook is not null) return Task.FromResult(EnterHook(roomId, request)); Assign(roomId); return Task.FromResult(Receipt("entered")); }
        public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct)
        { Controls.Add(request); Personal = Personal with { ControlLeaseId = request.LeaseId, AssignmentVersion = Personal.AssignmentVersion + 1,
            ControlGeneration = Personal.ControlGeneration + 1, Leases = Personal.Leases.Select(l => l with { Controls = l.LeaseId == request.LeaseId }).ToArray() }; return Task.FromResult(Receipt("control")); }
        public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct)
        { Leaves.Add(request); if (LeaveHook is not null) return Task.FromResult(LeaveHook(request)); Clear(); return Task.FromResult(Receipt("left")); }
        public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct)
        { Ends.Add(request); if (EndHook is not null) return Task.FromResult(EndHook(request)); Clear(); return Task.FromResult(Receipt("lease_ended")); }
        public Task<RoomDoorView> ResolveAsync(RoomCodeResolveRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RoomCodeResult> ResetCodeAsync(Guid roomId, RoomCodeResetRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IFormalRoomConnection CreateConnection()
        { var connection = new Connection(this); ConfigureConnection?.Invoke(connection); Connections.Add(connection); return connection; }
    }

    private sealed class Connection(Receiver client) : IFormalRoomConnection
    {
        public event Action<RoomChangeNotice>? RoomChanged;
        public event Action? Closed;
        public List<string> Calls { get; } = [];
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public int Heartbeats { get; private set; }
        public Func<Task<SharedRoomSnapshot>>? SnapshotHook { get; set; }
        public Action? AttachHook { get; set; }
        public Func<PersonalRoomSnapshot>? PersonalHook { get; set; }
        public Task StartAsync(CancellationToken ct) { Calls.Add("start"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public Task<RoomAccessResult> AttachAsync(RoomLeaseRequest request, CancellationToken ct)
        { Calls.Add("attach"); Assert.AreEqual(client.InstanceId, request.InstanceId); Assert.AreEqual(client.Personal.Leases.Single().LeaseId, request.LeaseId); AttachHook?.Invoke(); return Task.FromResult(client.Receipt("attached")); }
        public Task<RoomAccessResult> HeartbeatAsync(RoomLeaseRequest request, CancellationToken ct)
        { Heartbeats++; return Task.FromResult(client.Receipt("heartbeat")); }
        public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct)
        { Calls.Add("personal"); return Task.FromResult(PersonalHook?.Invoke() ?? client.Personal); }
        public Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct)
        { Calls.Add("snapshot"); return SnapshotHook?.Invoke() ?? Task.FromResult(client.Shared()); }
        public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Action CaptureClosed() => Closed ?? (() => { });
        public void Close() => Closed?.Invoke();
        public void Notify(RoomChangeNotice notice) => RoomChanged?.Invoke(notice);
    }

    private sealed class Clock : TimeProvider
    {
        private readonly Channel<Timer> timers = Channel.CreateUnbounded<Timer>();
        public long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Use server time with monotonic elapsed time.");
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(callback, state, dueTime, period); timers.Writer.TryWrite(timer); return timer; }
        public async Task<Timer> TimerAsync(TimeSpan due)
        {
            while (true)
            {
                var timer = await timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                if (timer.Due == due) return timer;
            }
        }
        public bool TryTimer(out Timer? timer) => timers.Reader.TryRead(out timer);
    }

    private sealed class Timer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
    {
        private bool disposed;
        public TimeSpan Due { get; private set; } = due;
        public TimeSpan Period { get; private set; } = period;
        public bool Change(TimeSpan dueTime, TimeSpan nextPeriod) { Due = dueTime; Period = nextPeriod; return !disposed; }
        public void Fire() { if (!disposed) callback(state); }
        public void FireEvenIfDisposed() => callback(state);
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
