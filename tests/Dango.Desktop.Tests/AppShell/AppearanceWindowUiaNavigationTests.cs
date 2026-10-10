using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.AccountSyncReview.Tests;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

// Actual UIA against one isolated ProductHomeView HWND. No AppHost, native runtime,
// backend/client, certificate, credential store or database is constructed.
[TestClass]
[DoNotParallelize]
[TestCategory("OfflineAppearanceWindowUia")]
public sealed class AppearanceWindowUiaNavigationTests
{
    [STATestMethod]
    public void Xaml_names_are_the_actual_automation_ids_for_all_journey_controls()
    {
        using var rig = new Rig(reduceMotion: false);
        foreach (var id in new[] { "FaceButton", "AppearanceEyesTab", "EyeSizeSlider", "CancelAppearanceButton", "ApplyButton", "FaceCloseEditorButton" })
        {
            var element = (FrameworkElement)rig.Home.FindName(id);
            Assert.AreEqual(id, element.Name);
            var peer = UIElementAutomationPeer.CreatePeerForElement(element);
            Assert.IsNotNull(peer);
            Assert.AreEqual(id, peer.GetAutomationId(), "The effective peer ID must be checked, rather than only the attached property.");
        }
        rig.Uia(client => Assert.IsTrue(client.HasId("FaceButton")));
    }

    [STATestMethod]
    public void Wardrobe_survives_account_close_and_reproduces_old_lookup_failure_before_navigation_recovers()
    {
        using var rig = new Rig(reduceMotion: false);
        rig.Uia(client => client.InvokeId("WardrobeButton"));
        rig.Wait(() => rig.Read().Editor == AccountSyncAppearanceEditor.Wardrobe && rig.Read().CloseReady);
        rig.Uia(client => client.InvokeId("AccountButton"));
        rig.Wait(() => rig.Home.IsAccountPanelOpen);
        rig.Uia(client => client.InvokeName("继续本地游玩"));
        rig.Wait(() => !rig.Home.IsAccountPanelOpen);
        Assert.AreEqual(AccountSyncAppearanceEditor.Wardrobe, rig.Read().Editor);
        Assert.AreEqual(Visibility.Collapsed, ((UIElement)rig.Home.FindName("Launchers")).Visibility);
        Assert.IsFalse(rig.Read().FaceEntryReady);

        // The old helper assumes a visible launcher even while another editor is open.
        Assert.ThrowsExactly<AssertFailedException>(() => rig.Uia(client => client.InvokeId("FaceButton")));
        Assert.IsFalse(File.Exists(rig.AppearancePath));

        rig.Navigator.SaveEyeSize(111);
        Assert.AreEqual(AccountSyncAppearanceEditor.None, rig.Read().Editor);
        Assert.AreEqual(111d, rig.Session.SavedAppearance.EyeLayout.SizePercent);
        Assert.AreEqual(111d, new AppearanceSettingsSession(rig.AppearancePath).SavedAppearance.EyeLayout.SizePercent);
        CollectionAssert.AreEqual(new[] { "CloseEditorButton", "FaceButton", "AppearanceEyesTab", "EyeSizeSlider", "ApplyButton", "FaceCloseEditorButton" },
            rig.NavigationActions.ToArray());
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Face_eyes_range_apply_cancel_and_close_use_actual_uia_and_bounded_state_transitions(bool reduceMotion)
    {
        using var rig = new Rig(reduceMotion);
        rig.Navigator.PreviewEyeSize(111);
        Assert.IsTrue(rig.Session.HasPendingSave);
        Assert.AreEqual(111d, rig.Session.DraftAppearance.EyeLayout.SizePercent);
        Assert.IsFalse(File.Exists(rig.AppearancePath));
        rig.Navigator.ApplyEyeSize(111);
        Assert.AreEqual(AccountSyncAppearanceEditor.Face, rig.Read().Editor);
        Assert.IsFalse(rig.Session.HasPendingSave);
        var saved = File.ReadAllBytes(rig.AppearancePath);

        var faceInvocations = rig.NavigationActions.Count(id => id == "FaceButton");
        rig.Navigator.PreviewEyeSize(118);
        Assert.AreEqual(faceInvocations, rig.NavigationActions.Count(id => id == "FaceButton"), "An open Face editor must be reused instead of toggled closed.");
        Assert.AreEqual(118d, rig.Session.DraftAppearance.EyeLayout.SizePercent);
        rig.Navigator.CancelPreview();
        Assert.IsFalse(rig.Session.HasPendingSave);
        Assert.AreEqual(111d, rig.Session.DraftAppearance.EyeLayout.SizePercent);
        CollectionAssert.AreEqual(saved, File.ReadAllBytes(rig.AppearancePath));

        // Reopening repeatedly checks that visibility and the UIA tree are usable after close.
        foreach (var size in new[] { 119d, 120d, 121d })
        {
            rig.Navigator.PreviewEyeSize(size);
            Assert.IsTrue(rig.Session.HasPendingSave);
            rig.Navigator.CloseEditor();
            Assert.AreEqual(AccountSyncAppearanceEditor.None, rig.Read().Editor);
            Assert.AreEqual(111d, rig.Session.DraftAppearance.EyeLayout.SizePercent);
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(rig.AppearancePath));
            rig.Uia(client => Assert.IsTrue(client.HasId("FaceButton")));
        }
    }

