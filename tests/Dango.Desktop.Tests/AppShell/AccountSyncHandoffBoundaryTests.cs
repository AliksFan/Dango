using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class AccountSyncHandoffBoundaryTests
{
    private static readonly Uri Origin = new("https://review.dango.invalid/");
    private static readonly JsonElement Cream = Configuration("#FFE8C2");
    private static readonly JsonElement White = Configuration("#FFFFFF");

    [STATestMethod]
    public void SuccessfulLocalSaveDuringPreviousPumpDrainRequiresFreshLoginComparison()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var view = new AccountPanelView(); var local = new Local(); var secrets = new Secrets();
            var old = new Receiver(); var next = new Receiver(); var created = 0;
            using var controller = new DesktopAccountController(view, Origin, secrets,
                remote => new(remote, local, new Journal()), local.CaptureAsync, () => { }, (_, _) => { },
                (origin, store) => FormalAccountClient.ForTesting(origin, ++created == 1 ? old : next, new CookieContainer(), store),
                createSync: (remote, baseline) => new(remote, local, new Journal(), baseline));
            controller.Open(); PumpUntil(() => Field<Button>(view, "AccountPrimary").IsEnabled);
            Login(view, "old@review.invalid"); PumpUntil(() => controller.ConfigurationReady && !Private<bool>(controller, "busy"));
            old.DelayNextPetRead = true;
            var stalePump = controller.PumpConfigurationAsync();
            PumpUntil(() => old.DelayedReadStarted);
            Require(!stalePump.IsCompleted, "Old GET must still be blocked.");
            ButtonNamed(view, "退出登录").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => !controller.IsLoggedIn);
            // The real FormalAccountClient gate keeps Logout behind the blocked GET. Closing cancels that wait.
            controller.Close();
            PumpUntil(() => Private<SemaphoreSlim>(controller, "operations").CurrentCount == 1);
            controller.Open();
            ButtonNamed(view, "重试清除记住登录").RaiseEvent(new(Button.ClickEvent));
            PumpUntil(() => !Private<bool>(controller, "busy"));
            Login(view, "next@review.invalid");
            PumpUntil(() => next.ProfileReads == 1 && controller.IsLoggedIn && Private<bool>(controller, "busy"));
            PumpFor(TimeSpan.FromMilliseconds(100));
            Require(!stalePump.IsCompleted && !controller.ConfigurationReady,
                "New login must still be awaiting the previous pump.");
            Require(Private<PetConfigurationReview?>(controller, "review") is null,
                "No stale configuration source choice may be published while the previous pump drains.");
            Require(AppearanceContract.Equivalent(Cream, local.Current), "The offline save starts from the previously confirmed baseline.");
            local.Current = White; controller.NotifyAppearanceSaved();
            old.DelayedPet.TrySetResult(old.Pet());
            PumpUntil(() => !Private<bool>(controller, "busy"));
            var pending = controller.PumpConfigurationAsync(); PumpUntil(() => pending.IsCompleted);
            var choice = view.FindName("AccountCloudChoice") is not null;
            var preserved = AppearanceContract.Equivalent(White, local.Current);
            Require(preserved, "A successful local save during old-pump drain was overwritten by the new session's cloud baseline.");
            Assert.IsTrue(choice, "Login must compare the newly saved local configuration after draining the previous pump.");
            Assert.IsFalse(controller.ConfigurationReady);
            Assert.AreEqual(0, local.Applied);
            Assert.AreEqual(0, next.ConfigurationWrites);
            Assert.AreEqual(0, old.LogoutRequests, "The old client gate must prevent logout from bypassing its delayed GET.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static T Field<T>(FrameworkElement view, string name) => (T)view.FindName(name);
    private static T Private<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Login(AccountPanelView view, string email)
    { Field<TextBox>(view, "AccountEmail").Text = email; Field<PasswordBox>(view, "AccountPassword").Password = "FictionalOnly123!"; Field<Button>(view, "AccountPrimary").RaiseEvent(new(Button.ClickEvent)); }
    private static Button ButtonNamed(AccountPanelView view, string text)
    {
        view.Measure(new Size(560, 640)); view.Arrange(new Rect(0, 0, 560, 640)); view.UpdateLayout();
        return Descendants(view).OfType<Button>().Single(item => AutomationProperties.GetName(item) == text);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var timeout = DateTime.UtcNow.AddSeconds(8);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow > timeout) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(condition(), "Expected bounded offscreen transition did not complete.");
    }
    private static void Require(bool condition, string message) => Assert.IsTrue(condition, message);
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private static JsonElement Configuration(string color) => JsonSerializer.SerializeToElement(new
    { appearanceSchemaVersion = 3, bodyColorHex = color, eyeColorHex = "#272321", eyeSizePercent = 100, eyeOvalityPercent = 0, eyeHeightControlPercent = 51, eyeSpacingControlPercent = 24.5 });

    private sealed class Receiver : HttpMessageHandler
    {
        internal Guid UserId = Guid.NewGuid(), PetId = Guid.NewGuid();
        internal bool DelayNextPetRead, DelayedReadStarted;
        internal int PetReads, ProfileReads, ConfigurationWrites, LogoutRequests;
        internal JsonElement CloudConfiguration = Cream;
        internal long ConfigurationVersion = 1;
        internal TaskCompletionSource<HttpResponseMessage> DelayedPet = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/me/profile") ProfileReads++;
            if (path == "/api/v1/me/pet")
            {
                PetReads++;
                if (DelayNextPetRead) { DelayNextPetRead = false; DelayedReadStarted = true; return await DelayedPet.Task; }
                return Pet();
            }
            if (path == "/api/v1/me/pet/configuration")
            {
                ConfigurationWrites++; var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                if (body.GetProperty("expectedVersion").GetInt64() != ConfigurationVersion)
                    return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { code = "pet_configuration_conflict" }) };
                CloudConfiguration = body.GetProperty("configuration").Clone(); ConfigurationVersion++; return Pet();
            }
            if (path == "/api/v1/auth/logout") { LogoutRequests++; return Json(new { status = "signed_out" }); }
            return path switch
            {
                "/api/v1/auth/request-protection" => Json(new { requestToken = "fictional_request_token_for_test" }),
                "/api/v1/auth/login" => Json(new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(7) }),
                "/api/v1/me" => Json(new { userId = UserId }),
                "/api/v1/me/profile" => Json(new { userId = UserId, nickname = "测试昵称", profileVersion = 1 }),
                _ => throw new Exception($"Unexpected fictional request: {path}")
            };
        }
        internal HttpResponseMessage Pet() => Json(new { userId = UserId, petId = PetId, configurationVersion = ConfigurationVersion, configuration = CloudConfiguration });
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
    private sealed class Local : ILocalPetConfigurationRepository
    {
        internal JsonElement Current = Cream;
        internal int Applied;
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(new LocalPetConfigurationSnapshot(false, ReadOnlyMemory<byte>.Empty, Current)); }
        public async Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Require(expected.SameOriginal(await CaptureAsync(ct)), "CAS must preserve intervening saves."); Current = configuration.Clone(); Applied++; }
    }
    private sealed class Journal : IPetConfigurationJournal
    {
        public Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct) => throw new Exception("Not used");
        public Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Secrets : IAccountSecretStore
    {
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task WriteAsync(Uri origin, string slot, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct) => Task.CompletedTask;
    }
}


