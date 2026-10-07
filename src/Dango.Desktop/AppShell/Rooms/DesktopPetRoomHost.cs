using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace Dango.Desktop.AppShell.Rooms;

// A single bounded failure snapshot. No pointer coordinates, input history, window
// titles, exception messages or external desktop content are retained here.
internal sealed record DesktopPetRoomSurfaceFailure(
    DateTimeOffset OccurredAtUtc,
    string Reason,
    int? NativeErrorCode,
    int NativeMessage,
    string PetFrameRejection,
    bool WallpaperDisposed,
    bool WallpaperRootMatches,
    bool SceneVisible,
    bool SceneHealthy,
    bool RuntimeFrameCurrent,
    bool PetVisible,
    bool PetHealthy,
    bool PetFrameMatches,
    bool RuntimeOwnerPresent,
    bool RuntimeRunning,
    bool RuntimeHealthy,
    bool PointerCaptured,
    bool PointerActive,
    bool PhysicalDragActive,
    HostFrameDiagnostics? Scheduler);

internal sealed class DesktopPetRoomHost : IDisposable
{
    private WorkerWNativeHost? nativeHost;
    private WorkerWDesktopTarget? target;
    private LocalRoomSceneDriver? driver;
    private System.Drawing.Rectangle screenBounds;
    private System.Drawing.Rectangle workArea;
    private bool driverReady;
    private bool renderedTogether;
    private bool readyReported;
    private bool faultReported;
    private EventHandler? confirmation;
    private StackedPhysicsPresentationFrame? awaitingRuntimeFrame;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly IDesktopPetPointerGesturePlatform? gesturePlatform;
    private readonly DispatcherTimer shellCheck = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
    internal LocalRoomSceneView Scene { get; } = new();
    internal DesktopPetDangoWindow? PetWindow { get; private set; }
    internal HwndSource? WallpaperSource { get; private set; }
    internal long Generation { get; private set; }
    internal bool IsVisible => WallpaperSource is not null && PetWindow?.IsVisible == true;
    internal bool IsReady { get; private set; }
    internal string LastFailureReason { get; private set; } = string.Empty;
    internal string LastFailureDetail { get; private set; } = string.Empty;
    internal DesktopPetRoomSurfaceFailure? LastFailureSnapshot { get; private set; }
    internal string LastSurfaceFailure => $"Reason={LastFailureReason}; {LastFailureDetail}";
    internal bool IsMenuOpen => menu.Visible;
    internal event Action<long>? HealthyFrameReady;
    internal event Action? LostDesktop;
    internal event Action? ReturnRequested;
    internal event Action? HideRequested;
    internal event Action? ControlCenterRequested;
    internal event Action? RecoverRequested;

