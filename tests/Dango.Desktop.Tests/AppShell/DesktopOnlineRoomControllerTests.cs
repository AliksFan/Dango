using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Client.Sync.Rooms;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopOnlineRoomControllerTests
{
    [STATestMethod]
    public void OpenAndCloseOnlyReadOrSuspendAndNeverEnterLeaveOrEndLease()
    {
        WithDispatcher(() =>
        {
            var client = new Client(); var view = new OnlineRoomPanelView(); var closed = 0;
            using var controller = Controller(view, () => client, () => closed++);
            controller.Open(Room()); PumpUntil(() => !controller.CurrentState.Busy);
            Assert.AreEqual(1, client.PersonalReads); Assert.AreEqual(0, client.EnterRequests.Count);
            controller.Close(); Assert.AreEqual(1, closed);
            controller.Open(); PumpUntil(() => !controller.CurrentState.Busy);
            Assert.AreEqual(0, client.EnterRequests.Count); Assert.AreEqual(0, client.Leaves); Assert.AreEqual(0, client.Ends);
            Assert.IsFalse(controller.CurrentState.Connected);
        });
    }

    [STATestMethod]
    public void UnknownAdmissionSurvivesCloseAndOnlyExplicitRetryReusesExactRequest()
    {
        WithDispatcher(() =>
        {
            var client = new Client { LoseEnterOnce = true }; var view = new OnlineRoomPanelView(); var room = Room();
            using var controller = Controller(view, () => client);
            controller.Open(room); PumpUntil(() => !controller.CurrentState.Busy);
            Run(controller.HandleAsync(new(OnlineRoomAction.OpenEnter, room.RoomId)));
            Run(controller.HandleAsync(new(OnlineRoomAction.Enter, room.RoomId)));
            Assert.IsTrue(controller.CurrentState.Pending); Assert.AreEqual(1, client.EnterRequests.Count);
            controller.Close(); controller.Open(); PumpUntil(() => !controller.CurrentState.Busy);
            Assert.AreEqual(1, client.EnterRequests.Count, "Reopening must not automatically retry or submit a fresh admission.");
            Run(controller.HandleAsync(new(OnlineRoomAction.Control)));
            Assert.AreEqual(0, client.Controls);
            Run(controller.HandleAsync(new(OnlineRoomAction.RetryPending)));
            Assert.AreEqual(2, client.EnterRequests.Count); Assert.AreEqual(client.EnterRequests[0], client.EnterRequests[1]);
            Assert.IsFalse(controller.CurrentState.Pending); Assert.IsTrue(controller.CurrentState.Connected);
            Assert.AreEqual(OnlineRoomPage.Status, controller.CurrentState.Page);
            Assert.IsNull(view.FindName("OnlineRoomEnter"));
            Run(controller.HandleAsync(new(OnlineRoomAction.Enter, room.RoomId)));
            Assert.AreEqual(2, client.EnterRequests.Count, "A consumed confirmation cannot issue another admission after reconciliation.");
            Assert.IsNull(view.FindName("OnlineRoomLeave"));
            controller.Close(); controller.Open(); PumpUntil(() => !controller.CurrentState.Busy);
            Assert.AreEqual(2, client.EnterRequests.Count, "Closing a connected panel must resume the lease without fresh Enter.");
            Assert.AreEqual(0, client.Leaves); Assert.AreEqual(0, client.Ends);
        });
    }

    [STATestMethod]
    public void RoomRejectionRetainsAccountButOnlyRealAuthenticationFailureClearsBinding()
    {
        WithDispatcher(() =>
        {
            var client = new Client { PersonalError = new(RoomAccessFailure.Rejected, "room_access_forbidden") };
            var view = new OnlineRoomPanelView(); var rejected = 0;
            using var controller = Controller(view, () => client, authenticationLost: _ => rejected++);
            controller.Open(); PumpUntil(() => !controller.CurrentState.Busy);
            Assert.AreEqual(0, rejected);
            client.PersonalError = new(RoomAccessFailure.Authentication, "request_protection_invalid");
            Run(controller.HandleAsync(new(OnlineRoomAction.Refresh))); Assert.AreEqual(0, rejected);
            client.PersonalError = new(RoomAccessFailure.Authentication, "authentication_required");
            Run(controller.HandleAsync(new(OnlineRoomAction.Refresh))); Assert.AreEqual(1, rejected);
        });
    }

    [STATestMethod]
    public void DelayedOldReadCannotPublishOrInvalidateNewAccountAfterBindingChange()
    {
        WithDispatcher(() =>
        {
            var old = new Client { DelayRead = true }; var next = new Client(); Client active = old;
            var view = new OnlineRoomPanelView(); var rejected = 0;
            using var controller = Controller(view, () => active, authenticationLost: _ => rejected++);
            controller.Open(); Assert.IsTrue(old.ReadStarted);
            active = next; controller.SynchronizeBinding(); controller.Open();
            old.Delayed.TrySetException(new RoomAccessClientException(RoomAccessFailure.Authentication, "authentication_required"));
            PumpUntil(() => !controller.CurrentState.Busy && controller.CurrentState.Personal?.UserId == next.UserId);
            Assert.AreEqual(0, rejected); Assert.AreEqual(next.UserId, controller.CurrentState.Personal!.UserId);
            Assert.AreEqual(0, next.EnterRequests.Count);
        });
    }

    [STATestMethod]
    public void UnknownCodeResetPreservesExactBodyWithoutAutomaticReplayAndHidesSecretOnClose()
    {
        WithDispatcher(() =>
        {
            var client = new Client { LoseCodeOnce = true }; var view = new OnlineRoomPanelView(); var room = Room();
            using var controller = Controller(view, () => client);
            controller.Open(room); PumpUntil(() => !controller.CurrentState.Busy);
            Run(controller.HandleAsync(new(OnlineRoomAction.OpenResetCode, room.RoomId)));
            Run(controller.HandleAsync(new(OnlineRoomAction.ResetCode, room.RoomId)));
            Assert.IsTrue(controller.CurrentState.Pending);
            controller.Close(); controller.Open(); PumpUntil(() => !controller.CurrentState.Busy);
            Assert.AreEqual(1, client.CodeRequests.Count);
            Run(controller.HandleAsync(new(OnlineRoomAction.RetryPending)));
            Assert.AreEqual(2, client.CodeRequests.Count); Assert.AreEqual(client.CodeRequests[0], client.CodeRequests[1]);
            Assert.IsFalse(controller.CurrentState.Pending); Assert.IsNull(controller.CurrentState.Target);
            Assert.AreEqual(43, ((TextBox)view.FindName("OnlineRoomCodeOutput")).Text.Length);
            controller.Close(); Assert.AreEqual(string.Empty, ((TextBox)view.FindName("OnlineRoomCodeOutput")).Text);
        });
    }

    private static DesktopOnlineRoomController Controller(OnlineRoomPanelView view, Func<IRoomAccessClient?> binding,
        Action? close = null, Action<IRoomAccessClient>? authenticationLost = null) =>
        new(view, binding, () => true, close ?? (() => { }), () => { }, () => { }, authenticationLost ?? (_ => { }));
    private static RoomManagementView Room() => new(Guid.NewGuid(), "publicShared", "虚构测试房间", 6,
        "active", 1, 1, 0, null, false, "shared-default", 1);
    private static void WithDispatcher(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void Run(Task operation) { PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Bounded dispatcher transition did not finish.");
    }

    internal sealed class Client : IRoomAccessClient
    {
        public Uri Origin => new("https://formal.dango.invalid/");
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        internal int PersonalReads, Controls, Leaves, Ends;
        internal bool LoseEnterOnce, LoseCodeOnce, DelayRead, ReadStarted;
        internal RoomAccessClientException? PersonalError;
        internal TaskCompletionSource<PersonalRoomSnapshot> Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<RoomEnterRequest> EnterRequests = [];
        internal List<RoomCodeResetRequest> CodeRequests = [];
        private PersonalRoomSnapshot? personal;
        public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct)
        {
            PersonalReads++; ReadStarted = true;
            if (DelayRead) return Delayed.Task; // Deliberately ignores cancellation to prove the binding fence.
            if (PersonalError is { } error) return Task.FromException<PersonalRoomSnapshot>(error);
            return Task.FromResult(personal ??= new(1, UserId, PetId, null, null, 1, 0, null, false, [], DateTimeOffset.UtcNow));
        }
        public Task<RoomAccessResult> EnterAsync(Guid roomId, RoomEnterRequest request, CancellationToken ct)
        {
            EnterRequests.Add(request);
            var now = DateTimeOffset.UtcNow;
            var lease = personal?.ControlLeaseId ?? Guid.NewGuid();
            personal = new(1, UserId, PetId, roomId, "publicShared", 2, 1, lease, false,
                [new(lease, request.InstanceId, "desktop", true, now.AddSeconds(30))], now);
            if (LoseEnterOnce) { LoseEnterOnce = false; throw new RoomAccessClientException(RoomAccessFailure.Unavailable, "response_lost"); }
            return Task.FromResult(Result("entered"));
        }
        private RoomAccessResult Result(string operation) => new(PetId, personal?.RoomId, personal?.ControlLeaseId,
            personal?.AssignmentVersion ?? 0, personal?.ControlGeneration ?? 0, 1, operation, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30));
        public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) { Controls++; return Task.FromResult(Result("control")); }
        public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) { Leaves++; return Task.FromResult(Result("left")); }
        public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) { Ends++; return Task.FromResult(Result("lease_ended")); }
        internal int Resolves;
        public Task<RoomDoorView> ResolveAsync(RoomCodeResolveRequest request, CancellationToken ct)
        { Resolves++; return Task.FromResult(new RoomDoorView(Guid.NewGuid(), "publicShared", "虚构访问房", 6, 1, "shared-default", 1)); }
        public Task<RoomCodeResult> ResetCodeAsync(Guid roomId, RoomCodeResetRequest request, CancellationToken ct)
        {
            CodeRequests.Add(request);
            if (LoseCodeOnce) { LoseCodeOnce = false; throw new RoomAccessClientException(RoomAccessFailure.Unavailable, "response_lost"); }
            return Task.FromResult(new RoomCodeResult(roomId, Guid.NewGuid(), 2, 1, new string('A', 43), DateTimeOffset.UtcNow.AddHours(24)));
        }
        public Task<SharedRoomSnapshot> SnapshotAsync(Guid roomId, RoomLeaseRequest request, CancellationToken ct)
        {
            var configuration = JsonSerializer.SerializeToElement(new
            { appearanceSchemaVersion = 3, bodyColorHex = "#FFFFFF", eyeColorHex = "#272321", eyeSizePercent = 100,
                eyeOvalityPercent = 0, eyeHeightControlPercent = 51, eyeSpacingControlPercent = 24.5 });
            return Task.FromResult(new SharedRoomSnapshot(1, roomId, 1, "publicShared", "虚构测试房间", 6,
                "shared-default", 1, [new(PetId, UserId, 1, configuration, 0, 0, false, personal?.AssignmentVersion ?? 1)], DateTimeOffset.UtcNow));
        }
        public IFormalRoomConnection CreateConnection() => new Connection(this);
        private sealed class Connection(Client owner) : IFormalRoomConnection
        {
            public event Action<RoomChangeNotice>? RoomChanged { add { } remove { } }
            public event Action? Closed { add { } remove { } }
            public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
            public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task<RoomAccessResult> AttachAsync(RoomLeaseRequest request, CancellationToken ct) => Task.FromResult(owner.Result("attached"));
            public Task<RoomAccessResult> HeartbeatAsync(RoomLeaseRequest request, CancellationToken ct) => Task.FromResult(owner.Result("heartbeat"));
            public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct) => owner.PersonalAsync(ct);
            public Task<SharedRoomSnapshot> SnapshotAsync(Guid id, RoomLeaseRequest request, CancellationToken ct) => owner.SnapshotAsync(id, request, ct);
            public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) => owner.ControlAsync(request, ct);
            public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) => owner.LeaveAsync(request, ct);
            public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) => owner.EndLeaseAsync(request, ct);
        }
    }
}
