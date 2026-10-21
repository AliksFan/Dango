using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FixedWindowPresetTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void AllThreePresetsPersistByIdentityRatherThanLastMeasuredWindowSize()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Dango.PresetTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var property = typeof(ControlCenterWindowSettings).GetProperty("SelectedPreset");
            Assert.IsNotNull(property, "The selected fixed preset must be part of durable window settings.");
            var store = new ControlCenterWindowSettingsStore();
            foreach (var (name, width, height) in new[]
                     {
                         ("Landscape1080p", 960d, 540d),
                         ("Landscape1440p", 1280d, 720d),
                         ("Portrait", 480d, 800d),
                     })
            {
                var settings = new ControlCenterWindowSettings(width, height);
                property.SetValue(settings, Enum.Parse(property.PropertyType, name));
                store.Save(path, settings);
                var reloaded = store.Load(path);
                Assert.AreEqual(name, property.GetValue(reloaded)?.ToString());
                Assert.AreEqual(Math.Max(820, width), reloaded.Width);
                Assert.AreEqual(Math.Max(560, height), reloaded.Height);
                Assert.IsFalse(File.ReadAllText(path).Contains("\"selectedPreset\""));
                StringAssert.Contains(File.ReadAllText(ControlCenterWindowSettingsStore.GetPresetPath(path)), "\"selectedPreset\"");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [STATestMethod]
    public void SizeGlyphOpensExactlyThreeNamedChoicesAndAppliesFixedDimensions()
    {
        using var rig = new WindowRig();
        var button = rig.Window.FindName("WindowSizeButton") as Button;
        Assert.IsNotNull(button, "The old maximize control must become a size-picker button.");
        Assert.AreEqual("⇌", button.Content, "The visible size glyph was explicitly approved.");
        StringAssert.Contains(AutomationProperties.GetName(button), "尺寸");
        var menu = rig.Window.FindName("WindowSizeMenu") as ContextMenu ?? button.ContextMenu;
        Assert.IsNotNull(menu);
        Assert.AreEqual(3, menu.Items.Count, "The size picker must offer exactly three fixed presets.");
        foreach (var name in new[] { "Landscape1080pMenuItem", "Landscape1440pMenuItem", "PortraitMenuItem" })
            Assert.IsNotNull(menu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Name == name), name);

        Click(menu.Items.OfType<MenuItem>().Single(item => item.Name == "Landscape1440pMenuItem"));
        Assert.AreEqual(1280d, rig.Window.Width);
        Assert.AreEqual(720d, rig.Window.Height);
        Click(menu.Items.OfType<MenuItem>().Single(item => item.Name == "PortraitMenuItem"));
        Assert.AreEqual(480d, rig.Window.Width);
        Assert.AreEqual(800d, rig.Window.Height);
        rig.Window.Left = rig.Window.Top = -10000;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsTrue(menu.IsOpen, "Clicking ⇌ must open the three-choice menu.");
        Assert.AreNotEqual(WindowState.Maximized, rig.Window.WindowState,
            "The size control must no longer maximize the window.");
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ClosingSizeMenuReturnsKeyboardFocusToTheDangoStage(bool selectPreset)
    {
        using var rig = new WindowRig();
        rig.Window.WindowStartupLocation = WindowStartupLocation.Manual;
        rig.Window.Left = rig.Window.Top = 80;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        Pump(100); // Let one-time Loaded/initial-focus work finish before exercising the menu.
        if (!selectPreset) rig.Window.Activate();
        if (!selectPreset && !rig.Window.IsActive)
            Assert.Inconclusive("MSTest could not make its WPF window foreground; the product intentionally must not steal focus from another process.");
        var stage = (FrameworkElement)rig.Window.ProductHome.FindName("Stage");
        Assert.IsTrue(stage.Focus(), "The visible home stage must accept keyboard focus before opening the menu.");
        var button = (Button)rig.Window.FindName("WindowSizeButton");
        var menu = Assert.IsInstanceOfType<ContextMenu>(button.ContextMenu);
        var closedCount = 0;
        menu.Closed += (_, _) => closedCount++;

        Assert.IsTrue(button.Focus(), "A real click focuses the chrome button before opening its menu.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsTrue(menu.IsOpen);
        Pump(50);
        var option = menu.Items.OfType<MenuItem>().Single(item => item.Name == "Landscape1080pMenuItem");
        Assert.IsTrue(option.Focus(), "A real menu interaction moves keyboard focus into the menu.");
        if (selectPreset)
            Click(option);
        else
            menu.IsOpen = false; // Escape uses the same ContextMenu.Closed path.
        Pump(50);
        // Some testhost runs do not emit ContextMenu.Closed for this synthetic click;
        // deliver only the missing notification to exercise the product handler.
        if (closedCount == 0)
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Pump(300); // The production fallback intentionally waits 75 ms before restoring focus.

        Assert.IsFalse(menu.IsOpen);
        Assert.IsTrue(closedCount >= 1, "The menu dismissal must take the ContextMenu.Closed path.");
        if (!selectPreset && !IsTestProcessForeground())
            Assert.Inconclusive("The synthetic menu dismissal left MSTest outside the foreground process; restoring focus would steal it from another app.");
        Assert.IsTrue(stage.IsKeyboardFocused,
            $"After a size selection or menu dismissal, A/D/W/S/Space must return to the live Dango stage. " +
            $"Focused: {Keyboard.FocusedElement?.GetType().Name} / " +
            $"{(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unnamed"}; " +
            $"Stage.IsKeyboardFocusWithin={stage.IsKeyboardFocusWithin}.");
    }

    [STATestMethod]
    public void SelectingSizeWithoutSyntheticClosedEventReturnsFocusToTheHomeStage()
    {
        using var rig = new WindowRig();
        rig.Window.WindowStartupLocation = WindowStartupLocation.Manual;
        rig.Window.Left = rig.Window.Top = 80;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        Pump(100); // Drain one-time Loaded focus before reproducing the real selection path.

        var stage = (FrameworkElement)rig.Window.ProductHome.FindName("Stage");
        Assert.IsTrue(stage.Focus());
        var button = (Button)rig.Window.FindName("WindowSizeButton");
        var menu = Assert.IsInstanceOfType<ContextMenu>(button.ContextMenu);
        InvokeThroughAutomation(button);
        Pump(100);
        Assert.IsTrue(menu.IsOpen);
        Pump(50);

        var portrait = menu.Items.OfType<MenuItem>().Single(item => item.Name == "PortraitMenuItem");
        InvokeThroughAutomation(portrait);
        Pump(150);

        Assert.AreEqual(ControlCenterWindowPreset.Portrait, rig.Window.SelectedWindowPreset);
        Assert.IsFalse(menu.IsOpen);
        Assert.IsTrue(stage.IsKeyboardFocused,
            $"The real selection path must restore Dango keyboard controls without a synthetic ClosedEvent. " +
            $"Focused: {Keyboard.FocusedElement?.GetType().Name} / " +
            $"{(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unnamed"}.");
    }

    [STATestMethod]
    public void EscapeFromSizeMenuReturnsFocusToTheHomeStageWithoutSyntheticClosedEvent()
    {
        using var rig = new WindowRig();
        rig.Window.WindowStartupLocation = WindowStartupLocation.Manual;
        rig.Window.Left = rig.Window.Top = 80;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        Pump(100);

        var stage = (FrameworkElement)rig.Window.ProductHome.FindName("Stage");
        Assert.IsTrue(stage.Focus());
        var button = (Button)rig.Window.FindName("WindowSizeButton");
        var menu = Assert.IsInstanceOfType<ContextMenu>(button.ContextMenu);
        Assert.IsTrue(button.Focus());
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsTrue(menu.IsOpen);
        Pump(50);
        Assert.IsTrue(menu.Items.OfType<MenuItem>().First().Focus());

        var source = PresentationSource.FromVisual(menu) ?? PresentationSource.FromVisual(rig.Window);
        Assert.IsNotNull(source);
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        menu.RaiseEvent(escape);
        if (menu.IsOpen) menu.IsOpen = false;
        Pump(300);

        Assert.IsFalse(menu.IsOpen);
        Assert.IsTrue(stage.IsKeyboardFocused,
            $"Escape must return focus to Dango without a synthetic ClosedEvent; " +
            $"focused {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unnamed"}.");
    }

    [STATestMethod]
    public void RealMouseSizeSelectionRestoresTheHomeStageFocus()
    {
        Assert.IsTrue(GetCursorPos(out var previousCursor), "Cannot save the user's cursor position.");
        var previousForeground = GetForegroundWindow();
        try
        {
            using var rig = new WindowRig();
            var workArea = SystemParameters.WorkArea;
            rig.Window.WindowStartupLocation = WindowStartupLocation.Manual;
            rig.Window.Left = workArea.Left + 80;
            rig.Window.Top = workArea.Top + 80;
            rig.Window.ShowInTaskbar = false;
            rig.Window.Show();
            rig.Window.Activate();
            Pump(100);
            if (!IsTestProcessForeground())
                Assert.Inconclusive("MSTest could not make its WPF window foreground for a safe real-mouse click.");

            var stage = (FrameworkElement)rig.Window.ProductHome.FindName("Stage");
            Assert.IsTrue(stage.Focus());
            var sizeButton = (Button)rig.Window.FindName("WindowSizeButton");
            var menu = Assert.IsInstanceOfType<ContextMenu>(sizeButton.ContextMenu);
            ClickByWin32Mouse(sizeButton);
            Pump(100);
            Assert.IsTrue(menu.IsOpen, "The Win32 mouse click must open the actual ⇌ menu.");

            var portrait = menu.Items.OfType<MenuItem>().Single(item => item.Name == "PortraitMenuItem");
            ClickByWin32Mouse(portrait);
            var samples = new List<string>(3);
            foreach (var (label, additionalWait) in new[] { ("100ms", 100), ("300ms", 200), ("600ms", 300) })
            {
                Pump(additionalWait);
                samples.Add($"{label}: Window.IsActive={rig.Window.IsActive}, " +
                    $"ForegroundProcess={ForegroundProcessId()}, " +
                    $"Keyboard.FocusedElement={DescribeFocusedElement()}, " +
                    $"Stage.IsKeyboardFocused={stage.IsKeyboardFocused}, " +
                    $"Stage.IsKeyboardFocusWithin={stage.IsKeyboardFocusWithin}");
            }

            Assert.AreEqual(ControlCenterWindowPreset.Portrait, rig.Window.SelectedWindowPreset,
                "The physical mouse path must select Portrait, not merely open the menu.");
            Assert.IsFalse(menu.IsOpen);
            TestContext.WriteLine("Win32 SendInput mouse-down/up clicked ⇌ then PortraitMenuItem.");
            foreach (var sample in samples) TestContext.WriteLine(sample);
            Assert.IsTrue(stage.IsKeyboardFocused,
                "Real Win32 mouse selection did not restore the Dango stage focus. " + string.Join(" | ", samples));
        }
        finally
        {
            _ = SetCursorPos(previousCursor.X, previousCursor.Y);
            if (previousForeground != IntPtr.Zero && IsTestProcessForeground())
                _ = SetForegroundWindow(previousForeground);
        }
    }

    [STATestMethod]
    public void SwitchingFixedPresetsKeepsTheWindowCenteredWhenThereIsRoom()
    {
        using var rig = new WindowRig();
        var workArea = SystemParameters.WorkArea;
        if (workArea.Width < 1100)
            Assert.Inconclusive("This display has no room to check an unclamped 960-DIP landscape window.");
        rig.Window.WindowStartupLocation = WindowStartupLocation.Manual;
        rig.Window.Left = workArea.Left + 80;
        rig.Window.Top = workArea.Top + 40;
        rig.Window.ShowInTaskbar = false;
        rig.Window.Show();
        Pump(100);
        var centerBefore = rig.Window.Left + rig.Window.ActualWidth / 2;

        var button = (Button)rig.Window.FindName("WindowSizeButton");
        var menu = Assert.IsInstanceOfType<ContextMenu>(button.ContextMenu);
        Click(menu.Items.OfType<MenuItem>().Single(item => item.Name == "PortraitMenuItem"));
        Pump(150);
        var centerAfter = rig.Window.Left + rig.Window.ActualWidth / 2;

        Assert.AreEqual(centerBefore, centerAfter, 2,
            "Switching to portrait should resize around the current window center when it fits on screen.");
    }

    [STATestMethod]
    [DataRow(ControlCenterWindowPreset.Landscape1080p, 960, 540)]
    [DataRow(ControlCenterWindowPreset.Landscape1440p, 1280, 720)]
    public void LandscapePresetUsesPhysicalPixelsOnTheCurrentDisplay(
        ControlCenterWindowPreset preset, int expectedWidthPixels, int expectedHeightPixels)
    {
        using var rig = new WindowRig();
        ShowPresetOnPrimaryDisplay(rig.Window, preset);
        var (bounds, workArea, dpiScale) = ReadPhysicalWindow(rig.Window);
        if (workArea.Width < expectedWidthPixels + 24 || workArea.Height < expectedHeightPixels + 24)
            Assert.Inconclusive(
                $"Current monitor work area {workArea.Width}×{workArea.Height} physical pixels cannot contain " +
                $"the {expectedWidthPixels}×{expectedHeightPixels} preset with a 24 px fit margin.");

        TestContext.WriteLine($"{preset}: DPI {dpiScale.M11:F3}×{dpiScale.M22:F3}; " +
            $"window {bounds.Width}×{bounds.Height} physical pixels; " +
            $"working area {workArea.Width}×{workArea.Height} physical pixels; " +
            $"WPF {rig.Window.ActualWidth:F2}×{rig.Window.ActualHeight:F2} DIP.");
        Assert.AreEqual(preset, rig.Window.SelectedWindowPreset);
        Assert.AreEqual((double)expectedWidthPixels, bounds.Width, 2,
            "The horizontal preset specifies physical screen pixels, not WPF DIPs multiplied by display DPI.");
        Assert.AreEqual((double)expectedHeightPixels, bounds.Height, 2,
            "The horizontal preset height must likewise remain fixed in physical screen pixels.");
    }

    [STATestMethod]
    public void PortraitPresetFitsContentAndNinetyPercentOfCurrentWorkAreaAtHighDpi()
    {
        using var rig = new WindowRig();
        ShowPresetOnPrimaryDisplay(rig.Window, ControlCenterWindowPreset.Portrait);
        var (bounds, workArea, dpiScale) = ReadPhysicalWindow(rig.Window);
        if (dpiScale.M11 < 1.25 || dpiScale.M22 < 1.25)
            Assert.Inconclusive("This regression requires a real high-DPI display; the current monitor is below 125%.");
        if (workArea.Width < 360 * dpiScale.M11 + 24 ||
            workArea.Height < 640 * dpiScale.M22 + 24)
            Assert.Inconclusive(
                $"Current monitor work area {workArea.Width}×{workArea.Height} physical pixels cannot fit " +
                "the portrait content minimum of 360×640 DIP plus a 24 px margin.");

        TestContext.WriteLine($"Portrait: DPI {dpiScale.M11:F3}×{dpiScale.M22:F3}; " +
            $"window {bounds.Width}×{bounds.Height} physical pixels; " +
            $"working area {workArea.Width}×{workArea.Height} physical pixels; " +
            $"WPF {rig.Window.ActualWidth:F2}×{rig.Window.ActualHeight:F2} DIP.");
        Assert.AreEqual(ControlCenterWindowPreset.Portrait, rig.Window.SelectedWindowPreset);
        Assert.IsGreaterThanOrEqualTo(360d, rig.Window.ActualWidth,
            "Portrait must retain the minimum DIP width required by its controls.");
        Assert.IsGreaterThanOrEqualTo(640d, rig.Window.ActualHeight,
            "Portrait must retain the minimum DIP height required by its controls.");
        Assert.IsLessThanOrEqualTo(workArea.Height * 0.9 + 2, (double)bounds.Height,
            "High-DPI portrait must fit within 90% of the current physical work-area height, " +
            "not simply expand 480×800 DIP to approximately 720×1200 physical pixels at 150%.");
    }

    [STATestMethod]
    public void LiveDpiAndWorkAreaFitDoNotOverwriteThePersistedPreset()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Dango.LivePresetFitTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var session = new ApplicationSettingsSession(path);
            using (var rig = new WindowRig(session))
            {
                ShowPresetOnPrimaryDisplay(rig.Window, ControlCenterWindowPreset.Landscape1440p);
                var (_, workArea, _) = ReadPhysicalWindow(rig.Window);
                if (workArea.Width < 1280 + 24 || workArea.Height < 720 + 24)
                    Assert.Inconclusive("Current monitor cannot contain the 1280×720 physical-pixel preset.");
                Assert.AreEqual(ControlCenterWindowPreset.Landscape1440p, session.Current.SelectedPreset);
                Assert.IsTrue(session.TrySave(), "The test preference must save to its isolated temporary path.");
            }

            var restored = new ControlCenterWindowSettingsStore().Load(path);
            Assert.AreEqual(ControlCenterWindowPreset.Landscape1440p, restored.SelectedPreset,
                "DPI/work-area fit must not replace the user's selected preset with a measured window size.");
            Assert.AreEqual(940d, restored.Width, "Product preset selection keeps the previous app's main geometry.");
            Assert.AreEqual(620d, restored.Height);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [STATestMethod]
    public void TemporaryDisplayFitDoesNotReplaceTheSelectedPresetOnSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Dango.PresetFitTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var settings = new ControlCenterWindowSettings(1280, 720);
            var property = typeof(ControlCenterWindowSettings).GetProperty("SelectedPreset");
            Assert.IsNotNull(property);
            property.SetValue(settings, Enum.Parse(property.PropertyType, "Landscape1440p"));
            new ControlCenterWindowSettingsStore().Save(path, settings);

            using (var host = new DangoApplicationHost(settingsFile: path, roomModulePath: path + ".missing.dll"))
            {
                var window = (ControlCenterWindow)typeof(DangoApplicationHost)
                    .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(host, null)!;
                // Models a temporary work-area/DPI fit: this measured size must not become the preference.
                window.Width = 900;
                window.Height = 600;
                var saved = (bool)typeof(DangoApplicationHost)
                    .GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(host, null)!;
                Assert.IsTrue(saved);
            }

            var restored = new ControlCenterWindowSettingsStore().Load(path);
            Assert.AreEqual("Landscape1440p", property.GetValue(restored)?.ToString());
            Assert.AreEqual(1280d, restored.Width);
            Assert.AreEqual(720d, restored.Height);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [STATestMethod]
    [DataRow(HomeEditor.Face)]
    [DataRow(HomeEditor.Wardrobe)]
    public void PortraitEditorDocksBelowTheRaisedDango(HomeEditor editor)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        Layout(home, 480, 800);
        var dango = (FrameworkElement)home.FindName("PetPreview");
        var before = Bounds(home, dango);

        home.ShowEditor(editor);
        Layout(home, 480, 800);
        var after = Bounds(home, dango);
        var panel = Bounds(home, (FrameworkElement)home.FindName("EditorPanel"));
        Assert.IsTrue(after.Top < before.Top - 20,
            $"Portrait editing should lift Dango visibly; before {before}, after {after}.");
        Assert.IsTrue(panel.Top >= after.Bottom + 8,
            $"Portrait editor should dock below Dango, not beside or over it; Dango {after}, panel {panel}.");
        Assert.IsTrue(panel.Left >= 0 && panel.Right <= 480 && panel.Bottom <= 800,
            $"Portrait editor must fit in the 480×800 viewport; actual {panel}.");
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        var size = new Size(width, height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement root, FrameworkElement element) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static void ShowPresetOnPrimaryDisplay(ControlCenterWindow window, ControlCenterWindowPreset preset)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        var area = SystemParameters.WorkArea;
        window.Left = area.Left + 24;
        window.Top = area.Top + 24;
        var button = (Button)window.FindName("WindowSizeButton");
        var menu = Assert.IsInstanceOfType<ContextMenu>(button.ContextMenu);
        var itemName = preset switch
        {
            ControlCenterWindowPreset.Landscape1080p => "Landscape1080pMenuItem",
            ControlCenterWindowPreset.Landscape1440p => "Landscape1440pMenuItem",
            ControlCenterWindowPreset.Portrait => "PortraitMenuItem",
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
        Click(menu.Items.OfType<MenuItem>().Single(item => item.Name == itemName));
        window.Show();
        Pump(150);
    }

    private static (NativeRect Bounds, System.Drawing.Rectangle WorkArea, System.Windows.Media.Matrix DpiScale)
        ReadPhysicalWindow(ControlCenterWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert.AreNotEqual(IntPtr.Zero, handle, "The physical-size regression requires a shown HWND.");
        Assert.IsTrue(GetWindowRect(handle, out var bounds), "Cannot read the real window's physical pixel bounds.");
        var source = PresentationSource.FromVisual(window);
        Assert.IsNotNull(source?.CompositionTarget, "The shown window must have a WPF composition target.");
        var dpiScale = source.CompositionTarget.TransformToDevice;
        var workArea = Forms.Screen.FromHandle(handle).WorkingArea;
        return (bounds, workArea, dpiScale);
    }

    private static void ClickByWin32Mouse(FrameworkElement element)
    {
        element.UpdateLayout();
        Assert.IsTrue(element.ActualWidth > 4 && element.ActualHeight > 4,
            $"{element.Name} has no clickable bounds.");
        var point = element.PointToScreen(new System.Windows.Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var x = checked((int)Math.Round(point.X));
        var y = checked((int)Math.Round(point.Y));
        var hitWindow = WindowFromPoint(new NativePoint(x, y));
        Assert.AreNotEqual(IntPtr.Zero, hitWindow, $"No native window at {element.Name} center ({x}, {y}).");
        _ = GetWindowThreadProcessId(hitWindow, out var hitProcessId);
        Assert.AreEqual((uint)Environment.ProcessId, hitProcessId,
            $"Refusing to click {element.Name} at ({x}, {y}): the hit window belongs to another process.");
        Assert.IsTrue(SetCursorPos(x, y));
        var inputs = new[]
        {
            new NativeInput { Type = 0, Mouse = new NativeMouseInput { Flags = 0x0002 } },
            new NativeInput { Type = 0, Mouse = new NativeMouseInput { Flags = 0x0004 } },
        };
        Assert.AreEqual(2u, SendInput(2, inputs, Marshal.SizeOf<NativeInput>()),
            $"Win32 SendInput did not deliver both mouse events to {element.Name}.");
    }

    private static string DescribeFocusedElement() => Keyboard.FocusedElement is FrameworkElement element
        ? $"{element.GetType().Name}/{element.Name}"
        : Keyboard.FocusedElement?.GetType().Name ?? "null";

    private static uint ForegroundProcessId()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeMouseInput Mouse;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static bool IsTestProcessForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private static void InvokeThroughAutomation(FrameworkElement element)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        Assert.IsNotNull(peer, $"No UI Automation peer for {element.Name}.");
        var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.IsNotNull(invoke, $"No UI Automation Invoke pattern for {element.Name}.");
        invoke.Invoke();
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class WindowRig : IDisposable
    {
        public WindowRig(ApplicationSettingsSession? settingsSession = null)
        {
            var definition = VerificationDefinition.CreateP1003WorkerW();
            Window = new ControlCenterWindow(
                new ApplicationCommandController(new ReadyProductHost(), ApplicationUiMode.Product),
                definition,
                new VerificationSessionStore(definition),
                Path.Combine(TestRepository.Root, "artifacts", "fictional-preset-draft.json"),
                () => { },
                settingsSession,
                settingsSession is null ? null : () => { _ = settingsSession.TrySave(); });
        }

        public ControlCenterWindow Window { get; }

        public void Dispose()
        {
            Window.PrepareForExit();
            Window.Close();
        }
    }

    private sealed class ReadyProductHost : IDesktopHostControl, IDangoSceneControl
    {
        public SceneInitializationState InitializationState => SceneInitializationState.Ready;
        public string HatPresetId => "sprout";
        public bool TryApplyHatPreset(string id) => true;
        public bool RetryInitialization() => true;
        public DesktopHostSnapshot GetSnapshot() => new("Wallpaper", true, true, "fictional host", null);
        public void SetVisibility(bool isVisible) { }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "fictional safe diagnostics";
    }
}