    [STATestMethod]
    public void Saving_the_already_saved_eye_size_does_not_invoke_the_disabled_apply_button()
    {
        using var rig = new Rig(reduceMotion: false);
        rig.Navigator.SaveEyeSize(rig.Session.SavedAppearance.EyeLayout.SizePercent);
        Assert.IsFalse(rig.NavigationActions.Contains("ApplyButton"));
        Assert.IsFalse(rig.Session.HasPendingSave);
        Assert.AreEqual(AccountSyncAppearanceEditor.None, rig.Read().Editor);
        Assert.IsFalse(File.Exists(rig.AppearancePath));
    }

    [STATestMethod]
    public void Timed_out_owned_uia_worker_is_drained_after_hwnd_closes_and_late_input_is_rejected()
    {
        using var inputGate = new ManualResetEventSlim();
        using var finishGate = new ManualResetEventSlim();
        using var workerStarted = new ManualResetEventSlim();
        using var lateInputAttempted = new ManualResetEventSlim();
        using var workerCompleted = new ManualResetEventSlim();
        using var rig = new Rig(reduceMotion: false);
        var lateFailures = new List<Exception>();
        Task? release = null;
        try
        {
            Assert.ThrowsExactly<AssertFailedException>(() => rig.Uia(client =>
            {
                try
                {
                    workerStarted.Set();
                    Assert.IsTrue(inputGate.Wait(TimeSpan.FromSeconds(4)), "The fault input gate was not released.");
                    try { client.InvokeId("FaceButton"); }
                    catch (Exception error) { lateFailures.Add(error); }
                    try { client.Range("EyeSizeSlider", 111); }
                    catch (Exception error) { lateFailures.Add(error); }
                    lateInputAttempted.Set();
                    Assert.IsTrue(finishGate.Wait(TimeSpan.FromSeconds(4)), "The fault finish gate was not released.");
                    if (lateFailures.Count > 0) throw new AggregateException("Late owned UIA input was rejected.", lateFailures);
                }
                finally { workerCompleted.Set(); }
            }, TimeSpan.FromMilliseconds(250)));
            Assert.IsTrue(workerStarted.IsSet);
            Assert.IsFalse(workerCompleted.IsSet);
            Assert.IsTrue(IsWindow(rig.OwnedHandle));

            var secondActionRan = false;
            var pending = Assert.ThrowsExactly<InvalidOperationException>(() => rig.Uia(_ => secondActionRan = true));
            Assert.AreEqual("owned_appearance_uia_worker_pending", pending.Message);
            Assert.IsFalse(secondActionRan);

            var ownedHandle = rig.OwnedHandle;
            release = Task.Run(() =>
            {
                try
                {
                    Assert.IsTrue(SpinWait.SpinUntil(() => !IsWindow(ownedHandle), TimeSpan.FromSeconds(4)),
                        "Dispose must close the owned HWND before this worker can proceed.");
                    inputGate.Set();
                    Assert.IsTrue(lateInputAttempted.Wait(TimeSpan.FromSeconds(4)));
                    // Keep a real worker alive long enough to detect Dispose returning without draining it.
                    Thread.Sleep(200);
                }
                finally { inputGate.Set(); finishGate.Set(); }
            });
            rig.Dispose();
            Assert.IsFalse(IsWindow(ownedHandle));
            Assert.IsTrue(workerCompleted.IsSet, "Dispose returned while the owned UIA worker was still alive.");
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(4)));
            release.GetAwaiter().GetResult();
            Assert.AreEqual(2, lateFailures.Count);
            Assert.IsTrue(lateFailures.All(error => error is ObjectDisposedException));
            Assert.ThrowsExactly<ObjectDisposedException>(() => rig.Uia(_ => secondActionRan = true));
            Assert.IsFalse(secondActionRan);

