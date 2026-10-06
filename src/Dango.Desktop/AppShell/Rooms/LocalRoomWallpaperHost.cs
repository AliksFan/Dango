using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Dango.Desktop.AppShell.Rooms;

internal enum RoomPointerStartResult { NotAccepted, CaptureRejected, Accepted }

internal sealed class LocalRoomWallpaperHost : IDisposable
{
    private WorkerWNativeHost? nativeHost;
    private HwndSource? source;
    private WorkerWDesktopTarget? target;
    private WallpaperWheelInputBridge? wheelBridge;
    private WallpaperGameplayInputBridge? gameplayBridge;
    private LocalRoomSceneDriver? driver;
    private readonly DispatcherTimer shellCheck = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
    private LogicalRect screenBounds;
    private System.Drawing.Rectangle workArea;
    private long generation;
    private bool ready;
    private long lastMenuBoundsUpdate;
    private WorkerWInputProxy? petMenuProxy;
    private LocalRoomWallpaperMenuWindow? menuWindow;
    private bool openingMenu;
    private readonly Forms.ContextMenuStrip petMenu = new();
    internal LocalRoomSceneView Scene { get; } = new();
    internal bool IsVisible => source is not null;
    internal bool IsMenuOpen => menuWindow is not null;
    internal LocalRoomWallpaperMenuWindow? MenuWindow => menuWindow;
    internal RoomPointerStartResult LastPointerStartResult { get; private set; }
    internal event Action? LostDesktop;
    internal event Action? ReturnRequested;
    internal event Action? WindowRequested;
    internal event Action? MenuRequested;
    internal event Action? ControlCenterRequested;
    internal event Action? RecoverRequested;

    internal void Bind(LocalRoomSceneDriver value)
    {
        if (ReferenceEquals(driver, value)) return;
        if (driver is not null) driver.PointerCancelled -= OnPointerCancelled;
        driver = value;
        driver.PointerCancelled += OnPointerCancelled;
    }
    private void RequestRoomMenu() => MenuRequested?.Invoke();
    private void OnPointerCancelled() { petMenuProxy?.ReleasePointerCapture(); gameplayBridge?.CancelPointer(); }
    private void ClearGameplayInput()
    {
        if (ready && source is not null) driver?.ClearInput();
        petMenuProxy?.ReleasePointerCapture(); wheelBridge?.ResetInput(); Scene.ClearWheel();
    }
    private void OnSceneInputInvalidated()
    {
        gameplayBridge?.CancelPointer(); petMenuProxy?.ReleasePointerCapture();
        if (ready && source is not null && gameplayBridge?.IsListening == true) driver?.CancelPointer();
    }

    internal LocalRoomWallpaperHost()
    {
        void Add(Forms.ToolStripMenuItem parent, string title, Action action)
        { var item = new Forms.ToolStripMenuItem(title); item.Click += (_, _) => action(); parent.DropDownItems.Add(item); }
        var view = new Forms.ToolStripMenuItem("视角");
        Add(view, "标准", () => Scene.SelectView(RoomViewPreset.Standard));
        Add(view, "近景", () => Scene.SelectView(RoomViewPreset.Near));
        Add(view, "全景", () => Scene.SelectView(RoomViewPreset.Whole));
        petMenu.Items.Add(view);
        var window = new Forms.ToolStripMenuItem("用窗口打开"); window.Click += (_, _) => WindowRequested?.Invoke(); petMenu.Items.Add(window);
        var door = new Forms.ToolStripMenuItem("房门 · 返回门廊"); door.Click += (_, _) => ReturnRequested?.Invoke(); petMenu.Items.Add(door);
        petMenu.Opening += (_, _) => { Scene.InputBlocked = true; gameplayBridge?.ResetInput(); ClearGameplayInput(); };
        petMenu.Closed += (_, _) => { UpdateMenuBlocking(); Scene.ClearWheel(); };
        Scene.ViewChanged += () => menuWindow?.Menu.RefreshStatus(Scene);
        Scene.FrameApplied += UpdatePetMenuBounds;
        Scene.InputInvalidated += OnSceneInputInvalidated;
        shellCheck.Tick += (_, _) =>
        {
            if (target is null || !NativeMethods.IsValidDesktopTarget(target) ||
                !Forms.Screen.AllScreens.Any(s => s.Bounds.X == screenBounds.X && s.Bounds.Y == screenBounds.Y &&
                    s.Bounds.Width == screenBounds.Width && s.Bounds.Height == screenBounds.Height))
            { StopInput(); LostDesktop?.Invoke(); }
        };
    }

