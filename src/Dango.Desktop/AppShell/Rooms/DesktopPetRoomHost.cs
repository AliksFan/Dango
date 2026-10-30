using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Physics.Presentation;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Dango.Desktop.AppShell.Rooms;

internal readonly record struct DesktopPetDisplayGeometry(System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkingArea);

// Bounded diagnostics only: no desktop identifiers, external content or input history.
internal sealed record DesktopPetRoomSurfaceFailure(
    DateTimeOffset OccurredAtUtc, string Reason, int? NativeErrorCode, int NativeMessage,
    string PetFrameRejection, bool SurfaceAttached, bool DesktopCurrent, bool DesktopSuspended,
    bool SceneVisible, bool SceneHealthy, bool RuntimeFrameCurrent, bool PetVisible,
    bool PetHealthy, bool PetFrameMatches, bool RuntimeOwnerPresent, bool RuntimeRunning,
    bool RuntimeHealthy, bool PointerCaptured, bool PointerActive, bool PhysicalDragActive,
    HostFrameDiagnostics? Scheduler);

internal sealed class DesktopPetRoomHost : IDisposable
{
    private LocalRoomSceneDriver? driver;
    private readonly Func<IDesktopPetVirtualDesktop> createVirtualDesktop;
    private readonly Func<DesktopPetDisplayGeometry?>? readDisplayGeometry;
    private IDesktopPetVirtualDesktop? virtualDesktop;
    private System.Drawing.Rectangle screenBounds;
    private System.Drawing.Rectangle workArea;
    private bool driverReady, framePresented, readyReported, faultReported, entryCommitted;
    private bool desktopCurrent, manuallyHidden, restoreWhenCurrent;
    private EventHandler? confirmation;
    private long confirmationRevision;
    private StackedPhysicsPresentationFrame? awaitingRuntimeFrame;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly IDesktopPetPointerGesturePlatform? gesturePlatform;
    private readonly DispatcherTimer desktopCheck = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    internal LocalRoomSceneView Scene { get; } = new();
    internal DesktopPetDangoWindow? PetWindow { get; private set; }
    internal Point SceneOriginPixels => new(screenBounds.Left, screenBounds.Top);
    internal long Generation { get; private set; }
    internal bool IsVisible => !manuallyHidden && !IsDesktopSuspended && PetWindow?.IsVisible == true;
    internal bool IsDesktopSuspended { get; private set; }
    internal bool HasRetainedPresentation => PetWindow is not null && virtualDesktop is not null;
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
    internal event Action? RestoreOnCurrentDesktopRequested;
    internal event Action<bool>? DesktopAvailabilityChanged;

    internal DesktopPetRoomHost(IDesktopPetPointerGesturePlatform? gesturePlatform = null,
        Func<IDesktopPetVirtualDesktop>? createVirtualDesktop = null,
        Func<DesktopPetDisplayGeometry?>? readDisplayGeometry = null)
    {
        this.gesturePlatform = gesturePlatform;
        this.createVirtualDesktop = createVirtualDesktop ?? (() => new DesktopPetVirtualDesktop());
        this.readDisplayGeometry = readDisplayGeometry;
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
        desktopCheck.Tick += (_, _) => CheckSurfaceHealth();
    }