            using var evidence = JsonDocument.Parse(File.ReadAllBytes(rig.EvidencePath));
            Assert.IsTrue(evidence.RootElement.GetProperty("HwndClosed").GetBoolean());
            Assert.IsTrue(evidence.RootElement.GetProperty("UiaWorkerSettled").GetBoolean());
            Assert.AreEqual(1, evidence.RootElement.GetProperty("UiaWorkerFaultsObserved").GetInt32());
            Assert.AreEqual(0, evidence.RootElement.GetProperty("Transitions").GetArrayLength(),
                "Late input must be rejected before tracing or reaching the UIA operation.");
        }
        finally
        {
            inputGate.Set(); finishGate.Set();
            try { rig.Dispose(); }
            finally
            {
                if (release is not null)
                {
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(4)));
                    release.GetAwaiter().GetResult();
                }
            }
        }
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(69d)]
    [DataRow(141d)]
    public void Invalid_eye_sizes_are_rejected_before_input_wait_or_state_read(double value)
    {
        var actions = 0; var waits = 0; var reads = 0;
        var navigation = new AccountSyncAppearanceNavigator(_ => actions++, (_, _) => actions++, _ => waits++, () =>
        {
            reads++;
            return new(false, false, false, false, AccountSyncAppearanceEditor.None,
                true, true, true, false, true, true, false, 100, 100);
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => navigation.PreviewEyeSize(value));
        Assert.AreEqual(0, actions); Assert.AreEqual(0, waits); Assert.AreEqual(0, reads);
    }

    [TestMethod]
    [DataRow("account")]
    [DataRow("settings")]
    [DataRow("porch")]
    [DataRow("away")]
    [DataRow("unknownEditor")]
    public void Navigation_rejects_inaccessible_background_pages_before_any_input(string obstacle)
    {
        var state = new AccountSyncAppearanceNavigationState(obstacle == "account", obstacle == "settings",
            obstacle == "porch", obstacle == "away", obstacle == "unknownEditor" ? (AccountSyncAppearanceEditor)99 : AccountSyncAppearanceEditor.None,
            true, true, true, true, true, true, false, 100, 100);
        var actions = 0;
        var navigation = new AccountSyncAppearanceNavigator(_ => actions++, (_, _) => actions++,
            _ => throw new AssertFailedException("Unavailable navigation reached a wait."), () => state);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => navigation.PreviewEyeSize(111));
        Assert.AreEqual("accountsync_appearance_navigation_unavailable", error.Message);
        Assert.AreEqual(0, actions);
    }

    private sealed class Rig : IDisposable
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        private readonly Window window;
        private readonly nint handle;
        private readonly string evidenceDirectory;
        private readonly List<object> transitions = [];
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        private volatile bool disposed;
        private Task? pendingUia;
        private int observedUiaFaults;
        internal ProductHomeView Home { get; } = new();
        internal AppearanceSettingsSession Session { get; }
        internal string AppearancePath { get; }
        internal nint OwnedHandle => handle;
        internal string EvidencePath => Path.Combine(evidenceDirectory, "uia-transitions.json");
        internal List<string> NavigationActions { get; } = [];
        internal AccountSyncAppearanceNavigator Navigator { get; }

        internal Rig(bool reduceMotion)
        {
            evidenceDirectory = Path.Combine(TestRepository.Root, "artifacts", "appearance-window-uia", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidenceDirectory);
            AppearancePath = Path.Combine(evidenceDirectory, "appearance.json");
            Session = new(AppearancePath);
            Home.BindAppearance(Session); Home.ReduceMotion = reduceMotion;
            Navigator = new(id => { NavigationActions.Add(id); Uia(client => client.InvokeId(id)); },
                (id, value) => { NavigationActions.Add(id); Uia(client => client.Range(id, value)); }, condition => Wait(condition), Read,
                (phase, state) => transitions.Add(new { Phase = phase, State = state }));
            window = new Window { Title = "Dango isolated appearance UIA", Content = Home, Width = 960, Height = 680,
                Left = 80, Top = 80, ShowActivated = false, ShowInTaskbar = false };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                window.Show(); window.UpdateLayout(); handle = new WindowInteropHelper(window).Handle;
                Assert.AreNotEqual(nint.Zero, handle);
                Assert.IsNull(Home.BoundRuntime); Assert.IsNull(Home.BoundSceneSession);
                Wait(() => Ready("FaceButton"));
            }
            catch
            {
                try { window.Close(); }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
                throw;
            }
        }

        internal AccountSyncAppearanceNavigationState Read()
        {
            var editor = !Home.IsEditing ? AccountSyncAppearanceEditor.None
                : Home.SelectedEditor == HomeEditor.Face ? AccountSyncAppearanceEditor.Face : AccountSyncAppearanceEditor.Wardrobe;
            return new(Home.IsAccountPanelOpen, Home.IsSettingsOpen, Home.IsInPorch, Home.IsDangoAway,
                editor, Ready("FaceButton"), Ready("AppearanceEyesTab"),
                ((UIElement)Home.FindName("AppearanceEyesPanel")).IsVisible && Ready("EyeSizeSlider"),
                Ready("ApplyButton"), Ready("CancelAppearanceButton"),
                Ready(editor == AccountSyncAppearanceEditor.Face ? "FaceCloseEditorButton" : "CloseEditorButton"),
                Session.HasPendingSave, Session.DraftAppearance.EyeLayout.SizePercent, Session.SavedAppearance.EyeLayout.SizePercent);
        }

        private bool Ready(string name) => Home.FindName(name) is FrameworkElement
            { IsVisible: true, IsEnabled: true, IsHitTestVisible: true, ActualWidth: > 0, ActualHeight: > 0 };
        internal void Uia(Action<UiaClient> action, TimeSpan? timeout = null)
        {
            EnsureUiaActive();
            if (pendingUia is { IsCompleted: false })
                throw new InvalidOperationException("owned_appearance_uia_worker_pending");
            if (pendingUia is { } previousWorker)
            {
                try { previousWorker.GetAwaiter().GetResult(); }
                finally { ObserveWorker(previousWorker); pendingUia = null; }
            }
            var task = pendingUia = Task.Run(() =>
            {
                EnsureUiaActive();
                Assert.AreNotEqual(ownerThread, Environment.CurrentManagedThreadId);
                action(new UiaClient(handle, phase =>
                {
                    EnsureUiaActive();
                    Home.Dispatcher.Invoke(() =>
                    {
                        EnsureUiaActive();
                        transitions.Add(new { Phase = "before-uia:" + phase, State = Read() });
                    });
                }, EnsureUiaActive));
            });
            try { Wait(() => task.IsCompleted, timeout); task.GetAwaiter().GetResult(); }
            finally
            {
                // A timed-out worker remains owned until Dispose closes its HWND and drains it.
                if (task.IsCompleted) { ObserveWorker(task); pendingUia = null; }
            }
        }

        private void EnsureUiaActive() => ObjectDisposedException.ThrowIf(disposed, this);
        private void ObserveWorker(Task worker)
        {
            Assert.IsTrue(worker.IsCompleted);
            if (worker.Exception is not null) observedUiaFaults++;
        }

        internal void Wait(Func<bool> condition, TimeSpan? timeout = null)
        {
            if (condition()) return;
            var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew();
            var limit = timeout ?? TimeSpan.FromSeconds(8);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed > limit) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.IsTrue(condition(), "The bounded owned appearance-window transition did not finish.");
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try
            {
                var cleanupErrors = new List<Exception>();
                try { window.Close(); Wait(() => !IsWindow(handle)); }
                catch (Exception error) { cleanupErrors.Add(error); }
                if (pendingUia is { } worker)
                {
                    try { Wait(() => worker.IsCompleted); }
                    catch (Exception error) { cleanupErrors.Add(error); }
                    if (worker.IsCompleted) { ObserveWorker(worker); pendingUia = null; }
                }
                var hwndClosed = !IsWindow(handle);
                var workerSettled = pendingUia is null;
                var evidence = JsonSerializer.SerializeToUtf8Bytes(new { UsesAppHost = false, UsesNativeRuntime = false,
                    UsesBackend = false, HwndClosed = hwndClosed, UiaWorkerSettled = workerSettled,
                    UiaWorkerFaultsObserved = observedUiaFaults, Actions = NavigationActions, Transitions = transitions });
                using var stream = new FileStream(EvidencePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                stream.Write(evidence); stream.Flush(true);
                if (!hwndClosed || !workerSettled || cleanupErrors.Count > 0)
                    throw new AggregateException("Owned appearance-window cleanup did not settle.", cleanupErrors);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }

    private sealed class UiaClient(nint handle, Action<string> trace, Action ensureActive)
    {
        private AutomationElement Root
        {
            get
            {
                ensureActive();
                Assert.IsTrue(IsWindow(handle));
                var root = AutomationElement.FromHandle(handle);
                Assert.AreEqual(Process.GetCurrentProcess().Id, root.Current.ProcessId);
                return root;
            }
        }
        private AutomationElement Required(string id) => Root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new AssertFailedException("Owned appearance-window UIA control missing: " + id);
        internal bool HasId(string id) => Root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id)) is not null;
        internal void InvokeId(string id)
        {
            ensureActive();
            trace("invoke:" + id);
            var element = Required(id); Assert.IsTrue(element.Current.IsEnabled); Assert.IsFalse(element.Current.IsOffscreen);
            var pattern = (InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern);
            ensureActive(); pattern.Invoke();
        }
        internal void Range(string id, double value)
        {
            ensureActive();
            trace("range:" + id + "=" + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var element = Required(id); Assert.IsTrue(element.Current.IsEnabled); Assert.IsFalse(element.Current.IsOffscreen);
            var pattern = (RangeValuePattern)element.GetCurrentPattern(RangeValuePattern.Pattern);
            ensureActive(); pattern.SetValue(value);
        }
        internal void InvokeName(string name)
        {
            ensureActive();
            trace("invoke-name:" + name);
            var element = Root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                ?? throw new AssertFailedException("Owned appearance-window UIA button missing: " + name);
            Assert.IsTrue(element.Current.IsEnabled);
            Assert.IsFalse(element.Current.IsOffscreen);
            var pattern = (InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern);
            ensureActive(); pattern.Invoke();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);
}