    internal void Show(long attempt, Forms.Screen screen)
    {
        Hide();
        if (gameplayBridge?.IsHookReleasePending == true || wheelBridge?.IsHookReleasePending == true)
            throw new InvalidOperationException("上一轮壁纸输入监听仍在释放，可重试或用窗口打开。");
        generation = attempt;
        target = NativeMethods.FindWorkerW() ?? throw new InvalidOperationException("壁纸挂接暂不可用，可重试或用窗口打开。");
        try
        {
            var b = screen.Bounds;
            screenBounds = new(b.X, b.Y, b.Width, b.Height);
            workArea = screen.WorkingArea;
            nativeHost = new(target);
            NativeMethods.PositionLocalRoomHost(nativeHost.Handle, target, b.X, b.Y, b.Width, b.Height);
            source = new HwndSource(new HwndSourceParameters("Dango Ordinary Room Wallpaper", b.Width, b.Height)
            {
                ParentWindow = nativeHost.Handle,
                WindowStyle = 0x40000000 | 0x10000000,
                ExtendedWindowStyle = 0x80,
                UsesPerPixelTransparency = false,
                TreatAsInputRoot = true,
                PositionX = 0, PositionY = 0,
            });
            source.CompositionTarget.BackgroundColor = Color.FromRgb(242, 235, 222);
            source.AddHook(MessageHook);
            source.RootVisual = Scene;
            LayoutScene();
            wheelBridge = new(Scene.Dispatcher, CreateInputContext, Scene.ApplyWheel, Scene.ClearWheel);
            wheelBridge.Start();
            if (!wheelBridge.IsListening) throw new InvalidOperationException("壁纸滚轮监听未能启动，可重试或用窗口打开。");
            petMenuProxy = new(target, (_, _) => false, (_, _) => { }, () => { }, ClearGameplayInput,
                contextRequested: OpenPetMenu, inputEligibility: IsGameplayEnvironmentEligible);
            gameplayBridge = new(Scene.Dispatcher, CreateInputContext, new(
                (key, down, repeat) => driver?.Key(key, down, repeat) == true,
                BeginPointer, (point, shift) => driver?.PointerMove(ToScenePoint(point), shift) == true,
                CompletePointer, point => Scene.IsPetHit(ToScenePoint(point)), RequestRoomMenu, ClearGameplayInput));
            gameplayBridge.Start();
            if (!gameplayBridge.IsListening) throw new InvalidOperationException("壁纸互动监听未能启动，可重试或用窗口打开。");
            shellCheck.Start();
        }
        catch { Hide(); throw; }
    }

    internal void ConfirmReady(long attempt) { if (generation == attempt) { ready = true; lastMenuBoundsUpdate = 0; UpdatePetMenuBounds(); petMenuProxy?.SetEnabled(true); } }
    private void UpdateMenuBlocking() => Scene.InputBlocked = openingMenu || menuWindow is not null || petMenu.Visible;