    internal void CheckSurfaceHealth()
    {
        if (PetWindow is null || faultReported) return;
        var primary = Forms.Screen.PrimaryScreen;
        var display = readDisplayGeometry is not null ? readDisplayGeometry() : primary is null ? (DesktopPetDisplayGeometry?)null : new(primary.Bounds, primary.WorkingArea);
        if (display is null || display.Value.Bounds != screenBounds || display.Value.WorkingArea != workArea)
        { FailSurface("primary display configuration changed"); return; }
        if (!TryReadDesktop(out var current)) return;
        desktopCurrent = current;
        if (manuallyHidden)
        {
            if (restoreWhenCurrent && current)
            {
                restoreWhenCurrent = false;
                var attempt = Generation;
                Scene.Dispatcher.BeginInvoke(() =>
                {
                    if (attempt == Generation && manuallyHidden && !faultReported)
                        RestoreOnCurrentDesktopRequested?.Invoke();
                });
            }
            return;
        }
        if (!current) { SuspendForDesktop(); return; }
        if (IsDesktopSuspended)
        {
            try
            {
                IsDesktopSuspended = false;
                PetWindow.InvalidateDisplayedFrame();
                PetWindow.Show();
                driver?.ResumeDesktopPresentation();
                DesktopAvailabilityChanged?.Invoke(true);
            }
            catch (Exception error) when (IsHostError(error)) { FailSurface("desktop presentation resume failed"); }
            return;
        }
        if (!PetWindow.IsVisible) { FailSurface("pet window hidden"); return; }
        if (!readyReported) return;
        if (!AppliedSurfaceHealthy() || driver?.OwnsRuntime != true ||
            Scene.LastAppliedRuntime is not { IsRunning: true, LastError: null, LastFrame: not null })
            FailSurface("pet surface health lost");
        else if (!ReferenceEquals(Scene.LastAppliedFrame, Scene.LastAppliedRuntime.LastFrame))
        {
            // Wake may update the runtime record before the following composition.
            if (ReferenceEquals(awaitingRuntimeFrame, Scene.LastAppliedFrame))
                FailSurface("runtime frame not reconciled after health interval");
            else awaitingRuntimeFrame = Scene.LastAppliedFrame;
        }
        else awaitingRuntimeFrame = null;
    }

    private static bool IsHostError(Exception error) => error is Win32Exception or InvalidOperationException or ArgumentException or COMException;

    private bool TryReadDesktop(out bool current)
    {
        current = false;
        try
        {
            if (virtualDesktop is null) throw new InvalidOperationException("桌宠所属桌面不可用。");
            current = virtualDesktop.IsCurrent;
            return true;
        }
        catch (Exception error) when (IsHostError(error))
        {
            FailSurface("virtual desktop affiliation unavailable", error is Win32Exception native ? native.NativeErrorCode :
                error is COMException com ? com.ErrorCode : null);
            return false;
        }
    }

    // Called from synchronous native mouse/window messages and gesture hooks.
    // Outbound Shell COM calls are illegal in an input-synchronous SendMessage
    // (RPC_E_CANTCALLOUT_ININPUTSYNCCALL). Preparation, explicit restoration
    // and desktopCheck refresh affiliation; mouse qualification reads the cache.
    // Foreground/Explorer focus is deliberately not part of mouse eligibility.
    private bool AllowsInteraction() => desktopCurrent && !manuallyHidden && !IsDesktopSuspended && !faultReported;

    private void SuspendForDesktop()
    {
        if (IsDesktopSuspended) return;
        IsDesktopSuspended = true;
        IsReady = false;
        driverReady = framePresented = readyReported = false;
        awaitingRuntimeFrame = null;
        CancelConfirmation();
        PetWindow?.SetInteractionEnabled(false);
        menu.Close();
        driver?.PauseDesktopPresentation();
        PetWindow?.InvalidateDisplayedFrame();
        PetWindow?.Hide();
        DesktopAvailabilityChanged?.Invoke(false);
    }

