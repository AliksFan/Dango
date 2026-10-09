using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Client.Sync.Rooms;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomIntegrationBoundaryTests
{
    [STATestMethod]
    public void UnknownCreateSurvivesCloseAndUsesSameBodyAndKeyOnlyAfterExplicitRetry() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote { LoseWrite = true };
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.OpenCreate); Form(view, "第一间", "6");
        Send(controller, RoomPanelAction.Create);
        Assert.AreEqual(1, remote.Creates.Count);
        var original = remote.Creates.Single();
        Assert.AreNotEqual(Guid.Empty, original.RequestId);
        StringAssert.Contains(Message(view), "未知");
        controller.Close(); controller.Open();
        Assert.AreEqual(1, remote.Creates.Count, "Reopening cannot automatically repeat a mutation.");
        Send(controller, RoomPanelAction.OpenCreate); Send(controller, RoomPanelAction.Create);
        Assert.AreEqual(1, remote.Creates.Count, "An unknown attempt must block fresh requests.");
        remote.LoseWrite = false;
        Send(controller, RoomPanelAction.RetryPending);
        Assert.AreEqual(2, remote.Creates.Count);
        Assert.AreSame(original, remote.Creates[1], "Retry must preserve the exact immutable request.");
        Assert.AreEqual(remote.Room, Selected(controller));
    });

    [STATestMethod]
    public void ReceiptFollowedByLostDetailRetriesReadWithoutRepeatingMutation() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote { LoseRead = true };
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.OpenCreate); Form(view, "回执", "6");
        Send(controller, RoomPanelAction.Create);
        Assert.AreEqual(1, remote.Creates.Count);
        StringAssert.Contains(Message(view), "回执");
        remote.LoseRead = false;
        Send(controller, RoomPanelAction.RetryPending);
        Assert.AreEqual(1, remote.Creates.Count);
        Assert.AreEqual(2, remote.Reads.Count);
        Assert.AreEqual(remote.Room, Selected(controller));
    });

    [STATestMethod]
    public void ConfirmedHistoricalReceiptWithMissingCurrentRoomDoesNotTrapDirectory() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote { ReadMissing = true };
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.OpenCreate); Form(view, "历史房间", "6");
        Send(controller, RoomPanelAction.Create);
        Assert.AreEqual(RoomPanelPage.Directory, controller.CurrentState.Page);
        StringAssert.Contains(Message(view), "历史操作已经确认");
        Send(controller, RoomPanelAction.Refresh);
        Assert.AreEqual(2, remote.Lists.Count);
        Assert.AreEqual(1, remote.Creates.Count);
        Send(controller, RoomPanelAction.RetryPending);
        Assert.AreEqual(1, remote.Creates.Count);
    });

    [STATestMethod]
    public void ConflictCannotResubmitUntilCurrentDetailIsReadAndUserChoosesAgain() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote();
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.SelectRoom, remote.Room.RoomId);
        Send(controller, RoomPanelAction.OpenEdit); Form(view, "过期版本", "5");
        remote.Conflict = true; Send(controller, RoomPanelAction.Update);
        Assert.AreEqual(1, remote.Updates.Count);
        Send(controller, RoomPanelAction.Update); Send(controller, RoomPanelAction.OpenEdit);
        Assert.AreEqual(1, remote.Updates.Count);
        remote.Room = remote.Room with { Version = 7, Name = "另一客户端修改" }; remote.Conflict = false;
        Send(controller, RoomPanelAction.RefreshConflict);
        Assert.AreEqual(7L, Selected(controller).Version);
        Send(controller, RoomPanelAction.OpenEdit); Form(view, "明确决定", "6"); Send(controller, RoomPanelAction.Update);
        Assert.AreEqual(7L, remote.Updates[1].ExpectedVersion);
        Assert.AreNotEqual(remote.Updates[0].RequestId, remote.Updates[1].RequestId);
    });

    [STATestMethod]
    public void DissolveNeedsDetailThenSecondConfirmationAndDesktopRoomCannotDissolve() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote();
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.Dissolve);
        Send(controller, RoomPanelAction.SelectRoom, remote.Room.RoomId); Send(controller, RoomPanelAction.Dissolve);
        Assert.AreEqual(0, remote.Dissolves.Count);
        Send(controller, RoomPanelAction.OpenDissolve);
        Assert.AreEqual(0, remote.Dissolves.Count);
        Send(controller, RoomPanelAction.Back);
        Assert.AreEqual(RoomPanelPage.Detail, controller.CurrentState.Page);
        Assert.AreEqual(0, remote.Dissolves.Count);
        Send(controller, RoomPanelAction.OpenDissolve);
        Send(controller, RoomPanelAction.Dissolve);
        Assert.AreEqual(1, remote.Dissolves.Count);
        Assert.IsTrue(remote.Dissolves[0].Confirmed);
        Assert.AreEqual(1L, remote.Dissolves[0].ExpectedVersion);
        remote.Room = remote.Room with { Kind = "desktopPet", Capacity = 2, Status = "active", RecoverUntil = null, Recoverable = false };
        Send(controller, RoomPanelAction.Back); Send(controller, RoomPanelAction.SelectRoom, remote.Room.RoomId);
        Send(controller, RoomPanelAction.OpenDissolve); Send(controller, RoomPanelAction.Dissolve);
        Assert.AreEqual(1, remote.Dissolves.Count);
    });

    [STATestMethod]
    public void RestoreUsesReadVersionAndCannotUseAnExpiredRecovery() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote();
        remote.Room = remote.Room with { Status = "dissolved", Version = 9, Recoverable = false, RecoverUntil = DateTimeOffset.UtcNow.AddDays(-1) };
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.SelectRoom, remote.Room.RoomId); Send(controller, RoomPanelAction.Restore);
        Assert.AreEqual(0, remote.Restores.Count);
        remote.Room = remote.Room with { Recoverable = true, RecoverUntil = DateTimeOffset.UtcNow.AddDays(1) };
        Send(controller, RoomPanelAction.Back); Send(controller, RoomPanelAction.SelectRoom, remote.Room.RoomId);
        Send(controller, RoomPanelAction.Restore);
        Assert.AreEqual(9L, remote.Restores.Single().ExpectedVersion);
        Assert.AreEqual("active", Selected(controller).Status);
    });

    [STATestMethod]
    public void DirectoryUsesOneBoundedPageAndNextCursor() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote { Next = Guid.NewGuid() };
        using var controller = Controller(view, () => remote);
        controller.Open(); Assert.AreEqual(1, remote.Lists.Count);
        Assert.AreEqual(("active", (Guid?)null, 20), remote.Lists[0]);
        Send(controller, RoomPanelAction.NextPage);
        Assert.AreEqual(("active", remote.Next, 20), remote.Lists[1]);
        Send(controller, RoomPanelAction.DissolvedDirectory);
        Assert.AreEqual(("dissolved", (Guid?)null, 20), remote.Lists[2]);
    });

    [STATestMethod]
    public void CancelledOldMutationCannotPublishIntoAnotherIdentity() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var old = new Remote { DelayWrite = true }; var next = new Remote();
        Remote bound = old;
        using var controller = Controller(view, () => bound);
        controller.Open(); Send(controller, RoomPanelAction.OpenCreate); Form(view, "旧账号", "6");
        var operation = controller.HandleAsync(new(RoomPanelAction.Create));
        Assert.AreEqual(1, old.Creates.Count);
        controller.Reset(); bound = next; controller.Open();
        old.Delayed.SetResult(new(old.Room.RoomId, 1, "created"));
        PumpUntil(() => operation.IsCompleted && next.Lists.Count == 1);
        operation.GetAwaiter().GetResult();
        Assert.AreEqual(0, old.Reads.Count);
        Assert.AreEqual(0, next.Creates.Count);
        Send(controller, RoomPanelAction.SelectRoom, next.Room.RoomId);
        Assert.AreEqual(next.Room.RoomId, Selected(controller).RoomId);
    });

    [STATestMethod]
    public void ClosingInFlightWritePreservesLateReceiptAndReopenOnlyReadsCurrentDetail() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote { DelayWrite = true };
        using var controller = Controller(view, () => remote);
        controller.Open(); Send(controller, RoomPanelAction.OpenCreate); Form(view, "关窗时提交", "6");
        var old = controller.HandleAsync(new(RoomPanelAction.Create));
        Assert.AreEqual(1, remote.Creates.Count);
        controller.Close(); controller.Open();
        Assert.AreEqual(RoomPanelPage.Pending, controller.CurrentState.Page);
        remote.Delayed.SetResult(new(remote.Room.RoomId, 1, "created"));
        PumpUntil(() => old.IsCompleted); old.GetAwaiter().GetResult();
        Assert.AreEqual(0, remote.Reads.Count);
        Assert.AreEqual(RoomPanelPage.Pending, controller.CurrentState.Page);
        Send(controller, RoomPanelAction.RetryPending);
        Assert.AreEqual(1, remote.Creates.Count);
        Assert.AreEqual(1, remote.Reads.Count);
        Assert.AreEqual(RoomPanelPage.Detail, controller.CurrentState.Page);
    });

    [STATestMethod]
    public void AuthenticationFailureClearsBindingAndRequiresAccountRoute() => WithDispatcher(() =>
    {
        var view = new RoomPanelView(); var remote = new Remote { Unauthorized = true }; var lost = 0; var account = 0;
        IRoomManagementClient? bound = remote;
        using var controller = new DesktopRoomController(view, () => bound, () => { }, () => account++, rejected =>
        { Assert.AreSame(remote, rejected); lost++; bound = null; });
        controller.Open(); Assert.AreEqual(1, lost);
        Send(controller, RoomPanelAction.OpenCreate); Assert.AreEqual(0, remote.Creates.Count);
        controller.Open(); Assert.AreEqual(1, account);
    });

    [STATestMethod]
    [DataRow(1024, 640)]
    [DataRow(336, 360)]
    public void CenteredRoomOverlaySwitchesFromAccountClearsSecretsAndKeepsBackgroundDisabled(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.ShowAccountPanel();
        ((PasswordBox)home.AccountPanel.FindName("AccountPassword")).Password = "FictionalOnly123!";
        home.ShowRoomPanel();
        home.RoomPanel.Apply(new(RoomPanelPage.Create));
        home.Measure(new Size(width, height)); home.Arrange(new Rect(0, 0, width, height)); home.UpdateLayout();
        Assert.IsTrue(home.IsAccountPanelOpen);
        Assert.IsTrue(home.IsRoomPanelOpen);
        Assert.AreEqual("", home.AccountPanel.Password);
        Assert.AreEqual(Visibility.Collapsed, home.AccountPanel.Visibility);
        Assert.IsFalse(((ScrollViewer)home.FindName("HomeContent")).IsEnabled);
        var bounds = home.RoomPanel.TransformToAncestor(home).TransformBounds(new Rect(home.RoomPanel.RenderSize));
        Assert.AreEqual(width / 2d, bounds.X + bounds.Width / 2, 1);
        Assert.IsTrue(bounds.Top >= 0 && bounds.Bottom <= height);
        Assert.AreEqual(0d, ((ScrollViewer)home.RoomPanel.FindName("RoomScroll")).ScrollableWidth);
        ((Button)home.RoomPanel.FindName("RoomClose")).RaiseEvent(new(Button.ClickEvent));
        Assert.IsFalse(home.IsAccountPanelOpen);
        Assert.IsTrue(((ScrollViewer)home.FindName("HomeContent")).IsEnabled);
        home.ShowRoomPanel(); home.ShowAccountPanel();
        Assert.IsTrue(home.IsAccountPanelOpen);
        Assert.IsFalse(home.IsRoomPanelOpen);
        home.HideAccountPanel();
    }

    private static DesktopRoomController Controller(RoomPanelView panel, Func<IRoomManagementClient?> binding) =>
        new(panel, binding, () => { }, () => { }, _ => throw new AssertFailedException());
    private static void Send(DesktopRoomController controller, RoomPanelAction action, Guid? room = null)
    { var task = controller.HandleAsync(new(action, room)); PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Form(RoomPanelView view, string name, string capacity)
    { ((TextBox)view.FindName("RoomName")).Text = name; ((TextBox)view.FindName("RoomCapacity")).Text = capacity; }
    private static string Message(RoomPanelView view) => ((TextBlock)view.FindName("RoomMessage")).Text;
    private static RoomManagementView Selected(DesktopRoomController controller) =>
        controller.CurrentState.Selected!;
    private static void WithDispatcher(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Bounded UI operation did not complete.");
    }
    private sealed class Remote : IRoomManagementClient
    {
        public Uri Origin { get; } = new("https://formal.dango.invalid/");
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        internal RoomManagementView Room = new(Guid.NewGuid(), "privateShared", "虚构房间", 6, "active", 1, 1, 0, null, false, "ordinary-default", 1);
        internal Guid? Next;
        internal bool LoseWrite, LoseRead, ReadMissing, Conflict, DelayWrite, Unauthorized;
        internal readonly TaskCompletionSource<RoomOperationResult> Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<(string, Guid?, int)> Lists = [];
        internal readonly List<Guid> Reads = [];
        internal readonly List<RoomCreateRequest> Creates = [];
        internal readonly List<RoomUpdateRequest> Updates = [];
        internal readonly List<RoomDissolveRequest> Dissolves = [];
        internal readonly List<RoomRestoreRequest> Restores = [];
        public Task<RoomManagementPage> ListAsync(string state, Guid? after, int limit, CancellationToken ct)
        {
            Lists.Add((state, after, limit));
            if (Unauthorized) throw new RoomManagementClientException(RoomManagementFailure.Authentication, "authentication_required");
            return Task.FromResult(new RoomManagementPage([Room], Next));
        }
        public Task<RoomManagementView> ReadAsync(Guid roomId, CancellationToken ct)
        {
            Reads.Add(roomId);
            if (ReadMissing) throw new RoomManagementClientException(RoomManagementFailure.Rejected, "room_not_found");
            if (LoseRead) throw new RoomManagementClientException(RoomManagementFailure.Unavailable, "request_outcome_unknown");
            return Task.FromResult(Room);
        }
        public async Task<RoomOperationResult> CreateAsync(RoomCreateRequest request, CancellationToken ct)
        {
            Creates.Add(request);
            if (LoseWrite) throw new RoomManagementClientException(RoomManagementFailure.Unavailable, "request_outcome_unknown");
            if (DelayWrite) return await Delayed.Task;
            Room = Room with { Name = request.Name, Capacity = request.Capacity, Kind = request.Kind };
            return new(Room.RoomId, 1, "created");
        }
        public Task<RoomOperationResult> UpdateAsync(Guid roomId, RoomUpdateRequest request, CancellationToken ct)
        {
            Updates.Add(request);
            if (Conflict) throw new RoomManagementClientException(RoomManagementFailure.Conflict, "room_version_conflict");
            Room = Room with { Name = request.Name, Capacity = request.Capacity, Version = request.ExpectedVersion + 1 };
            return Task.FromResult(new RoomOperationResult(roomId, Room.Version, "updated"));
        }
        public Task<RoomOperationResult> DissolveAsync(Guid roomId, RoomDissolveRequest request, CancellationToken ct)
        {
            Dissolves.Add(request); Room = Room with { Status = "dissolved", Version = Room.Version + 1, Recoverable = true, RecoverUntil = DateTimeOffset.UtcNow.AddDays(7) };
            return Task.FromResult(new RoomOperationResult(roomId, Room.Version, "dissolved"));
        }
        public Task<RoomOperationResult> RestoreAsync(Guid roomId, RoomRestoreRequest request, CancellationToken ct)
        {
            Restores.Add(request); Room = Room with { Status = "active", Version = Room.Version + 1, Recoverable = false, RecoverUntil = null };
            return Task.FromResult(new RoomOperationResult(roomId, Room.Version, "restored"));
        }
    }
}
