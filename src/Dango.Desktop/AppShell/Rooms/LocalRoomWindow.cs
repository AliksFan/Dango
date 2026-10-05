using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Dango.Desktop.DesktopHost.Win32;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomWindow : Window
{
    private readonly LocalRoomSceneDriver driver;
    private readonly TextBlock viewLabel = new() { Margin = new Thickness(0, 6, 0, 14), FontSize = 12, Foreground = Brushes.DimGray };
    private readonly Grid menuOverlay = new()
    {
        Name = "RoomMenuOverlay",
        Background = new SolidColorBrush(Color.FromArgb(150, 34, 30, 26)),
        Visibility = Visibility.Collapsed,
    };
    private readonly Button continueButton;
    private bool allowClose;
    internal LocalRoomSceneView Scene { get; } = new();
    internal bool IsMenuOpen => menuOverlay.Visibility == Visibility.Visible;
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

        var menuContent = new StackPanel();
        menuContent.Children.Add(new TextBlock { Text = "房间菜单", FontSize = 22, FontWeight = FontWeights.SemiBold });
        menuContent.Children.Add(viewLabel);
        continueButton = CreateMenuButton("RoomContinueButton", "继续游戏", () => { });
        menuContent.Children.Add(continueButton);
        menuContent.Children.Add(CreateActionRow(
            CreateMenuButton("RoomReturnButton", "回门廊", () => ReturnRequested?.Invoke()),
            CreateMenuButton("RoomControlCenterButton", "控制中心", () => ControlCenterRequested?.Invoke())));
        menuContent.Children.Add(CreateSectionLabel("显示方式"));
        menuContent.Children.Add(CreateActionRow(
            CreateMenuButton("RoomWindowButton", "窗口", () => PresentationRequested?.Invoke(LocalRoomPresentation.Window)),
            CreateMenuButton("RoomWallpaperButton", "壁纸", () => PresentationRequested?.Invoke(LocalRoomPresentation.Wallpaper))));
        menuContent.Children.Add(CreateMenuButton("RoomRecoverButton", "恢复团子", () => RecoverRequested?.Invoke()));
        menuContent.Children.Add(CreateSectionLabel("视角"));
        menuContent.Children.Add(CreateActionRow(
            CreateMenuButton("RoomStandardViewButton", "标准", () => Scene.SelectView(RoomViewPreset.Standard)),
            CreateMenuButton("RoomNearViewButton", "近景", () => Scene.SelectView(RoomViewPreset.Near)),
            CreateMenuButton("RoomWholeViewButton", "全景", () => Scene.SelectView(RoomViewPreset.Whole))));
        menuContent.Children.Add(new TextBlock { Text = "Esc 收起菜单", FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) });
        var card = new Border
        {
            Name = "RoomMenuCard", Width = 388, Padding = new Thickness(22, 18, 22, 18),
            CornerRadius = new CornerRadius(14), Background = Brushes.WhiteSmoke,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = menuContent,
        };
        menuOverlay.Children.Add(card);
        WindowChrome.SetIsHitTestVisibleInChrome(menuOverlay, true);
        KeyboardNavigation.SetTabNavigation(menuOverlay, KeyboardNavigationMode.Cycle);
        menuOverlay.PreviewMouseWheel += (_, e) => { Scene.ClearWheel(); e.Handled = true; };
        root.Children.Add(menuOverlay);
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
            if (!menuOverlay.IsKeyboardFocusWithin) continueButton.Focus();
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
        menuOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) continueButton.Focus();
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
        viewLabel.Text = $"普通房 · 本地单人   视距 {Scene.Camera.ZoomPercent:0}%" + (Scene.IsHeightLimited ? " · 受窗口高度限制" : "");
        Scene.ToolTip = "A / D 移动 · S 轻压 · 空格跳跃 · 滚轮调视距 · Esc 菜单";
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

    private Button CreateMenuButton(string name, string label, Action action)
    {
        var button = new Button { Name = name, Content = label, Height = 32, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 0, 8, 0) };
        button.Click += (_, _) => { SetMenuOpen(false); action(); };
        return button;
    }

    private static Grid CreateActionRow(params Button[] buttons)
    {
        var row = new Grid();
        for (var i = 0; i < buttons.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(buttons[i], i);
            buttons[i].Margin = new Thickness(i == 0 ? 0 : 4, 0, i == buttons.Length - 1 ? 0 : 4, 8);
            row.Children.Add(buttons[i]);
        }
        return row;
    }

    private static TextBlock CreateSectionLabel(string label) => new() { Text = label, FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 5) };
}