    internal bool RequestRestoreOnCurrentDesktop()
    {
        if (!HasRetainedPresentation) return true;
        if (faultReported) return false;
        if (!TryReadDesktop(out var current)) return false;
        if (current) { restoreWhenCurrent = false; return true; }
        restoreWhenCurrent = true;
        desktopCheck.Start();
        return false;
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

    internal void SetDisplayPercent(int percent)
    {
        if (Scene.DesktopPetDisplayPercent == percent) return;
        Scene.DesktopPetDisplayPercent = percent;
        driver?.RefreshDesktopProjection();
        if (PetWindow is null || manuallyHidden || IsDesktopSuspended || faultReported) return;
        IsReady = framePresented = readyReported = false;
        driverReady = driver?.IsReady == true;
        CancelConfirmation();
        PetWindow.InvalidateDisplayedFrame();
    }

    internal void Show(long attempt, Forms.Screen screen)
    {
        var retained = manuallyHidden && HasRetainedPresentation;
        if (retained && faultReported) throw new InvalidOperationException("所属桌面无法确认，请返回门廊重新进入。");
        if (!retained) Hide();
        Generation = attempt;
        driverReady = framePresented = readyReported = faultReported = entryCommitted = false;
        manuallyHidden = restoreWhenCurrent = IsDesktopSuspended = false;
        awaitingRuntimeFrame = null;
        LastFailureReason = LastFailureDetail = string.Empty;
        LastFailureSnapshot = null;
        Scene.InputBlocked = false;
        var geometry = readDisplayGeometry is not null ? readDisplayGeometry() ?? throw new InvalidOperationException("没有可用主显示器。") : new(screen.Bounds, screen.WorkingArea);
        screenBounds = geometry.Bounds;
        workArea = geometry.WorkingArea;
        var preparationStage = "create pet window";
        try
        {
            if (!retained)
            {
                PetWindow = new(Scene, ScreenPixelsToScene, SceneToScreenPixels, gesturePlatform,
                    new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height));
                if (driver is not null) PetWindow.Bind(driver);
                PetWindow.ContextRequested += OpenMenu;
                PetWindow.SurfaceInvalidated += FailSurface;
                // Establish affinity while the actual pet pixels and input are still disabled.
                var handle = new WindowInteropHelper(PetWindow).EnsureHandle();
                preparationStage = "attach virtual desktop";
                virtualDesktop = createVirtualDesktop();
                virtualDesktop.Attach(handle);
                PetWindow.DesktopAllowsInteraction = AllowsInteraction;
            }
            preparationStage = "verify virtual desktop";
            desktopCurrent = virtualDesktop!.IsCurrent;
            if (!desktopCurrent) throw new InvalidOperationException("请切回团子所属桌面后恢复。");
            PetWindow!.InvalidateDisplayedFrame();
            preparationStage = "show pet window";
            PetWindow.Show();
            preparationStage = "layout pet surface";
            PetWindow.PositionStableViewport();
            LayoutScene();
            desktopCheck.Start();
        }
        catch (Exception error)
        {
            RecordPreparationFailure(error, preparationStage);
            Hide();
            throw;
        }
    }

    private HwndSource? Surface => PetWindow is null ? null : PresentationSource.FromVisual(PetWindow) as HwndSource;

    private void LayoutScene()
    {
        if (Surface is not { IsDisposed: false } source) return;
        var fromPixels = source.CompositionTarget.TransformFromDevice;
        Scene.Width = screenBounds.Width * fromPixels.M11;
        Scene.Height = screenBounds.Height * fromPixels.M22;
        Scene.RenderTransform = new TranslateTransform((screenBounds.Left - workArea.Left) * fromPixels.M11,
            (screenBounds.Top - workArea.Top) * fromPixels.M22);
        Scene.SafeInsets = new(
            Math.Max(0, workArea.Left - screenBounds.Left) * fromPixels.M11,
            Math.Max(0, workArea.Top - screenBounds.Top) * fromPixels.M22,
            Math.Max(0, screenBounds.Right - workArea.Right) * fromPixels.M11,
            Math.Max(0, screenBounds.Bottom - workArea.Bottom) * fromPixels.M22);
        Scene.Measure(new(Scene.Width, Scene.Height));
        Scene.Arrange(new(0, 0, Scene.Width, Scene.Height));
        Scene.UpdateLayout();
    }

    private Point ScreenPixelsToScene(Point point) => Surface is { IsDisposed: false } source
        ? source.CompositionTarget.TransformFromDevice.Transform(new Point(point.X - screenBounds.X, point.Y - screenBounds.Y))
        : new(double.NaN, double.NaN);

