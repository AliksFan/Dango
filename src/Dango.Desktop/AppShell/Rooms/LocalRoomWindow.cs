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
using Point = System.Windows.Point;

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
    private bool cameraPanActive;
    private Point lastCameraPanPoint;
    internal LocalRoomSceneView Scene { get; } = new();
    internal bool IsMenuOpen => menu.Visibility == Visibility.Visible;
    internal bool IsCameraPanActive => cameraPanActive;
    internal event Action? ReturnRequested;
    internal event Action? HideRequested;
    internal event Action? RecoverRequested;
    internal event Action? ControlCenterRequested;
    internal LocalRoomWindow(LocalRoomSceneDriver driver)
    {
        this.driver = driver;
        Scene.EnableWindowCamera();
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
        menu.RecoverRequested += () => RunMenuAction(() => RecoverRequested?.Invoke());
        menu.CameraModeRequested += mode => Scene.SelectCameraMode(mode);
        WindowChrome.SetIsHitTestVisibleInChrome(menu, true);
        menu.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) =>
        { Scene.ClearWheel(); if (!menu.IsCameraDropDownOpen) e.Handled = true; }), true);
        root.Children.Add(menu);
        Content = root;
        Scene.ViewChanged += RefreshLabel;
        Scene.SizeChanged += (_, _) => RefreshLabel();
        Deactivated += (_, _) => { driver.ClearInput(); ReleaseScenePointerCapture(); };
        LostKeyboardFocus += (_, _) => { if (!Scene.IsKeyboardFocusWithin) { CancelCameraPan(); driver.ClearInput(); } };
        Scene.MouseWheel += OnSceneWheel;
        Scene.MouseLeftButtonDown += OnScenePointerDown;
        Scene.MouseMove += OnScenePointerMove;
        Scene.MouseLeftButtonUp += OnScenePointerUp;
        Scene.MouseRightButtonDown += OnSceneCameraPanDown;
        Scene.MouseRightButtonUp += OnSceneCameraPanUp;
        Scene.InputInvalidated += CancelCameraPan;
        Scene.LostMouseCapture += (_, _) =>
        {
            if (cameraPanActive) CancelCameraPan();
            if (driver.IsPointerActive) driver.ClearInput();
        };
        driver.PointerCancelled += ReleaseScenePointerCapture;
        Scene.KeyDown += OnSceneKeyDown;
        Scene.KeyUp += (_, e) => { if (IsActive && Scene.IsKeyboardFocusWithin && !Scene.InputBlocked) e.Handled = driver.Key(e.Key, false, false); };
        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { driver.ClearInput(); ReleaseScenePointerCapture(); SetMenuOpen(false); }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        { driver.PointerCancelled -= ReleaseScenePointerCapture; Scene.InputInvalidated -= CancelCameraPan; };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideRequested?.Invoke(); };
        RefreshLabel();
    }

    internal void PrepareForExit() => allowClose = true;
    private nint WindowMessage(nint hwnd, int message, nint w, nint l, ref bool handled)
    { if (message == 0x02E0) { CancelCameraPan(); driver.ClearInput(); } return 0; }
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
        if (cameraPanActive) { CancelCameraPan(); e.Handled = true; return; }
        Scene.Focus();
        if (!PointerEligible || !TryGetPointerShift(out var shift)) { driver.ClearInput(); return; }
        if (!driver.PointerDown(e.GetPosition(Scene), shift)) return;
        e.Handled = true;
        if (!Scene.CaptureMouse()) driver.CancelPointer();
    }

    private void OnScenePointerMove(object sender, MouseEventArgs e)
    {
        if (cameraPanActive)
        {
            e.Handled = MoveCameraPan(e.GetPosition(Scene), e.RightButton == MouseButtonState.Pressed, Keyboard.Modifiers);
            return;
        }
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
        cameraPanActive = false;
        if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
    }

    private void OnSceneCameraPanDown(object sender, MouseButtonEventArgs e)
    {
        if (!Scene.InputBlocked) Scene.Focus();
        e.Handled = TryBeginCameraPan(e.GetPosition(Scene), Keyboard.Modifiers, e.LeftButton == MouseButtonState.Pressed);
    }

    private void OnSceneCameraPanUp(object sender, MouseButtonEventArgs e)
    {
        if (!cameraPanActive) return;
        MoveCameraPan(e.GetPosition(Scene), true, Keyboard.Modifiers);
        EndCameraPan();
        e.Handled = true;
    }

    internal bool TryBeginCameraPan(Point sceneDip, ModifierKeys modifiers, bool leftPressed)
    {
        if (cameraPanActive || leftPressed || driver.IsPointerActive || !CameraPanEligible(modifiers) ||
            !IsCameraPanStartPoint(sceneDip) || Mouse.Captured is { } captured && captured != Scene) return false;
        if (!Scene.CaptureMouse()) return false;
        if (!CameraPanEligible(modifiers) || driver.IsPointerActive || Mouse.Captured != Scene)
        {
            if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
            return false;
        }
        lastCameraPanPoint = sceneDip;
        cameraPanActive = true;
        return true;
    }

    internal bool MoveCameraPan(Point sceneDip, bool rightPressed, ModifierKeys modifiers)
    {
        if (!cameraPanActive) return false;
        if (!rightPressed || !CameraPanEligible(modifiers) || Mouse.Captured != Scene ||
            !double.IsFinite(sceneDip.X) || !double.IsFinite(sceneDip.Y))
        { CancelCameraPan(); return false; }
        var delta = sceneDip - lastCameraPanPoint;
        lastCameraPanPoint = sceneDip;
        Scene.PanCamera(delta.X, delta.Y);
        return true;
    }

    internal bool EndCameraPan()
    {
        if (!cameraPanActive) return false;
        CancelCameraPan(); // Normal completion does not clear directional controls.
        return true;
    }

    private void CancelCameraPan()
    {
        if (!cameraPanActive) return;
        cameraPanActive = false;
        if (Mouse.Captured == Scene) Scene.ReleaseMouseCapture();
    }

    private bool CameraPanEligible(ModifierKeys modifiers) => PointerEligible && driver.OwnsRuntime &&
        Scene.Camera.CameraMode == RoomCameraMode.Manual && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    private bool IsCameraPanStartPoint(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        point.X >= 7 && point.X < Scene.ActualWidth - 7 && point.Y >= 36 && point.Y < Scene.ActualHeight - 7;

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
            if (!e.IsRepeat && !(IsMenuOpen && menu.CloseCameraDropDown())) SetMenuOpen(!IsMenuOpen);
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
        if (Scene.Camera.CameraMode == RoomCameraMode.Manual) Scene.ToolTip += " · 右键拖动视角";
    }

    private Button CreateWindowControl(string name, string label, string hint)
    {
        var button = new Button
        {
            Name = name,
            Content = label,
            ToolTip = hint,
            Width = 36,
            Height = 30,
            Margin = new Thickness(2, 3, 2, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 16,
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
