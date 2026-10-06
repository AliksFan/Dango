using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace Dango.Desktop.DesktopHost.WorkerW;

// A cached frame, a successful attach or a refresh callback is never entry readiness.
internal sealed class DesktopPetPresentationGate
{
    private long attempt;
    private long appliedBaseline;
    private long stepBaseline;
    private bool active;
    private bool applied;
    internal bool Ready { get; private set; }
    internal bool InputEnabled { get; private set; }
    internal void Begin(long value, long appliedFrames, long successfulSteps)
    {
        attempt = value; appliedBaseline = appliedFrames; stepBaseline = successfulSteps;
        active = true; applied = false; Ready = false; InputEnabled = false;
    }
    internal bool ObserveApplied(long value, long appliedFrames, long successfulSteps, bool healthy)
    {
        if (!active || attempt != value || applied || !healthy ||
            appliedFrames <= appliedBaseline || successfulSteps <= stepBaseline) return false;
        applied = true;
        return true;
    }
    internal bool ConfirmRendered(long value, bool healthy)
    {
        if (!active || attempt != value || !applied || Ready || !healthy) return false;
        return Ready = true;
    }
    internal bool Confirm(long value)
    {
        if (!active || attempt != value || !Ready) return false;
        return InputEnabled = true;
    }
    internal void Cancel() { active = false; applied = false; Ready = false; InputEnabled = false; }
}

public partial class WorkerWWindow
{
    private const double DesktopPetSafeInset = 16;
    private bool desktopPetRoomMode;
    private bool desktopPetRequestedInteraction = true;
    private bool desktopPetAlignmentPending;
    private bool desktopPetSuspended;
    private bool desktopPetFailed;
    private long desktopPetAttempt;
    private long desktopPetFaultBaseline;
    private long desktopPetLifecycle;
    private double? desktopPetResumeX;
    private LogicalRect desktopPetSafeArea;
    private StackedPhysicsPresentationFrame? desktopPetAppliedFrame;
    private EventHandler? desktopPetRenderConfirmation;
    private readonly DesktopPetPresentationGate desktopPetPresentation = new();
    private readonly Forms.ContextMenuStrip desktopPetMenu = new();

    internal bool DesktopPetRoomReady => desktopPetRoomMode && desktopPetPresentation.Ready &&
        !desktopPetFailed && !desktopPetSuspended && DesktopPetHealthyPresentation();
    internal bool OwnsDesktopPetRuntime => desktopPetRoomMode && motionScheduler.OwnsRuntime;
    internal Func<WorkerWDesktopTarget?> DesktopPetTargetFinder { get; set; } = NativeMethods.FindWorkerW;
    internal event Action<long>? DesktopPetRoomFrameReady;
    internal event Action<long, string>? DesktopPetRoomFailed;
    internal event Action? DesktopPetRoomDoorRequested;
    internal event Action? DesktopPetRoomHideRequested;
    internal event Action? DesktopPetRoomControlCenterRequested;

    private bool DesktopPetInputActive => DesktopPetRoomReady && desktopPetPresentation.InputEnabled &&
        !desktopPetMenu.Visible;

    private void InitializeDesktopPetRoom()
    {
        motionScheduler.CallbackCompleted += OnDesktopPetFrameCompleted;
        void Add(string title, Action action)
        {
            var item = new Forms.ToolStripMenuItem(title);
            item.Click += (_, _) => { desktopPetMenu.Close(); ClearHostInput(); action(); };
            desktopPetMenu.Items.Add(item);
        }
        Add("房门 · 返回门廊", () => DesktopPetRoomDoorRequested?.Invoke());
        Add("暂时隐藏", () => DesktopPetRoomHideRequested?.Invoke());
        Add("打开控制中心", () => DesktopPetRoomControlCenterRequested?.Invoke());
        desktopPetMenu.Opening += (_, _) => ClearHostInput();
        desktopPetMenu.Closed += (_, _) => ClearHostInput();
    }

    internal void BeginDesktopPetRoom(long attempt, bool enterWorld)
    {
        ObjectDisposedException.ThrowIf(isClosed, this);
        // Do not cancel the input or physics owned by another still-active host.
        motionScheduler.VerifyCanStart();
        SetVisibility(false);
        desktopPetRoomMode = true;
        desktopPetAttempt = attempt;
        desktopPetFailed = false;
        desktopPetSuspended = false;
        desktopPetAlignmentPending = true;
        if (enterWorld) desktopPetResumeX = null;
        isInteractionEnabled = false;
        desktopPetAppliedFrame = null;
        desktopPetPresentation.Begin(attempt, motionScheduler.Snapshot.AppliedFrames, motionScheduler.Snapshot.SuccessfulSteps);
        if (motionRuntime.DangoCount != 1)
        {
            ReportDesktopPetFailure("本地桌宠房间需要一只团子，请返回设置重试。");
            return;
        }
        SetVisibility(true);
    }

