using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class AccountIntegrationBoundaryTests
{
    private static readonly Uri Origin = new("https://formal.dango.invalid/");
    private static readonly JsonElement White = Configuration("#FFFFFF");
    private static readonly JsonElement Cream = Configuration("#FFE8C2");

    [TestMethod]
    public void CredentialTargetsAreOriginAndSlotBoundWithoutSystemStoreWrites()
    {
        var session = WindowsAccountSecretStore.Target(Origin, "session");
        Assert.AreNotEqual(session, WindowsAccountSecretStore.Target(Origin, "registration"));
        Assert.AreNotEqual(session, WindowsAccountSecretStore.Target(new("https://other.dango.invalid/"), "session"));
        Assert.IsFalse(session.Contains("formal.dango.invalid", StringComparison.Ordinal));
        foreach (var value in new[] { "http://formal.dango.invalid/", "https://person@formal.dango.invalid/", "https://formal.dango.invalid/api", "https://formal.dango.invalid/?secret=x" })
            Assert.ThrowsExactly<System.IO.IOException>(() => WindowsAccountSecretStore.Target(new(value), "session"));
        Assert.ThrowsExactly<System.IO.IOException>(() => WindowsAccountSecretStore.Target(Origin, "arbitrary-slot"));
    }

    [STATestMethod]
    public void UnconfiguredServerCannotPretendLoginAndLocalCloseRemainsAvailable()
    {
        var view = new AccountPanelView();
        var closes = 0;
        using var controller = new DesktopAccountController(view, null, new MemorySecrets(), _ => throw new AssertFailedException(),
            _ => throw new AssertFailedException(), () => closes++, (_, _) => { });
        controller.Open();
        Assert.IsFalse(Field<Button>(view, "AccountPrimary").IsEnabled);
        Assert.IsTrue(Field<TextBlock>(view, "AccountMessage").Text.Contains("本地单人", StringComparison.Ordinal));
        ButtonNamed(view, "继续本地游玩").RaiseEvent(new(Button.ClickEvent));
        Assert.AreEqual(1, closes);
        Assert.IsFalse(controller.IsLoggedIn);
    }

    [STATestMethod]
    public void LoginOnlyReadsConfigurationsAndCloudChangesLocalOnlyAfterExplicitConfirmation()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var local = new Local(); var handler = new Receiver();
            using var controller = Controller(view, local, () => handler);
            controller.Open(); Login(view, "first@dango.invalid");
            PumpUntil(() => view.FindName("AccountCloudChoice") is not null);
            Assert.IsTrue(controller.IsLoggedIn);
            Assert.AreEqual(0, local.Applied);
            Assert.IsFalse(controller.ConfigurationReady);
            Field<RadioButton>(view, "AccountCloudChoice").IsChecked = true;
            Assert.AreEqual(0, local.Applied);
            Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => controller.ConfigurationReady);
            Assert.AreEqual(1, local.Applied);
            Assert.IsTrue(AppearanceContract.Equivalent(Cream, local.Current));
            ButtonNamed(view, "退出登录").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => !controller.IsLoggedIn && Field<Button>(view, "AccountPrimary").IsEnabled);
            Assert.AreEqual(1, local.Applied, "Logout must not replace the already applied local appearance.");
        });
    }

    [STATestMethod]
    public void ClosedLoginWithReceiverIgnoringCancellationCannotPublishOrContaminateTheNextLogin()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var local = new Local();
            var old = new Receiver { DelayLogin = true }; var next = new Receiver(); var created = 0;
            using var controller = Controller(view, local, () => ++created == 1 ? old : next);
            controller.Open(); Login(view, "old@dango.invalid");
            Assert.IsTrue(old.LoginStarted);
            controller.Close(); controller.Open();
            Assert.IsFalse(controller.IsLoggedIn);
            old.Delayed.TrySetResult(Receiver.Json(new { userId = old.UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(7) }));
            PumpUntil(() => Field<Button>(view, "AccountPrimary").IsEnabled);
            Assert.IsFalse(controller.IsLoggedIn);
            Assert.AreEqual(0, local.Applied);
            Login(view, "new@dango.invalid");
            PumpUntil(() => view.FindName("AccountCloudChoice") is not null);
            Assert.IsTrue(controller.IsLoggedIn);
            Assert.AreEqual(next.UserId, local.ReviewUserId);
            Assert.AreEqual(0, local.Applied);
            Assert.AreEqual(string.Empty, view.Password);
        });
    }

    [STATestMethod]
    public void FailedRememberedCleanupRemainsVisibleAndRetryAfterReopenActuallyDeletesTheSlot()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var store = new MemorySecrets(); var receiver = new Receiver();
            using var controller = Controller(view, new Local(), () => receiver, store);
            controller.Open(); Login(view, "first@dango.invalid");
            PumpUntil(() => view.FindName("AccountCloudChoice") is not null);
            controller.Close(); controller.Open();
            store.FailDelete = true;
            ButtonNamed(view, "退出登录").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => Field<TextBlock>(view, "AccountMessage").Text.Contains("尚未清除", StringComparison.Ordinal));
            Assert.IsFalse(controller.IsLoggedIn);
            controller.Close(); controller.Open();
            Assert.IsTrue(Field<TextBlock>(view, "AccountMessage").Text.Contains("尚未清除", StringComparison.Ordinal));
            store.FailDelete = false;
            var deletions = store.Deletions;
            ButtonNamed(view, "重试清除记住登录").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => Field<TextBlock>(view, "AccountMessage").Text.Contains("材料已清除", StringComparison.Ordinal));
            Assert.AreEqual(deletions + 1, store.Deletions);
            controller.Close(); controller.Open();
            Assert.IsFalse(Field<TextBlock>(view, "AccountMessage").Text.Contains("尚未清除", StringComparison.Ordinal));
        });
    }

    [STATestMethod]
    public void CloseDuringFailedLogoutCannotRestoreRememberedSessionAfterOldOperationDrains()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var store = new MemorySecrets();
            var old = new Receiver { DelayLogout = true };
            var next = new Receiver { UserId = old.UserId, PetId = old.PetId };
            var created = 0;
            using var controller = new DesktopAccountController(view, Origin, store,
                remote => new(remote, new Local(), new Journal()), new Local().CaptureAsync,
                () => { }, (_, _) => { }, (origin, secrets) =>
                {
                    var receiver = ++created == 1 ? old : next;
                    receiver.Cookies = new CookieContainer();
                    return FormalAccountClient.ForTesting(origin, receiver, receiver.Cookies, secrets);
                });
            controller.Open();
            Field<CheckBox>(view, "AccountRememberLogin").IsChecked = true;
            Login(view, "remembered@dango.invalid");
            PumpUntil(() => view.FindName("AccountCloudChoice") is not null);
            controller.Close(); controller.Open();
            store.FailDelete = true;
            ButtonNamed(view, "退出登录").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => old.LogoutStarted);
            controller.Close(); controller.Open();
            old.DelayedLogout.TrySetResult(Receiver.Json(new { status = "signed_out" }));
            PumpUntil(() => Field<Button>(view, "AccountPrimary").IsEnabled);
            Assert.IsFalse(controller.IsLoggedIn, "A failed remembered-secret cleanup must survive an obsolete UI generation.");
            Assert.AreEqual(0, next.IdentityReads, "Queued Open must recheck cleanup state after draining old Logout.");
            StringAssert.Contains(Field<TextBlock>(view, "AccountMessage").Text, "尚未清除");
        });
    }

    [STATestMethod]
    public void CancelledQueuedLogoutCannotRestoreRememberedSessionBeforeCleanupHasStarted()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var store = new MemorySecrets();
            var old = new Receiver { DelayPetReadNumber = 2 };
            var next = new Receiver { UserId = old.UserId, PetId = old.PetId }; var created = 0;
            using var controller = new DesktopAccountController(view, Origin, store,
                remote => new(remote, new Local(), new Journal()), new Local().CaptureAsync,
                () => { }, (_, _) => { }, (origin, secrets) =>
                {
                    var receiver = ++created == 1 ? old : next;
                    receiver.Cookies = new CookieContainer();
                    return FormalAccountClient.ForTesting(origin, receiver, receiver.Cookies, secrets);
                });
            controller.Open(); Field<CheckBox>(view, "AccountRememberLogin").IsChecked = true;
            Login(view, "remembered@dango.invalid");
            PumpUntil(() => old.PetReads == 2);
            Assert.IsTrue(controller.IsLoggedIn);
            controller.Close(); controller.Open();
            ButtonNamed(view, "退出登录").RaiseEvent(new(Button.ClickEvent));
            controller.Close(); controller.Open();
            old.DelayedPet.TrySetResult(Receiver.Json(new { userId = old.UserId, petId = old.PetId, configurationVersion = 1, configuration = Cream }));
            PumpUntil(() => Field<Button>(view, "AccountPrimary").IsEnabled);
            Assert.IsFalse(old.LogoutStarted, "Logout was cancelled while waiting for the old request to drain.");
            Assert.IsFalse(controller.IsLoggedIn, "Logout intent must block recovery before persistent cleanup begins.");
            Assert.AreEqual(0, next.IdentityReads);
            StringAssert.Contains(Field<TextBlock>(view, "AccountMessage").Text, "尚未清除");
        });
    }

    [STATestMethod]
    public void CompletedRegistrationAfterUnknownResponseUsesFirstPasswordAndDoesNotRecaptureAppearance()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var receiver = new Receiver { RegisterFlow = true }; var captures = 0;
            using var controller = new DesktopAccountController(view, Origin, new MemorySecrets(), _ => throw new AssertFailedException(),
                _ => { captures++; return Task.FromResult(new LocalPetConfigurationSnapshot(false, ReadOnlyMemory<byte>.Empty, White)); },
                () => { }, (_, _) => { }, (origin, store) => FormalAccountClient.ForTesting(origin, receiver, new CookieContainer(), store));
            controller.Open();
            ButtonNamed(view, "受邀注册").RaiseEvent(new(Button.ClickEvent));
            Field<TextBox>(view, "AccountEmail").Text = "invited@dango.invalid";
            Field<TextBox>(view, "AccountInvitationId").Text = Guid.NewGuid().ToString("D");
            Field<PasswordBox>(view, "AccountInvitationProof").Password = new string('A', 43);
            Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => AutomationProperties.GetName(Field<Button>(view, "AccountPrimary")) == "完成注册");
            Field<PasswordBox>(view, "AccountPassword").Password = "FirstFictional123!";
            Field<PasswordBox>(view, "AccountConfirmPassword").Password = "FirstFictional123!";
            Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => AutomationProperties.GetName(Field<Button>(view, "AccountPrimary")) == "刷新验证状态");
            Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => AutomationProperties.GetName(Field<Button>(view, "AccountPrimary")) == "登录");
            Assert.AreEqual(1, captures);
            Assert.AreEqual(1, receiver.Completions);
            Assert.IsTrue(Field<TextBlock>(view, "AccountMessage").Text.Contains("首次提交", StringComparison.Ordinal));
            Assert.IsFalse(controller.IsLoggedIn, "Completed registration is still followed by ordinary login.");
        });
    }

    [STATestMethod]
    [DataRow("smtp_accepted", "尚未验证")]
    [DataRow("smtp_not_configured", "没有发送")]
    [DataRow("smtp_permanent", "发送失败")]
    public void MailDeliveryStateNeverAllowsCompletionWithoutEmailVerification(string status, string expectedMessage)
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView();
            var receiver = new Receiver { RegisterFlow = true, RegistrationState = "pending", EmailStatus = status };
            using var controller = Controller(view, new Local(), () => receiver);
            controller.Open(); StartRegistration(view);
            PumpUntil(() => AutomationProperties.GetName(Field<Button>(view, "AccountPrimary")) == "刷新验证状态");
            StringAssert.Contains(Field<TextBlock>(view, "AccountMessage").Text, expectedMessage);
            Assert.IsFalse(Descendants(view).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "完成注册"));
            Assert.AreEqual(0, receiver.Completions);
            Assert.IsFalse(controller.IsLoggedIn);
        });
    }

    [STATestMethod]
    public void ExplicitlyRejectedRegistrationUsesANewRequestOnlyAfterConfirmedLocalCleanup()
    {
        WithDispatcher(() =>
        {
            var view = new AccountPanelView(); var store = new MemorySecrets();
            var receiver = new Receiver { RegisterFlow = true, RejectRegistration = true, RegistrationState = "pending" };
            using var controller = Controller(view, new Local(), () => receiver, store);
            controller.Open(); StartRegistration(view);
            PumpUntil(() => Field<TextBlock>(view, "AccountMessage").Text.Contains("明确拒绝", StringComparison.Ordinal));
            Assert.AreEqual(1, receiver.RegistrationKeys.Count);
            receiver.RejectRegistration = false;
            var deletes = store.Deletions;
            ButtonNamed(view, "确认清除旧继续信息，改用新邀请").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => Field<TextBlock>(view, "AccountMessage").Text.Contains("不会取消", StringComparison.Ordinal));
            Assert.AreEqual(deletes + 1, store.Deletions);
            Assert.AreEqual(1, receiver.RegistrationKeys.Count, "Local cleanup cannot create a new server request.");
            Field<PasswordBox>(view, "AccountInvitationProof").Password = new string('A', 43);
            Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => receiver.RegistrationKeys.Count == 2);
            Assert.AreNotEqual(receiver.RegistrationKeys[0], receiver.RegistrationKeys[1]);
            Assert.IsFalse(controller.IsLoggedIn);
        });
    }

    [STATestMethod]
    public void AccountOverlayKeepsBackgroundDisabledAcrossResizeAndCloseRestoresBackground()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.Measure(new Size(336, 640)); home.Arrange(new Rect(0, 0, 336, 640)); home.UpdateLayout();
        Field<Button>(home, "AccountButton").RaiseEvent(new(Button.ClickEvent));
        Assert.IsTrue(home.IsAccountPanelOpen);
        home.Measure(new Size(640, 640)); home.Arrange(new Rect(0, 0, 640, 640)); home.UpdateLayout();
        Assert.IsFalse(Field<Grid>(home, "TopNavigation").IsEnabled);
        Assert.IsFalse(Field<ScrollViewer>(home, "HomeContent").IsEnabled);
        Assert.IsTrue(Field<Border>(home, "AccountOverlay").ActualWidth <= home.ActualWidth);
        ButtonNamed(home.AccountPanel, "继续本地游玩").RaiseEvent(new(Button.ClickEvent));
        Assert.IsFalse(home.IsAccountPanelOpen);
        Assert.IsTrue(Field<ScrollViewer>(home, "HomeContent").IsEnabled);
    }

    [STATestMethod]
    [DataRow(1024, 640)]
    [DataRow(336, 640)]
    public void AccountPanelIsCenteredInsideResponsiveOverlay(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.Measure(new Size(width, height)); home.Arrange(new Rect(0, 0, width, height)); home.UpdateLayout();
        Field<Button>(home, "AccountButton").RaiseEvent(new(Button.ClickEvent));
        home.Measure(new Size(width, height)); home.Arrange(new Rect(0, 0, width, height)); home.UpdateLayout();
        var panel = home.AccountPanel;
        var bounds = panel.TransformToAncestor(home).TransformBounds(new Rect(panel.RenderSize));
        Assert.AreEqual(width / 2.0, bounds.Left + bounds.Width / 2, 1);
        Assert.AreEqual(height / 2.0, bounds.Top + bounds.Height / 2, 1);
        Assert.IsTrue(bounds.Left >= 16 && bounds.Right <= width - 16);
        Assert.IsTrue(bounds.Top >= 16 && bounds.Bottom <= height - 16);
        Assert.IsTrue(panel.ActualWidth <= 560);
        home.ApplyTheme(true);
        Assert.AreSame(home.Background, Field<Border>(home, "AccountOverlay").Background);
    }

    [STATestMethod]
    public void AccountFormKeysCannotOpenEditorsOrPorchAndEscapeClosesFromPasswordField()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.AccountRequested += () => home.AccountPanel.Apply(new(AccountPanelPage.Login));
        var window = new Window { Content = home, Width = 640, Height = 640, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            Field<Button>(home, "AccountButton").RaiseEvent(new(Button.ClickEvent));
            var password = Field<PasswordBox>(home.AccountPanel, "AccountPassword");
            password.Focus();
            var source = PresentationSource.FromVisual(window)!;
            foreach (var key in new[] { Key.A, Key.D, Key.W, Key.Space })
            {
                password.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.IsTrue(home.IsAccountPanelOpen);
                Assert.IsFalse(home.IsEditing);
                Assert.IsFalse(home.IsInPorch);
            }
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            password.RaiseEvent(escape);
            Assert.IsTrue(escape.Handled);
            Assert.IsFalse(home.IsAccountPanelOpen);
            Assert.IsTrue(Field<ScrollViewer>(home, "HomeContent").IsEnabled);
        }
        finally { window.Close(); }
    }

    private static DesktopAccountController Controller(AccountPanelView view, Local local, Func<Receiver> receiver, MemorySecrets? store = null) =>
        new(view, Origin, store ?? new MemorySecrets(), remote =>
        {
            local.ReviewUserId = remote.UserId;
            return new(remote, local, new Journal());
        }, local.CaptureAsync, () => { }, (_, _) => { },
        (origin, store) => FormalAccountClient.ForTesting(origin, receiver(), new CookieContainer(), store));

    private static void Login(AccountPanelView view, string email)
    {
        Field<TextBox>(view, "AccountEmail").Text = email;
        Field<PasswordBox>(view, "AccountPassword").Password = "FictionalOnly123!";
        Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
    }
    private static void StartRegistration(AccountPanelView view)
    {
        ButtonNamed(view, "受邀注册").RaiseEvent(new(Button.ClickEvent));
        Field<TextBox>(view, "AccountEmail").Text = "invited@dango.invalid";
        Field<TextBox>(view, "AccountInvitationId").Text = Guid.NewGuid().ToString("D");
        Field<PasswordBox>(view, "AccountInvitationProof").Password = new string('A', 43);
        Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent));
    }
    private static T Field<T>(FrameworkElement parent, string name) => (T)parent.FindName(name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static Button ButtonNamed(AccountPanelView view, string text)
    {
        view.Measure(new Size(560, 640)); view.Arrange(new Rect(0, 0, 560, 640)); view.UpdateLayout();
        return Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == text);
    }
    private static void WithDispatcher(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var timeout = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow > timeout) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Expected bounded UI transition did not complete.");
    }
    private static JsonElement Configuration(string body) => JsonSerializer.SerializeToElement(new
    { appearanceSchemaVersion = 3, bodyColorHex = body, eyeColorHex = "#272321", eyeSizePercent = 100, eyeOvalityPercent = 0, eyeHeightControlPercent = 51, eyeSpacingControlPercent = 24.5 });

    private sealed class Receiver : HttpMessageHandler
    {
        internal Guid UserId { get; init; } = Guid.NewGuid();
        internal Guid PetId { get; init; } = Guid.NewGuid();
        internal bool DelayLogin, LoginStarted;
        internal bool DelayLogout, LogoutStarted;
        internal int IdentityReads;
        internal int PetReads, DelayPetReadNumber;
        internal CookieContainer? Cookies;
        internal bool RegisterFlow;
        internal bool RejectRegistration;
        internal string RegistrationState = "verified", EmailStatus = "smtp_accepted";
        internal List<string> RegistrationKeys { get; } = [];
        internal int Completions;
        private readonly Guid registrationId = Guid.NewGuid();
        internal TaskCompletionSource<HttpResponseMessage> Delayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<HttpResponseMessage> DelayedLogout { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<HttpResponseMessage> DelayedPet { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (RegisterFlow && path.StartsWith("/api/v1/auth/registrations", StringComparison.Ordinal))
            {
                if (path == "/api/v1/auth/registrations")
                {
                    var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                    RegistrationKeys.Add(body.GetProperty("requestKey").GetString()!);
                    if (RejectRegistration) return new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { code = "invitation_invalid" }) };
                }
                if (path.EndsWith("/complete", StringComparison.Ordinal))
                {
                    Completions++;
                    throw new HttpRequestException("fictional interrupted response");
                }
                return Json(new { id = registrationId, state = Completions == 0 ? RegistrationState : "completed",
                    expiresAt = DateTimeOffset.UtcNow.AddHours(24), completionExpiresAt = RegistrationState == "pending" && Completions == 0
                        ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddHours(1), emailStatus = EmailStatus });
            }
            if (path == "/api/v1/auth/login")
            {
                LoginStarted = true;
                Cookies?.Add(Origin, new Cookie("__Host-Dango.Auth", "fictional_session", "/")
                    { Secure = true, HttpOnly = true, Expires = DateTime.UtcNow.AddHours(7) });
                return DelayLogin ? await Delayed.Task : Json(new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(7) });
            }
            if (path == "/api/v1/auth/logout")
            {
                LogoutStarted = true;
                return DelayLogout ? await DelayedLogout.Task : Json(new { status = "signed_out" });
            }
            if (path == "/api/v1/me") IdentityReads++;
            if (path == "/api/v1/me/pet" && ++PetReads == DelayPetReadNumber) return await DelayedPet.Task;
            return path switch
            {
                "/api/v1/auth/request-protection" => Json(new { requestToken = "fictional_request_token_for_test" }),
                "/api/v1/me" => Json(new { userId = UserId }),
                "/api/v1/me/pet" => Json(new { userId = UserId, petId = PetId, configurationVersion = 1, configuration = Cream }),
                "/api/v1/auth/logout" => Json(new { status = "signed_out" }),
                _ => throw new AssertFailedException("Unexpected request path")
            };
        }
        internal static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
    private sealed class MemorySecrets : IAccountSecretStore
    {
        private readonly Dictionary<string, byte[]> values = [];
        internal bool FailDelete;
        internal int Deletions;
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(values.TryGetValue(slot, out var value) ? value.ToArray() : null); }
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); values[slot] = value.ToArray(); return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Deletions++; if (FailDelete) throw new System.IO.IOException("fictional storage failure"); values.Remove(slot); return Task.CompletedTask; }
    }
    private sealed class Local : ILocalPetConfigurationRepository
    {
        internal JsonElement Current = White;
        internal int Applied;
        internal Guid ReviewUserId;
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(new LocalPetConfigurationSnapshot(false, ReadOnlyMemory<byte>.Empty, Current)); }
        public Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Current = configuration.Clone(); Applied++; return Task.CompletedTask; }
    }
    private sealed class Journal : IPetConfigurationJournal
    {
        public Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct) => throw new AssertFailedException();
        public Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct) => Task.CompletedTask;
    }
}
