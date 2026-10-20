using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltPorchIntegrationTests
{
    [STATestMethod]
    public void TrayRoomRecoveryCannotCreateAnUnselectedRoomOrRaceCatalogPreparation()
    {
        using var fixture = new HiddenHome(productHost: true);
        var host = fixture.Host!;
        var owner = fixture.Home.BoundBeltBody;
        var runtime = (StackedDangoSceneRuntime)typeof(DangoApplicationHost)
            .GetField("stackedRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var actions = (Dango.Desktop.AppShell.Tray.LocalRoomTrayActions)typeof(DangoApplicationHost)
            .GetMethod("CreateLocalRoomTrayActions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        Assert.IsFalse(actions.IsSelected());
        actions.Recover();
        Assert.AreEqual(0, runtime.EntityIds.Count, "Room recovery must not bypass room selection and start another owner from the home.");
        Assert.IsFalse(host.LocalHandoffPending);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
        var selected = typeof(DangoApplicationHost).GetField("localRoomSelected", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var loading = typeof(DangoApplicationHost).GetField("catalogEntryLoading", BindingFlags.Instance | BindingFlags.NonPublic)!;
        selected.SetValue(host, true);
        loading.SetValue(host, true);
        try
        {
            actions.Recover();
            Assert.AreEqual(0, runtime.EntityIds.Count, "A pending catalog load must finish admission before room recovery may initialize anything.");
        }
        finally { selected.SetValue(host, false); loading.SetValue(host, false); }
        var calls = owner!.Metrics.NativeStepCalls;
        fixture.Wait(() => owner.Metrics.NativeStepCalls > calls, "the original home remains healthy");
        Assert.AreSame(owner, fixture.Home.BoundBeltBody);
    }

    [STATestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void OrdinaryHostDisplaysSavedAppearanceAndOnlyExplicitApplyMigrates(int schema)
    {
        byte[] original = [];
        using var fixture = new HiddenHome(root =>
        {
            var path = Path.Combine(root, "appearance.json");
            new AppearanceSettingsStore().Save(path,
                DangoAppearanceConfiguration.Default.WithBody(new DangoBodyControls(119.5, 95, 2)));
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json["appearanceSchemaVersion"] = schema;
            if (schema < 5) json.Remove("bodyPhysicsModel");
            if (schema < 4) foreach (var field in new[] { "bodySizePercent", "bodySoftness", "bodyLandingBuild" }) json.Remove(field);
            if (schema < 3) foreach (var field in new[] { "eyeSizePercent", "eyeOvalityPercent", "eyeHeightControlPercent", "eyeSpacingControlPercent" }) json.Remove(field);
            if (schema < 2) json.Remove("eyeColorHex");
            File.WriteAllText(path, json.ToJsonString());
            original = File.ReadAllBytes(path);
        }, productHost: true);
        var home = fixture.Home;
        var session = home.BoundAppearance!;
        var path = Path.Combine(fixture.RootPath, "appearance.json");
        Assert.AreEqual(schema < 4 ? DangoBodyControls.Default : new DangoBodyControls(119.5, 95, 2), home.DisplayedBeltSnapshot!.Body);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path), "Construction and native display must not migrate the file.");
        var saved = session.SavedAppearance;
        home.ShowEditor(HomeEditor.Face);
        Element<System.Windows.Controls.Primitives.RangeBase>(home, "BodySizeSlider").Value = 80;
        fixture.Wait(() => !session.RequiresBodyPreview, "real native draft application");
        fixture.Click("CancelAppearanceButton");
        fixture.Wait(() => !session.RequiresBodyPreview, "cancel restores the saved native body");
        Assert.AreEqual(saved, session.DraftAppearance);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path), "Cancelling must preserve the old format byte for byte.");
        home.ShowEditor(HomeEditor.Face);
        fixture.Click("ApplyButton");
        Assert.IsFalse(session.HasPendingSave, session.StatusMessage);
        Assert.AreEqual(5, JsonNode.Parse(File.ReadAllText(path))!["appearanceSchemaVersion"]!.GetValue<int>());
        Assert.AreEqual(saved, new AppearanceSettingsSession(path).SavedAppearance);
    }

    [STATestMethod]
    public void HiddenPorchRendersAndMovesTheSameContinuousBodyAndKeepsDoorActions()
    {
        using var fixture = new HiddenHome();
        var home = fixture.Home;
        var runtime = home.BoundBeltBody;
        var presenter = home.BoundBeltPresenter;
        var scene = Element<PorchSceneView>(home, "PorchScene");
        home.EnableLocalRoomDoor();
        fixture.Click("EnterRoomButton");
        fixture.Wait(() => scene.HasBeltBody, "continuous body enters the porch");
        Assert.IsTrue(home.IsInPorch);
        Assert.AreSame(runtime, home.BoundBeltBody);
        Assert.AreSame(presenter, home.BoundBeltPresenter);
        Assert.IsNull(home.BoundRuntime, "The porch must not start a second legacy physics world.");
        Assert.AreEqual(560d, scene.PetWorldX, 1.5);
        var initialX = scene.PetWorldX;
        var initialTick = home.DisplayedBeltSnapshot!.Tick;
        SetDirection(home, Key.D, true);
        fixture.Wait(() => scene.PetWorldX > initialX + 6, "real native motion is projected into the porch");
        SetDirection(home, Key.D, false);
        Assert.IsTrue(home.DisplayedBeltSnapshot!.Tick > initialTick);
        Assert.IsNull(runtime!.Metrics.Error, home.ReadBeltBodySettingsScene().Diagnostics);
        Assert.IsFalse(SetDirection(home, Key.W, true), "Porch W remains the existing door action, not body stretch.");
        var entries = 0;
        home.LocalRoomRequested += () => entries++;
        Assert.IsTrue(scene.InteractNearestDoor());
        Assert.AreEqual(1, entries);
        var pixels = fixture.Render();
        presenter!.View.Visibility = Visibility.Hidden;
        var withoutBody = fixture.Render();
        presenter.View.Visibility = Visibility.Visible;
        Assert.IsTrue(pixels.Where((value, i) => value != withoutBody[i]).Count() > 200,
            "The actual body, eyes and accessories must contribute visible pixels to the porch render.");
        var selectedX = scene.PetWorldX;
        fixture.Click("BackFromPorchButton");
        fixture.Wait(() => !home.IsInPorch && home.BoundBeltBody!.Metrics.Running, "return home");
        Assert.AreSame(Element<Canvas>(home, "LiveEntityLayer"), VisualTreeHelper.GetParent(presenter.View));
        var reentryCoreX = home.DisplayedBeltSnapshot!.Frame!.CoreX;
        fixture.Click("EnterRoomButton");
        var origin = (double)typeof(ProductHomeView).GetField("beltPorchOriginX", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
        Assert.AreEqual(selectedX, origin + reentryCoreX * BeltBodyAppearancePresenter.WorldToCanvas, 1e-6,
            "Re-entry maps the displayed source pose back to the prior porch position; later native motion must remain continuous.");
        fixture.Wait(() => scene.HasBeltBody, "re-enter porch");
        Assert.IsNull(runtime.Metrics.Error);
    }

    [STATestMethod]
    public void NativeHomePreviewsAccessoriesSavesAndKeepsDraftOnWriteFailureThenCancels()
    {
        using var fixture = new HiddenHome();
        var home = fixture.Home;
        var runtime = home.BoundBeltBody;
        var presenter = home.BoundBeltPresenter!;
        var session = home.BoundAccessories!;
        var panel = (AccessoryEditorPanel)typeof(ProductHomeView)
            .GetField("accessoryPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
        home.ShowEditor(HomeEditor.Wardrobe);
        panel.SetBodyAnchor(new(.31, .43));
        Assert.IsTrue(session.IsDirty);
        Assert.AreEqual(AccessoryBodyMapper.Map(presenter.Inspect().Body!, null, .31, .43),
            presenter.Inspect().Accessories.Single().RawBodyAnchor);
        var apply = Descendants(panel.SaveActions).OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "应用");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var saved = session.SavedConfiguration;
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(saved, new AccessorySettingsSession(fixture.RootPath).SavedConfiguration);
        panel.SetBodyAnchor(new(.67, .52));
        var draft = session.DraftConfiguration;
        using (File.Open(Path.Combine(fixture.RootPath, "accessories.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(saved, session.SavedConfiguration);
            Assert.AreEqual(draft, session.DraftConfiguration);
            Assert.IsTrue(session.HasPendingSave);
            StringAssert.Contains(session.StatusMessage, "未保存");
        }
        home.CloseEditor();
        fixture.Wait(() => home.BoundAppearance?.RequiresBodyPreview == false, "cancel restores the actual native home");
        Assert.AreEqual(saved, session.DraftConfiguration);
        Assert.AreEqual(saved, new AccessorySettingsSession(fixture.RootPath).SavedConfiguration);
        Assert.AreSame(runtime, home.BoundBeltBody);
        Assert.IsNull(runtime!.Metrics.Error);
        fixture.Click("EnterRoomButton");
        fixture.Wait(() => Element<PorchSceneView>(home, "PorchScene").HasBeltBody, "saved accessories enter the porch");
        Assert.AreSame(presenter, home.BoundBeltPresenter);
        Assert.AreEqual(1, presenter.RenderedAccessoryCount);
        Assert.AreEqual(saved, session.DraftConfiguration);
    }

    [STATestMethod]
    public void RoomReturnWaitsForTheNewPorchFrameAndReleasesSourceInput()
    {
        using var fixture = new HiddenHome();
        var home = fixture.Home;
        fixture.Click("EnterRoomButton");
        fixture.Wait(() => Element<PorchSceneView>(home, "PorchScene").HasBeltBody, "porch frame");
        SetDirection(home, Key.D, true);
        fixture.Wait(() => home.DisplayedBeltSnapshot!.ActualInput.Horizontal > 0, "native movement input");
        var runtime = home.BoundBeltBody!;
        var paused = home.PauseBeltForRoomAsync();
        fixture.Wait(() => paused.IsCompleted, "native owner pause barrier");
        paused.GetAwaiter().GetResult();
        home.SetDangoAway(true);
        var calls = runtime.Metrics.NativeStepCalls;
        fixture.PumpFor(80);
        Assert.AreEqual(calls, runtime.Metrics.NativeStepCalls);
        var confirmations = new List<long>();
        home.HealthyPorchFrameReady += confirmations.Add;
        home.ResumePorch(73);
        Assert.IsTrue(home.IsInPorch, "A return to the porch must not be redirected to the homepage.");
        Assert.AreEqual(0, confirmations.Count, "Changing the route alone is not a displayed-frame acknowledgement.");
        fixture.Wait(() => confirmations.Count == 1, "returned continuous body frame");
        CollectionAssert.AreEqual(new long[] { 73 }, confirmations);
        Assert.AreSame(runtime, home.BoundBeltBody);
        Assert.AreEqual(0d, home.DisplayedBeltSnapshot!.ActualInput.Horizontal);
        fixture.PumpFor(80);
        Assert.AreEqual(1, confirmations.Count);
        home.ShowSettingsPage();
        fixture.Wait(() => !runtime.Metrics.Running, "settings pauses the porch body");
        Assert.IsFalse(home.IsInPorch);
        Assert.IsNull(runtime.Metrics.Error);
    }

    private static bool SetDirection(ProductHomeView home, Key key, bool down) => (bool)typeof(ProductHomeView)
        .GetMethod("SetBeltDirectionalKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(home, [key, down, false])!;
    private static T Element<T>(ProductHomeView home, string name) => (T)home.FindName(name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class HiddenHome : IDisposable
    {
        internal ProductHomeView Home { get; }
        internal string RootPath { get; }
        private readonly DangoApplicationHost? host;
        internal DangoApplicationHost? Host => host;
        private readonly Panel? parent;
        private readonly int childIndex;
        private readonly HwndSource source;
        internal HiddenHome(Action<string>? prepare = null, bool productHost = false)
        {
            var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll");
            Assert.IsTrue(File.Exists(module), "Build with the independent belt body module; this test may not be skipped.");
            var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-porch", Guid.NewGuid().ToString("N"));
            RootPath = root;
            Directory.CreateDirectory(root);
            prepare?.Invoke(root);
            if (productHost)
            {
                host = new DangoApplicationHost(settingsFile: App.ResolveProductSettingsFile(["--data-root", root]));
                var window = (ControlCenterWindow)typeof(DangoApplicationHost)
                    .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
                window.ShowOverview();
                Assert.IsFalse(window.IsVisible, "The production control center must never be shown by this background test.");
                Home = window.ProductHome;
                Home.ReduceMotion = true;
                var room = (StackedDangoSceneRuntime)typeof(DangoApplicationHost)
                    .GetField("stackedRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                Assert.AreEqual(0, room.EntityIds.Count, "The actual default host must defer room initialization.");
                Assert.IsNull(Home.BoundRuntime);
                Assert.IsNotNull(Home.BoundBeltBody, "The ordinary host must bind the continuous body without a model-specific mode.");
                Assert.IsTrue(Home.BoundAppearance!.RequiresBodyPreview);
                Assert.IsFalse(Home.BoundAppearance.TrySave(), "Persistence must wait for the actual displayed native frame.");
                parent = (Panel)VisualTreeHelper.GetParent(Home);
                childIndex = parent.Children.IndexOf(Home);
                parent.Children.Remove(Home);
            }
            else
            {
                Home = new ProductHomeView { ReduceMotion = true };
                Home.BindAppearance(new AppearanceSettingsSession(Path.Combine(root, "appearance.json")));
                Home.BindAccessories(new AccessorySettingsSession(root));
                Home.BindBeltBody(Path.Combine(root, "settings.json"));
            }
            source = new HwndSource(new HwndSourceParameters("Fictional hidden continuous porch")
            { Width = 1100, Height = 760, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = Home };
            Layout();
            Assert.IsFalse(IsWindowVisible(source.Handle));
            Wait(() => Home.DisplayedBeltSnapshot is not null && Home.BoundAppearance?.RequiresBodyPreview == false,
                "actual body is running while the native surface remains hidden");
        }
        internal void Click(string name) => Element<Button>(Home, name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        private void Layout()
        {
            Home.Measure(new Size(1100, 760));
            Home.Arrange(new Rect(0, 0, 1100, 760));
            Home.UpdateLayout();
        }
        private void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Layout();
            // Hidden sources have no guaranteed compositor cadence. Explicitly consume real native
            // snapshots through the product presentation callback; never count hidden wall time as ticks.
            typeof(ProductHomeView).GetMethod("OnBeltRendering", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Home, [null, EventArgs.Empty]);
            Thread.Sleep(5);
        }
        internal void Wait(Func<bool> condition, string reason)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.ElapsedMilliseconds < 8000) Pump();
            Assert.IsTrue(condition(), reason + "\n" + Home.ReadBeltBodySettingsScene().Diagnostics);
            Assert.IsFalse(IsWindowVisible(source.Handle));
        }
        internal void PumpFor(int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) Pump();
        }
        internal byte[] Render()
        {
            Layout();
            var bitmap = new RenderTargetBitmap(1100, 760, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(Home);
            var pixels = new byte[1100 * 760 * 4];
            bitmap.CopyPixels(pixels, 1100 * 4, 0);
            return pixels;
        }
        public void Dispose()
        {
            source.RootVisual = null;
            if (host is not null)
            {
                parent!.Children.Insert(childIndex, Home);
                host.Dispose();
            }
            else Home.UnbindBeltBody();
            source.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
