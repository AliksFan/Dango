using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Client.Sync.Rooms;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class PorchAccountSceneTests
{
    [STATestMethod]
    public void RealDoorsHaveStableAccessibleIdentitiesAndDistantInvocationDoesNotOpenAnything()
    {
        using var rig = new Rig();
        rig.Enter();
        Assert.AreEqual(4, rig.Client.LastLimit);
        Assert.IsNull(rig.Home.FindName("PorchAccountButton"));
        Assert.AreEqual(Visibility.Collapsed, rig.Element<Border>("AccountOverlay").Visibility);
        var door = rig.Target("room-" + rig.Client.First.RoomId.ToString("N"));
        var peer = UIElementAutomationPeer.CreatePeerForElement(door)!;
        Assert.AreEqual("PorchTarget_room-" + rig.Client.First.RoomId.ToString("N"), peer.GetAutomationId());
        Assert.IsInstanceOfType(peer.GetPattern(PatternInterface.Invoke), typeof(IInvokeProvider));
        rig.Scene.ResetProjection(560);
        Assert.IsFalse(rig.Scene.InteractTarget("room-" + rig.Client.First.RoomId.ToString("N")));
        Assert.AreEqual(0, rig.Client.DetailReads);
        Assert.IsFalse(rig.Home.IsAccountPanelOpen);
        rig.Scene.ResetProjection(1000);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        PumpUntil(() => rig.Home.IsRoomPanelOpen && !rig.Controller.CurrentState.Busy);
        Assert.AreEqual(1, rig.Client.DetailReads);
        Assert.AreEqual(rig.Client.First.RoomId, rig.Controller.CurrentState.Selected!.RoomId);
        Assert.IsFalse(rig.Home.RoomNavigation.CanSendRoomInput, "Reading a door cannot fabricate shared scene admission.");
    }

    [STATestMethod]
    [DataRow(960d, 540d)]
    [DataRow(520d, 820d)]
    [DataRow(430d, 500d)]
    public void FormsStayBesideTheTargetInsideThePorchAndClearGameplayInput(double width, double height)
    {
        using var rig = new Rig(width, height);
        rig.Enter(); rig.Scene.ResetProjection(160);
        Assert.IsTrue(rig.Scene.InteractNearestDoor());
        PumpUntil(() => rig.Home.IsRoomPanelOpen);
        rig.Window.UpdateLayout();
        var operations = rig.Element<Border>("PorchOperations");
        var porch = rig.Element<Grid>("PorchContent");
        Assert.AreSame(porch, operations.Parent);
        Assert.AreEqual(Visibility.Collapsed, rig.Element<Border>("AccountOverlay").Visibility);
        Assert.AreEqual(RoomPanelPage.Create, rig.Controller.CurrentState.Page);
        var bounds = operations.TransformToAncestor(porch).TransformBounds(new Rect(operations.RenderSize));
        Assert.IsTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= porch.ActualWidth + 1 && bounds.Bottom <= porch.ActualHeight + 1,
            $"Scene form escaped its viewport: {bounds} / {porch.RenderSize}.");
        var scroll = (ScrollViewer)rig.Home.RoomPanel.FindName("RoomScroll");
        Assert.AreEqual(0, scroll.ScrollableWidth, .1);
        Assert.IsTrue(scroll.ScrollableHeight > 0);
        var name = (TextBox)rig.Home.RoomPanel.FindName("RoomName");
        name.Focus();
        var before = rig.Scene.SelectedDoorId;
        rig.Press(Key.W);
        Assert.AreEqual(before, rig.Scene.SelectedDoorId, "Typing W in the scene form cannot select another target.");
        Save(rig.Home, $"scene-form-{width}-{height}.png");
        rig.Press(Key.Escape);
        Assert.IsFalse(rig.Home.IsAccountPanelOpen);
        Assert.IsTrue(rig.Home.IsInPorch);
        Assert.IsTrue(rig.Element<Grid>("Stage").IsKeyboardFocused);
        Assert.AreEqual(0, rig.Client.Creates);
    }

    [STATestMethod]
    public void SceneCrudPreservesExplicitConfirmationPendingRetryAndRestore()
    {
        using var rig = new Rig(); rig.Enter();
        rig.Scene.ResetProjection(1000); rig.Scene.InteractNearestDoor();
        PumpUntil(() => rig.Home.IsRoomPanelOpen);
        rig.Click("RoomPrimary");
        Assert.AreEqual(RoomPanelPage.Edit, rig.Controller.CurrentState.Page);
        ((TextBox)rig.Home.RoomPanel.FindName("RoomName")).Text = "新房名";
        rig.Click("RoomPrimary");
        PumpUntil(() => !rig.Controller.CurrentState.Busy);
        Assert.AreEqual(1, rig.Client.Updates);
        Assert.AreEqual("新房名", rig.Controller.CurrentState.Selected!.Name);
        rig.Click("RoomOpenDissolve_" + rig.Client.First.RoomId.ToString("N"));
        Assert.AreEqual(0, rig.Client.Dissolves, "Opening the scene confirmation does not dissolve.");
        rig.Client.LoseDissolveOnce = true;
        rig.Click("RoomPrimary"); PumpUntil(() => !rig.Controller.CurrentState.Busy);
        Assert.AreEqual(RoomPanelPage.Pending, rig.Controller.CurrentState.Page);
        var original = rig.Client.DissolveRequests.Single();
        rig.Home.HideAccountPanel();
        rig.Scene.ResetProjection(160); rig.Scene.InteractNearestDoor();
        Assert.AreEqual(RoomPanelPage.Pending, rig.Controller.CurrentState.Page, "Another facility cannot bypass an unknown operation.");
        Assert.AreEqual(0, rig.Client.Creates);
        rig.Click("RoomPrimary"); PumpUntil(() => !rig.Controller.CurrentState.Busy);
        Assert.AreEqual(2, rig.Client.Dissolves);
        Assert.AreEqual(original, rig.Client.DissolveRequests.Last());
        Assert.AreEqual("dissolved", rig.Controller.CurrentState.Selected!.Status);
        rig.Click("RoomPrimary"); PumpUntil(() => !rig.Controller.CurrentState.Busy);
        Assert.AreEqual(1, rig.Client.Restores);
        Assert.AreEqual("active", rig.Controller.CurrentState.Selected!.Status);
    }

    [STATestMethod]
    public void ArchivePaginationReplacesAccessibleDoorsAndDetachedOrFabricatedTargetsCannotSelect()
    {
        using var rig = new Rig(); rig.Enter();
        var stale = rig.Target("room-" + rig.Client.First.RoomId.ToString("N"));
        rig.Scene.ResetProjection(1800); rig.Scene.InteractNearestDoor();
        PumpUntil(() => rig.Controller.CurrentState.DirectoryState == "dissolved" && !rig.Controller.CurrentState.Busy);
        Assert.IsTrue(rig.Home.IsRoomPanelOpen);
        Assert.IsFalse(rig.Scene.TargetButtons.Contains(stale));
        var reads = rig.Client.DetailReads;
        stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(reads, rig.Client.DetailReads);
        rig.Click("RoomNextPage"); PumpUntil(() => !rig.Controller.CurrentState.Busy);
        Assert.AreEqual(rig.Client.Cursor, rig.Client.LastAfter);
        Assert.AreEqual(4, rig.Client.LastLimit);
        Assert.IsTrue(rig.Scene.TargetButtons.Any(b => AutomationProperties.GetAutomationId(b).EndsWith(rig.Client.Last.RoomId.ToString("N"))));
        Run(rig.Controller.HandleSceneAsync(new(RoomPanelAction.SelectRoom, Guid.NewGuid())));
        Assert.AreEqual(reads, rig.Client.DetailReads);
        rig.Click("RoomActiveDirectory"); PumpUntil(() => !rig.Controller.CurrentState.Busy);
        Assert.IsNull(rig.Client.LastAfter);
        Assert.IsNull(rig.Home.RoomPanel.FindName("RoomSelectRoom_" + rig.Client.First.RoomId.ToString("N")));
    }

    [STATestMethod]
    public void OfflineFacilitiesAskForLoginWhileBothLocalDoorsRemainPlayable()
    {
        using var rig = new Rig(signedIn: false);
        var ordinary = 0; var desktop = 0; var accounts = 0;
        rig.Home.LocalRoomRequested += () => ordinary++;
        rig.Home.DesktopPetRoomRequested += () => desktop++;
        rig.Home.AccountRequested += () => accounts++;
        rig.Enter(); Assert.AreEqual(0, rig.Client.ListReads);
        rig.Scene.ResetProjection(560); rig.Scene.InteractNearestDoor();
        rig.Scene.ResetProjection(780); rig.Scene.InteractNearestDoor();
        Assert.AreEqual(1, ordinary); Assert.AreEqual(1, desktop);
        rig.Scene.ResetProjection(350); rig.Scene.InteractNearestDoor();
        Assert.AreEqual(1, accounts);
        Assert.AreEqual(Visibility.Visible, rig.Element<Border>("AccountOverlay").Visibility);
        Assert.AreEqual(Visibility.Collapsed, rig.Element<Border>("PorchOperations").Visibility);
        Assert.AreEqual(0, rig.Client.ListReads);
    }

    [STATestMethod]
    public void VisitorAndStatusFacilitiesReachFormalOperationsWithoutEnteringOnSelection()
    {
        using var rig = new Rig(); rig.Enter();
        rig.Scene.ResetProjection(350); rig.Scene.InteractNearestDoor();
        PumpUntil(() => rig.Home.IsOnlineRoomPanelOpen && !rig.Online.CurrentState.Busy);
        Assert.AreEqual(OnlineRoomPage.ResolveCode, rig.Online.CurrentState.Page);
        ((PasswordBox)rig.Home.OnlineRoomPanel.FindName("OnlineRoomCodeInput")).Password = new string('A', 43);
        rig.OnlineClick(OnlineRoomAction.Resolve); PumpUntil(() => !rig.Online.CurrentState.Busy);
        Assert.AreEqual(1, rig.Access.Resolves);
        Assert.AreEqual(0, rig.Access.EnterRequests.Count);
        Assert.AreEqual(OnlineRoomPage.ConfirmEnter, rig.Online.CurrentState.Page);
        rig.OnlineClick(OnlineRoomAction.Enter); PumpUntil(() => !rig.Online.CurrentState.Busy);
        Assert.AreEqual(1, rig.Access.EnterRequests.Count);
        Assert.IsTrue(rig.Online.CurrentState.Connected);
        rig.OnlineClick(OnlineRoomAction.Close);
        rig.Scene.ResetProjection(2000); rig.Scene.InteractNearestDoor();
        PumpUntil(() => !rig.Online.CurrentState.Busy);
        Assert.AreEqual(1, rig.Access.EnterRequests.Count, "Opening the status sign restores a lease rather than submitting Enter.");
        Assert.IsNotNull(rig.Home.OnlineRoomPanel.FindName("OnlineRoomEndLease"));
        rig.OnlineClick(OnlineRoomAction.OpenLeave);
        Assert.AreEqual(0, rig.Access.Leaves);
        Assert.IsNotNull(rig.Home.OnlineRoomPanel.FindName("OnlineRoomLeave"));
        Assert.IsFalse(rig.Home.RoomNavigation.CanSendRoomInput);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SameRealPhysicalPetWalksToBuildSignAndKeepsRenderingUnderTheSceneForm() => PhysicalScene(false);

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("NativeWindowInput")]
    public void OwnedNativeKeyboardOpensAndWalksThePhysicalPorch() => PhysicalScene(true);

    private static void PhysicalScene(bool nativeKeyboard)
    {
        using var native = new PorchSceneIntegrationTests.NativeHome(reviewMode: false);
        var home = native.Home;
        home.EnableLocalRoomDoor(); home.EnableDesktopPetRoomDoor(); home.EnablePorchAccountInteractions();
        home.SetAccountStatus(true, true, "测试昵称");
        var client = new ManagementClient();
        using var controller = new DesktopRoomController(home.RoomPanel, () => client, home.HideAccountPanel, home.ShowAccountPanel, _ => { });
        home.RoomPanelClosed += controller.Close;
        var scene = (PorchSceneView)home.FindName("PorchScene");
        native.Advance(90);
        var entity = native.Runtime.EntityIds.Single();
        using var keyboard = nativeKeyboard ? new OwnedPorchKeyboard(native.Window) : null;
        var stage = (Grid)home.FindName("Stage");
        stage.Focus();
        if (keyboard is not null)
        {
            PumpUntil(() => stage.IsKeyboardFocused);
            Assert.IsTrue(home.IsKeyboardFocusWithin, "Native input requires this owned scene to have WPF focus.");
            var trace = new List<object>();
            KeyEventHandler observe = (_, input) => trace.Add(new
            {
                Key = input.Key.ToString(), ImeKey = input.ImeProcessedKey.ToString(),
                input.Handled, StageFocused = stage.IsKeyboardFocused, home.IsAccountPanelOpen,
                home.IsEditing, home.IsInPorch, Modifiers = Keyboard.Modifiers.ToString()
            });
            home.AddHandler(Keyboard.PreviewKeyDownEvent, observe, true);
            try { keyboard.Press(0x57); }
            finally
            {
                home.RemoveHandler(Keyboard.PreviewKeyDownEvent, observe);
                Console.WriteLine("Owned W input: " + System.Text.Json.JsonSerializer.Serialize(trace));
            }
        }
        else
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement((Button)home.FindName("EnterRoomButton"))!;
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            PumpUntil(() => home.IsInPorch);
        }
        native.Advance(60);
        Assert.IsTrue(home.IsInPorch);
        Assert.AreEqual(Visibility.Visible, scene.LiveBody.Visibility);
        if (keyboard is not null) keyboard.Down(0x41); else SetDirection(home, Key.A, true);
        for (var i = 0; i < 300 && scene.PetWorldX > 220; i++) native.Advance(1);
        if (keyboard is not null) keyboard.Up(0x41); else SetDirection(home, Key.A, false);
        Assert.AreEqual("porch-create", scene.NearbyDoorId);
        if (keyboard is not null) keyboard.Press(0x57); else scene.InteractNearestDoor();
        native.Advance(60);
        Assert.IsTrue(home.IsRoomPanelOpen);
        Assert.AreEqual(RoomPanelPage.Create, controller.CurrentState.Page);
        Assert.AreEqual(Visibility.Visible, scene.LiveBody.Visibility);
        Assert.AreEqual(entity, native.Runtime.EntityIds.Single());
        Assert.IsNull(native.Runtime.LastError);
        Assert.AreEqual(0, client.Creates);
        Save(home, nativeKeyboard ? "native-input-inline-build-form.png" : "same-real-pet-inline-build-form.png");
    }

    private sealed class Rig : IDisposable
    {
        internal readonly ProductHomeView Home = new() { ReduceMotion = true };
        internal readonly Window Window;
        internal readonly ManagementClient Client = new();
        internal readonly DesktopRoomController Controller;
        internal readonly DesktopOnlineRoomControllerTests.Client Access = new();
        internal readonly DesktopOnlineRoomController Online;
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        internal PorchSceneView Scene => Element<PorchSceneView>("PorchScene");
        internal Rig(double width = 960, double height = 540, bool signedIn = true)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Home.EnableLocalRoomDoor(); Home.EnableDesktopPetRoomDoor(); Home.EnablePorchAccountInteractions();
            Home.SetAccountStatus(signedIn, true, signedIn ? "测试昵称" : null);
            Controller = new(Home.RoomPanel, () => signedIn ? Client : null, Home.HideAccountPanel, Home.ShowAccountPanel, _ => { });
            Home.RoomPanelClosed += Controller.Close;
            Online = new(Home.OnlineRoomPanel, () => signedIn ? Access : null, () => true, Home.HideAccountPanel,
                () => { Home.ShowRoomPanel(); Controller.Open(); }, Home.ShowAccountPanel, _ => { });
            Home.OnlineRoomPanelClosed += Online.Close;
            Controller.OnlineRequested += target => { Home.ShowOnlineRoomPanel(); Online.Open(target); };
            Home.RoomDirectoryRequested += Controller.Open;
            Window = new Window { Content = Home, Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false };
            Window.Show(); Window.UpdateLayout();
        }
        internal void Enter()
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(Element<Button>("EnterRoomButton"))!;
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            PumpUntil(() => Home.IsInPorch && !Controller.CurrentState.Busy);
            Window.UpdateLayout();
        }
        internal T Element<T>(string name) where T : class => (T)Home.FindName(name);
        internal Button Target(string id) => Scene.TargetButtons.Single(b => AutomationProperties.GetAutomationId(b) == "PorchTarget_" + id);
        internal void Click(string id) => ((Button)Home.RoomPanel.FindName(id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        internal void OnlineClick(OnlineRoomAction action) => ((Button)Home.OnlineRoomPanel.FindName("OnlineRoom" + action)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        internal void Press(Key key)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window)!, Environment.TickCount, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            typeof(ProductHomeView).GetMethod("OnPreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(Home, [Home, e]);
        }
        public void Dispose() { Window.Close(); Controller.Dispose(); Online.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class ManagementClient : IRoomManagementClient
    {
        public Uri Origin => new("https://porch.dango.invalid/");
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        internal RoomManagementView First = Room("同名房门", "active");
        internal readonly RoomManagementView Old = Room("同名房门", "dissolved");
        internal readonly RoomManagementView Last = Room("第二页旧房", "dissolved");
        internal readonly Guid Cursor = Guid.NewGuid();
        internal int ListReads, DetailReads, Creates, Updates, Dissolves, Restores, LastLimit;
        internal Guid? LastAfter;
        internal bool LoseDissolveOnce;
        internal readonly List<RoomDissolveRequest> DissolveRequests = [];
        public Task<RoomManagementPage> ListAsync(string state, Guid? after, int limit, CancellationToken ct)
        {
            ListReads++; LastLimit = limit; LastAfter = after;
            return Task.FromResult(state == "active" ? new RoomManagementPage([First], null) :
                after is null ? new RoomManagementPage([Old], Cursor) : new RoomManagementPage([Last], null));
        }
        public Task<RoomManagementView> ReadAsync(Guid id, CancellationToken ct)
        { DetailReads++; return Task.FromResult(id == First.RoomId ? First : id == Old.RoomId ? Old : Last); }
        public Task<RoomOperationResult> CreateAsync(RoomCreateRequest request, CancellationToken ct)
        { Creates++; First = Room(request.Name, "active") with { Kind = request.Kind, Capacity = request.Capacity }; return Receipt("created"); }
        public Task<RoomOperationResult> UpdateAsync(Guid id, RoomUpdateRequest request, CancellationToken ct)
        { Updates++; First = First with { Name = request.Name, Capacity = request.Capacity, Version = First.Version + 1 }; return Receipt("updated"); }
        public Task<RoomOperationResult> DissolveAsync(Guid id, RoomDissolveRequest request, CancellationToken ct)
        {
            Dissolves++; DissolveRequests.Add(request);
            First = First with { Status = "dissolved", Version = First.Version + 1, Recoverable = true, RecoverUntil = DateTimeOffset.UtcNow.AddDays(7) };
            if (LoseDissolveOnce) { LoseDissolveOnce = false; throw new RoomManagementClientException(RoomManagementFailure.Unavailable, "response_lost"); }
            return Receipt("dissolved");
        }
        public Task<RoomOperationResult> RestoreAsync(Guid id, RoomRestoreRequest request, CancellationToken ct)
        { Restores++; First = First with { Status = "active", Version = First.Version + 1 }; return Receipt("restored"); }
        private Task<RoomOperationResult> Receipt(string operation) => Task.FromResult(new RoomOperationResult(First.RoomId, First.Version, operation));
        private static RoomManagementView Room(string name, string status) => new(Guid.NewGuid(), "publicShared", name, 6,
            status, 1, 1, 0, status == "dissolved" ? DateTimeOffset.UtcNow.AddDays(7) : null, status == "dissolved", "shared-default", 1);
    }

    private static void Run(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void SetDirection(ProductHomeView home, Key key, bool down) =>
        Assert.IsTrue((bool)typeof(ProductHomeView).GetMethod("SetLiveDirectionalKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(home, [key, down, false])!);
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "The bounded scene transition did not finish.");
    }
    private static void Save(ProductHomeView home, string name)
    {
        home.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(home.ActualWidth), (int)Math.Ceiling(home.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(home); using var stream = TestEvidence.Create("porch-account-scenes", name);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
}