    internal void OpenMenu()
    {
        if (menuWindow is { } current) { current.Activate(); current.Menu.Focus(); return; }
        if (openingMenu || !ready || source is null || target is null || !Scene.HasHealthyFrame ||
            !NativeMethods.IsValidDesktopTarget(target)) return;
        openingMenu = true;
        UpdateMenuBlocking();
        gameplayBridge?.ResetInput(); ClearGameplayInput();
        try
        {
            petMenu.Close();
            var menu = new LocalRoomWallpaperMenuWindow(screenBounds);
            menuWindow = menu;
            openingMenu = false;
            menu.Menu.RefreshStatus(Scene);
            void Act(Action action)
            {
                if (!ReferenceEquals(menuWindow, menu)) return;
                CloseMenu(restoreDesktop: false);
                action();
            }
            menu.DismissRequested += restore => { if (ReferenceEquals(menuWindow, menu)) CloseMenu(restore); };
            menu.Menu.ReturnRequested += () => Act(() => ReturnRequested?.Invoke());
            menu.Menu.ControlCenterRequested += () => Act(() => ControlCenterRequested?.Invoke());
            menu.Menu.RecoverRequested += () => Act(() => RecoverRequested?.Invoke());
            menu.Menu.PresentationRequested += presentation => Act(() =>
            {
                if (presentation == LocalRoomPresentation.Window) WindowRequested?.Invoke();
            });
            menu.Show();
            if (ReferenceEquals(menuWindow, menu)) { menu.Activate(); menu.Menu.Focus(); }
        }
        catch { openingMenu = false; CloseMenu(restoreDesktop: false); UpdateMenuBlocking(); throw; }
    }

