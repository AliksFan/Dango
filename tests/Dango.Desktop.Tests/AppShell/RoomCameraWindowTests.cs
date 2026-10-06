using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomCameraWindowTests
{
    [STATestMethod]
    public void CameraSelectorIsWindowOnlyAndRefreshDoesNotDispatchAnotherModeChange()
    {
        var scene = new LocalRoomSceneView();
        scene.EnableWindowCamera();
        var sharedMenu = new LocalRoomMenuView();
        Assert.IsFalse(sharedMenu.ShowCameraSettings);
        Assert.AreEqual(Visibility.Collapsed, E<FrameworkElement>(sharedMenu, "RoomCameraSettings").Visibility);
        var modes = new List<RoomCameraMode>();
        sharedMenu.CameraModeRequested += modes.Add;
        sharedMenu.ShowCameraSettings = true;
        scene.SelectCameraMode(RoomCameraMode.EdgeFollow);
        sharedMenu.RefreshStatus(scene);
        var selector = E<ComboBox>(sharedMenu, "RoomCameraModeSelector");
        Assert.AreEqual(1, selector.SelectedIndex);
        Assert.AreEqual(0, modes.Count, "Refreshing a camera snapshot must not recursively dispatch a settings command.");
        CollectionAssert.AreEqual(new[] { "跟随", "不积极跟随", "不跟随" },
            selector.Items.Cast<ComboBoxItem>().Select(item => (string)item.Content).ToArray());
        selector.SelectedIndex = 2;
        CollectionAssert.AreEqual(new[] { RoomCameraMode.Manual }, modes);
        scene.SelectCameraMode(RoomCameraMode.Manual);
        sharedMenu.RefreshStatus(scene);
        Assert.AreEqual(1, modes.Count);
        sharedMenu.ShowCameraSettings = false;
        selector.SelectedIndex = 0;
        Assert.AreEqual(1, modes.Count, "The paused wallpaper menu must not emit camera commands from its hidden selector.");
    }

    [STATestMethod]
    public void MinimumWindowMenuKeepsEveryActionReachableAndEscapeClosesDropdownBeforeMenu()
    {
        using var rig = new MenuRig();
        rig.Window.OpenMenu();
        var menu = Field<LocalRoomMenuView>(rig.Window, "menu");
        rig.Window.UpdateLayout();
        Assert.IsTrue(menu.ShowCameraSettings);
        Assert.AreEqual(Visibility.Visible, E<FrameworkElement>(menu, "RoomCameraSettings").Visibility);
        var scroller = E<ScrollViewer>(menu, "RoomMenuScroll");
        var presenter = VisualDescendants<ScrollContentPresenter>(scroller).First();
        foreach (var name in new[] { "RoomCameraModeSelector", "RoomReturnButton", "RoomControlCenterButton", "RoomWindowButton", "RoomWallpaperButton", "RoomRecoverButton" })
        {
            var action = E<FrameworkElement>(menu, name);
            action.BringIntoView();
            rig.Window.UpdateLayout();
            Pump(25);
            var origin = action.TranslatePoint(new Point(), presenter);
            var tolerance = .5 / VisualTreeHelper.GetDpi(action).DpiScaleY + .01;
            Assert.IsTrue(action.ActualHeight > 0 && origin.Y >= -tolerance &&
                origin.Y + action.ActualHeight <= presenter.ActualHeight + tolerance,
                name + " must be fully reachable inside the minimum-height menu viewport.");
        }
        var selector = E<ComboBox>(menu, "RoomCameraModeSelector");
        selector.BringIntoView(); rig.Window.UpdateLayout();
        selector.SelectedIndex = 2;
        Assert.AreEqual(RoomCameraMode.Manual, rig.Window.Scene.Camera.CameraMode);
        Assert.IsTrue(rig.Window.IsMenuOpen, "Selecting a camera preference must keep the room menu open.");
        CaptureMenu(menu);
        selector.Focus(); selector.IsDropDownOpen = true;
        Assert.IsTrue(Press(rig.Window, selector, Key.Escape).Handled);
        Assert.IsFalse(selector.IsDropDownOpen);
        Assert.IsTrue(rig.Window.IsMenuOpen, "The first Escape belongs to the expanded selection list.");
        Assert.IsTrue(Press(rig.Window, selector, Key.Escape).Handled);
        Assert.IsFalse(rig.Window.IsMenuOpen);
        Assert.IsTrue(rig.Window.Scene.IsKeyboardFocused);
        var chrome = WindowChrome.GetWindowChrome(rig.Window);
        Assert.AreEqual(36d, chrome.CaptionHeight);
        Assert.AreEqual(new Thickness(7), chrome.ResizeBorderThickness);
        Assert.IsFalse(WindowChrome.GetIsHitTestVisibleInChrome(rig.Window.Scene), "The scene must not consume native caption and resize hit tests.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ManualPanUsesTwoAxisDipsAndNormalReleasePreservesHeldMovementAndIdentity()
    {
        using var rig = new NativeRig();
        var window = rig.Window;
        window.Scene.SelectCameraMode(RoomCameraMode.Manual);
        var entity = rig.Runtime.EntityIds.Single();
        var owner = rig.Runtime.FrameDriver;
        Assert.IsTrue(rig.Driver.Key(Key.D, true, false));
        var held = Field<DirectionalControlIntent>(rig.Driver, "intent");
        var startX = window.Scene.Camera.CameraX;
        var scale = window.Scene.Camera.Scale;
        var startProjection = window.Scene.Camera.Projection;
        Assert.IsTrue(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
        Assert.AreSame(window.Scene, Mouse.Captured);
        Assert.IsFalse(rig.Driver.IsPointerActive, "Right drag must not enter the left pet/physical interaction session.");
        Assert.IsTrue(window.MoveCameraPan(new Point(310, 190), true, ModifierKeys.None));
        Assert.AreEqual(startX - 10 / scale, window.Scene.Camera.CameraX, 1e-8);
        Assert.AreEqual(startProjection.OffsetX + 10, window.Scene.Camera.Projection.OffsetX, 1e-8,
            "Positive horizontal DIP motion must pull the whole world right.");
        Assert.AreEqual(startProjection.OffsetY + 10, window.Scene.Camera.Projection.OffsetY, 1e-8,
            "Positive vertical DIP motion must pull the whole world down.");
        Assert.IsTrue(window.EndCameraPan());
        Assert.IsFalse(window.IsCameraPanActive);
        Assert.IsNull(Mouse.Captured);
        Assert.AreEqual(held, Field<DirectionalControlIntent>(rig.Driver, "intent"), "Normal right-up must not clear still-held D.");
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        rig.Driver.ClearInput();
        rig.Host.HideLocalRoom(); rig.Host.RestoreLocalRoom();
        rig.WaitReady();
        Assert.AreEqual(RoomCameraMode.Manual, window.Scene.Camera.CameraMode, "Camera selection belongs to this running window, not each presentation attempt.");
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PanAdmissionRejectsFollowModifiersLeftGestureAndNativeChrome()
    {
        using var rig = new NativeRig();
        var window = rig.Window;
        Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
        window.Scene.SelectCameraMode(RoomCameraMode.Manual);
        foreach (var modifiers in new[] { ModifierKeys.Alt, ModifierKeys.Control, ModifierKeys.Windows, ModifierKeys.Shift | ModifierKeys.Control })
            Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), modifiers, false));
        foreach (var point in new[] { new Point(300, 12), new Point(3, 180), new Point(300, window.Scene.ActualHeight - 3), new Point(-1, 180), new Point(double.NaN, 180) })
            Assert.IsFalse(window.TryBeginCameraPan(point, ModifierKeys.None, false));
        Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, true));
        var otherCapture = new Border { Width = 20, Height = 20 };
        var root = (Grid)window.Content;
        root.Children.Add(otherCapture);
        try
        {
            Assert.IsTrue(otherCapture.CaptureMouse());
            Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
            Assert.AreSame(otherCapture, Mouse.Captured, "Rejected pan must not release another control's capture.");
        }
        finally { otherCapture.ReleaseMouseCapture(); root.Children.Remove(otherCapture); }
        var bounds = window.Scene.PetScreenBounds!.Value;
        Assert.IsTrue(rig.Driver.PointerDown(new Point(bounds.Center.X, bounds.Center.Y), true));
        Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
        Assert.IsTrue(rig.Driver.IsPointerActive, "Rejected right-down must preserve the already active left gesture.");
        rig.Driver.CancelPointer();
        Assert.IsTrue(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.Shift, false));
        window.EndCameraPan();
        window.OpenMenu();
        Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
        Assert.IsFalse(window.IsCameraPanActive);
        window.CloseMenu();
        window.Scene.ShowUnavailable("fictional test unavailable frame");
        Assert.IsFalse(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void CaptureTimeModeChangeCannotArmAPanOrKeepSceneCapture()
    {
        using var rig = new NativeRig();
        rig.Window.Scene.SelectCameraMode(RoomCameraMode.Manual);
        MouseEventHandler changeMode = (_, _) => rig.Window.Scene.SelectCameraMode(RoomCameraMode.Follow);
        rig.Window.Scene.GotMouseCapture += changeMode;
        try
        {
            Assert.IsFalse(rig.Window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
            Assert.IsFalse(rig.Window.IsCameraPanActive);
            Assert.AreNotSame(rig.Window.Scene, Mouse.Captured);
        }
        finally { rig.Window.Scene.GotMouseCapture -= changeMode; }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow("Menu")]
    [DataRow("Mode")]
    [DataRow("Zoom")]
    [DataRow("Resize")]
    [DataRow("Dpi")]
    [DataRow("InvalidFrame")]
    [DataRow("Presentation")]
    [DataRow("DriverStop")]
    [DataRow("LostCapture")]
    [DataRow("LostKeyboardFocus")]
    [DataRow("Deactivate")]
    [DataRow("Hide")]
    public void CancellingPanReleasesCaptureAndAnOldMoveCannotResumeIt(string cancellation)
    {
        using var rig = new NativeRig();
        var window = rig.Window;
        window.Scene.SelectCameraMode(RoomCameraMode.Manual);
        Assert.IsTrue(window.TryBeginCameraPan(new Point(300, 180), ModifierKeys.None, false));
        switch (cancellation)
        {
            case "Menu": window.OpenMenu(); break;
            case "Mode": window.Scene.SelectCameraMode(RoomCameraMode.Follow); break;
            case "Zoom": window.Scene.ApplyWheel(120); break;
            case "Resize": window.Width += 80; window.UpdateLayout(); break;
            case "Dpi":
                typeof(LocalRoomWindow).GetMethod("WindowMessage", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { (nint)0, 0x02E0, (nint)0, (nint)0, false });
                break;
            case "InvalidFrame": window.Scene.ShowUnavailable("fictional cancelled pan"); break;
            case "Presentation": window.Scene.BeginPresentation(); break;
            case "DriverStop": rig.Driver.Stop(); break;
            case "LostCapture": window.Scene.ReleaseMouseCapture(); break;
            case "LostKeyboardFocus":
                Assert.IsTrue(VisualDescendants<Button>((DependencyObject)window.Content).Single(button => button.Name == "RoomHideButton").Focus());
                break;
            case "Deactivate":
                var other = new Window { Width = 120, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false };
                try { other.Show(); other.Activate(); Pump(25); Assert.IsFalse(window.IsActive); }
                finally { other.Close(); }
                break;
            case "Hide": rig.Host.HideLocalRoom(); break;
            default: Assert.Fail("Unknown cancellation test case."); break;
        }
        Assert.IsFalse(window.IsCameraPanActive, cancellation);
        Assert.AreNotSame(window.Scene, Mouse.Captured, cancellation);
        var cancelledX = window.Scene.Camera.CameraX;
        Assert.IsFalse(window.MoveCameraPan(new Point(360, 180), true, ModifierKeys.None));
        Assert.AreEqual(cancelledX, window.Scene.Camera.CameraX, 1e-8, "An old move must not revive a cancelled camera gesture.");
    }

    private sealed class NativeRig : IDisposable
    {
        internal DangoApplicationHost Host { get; }
        internal StackedDangoSceneRuntime Runtime { get; }
        internal LocalRoomSceneDriver Driver { get; }
        internal LocalRoomWindow Window => Host.LocalWindow!;
        internal NativeRig()
        {
            var root = Path.Combine(TestRepository.Root, "artifacts", "local-room", "camera-window", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), isolatedLocalRoomReview: true);
            try
            {
                Runtime = Field<StackedDangoSceneRuntime>(Host, "stackedRuntime");
                Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
                Host.ShowControlCenter(); Host.EnterLocalRoom();
                Driver = Field<LocalRoomSceneDriver>(Host, "localRoomDriver");
                WaitReady();
            }
            catch { Host.Dispose(); throw; }
        }
        internal void WaitReady()
        {
            WaitUntil(() => !Host.LocalHandoffPending && Window.Scene.HasHealthyFrame);
            Assert.AreEqual(LocalRoomPresentation.Window, Host.LocalPresentation, Host.LocalRoomFailure);
            Assert.IsNull(Host.LocalRoomFailure);
            Window.FocusScene();
            Assert.IsTrue(Window.IsActive && Window.Scene.IsKeyboardFocusWithin);
            if (!NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(Window).Handle))
                Assert.Inconclusive("The exact room HWND did not acquire native foreground; camera input admission/capture is unverified.");
        }
        public void Dispose() => Host.Dispose();
    }

    private sealed class MenuRig : IDisposable
    {
        private readonly StackedDangoSceneRuntime runtime = StackedDangoSceneRuntime.CreateProduct();
        private readonly LocalRoomSceneDriver driver;
        internal LocalRoomWindow Window { get; }
        internal MenuRig()
        {
            driver = new(new DesktopSceneSession(), runtime);
            Window = new(driver) { Width = 640, Height = 420, Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            try { Window.Show(); Window.FocusScene(); Pump(25); Assert.IsTrue(Window.IsActive); }
            catch { Dispose(); throw; }
        }
        public void Dispose() { Window.PrepareForExit(); Window.Close(); driver.Dispose(); runtime.Dispose(); }
    }

    private static KeyEventArgs Press(LocalRoomWindow window, UIElement target, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(e); return e;
    }
    private static void CaptureMenu(LocalRoomMenuView menu)
    {
        var dpi = VisualTreeHelper.GetDpi(menu);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(menu.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(menu);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(TestRepository.Root, "artifacts", "local-room", "visual");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "window-camera-menu.png"));
        encoder.Save(stream); // Own WPF content only; this image is not a live/manual acceptance gate.
    }
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var item in VisualDescendants<T>(child)) yield return item;
        }
    }
    private static T E<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void WaitUntil(Func<bool> condition)
    {
        var until = Environment.TickCount64 + 4500;
        while (!condition() && Environment.TickCount64 < until) Pump(20);
        Assert.IsTrue(condition(), "A real window frame/focus did not satisfy its bounded readiness gate.");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
