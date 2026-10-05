using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Dango.Desktop.DesktopHost.Win32;
using Menu = System.Windows.Controls.Menu;
using MenuItem = System.Windows.Controls.MenuItem;
using DockPanel = System.Windows.Controls.DockPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Shell;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomWindow : Window
{
    private readonly LocalRoomSceneDriver driver;
    private readonly TextBlock viewLabel = new() { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private bool allowClose;
    internal LocalRoomSceneView Scene { get; } = new();
    internal event Action? ReturnRequested;
    internal event Action? HideRequested;
    internal event Action? RecoverRequested;
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
        var root = new DockPanel();
        var toolbar = new DockPanel { Height = 36, Background = System.Windows.Media.Brushes.WhiteSmoke };
        DockPanel.SetDock(toolbar, Dock.Top);
        var menu = new Menu();
        WindowChrome.SetIsHitTestVisibleInChrome(menu, true);
        var close = new Button { Content = "×", Width = 36, ToolTip = "隐藏房间，托盘恢复" };
        close.Click += (_, _) => HideRequested?.Invoke();
        DockPanel.SetDock(close, Dock.Right); WindowChrome.SetIsHitTestVisibleInChrome(close, true); toolbar.Children.Add(close);
        var minimize = new Button { Content = "—", Width = 36, ToolTip = "隐藏房间" };
        minimize.Click += (_, _) => HideRequested?.Invoke();
        DockPanel.SetDock(minimize, Dock.Right); WindowChrome.SetIsHitTestVisibleInChrome(minimize, true); toolbar.Children.Add(minimize);
        var door = new MenuItem { Header = "房门" };
        door.Click += (_, _) => ReturnRequested?.Invoke();
        menu.Items.Add(door);
        var presentation = new MenuItem { Header = "显示方式" };
        Add(presentation, "窗口", () => PresentationRequested?.Invoke(LocalRoomPresentation.Window));
        Add(presentation, "壁纸", () => PresentationRequested?.Invoke(LocalRoomPresentation.Wallpaper));
        menu.Items.Add(presentation);
        var recover = new MenuItem { Header = "恢复团子" };
        recover.Click += (_, _) => RecoverRequested?.Invoke();
        menu.Items.Add(recover);
        var view = new MenuItem { Header = "视角" };
        Add(view, "标准", () => Scene.SelectView(RoomViewPreset.Standard));
        Add(view, "近景", () => Scene.SelectView(RoomViewPreset.Near));
        Add(view, "全景", () => Scene.SelectView(RoomViewPreset.Whole));
        menu.Items.Add(view);
        foreach (MenuItem item in menu.Items)
        {
            item.SubmenuOpened += (_, _) => { Scene.InputBlocked = true; driver.ClearInput(); };
            item.SubmenuClosed += (_, _) => { Scene.InputBlocked = false; Scene.ClearWheel(); Scene.Focus(); };
        }
        toolbar.Children.Add(menu);
        toolbar.Children.Add(viewLabel);
        root.Children.Add(toolbar);
        root.Children.Add(Scene);
        Content = root;
        Scene.ViewChanged += RefreshLabel;
        Scene.SizeChanged += (_, _) => RefreshLabel();
        Deactivated += (_, _) => driver.ClearInput();
        LostKeyboardFocus += (_, _) => { if (!Scene.IsKeyboardFocusWithin) driver.ClearInput(); };
        Scene.MouseWheel += OnSceneWheel;
        Scene.KeyDown += OnSceneKeyDown;
        Scene.KeyUp += (_, e) => { if (IsActive && Scene.IsKeyboardFocusWithin) e.Handled = driver.Key(e.Key, false, false); };
        Closing += OnClosing;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideRequested?.Invoke(); };
        RefreshLabel();
    }

    internal void PrepareForExit() => allowClose = true;
    private nint WindowMessage(nint hwnd, int message, nint w, nint l, ref bool handled)
    { if (message == 0x02E0) driver.ClearInput(); return 0; }
    internal void FocusScene() { Activate(); Scene.Focus(); }
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
        if (e.Key == Key.Escape)
        { if (!e.IsRepeat) ReturnRequested?.Invoke(); e.Handled = true; return; }
        e.Handled = driver.Key(e.Key, true, e.IsRepeat);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    { if (!allowClose) { e.Cancel = true; HideRequested?.Invoke(); } }
    private void RefreshLabel()
    {
        viewLabel.Text = $"普通房 · 本地单人   视距 {Scene.Camera.ZoomPercent:0}%" + (Scene.IsHeightLimited ? " · 受窗口高度限制" : "");
        Scene.ToolTip = "A / D 移动 · S 轻压 · 空格跳跃 · 滚轮调视距 · Esc 房门";
    }
    private static void Add(MenuItem parent, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        parent.Items.Add(item);
    }
}