    internal DesktopPetRoomHost(IDesktopPetPointerGesturePlatform? gesturePlatform = null)
    {
        this.gesturePlatform = gesturePlatform;
        Scene.EnableDesktopPetPresentation();
        Scene.FrameApplied += ApplyPetFrame;
        Scene.InputInvalidated += ReleasePointer;
        void Add(string title, Action action)
        {
            var item = new Forms.ToolStripMenuItem(title);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("房门 · 返回门廊", () => ReturnRequested?.Invoke());
        Add("暂时隐藏", () => HideRequested?.Invoke());
        Add("重试团子", () => RecoverRequested?.Invoke());
        Add("控制中心", () => ControlCenterRequested?.Invoke());
        menu.Opening += (_, _) => { Scene.InputBlocked = true; driver?.ClearInput(); PetWindow?.SetInteractionEnabled(false); };
        menu.Closed += (_, _) =>
        {
            Scene.InputBlocked = false;
            if (IsReady) PetWindow?.SetInteractionEnabled(true);
        };
        shellCheck.Tick += (_, _) => CheckSurfaceHealth();
    }

    internal void CheckSurfaceHealth()
    {
        var primary = Forms.Screen.PrimaryScreen;
        if (target is null || !NativeMethods.IsValidDesktopTarget(target)) FailSurface("shell target invalid");
        else if (primary is null || primary.Bounds != screenBounds || primary.WorkingArea != workArea) FailSurface("primary display configuration changed");
        else if (PetWindow?.IsVisible != true) FailSurface("pet window hidden");
        else if (readyReported)
        {
            if (!AppliedSurfacesHealthy() || driver?.OwnsRuntime != true ||
                Scene.LastAppliedRuntime is not { IsRunning: true, LastError: null, LastFrame: not null })
                FailSurface("joint surface health lost");
            else if (!ReferenceEquals(Scene.LastAppliedFrame, Scene.LastAppliedRuntime.LastFrame))
            {
                // Wake can replace only the runtime record before the next real apply.
                // The two displayed surfaces are still consistent. Permit one timer
                // interval, then reject a record that never reached either surface.
                if (ReferenceEquals(awaitingRuntimeFrame, Scene.LastAppliedFrame))
                    FailSurface("runtime frame not reconciled after health interval");
                else awaitingRuntimeFrame = Scene.LastAppliedFrame;
            }
            else awaitingRuntimeFrame = null;
        }
    }

    internal void Bind(LocalRoomSceneDriver value)
    {
        if (ReferenceEquals(value, driver)) return;
        if (driver is not null)
        {
            driver.HealthyFrameReady -= OnDriverReady;
            driver.PointerCancelled -= ReleasePointer;
        }
        driver = value;
        driver.HealthyFrameReady += OnDriverReady;
        driver.PointerCancelled += ReleasePointer;
        PetWindow?.Bind(value);
    }

    internal void Show(long attempt, Forms.Screen screen)
    {
        Hide();
        Generation = attempt;
        driverReady = renderedTogether = readyReported = faultReported = false;
        awaitingRuntimeFrame = null;
        LastFailureReason = LastFailureDetail = string.Empty;
        LastFailureSnapshot = null;
        Scene.InputBlocked = false;
        target = NativeMethods.FindWorkerW() ?? throw new InvalidOperationException("桌宠房间壁纸挂接不可用，请返回门廊重试。");
        try
        {
            screenBounds = screen.Bounds;
            workArea = screen.WorkingArea;
            nativeHost = new(target);
            NativeMethods.PositionLocalRoomHost(nativeHost.Handle, target, screenBounds.X, screenBounds.Y, screenBounds.Width, screenBounds.Height);
            WallpaperSource = new(new HwndSourceParameters("Dango Desktop Pet Room Wallpaper", screenBounds.Width, screenBounds.Height)
            {
                ParentWindow = nativeHost.Handle,
                WindowStyle = 0x40000000 | 0x10000000,
                ExtendedWindowStyle = 0x08000000 | 0x80,
                UsesPerPixelTransparency = false,
                TreatAsInputRoot = true,
                PositionX = 0,
                PositionY = 0,
            });
            WallpaperSource.CompositionTarget.BackgroundColor = Color.FromRgb(242, 235, 222);
            WallpaperSource.AddHook(BackgroundMessageHook);
            WallpaperSource.RootVisual = Scene;
            LayoutScene();
            PetWindow = new(Scene, ScreenPixelsToScene, SceneToScreenPixels, gesturePlatform,
                new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height));
            if (driver is not null) PetWindow.Bind(driver);
            PetWindow.ContextRequested += OpenMenu;
            PetWindow.SurfaceInvalidated += FailSurface;
            PetWindow.Show();
            PetWindow.PositionStableViewport();
            shellCheck.Start();
        }
        catch { Hide(); throw; }
    }

    private void LayoutScene()
    {
        if (WallpaperSource is null) return;
        var fromPixels = WallpaperSource.CompositionTarget.TransformFromDevice;
        Scene.Width = screenBounds.Width * fromPixels.M11;
        Scene.Height = screenBounds.Height * fromPixels.M22;
        Scene.SafeInsets = new(
            Math.Max(0, workArea.Left - screenBounds.Left) * fromPixels.M11,
            Math.Max(0, workArea.Top - screenBounds.Top) * fromPixels.M22,
            Math.Max(0, screenBounds.Right - workArea.Right) * fromPixels.M11,
            Math.Max(0, screenBounds.Bottom - workArea.Bottom) * fromPixels.M22);
        Scene.Measure(new(Scene.Width, Scene.Height));
        Scene.Arrange(new(0, 0, Scene.Width, Scene.Height));
        Scene.UpdateLayout();
    }

