using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Dango.AccountSyncReview.Tests;
internal sealed record AccountSyncB1cArchive(Guid Id, string Directory);
internal sealed class AccountSyncB1cMemorySecrets(Uri origin) : IAccountSecretStore, IDisposable
{
    private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
    private bool disposed;
    internal int Deletes;
    internal bool IsDisposed { get { lock (values) return disposed && values.Count == 0; } }
    public Task<byte[]?> ReadAsync(Uri requested, string slot, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Assert.AreEqual(origin, requested); lock (values) { ObjectDisposedException.ThrowIf(disposed, this); return Task.FromResult(values.TryGetValue(slot, out var bytes) ? bytes.ToArray() : null); } }
    public Task WriteAsync(Uri requested, string slot, byte[] value, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Assert.AreEqual(origin, requested); lock (values) { ObjectDisposedException.ThrowIf(disposed, this); Delete(slot); values[slot] = value.ToArray(); } return Task.CompletedTask; }
    public Task DeleteAsync(Uri requested, string slot, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Assert.AreEqual(origin, requested); lock (values) { ObjectDisposedException.ThrowIf(disposed, this); Deletes++; Delete(slot); } return Task.CompletedTask; }
    private void Delete(string slot) { if (values.Remove(slot, out var bytes)) CryptographicOperations.ZeroMemory(bytes); }
    public void Dispose() { lock (values) { if (disposed) return; disposed = true; foreach (var bytes in values.Values) CryptographicOperations.ZeroMemory(bytes); values.Clear(); } }
}

    internal sealed class AccountSyncB1cDesktopRig
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        private readonly string name;
        private readonly DangoUserDataPaths paths;
        private readonly List<object> transitions = [];
        private readonly HashSet<Task> pumps = [];
        private readonly HashSet<Task> localTasks = [];
        private readonly List<Task> uiaTasks = [];
        private readonly HashSet<PetConfigurationLiveSyncCoordinator> coordinators = [];
        private DangoApplicationHost? host;
        private ControlCenterWindow? window;
        private DesktopAccountController? controller;
        private AppearanceSettingsSession? appearance;
        private StackedDangoSceneRuntime? runtime;
        private nint handle;
        private Task? pendingUia;
        private volatile bool finishing;
        private volatile bool independentUiaObserved;
        private bool finished;
        private bool hostStarted, nativeHashVerified, healthyFrameObserved;
        internal AccountSyncB1cHttpsFixture Fixture { get; }
        private readonly int index;
        private Uri Origin => Fixture.Endpoint;
        internal AccountSyncB1cMemorySecrets Secrets { get; }
        internal ProductHomeView Home => window!.ProductHome;
        internal DesktopAccountController Controller => controller ?? throw new AssertFailedException("The production account controller is required.");
        internal DispatcherTimer ConfigurationTimer => Field<DispatcherTimer>(Controller, "configurationTimer");
        internal Task CurrentPump { get { RememberPump(); return Field<Task>(Controller, "configurationPump"); } }
        internal int SuccessfulSaveEvents { get; private set; }
        internal AccountSyncAppearanceNavigator Navigator { get; private set; } = null!;
        internal bool PrimaryEnabled => ((Button)Home.AccountPanel.FindName("AccountPrimary")).IsEnabled;
        internal string PrimaryName => AutomationProperties.GetName((Button)Home.AccountPanel.FindName("AccountPrimary"));
        internal bool HasChoice => Home.AccountPanel.FindName("AccountCloudChoice") is RadioButton;

        internal AccountSyncB1cDesktopRig(string name, int index, AccountSyncB1cTestOwner owner, AccountSyncB1cHttpsFixture fixture)
        {
            this.name = name; this.index = index; Fixture = fixture; Secrets = new(fixture.Endpoint);
            var root = owner.RunDirectory;
            var directory = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(Directory.Exists(directory)); Directory.CreateDirectory(directory);
            paths = new(Path.Combine(directory, "settings.json"));
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                var native = Path.Combine(AccountSyncB1cHttpsFixture.RepositoryRoot(), AccountSyncB1cEvidenceContract.NativeRelativePath);
                Assert.IsTrue(File.Exists(native), "The existing isolated native candidate is required; no fake-scene fallback is allowed.");
                Assert.AreEqual(AccountSyncB1cEvidenceContract.NativeSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))));
                nativeHashVerified = true;
                new AppearanceSettingsStore().Save(paths.AppearancePath, new DangoAppearanceConfiguration(
                    OpaqueSrgbColor.Parse("#FFE8C2"), OpaqueSrgbColor.Parse("#272321"), new EyeLayoutParameters(103)));
                var dependencies = new DangoAccountDependencies(Origin, Secrets, (origin, store) =>
                {
                    Assert.AreEqual(Origin, origin); Assert.AreSame(Secrets, store);
                    return fixture.CreateDesktopClient(index, store);
                });
                host = DangoApplicationHost.CreateForAccountIntegration(paths.SettingsPath, native, dependencies);
                runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
                fixture.Resource("DesktopStarted", new { Index = index, Attempt = true });
                host.Start();
                hostStarted = true;
                window = Field<ControlCenterWindow>(host, "controlCenterWindow");
                controller = Field<DesktopAccountController>(host, "accountController");
                appearance = Field<AppearanceSettingsSession>(host, "appearance");
                appearance.SavedToDisk += OnSaved;
                window.Left = 80; window.Top = 80; window.ShowInTaskbar = false;
                handle = new WindowInteropHelper(window).Handle; Assert.AreNotEqual(nint.Zero, handle);
                Uia(client => client.AssertOwnedWindow());
                Assert.AreSame(appearance, Home.BoundAppearance);
                Assert.AreSame(Secrets, Field<IAccountSecretStore>(Controller, "secrets"));
                Assert.AreEqual(TimeSpan.FromSeconds(2), ConfigurationTimer.Interval);
                Navigator = new(id => Uia(client => client.InvokeId(id)),
                    (id, value) => Uia(client => client.Range(id, value)), Wait, ReadNavigation);
                Wait(() => PrimaryEnabled && !Field<bool>(Controller, "busy"));
                AssertHealthyFrame(); Mark("host-ready");
            }
            catch { Finish(false); throw; }
        }

        private void OnSaved() => SuccessfulSaveEvents++;
        internal void SaveEyeSize(double value)
        {
            var before = SuccessfulSaveEvents;
            Navigator.SaveEyeSize(value);
            Wait(() => SuccessfulSaveEvents == before + 1);
            Assert.AreEqual(before + 1, SuccessfulSaveEvents, "Each real Apply must publish the production SavedToDisk event exactly once.");
        }
        internal void Login(AccountSyncB1cActor account)
        {
            if (!Home.IsAccountPanelOpen) OpenAccount();
            Wait(() => PrimaryEnabled && PrimaryName == "登录");
            Uia(client => client.Value("AccountEmail", account.Email));
            // PasswordBox has no readable ValuePattern. Only this fixture's synthetic password is supplied in-process.
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = account.Password;
            Uia(client => client.InvokeId("AccountPrimary"));
        }
        internal void OpenAccount()
        {
            Uia(client => client.InvokeId("AccountButton"));
            Wait(() => Home.IsAccountPanelOpen);
        }
        internal void CloseAccount()
        {
            Uia(client => client.InvokeName("继续本地游玩"));
            Wait(() => !Home.IsAccountPanelOpen);
        }
        internal byte[] ReadDiskBytes() => File.ReadAllBytes(paths.AppearancePath);
        internal JsonElement ReadDiskConfiguration() { using var parsed = JsonDocument.Parse(ReadDiskBytes()); return AppearanceContract.ValidateAndClone(parsed.RootElement); }
        internal LocalPetConfigurationSnapshot CaptureLocal() => Complete(new LocalPetConfigurationRepository(paths.AppearancePath, appearance!, Home.Dispatcher).CaptureAsync(CancellationToken.None));
        internal string JournalDirectory(AccountSyncB1cActor account, string slot) => Path.Combine(paths.GetAccountDirectory(Origin, account.UserId, account.PetId), slot);
        internal AccountSyncB1cArchive[] Archives(AccountSyncB1cActor account, string slot)
        {
            var directory = JournalDirectory(account, slot);
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.originals.json")
                .Select(path => new AccountSyncB1cArchive(Guid.ParseExact(Path.GetFileName(path).Split('.')[0], "N"), directory)).ToArray() : [];
        }
        internal int ReceiptCount(AccountSyncB1cActor account, string slot) => Directory.Exists(JournalDirectory(account, slot))
            ? Directory.GetFiles(JournalDirectory(account, slot), "*.receipt.json").Length : 0;
        internal PetConfigurationOriginals LoadOriginal(AccountSyncB1cArchive archive) => Complete(new PetConfigurationFileJournal(archive.Directory).LoadOriginalsAsync(archive.Id, CancellationToken.None));
        internal PetConfigurationReceipt? LoadReceipt(AccountSyncB1cArchive archive) => Complete(new PetConfigurationFileJournal(archive.Directory).LoadReceiptAsync(archive.Id));
        internal void AssertArchive(AccountSyncB1cArchive archive, AccountSyncB1cActor account, byte[] localBytes, JsonElement localConfiguration,
            PetConfigurationSnapshot cloud, PetConfigurationChoice choice, PetChoiceOutcome outcome, long observed)
        {
            AssertOriginal(archive, account, localBytes, localConfiguration, cloud, choice);
            var originals = LoadOriginal(archive); var receipt = LoadReceipt(archive);
            Assert.IsNotNull(receipt);
            Assert.AreEqual(choice, receipt.Choice); Assert.AreEqual(outcome, receipt.Outcome);
            Assert.AreEqual((long?)observed, receipt.ObservedCloudVersion); Assert.IsNull(receipt.FailureCode);
            Assert.IsTrue(receipt.RecordedAt >= originals.CapturedAt);
        }
        internal void AssertOriginal(AccountSyncB1cArchive archive, AccountSyncB1cActor account, byte[] localBytes, JsonElement localConfiguration,
            PetConfigurationSnapshot cloud, PetConfigurationChoice choice)
        {
            var originals = LoadOriginal(archive);
            Assert.AreEqual(Origin, originals.Origin); Assert.IsTrue(originals.Local.Exists);
            CollectionAssert.AreEqual(localBytes, originals.Local.OriginalBytes.ToArray());
            Assert.IsTrue(AppearanceContract.Equivalent(localConfiguration, originals.Local.NormalizedConfiguration));
            Assert.AreEqual(account.UserId, originals.Cloud.UserId); Assert.AreEqual(account.PetId, originals.Cloud.PetId);
            Assert.AreEqual(cloud.ConfigurationVersion, originals.Cloud.ConfigurationVersion);
            Assert.IsTrue(AppearanceContract.Equivalent(cloud.Configuration, originals.Cloud.Configuration));
            Assert.AreEqual(choice, originals.Choice);
        }
        internal void AssertHealthyFrame()
        {
            var scene = runtime ?? throw new AssertFailedException("The actual native runtime is required.");
            var visual = (DeformableDangoVisual)Home.FindName("LiveDangoVisual");
            bool Healthy() => window!.IsVisible && Home.IsVisible && !Home.IsInPorch && visual.IsVisible
                && PresentationSource.FromVisual(visual) is not null
                && visual.LastContourFailure == DangoContourFailure.None
                && visual.RenderedBodyBottom is { } bottom && double.IsFinite(bottom)
                && scene.InitializationState == SceneInitializationState.Ready
                && scene.LastPhysicsFrame is { Succeeded: true } && scene.LastFrame is { Bodies.Count: > 0 }
                && scene.LastError is null && scene.IsRunning
                && Home.OwnsLiveFrameDriver;
            Wait(Healthy);
            Assert.IsTrue(scene.EntityIds.Count > 0);
            var frame = scene.LastPhysicsFrame;
            Wait(() => !ReferenceEquals(frame, scene.LastPhysicsFrame) && Healthy());
            var rendered = false;
            EventHandler rendering = (_, _) => { if (Healthy()) rendered = true; };
            CompositionTarget.Rendering += rendering;
            try { Wait(() => rendered); }
            finally { CompositionTarget.Rendering -= rendering; }
            Assert.IsTrue(Healthy()); healthyFrameObserved = true;
        }
        internal void Mark(string phase, bool capture = false)
        {
            Assert.IsTrue(transitions.Count < 128);
            var screenshot = capture ? SaveOwnWindow(phase) : null;
            transitions.Add(new { Phase = phase, UserId = Field<AccountIdentity?>(Controller, "identity")?.UserId,
                Controller.Nickname, Controller.ConfigurationReady, AccountOpen = Home.IsAccountPanelOpen,
                PumpSettled = CurrentPump.IsCompleted, TimerEnabled = ConfigurationTimer.IsEnabled,
                AppearanceSha256 = Convert.ToHexString(SHA256.HashData(ReadDiskBytes())), Screenshot = screenshot });
        }
        private string SaveOwnWindow(string phase)
        {
            AssertHealthyFrame();
            window!.UpdateLayout();
            var width = (int)Math.Ceiling(window.ActualWidth);
            var height = (int)Math.Ceiling(window.ActualHeight);
            Assert.IsTrue(width is > 0 and <= 2048 && height is > 0 and <= 2048
                && (long)width * height <= 4_194_304, "Only bounded pixels from this fixture's own WPF window may be recorded.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var name = phase + ".png";
            using var file = new FileStream(Path.Combine(paths.RootDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            encoder.Save(file); file.Flush(true);
            return name;
        }
        private AccountSyncAppearanceNavigationState ReadNavigation()
        {
            bool Ready(string id) => Home.FindName(id) is FrameworkElement
                { IsVisible: true, IsEnabled: true, IsHitTestVisible: true, ActualWidth: > 0, ActualHeight: > 0 };
            var editor = !Home.IsEditing ? AccountSyncAppearanceEditor.None : Home.SelectedEditor == HomeEditor.Face
                ? AccountSyncAppearanceEditor.Face : AccountSyncAppearanceEditor.Wardrobe;
            return new(Home.IsAccountPanelOpen, Home.IsSettingsOpen, Home.IsInPorch, Home.IsDangoAway, editor,
                Ready("FaceButton"), Ready("AppearanceEyesTab"), Ready("AppearanceEyesPanel") && Ready("EyeSizeSlider"),
                Ready("ApplyButton"), Ready("CancelAppearanceButton"), Ready(editor == AccountSyncAppearanceEditor.Face
                    ? "FaceCloseEditorButton" : "CloseEditorButton"), appearance!.HasPendingSave,
                appearance.DraftAppearance.EyeLayout.SizePercent, appearance.SavedAppearance.EyeLayout.SizePercent);
        }
        internal void Uia(Action<UiaClient> action)
        {
            Assert.IsFalse(finishing); Assert.IsTrue(pendingUia is null || pendingUia.IsCompleted);
            Assert.IsTrue(uiaTasks.Count < 128);
            var task = pendingUia = Task.Run(() =>
            {
                Assert.AreNotEqual(ownerThread, Environment.CurrentManagedThreadId);
                independentUiaObserved = true;
                action(new UiaClient(handle, () => !finishing));
            });
            uiaTasks.Add(task);
            Observe(task);
            try { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
            finally { if (task.IsCompleted) pendingUia = null; }
        }
        internal void Wait(Func<bool> condition)
        {
            RememberPump(); if (condition()) return;
            var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { RememberPump(); if (condition() || elapsed.Elapsed >= TimeSpan.FromSeconds(8)) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.IsTrue(condition(), "The bounded complete-host HWND/HTTP/dispatcher transition did not settle.");
        }
        private T Complete<T>(Task<T> task)
        {
            localTasks.Add(task); Observe(task); Assert.IsTrue(localTasks.Count <= 128);
            Wait(() => task.IsCompleted); return task.GetAwaiter().GetResult();
        }
        private void RememberPump()
        {
            if (controller is null) return;
            var actual = Field<Task>(controller, "configurationPump");
            if (pumps.Add(actual)) { Assert.IsTrue(pumps.Count <= 128); Observe(actual); }
            var sync = Field<PetConfigurationLiveSyncCoordinator?>(controller, "configurationSync");
            if (sync is not null) { coordinators.Add(sync); Assert.IsTrue(coordinators.Count <= 4); }
        }
        private static void Observe(Task task) => _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        internal bool CleanupPassed { get; private set; }
        internal bool HealthyFrameObserved => nativeHashVerified && healthyFrameObserved;
        internal void Finish(bool journeyPassed)
        {
            if (finished) return; finished = true; finishing = true;
            var failures = new List<object>();
            var hostDisposed = false; var hwndClosed = false; var uiSettled = false; var pumpsSettled = false;
            var httpSettled = false; var timerStopped = false;
            var hostTimersStopped = false; var runtimeStopped = false; var homeDriverReleased = false;
            var coordinatorsStopped = false; var accountOperationsDrained = false;
            var secretsDisposed = false;
            void Cleanup(string step, Action action)
            {
                try { action(); } catch (Exception error) { failures.Add(new { Step = step, FailureType = error.GetType().Name }); }
            }
            try
            {
                Cleanup("RememberPump", RememberPump);
                Cleanup("ControllerStop", () => { controller?.Dispose(); timerStopped = controller is null || !ConfigurationTimer.IsEnabled; });
                Cleanup("ReleaseFault", () => Fixture.CurrentHold?.Release(cleanup: true));
                Cleanup("HostDispose", () => { host?.Dispose(); hostDisposed = true; });
                Cleanup("HostTimersStopped", () =>
                {
                    hostTimersStopped = host is not null && !Field<DispatcherTimer>(host, "geometrySaveTimer").IsEnabled
                        && !Field<DispatcherTimer>(host, "roomSaveTimer").IsEnabled;
                    Assert.IsTrue(hostTimersStopped);
                });
                Cleanup("RuntimeStopped", () =>
                {
                    runtimeStopped = runtime is not null && !runtime.IsRunning && runtime.FrameDriver is null;
                    Assert.IsTrue(runtimeStopped);
                });
                Cleanup("HomeDriverReleased", () => { homeDriverReleased = window is not null && !Home.OwnsLiveFrameDriver; Assert.IsTrue(homeDriverReleased); });
                Cleanup("HwndClose", () => { Wait(() => handle == nint.Zero || !IsWindow(handle)); hwndClosed = handle != nint.Zero && !IsWindow(handle); });
                Cleanup("UiaDrain", () =>
                {
                    Wait(() => uiaTasks.All(task => task.IsCompleted));
                    foreach (var task in uiaTasks) task.GetAwaiter().GetResult();
                    uiSettled = true;
                });
                Cleanup("PumpDrain", () => { Wait(() => pumps.All(task => task.IsCompleted)); foreach (var task in pumps) task.GetAwaiter().GetResult(); pumpsSettled = true; });
                Cleanup("HttpDrain", () => { Wait(() => Fixture.Tasks.All(task => task.IsCompleted)); foreach (var task in Fixture.Tasks) { if (task.IsFaulted) _ = task.Exception; } httpSettled = true; });
                Cleanup("AccountOperationsDrain", () =>
                {
                    Wait(() => localTasks.All(task => task.IsCompleted)
                        && (controller is null || Field<SemaphoreSlim>(controller, "operations").CurrentCount == 1));
                    foreach (var task in localTasks) if (task.IsFaulted) _ = task.Exception;
                    accountOperationsDrained = localTasks.All(task => task.IsCompleted) && controller is not null
                        && Field<SemaphoreSlim>(controller, "operations").CurrentCount == 1;
                    Assert.IsTrue(accountOperationsDrained);
                });
                // The production fire-and-forget DisposeAfterPumpAsync task is not observable here.
                // Record the observable original tasks and Stopped coordinator state, without claiming that task was awaited.
                Cleanup("CoordinatorsStopped", () => { coordinatorsStopped = coordinators.Count > 0 && coordinators.All(sync => sync.State.Status == PetLiveSyncStatus.Stopped); Assert.IsTrue(coordinatorsStopped); });
                Cleanup("SaveEventUnsubscribe", () => { if (appearance is not null) appearance.SavedToDisk -= OnSaved; });
                Cleanup("MemorySecretsDispose", () => { Secrets.Dispose(); secretsDisposed = Secrets.IsDisposed; Assert.IsTrue(secretsDisposed); });
                Cleanup("Evidence", () =>
                {
                    using var file = new FileStream(Path.Combine(paths.RootDirectory, "desktop-evidence.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Case = name, JourneyPassed = journeyPassed,
                        UsesCompleteAppHost = hostStarted, HostCreated = host is not null,
                        UsesActualNative = nativeHashVerified && healthyFrameObserved,
                        NativeHashVerified = nativeHashVerified, HealthyFrameObserved = healthyFrameObserved, UsesDatabase = true,
                        UsesTlsListener = true, UsesWindowsCredentials = false, UsesIndependentUiaClient = independentUiaObserved,
                        HostDisposed = hostDisposed, HwndClosed = hwndClosed, UiaSettled = uiSettled,
                        PumpsSettled = pumpsSettled, HttpSettled = httpSettled, TimerStopped = timerStopped,
                        HostTimersStopped = hostTimersStopped, RuntimeStopped = runtimeStopped,
                        HomeDriverReleased = homeDriverReleased, CoordinatorsStopped = coordinatorsStopped,
                        AccountOperationsDrained = accountOperationsDrained,
                        SecretsDisposed = secretsDisposed,
                        ObservedPumpCount = pumps.Count, UiaTaskCount = uiaTasks.Count, HttpTaskCount = Fixture.Tasks.Length,
                        SuccessfulSaveEvents, Requests = Fixture.Requests, Phases = Fixture.Phases, Transitions = transitions,
                        Held = Fixture.CurrentHold?.Snapshot(),
                        Failures = failures.ToArray() });
                    Assert.IsTrue(bytes.Length <= 131072); file.Write(bytes); file.Flush(true);
                });
                CleanupPassed = hostDisposed && hwndClosed && uiSettled && pumpsSettled && httpSettled && timerStopped && hostTimersStopped && runtimeStopped && homeDriverReleased && coordinatorsStopped && accountOperationsDrained && secretsDisposed && failures.Count == 0;
                fixtureResource();
                void fixtureResource() => Fixture.Resource("DesktopDisposed", new { Index = index, Disposed = hostDisposed, HwndClosed = hwndClosed, UiaSettled = uiSettled, PumpsSettled = pumpsSettled, HttpSettled = httpSettled, TimerStopped = timerStopped, HostTimersStopped = hostTimersStopped, RuntimeStopped = runtimeStopped, HomeDriverReleased = homeDriverReleased, CoordinatorsStopped = coordinatorsStopped, AccountOperationsDrained = accountOperationsDrained, SecretsDisposed = secretsDisposed, CleanupPassed, InternalDisposeTaskObservable = false });
                if (journeyPassed)
                {
                    Assert.IsTrue(hostDisposed && hwndClosed && uiSettled && pumpsSettled && httpSettled && timerStopped
                        && hostTimersStopped && runtimeStopped && homeDriverReleased && coordinatorsStopped && accountOperationsDrained && secretsDisposed);
                    Assert.AreEqual(0, failures.Count, "Every owned cleanup step must settle independently.");
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        internal sealed class UiaClient(nint handle, Func<bool> active)
    {
        private AutomationElement Root
        {
            get
            {
                Assert.IsTrue(active() && IsWindow(handle));
                var root = AutomationElement.FromHandle(handle); Assert.AreEqual(Process.GetCurrentProcess().Id, root.Current.ProcessId); return root;
            }
        }
        private AutomationElement Required(string id) => Root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new AssertFailedException("Owned complete-host UIA control missing: " + id);
        internal void AssertOwnedWindow() => _ = Root;
        private AutomationElement Ready(string id)
        {
            var element = Required(id); Assert.IsTrue(element.Current.IsEnabled && !element.Current.IsOffscreen); Assert.IsTrue(active()); return element;
        }
        internal bool HasId(string id) => Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) is not null;
        internal string Name(string id) => Required(id).Current.Name;
        internal bool Enabled(string id) => Required(id).Current.IsEnabled;
        internal bool Selected(string id) => ((SelectionItemPattern)Ready(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected;
        internal void Value(string id, string value) => ((ValuePattern)Ready(id).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
        internal void Select(string id) => ((SelectionItemPattern)Ready(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        internal void Range(string id, double value) => ((RangeValuePattern)Ready(id).GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);
        internal void InvokeId(string id) => ((InvokePattern)Ready(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        internal void InvokeName(string name)
        {
            var button = Root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                ?? throw new AssertFailedException("Owned complete-host UIA button missing: " + name);
            Assert.IsTrue(button.Current.IsEnabled && !button.Current.IsOffscreen && active());
            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
    }

        internal static T Field<T>(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new AssertFailedException("Owned host field missing: " + name);
            return (T)field.GetValue(instance)!;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(nint handle);
}
