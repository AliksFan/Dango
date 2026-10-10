using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

/// <summary>
/// Offline composition checks: the actual application host, appearance writer and file journals are used.
/// No Host.Start, native initialization, visible window, HTTPS listener, database or OS credential access.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HostAccountBindingTests
{
    private static readonly Uri Origin = new("https://127.0.0.1:54443/");
    private static readonly JsonElement Cream = Configuration("#FFE8C2");
    private static readonly JsonElement White = Configuration("#FFFFFF");

    [TestMethod]
    public void DependenciesRejectMissingInputsAndOriginsOutsideTheExplicitLoopbackBoundary()
    {
        var store = new MemorySecrets();
        Func<Uri, IAccountSecretStore, FormalAccountClient> factory = (_, _) => throw new AssertFailedException("No client construction is allowed.");
        Assert.ThrowsExactly<ArgumentNullException>(() => new DangoAccountDependencies(null!, store, factory));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DangoAccountDependencies(Origin, null!, factory));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DangoAccountDependencies(Origin, store, null!));
        foreach (var address in new[] { "relative", "http://127.0.0.1/", "https://localhost/", "https://[::1]/",
                     "https://outside.dango.invalid/", "https://fixture@127.0.0.1/", "https://127.0.0.1:0/", "https://127.0.0.1/path",
                     "https://127.0.0.1/?fixture=1", "https://127.0.0.1/#fixture" })
            Assert.ThrowsExactly<ArgumentException>(() => new DangoAccountDependencies(new Uri(address, UriKind.RelativeOrAbsolute), store, factory));
        var accepted = new DangoAccountDependencies(Origin, store, factory);
        Assert.AreEqual(Origin, accepted.Origin);
        Assert.AreSame(store, accepted.SecretStore);
        Assert.AreSame(factory, accepted.CreateClient);
    }

    [STATestMethod]
    public void IntegrationFactoryRejectsIncompleteCompositionBeforeCreatingAnyHost()
    {
        var root = FixtureDirectory();
        var settings = Path.Combine(root, "settings.json");
        var native = Path.Combine(root, "never-load.missing.dll");
        var dependencies = new DangoAccountDependencies(Origin, new MemorySecrets(), (_, _) => throw new AssertFailedException("No client construction is allowed."));
        Assert.ThrowsExactly<ArgumentNullException>(() => DangoApplicationHost.CreateForAccountIntegration(settings, native, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => DangoApplicationHost.CreateForAccountIntegration(null!, native, dependencies));
        Assert.ThrowsExactly<ArgumentNullException>(() => DangoApplicationHost.CreateForAccountIntegration(settings, null!, dependencies));
        Assert.ThrowsExactly<ArgumentException>(() => DangoApplicationHost.CreateForAccountIntegration("", native, dependencies));
        Assert.ThrowsExactly<ArgumentException>(() => DangoApplicationHost.CreateForAccountIntegration(settings, " ", dependencies));
        Assert.ThrowsExactly<ArgumentException>(() => DangoApplicationHost.CreateForAccountIntegration("relative-settings.json", native, dependencies));
        Assert.ThrowsExactly<ArgumentException>(() => DangoApplicationHost.CreateForAccountIntegration(settings, "relative-native.dll", dependencies));
        Assert.IsFalse(File.Exists(settings), "Rejected composition must not create application settings.");
        Assert.IsFalse(File.Exists(Path.Combine(root, "appearance.json")));
    }

    [STATestMethod]
    public void DefaultHostDoesNotInheritAnotherHostsTestBindingOrReadCredentialsWithoutAnOrigin()
    {
        using var environment = new OriginEnvironment(null);
        using var injected = new Rig(new Receiver(), Cream);
        var calls = injected.FactoryCalls;
        var root = FixtureDirectory();
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"),
            nativeLibraryPath: Path.Combine(root, "never-load.missing.dll"));
        var window = ControlCenter(host);
        Assert.IsFalse(window.IsVisible);
        Assert.IsNull(Field<object?>(host, "accountDependencies"));
        var controller = Field<DesktopAccountController>(host, "accountController");
        Assert.IsNull(Field<Uri?>(controller, "origin"));
        Assert.IsInstanceOfType<WindowsAccountSecretStore>(Field<IAccountSecretStore>(controller, "secrets"));
        Assert.AreNotSame(injected.Dependencies.CreateClient,
            Field<Func<Uri, IAccountSecretStore, FormalAccountClient>>(controller, "createClient"));
        controller.Open();
        Assert.IsNull(Field<FormalAccountClient?>(controller, "client"), "An unavailable origin must not create a client capable of reading WinCred.");
        Assert.IsFalse(((Button)window.ProductHome.AccountPanel.FindName("AccountPrimary")).IsEnabled);
        Assert.IsTrue(((TextBlock)window.ProductHome.AccountPanel.FindName("AccountMessage")).Text.Contains("尚未配置", StringComparison.Ordinal));
        Assert.AreEqual(calls, injected.FactoryCalls, "Internal test dependencies must remain per-host.");
    }

    [STATestMethod]
    public void ExplicitDependenciesOverrideExternalOriginAndRequireTheFirstNicknameBeforeConfiguration()
    {
        using var environment = new OriginEnvironment("https://outside.dango.invalid/");
        using var rig = new Rig(new Receiver { Nickname = null, Cloud = White }, White);
        rig.Login(remember: true);
        rig.Wait(() => rig.Controller.IsLoggedIn && rig.PrimaryEnabled && rig.PrimaryName == "保存昵称");
        Assert.IsNull(rig.Controller.Nickname);
        Assert.IsNull(rig.Controller.RoomClient);
        Assert.IsFalse(rig.Controller.ConfigurationReady);
        Assert.AreEqual(0, rig.Receiver.ProfileWrites);
        Assert.AreEqual(1, rig.Receiver.PetReads, "Login may resolve the pet identity; a missing nickname must prevent the additional configuration comparison.");
        ((TextBox)rig.Home.AccountPanel.FindName("AccountNickname")).Text = "  接线团子  ";
        rig.ClickPrimary();
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        Assert.AreEqual("接线团子", rig.Controller.Nickname);
        Assert.AreEqual("接线团子", AutomationProperties.GetName((Button)rig.Home.FindName("AccountButton")));
        Assert.AreEqual(1, rig.Receiver.ProfileWrites);
        Assert.AreEqual(0, rig.Receiver.ConfigurationWrites);
        Assert.AreEqual(rig.Receiver.PetId, rig.Controller.RoomClient!.PetId);
        Assert.IsTrue(rig.FactoryCalls > 0);
        Assert.IsTrue(rig.Secrets.Reads > 0);
        Assert.IsTrue(rig.Secrets.Writes > 0, "Remember login must write to the injected memory store.");
        Assert.IsTrue(rig.Secrets.Deletes > 0);
        Assert.IsTrue(rig.Secrets.Origins.All(origin => origin == Origin));
        Assert.IsTrue(rig.Receiver.Requests.All(request => request.Origin == Origin));
        Assert.IsFalse(Directory.Exists(rig.JournalDirectory("configuration-choices")), "Equal login configurations need no archive or choice write.");
        Assert.AreEqual(White.GetProperty("bodyColorHex").GetString(), rig.Appearance.SavedBodyColor.ToHex());
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExplicitConfigurationChoiceKeepsPetIdentityAndPersistsBothOriginalsAndReceipt(bool chooseLocal)
    {
        using var environment = new OriginEnvironment(null);
        using var rig = new Rig(new Receiver(), White);
        var originalBytes = File.ReadAllBytes(rig.AppearancePath);
        rig.Login();
        rig.Wait(() => rig.Home.AccountPanel.FindName("AccountCloudChoice") is not null);
        Assert.IsFalse(rig.Controller.ConfigurationReady);
        Assert.AreEqual(0, rig.Receiver.ConfigurationWrites);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(rig.AppearancePath), "Login comparison cannot overwrite the local file.");
        ((RadioButton)rig.Home.AccountPanel.FindName(chooseLocal ? "AccountLocalChoice" : "AccountCloudChoice")).IsChecked = true;
        rig.ClickPrimary();
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        var expected = chooseLocal ? White : Cream;
        Assert.IsTrue(AppearanceContract.Equivalent(expected, rig.ReadDiskConfiguration()));
        Assert.IsTrue(AppearanceContract.Equivalent(expected, rig.Receiver.Cloud));
        Assert.AreEqual(chooseLocal ? 1 : 0, rig.Receiver.ConfigurationWrites);
        Assert.AreEqual(rig.Receiver.PetId, rig.Controller.RoomClient!.PetId);
        var directory = rig.JournalDirectory("configuration-choices");
        var id = rig.SingleArchiveId(directory);
        var journal = new PetConfigurationFileJournal(directory);
        var originals = rig.Complete(journal.LoadOriginalsAsync(id, CancellationToken.None));
        var receipt = rig.Complete(journal.LoadReceiptAsync(id));
        Assert.AreEqual(Origin, originals.Origin);
        Assert.IsTrue(originals.Local.Exists);
        CollectionAssert.AreEqual(originalBytes, originals.Local.OriginalBytes.ToArray());
        Assert.IsTrue(AppearanceContract.Equivalent(White, originals.Local.NormalizedConfiguration));
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, originals.Cloud.Configuration));
        Assert.AreEqual(rig.Receiver.UserId, originals.Cloud.UserId);
        Assert.AreEqual(rig.Receiver.PetId, originals.Cloud.PetId);
        Assert.IsNotNull(receipt);
        Assert.AreEqual(chooseLocal ? PetConfigurationChoice.Local : PetConfigurationChoice.Cloud, receipt.Choice);
        Assert.AreEqual(chooseLocal ? PetChoiceOutcome.CloudUpdated : PetChoiceOutcome.LocalApplied, receipt.Outcome);
        Assert.IsNull(receipt.FailureCode);
        Assert.AreEqual(rig.Receiver.ConfigurationVersion, receipt.ObservedCloudVersion);
        Assert.AreEqual(2, Directory.GetFiles(directory).Length, "The real journal must contain one durable originals/receipt pair.");
    }

    [STATestMethod]
    public void ActualAppearanceSavePublishesThroughHostAndRemoteTimerPersistsWithoutEchoing()
    {
        using var environment = new OriginEnvironment(null);
        using var rig = new Rig(new Receiver(), Cream);
        rig.Login();
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        rig.Home.HideAccountPanel();
        Assert.IsFalse(rig.Home.IsAccountPanelOpen);
        var savedEvents = 0;
        rig.Appearance.SavedToDisk += () => savedEvents++;
        rig.Appearance.StageBodyPreview(OpaqueSrgbColor.Parse("#FFFFFF"));
        Assert.IsTrue(rig.Appearance.TrySave());
        Assert.AreEqual(1, savedEvents);
        Assert.IsTrue(AppearanceContract.Equivalent(White, rig.ReadDiskConfiguration()));
        // No NotifyAppearanceSaved or manual PumpConfigurationAsync call: this exercises the host event subscription.
        rig.Wait(() => rig.Receiver.ConfigurationWrites == 1 && AppearanceContract.Equivalent(White, rig.Receiver.Cloud)
            && rig.ReceiptCount("configuration-sync") == 1);
        var localUploadBytes = File.ReadAllBytes(rig.AppearancePath);
        var directory = rig.JournalDirectory("configuration-sync");
        var uploadId = rig.SingleArchiveId(directory);
        var journal = new PetConfigurationFileJournal(directory);
        var uploadOriginals = rig.Complete(journal.LoadOriginalsAsync(uploadId, CancellationToken.None));
        var uploadReceipt = rig.Complete(journal.LoadReceiptAsync(uploadId));
        CollectionAssert.AreEqual(localUploadBytes, uploadOriginals.Local.OriginalBytes.ToArray());
        Assert.IsTrue(AppearanceContract.Equivalent(White, uploadOriginals.Local.NormalizedConfiguration));
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, uploadOriginals.Cloud.Configuration));
        Assert.IsNotNull(uploadReceipt);
        Assert.AreEqual(PetConfigurationChoice.Local, uploadReceipt.Choice);
        Assert.AreEqual(PetChoiceOutcome.CloudUpdated, uploadReceipt.Outcome);
        var reads = rig.Receiver.PetReads;
        rig.Receiver.Cloud = Cream;
        rig.Receiver.ConfigurationVersion++;
        // The real dispatcher timer must see the remote version while the account panel remains closed.
        rig.Wait(() => AppearanceContract.Equivalent(Cream, rig.ReadDiskConfiguration())
            && rig.Appearance.SavedBodyColor.ToHex() == "#FFE8C2" && rig.ReceiptCount("configuration-sync") == 2);
        rig.Wait(() => rig.Receiver.PetReads >= reads + 2);
        Assert.AreEqual(1, rig.Receiver.ConfigurationWrites, "Remote application must not upload an echo during the next poll.");
        Assert.AreEqual(1, savedEvents, "Remote Apply must use AcceptExternalSave and cannot emit a local-save notification.");
        Assert.IsFalse(rig.Home.IsAccountPanelOpen);
        Assert.AreEqual(rig.Receiver.PetId, rig.Controller.RoomClient!.PetId);
        var remoteId = rig.ArchiveIds(directory).Single(id => id != uploadId);
        var remoteOriginals = rig.Complete(journal.LoadOriginalsAsync(remoteId, CancellationToken.None));
        var remoteReceipt = rig.Complete(journal.LoadReceiptAsync(remoteId));
        CollectionAssert.AreEqual(localUploadBytes, remoteOriginals.Local.OriginalBytes.ToArray());
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, remoteOriginals.Cloud.Configuration));
        Assert.IsNotNull(remoteReceipt);
        Assert.AreEqual(PetConfigurationChoice.Cloud, remoteReceipt.Choice);
        Assert.AreEqual(PetChoiceOutcome.LocalApplied, remoteReceipt.Outcome);
        Assert.AreEqual(4, Directory.GetFiles(directory).Length);
    }

    private sealed class Rig : IDisposable
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        internal readonly Receiver Receiver;
        internal readonly MemorySecrets Secrets = new();
        internal readonly DangoAccountDependencies Dependencies;
        internal readonly DangoApplicationHost Host;
        internal readonly ControlCenterWindow Window;
        internal readonly ProductHomeView Home;
        internal readonly DesktopAccountController Controller;
        internal readonly AppearanceSettingsSession Appearance;
        internal readonly string SettingsPath;
        internal readonly string AppearancePath;
        internal int FactoryCalls;
        internal bool PrimaryEnabled => ((Button)Home.AccountPanel.FindName("AccountPrimary")).IsEnabled;
        internal string PrimaryName => AutomationProperties.GetName((Button)Home.AccountPanel.FindName("AccountPrimary"));

        internal Rig(Receiver receiver, JsonElement local)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Receiver = receiver;
            var root = FixtureDirectory();
            SettingsPath = Path.Combine(root, "settings.json");
            AppearancePath = Path.Combine(root, "appearance.json");
            new AppearanceSettingsStore().Save(AppearancePath, new DangoAppearanceConfiguration(
                OpaqueSrgbColor.Parse(local.GetProperty("bodyColorHex").GetString()!), OpaqueSrgbColor.Parse("#272321"), EyeLayoutParameters.Default));
            Dependencies = new(Origin, Secrets, (origin, store) =>
            {
                FactoryCalls++;
                Assert.AreEqual(Origin, origin);
                Assert.AreSame(Secrets, store);
                Receiver.Cookies = new CookieContainer();
                return FormalAccountClient.ForTesting(origin, Receiver, Receiver.Cookies, store);
            });
            Host = DangoApplicationHost.CreateForAccountIntegration(SettingsPath, Path.Combine(root, "never-load.missing.dll"), Dependencies);
            try
            {
                Window = ControlCenter(Host);
                Home = Window.ProductHome;
                Controller = Field<DesktopAccountController>(Host, "accountController");
                Appearance = Field<AppearanceSettingsSession>(Host, "appearance");
                Assert.IsFalse(Window.IsVisible);
                Assert.AreSame(Dependencies, Field<DangoAccountDependencies>(Host, "accountDependencies"));
                Assert.AreSame(Secrets, Field<IAccountSecretStore>(Controller, "secrets"));
                Assert.AreSame(Dependencies.CreateClient, Field<Func<Uri, IAccountSecretStore, FormalAccountClient>>(Controller, "createClient"));
                Wait(() => PrimaryEnabled);
                // These checks precede every Login. A broken factory cannot fall through to network login.
                Assert.IsTrue(FactoryCalls > 0, "Remembered initialization must construct the injected controlled client before any login.");
                Assert.IsTrue(Secrets.Reads > 0, "Remembered initialization must read the injected memory store before any login.");
            }
            catch { Host.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); throw; }
        }

        internal void Login(bool remember = false)
        {
            ((Button)Home.FindName("AccountButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => Home.IsAccountPanelOpen && PrimaryEnabled);
            ((TextBox)Home.AccountPanel.FindName("AccountEmail")).Text = "host-binding@dango.invalid";
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = "fictional_host_binding_password";
            ((CheckBox)Home.AccountPanel.FindName("AccountRememberLogin")).IsChecked = remember;
            ClickPrimary();
        }

        internal void ClickPrimary() => ((Button)Home.AccountPanel.FindName("AccountPrimary")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        internal string JournalDirectory(string name) => Path.Combine(new DangoUserDataPaths(SettingsPath)
            .GetAccountDirectory(Origin, Receiver.UserId, Receiver.PetId), name);
        internal JsonElement ReadDiskConfiguration()
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(AppearancePath));
            return AppearanceContract.ValidateAndClone(document.RootElement);
        }
        internal int ReceiptCount(string name) => Directory.Exists(JournalDirectory(name))
            ? Directory.GetFiles(JournalDirectory(name), "*.receipt.json").Length : 0;
        internal Guid[] ArchiveIds(string directory) => Directory.GetFiles(directory, "*.originals.json")
            .Select(path => Guid.ParseExact(Path.GetFileName(path).Split('.')[0], "N")).ToArray();
        internal Guid SingleArchiveId(string directory) => ArchiveIds(directory).Single();
        internal T Complete<T>(Task<T> task) { Wait(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
        internal void Wait(Func<bool> condition)
        {
            if (condition()) return;
            var frame = new DispatcherFrame();
            var watch = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (condition() || watch.Elapsed > TimeSpan.FromSeconds(8)) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.IsTrue(condition(), "The bounded host account transition did not complete.");
        }

        public void Dispose()
        {
            Host.Dispose();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class Receiver : HttpMessageHandler
    {
        internal readonly Guid UserId = Guid.NewGuid();
        internal readonly Guid PetId = Guid.NewGuid();
        internal CookieContainer Cookies = new();
        internal string? Nickname = "号主团子";
        internal JsonElement Cloud = Cream;
        internal long ProfileVersion = 1, ConfigurationVersion = 1;
        internal int ProfileWrites, ConfigurationWrites, PetReads;
        internal readonly List<(Uri Origin, string Method, string Path)> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            var actualOrigin = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            Assert.AreEqual(Origin, actualOrigin, "Controlled transport must reject an unexpected origin.");
            var path = uri.AbsolutePath;
            Requests.Add((actualOrigin, request.Method.Method, path));
            if (path == "/api/v1/auth/login")
            {
                Cookies.Add(Origin, new Cookie("__Host-Dango.Auth", "fictional_host_binding_session", "/")
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
            if (path == "/api/v1/me/pet") PetReads++;
            return path switch
            {
                "/api/v1/auth/request-protection" => Json(new { requestToken = "fictional_host_binding_request_protection" }),
                "/api/v1/me" => Json(new { userId = UserId }),
                "/api/v1/me/profile" => Json(new { userId = UserId, nickname = Nickname, profileVersion = ProfileVersion }),
                "/api/v1/me/pet" => Json(new { userId = UserId, petId = PetId, configurationVersion = ConfigurationVersion, configuration = Cloud }),
                _ => throw new AssertFailedException("Unexpected offline host-binding request.")
            };
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class MemorySecrets : IAccountSecretStore
    {
        private readonly Dictionary<string, byte[]> values = [];
        internal readonly List<Uri> Origins = [];
        internal int Reads, Writes, Deletes;
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Origins.Add(origin); Reads++; return Task.FromResult(values.TryGetValue(slot, out var value) ? value.ToArray() : null); }
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Origins.Add(origin); Writes++; values[slot] = value.ToArray(); return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Origins.Add(origin); Deletes++; values.Remove(slot); return Task.CompletedTask; }
    }

    private sealed class OriginEnvironment : IDisposable
    {
        private readonly string? previous = Environment.GetEnvironmentVariable("DANGO_SERVER_ORIGIN");
        internal OriginEnvironment(string? value) => Environment.SetEnvironmentVariable("DANGO_SERVER_ORIGIN", value);
        public void Dispose() => Environment.SetEnvironmentVariable("DANGO_SERVER_ORIGIN", previous);
    }

    private static ControlCenterWindow ControlCenter(DangoApplicationHost host) =>
        (ControlCenterWindow)typeof(DangoApplicationHost).GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static string FixtureDirectory()
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "host-account-binding", "20261010-wiring", "green", "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
    private static JsonElement Configuration(string body) => JsonSerializer.SerializeToElement(new
    { appearanceSchemaVersion = 3, bodyColorHex = body, eyeColorHex = "#272321", eyeSizePercent = 100, eyeOvalityPercent = 0, eyeHeightControlPercent = 51, eyeSpacingControlPercent = 24.5 });
}