    private Point ScreenPixelsToScene(Point point) => WallpaperSource is { IsDisposed: false } source
        ? source.CompositionTarget.TransformFromDevice.Transform(new Point(point.X - screenBounds.X, point.Y - screenBounds.Y))
        : new(double.NaN, double.NaN);

    private Point SceneToScreenPixels(Point point)
    {
        if (WallpaperSource is not { IsDisposed: false } source) return new(double.NaN, double.NaN);
        var pixels = source.CompositionTarget.TransformToDevice.Transform(point);
        return new(pixels.X + screenBounds.X, pixels.Y + screenBounds.Y);
    }

    private void ApplyPetFrame()
    {
        if (WallpaperSource?.IsDisposed == true) { FailSurface("wallpaper source disposed during frame"); return; }
        if (PetWindow is null || WallpaperSource is null || faultReported ||
            Scene.LastAppliedFrame is not { } frame || Scene.LastAppliedRuntime is not { } runtime) return;
        try
        {
            if (!PetWindow.ApplyFrame(frame, runtime)) { FailSurface($"pet frame rejected: {PetWindow.LastFrameRejection}"); return; }
            if (ReferenceEquals(frame, runtime.LastFrame)) awaitingRuntimeFrame = null;
            ScheduleJointConfirmation();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException)
        {
            FailSurface($"pet frame exception: {error.GetType().Name}" + (error is Win32Exception native ? $" ({native.NativeErrorCode})" : string.Empty),
            error is Win32Exception nativeError ? nativeError.NativeErrorCode : null);
        }
    }

    private void OnDriverReady(long attempt)
    {
        if (attempt != Generation || WallpaperSource is null || faultReported) return;
        driverReady = true;
        ScheduleJointConfirmation();
    }

    private bool AppliedSurfacesHealthy() => WallpaperSource is { IsDisposed: false, RootVisual: { } root } &&
        ReferenceEquals(root, Scene) && Scene.IsVisible && Scene.HasHealthyFrame && Scene.LastAppliedFrame is { } frame &&
        PresentationSource.FromVisual(Scene) is not null &&
        PetWindow is { IsVisible: true, HasHealthyFrame: true, Handle: not 0 } pet &&
        ReferenceEquals(frame, pet.LastAppliedFrame) && PresentationSource.FromVisual(pet) is not null;

    private bool BothSurfacesHealthy() => AppliedSurfacesHealthy() &&
        ReferenceEquals(Scene.LastAppliedFrame, Scene.LastAppliedRuntime?.LastFrame);

    private void ScheduleJointConfirmation()
    {
        if (!driverReady || readyReported || confirmation is not null || !BothSurfacesHealthy()) return;
        var attempt = Generation;
        confirmation = (_, _) =>
        {
            CancelConfirmation();
            Scene.Dispatcher.BeginInvoke(() =>
            {
                if (attempt != Generation || faultReported || readyReported || !driverReady || driver?.OwnsRuntime != true ||
                    !BothSurfacesHealthy()) return;
                renderedTogether = readyReported = true;
                HealthyFrameReady?.Invoke(attempt);
            }, DispatcherPriority.Render);
        };
        CompositionTarget.Rendering += confirmation;
    }

    internal void ConfirmReady(long attempt)
    {
        if (attempt != Generation || !renderedTogether || faultReported || !BothSurfacesHealthy()) return;
        IsReady = true;
        PetWindow!.SetInteractionEnabled(true);
    }

    private void CancelConfirmation()
    {
        if (confirmation is not null) CompositionTarget.Rendering -= confirmation;
        confirmation = null;
    }