    private Point SceneToScreenPixels(Point point)
    {
        if (Surface is not { IsDisposed: false } source) return new(double.NaN, double.NaN);
        var pixels = source.CompositionTarget.TransformToDevice.Transform(point);
        return new(pixels.X + screenBounds.X, pixels.Y + screenBounds.Y);
    }

    private void ApplyPetFrame()
    {
        if (PetWindow is null || manuallyHidden || IsDesktopSuspended || faultReported ||
            Scene.LastAppliedFrame is not { } frame || Scene.LastAppliedRuntime is not { } runtime) return;
        try
        {
            if (!AllowsInteraction()) { if (!faultReported) SuspendForDesktop(); return; }
            if (!PetWindow.ApplyFrame(frame, runtime)) { FailSurface($"pet frame rejected: {PetWindow.LastFrameRejection}"); return; }
            if (ReferenceEquals(frame, runtime.LastFrame)) awaitingRuntimeFrame = null;
            ScheduleFrameConfirmation();
        }
        catch (Exception error) when (IsHostError(error))
        {
            FailSurface($"pet frame exception: {error.GetType().Name}", error is Win32Exception native ? native.NativeErrorCode : null);
        }
    }

    private void OnDriverReady(long attempt)
    {
        if (attempt != Generation || PetWindow is null || faultReported || manuallyHidden || IsDesktopSuspended) return;
        driverReady = true;
        ScheduleFrameConfirmation();
    }

    private bool SurfaceAttached => Surface is { IsDisposed: false } source && ReferenceEquals(PresentationSource.FromVisual(Scene), source);
    private bool AppliedSurfaceHealthy() => SurfaceAttached && Scene.IsVisible && Scene.HasHealthyFrame &&
        Scene.LastAppliedFrame is { } frame && PetWindow is { IsVisible: true, HasHealthyFrame: true, Handle: not 0 } pet &&
        ReferenceEquals(frame, pet.LastAppliedFrame);
    private bool NewFrameHealthy() => !manuallyHidden && !IsDesktopSuspended && AppliedSurfaceHealthy() &&
        ReferenceEquals(Scene.LastAppliedFrame, Scene.LastAppliedRuntime?.LastFrame);

    private void ScheduleFrameConfirmation()
    {
        if (!driverReady || readyReported || confirmation is not null || !NewFrameHealthy()) return;
        var attempt = Generation;
        var revision = confirmationRevision;
        confirmation = (_, _) =>
        {
            if (confirmation is not null) CompositionTarget.Rendering -= confirmation;
            confirmation = null;
            Scene.Dispatcher.BeginInvoke(() =>
            {
                if (attempt != Generation || revision != confirmationRevision || faultReported || readyReported || !driverReady ||
                    driver?.OwnsRuntime != true || !NewFrameHealthy() || !AllowsInteraction()) return;
                framePresented = readyReported = true;
                if (entryCommitted) ConfirmReady(attempt);
                else HealthyFrameReady?.Invoke(attempt);
            }, DispatcherPriority.Render);
        };
        CompositionTarget.Rendering += confirmation;
    }

    internal void ConfirmReady(long attempt)
    {
        if (attempt != Generation || !framePresented || faultReported || !NewFrameHealthy() || !AllowsInteraction()) return;
        entryCommitted = IsReady = true;
        PetWindow!.SetInteractionEnabled(true);
    }

    private void CancelConfirmation()
    {
        if (confirmation is not null) CompositionTarget.Rendering -= confirmation;
        confirmation = null;
        ++confirmationRevision;
    }

