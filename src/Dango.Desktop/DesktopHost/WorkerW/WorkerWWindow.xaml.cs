using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;
using Dango.Desktop.AppShell;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using CoreDpiScale = Dango.Core.Coordinates.DpiScale;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfColor = System.Windows.Media.Color;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.DesktopHost.WorkerW;

public partial class WorkerWWindow : WpfUserControl, IDesktopHostControl, IDesktopLayeringControl, IKeyboardControl, IDesktopHostSwitchGuard
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly DesktopInteractionController interactionController = new();
    private readonly WorkerWRecoveryStateMachine recoveryState = new(TimeSpan.FromSeconds(8), 8);
    private readonly DispatcherTimer shellTimer;
    private readonly DispatcherTimer motionTimer;
    private readonly DesktopObjectOverlayWindow dangoAboveWindow;
    private readonly DesktopObjectOverlayWindow cushionAboveWindow;
    private readonly IslandOverlayWindow islandOverlayWindow;
    private DesktopHitTestPolicy hitTestPolicy = new([]);
    private System.Windows.Point dragPointerOrigin;
    private double dragVisualLeft;
    private double dragVisualTop;
    private nint windowHandle;
    private WorkerWNativeHost? nativeHost;
    private WorkerWInputProxy? inputProxy;
    private WorkerWKeyboardInputBridge? keyboardInputBridge;
    private HwndSource? windowSource;
    private WorkerWDesktopTarget? currentTarget;
    private bool isInteractionEnabled = true;
    private bool isVisible;
    private bool isClosed;
    private bool scenePositionInitialized;
    private DesktopLayeringSettings layeringSettings = DesktopLayeringSettings.CreateDefault();
    private KeyboardControlSettings keyboardControlSettings = KeyboardControlSettings.CreateDefault();
    private string? lastError;
    private readonly SingleDangoMotionRuntime motionRuntime;
    private readonly DesktopSceneSession sceneSession;
    private SingleDangoMotionController? motionController => sceneSession.Controller;
    private DangoActionState reactionReturnState = DangoActionState.Idle;
    private LogicalPoint proxyDragPointer;
    private double dragGrabVisualX;
    private double dragGrabVisualY;
    private PresentationPoint? dragGrabPoint;
    private bool proxyDragMoved;
    private bool aboveDragPending;

    public WorkerWWindow(
        DesktopSceneSession sceneSession,
        SingleDangoMotionRuntimeFactory motionRuntimeFactory)
    {
        this.sceneSession = sceneSession ?? throw new ArgumentNullException(nameof(sceneSession));
        ArgumentNullException.ThrowIfNull(motionRuntimeFactory);
        InitializeComponent();
        dangoAboveWindow = new DesktopObjectOverlayWindow(
            PrototypeDesktopObject.Dango,
            OnAboveDangoMoved,
            BeginAboveDangoDrag,
            CompleteAboveDangoDrag,
            CancelAboveDangoDrag);
        cushionAboveWindow = new DesktopObjectOverlayWindow(PrototypeDesktopObject.Cushion);
        islandOverlayWindow = new IslandOverlayWindow();
        motionRuntime = motionRuntimeFactory(new MirroredDangoSink(DynamicDangoVisual, dangoAboveWindow));
        motionTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1d / 60d),
        };
        motionTimer.Tick += OnMotionTimerTick;
        shellTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        shellTimer.Tick += OnShellTimerTick;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        LostKeyboardFocus += OnLostKeyboardFocus;
        KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionScene();
        RebuildHitTestPolicy();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionScene();
        RebuildHitTestPolicy();
    }

    private nint WindowMessageHook(
        nint sourceWindow,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WmNcHitTest && !isInteractionEnabled)
        {
            handled = true;
            return HtTransparent;
        }

        if (message != WmMouseActivate)
        {
            return nint.Zero;
        }

        handled = true;
        return MaNoActivate;
    }

    public DesktopHostSnapshot GetSnapshot() =>
        new(
            "Wallpaper",
            isVisible && recoveryState.State == WorkerWRecoveryState.Attached,
            isInteractionEnabled,
            recoveryState.State == WorkerWRecoveryState.Attached ? "已通过人工验收" : StateText(),
            lastError);

    public void SetVisibility(bool isVisible)
    {
        if (!isVisible)
        {
            CancelDrag(DragCancellationReason.WindowDeactivated);
            shellTimer.Stop();
            DisposeSurface();
            HideLayeredObjectWindows();
            currentTarget = null;
            recoveryState.EnterSafeHidden();
            return;
        }

        if (isClosed)
        {
            throw new ObjectDisposedException(nameof(WorkerWWindow));
        }

        Opacity = 0;
        if (recoveryState.State is WorkerWRecoveryState.Detached or WorkerWRecoveryState.SafeHidden)
        {
            BeginAttachment();
        }
    }

    public void SetInteractionEnabled(bool isEnabled)
    {
        if (!isEnabled)
        {
            CancelDrag(DragCancellationReason.WindowDeactivated);
        }

        isInteractionEnabled = isEnabled;
        keyboardInputBridge?.Refresh(
            recoveryState.State == WorkerWRecoveryState.Attached && isVisible,
            isEnabled);
        DangoVisual.IsHitTestVisible = isEnabled;
        IslandVisual.IsHitTestVisible = isEnabled;
        inputProxy?.SetEnabled(isEnabled);
        dangoAboveWindow.SetInteractionEnabled(isEnabled);
        ApplyObjectLayerVisibility();
    }

    public DesktopLayeringSettings GetLayeringSettings() => layeringSettings;

    public KeyboardControlSettings GetKeyboardControlSettings() => keyboardControlSettings;

    public string? GetSwitchBlockReason()
    {
        if (interactionController.IsDragging || motionController?.IsDragging == true)
        {
            return "团子拖动结束后才能切换显示模式。";
        }

        return recoveryState.State == WorkerWRecoveryState.Recovering
            ? "壁纸层恢复完成后才能切换显示模式。"
            : null;
    }

    public void ApplyKeyboardControlSettings(KeyboardControlSettings settings)
    {
        KeyboardControlSettingsValidator.Validate(settings);
        keyboardControlSettings = settings;
        keyboardInputBridge?.ApplySettings(settings);
    }

    public void ApplyLayeringSettings(DesktopLayeringSettings settings)
    {
        DesktopLayeringSettingsValidator.Validate(settings);
        layeringSettings = new DesktopLayeringSettings(
            settings.DangoLayer,
            new Dictionary<string, DesktopObjectLayer>(settings.FurnitureLayers, StringComparer.Ordinal));
        ApplyObjectLayerVisibility();
    }

    public string CreateDiagnosticSummary()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return string.Join(
            Environment.NewLine,
            "Dango P1-003 WorkerW/Shell 诊断",
            $"运行时：.NET {Environment.Version}",
            $"系统：{RuntimeInformation.OSDescription}",
            $"进程架构：{RuntimeInformation.ProcessArchitecture}",
            $"恢复状态：{recoveryState.State}",
            $"父句柄有效：{NativeMethods.IsValidWindow(recoveryState.ParentHandle)}",
            $"桌面结构：{(currentTarget?.IsRaisedDesktop == true ? "Raised desktop" : "Legacy/未挂载")}",
            $"恢复尝试：{recoveryState.RecoveryAttempts}",
            $"窗口逻辑尺寸：{ActualWidth:F0} × {ActualHeight:F0}",
            $"DPI 比例：{dpi.DpiScaleX:F2} × {dpi.DpiScaleY:F2}",
            $"桌面显示：{GetSnapshot().IsVisible}",
            $"允许交互：{isInteractionEnabled}",
            $"键盘控制：{(keyboardControlSettings.IsEnabled ? "开启" : "关闭")}",
            $"键位方案：{keyboardControlSettings.Scheme}",
            $"键盘输入桥：{keyboardInputBridge?.State ?? "Inactive"}",
            $"团子层级：{layeringSettings.DangoLayer}",
            $"占位软垫层级：{layeringSettings.GetFurnitureLayer(DesktopLayeringSettings.PrototypeCushionId)}",
            $"悬浮岛避让全屏：{NativeMethods.IsForegroundFullscreen()}",
            $"拖动中：{interactionController.IsDragging}",
            $"最近错误：{lastError ?? "无"}");
    }

    private void BeginAttachment()
    {
        if (isClosed)
        {
            return;
        }

        Opacity = 0;
        recoveryState.BeginAttach(DateTimeOffset.Now);
        lastError = null;
        TryAttach();
        shellTimer.Start();
    }

    private void TryAttach()
    {
        if (!recoveryState.TryBeginRecoveryAttempt(DateTimeOffset.Now))
        {
            if (recoveryState.State == WorkerWRecoveryState.SafeHidden)
            {
                EnterSafeHidden("WorkerW 恢复超时或重试次数耗尽，已安全隐藏。");
            }

            return;
        }

        var target = NativeMethods.FindWorkerW();
        if (!NativeMethods.IsValidDesktopTarget(target))
        {
            lastError = "未发现兼容的 WorkerW 桌面层，已保持安全隐藏。";
            return;
        }

        try
        {
            CreateSurface(target!);
            currentTarget = target;
            recoveryState.AttachSucceeded(target!.ParentHandle);
            Opacity = 1;
            isVisible = true;
            var dpi = VisualTreeHelper.GetDpi(this);
            var logicalSurface = WorkerWSurfaceCoordinateMapper.ToLogicalBounds(
                nativeHost!.SurfaceSize.Width,
                nativeHost.SurfaceSize.Height,
                new CoreDpiScale(dpi.DpiScaleX, dpi.DpiScaleY));
            StartMotion(logicalSurface.Width, logicalSurface.Height);
            lastError = null;
            ApplyObjectLayerVisibility();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DisposeSurface();
            lastError = exception is System.ComponentModel.Win32Exception win32Exception
                ? $"WorkerW 挂载失败：Win32 {win32Exception.NativeErrorCode}，{win32Exception.Message}"
                : $"WorkerW 挂载失败：{exception.GetType().Name}。";
        }
    }

    private void OnShellTimerTick(object? sender, EventArgs e)
    {
        if (recoveryState.State == WorkerWRecoveryState.Attached &&
            !NativeMethods.IsValidDesktopTarget(currentTarget))
        {
            Opacity = 0;
            motionRuntime.NotifyHostUnavailable();
            motionTimer.Stop();
            DisposeSurface();
            currentTarget = null;
            recoveryState.NotifyShellHandleLost(DateTimeOffset.Now);
            lastError = "原 WorkerW 句柄已失效，正在恢复。";
        }

        if (recoveryState.State == WorkerWRecoveryState.Recovering)
        {
            TryAttach();
        }
    }

    private void EnterSafeHidden(string error)
    {
        shellTimer.Stop();
        DisposeSurface();
        currentTarget = null;
        recoveryState.EnterSafeHidden();
        lastError = error;
    }

    private string StateText() => recoveryState.State switch
    {
        WorkerWRecoveryState.Recovering => "正在发现或恢复 Shell 宿主",
        WorkerWRecoveryState.SafeHidden => "恢复失败，已安全隐藏",
        WorkerWRecoveryState.Stopped => "已停止",
        _ => "尚未挂载",
    };

    private void OnDangoMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pointer = e.GetPosition(SceneCanvas);
        var target = hitTestPolicy.HitTest(new LogicalPoint(pointer.X, pointer.Y));
        if (!interactionController.TryBeginDrag(target))
        {
            return;
        }

        Focus();
        dragPointerOrigin = pointer;
        SetDragGrabPoint(e.GetPosition(DangoVisual));
        dragVisualLeft = WpfCanvas.GetLeft(DangoVisual);
        dragVisualTop = WpfCanvas.GetTop(DangoVisual);
        if (!DangoVisual.CaptureMouse())
        {
            interactionController.CancelDrag(DragCancellationReason.LostCapture);
        }

        e.Handled = true;
    }

    private void OnDangoMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!interactionController.IsDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pointer = e.GetPosition(SceneCanvas);
        if (motionController?.IsDragging == false)
        {
            motionController.BeginDrag(
                new LogicalPoint(dragPointerOrigin.X, dragPointerOrigin.Y),
                NowMilliseconds());
        }
        motionController?.MoveDrag(new LogicalPoint(pointer.X, pointer.Y));
        ApplyAuthoritativePosition();
        RebuildHitTestPolicy();
    }

    private void OnDangoMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (interactionController.CompleteDrag() && DangoVisual.IsMouseCaptured)
        {
            DangoVisual.ReleaseMouseCapture();
        }

        if (motionController?.IsDragging == true)
        {
            motionController.ReleaseDrag(NowMilliseconds());
        }
        else
        {
            TriggerReaction();
        }

        dragGrabPoint = null;

        e.Handled = true;
    }

    private void OnDangoLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        interactionController.CancelDrag(DragCancellationReason.LostCapture);
        motionController?.CancelDrag(NowMilliseconds());
        dragGrabPoint = null;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelDrag(DragCancellationReason.Escape);
            e.Handled = true;
        }
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CancelDrag(DragCancellationReason.WindowDeactivated);

    public void Close()
    {
        if (isClosed)
        {
            return;
        }

        isClosed = true;
        shellTimer.Stop();
        CancelDrag(DragCancellationReason.WindowClosing);
        motionTimer.Stop();
        motionRuntime.Dispose();
        DisposeSurface();
        currentTarget = null;
        recoveryState.Stop();
        dangoAboveWindow.Close();
        cushionAboveWindow.Close();
        islandOverlayWindow.Close();
    }

    private void CreateSurface(WorkerWDesktopTarget target)
    {
        DisposeSurface();
        nativeHost = new WorkerWNativeHost(target);
        var parameters = new HwndSourceParameters(
            "Dango P1-003 WPF Desktop Renderer",
            nativeHost.SurfaceSize.Width,
            nativeHost.SurfaceSize.Height)
        {
            ParentWindow = nativeHost.Handle,
            WindowStyle = WsChild | WsVisible,
            ExtendedWindowStyle = WsExToolWindow,
            UsesPerPixelTransparency = false,
            TreatAsInputRoot = true,
            PositionX = 0,
            PositionY = 0,
        };

        try
        {
            windowSource = new HwndSource(parameters);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new System.ComponentModel.Win32Exception(
                exception.NativeErrorCode,
                "创建透明桌面子窗口失败。");
        }

        windowHandle = windowSource.Handle;
        windowSource.CompositionTarget.BackgroundColor = WpfColor.FromRgb(0xEA, 0xF4, 0xFF);
        windowSource.RootVisual = this;
        windowSource.AddHook(WindowMessageHook);
        inputProxy = new WorkerWInputProxy(
            target,
            BeginProxyDrag,
            MoveProxyDrag,
            CompleteProxyDrag,
            CancelProxyDrag);
        inputProxy.SetEnabled(isInteractionEnabled);
        keyboardInputBridge = new WorkerWKeyboardInputBridge(target);
        keyboardInputBridge.ApplySettings(keyboardControlSettings);
        Dispatcher.BeginInvoke(RebuildHitTestPolicy, DispatcherPriority.Loaded);
    }

    private void DisposeSurface()
    {
        isVisible = false;
        Opacity = 0;
        motionTimer.Stop();
        motionRuntime.NotifyHostUnavailable();
        inputProxy?.Dispose();
        inputProxy = null;
        keyboardInputBridge?.Dispose();
        keyboardInputBridge = null;
        if (windowSource is not null)
        {
            windowSource.RemoveHook(WindowMessageHook);
            windowSource.RootVisual = null;
            windowSource.Dispose();
            windowSource = null;
        }

        nativeHost?.Dispose();
        nativeHost = null;
        windowHandle = nint.Zero;
    }

    private void PositionScene()
    {
        if (scenePositionInitialized || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var left = Math.Max(0, ActualWidth - 520);
        var top = Math.Max(0, ActualHeight - 320);
        WpfCanvas.SetLeft(IslandVisual, left + 80);
        WpfCanvas.SetTop(IslandVisual, top + 190);
        WpfCanvas.SetLeft(DangoVisual, left + 185);
        WpfCanvas.SetTop(DangoVisual, top + 55);
        WpfCanvas.SetLeft(CushionVisual, left + 30);
        WpfCanvas.SetTop(CushionVisual, top + 245);
        scenePositionInitialized = true;
    }

    private void CancelDrag(DragCancellationReason reason)
    {
        if (interactionController.CancelDrag(reason) && DangoVisual.IsMouseCaptured)
        {
            DangoVisual.ReleaseMouseCapture();
        }

        motionController?.CancelDrag(NowMilliseconds());
    }

    private void RebuildHitTestPolicy()
    {
        var regions = new[]
        {
            Region(DesktopHitTarget.Dango, DangoVisual),
            Region(DesktopHitTarget.Island, IslandVisual),
        }.OfType<DesktopHitRegion>();
        hitTestPolicy = new DesktopHitTestPolicy(regions);
        UpdateInputProxyBounds();
    }

    private bool BeginProxyDrag(double deviceLocalX, double deviceLocalY)
    {
        if (!isInteractionEnabled || !interactionController.TryBeginDrag(DesktopHitTarget.Dango))
        {
            return false;
        }

        dragVisualLeft = WpfCanvas.GetLeft(DangoVisual);
        dragVisualTop = WpfCanvas.GetTop(DangoVisual);
        var dpi = VisualTreeHelper.GetDpi(this);
        SetDragGrabPoint(new System.Windows.Point(
            deviceLocalX / dpi.DpiScaleX,
            deviceLocalY / dpi.DpiScaleY));
        proxyDragPointer = new LogicalPoint(
            dragVisualLeft + dragGrabVisualX,
            dragVisualTop + dragGrabVisualY);
        proxyDragMoved = false;
        return true;
    }

    private void MoveProxyDrag(double deviceDeltaX, double deviceDeltaY)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var deltaX = deviceDeltaX / dpi.DpiScaleX;
        var deltaY = deviceDeltaY / dpi.DpiScaleY;
        if (!proxyDragMoved && Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) < 3)
        {
            return;
        }

        if (!proxyDragMoved)
        {
            proxyDragMoved = true;
            motionController?.BeginDrag(proxyDragPointer, NowMilliseconds());
        }
        motionController?.MoveDrag(new LogicalPoint(
            proxyDragPointer.X + deltaX,
            proxyDragPointer.Y + deltaY));
        ApplyAuthoritativePosition();
        RebuildHitTestPolicy();
    }

    private void CompleteProxyDrag()
    {
        interactionController.CompleteDrag();
        if (motionController?.IsDragging == true)
        {
            motionController.ReleaseDrag(NowMilliseconds());
        }
        else
        {
            TriggerReaction();
        }
        dragGrabPoint = null;
    }

    private void CancelProxyDrag()
    {
        interactionController.CancelDrag(DragCancellationReason.LostCapture);
        motionController?.CancelDrag(NowMilliseconds());
        dragGrabPoint = null;
    }

    private void UpdateInputProxyBounds()
    {
        if (inputProxy is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = DangoVisual.ActualWidth > 0 ? DangoVisual.ActualWidth : DangoVisual.Width;
        var height = DangoVisual.ActualHeight > 0 ? DangoVisual.ActualHeight : DangoVisual.Height;
        inputProxy.SetBounds(
            (int)Math.Round(WpfCanvas.GetLeft(DangoVisual) * dpi.DpiScaleX),
            (int)Math.Round(WpfCanvas.GetTop(DangoVisual) * dpi.DpiScaleY),
            (int)Math.Round(width * dpi.DpiScaleX),
            (int)Math.Round(height * dpi.DpiScaleY));
    }

    private void ApplyObjectLayerVisibility()
    {
        if (!isVisible || isClosed)
        {
            HideLayeredObjectWindows();
            return;
        }

        var dangoAbove = layeringSettings.DangoLayer == DesktopObjectLayer.AboveDesktopIcons;
        var cushionAbove = layeringSettings.GetFurnitureLayer(DesktopLayeringSettings.PrototypeCushionId) ==
            DesktopObjectLayer.AboveDesktopIcons;

        DangoVisual.Visibility = dangoAbove ? Visibility.Collapsed : Visibility.Visible;
        CushionVisual.Visibility = cushionAbove ? Visibility.Collapsed : Visibility.Visible;
        IslandVisual.Visibility = Visibility.Collapsed;

        dangoAboveWindow.SetInteractionEnabled(isInteractionEnabled);
        dangoAboveWindow.SetPosition(WpfCanvas.GetLeft(DangoVisual), WpfCanvas.GetTop(DangoVisual));
        cushionAboveWindow.SetPosition(WpfCanvas.GetLeft(CushionVisual), WpfCanvas.GetTop(CushionVisual));
        SetWindowVisibility(dangoAboveWindow, dangoAbove);
        SetWindowVisibility(cushionAboveWindow, cushionAbove);
        islandOverlayWindow.SetDesiredVisibility(true);
        inputProxy?.SetEnabled(isInteractionEnabled && !dangoAbove);
    }

    private void HideLayeredObjectWindows()
    {
        dangoAboveWindow.Hide();
        cushionAboveWindow.Hide();
        islandOverlayWindow.SetDesiredVisibility(false);
        inputProxy?.SetEnabled(false);
        keyboardInputBridge?.Refresh(hostReady: false, interactionEnabled: false);
    }

    private static void SetWindowVisibility(Window window, bool isVisible)
    {
        if (isVisible && !window.IsVisible)
        {
            window.Show();
        }
        else if (!isVisible && window.IsVisible)
        {
            window.Hide();
        }
    }

    private void OnAboveDangoMoved(double left, double top)
    {
        if (aboveDragPending && motionController?.IsDragging == false)
        {
            aboveDragPending = false;
            motionController.BeginDrag(
                new LogicalPoint(dragVisualLeft + dragGrabVisualX, dragVisualTop + dragGrabVisualY),
                NowMilliseconds());
        }
        motionController?.MoveDrag(new LogicalPoint(left + dragGrabVisualX, top + dragGrabVisualY));
        ApplyAuthoritativePosition();
        RebuildHitTestPolicy();
    }

    private void BeginAboveDangoDrag(double localX, double localY)
    {
        dragVisualLeft = dangoAboveWindow.Left;
        dragVisualTop = dangoAboveWindow.Top;
        SetDragGrabPoint(new System.Windows.Point(localX, localY));
        aboveDragPending = true;
    }

    private void CompleteAboveDangoDrag()
    {
        if (motionController?.IsDragging == true)
        {
            motionController.ReleaseDrag(NowMilliseconds());
        }
        else if (aboveDragPending)
        {
            TriggerReaction();
        }

        aboveDragPending = false;
        dragGrabPoint = null;
    }

    private void CancelAboveDangoDrag()
    {
        aboveDragPending = false;
        motionController?.CancelDrag(NowMilliseconds());
        dragGrabPoint = null;
    }

    private void TriggerReaction()
    {
        if (motionController is null)
        {
            return;
        }

        var state = motionController.Snapshot.Dango.State;
        if (state is not (DangoActionState.Idle or DangoActionState.Wander or DangoActionState.OnIsland))
        {
            return;
        }

        reactionReturnState = state == DangoActionState.OnIsland
            ? DangoActionState.OnIsland
            : DangoActionState.Idle;
        motionController.StartAction(DangoActionState.Reacting, NowMilliseconds());
    }

    private void StartMotion(double surfaceWidth, double surfaceHeight)
    {
        var now = NowMilliseconds();
        sceneSession.AttachSurface(new LogicalRect(0, 0, surfaceWidth, surfaceHeight), now);

        ApplyAuthoritativePosition();
        motionRuntime.Start();
        motionTimer.Start();
    }

    private void OnMotionTimerTick(object? sender, EventArgs e)
    {
        if (!isVisible || motionController is null || !motionRuntime.IsRunning)
        {
            return;
        }

        var now = NowMilliseconds();
        keyboardInputBridge?.Refresh(
            recoveryState.State == WorkerWRecoveryState.Attached && isVisible,
            isInteractionEnabled);
        var step = TimeSpan.FromSeconds(1d / 60d);
        motionController.AdvanceDirectionalControl(
            keyboardInputBridge?.CurrentIntent ?? DirectionalControlIntent.None,
            step,
            now);
        AdvanceNonDirectionalAction(motionController, now);
        ApplyAuthoritativePosition();
        motionRuntime.Tick(
            PhysicsPresentationInput.FromSnapshot(
                motionController.Snapshot,
                motionController.SquashAmount,
                motionController.GroundFrictionCue,
                motionController.FacingDirection,
                dragGrabPoint),
            step);
        motionController.ObservePresentationFrame();
    }

    private void AdvanceNonDirectionalAction(SingleDangoMotionController controller, long now)
    {
        if (controller.IsDragging)
        {
            return;
        }

        var elapsed = now - controller.Snapshot.Dango.StateStartedAtMilliseconds;
        switch (controller.Snapshot.Dango.State)
        {
            case DangoActionState.Reacting when elapsed >= 450:
                controller.StartAction(reactionReturnState, now);
                break;
        }
    }

    private void ApplyAuthoritativePosition()
    {
        if (motionController is null)
        {
            return;
        }

        var bounds = motionController.Snapshot.Dango.VisualBounds;
        WpfCanvas.SetLeft(DangoVisual, bounds.X);
        WpfCanvas.SetTop(DangoVisual, bounds.Y);
        dangoAboveWindow.SetPosition(bounds.X, bounds.Y);
        UpdateInputProxyBounds();
    }

    private static long NowMilliseconds() => Environment.TickCount64;

    private void SetDragGrabPoint(System.Windows.Point visualPoint)
    {
        var width = DangoVisual.ActualWidth > 0 ? DangoVisual.ActualWidth : DangoVisual.Width;
        var height = DangoVisual.ActualHeight > 0 ? DangoVisual.ActualHeight : DangoVisual.Height;
        dragGrabVisualX = Math.Clamp(visualPoint.X, 0, width);
        dragGrabVisualY = Math.Clamp(visualPoint.Y, 0, height);
        dragGrabPoint = DragGrabPointMapper.FromVisualPoint(
            dragGrabVisualX,
            dragGrabVisualY,
            width,
            height);
    }

    private sealed class MirroredDangoSink(
        DeformableDangoVisual workerVisual,
        DesktopObjectOverlayWindow aboveWindow) : IPhysicsPresentationSink
    {
        public void ApplyFrame(PhysicsPresentationFrame frame)
        {
            workerVisual.ApplyFrame(frame);
            aboveWindow.ApplyDangoFrame(frame);
        }
    }

    private static DesktopHitRegion? Region(DesktopHitTarget target, FrameworkElement element) =>
        element.Visibility != Visibility.Visible
            ? null
            : DesktopHitRegionFactory.TryCreate(
                target,
                WpfCanvas.GetLeft(element),
                WpfCanvas.GetTop(element),
                element.ActualWidth,
                element.ActualHeight);
}