    private void ReleasePointer() => PetWindow?.ReleasePointerCapture();
    private void OpenMenu(Point screenPoint)
    {
        if (!IsReady || menu.Visible) return;
        menu.Show(new System.Drawing.Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)));
    }

    private void FailSurface() => FailSurface($"pet native display message: 0x{PetWindow?.LastSurfaceInvalidationMessage:X4}",
        nativeDisplayMessage: PetWindow?.LastSurfaceInvalidationMessage ?? 0);

    private void FailSurface(string reason, int? nativeErrorCode = null, int nativeDisplayMessage = 0)
    {
        if (faultReported || WallpaperSource is null) return;
        faultReported = true;
        CaptureFailure(reason, nativeErrorCode, nativeDisplayMessage);
        var attempt = Generation;
        StopInput();
        Scene.Dispatcher.BeginInvoke(() => { if (attempt == Generation) LostDesktop?.Invoke(); });
    }

    // The application host retains responsibility for its existing driver-failure
    // shutdown. Capture only the same fixed diagnostics before that shutdown.
    internal void RecordDriverFailure() => CaptureFailure("room driver failed", null, 0);

    private void CaptureFailure(string reason, int? nativeErrorCode, int nativeDisplayMessage)
    {
        var source = WallpaperSource;
        LastFailureReason = reason;
        LastFailureDetail = $"WallpaperDisposed={source?.IsDisposed}; RootMatches={source is { IsDisposed: false } && ReferenceEquals(source.RootVisual, Scene)}; " +
            $"SceneVisible={Scene.IsVisible}; SceneHealthy={Scene.HasHealthyFrame}; FrameCurrent={ReferenceEquals(Scene.LastAppliedFrame, Scene.LastAppliedRuntime?.LastFrame)}; " +
            $"PetVisible={PetWindow?.IsVisible}; PetHealthy={PetWindow?.HasHealthyFrame}; PetFrameMatches={ReferenceEquals(Scene.LastAppliedFrame, PetWindow?.LastAppliedFrame)}; " +
            $"PetReject={PetWindow?.LastFrameRejection}; PetNativeMessage=0x{PetWindow?.LastSurfaceInvalidationMessage:X4}";
        var runtime = Scene.LastAppliedRuntime;
        LastFailureSnapshot = new(DateTimeOffset.UtcNow, reason, nativeErrorCode,
            nativeDisplayMessage, PetWindow?.LastFrameRejection ?? string.Empty,
            source?.IsDisposed != false, source is { IsDisposed: false } && ReferenceEquals(source.RootVisual, Scene),
            Scene.IsVisible, Scene.HasHealthyFrame, Scene.LastAppliedFrame is not null && ReferenceEquals(Scene.LastAppliedFrame, runtime?.LastFrame),
            PetWindow?.IsVisible == true, PetWindow?.HasHealthyFrame == true,
            Scene.LastAppliedFrame is not null && ReferenceEquals(Scene.LastAppliedFrame, PetWindow?.LastAppliedFrame), driver?.OwnsRuntime == true,
            driver?.RuntimeRunning ?? (runtime?.IsRunning == true),
            driver?.RuntimeHealthy ?? (runtime is { LastError: null, LastFrame: not null }),
            PetWindow?.IsPointerCaptured == true, driver?.IsPointerActive == true, runtime?.IsDragging == true,
            driver?.FrameDiagnostics);
    }

    private nint BackgroundMessageHook(nint window, int message, nint w, nint l, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return -1; }
        if (message == 0x0021) { handled = true; return 3; }
        if (message is 0x02E0 or 0x007E) FailSurface($"wallpaper native display message: 0x{message:X4}", nativeDisplayMessage: message);
        return 0;
    }

    internal void StopInput()
    {
        IsReady = false;
        awaitingRuntimeFrame = null;
        driverReady = renderedTogether = false;
        CancelConfirmation();
        shellCheck.Stop();
        PetWindow?.SetInteractionEnabled(false);
        driver?.ClearInput();
        menu.Close();
        Scene.InputBlocked = false;
    }

    internal void Hide()
    {
        StopInput();
        ++Generation;
        if (PetWindow is { } pet)
        {
            pet.ContextRequested -= OpenMenu;
            pet.SurfaceInvalidated -= FailSurface;
            pet.Close();
            PetWindow = null;
        }
        if (WallpaperSource is { } source)
        {
            source.RootVisual = null;
            source.Dispose();
            WallpaperSource = null;
        }
        nativeHost?.Dispose();
        nativeHost = null;
        target = null;
    }

    public void Dispose()
    {
        Hide();
        if (driver is not null)
        {
            driver.HealthyFrameReady -= OnDriverReady;
            driver.PointerCancelled -= ReleasePointer;
            driver = null;
        }
        Scene.FrameApplied -= ApplyPetFrame;
        Scene.InputInvalidated -= ReleasePointer;
        menu.Dispose();
    }
}
