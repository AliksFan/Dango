using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Core.Animation;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

// Complete native Desktop host, with an in-memory HTTP handler: no database,
// TLS listener, original state, credentials, WinCred or earlier stage grant.
[TestClass]
[DoNotParallelize]
[TestCategory("OfflinePorchHost")]
public sealed class PorchCrudC1OfflineHostTests
{
    [STATestMethod]
    public void CompleteHostNativeWalkReachesBuildArchiveAndConfirmedRoomDoor()
    {
        using var rig = new Rig();
        rig.LoginAndEnter();
        var home = rig.Home;
        var start = home.PorchScene.PetWorldX;
        var minimum = start; var maximum = start;
        var trace = new List<object>();
        KeyEventHandler observe = (_, input) => trace.Add(new
        {
            Key = input.Key.ToString(), ImeKey = input.ImeProcessedKey.ToString(), input.Handled,
            StageFocused = ((Grid)home.FindName("Stage")).IsKeyboardFocused,
            home.IsAccountPanelOpen, home.IsInPorch, Modifiers = Keyboard.Modifiers.ToString(),
            Intent = PorchCrudC1DesktopRig.Field<object>(home, "liveIntent").ToString()
        });
        using var keyboard = new PorchCrudC1Keyboard(rig.Window);
        rig.Wait(() => ((Grid)home.FindName("Stage")).IsKeyboardFocused);
        home.AddHandler(Keyboard.PreviewKeyDownEvent, observe, true);
        try
        {
            keyboard.Down(0x41);
            try
            {
                var intent = PorchCrudC1DesktopRig.Field<DirectionalControlIntent>(home, "liveIntent");
                Assert.IsTrue(intent.Left && !intent.Right, "A must reach gameplay before any position can count as controlled movement.");
                rig.Wait(() =>
                {
                    keyboard.AssertForegroundOwned();
                    minimum = Math.Min(minimum, home.PorchScene.PetWorldX);
                    maximum = Math.Max(maximum, home.PorchScene.PetWorldX);
                    return home.PorchScene.NearbyDoorId == "porch-create";
                }, 35);
            }
            finally { keyboard.Up(0x41); }
            Assert.AreEqual("porch-create", home.PorchScene.NearbyDoorId);
            Assert.IsTrue(Math.Abs(start - home.PorchScene.PetWorldX) > 50);
            keyboard.Press(0x57);
            rig.Wait(() => home.IsRoomPanelOpen && rig.Rooms.CurrentState.Page == RoomPanelPage.Create);
            Assert.IsTrue(rig.Runtime.IsRunning && rig.Runtime.LastError is null);
            rig.CloseRoom();
            MoveTo("porch-archive", 0x44);
            keyboard.Press(0x57);
            rig.Wait(() => home.IsRoomPanelOpen && rig.Rooms.CurrentState is { Busy: false, Page: RoomPanelPage.Directory, DirectoryState: "dissolved", Rooms.Length: 0 });
            rig.ClickRoom("RoomActiveDirectory");
            rig.Wait(() => rig.Rooms.CurrentState is { Busy: false, Page: RoomPanelPage.Directory, DirectoryState: "active", Rooms.Length: 1 });
            var roomId = rig.Rooms.CurrentState.Rooms!.Single().RoomId;
            rig.CloseRoom();
            MoveTo("room-" + roomId.ToString("N"), 0x41);
            keyboard.Press(0x57);
            rig.Wait(() => home.IsRoomPanelOpen && rig.Rooms.CurrentState is { Busy: false, Page: RoomPanelPage.Detail, Selected: { } selected } && selected.RoomId == roomId);
            Assert.IsTrue(rig.Runtime.IsRunning && rig.Runtime.LastPhysicsFrame is { Succeeded: true } && rig.Runtime.LastError is null);

            void MoveTo(string target, ushort key)
            {
                var before = home.PorchScene.PetWorldX;
                rig.Wait(() => ((Grid)home.FindName("Stage")).IsKeyboardFocused);
                keyboard.Down(key);
                try
                {
                    var control = PorchCrudC1DesktopRig.Field<DirectionalControlIntent>(home, "liveIntent");
                    Assert.IsTrue(key == 0x41 ? control.Left && !control.Right : control.Right && !control.Left);
                    rig.Wait(() => { keyboard.AssertForegroundOwned(); return home.PorchScene.NearbyDoorId == target; }, 35);
                }
                finally { keyboard.Up(key); }
                Assert.IsTrue(Math.Abs(home.PorchScene.PetWorldX - before) > 50);
            }
        }
        finally
        {
            home.RemoveHandler(Keyboard.PreviewKeyDownEvent, observe);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Offline = true, DatabaseOpened = false, HttpListenerOpened = false,
                Start = start, End = home.PorchScene.PetWorldX, Minimum = minimum, Maximum = maximum,
                home.IsAccountPanelOpen, Target = home.PorchScene.NearbyDoorId, Trace = trace,
                RuntimeError = rig.Runtime.LastError, HealthyNativeFrame = rig.Runtime.LastPhysicsFrame?.Succeeded
            }));
        }
    }

    [STATestMethod]
    public void GameplayDisablesImeWhileAccountRoomAndEditorTextContextsRemainEnabled()
    {
        var home = new ProductHomeView();
        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled((Grid)home.FindName("Stage")));
        foreach (var name in new[] { "EditorPanel", "FaceEditorTools", "AccessoryEditorTools", "PorchOperations", "AccountOverlay" })
            Assert.IsTrue(InputMethod.GetIsInputMethodEnabled((DependencyObject)home.FindName(name)), "Text context must retain IME: " + name);
        home.RoomPanel.Apply(new(RoomPanelPage.Create));
        var roomName = (TextBox)home.RoomPanel.FindName("RoomName");
        Assert.IsTrue(InputMethod.GetIsInputMethodEnabled(roomName));
        roomName.Text = "中文门廊房名";
        Assert.AreEqual("中文门廊房名", roomName.Text);
        Assert.IsTrue(InputMethod.GetIsInputMethodEnabled((TextBox)home.AccountPanel.FindName("AccountEmail")));
    }

    [STATestMethod]
    public void CapturedRoomControllerCanBeDrainedAfterHostClearsItsReference()
    {
        using var rig = new Rig();
        var original = rig.Rooms;
        var operations = PorchCrudC1DesktopRig.Field<SemaphoreSlim>(original, "operations");
        var drained = PorchCrudC1DesktopRig.CaptureRoomDrain(original);
        Assert.IsTrue(operations.Wait(0));
        rig.Host.Dispose();
        Assert.IsNull(PorchCrudC1DesktopRig.Field<DesktopRoomController?>(rig.Host, "roomController"));
        Assert.AreSame(original, rig.Rooms);
        Assert.IsFalse(drained(), "Host disposal cannot fabricate completion of the original operation.");
        operations.Release();
        rig.Wait(drained);
        Assert.IsTrue(drained());
    }

    private sealed class Rig : IDisposable
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        private readonly AccountSyncB1c2MemorySecrets secrets;
        internal DangoApplicationHost Host { get; }
        internal ControlCenterWindow Window { get; }
        internal ProductHomeView Home => Window.ProductHome;
        internal DesktopAccountController Account { get; }
        internal DesktopRoomController Rooms { get; }
        internal StackedDangoSceneRuntime Runtime { get; }
        internal Rig()
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var origin = new Uri("https://127.0.0.1:54321/");
            secrets = new(origin);
            var root = PorchCrudC1HttpsFixture.RepositoryRoot();
            var directory = Path.Combine(root, "artifacts", "porch-crud-c1-offline-host", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var paths = new DangoUserDataPaths(Path.Combine(directory, "settings.json"));
            var native = Path.Combine(root, PorchCrudC1HttpsFixture.NativeRelativePath);
            Assert.AreEqual(PorchCrudC1HttpsFixture.NativeSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))));
            new AppearanceSettingsStore().Save(paths.AppearancePath, new DangoAppearanceConfiguration(
                OpaqueSrgbColor.Parse("#FFE8C2"), OpaqueSrgbColor.Parse("#272321"), new EyeLayoutParameters(103)));
            var state = new State();
            var dependencies = new DangoAccountDependencies(origin, secrets, (endpoint, store) =>
            {
                Assert.AreEqual(origin, endpoint); Assert.AreSame(secrets, store);
                var cookies = new CookieContainer();
                return FormalAccountClient.ForTesting(origin, new Receiver(origin, state, cookies), cookies, store);
            });
            Host = DangoApplicationHost.CreateForAccountIntegration(paths.SettingsPath, native, dependencies);
            Host.Start();
            Window = PorchCrudC1DesktopRig.Field<ControlCenterWindow>(Host, "controlCenterWindow");
            Account = PorchCrudC1DesktopRig.Field<DesktopAccountController>(Host, "accountController");
            Rooms = PorchCrudC1DesktopRig.Field<DesktopRoomController>(Host, "roomController");
            Runtime = PorchCrudC1DesktopRig.Field<StackedDangoSceneRuntime>(Host, "stackedRuntime");
            Window.Left = 80; Window.Top = 80; Window.ShowInTaskbar = false;
            Wait(() => PrimaryEnabled && Runtime.LastPhysicsFrame is { Succeeded: true });
        }
        private bool PrimaryEnabled => ((Button)Home.AccountPanel.FindName("AccountPrimary")).IsEnabled;
        internal void LoginAndEnter()
        {
            Uia(root => Invoke(root, "AccountButton"));
            Wait(() => Home.IsAccountPanelOpen && PrimaryEnabled);
            Uia(root => ((ValuePattern)Find(root, "AccountEmail").GetCurrentPattern(ValuePattern.Pattern)).SetValue("offline-porch@example.invalid"));
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = "fictional_offline_probe";
            Uia(root => Invoke(root, "AccountPrimary"));
            Wait(() => Account.ConfigurationReady && PrimaryEnabled);
            Uia(root =>
            {
                var close = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "继续本地游玩"))!;
                ((InvokePattern)close.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            });
            Wait(() => !Home.IsAccountPanelOpen);
            Uia(root => Invoke(root, "EnterRoomButton"));
            Wait(() => Home.IsInPorch && Rooms.CurrentState is { Busy: false, Page: RoomPanelPage.Directory, Rooms.Length: 1 });
            Wait(() => Home.PorchScene.LiveBody.IsVisible && Runtime.LastPhysicsFrame is { Succeeded: true });
        }
        internal void ClickRoom(string id) => Uia(root => Invoke(root, id));
        internal void CloseRoom()
        {
            ClickRoom("RoomClose");
            Wait(() => !Home.IsRoomPanelOpen);
        }
        private void Uia(Action<AutomationElement> action)
        {
            var handle = new WindowInteropHelper(Window).Handle;
            var task = Task.Run(() =>
            {
                var root = AutomationElement.FromHandle(handle);
                Assert.AreEqual(Process.GetCurrentProcess().Id, root.Current.ProcessId);
                action(root);
            });
            Wait(() => task.IsCompleted); task.GetAwaiter().GetResult();
        }
        private static AutomationElement Find(AutomationElement root, string id) => root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id)) ?? throw new AssertFailedException("Owned control missing: " + id);
        private static void Invoke(AutomationElement root, string id) => ((InvokePattern)Find(root, id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        internal void Wait(Func<bool> condition, int seconds = 8)
        {
            if (condition()) return;
            var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed.TotalSeconds >= seconds) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.IsTrue(condition(), "Offline complete-host condition timed out.");
        }
        public void Dispose()
        {
            try { Host.Dispose(); secrets.Dispose(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
    private sealed class State
    {
        internal Guid UserId = Guid.NewGuid(), PetId = Guid.NewGuid(), RoomId = Guid.NewGuid(), SessionId = Guid.NewGuid();
        internal RoomManagementView Room => new(RoomId, "desktopPet", "虚构默认房", 2, "active", 1, 1, 1, null, false, "desktop-pet-default", 1);
    }
    private sealed class Receiver(Uri origin, State state, CookieContainer cookies) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Assert.AreEqual(origin, new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"));
            object response;
            if (uri.AbsolutePath == "/api/v1/auth/login")
            {
                cookies.Add(origin, new Cookie("__Host-Dango.Auth", "fictional_offline_session", "/") { Secure = true, HttpOnly = true });
                response = new { userId = state.UserId, sessionId = state.SessionId, expiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            }
            else if (uri.AbsolutePath == $"/api/v1/rooms/{state.RoomId:D}") response = state.Room;
            else response = uri.AbsolutePath switch
            {
                "/api/v1/auth/request-protection" => new { requestToken = "fictional_offline_token" },
                "/api/v1/me" => new { userId = state.UserId },
                "/api/v1/me/profile" => new { userId = state.UserId, nickname = "离线复现", profileVersion = 2 },
                "/api/v1/me/pet" => new { userId = state.UserId, petId = state.PetId, configurationVersion = 1, configuration = PorchCrudC1HttpsFixture.Sample },
                "/api/v1/me/rooms" => new RoomManagementPage(uri.Query == "?state=dissolved&limit=4" ? [] : [state.Room], null),
                _ => throw new AssertFailedException("Unexpected offline request: " + request.Method + " " + uri.AbsolutePath)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        }
    }
}