    private void ReleasePointer() => PetWindow?.ReleasePointerCapture();
    private void OpenMenu(Point screenPoint)
    {
        if (!IsReady || menu.Visible || !AllowsInteraction()) return;
        menu.Show(new System.Drawing.Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)));
    }

    private void FailSurface() => FailSurface($"pet native display message: 0x{PetWindow?.LastSurfaceInvalidationMessage:X4}",
        nativeDisplayMessage: PetWindow?.LastSurfaceInvalidationMessage ?? 0);

    private void FailSurface(string reason, int? nativeErrorCode = null, int nativeDisplayMessage = 0)
    {
        if (faultReported || PetWindow is null) return;
        faultReported = true;
        CaptureFailure(reason, nativeErrorCode, nativeDisplayMessage);
        var attempt = Generation;
        StopInput();
        Scene.Dispatcher.BeginInvoke(() => { if (attempt == Generation) LostDesktop?.Invoke(); });
    }

    internal void RecordDriverFailure() => CaptureFailure("room driver failed", null, 0);

    internal void RecordPreparationFailure(Exception error, string stage = "start room driver")
    {
        if (LastFailureSnapshot is not null) return;
        // Only our fixed lifecycle stage and exception type are retained.
        // Exception messages may contain external data and are never recorded.
        CaptureFailure($"pet preparation failed at {stage}: {error.GetType().Name}",
            error is Win32Exception native ? native.NativeErrorCode : null, 0);
    }

    private void CaptureFailure(string reason, int? nativeErrorCode, int nativeDisplayMessage)
    {
        LastFailureReason = reason;
        LastFailureDetail = $"SurfaceAttached={SurfaceAttached}; DesktopCurrent={desktopCurrent}; DesktopSuspended={IsDesktopSuspended}; " +
            $"SceneVisible={Scene.IsVisible}; SceneHealthy={Scene.HasHealthyFrame}; PetVisible={PetWindow?.IsVisible}; PetHealthy={PetWindow?.HasHealthyFrame}; " +
            $"PetReject={PetWindow?.LastFrameRejection}; DriverOwner={driver?.OwnsRuntime}; DriverRunning={driver?.RuntimeRunning}; " +
            $"Pointer={driver?.IsPointerActive}; PhysicalDrag={Scene.LastAppliedRuntime?.IsDragging}";
        var runtime = Scene.LastAppliedRuntime;
        LastFailureSnapshot = new(DateTimeOffset.UtcNow, reason, nativeErrorCode, nativeDisplayMessage,
            PetWindow?.LastFrameRejection ?? string.Empty, SurfaceAttached, desktopCurrent, IsDesktopSuspended,
            Scene.IsVisible, Scene.HasHealthyFrame, Scene.LastAppliedFrame is not null && ReferenceEquals(Scene.LastAppliedFrame, runtime?.LastFrame),
            PetWindow?.IsVisible == true, PetWindow?.HasHealthyFrame == true,
            Scene.LastAppliedFrame is not null && ReferenceEquals(Scene.LastAppliedFrame, PetWindow?.LastAppliedFrame), driver?.OwnsRuntime == true,
            driver?.RuntimeRunning ?? (runtime?.IsRunning == true), driver?.RuntimeHealthy ?? (runtime is { LastError: null, LastFrame: not null }),
            PetWindow?.IsPointerCaptured == true, driver?.IsPointerActive == true, runtime?.IsDragging == true, driver?.FrameDiagnostics);
    }

    internal void StopInput()
    {
        desktopCurrent = false;
        IsReady = false;
        awaitingRuntimeFrame = null;
        driverReady = framePresented = readyReported = false;
        CancelConfirmation();
        desktopCheck.Stop();
        PetWindow?.SetInteractionEnabled(false);
        driver?.ClearInput();
        menu.Close();
        Scene.InputBlocked = false;
    }

    internal void Hide(bool preserveDesktop = false)
    {
        StopInput();
        ++Generation;
        restoreWhenCurrent = false;
        if (preserveDesktop && HasRetainedPresentation && !faultReported)
        {
            manuallyHidden = true;
            PetWindow!.InvalidateDisplayedFrame();
            PetWindow.Hide();
            desktopCheck.Start();
            return;
        }
        manuallyHidden = IsDesktopSuspended = false;
        if (PetWindow is { } pet)
        {
            pet.ContextRequested -= OpenMenu;
            pet.SurfaceInvalidated -= FailSurface;
            pet.Close();
            PetWindow = null;
        }
        virtualDesktop?.Dispose();
        virtualDesktop = null;
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