    internal bool ConfirmDesktopPetRoom(long attempt)
    {
        if (!DesktopPetRoomReady || !desktopPetPresentation.Confirm(attempt)) return false;
        Opacity = 1;
        SetInteractionEnabled(desktopPetRequestedInteraction);
        ApplyObjectLayerVisibility();
        UpdateInputProxyBounds();
        return true;
    }

    // Preserve only the already-rendered picture until the next host is healthy.
    // The source has no input and no physics owner while suspended.
    internal void SuspendDesktopPetRoom()
    {
        if (!desktopPetRoomMode) return;
        if (motionScheduler.OwnsRuntime && motionRuntime.CurrentSelectedCenter is { } center) desktopPetResumeX = center.X;
        CancelDesktopPetPresentation();
        desktopPetSuspended = true;
        shellTimer.Stop();
        isInteractionEnabled = false;
        ClearHostInput();
        foreach (var proxy in inputProxies.Values) proxy.SetEnabled(false);
        keyboardInputBridge?.Refresh(false, false, false);
        dangoAboveWindow.SetInteractionEnabled(false);
        StopMotion();
    }

    private void CancelDesktopPetPresentation()
    {
        ++desktopPetLifecycle;
        desktopPetPresentation.Cancel();
        if (desktopPetRenderConfirmation is not null) CompositionTarget.Rendering -= desktopPetRenderConfirmation;
        desktopPetRenderConfirmation = null;
        if (desktopPetMenu.Visible) desktopPetMenu.Close();
    }

