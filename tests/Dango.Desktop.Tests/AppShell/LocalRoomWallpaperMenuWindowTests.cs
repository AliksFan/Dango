using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomWallpaperMenuWindowTests
{
    [STATestMethod]
    public void SharedMenuKeepsOnlyTheFiveActionsAndCanTakeInitialFocusItself()
    {
        var menu = new LocalRoomMenuView();
        Assert.IsInstanceOfType<UserControl>(menu);
        Assert.IsTrue(menu.Focusable, "The shared menu itself must accept initial focus without focusing an action button.");
        var buttons = Descendants<Button>(menu).Where(button => button.Visibility != Visibility.Collapsed).ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            "RoomReturnButton", "RoomControlCenterButton", "RoomWindowButton",
            "RoomWallpaperButton", "RoomRecoverButton",
        }, buttons.Select(button => button.Name).ToArray());
        var furniture = Descendants<Button>(menu).Single(button => button.Name == "RoomFurnitureButton");
        Assert.AreEqual(Visibility.Collapsed, furniture.Visibility, "The wallpaper menu does not gain the ordinary-window furniture capability.");
        Assert.IsFalse(furniture.IsEnabled);
        foreach (var removed in new[]
        {
            "RoomContinueButton", "RoomStandardViewButton", "RoomNearViewButton", "RoomWholeViewButton",
        })
            Assert.IsFalse(Descendants<FrameworkElement>(menu).Any(element => element.Name == removed),
                "The shared menu must remove the old continuation and view-preset controls: " + removed);
    }

    [STATestMethod]
    public void EachMenuActionDispatchesItsOwnExistingCommandExactlyOnce()
    {
        var menu = new LocalRoomMenuView();
        var dispatched = new List<string>();
        menu.ReturnRequested += () => dispatched.Add("return");
        menu.ControlCenterRequested += () => dispatched.Add("control-center");
        menu.RecoverRequested += () => dispatched.Add("recover");
        menu.PresentationRequested += presentation => dispatched.Add("presentation:" + presentation);
        foreach (var (name, expected) in new[]
        {
            ("RoomReturnButton", "return"),
            ("RoomControlCenterButton", "control-center"),
            ("RoomWindowButton", "presentation:" + LocalRoomPresentation.Window),
            ("RoomWallpaperButton", "presentation:" + LocalRoomPresentation.Wallpaper),
            ("RoomRecoverButton", "recover"),
        })
        {
            dispatched.Clear();
            Find<Button>(menu, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CollectionAssert.AreEqual(new[] { expected }, dispatched.ToArray(),
                "A menu action must dispatch its actual command without also requesting a different action.");
        }
    }

    [STATestMethod]
    public void SharedMenuHasACentredCardOverATranslucentShadeAndCompleteGameplayInstructions()
    {
        var menu = new LocalRoomMenuView();
        var overlay = Find<Grid>(menu, "RoomMenuOverlay");
        var card = Find<Border>(menu, "RoomMenuCard");
        Assert.IsTrue(overlay.Background is SolidColorBrush { Color.A: > 0 and < 255 },
            "The independent menu must shade rather than replace the wallpaper behind it.");
        Assert.AreEqual(System.Windows.HorizontalAlignment.Center, card.HorizontalAlignment);
        Assert.AreEqual(VerticalAlignment.Center, card.VerticalAlignment);
        var instructions = Find<TextBlock>(menu, "RoomInstructions").Text;
        foreach (var key in new[] { "A", "D", "S" })
            Assert.IsTrue(Regex.IsMatch(instructions, @"(?<![A-Za-z])" + key + @"(?![A-Za-z])"),
                "The actual instruction text must describe gameplay key " + key + ".");
        AssertMentions(instructions, "space jump", "空格", "Space");
        AssertMentions(instructions, "wheel zoom", "滚轮", "wheel");
        AssertMentions(instructions, "Shift petting modifier", "Shift");
        AssertMentions(instructions, "left-button petting", "左键", "左鼠标", "left");
        AssertMentions(instructions, "horizontal movement", "左右", "横向", "横移", "水平", "horizontal");
        AssertMentions(instructions, "Esc menu dismissal", "Esc");
    }

    [STATestMethod]
    public void StatusRefreshDescribesTheCurrentSceneWithoutChangingItsCamera()
    {
        var menu = new LocalRoomMenuView();
        var scene = new LocalRoomSceneView();
        scene.Camera.Resize(900, 600, LocalRoomSceneView.WorldHeight, LocalRoomSceneView.SafeActivityHeight);
        scene.Camera.SelectPreset(RoomViewPreset.Near);
        var nearScale = scene.Camera.Scale;
        menu.RefreshStatus(scene);
        var label = Find<TextBlock>(menu, "RoomStatusLabel");
        AssertMentions(label.Text, "ordinary room status", "普通房");
        AssertMentions(label.Text, "local single-player scope", "本地单人");
        StringAssert.Contains(label.Text, scene.Camera.ZoomPercent.ToString("0", CultureInfo.CurrentCulture) + "%");
        Assert.AreEqual(RoomViewPreset.Near, scene.Camera.Preset);
        Assert.AreEqual(nearScale, scene.Camera.Scale, 1e-9, "A status refresh must not change the room view.");
        var before = label.Text;
        scene.Camera.SelectPreset(RoomViewPreset.Whole);
        var wholeScale = scene.Camera.Scale;
        menu.RefreshStatus(scene);
        StringAssert.Contains(label.Text, scene.Camera.ZoomPercent.ToString("0", CultureInfo.CurrentCulture) + "%");
        Assert.AreNotEqual(before, label.Text, "The status must refresh when the actual room view changes.");
        Assert.AreEqual(RoomViewPreset.Whole, scene.Camera.Preset);
        Assert.AreEqual(wholeScale, scene.Camera.Scale, 1e-9);
    }

    [STATestMethod]
    public void WallpaperMenuWindowContainsOnlyTheSharedMenuAndOwnsNoRoomRuntime()
    {
        WithUnshownWindow(window =>
        {
            Assert.IsTrue(window.AllowsTransparency);
            Assert.AreEqual(WindowStyle.None, window.WindowStyle);
            Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
            Assert.IsFalse(window.ShowInTaskbar);
            Assert.IsNull(window.Owner, "The independent wallpaper menu must not be owned by a room window.");
            Assert.AreSame(window.Menu, window.Content, "The overlay window contains the shared menu, not a second room scene.");
            Assert.HasCount(0, Descendants<LocalRoomSceneView>(window).ToArray());
            var forbidden = new[]
            {
                typeof(LocalRoomSceneView), typeof(LocalRoomSceneDriver),
                typeof(DesktopSceneSession), typeof(StackedDangoSceneRuntime), typeof(HostFrameScheduler),
            };
            foreach (var field in window.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Assert.IsFalse(forbidden.Any(type => type.IsAssignableFrom(field.FieldType)),
                    "The wallpaper menu cannot own a scene, driver, session, entity runtime or frame owner: " + field.Name);
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle,
                "This contract test must not create or activate a native window.");
        });
    }

    [STATestMethod]
    public void EscapeRequestsShellReturnOnlyForAFreshPressAndDoesNotCloseTheWindowItself()
    {
        WithUnshownWindow(window =>
        {
            var dismissed = new List<bool>();
            var closed = 0;
            window.DismissRequested += dismissed.Add;
            window.Closed += (_, _) => closed++;
            Press(window, Key.A);
            Assert.HasCount(0, dismissed);
            Press(window, Key.Escape, repeat: true);
            Assert.HasCount(0, dismissed, "Holding Esc must not issue repeated close/restore requests.");
            var escape = Press(window, Key.Escape);
            Assert.IsTrue(escape.Handled);
            CollectionAssert.AreEqual(new[] { true }, dismissed.ToArray(),
                "Only an explicit fresh Esc requests a verified Shell foreground return.");
            Assert.AreEqual(0, closed, "The host must coordinate hiding and focus recovery; the menu window cannot close itself.");
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
        });
    }

    [TestMethod]
    public void NativeRepeatedEscapeDoesNotDismissWhenWpfHasNotSeenTheOpeningDown()
    {
        var menuHandle = (nint)17;
        foreach (var message in new[] { 0x0100, 0x0104 })
            Assert.IsFalse(LocalRoomWallpaperMenuWindow.ShouldDismissEscape(
                NativeEscapeDown(menuHandle, message, wasAlreadyDown: true), menuHandle, wpfRepeat: false),
                "The first native auto-repeat after the menu gains foreground must not dismiss it even when WPF reports a fresh key.");
    }

    [TestMethod]
    public void NativeFreshEscapeDismissesWhenWpfMissedReleaseBeforeTheMenuGainedFocus()
    {
        var menuHandle = (nint)17;
        foreach (var message in new[] { 0x0100, 0x0104 })
            Assert.IsTrue(LocalRoomWallpaperMenuWindow.ShouldDismissEscape(
                NativeEscapeDown(menuHandle, message, wasAlreadyDown: false), menuHandle, wpfRepeat: true),
                "A new native Esc press must dismiss even when its earlier release happened outside the menu and WPF still reports a repeat.");
    }

    [TestMethod]
    public void MessagesOutsideTheCurrentMenuEscapeDownKeepTheWpfRepeatFallback()
    {
        var menuHandle = (nint)17;
        var unrelatedMessages = new[]
        {
            NativeEscapeDown((nint)18, 0x0100, wasAlreadyDown: true),
            new MSG { hwnd = menuHandle, message = 0x0100, wParam = (nint)0x41 },
            NativeEscapeDown(menuHandle, 0x0101, wasAlreadyDown: false),
            default(MSG),
        };
        foreach (var message in unrelatedMessages)
        {
            Assert.IsTrue(LocalRoomWallpaperMenuWindow.ShouldDismissEscape(message, menuHandle, wpfRepeat: false),
                "An unrelated native message must not veto the existing fresh WPF Esc event.");
            Assert.IsFalse(LocalRoomWallpaperMenuWindow.ShouldDismissEscape(message, menuHandle, wpfRepeat: true),
                "An unrelated native message must not turn a repeated WPF Esc event into a fresh press.");
        }
        var unshownMessage = NativeEscapeDown(nint.Zero, 0x0100, wasAlreadyDown: false);
        Assert.IsTrue(LocalRoomWallpaperMenuWindow.ShouldDismissEscape(unshownMessage, nint.Zero, wpfRepeat: false));
        Assert.IsFalse(LocalRoomWallpaperMenuWindow.ShouldDismissEscape(unshownMessage, nint.Zero, wpfRepeat: true),
            "A zero menu handle must retain the unshown-window fallback rather than match a native message.");
    }

    [STATestMethod]
    public void DeactivationRequestsDismissalWithoutShellReturnAndClosingSuppressesFurtherRequests()
    {
        WithUnshownWindow(window =>
        {
            var dismissed = new List<bool>();
            var closed = 0;
            window.DismissRequested += dismissed.Add;
            window.Closed += (_, _) => closed++;
            Deactivate(window);
            CollectionAssert.AreEqual(new[] { false }, dismissed.ToArray(),
                "Losing foreground must dismiss without reclaiming focus from another application.");
            Assert.AreEqual(0, closed);
            window.BeginClosing();
            window.BeginClosing();
            Press(window, Key.Escape);
            Deactivate(window);
            CollectionAssert.AreEqual(new[] { false }, dismissed.ToArray(),
                "Hide/host-switch teardown cannot reenter dismissal or request a later Shell return.");
            Assert.AreEqual(0, closed);
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
        });
    }

    private static MSG NativeEscapeDown(nint menuHandle, int message, bool wasAlreadyDown) => new()
    {
        hwnd = menuHandle,
        message = message,
        wParam = (nint)0x1B,
        lParam = wasAlreadyDown ? (nint)(1L << 30) : nint.Zero,
    };

    private static void WithUnshownWindow(Action<LocalRoomWallpaperMenuWindow> inspect)
    {
        var window = new LocalRoomWallpaperMenuWindow(new LogicalRect(-1920, 0, 1920, 1080));
        try { inspect(window); }
        finally { window.BeginClosing(); window.Close(); }
    }

    private static KeyEventArgs Press(LocalRoomWallpaperMenuWindow window, Key key, bool repeat = false)
    {
        // A plain PresentationSource supplies the event contract without creating an HWND or taking focus.
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new UnconnectedPresentationSource(), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(args, [repeat]);
        window.RaiseEvent(args);
        return args;
    }

    private static void Deactivate(LocalRoomWallpaperMenuWindow window) =>
        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);

    private static void AssertMentions(string text, string meaning, params string[] alternatives) =>
        Assert.IsTrue(alternatives.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)),
            "The visible menu instructions/status must include " + meaning + ".");

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(element => element.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }

    private sealed class UnconnectedPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
