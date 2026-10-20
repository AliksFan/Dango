using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltPorchIntegrationTests
{
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
        fixture.Click("EnterRoomButton");
        fixture.Wait(() => scene.HasBeltBody, "re-enter porch");
        Assert.AreEqual(selectedX, scene.PetWorldX, 2, "Navigation preserves the prior porch position.");
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
        internal ProductHomeView Home { get; } = new() { ReduceMotion = true };
        internal string RootPath { get; }
        private readonly HwndSource source;
        internal HiddenHome()
        {
            var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll");
            Assert.IsTrue(File.Exists(module), "Build with the independent belt body module; this test may not be skipped.");
            var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-porch", Guid.NewGuid().ToString("N"));
            RootPath = root;
            Directory.CreateDirectory(root);
            Home.BindAppearance(new AppearanceSettingsSession(Path.Combine(root, "appearance.json")));
            Home.BindAccessories(new AccessorySettingsSession(root));
            Home.BindBeltBody(Path.Combine(root, "settings.json"), enableRoomEntry: true);
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
            Home.UnbindBeltBody();
            source.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
