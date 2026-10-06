using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Dango.Desktop.DesktopHost.Win32;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using StackPanel = System.Windows.Controls.StackPanel;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomWindow : Window
{
    private readonly LocalRoomSceneDriver driver;
    private readonly LocalRoomMenuView menu = new()
    {
        Name = "RoomMenu",
        Visibility = Visibility.Collapsed,
    };
    private bool allowClose;
    internal LocalRoomSceneView Scene { get; } = new();
    internal bool IsMenuOpen => menu.Visibility == Visibility.Visible;
    internal event Action? ReturnRequested;
    internal event Action? HideRequested;
    internal event Action? RecoverRequested;
    internal event Action? ControlCenterRequested;
    internal event Action<LocalRoomPresentation>? PresentationRequested;
    internal LocalRoomWindow(LocalRoomSceneDriver driver)
    {
        this.driver = driver;
        Title = "Dango · 普通房（本地单人）";
        Width = 960; Height = 600; MinWidth = 640; MinHeight = 420;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        { CaptionHeight = 36, ResizeBorderThickness = new Thickness(7), CornerRadius = new CornerRadius(14), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid();
        root.Children.Add(Scene);
        var caption = new Grid { Height = 36, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        var windowControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        windowControls.Children.Add(CreateWindowControl("RoomHideButton", "—", "隐藏房间"));
        windowControls.Children.Add(CreateWindowControl("RoomCloseButton", "×", "隐藏房间，托盘恢复"));
        caption.Children.Add(windowControls);
        root.Children.Add(caption);

        menu.ReturnRequested += () => RunMenuAction(() => ReturnRequested?.Invoke());
        menu.ControlCenterRequested += () => RunMenuAction(() => ControlCenterRequested?.Invoke());
        menu.PresentationRequested += presentation => RunMenuAction(() => PresentationRequested?.Invoke(presentation));
        menu.RecoverRequested += () => RunMenuAction(() => RecoverRequested?.Invoke());
        WindowChrome.SetIsHitTestVisibleInChrome(menu, true);
        menu.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) => { Scene.ClearWheel(); e.Handled = true; }), true);
        root.Children.Add(menu);
        Content = root;
        Scene.ViewChanged += RefreshLabel;
        Scene.SizeChanged += (_, _) => RefreshLabel();
        Deactivated += (_, _) => { driver.ClearInput(); ReleaseScenePointerCapture(); };
        LostKeyboardFocus += (_, _) => { if (!Scene.IsKeyboardFocusWithin) driver.ClearInput(); };
        Scene.MouseWheel += OnSceneWheel;
        Scene.MouseLeftButtonDown += OnScenePointerDown;
        Scene.MouseMove += OnScenePointerMove;
        Scene.MouseLeftButtonUp += OnScenePointerUp;
        Scene.LostMouseCapture += (_, _) => { if (driver.IsPointerActive) driver.ClearInput(); };
        driver.PointerCancelled += ReleaseScenePointerCapture;
        Scene.KeyDown += OnSceneKeyDown;
        Scene.KeyUp += (_, e) => { if (IsActive && Scene.IsKeyboardFocusWithin && !Scene.InputBlocked) e.Handled = driver.Key(e.Key, false, false); };
        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { driver.ClearInput(); ReleaseScenePointerCapture(); SetMenuOpen(false); }
        };
        Closing += OnClosing;
        Closed += (_, _) => driver.PointerCancelled -= ReleaseScenePointerCapture;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideRequested?.Invoke(); };
        RefreshLabel();
    }

    internal void PrepareForExit() => allowClose = true;
    private nint WindowMessage(nint hwnd, int message, nint w, nint l, ref bool handled)
    { if (message == 0x02E0) driver.ClearInput(); return 0; }
    internal void FocusScene()
    {
        Activate();
        if (IsMenuOpen)
        {
            if (!menu.IsKeyboardFocusWithin) menu.Focus();
        }
        else Scene.Focus();
    }
    internal void OpenMenu() { if (IsVisible) { Activate(); SetMenuOpen(true); } }
    internal void CloseMenu() => SetMenuOpen(false);
    internal bool WheelEligible => IsActive && IsVisible && !Scene.InputBlocked && Scene.IsMouseOver &&
        NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(this).Handle);
    private void OnSceneWheel(object sender, MouseWheelEventArgs e)
    {
        if (!WheelEligible || Keyboard.Modifiers != ModifierKeys.None) { Scene.ClearWheel(); return; }
        Scene.ApplyWheel(e.Delta);
        e.Handled = true;
        RefreshLabel();
    }

    private void OnSceneKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActive || !Scene.IsKeyboardFocusWithin || Scene.InputBlocked || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = driver.Key(e.Key, true, e.IsRepeat);
    }

    private bool PointerEligible => IsActive && IsVisible && Scene.HasHealthyFrame &&
        Scene.IsKeyboardFocusWithin && !Scene.InputBlocked &&
        NativeMethods.IsLocalRoomForeground(new WindowInteropHelper(this).Handle);

    private void OnScenePointerDown(object sender, MouseButtonEventArgs e)
    {
        Scene.Focus();
        if (!PointerEligible || !TryGetPointerShift(out var shift)) { driver.ClearInput(); return; }
        if (!driver.PointerDown(e.GetPosition(Scene), shift)) return;
        e.Handled = true;
        if (!Scene.CaptureMouse()) driver.CancelPointer();
    }

    private void OnScenePointerMove(object sender, MouseEventArgs e)
    {
        if (!driver.IsPointerActive) return;
        if (e.LeftButton != MouseButtonState.Pressed || !PointerEligible || !TryGetPointerShift(out var shift))
        { driver.ClearInput(); return; }
        e.Handled = true;
        if (!driver.PointerMove(e.GetPosition(Scene), shift)) driver.CancelPointer();
    }

    private void OnScenePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!driver.IsPointerActive) return;
        e.Handled = true;
        if (PointerEligible && TryGetPointerShift(out var shift))
        {
            if (!driver.PointerUp(e.GetPosition(Scene), shift)) driver.CancelPointer();
        }
        else driver.ClearInput();
        ReleaseScenePointerCapture();
    }

    private void ReleaseScenePointerCapture()
    {
        if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
    }

    private static bool TryGetPointerShift(out bool shift)
    {
        var modifiers = Keyboard.Modifiers;
        shift = modifiers == ModifierKeys.Shift;
        return modifiers is ModifierKeys.None or ModifierKeys.Shift;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActive || !IsKeyboardFocusWithin || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape)
        {
            if (!e.IsRepeat) SetMenuOpen(!IsMenuOpen);
            e.Handled = true;
        }
        else if (IsMenuOpen && e.Key is Key.A or Key.D or Key.S)
        {
            driver.ClearInput();
            e.Handled = true;
        }
    }

    private void SetMenuOpen(bool open)
    {
        if (open == IsMenuOpen) return;
        driver.ClearInput();
        ReleaseScenePointerCapture();
        Scene.ClearWheel();
        Scene.InputBlocked = open;
        Scene.IsHitTestVisible = !open;
        menu.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) menu.Focus();
        else if (IsVisible && IsActive) Scene.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        driver.ClearInput();
        ReleaseScenePointerCapture();
        if (!allowClose) { e.Cancel = true; HideRequested?.Invoke(); }
    }
    private void RefreshLabel()
    {
        menu.RefreshStatus(Scene);
        Scene.ToolTip = "A / D 移动 · S 轻压 · 空格跳跃 · 滚轮调视距 · Shift + 左键横移抚摸 · Esc 菜单";
    }

    private Button CreateWindowControl(string name, string label, string hint)
    {
        var button = new Button
        {
            Name = name, Content = label, ToolTip = hint, Width = 36, Height = 30,
            Margin = new Thickness(2, 3, 2, 3), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), FontSize = 16,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => { driver.ClearInput(); ReleaseScenePointerCapture(); HideRequested?.Invoke(); };
        return button;
    }

    private void RunMenuAction(Action action)
    {
        SetMenuOpen(false);
        action();
    }
}
