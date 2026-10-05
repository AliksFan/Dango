using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomWindowMenuTests
{
    [STATestMethod]
    public void EscapeTogglesACentredFullWindowMenuWithoutReturningToPorch()
    {
        using var rig = new WindowRig();
        var returned = 0;
        rig.Window.ReturnRequested += () => returned++;
        var root = (Grid)rig.Window.Content;
        var overlay = Find<Grid>(root, "RoomMenuOverlay");
        var card = Find<Border>(root, "RoomMenuCard");
        Assert.AreEqual(Visibility.Collapsed, overlay.Visibility);
        Assert.AreEqual(0, Descendants<System.Windows.Controls.Menu>(root).Count(), "Gameplay must not retain the top operation menu.");
        Assert.AreEqual(2, Descendants<Button>(root).Count(button => !overlay.IsAncestorOf(button)),
            "Only the minimal hide/close window controls may stay outside the Esc menu.");

        Assert.IsTrue(Press(rig.Window, rig.Window.Scene, Key.Escape).Handled);
        rig.Window.UpdateLayout();
        Assert.IsTrue(rig.Window.IsMenuOpen);
        Assert.AreEqual(Visibility.Visible, overlay.Visibility);
        Assert.AreEqual(root.ActualWidth, overlay.ActualWidth, .1);
        Assert.AreEqual(root.ActualHeight, overlay.ActualHeight, .1);
        var origin = card.TranslatePoint(new System.Windows.Point(), root);
        Assert.AreEqual(root.ActualWidth / 2, origin.X + card.ActualWidth / 2, .1);
        Assert.AreEqual(root.ActualHeight / 2, origin.Y + card.ActualHeight / 2, .1);
        Assert.IsTrue(card.ActualHeight < root.ActualHeight, "The card must fit the accepted minimum window height.");
        Assert.IsTrue(overlay.Background is SolidColorBrush { Color.A: > 0 and < 255 }, "The room must remain visible through the full-window shade.");
        Assert.IsTrue(Find<Button>(root, "RoomContinueButton").IsKeyboardFocused);
        Assert.AreEqual(0, returned, "Esc opens the room menu; only its explicit door action returns to porch.");

        Assert.IsTrue(Press(rig.Window, Find<Button>(root, "RoomContinueButton"), Key.Escape).Handled);
        Assert.IsFalse(rig.Window.IsMenuOpen, "Esc must also work while a menu button owns focus.");
        Assert.IsFalse(rig.Window.Scene.InputBlocked);
        Assert.IsTrue(rig.Window.Scene.IsKeyboardFocused);
        Assert.AreEqual(0, returned);
    }

    [STATestMethod]
    public void OpeningMenuClearsHeldControlsAndWheelRemainderAndBlocksGameplay()
    {
        using var rig = new WindowRig();
        var scene = rig.Window.Scene;
        // Isolate the window input gate without manufacturing or claiming a native healthy frame.
        typeof(LocalRoomSceneView).GetProperty(nameof(LocalRoomSceneView.HasHealthyFrame), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, true);
        SetField(rig.Driver, "intent", new DirectionalControlIntent(false, true, true, true));
        scene.ApplyWheel(-60);
        Assert.AreEqual(-60, Field<RoomWheelAccumulator>(scene, "wheel").Remainder);
        var scale = scene.Camera.Scale;

        Press(rig.Window, scene, Key.Escape);
        Assert.IsTrue(scene.InputBlocked);
        Assert.IsFalse(scene.IsHitTestVisible);
        Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(rig.Driver, "intent"));
        Assert.AreEqual(0, Field<RoomWheelAccumulator>(scene, "wheel").Remainder);
        Assert.IsFalse(rig.Window.WheelEligible);
        foreach (var key in new[] { Key.A, Key.D, Key.S })
            Assert.IsTrue(Press(rig.Window, Find<Button>((Grid)rig.Window.Content, "RoomContinueButton"), key).Handled);
        scene.ApplyWheel(-120);
        Assert.AreEqual(scale, scene.Camera.Scale, 1e-9);
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        Find<Button>((Grid)rig.Window.Content, "RoomContinueButton").RaiseEvent(wheel);
        Assert.IsTrue(wheel.Handled, "Wheel over any menu control must end at the overlay.");
        Assert.AreEqual(scale, scene.Camera.Scale, 1e-9);

        Press(rig.Window, Find<Button>((Grid)rig.Window.Content, "RoomContinueButton"), Key.Escape);
        Assert.IsTrue(scene.IsHitTestVisible);
        scene.ApplyWheel(-60);
        Assert.AreEqual(scale, scene.Camera.Scale, 1e-9, "The old half-step must not survive opening the menu.");
        scene.ApplyWheel(-60);
        Assert.IsTrue(scene.Camera.Scale < scale, "Fresh wheel input resumes after the menu closes.");
    }

    [STATestMethod]
    public void MenuActionsDispatchExistingCommandsAndRestoreSceneFocus()
    {
        using var rig = new WindowRig();
        var returned = 0;
        var recovered = 0;
        var controlCenter = 0;
        var presentations = new List<LocalRoomPresentation>();
        rig.Window.ReturnRequested += () => returned++;
        rig.Window.RecoverRequested += () => recovered++;
        rig.Window.ControlCenterRequested += () => controlCenter++;
        rig.Window.PresentationRequested += presentations.Add;

        Choose(rig.Window, "RoomContinueButton");
        Assert.AreEqual(0, returned);
        Choose(rig.Window, "RoomReturnButton");
        Assert.AreEqual(1, returned);
        Choose(rig.Window, "RoomRecoverButton");
        Assert.AreEqual(1, recovered);
        Choose(rig.Window, "RoomControlCenterButton");
        Assert.AreEqual(1, controlCenter);
        Choose(rig.Window, "RoomWindowButton");
        Choose(rig.Window, "RoomWallpaperButton");
        CollectionAssert.AreEqual(new[] { LocalRoomPresentation.Window, LocalRoomPresentation.Wallpaper }, presentations);
        foreach (var (name, preset) in new[]
        {
            ("RoomNearViewButton", RoomViewPreset.Near),
            ("RoomWholeViewButton", RoomViewPreset.Whole),
            ("RoomStandardViewButton", RoomViewPreset.Standard),
        })
        {
            Choose(rig.Window, name);
            Assert.AreEqual(preset, rig.Window.Scene.Camera.Preset);
        }
    }

    [STATestMethod]
    public void OpeningMenuReleasesSceneCaptureAndClearsInputBeforeItsButtonsOwnFocus()
    {
        using var rig = new WindowRig();
        Assert.IsTrue(rig.Window.Scene.CaptureMouse());
        Assert.AreSame(rig.Window.Scene, Mouse.Captured);
        SetField(rig.Driver, "intent", new DirectionalControlIntent(true, false, false, true));
        rig.Window.OpenMenu();
        Assert.IsTrue(rig.Window.IsMenuOpen);
        Assert.IsNull(Mouse.Captured, "The gameplay capture must not intercept the central menu.");
        Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(rig.Driver, "intent"));
        Assert.IsTrue(Find<Button>((Grid)rig.Window.Content, "RoomContinueButton").IsKeyboardFocused);
    }

    [STATestMethod]
    public void CompletedPointerCaptureReleaseDoesNotClearStillHeldDirectionalControls()
    {
        using var rig = new WindowRig();
        Assert.IsTrue(rig.Window.Scene.CaptureMouse());
        var held = new DirectionalControlIntent(false, true, false, false);
        SetField(rig.Driver, "intent", held);
        Assert.IsFalse(rig.Driver.IsPointerActive, "A completed PointerUp leaves the pointer inactive before the window releases capture.");
        rig.Window.Scene.ReleaseMouseCapture();
        Assert.IsNull(Mouse.Captured);
        Assert.AreEqual(held, Field<DirectionalControlIntent>(rig.Driver, "intent"),
            "A normal pointer release must not route through full input cancellation while D remains held.");
    }

    [STATestMethod]
    public void HandoffFocusPreservesAnAlreadyOpenedMenuAndItsSelectedAction()
    {
        using var rig = new WindowRig();
        rig.Window.OpenMenu();
        var action = Find<Button>((Grid)rig.Window.Content, "RoomRecoverButton");
        Assert.IsTrue(action.Focus());
        // The healthy-frame completion path calls this same focus operation.
        rig.Window.FocusScene();
        Assert.IsTrue(rig.Window.IsMenuOpen, "A late healthy frame must not close a menu the user has already opened.");
        Assert.IsTrue(rig.Window.Scene.InputBlocked);
        Assert.IsTrue(action.IsKeyboardFocused, "Gameplay focus must not override the current central-menu action.");
        rig.Window.CloseMenu();
        rig.Window.FocusScene();
        Assert.IsFalse(rig.Window.IsMenuOpen);
        Assert.IsTrue(rig.Window.Scene.IsKeyboardFocused);
    }

    [STATestMethod]
    public void HidingAnOpenMenuLeavesRestoreWithUnblockedSceneFocus()
    {
        using var rig = new WindowRig();
        Press(rig.Window, rig.Window.Scene, Key.Escape);
        Assert.IsTrue(rig.Window.IsMenuOpen);
        rig.Window.Hide();
        Assert.IsFalse(rig.Window.IsMenuOpen);
        Assert.IsFalse(rig.Window.Scene.InputBlocked);
        rig.Window.Show();
        rig.Window.FocusScene();
        Pump();
        Assert.IsFalse(rig.Window.IsMenuOpen);
        Assert.IsTrue(rig.Window.Scene.IsKeyboardFocused);
    }

    private static void Choose(LocalRoomWindow window, string name)
    {
        Assert.IsTrue(Press(window, window.Scene, Key.Escape).Handled);
        Assert.IsTrue(window.IsMenuOpen);
        Find<Button>((Grid)window.Content, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(window.IsMenuOpen);
        Assert.IsFalse(window.Scene.InputBlocked);
        Assert.IsTrue(window.Scene.IsKeyboardFocused);
    }

    private static KeyEventArgs Press(LocalRoomWindow window, UIElement target, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(e);
        return e;
    }

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(element => element.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private sealed class WindowRig : IDisposable
    {
        private readonly StackedDangoSceneRuntime runtime = StackedDangoSceneRuntime.CreateProduct();
        internal LocalRoomSceneDriver Driver { get; }
        internal LocalRoomWindow Window { get; }
        internal WindowRig()
        {
            Driver = new(new DesktopSceneSession(), runtime);
            Window = new(Driver)
            {
                Width = 640, Height = 420, Left = -10000, Top = -10000,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            };
            Window.Show(); Window.FocusScene(); Pump();
            Assert.IsTrue(Window.IsActive);
            Assert.IsTrue(Window.Scene.IsKeyboardFocused);
        }
        public void Dispose()
        {
            Window.PrepareForExit(); Window.Close(); Driver.Dispose(); runtime.Dispose();
        }
    }
}