    internal void CloseMenu(bool restoreDesktop = false)
    {
        var menu = menuWindow;
        if (menu is null) return;
        menuWindow = null;
        openingMenu = false;
        try
        {
            menu.BeginClosing();
            if (restoreDesktop && target is not null)
                _ = NativeMethods.TryRestoreLocalRoomDesktopFocus(target, new WindowInteropHelper(menu).Handle);
            menu.Close();
        }
        finally
        {
            UpdateMenuBlocking();
            gameplayBridge?.ResetInput(); ClearGameplayInput();
        }
    }
    private void UpdatePetMenuBounds()
    {
        if (source is null || petMenuProxy is null || !ready || Scene.PetScreenBounds is not { } bounds) return;
        var now = Environment.TickCount64;
        if (now - lastMenuBoundsUpdate < 100) return;
        lastMenuBoundsUpdate = now;
        var toPixels = source.CompositionTarget.TransformToDevice;
        // Proxy placement is relative to WorkerW parent, not DIP or virtual-screen coordinates.
        try
        {
            var origin = NativeMethods.LocalRoomParentScreenOrigin(target!);
            petMenuProxy.SetBounds((int)(screenBounds.X - origin.X + bounds.X * toPixels.M11),
                (int)(screenBounds.Y - origin.Y + bounds.Y * toPixels.M22),
                (int)Math.Ceiling(bounds.Width * toPixels.M11), (int)Math.Ceiling(bounds.Height * toPixels.M22));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { StopInput(); Scene.Dispatcher.BeginInvoke(() => LostDesktop?.Invoke()); }
    }
    private WallpaperWheelInputContext? CreateInputContext() => source is not null && target is not null
        ? new(target, screenBounds, generation, ready && Scene.HasHealthyFrame, Scene.InputBlocked) : null;
    private Point ToScenePoint(WallpaperWheelPoint point)
    {
        if (source is null) return new(double.NaN, double.NaN);
        return source.CompositionTarget.TransformFromDevice.Transform(new Point(point.X - screenBounds.X, point.Y - screenBounds.Y));
    }
    private bool BeginPointer(WallpaperWheelPoint point, bool shift)
    {
        LastPointerStartResult = RoomPointerStartResult.NotAccepted;
        if (driver?.PointerDown(ToScenePoint(point), shift) != true) return false;
        if (petMenuProxy?.TryCapturePointer() == true) { LastPointerStartResult = RoomPointerStartResult.Accepted; return true; }
        LastPointerStartResult = RoomPointerStartResult.CaptureRejected;
        driver.CancelPointer();
        return false;
    }
    private bool CompletePointer(WallpaperWheelPoint point, bool shift)
    {
        try { return driver?.PointerUp(ToScenePoint(point), shift) == true; }
        finally { petMenuProxy?.ReleasePointerCapture(); }
    }
    private bool IsGameplayEnvironmentEligible()
    {
        if (CreateInputContext() is not { } captured) return false;
        var environment = NativeMethods.InspectRoomGameplayInput(captured, null, false, allowShift: true);
        return WallpaperWheelInputPolicy.IsSamePresentation(captured, CreateInputContext()) && environment.IsKnown &&
            environment.IsDesktopForeground && !environment.HasFullscreenApplication && !environment.HasModifier;
    }
    private void OpenPetMenu()
    {
        if (IsMenuOpen || openingMenu) return;
        if (CreateInputContext() is not { } captured || !NativeMethods.GetCursorPos(out var point)) return;
        var p = new WallpaperWheelPoint(point.X, point.Y);
        var environment = NativeMethods.InspectRoomGameplayInput(captured, p, false, allowShift: true);
        if (!WallpaperWheelInputPolicy.ShouldApply(captured, CreateInputContext(), p, environment) || !Scene.IsPetHit(ToScenePoint(p))) return;
        gameplayBridge?.ResetInput(); ClearGameplayInput();
        petMenu.Show(Forms.Cursor.Position);
    }
    private void LayoutScene()
    {
        if (source is null) return;
        var fromPixels = source.CompositionTarget.TransformFromDevice;
        Scene.Width = screenBounds.Width * fromPixels.M11;
        Scene.Height = screenBounds.Height * fromPixels.M22;
        Scene.SafeInsets = new Thickness(
            Math.Max(0, workArea.Left - screenBounds.X) * fromPixels.M11,
            Math.Max(0, workArea.Top - screenBounds.Y) * fromPixels.M22,
            Math.Max(0, screenBounds.Right - workArea.Right) * fromPixels.M11,
            Math.Max(0, screenBounds.Bottom - workArea.Bottom) * fromPixels.M22);
        Scene.Measure(new(Scene.Width, Scene.Height));
        Scene.Arrange(new(0, 0, Scene.Width, Scene.Height));
        Scene.UpdateLayout();
    }
    private nint MessageHook(nint hwnd, int message, nint w, nint l, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return -1; } // background never consumes Shell mouse input
        if (message == 0x0021) { handled = true; return 3; }
        if (message == 0x02E0 || message == 0x007E)
        {
            CloseMenu(restoreDesktop: false);
            if (ready && source is not null)
            { gameplayBridge?.ResetInput(); ClearGameplayInput(); }
            else { gameplayBridge?.CancelPointer(); petMenuProxy?.ReleasePointerCapture(); Scene.ClearWheel(); }
            Scene.Dispatcher.BeginInvoke(LayoutScene);
        }
        return 0;
    }
    internal void StopInput()
    {
        CloseMenu(restoreDesktop: false);
        var wasActive = ready && source is not null;
        ready = false; gameplayBridge?.Stop(); wheelBridge?.Stop();
        if (wasActive) driver?.ClearInput();
        petMenuProxy?.ReleasePointerCapture(); petMenuProxy?.SetEnabled(false); petMenu.Close(); Scene.ClearWheel(); shellCheck.Stop();
    }
    internal void Hide()
    {
        StopInput();
        ++generation;
        gameplayBridge?.Dispose();
        if (gameplayBridge?.IsHookReleasePending != true) gameplayBridge = null;
        wheelBridge?.Dispose();
        if (wheelBridge?.IsHookReleasePending != true) wheelBridge = null;
        petMenuProxy?.Dispose(); petMenuProxy = null;
        if (source is not null) { source.RootVisual = null; source.Dispose(); source = null; }
        nativeHost?.Dispose(); nativeHost = null;
        target = null;
    }
    public void Dispose()
    {
        Hide();
        if (driver is not null) driver.PointerCancelled -= OnPointerCancelled;
        driver = null;
        petMenu.Dispose();
    }
}