    private void ConfigureDesktopPetSurface()
    {
        if (!desktopPetRoomMode || nativeHost is null || windowSource is null) return;
        // The existing WorkerW host already uses a system-managed layered HWND.
        // Key its renderer background out instead of placing an opaque room over the wallpaper.
        // Pick a key different from the two user-selected solid appearance colours.
        var candidates = new[] { WpfColor.FromRgb(1, 2, 3), WpfColor.FromRgb(3, 1, 2), WpfColor.FromRgb(2, 3, 1) };
        var appearance = motionRuntime.EffectiveAppearance;
        var key = candidates.First(color =>
            (color.R != appearance.BodyColor.R || color.G != appearance.BodyColor.G || color.B != appearance.BodyColor.B) &&
            (color.R != appearance.EyeColor.R || color.G != appearance.EyeColor.G || color.B != appearance.EyeColor.B));
        var colorRef = (uint)(key.R | key.G << 8 | key.B << 16);
        if (!SetDesktopPetLayeredWindowAttributes(nativeHost.Handle, colorRef, byte.MaxValue, 0x1 | 0x2))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "桌宠透明宿主不可用。");
        Background = new SolidColorBrush(key);
        windowSource.CompositionTarget.BackgroundColor = key;
        CushionVisual.Visibility = IslandVisual.Visibility = Visibility.Collapsed;
        HideLayeredObjectWindows();
    }

    private void StartDesktopPetMotion(double width, double height)
    {
        Width = width; Height = height;
        Measure(new(width, height)); Arrange(new(0, 0, width, height));
        var screen = Forms.Screen.PrimaryScreen ?? throw new InvalidOperationException("桌面工作区域不可用。");
        var topLeft = PointFromScreen(new(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = PointFromScreen(new(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        desktopPetSafeArea = new(topLeft.X + DesktopPetSafeInset, topLeft.Y + DesktopPetSafeInset,
            bottomRight.X - topLeft.X - 2 * DesktopPetSafeInset,
            bottomRight.Y - topLeft.Y - 2 * DesktopPetSafeInset);
        if (!ValidDesktopPetArea(desktopPetSafeArea)) throw new InvalidOperationException("桌面安全工作区过小或不可用。");
        sceneSession.AttachSurface(DesktopPetControllerArea(desktopPetSafeArea), NowMilliseconds());
        motionRuntime.SetViewport(desktopPetSafeArea);
        motionRuntime.Start();
        lastDesktopForeground = null;
        motionRuntime.AttachQuietIdleHost(this, false);
        motionScheduler.Start();
        var baseline = motionScheduler.Snapshot;
        desktopPetFaultBaseline = baseline.Faults;
        desktopPetPresentation.Begin(desktopPetAttempt, baseline.AppliedFrames, baseline.SuccessfulSteps);
    }

    internal static bool ValidDesktopPetArea(LogicalRect bounds) => bounds.Width >= 240 && bounds.Height >= 200 &&
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);

    private static LogicalRect DesktopPetControllerArea(LogicalRect safe) =>
        new(safe.X - 80, safe.Y - 60, safe.Width + 160, safe.Height + 120);

    internal static bool TryPlaceDesktopPetRoom(StackedDangoSceneRuntime runtime, DesktopSceneSession session,
        LogicalRect safeArea, double? requestedX = null)
    {
        if (!ValidDesktopPetArea(safeArea) || runtime.DangoCount != 1 ||
            runtime.CurrentSelectedCenter is not { } existing || runtime.LocalGroundY is not { } previousGround) return false;
        var target = new LogicalPoint(requestedX ?? safeArea.Right - 180, safeArea.Bottom - (previousGround - existing.Y));
        if (!runtime.TryGetHorizontalPlacement(target, safeArea.X, safeArea.Right, out var placement)) return false;
        // The controller stores a fixed 160x120 reference box. Its blank margin is
        // larger than the real body footprint, so clamping that box to the floor
        // would move the reference core and cause Tick to rehost/clear input again.
        // Only the reference domain gets padding; native walls/floor stay in safeArea.
        session.AttachSurface(DesktopPetControllerArea(safeArea), NowMilliseconds());
        var bounds = session.Controller!.Snapshot.Dango.VisualBounds;
        session.Controller.MoveTo(new(placement.X - bounds.Width / 2, placement.Y - bounds.Height / 2));
        if (session.Controller.Snapshot.Dango.VisualBounds.Center != placement) return false;
        if (!runtime.RehostSelectedEntityAt(placement)) return false;
        // Rehost removes the previous local-world walls before the next Step.
        runtime.SetHorizontalBounds(safeArea.X, safeArea.Right);
        return true;
    }

    private bool PrepareDesktopPetMotion()
    {
        if (desktopPetSuspended || desktopPetFailed) return false;
        if (motionRuntime.LastError is not null || motionRuntime.AttachmentError is not null ||
            motionRuntime.LightInteractionError is not null || motionRuntime.QuietIdleError is not null)
        { ReportDesktopPetFailure("团子暂不可用，请返回设置重试。"); return false; }
        if (!desktopPetAlignmentPending) return true;
        try
        {
            // A cold runtime may need one native step to publish placement geometry.
            if (motionRuntime.CurrentSelectedCenter is null)
            { motionRuntime.SetDirectionalControl(Dango.Core.Animation.DirectionalControlIntent.None); return true; }
            if (!TryPlaceDesktopPetRoom(motionRuntime, sceneSession, desktopPetSafeArea, desktopPetResumeX))
            { ReportDesktopPetFailure("桌面放置不可用，请从门廊重试。"); return false; }
            desktopPetAlignmentPending = false;
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or Dango.Physics.DangoPhysicsException)
        { ReportDesktopPetFailure("桌宠物理准备失败，请返回设置重试。"); return false; }
    }

    private bool DesktopPetHealthyPresentation() => !isClosed && isVisible && !desktopPetAlignmentPending &&
        !desktopPetFailed && !desktopPetSuspended && motionScheduler.OwnsRuntime && motionScheduler.IsRunning &&
        motionRuntime.IsRunning && recoveryState.State == WorkerWRecoveryState.Attached &&
        currentTarget is not null && NativeMethods.IsValidDesktopTarget(currentTarget) && windowSource is not null &&
        nativeHost is not null && NativeMethods.IsValidWindow(nativeHost.Handle) && NativeMethods.IsValidWindow(windowHandle) &&
        motionRuntime.LastError is null && motionRuntime.AttachmentError is null && motionRuntime.LightInteractionError is null &&
        motionRuntime.QuietIdleError is null && motionRuntime.LastPhysicsFrame?.Succeeded == true &&
        motionRuntime.LayoutWarning is null && desktopPetAppliedFrame is { Bodies.Count: 1, EntityIds.Count: 1, ConstraintsReleased: false } &&
        desktopPetAppliedFrame.Bodies[0].IsVisible &&
        desktopPetAppliedFrame.Bodies[0].FallbackReason == PhysicsPresentationFallbackReason.None &&
        DangoVisual.LastContourFailure == DangoContourFailure.None &&
        ReferenceEquals(desktopPetAppliedFrame, motionRuntime.LastFrame);

    private void OnDesktopPetFrameCompleted(object? sender, EventArgs e)
    {
        if (!desktopPetRoomMode || desktopPetFailed || desktopPetSuspended) return;
        var snapshot = motionScheduler.Snapshot;
        if (snapshot.Faults != desktopPetFaultBaseline)
        { ReportDesktopPetFailure("桌宠新物理帧不可用，请返回设置重试。"); return; }
        if (desktopPetRenderConfirmation is not null || !desktopPetPresentation.ObserveApplied(desktopPetAttempt,
                snapshot.AppliedFrames, snapshot.SuccessfulSteps, DesktopPetHealthyPresentation())) return;
        var attempt = desktopPetAttempt;
        var lifecycle = desktopPetLifecycle;
        EventHandler? confirmation = null;
        confirmation = (_, _) =>
        {
            CompositionTarget.Rendering -= confirmation;
            if (!ReferenceEquals(desktopPetRenderConfirmation, confirmation)) return;
            desktopPetRenderConfirmation = null;
            Dispatcher.BeginInvoke(() =>
            {
                if (lifecycle != desktopPetLifecycle || !desktopPetPresentation.ConfirmRendered(attempt,
                        DesktopPetHealthyPresentation())) return;
                DesktopPetRoomFrameReady?.Invoke(attempt);
            }, DispatcherPriority.Render);
        };
        desktopPetRenderConfirmation = confirmation;
        CompositionTarget.Rendering += confirmation;
    }

    private WallpaperWheelInputContext? DesktopPetInputContext() => currentTarget is not null && windowSource is not null &&
        nativeHost is not null ? CreateDesktopPetInputContext() : null;

    private WallpaperWheelInputContext CreateDesktopPetInputContext()
    {
        var origin = PointToScreen(new(0, 0));
        return new(currentTarget!, new(origin.X, origin.Y, nativeHost!.SurfaceSize.Width, nativeHost.SurfaceSize.Height),
            desktopPetAttempt, DesktopPetInputActive, desktopPetMenu.Visible || !isInteractionEnabled);
    }

    private bool DesktopPetPointerEligible()
    {
        try
        {
            if (DesktopPetInputContext() is not { } context || !NativeMethods.GetCursorPos(out var cursor)) return false;
            var point = new WallpaperWheelPoint(cursor.X, cursor.Y);
            var environment = NativeMethods.InspectRoomGameplayInput(context, point, false, allowShift: true);
            return WallpaperWheelInputPolicy.ShouldApply(context, DesktopPetInputContext(), point, environment);
        }
        catch { return false; }
    }

    private bool DesktopPetProxyEligible()
    {
        if (!DesktopPetPointerEligible()) return false;
        if (lightInteraction.IsActive || motionRuntime.IsDragging) return true;
        if (motionRuntime.LastFrame is not { EntityIds.Count: 1 } frame ||
            !NativeMethods.GetCursorPos(out var cursor)) return false;
        var point = PointFromScreen(new(cursor.X, cursor.Y));
        // Blank corners of the bounded native proxy remain normal desktop space.
        return DesktopPetBodyContains(frame, new(point.X, point.Y));
    }

    internal static bool DesktopPetBodyContains(StackedPhysicsPresentationFrame frame, LogicalPoint point)
    {
        if (frame is not { Bodies.Count: 1, EntityIds.Count: 1, ConstraintsReleased: false } ||
            !frame.Bodies[0].IsVisible ||
            frame.Bodies[0].FallbackReason != PhysicsPresentationFallbackReason.None ||
            !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        var bounds = DangoHostFrameBinding.Bounds(frame, 0);
        if (!bounds.Contains(point)) return false;
        var geometry = DangoStreamGeometryFactory.TryCreate(frame.Bodies[0].Contour, bounds.Width, bounds.Height);
        if (!geometry.Succeeded) return false;
        var inverse = DeformableDangoVisual.CreateGaitTransform(frame.Bodies[0], frame.Gait).Inverse;
        return inverse is not null && geometry.Geometry!.FillContains(inverse.Transform(new(point.X - bounds.X, point.Y - bounds.Y)));
    }

    private void OpenDesktopPetMenu()
    {
        if (!DesktopPetPointerEligible() || desktopPetMenu.Visible ||
            motionRuntime.LastFrame is not { } frame || !NativeMethods.GetCursorPos(out var cursor)) return;
        var point = PointFromScreen(new(cursor.X, cursor.Y));
        if (!DesktopPetBodyContains(frame, new(point.X, point.Y))) return;
        ClearHostInput();
        desktopPetMenu.Show(Forms.Cursor.Position);
    }

    private void ReportDesktopPetFailure(string message)
    {
        if (desktopPetFailed) return;
        desktopPetFailed = true;
        var attempt = desktopPetAttempt;
        isInteractionEnabled = false;
        CancelDesktopPetPresentation();
        ClearHostInput();
        shellTimer.Stop();
        StopMotion();
        Opacity = 0;
        foreach (var proxy in inputProxies.Values) proxy.SetEnabled(false);
        keyboardInputBridge?.Refresh(false, false, false);
        // Failure never calls into another owner's start from inside a physics callback.
        var failedLifecycle = desktopPetLifecycle;
        Dispatcher.BeginInvoke(() =>
        {
            if (desktopPetFailed && attempt == desktopPetAttempt && failedLifecycle == desktopPetLifecycle)
                DesktopPetRoomFailed?.Invoke(attempt, message);
        });
    }

    [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDesktopPetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
}
