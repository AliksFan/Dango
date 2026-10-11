using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Client.Sync.Rooms;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PorchCrudC1JourneyTests
{
    [STATestMethod]
    [TestCategory("PorchCrudC1")]
    public void NativePorchCrudPersistsAndForeignAccountCannotReadOrMutate()
    {
        // First operation: a separate approval, before any path, key, SQL, HWND or listener.
        var owner = PorchCrudC1TestOwner.Require(); var elapsed = Stopwatch.StartNew();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        PorchCrudC1HttpsFixture? fixture = null; PorchCrudC1DesktopRig? rig = null;
        var operations = new List<Task>(); var passed = false;
        PorchCrudC1ActorEvidence[]? actors = null; Guid? roomId = null;
        var walks = new List<object>();
        try
        {
            fixture = Complete(PorchCrudC1HttpsFixture.CreateAsync(owner, value => fixture = value), operations, 120);
            Complete(fixture.RegisterAsync(fixture.A), operations); Complete(fixture.RegisterAsync(fixture.B), operations);
            using var outsiderSecrets = new AccountSyncB1c2MemorySecrets(fixture.Endpoint);
            using var outsider = fixture.CreateOutsider(outsiderSecrets);
            var identity = Complete(outsider.LoginAsync(fixture.B.Email, fixture.B.Password, false), operations);
            Complete(outsider.CreateProfileClient(identity).UpdateAsync(1, "门廊访客", CancellationToken.None), operations);
            var foreign = outsider.CreateRoomManagementClient(identity);
            rig = new("porch", 1, owner, fixture); rig.Login(fixture.A);
            rig.Wait(() => rig.Controller.IsLoggedIn && rig.PrimaryEnabled && rig.PrimaryName == "保存昵称");
            rig.Uia(u => { u.Value("AccountNickname", "门廊主人"); u.InvokeId("AccountPrimary"); });
            rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
            Assert.IsFalse(rig.HasChoice); rig.CloseAccount();
            var runtime = rig.Home.BoundRuntime; var entityIds = runtime!.EntityIds.ToArray();
            rig.Uia(u => u.InvokeId("EnterRoomButton"));
            rig.Wait(() => rig.Home.IsInPorch && rig.Rooms.CurrentState is { Page: RoomPanelPage.Directory, Busy: false, Rooms.Length: 1 });
            Assert.AreSame(runtime, rig.Home.BoundRuntime); CollectionAssert.AreEqual(entityIds, runtime.EntityIds.ToArray());
            rig.AssertHealthyFrame();
            using (var keyboard = new PorchCrudC1Keyboard(rig.Window))
            {
                Walk(rig, keyboard, "porch-create", 160, walks);
                keyboard.Press(0x57); rig.Wait(() => rig.Home.IsRoomPanelOpen && rig.Rooms.CurrentState.Page == RoomPanelPage.Create);
                AssertSceneForm(rig); rig.Mark("create-form", capture: true);
                var before = owner.Budget.TotalAttempts;
                rig.Uia(u => { u.Value("RoomName", ""); u.Value("RoomCapacity", "6"); u.InvokeId("RoomPrimary"); });
                rig.Wait(() => !rig.Rooms.CurrentState.Busy && rig.Rooms.CurrentState.Message is not null);
                Assert.AreEqual(before, owner.Budget.TotalAttempts);
                rig.Uia(u => { u.Value("RoomName", "门廊试验房"); u.Value("RoomCapacity", "13"); u.InvokeId("RoomPrimary"); });
                rig.Wait(() => !rig.Rooms.CurrentState.Busy); Assert.AreEqual(before, owner.Budget.TotalAttempts);
                rig.Uia(u => { u.Value("RoomCapacity", "6"); u.InvokeId("RoomPrimary"); });
                Detail(rig, 1, "active", "门廊试验房", 6); roomId = fixture.RoomId;
                Assert.IsNotNull(roomId); rig.Mark("created-v1", capture: true);
                RejectForeign(() => foreign.ReadAsync(roomId.Value, CancellationToken.None), operations);
                RejectForeign(() => foreign.UpdateAsync(roomId.Value, new(Guid.NewGuid(), 1, "外来修改", 7), CancellationToken.None), operations);
                rig.Uia(u => u.InvokeId("RoomPrimary")); rig.Wait(() => rig.Rooms.CurrentState.Page == RoomPanelPage.Edit);
                rig.Uia(u => { u.Value("RoomName", "门廊茶室"); u.Value("RoomCapacity", "8"); u.InvokeId("RoomPrimary"); });
                Detail(rig, 2, "active", "门廊茶室", 8); rig.Mark("updated-v2", capture: true);
                var dissolveId = "RoomOpenDissolve_" + roomId.Value.ToString("N"); before = owner.Budget.TotalAttempts;
                rig.Uia(u => u.InvokeId(dissolveId)); rig.Wait(() => rig.Rooms.CurrentState.Page == RoomPanelPage.ConfirmDissolve);
                Assert.AreEqual(before, owner.Budget.TotalAttempts); rig.Mark("dissolve-confirmation", capture: true);
                rig.Uia(u => u.InvokeId("RoomBack")); Detail(rig, 2, "active", "门廊茶室", 8);
                Assert.AreEqual(before, owner.Budget.TotalAttempts);
                RejectForeign(() => foreign.DissolveAsync(roomId.Value, new(Guid.NewGuid(), 2, true), CancellationToken.None), operations);
                rig.Uia(u => u.InvokeId(dissolveId)); rig.Wait(() => rig.Rooms.CurrentState.Page == RoomPanelPage.ConfirmDissolve);
                rig.Uia(u => u.InvokeId("RoomPrimary")); Detail(rig, 3, "dissolved", "门廊茶室", 8);
                Assert.IsTrue(rig.Rooms.CurrentState.Selected!.Recoverable); rig.Mark("dissolved-v3", capture: true);
                RejectForeign(() => foreign.RestoreAsync(roomId.Value, new(Guid.NewGuid(), 3), CancellationToken.None), operations);
                rig.Uia(u => u.InvokeId("RoomClose")); rig.Wait(() => !rig.Home.IsRoomPanelOpen);
                Walk(rig, keyboard, "porch-archive", 1800, walks); keyboard.Press(0x57);
                rig.Wait(() => rig.Home.IsRoomPanelOpen && rig.Rooms.CurrentState is { Page: RoomPanelPage.Directory, DirectoryState: "dissolved", Busy: false });
                Assert.AreEqual(roomId.Value, rig.Rooms.CurrentState.Rooms!.Single().RoomId);
                rig.Uia(u => u.InvokeId("RoomSelectRoom_" + roomId.Value.ToString("N")));
                Detail(rig, 3, "dissolved", "门廊茶室", 8); rig.Uia(u => u.InvokeId("RoomPrimary"));
                Detail(rig, 4, "active", "门廊茶室", 8); rig.Mark("restored-v4", capture: true);
                rig.Uia(u => u.InvokeId("RoomBack")); rig.Wait(() => !rig.Rooms.CurrentState.Busy && rig.Rooms.CurrentState.Page == RoomPanelPage.Directory);
                Assert.AreEqual(0, rig.Rooms.CurrentState.Rooms!.Length);
                rig.Uia(u => u.InvokeId("RoomActiveDirectory")); rig.Wait(() => !rig.Rooms.CurrentState.Busy && rig.Rooms.CurrentState.Rooms?.Length == 2);
                rig.Uia(u => u.InvokeId("RoomClose")); rig.Wait(() => !rig.Home.IsRoomPanelOpen);
                var target = "room-" + roomId.Value.ToString("N");
                var doorIndex = Array.FindIndex(rig.Rooms.CurrentState.Rooms!, r => r.RoomId == roomId.Value);
                Walk(rig, keyboard, target, 1000 + 200 * doorIndex, walks); keyboard.Press(0x57);
                Detail(rig, 4, "active", "门廊茶室", 8); rig.Mark("restored-door-reread", capture: true);
                CollectionAssert.AreEqual(entityIds, runtime.EntityIds.ToArray());
                rig.Uia(u => u.InvokeId("RoomClose")); rig.Wait(() => !rig.Home.IsRoomPanelOpen);
            }
            rig.Uia(u => u.InvokeId("BackFromPorchButton")); rig.Wait(() => !rig.Home.IsInPorch);
            rig.OpenAccount(); rig.Uia(u => u.InvokeName("退出登录"));
            rig.Wait(() => !rig.Controller.IsLoggedIn && rig.PrimaryEnabled && rig.PrimaryName == "登录");
            Complete(outsider.LogoutAsync(), operations); actors = Complete(fixture.ReadFinalActorsAsync(), operations);
            Complete(fixture.DrainObserversAsync(), operations);
            Assert.AreEqual(21, owner.Budget.TotalAttempts);
            PorchCrudC1EvidenceContract.VerifyRequests(fixture.Requests, fixture.A.SessionId, fixture.B.SessionId, roomId!.Value, Stopwatch.Frequency);
            rig.Finish(true); passed = true;
        }
        catch { owner.Abort(); throw; }
        finally
        {
            try
            {
                var cleanup = Complete(AccountSyncB1bCleanup.RunAsync(
                    ("Desktop", () => { rig?.Finish(false); return Task.CompletedTask; }),
                    ("Operations", async () => { try { await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(20)); } catch when (operations.All(t => t.IsCompleted)) { foreach (var task in operations) _ = task.Exception; } }),
                    ("Fixture", async () => { if (fixture is not null) await fixture.DisposeAsync(); })), [], 90);
                cleanup.RequireSuccess();
                if (passed)
                {
                    Assert.IsTrue(rig!.CleanupPassed && fixture!.Tasks.All(t => t.IsCompleted));
                    owner.WriteEvidence("scoped-inputs-evidence.json", new { Version = 1, owner.OwnerId, owner.RecoveryEpoch, Accounts = actors, RoomId = roomId,
                        RoomRequestIds = fixture.Requests.Where(r => r.Path.StartsWith("/api/v1/rooms", StringComparison.Ordinal) && r.Method != "GET").Select(r => r.RequestId!.Value).ToArray() });
                    owner.WriteEvidence("journey-evidence.json", new { Version = 1, owner.OwnerId, Passed = true, Actors = actors, RoomId = roomId,
                        DurationSeconds = elapsed.Elapsed.TotalSeconds, HttpRequests = owner.Budget.HttpRequests, fixture.ClientsCreated, fixture.DesktopTransports,
                        StopwatchFrequency = Stopwatch.Frequency, Requests = fixture.Requests, Walks = walks, CleanupPassed = true,
                        UsesSystemTrust = false, UsesWindowsCredentials = false, ActualFormalHost = true, ActualApplicationHost = true, HealthyNativeFrames = rig.HealthyFrameObserved });
                }
            }
            finally { owner.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
    private static void Detail(PorchCrudC1DesktopRig rig, long version, string status, string name, int capacity)
    {
        rig.Wait(() => rig.Rooms.CurrentState is { Page: RoomPanelPage.Detail, Busy: false, Selected: { } selected } && selected.Version == version);
        var room = rig.Rooms.CurrentState.Selected!; Assert.AreEqual(status, room.Status); Assert.AreEqual(name, room.Name);
        Assert.AreEqual(capacity, room.Capacity); Assert.AreEqual("privateShared", room.Kind); Assert.AreEqual(1, room.ReservedSeats); Assert.AreEqual(0, room.Occupancy);
        AssertSceneForm(rig);
    }
    private static void AssertSceneForm(PorchCrudC1DesktopRig rig)
    {
        var form = (FrameworkElement)rig.Home.FindName("PorchOperations"); var porch = (FrameworkElement)rig.Home.FindName("PorchContent");
        rig.Window.UpdateLayout(); Assert.AreSame(porch, form.Parent);
        var rect = form.TransformToAncestor(porch).TransformBounds(new Rect(form.RenderSize));
        Assert.IsTrue(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= porch.ActualWidth + 1 && rect.Bottom <= porch.ActualHeight + 1);
        Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)rig.Home.FindName("AccountOverlay")).Visibility);
        Assert.AreEqual(0d, ((ScrollViewer)rig.Home.RoomPanel.FindName("RoomScroll")).ScrollableWidth, .1);
    }
    private static void Walk(PorchCrudC1DesktopRig rig, PorchCrudC1Keyboard keyboard, string target, double x, List<object> walks)
    {
        var start = rig.Home.PorchScene.PetWorldX; var key = start > x ? (ushort)0x41 : (ushort)0x44;
        rig.Wait(() => ((Grid)rig.Home.FindName("Stage")).IsKeyboardFocused);
        Assert.IsTrue(Math.Abs(start - x) > 50); var frame = rig.Home.BoundRuntime!.LastPhysicsFrame;
        keyboard.Down(key);
        try { rig.Wait(() => Math.Abs(rig.Home.PorchScene.PetWorldX - x) < 35, 35); }
        finally { keyboard.Up(key); }
        Assert.AreEqual(target, rig.Home.PorchScene.NearbyDoorId);
        Assert.AreNotSame(frame, rig.Home.BoundRuntime.LastPhysicsFrame); rig.AssertHealthyFrame();
        var end = rig.Home.PorchScene.PetWorldX; Assert.IsTrue(Math.Abs(start - end) > 50);
        walks.Add(new { Target = target, StartX = start, EndX = end, Key = key, SameRuntime = true, NewHealthyFrame = true });
    }
    private static void RejectForeign<T>(Func<Task<T>> action, List<Task> tasks)
    {
        var task = action(); tasks.Add(task);
        try { Complete(task, [], 30); Assert.Fail("Foreign account request unexpectedly succeeded."); }
        catch (RoomManagementClientException ex) { Assert.AreEqual(RoomManagementFailure.Rejected, ex.Failure); Assert.AreEqual("room_not_found", ex.Code); }
    }
    private static T Complete<T>(Task<T> task, List<Task> tasks, int seconds = 30)
    { Complete((Task)task, tasks, seconds); return task.GetAwaiter().GetResult(); }
    private static void Complete(Task task, List<Task> tasks, int seconds = 30)
    {
        tasks.Add(task); _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (task.IsCompleted || elapsed.Elapsed.TotalSeconds >= seconds) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        }
        Assert.IsTrue(task.IsCompleted, "Bounded original operation did not settle."); task.GetAwaiter().GetResult();
    }
}
