using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

/// <summary>Independent UIA client against one owned real HWND; HTTP and secrets remain controlled fixtures.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AccountWindowUiaTests
{
    private static readonly Uri Origin = new("https://account-uia.dango.invalid/");
    private static readonly JsonElement White = Configuration("#FFFFFF");
    private static readonly JsonElement Cream = Configuration("#FFE8C2");

    [STATestMethod]
    public void IndependentUiaClientRequiresNicknameChoosesConfigurationAndShowsTheHomepageNickname()
    {
        using var rig = new Rig(new Receiver { Nickname = null }, new Local { Current = White }, new Secrets());
        rig.Login();
        rig.Wait(() => rig.Controller.IsLoggedIn && rig.Home.AccountPanel.FindName("AccountNickname") is TextBox
            && rig.PrimaryEnabled);
        Assert.IsNull(rig.Controller.Nickname);
        Assert.IsNull(rig.Controller.RoomClient);
        Assert.IsFalse(rig.Controller.ConfigurationReady);
        Assert.AreEqual(0, rig.Receiver.ProfileWrites);
        rig.Uia(client =>
        {
            Assert.AreEqual("保存昵称", client.Name("AccountPrimary"));
            Assert.IsFalse(client.HasId("AccountCloudChoice"));
            client.Value("AccountNickname", "  ");
            client.InvokeId("AccountPrimary");
        });
        rig.Wait(() => rig.PrimaryEnabled && rig.Message.Contains("1–24", StringComparison.Ordinal));
        Assert.AreEqual(0, rig.Receiver.ProfileWrites);
        rig.Uia(client => { client.Value("AccountNickname", "  小团子  "); client.InvokeId("AccountPrimary"); });
        rig.Wait(() => rig.Home.AccountPanel.FindName("AccountCloudChoice") is not null && rig.PrimaryEnabled == false);
        Assert.AreEqual("小团子", rig.Controller.Nickname);
        Assert.AreEqual(1, rig.Receiver.ProfileWrites);
        Assert.AreEqual(0, rig.Local.Applied);
        Assert.IsFalse(rig.Controller.ConfigurationReady);
        rig.Save("nickname-before-configuration.png");
        rig.Uia(client => { client.Select("AccountCloudChoice"); client.InvokeId("AccountPrimary"); });
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, rig.Local.Current));
        Assert.AreEqual(1, rig.Local.Applied);
        Assert.AreEqual(0, rig.Receiver.ConfigurationWrites);
        Assert.AreEqual(rig.Receiver.PetId, rig.Controller.RoomClient!.PetId);
        rig.Uia(client => client.InvokeName("继续本地游玩"));
        rig.Wait(() => !rig.Home.IsAccountPanelOpen);
        rig.Uia(client => Assert.AreEqual("小团子", client.Name("AccountButton")));
        rig.Save("homepage-after-nickname-choice.png");
    }

    [STATestMethod]
    public void UiaClosingThePanelLeavesRealControllerSaveAndTimerSynchronizationRunning()
    {
        using var rig = new Rig(new Receiver(), new Local { Current = Cream }, new Secrets());
        rig.Login();
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        rig.Uia(client => client.InvokeName("继续本地游玩"));
        rig.Wait(() => !rig.Home.IsAccountPanelOpen);
        rig.Local.Current = White;
        rig.Controller.NotifyAppearanceSaved();
        rig.Wait(() => rig.Receiver.ConfigurationWrites == 1 && AppearanceContract.Equivalent(White, rig.Receiver.Cloud));
        var writes = rig.Receiver.ConfigurationWrites;
        rig.Receiver.Cloud = Cream; rig.Receiver.ConfigurationVersion++;
        // Do not manually call PumpConfigurationAsync: the actual Desktop dispatcher timer must observe this change.
        rig.Wait(() => AppearanceContract.Equivalent(Cream, rig.Local.Current));
        Assert.IsFalse(rig.Home.IsAccountPanelOpen);
        Assert.AreEqual(writes, rig.Receiver.ConfigurationWrites, "The timer-applied remote change must not echo back.");
        rig.Uia(client =>
        {
            Assert.AreEqual("团子号主", client.Name("AccountButton"));
            client.InvokeId("AccountButton");
        });
        rig.Wait(() => rig.Home.IsAccountPanelOpen && rig.PrimaryEnabled);
        rig.Uia(client =>
        {
            Assert.IsFalse(client.HasId("AccountCloudChoice"));
            Assert.IsFalse(client.HasButton("我的房间"));
            client.InvokeName("继续本地游玩");
        });
        rig.Wait(() => !rig.Home.IsAccountPanelOpen);
        rig.Save("homepage-after-closed-panel-timer-sync.png");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RememberedBackgroundLoginPublishesNicknameAndAsksOnlyForActualConfigurationDifferences(bool differs)
    {
        var secrets = new Secrets();
        var initial = new Receiver();
        using (var first = new Rig(initial, new Local { Current = Cream }, secrets))
        {
            first.Login(remember: true);
            first.Wait(() => first.Controller.ConfigurationReady && first.PrimaryEnabled);
            first.Uia(client => client.InvokeName("继续本地游玩"));
            first.Wait(() => !first.Home.IsAccountPanelOpen);
        }
        var receiver = new Receiver { UserId = initial.UserId, PetId = initial.PetId };
        using var restored = new Rig(receiver, new Local { Current = differs ? White : Cream }, secrets);
        restored.Controller.InitializeRememberedSession();
        restored.Wait(() => differs ? restored.Home.IsAccountPanelOpen
            && restored.Home.AccountPanel.FindName("AccountCloudChoice") is not null
            : restored.Controller.ConfigurationReady);
        Assert.IsTrue(restored.Controller.IsLoggedIn);
        Assert.AreEqual(differs, restored.Home.IsAccountPanelOpen);
        Assert.AreEqual(0, receiver.ConfigurationWrites);
        Assert.AreEqual(0, restored.Local.Applied);
        restored.Uia(client =>
        {
            Assert.AreEqual("团子号主", client.Name("AccountButton"));
            Assert.AreEqual(differs, client.HasId("AccountCloudChoice"));
            if (differs) { client.Select("AccountLocalChoice"); client.InvokeId("AccountPrimary"); }
        });
        if (differs)
        {
            restored.Wait(() => restored.Controller.ConfigurationReady && restored.PrimaryEnabled);
            Assert.AreEqual(1, receiver.ConfigurationWrites);
            Assert.IsTrue(AppearanceContract.Equivalent(White, receiver.Cloud));
            Assert.AreEqual(initial.PetId, restored.Controller.RoomClient!.PetId);
            restored.Uia(client => client.InvokeName("继续本地游玩"));
            restored.Wait(() => !restored.Home.IsAccountPanelOpen);
        }
        restored.Save(differs ? "remembered-different-after-explicit-choice.png" : "remembered-equal-no-overlay.png");
    }

    private sealed class Rig : IDisposable
    {
        internal readonly Receiver Receiver;
        internal readonly Local Local;
        internal readonly ProductHomeView Home = new() { ReduceMotion = true };
        internal readonly DesktopAccountController Controller;
        private readonly Window window;
        private readonly nint handle;
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        internal bool PrimaryEnabled => ((Button)Home.AccountPanel.FindName("AccountPrimary")).IsEnabled;
        internal string Message => ((TextBlock)Home.AccountPanel.FindName("AccountMessage")).Text;

        internal Rig(Receiver receiver, Local local, Secrets secrets)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Receiver = receiver; Local = local;
            DesktopAccountController? current = null;
            Controller = current = new(Home.AccountPanel, Origin, secrets, remote => new(remote, local, new Journal()), local.CaptureAsync,
                Home.HideAccountPanel, (signedIn, ready) => Home.SetAccountStatus(signedIn, ready, current?.Nickname),
                (origin, store) =>
                {
                    receiver.Cookies = new CookieContainer();
                    return FormalAccountClient.ForTesting(origin, receiver, receiver.Cookies, store);
                }, createSync: (remote, baseline) => new(remote, local, new Journal(), baseline));
            Home.AccountRequested += Controller.Open;
            Home.AccountPanelClosed += Controller.Close;
            Controller.AccountAttentionRequested += () => { Home.ShowAccountPanel(); Controller.Open(); };
            window = new Window { Title = "Dango isolated account UIA", Content = Home, Width = 960, Height = 680,
                Left = 80, Top = 80, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); window.UpdateLayout();
            handle = new WindowInteropHelper(window).Handle;
            Assert.AreNotEqual(nint.Zero, handle);
        }

        internal void Login(bool remember = false)
        {
            Uia(client => client.InvokeId("AccountButton"));
            Wait(() => Home.IsAccountPanelOpen && PrimaryEnabled);
            Uia(client =>
            {
                client.Value("AccountEmail", "uia-fixture@dango.invalid");
                if (remember) client.Toggle("AccountRememberLogin");
            });
            // PasswordBox intentionally has no readable ValuePattern. Supply only this fixture's synthetic password.
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = "fictional_fixture_password";
            Uia(client => client.InvokeId("AccountPrimary"));
        }

        internal void Uia(Action<UiaClient> action)
        {
            var task = Task.Run(() =>
            {
                Assert.AreNotEqual(ownerThread, Environment.CurrentManagedThreadId, "Client UIA calls must cross the dispatcher boundary.");
                action(new UiaClient(handle));
            });
            Wait(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        internal void Wait(Func<bool> condition)
        {
            if (condition()) return;
            var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed > TimeSpan.FromSeconds(8)) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.IsTrue(condition(), "The bounded real-window account transition did not finish.");
        }

        internal void Save(string name)
        {
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Home.ActualWidth), (int)Math.Ceiling(Home.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(Home);
            using var stream = TestEvidence.Create("account-window-uia", name);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        }

        public void Dispose()
        {
            Controller.Dispose(); window.Close();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class UiaClient(nint handle)
    {
        private readonly AutomationElement root = AutomationElement.FromHandle(handle);
        private AutomationElement? Id(string id) => root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        private AutomationElement? Button(string name) => root.FindFirst(TreeScope.Descendants,
            new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, name)));
        private AutomationElement Required(string id) => Id(id) ?? throw new AssertFailedException($"Owned window UIA id not found: {id}.");
        internal bool HasId(string id) => Id(id) is not null;
        internal bool HasButton(string name) => Button(name) is not null;
        internal string Name(string id) => Required(id).Current.Name;
        internal void Value(string id, string value) => ((ValuePattern)Required(id).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
        internal void Select(string id) => ((SelectionItemPattern)Required(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        internal void Toggle(string id) => ((TogglePattern)Required(id).GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        internal void InvokeId(string id) => ((InvokePattern)Required(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        internal void InvokeName(string name) => ((InvokePattern)(Button(name) ?? throw new AssertFailedException($"Owned window UIA button not found: {name}."))
            .GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    private sealed class Receiver : HttpMessageHandler
    {
        internal Guid UserId { get; init; } = Guid.NewGuid();
        internal Guid PetId { get; init; } = Guid.NewGuid();
        internal CookieContainer Cookies = new();
        internal string? Nickname = "团子号主";
        internal long ProfileVersion = 1, ConfigurationVersion = 1;
        internal JsonElement Cloud = Cream;
        internal int ProfileWrites, ConfigurationWrites;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual(Origin.Host, request.RequestUri!.Host, "The fixture cannot address another origin.");
            ct.ThrowIfCancellationRequested();
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                Cookies.Add(Origin, new Cookie("__Host-Dango.Auth", "fictional_uia_session", "/")
                    { Secure = true, HttpOnly = true, Expires = DateTime.UtcNow.AddHours(7) });
                return Json(new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(7) });
            }
            if (path == "/api/v1/me/profile" && request.Method == HttpMethod.Patch)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.AreEqual(ProfileVersion, body.GetProperty("expectedVersion").GetInt64());
                Nickname = AccountNickname.Normalize(body.GetProperty("nickname").GetString());
                ProfileVersion++; ProfileWrites++;
            }
            if (path == "/api/v1/me/pet/configuration" && request.Method == HttpMethod.Patch)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.AreEqual(ConfigurationVersion, body.GetProperty("expectedVersion").GetInt64());
                Cloud = AppearanceContract.ValidateAndClone(body.GetProperty("configuration"));
                ConfigurationVersion++; ConfigurationWrites++;
                return Json(new { userId = UserId, petId = PetId, configurationVersion = ConfigurationVersion, configuration = Cloud });
            }
            return path switch
            {
                "/api/v1/auth/request-protection" => Json(new { requestToken = "fictional_uia_request_protection" }),
                "/api/v1/me" => Json(new { userId = UserId }),
                "/api/v1/me/profile" => Json(new { userId = UserId, nickname = Nickname, profileVersion = ProfileVersion }),
                "/api/v1/me/pet" => Json(new { userId = UserId, petId = PetId, configurationVersion = ConfigurationVersion, configuration = Cloud }),
                _ => throw new AssertFailedException("Unexpected controlled account UIA request path.")
            };
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class Secrets : IAccountSecretStore
    {
        private readonly Dictionary<string, byte[]> values = [];
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) => Task.FromResult(values.TryGetValue(slot, out var value) ? value.ToArray() : null);
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct) { values[slot] = value.ToArray(); return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct) { values.Remove(slot); return Task.CompletedTask; }
    }

    private sealed class Local : ILocalPetConfigurationRepository
    {
        internal JsonElement Current = White;
        internal int Applied;
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(new LocalPetConfigurationSnapshot(false, ReadOnlyMemory<byte>.Empty, Current));
        public Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Current = configuration.Clone(); Applied++; return Task.CompletedTask; }
    }

    private sealed class Journal : IPetConfigurationJournal
    {
        private readonly Dictionary<Guid, PetConfigurationOriginals> originals = [];
        public Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals value, CancellationToken ct)
        { var id = Guid.NewGuid(); originals[id] = value; return Task.FromResult(id); }
        public Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct) => Task.FromResult(originals[id]);
        public Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct) => Task.CompletedTask;
    }

    private static JsonElement Configuration(string body) => JsonSerializer.SerializeToElement(new
    { appearanceSchemaVersion = 3, bodyColorHex = body, eyeColorHex = "#272321", eyeSizePercent = 100, eyeOvalityPercent = 0, eyeHeightControlPercent = 51, eyeSpacingControlPercent = 24.5 });
}
