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

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomWallpaperHost : IDisposable
{
    private WorkerWNativeHost? nativeHost;
    private HwndSource? source;
    private WorkerWDesktopTarget? target;
    private WallpaperWheelInputBridge? wheelBridge;
    private readonly DispatcherTimer shellCheck = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
    private LogicalRect screenBounds;
    private System.Drawing.Rectangle workArea;
    private long generation;
    private bool ready;
    private long lastMenuBoundsUpdate;
    private WorkerWInputProxy? petMenuProxy;
    private readonly Forms.ContextMenuStrip petMenu = new();
    internal LocalRoomSceneView Scene { get; } = new();
    internal bool IsVisible => source is not null;
    internal event Action? LostDesktop;
    internal event Action? ReturnRequested;
    internal event Action? WindowRequested;

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
        petMenu.Opening += (_, _) => { Scene.InputBlocked = true; Scene.ClearWheel(); };
        petMenu.Closed += (_, _) => { Scene.InputBlocked = false; Scene.ClearWheel(); };
        Scene.FrameApplied += UpdatePetMenuBounds;
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
            petMenuProxy = new(target, (_, _) => false, (_, _) => { }, () => { }, Scene.ClearWheel,
                contextRequested: () => { Scene.ClearWheel(); petMenu.Show(Forms.Cursor.Position); });
            shellCheck.Start();
        }
        catch { Hide(); throw; }
    }

    internal void ConfirmReady(long attempt) { if (generation == attempt) { ready = true; lastMenuBoundsUpdate = 0; UpdatePetMenuBounds(); petMenuProxy?.SetEnabled(true); } }
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
        { Scene.ClearWheel(); Scene.Dispatcher.BeginInvoke(LayoutScene); }
        return 0;
    }
    internal void StopInput()
    { ready = false; wheelBridge?.Stop(); petMenuProxy?.SetEnabled(false); petMenu.Close(); Scene.ClearWheel(); shellCheck.Stop(); }
    internal void Hide()
    {
        StopInput();
        ++generation;
        wheelBridge?.Dispose(); wheelBridge = null;
        petMenuProxy?.Dispose(); petMenuProxy = null;
        if (source is not null) { source.RootVisual = null; source.Dispose(); source = null; }
        nativeHost?.Dispose(); nativeHost = null;
        target = null;
    }
    public void Dispose() { Hide(); petMenu.Dispose(); }
}
