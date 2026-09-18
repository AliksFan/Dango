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
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using CoreDpiScale = Dango.Core.Coordinates.DpiScale;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfColor = System.Windows.Media.Color;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.DesktopHost.WorkerW;

public partial class WorkerWWindow : WpfUserControl, IDesktopHostControl, IDesktopLayeringControl, IKeyboardControl, IDesktopHostSwitchGuard, IPhysicsDebugControl, IDangoPopulationControl, IDangoPhysicsTuningControl
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
    private readonly DesktopDangoSceneOverlayWindow dangoAboveWindow;
    private readonly DesktopObjectOverlayWindow cushionAboveWindow;
    private readonly IslandOverlayWindow islandOverlayWindow;
    private DesktopHitTestPolicy hitTestPolicy = new([]);
    private nint windowHandle;
    private WorkerWNativeHost? nativeHost;
    private readonly Dictionary<DangoEntityId, WorkerWInputProxy> inputProxies = [];
    private DeformableDangoVisual? capturedDango;
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
    private readonly StackedDangoSceneRuntime motionRuntime;
    private readonly LightInteractionSession lightInteraction;
    private readonly DesktopSceneSession sceneSession;
    private SingleDangoMotionController? motionController => sceneSession.Controller;
    private DangoActionState reactionReturnState = DangoActionState.Idle;
    private LogicalPoint proxyDragPointer;
    private bool proxyDragMoved;

    public WorkerWWindow(
        DesktopSceneSession sceneSession,
        StackedDangoSceneRuntime motionRuntime)
    {
        this.sceneSession = sceneSession ?? throw new ArgumentNullException(nameof(sceneSession));
        this.motionRuntime = motionRuntime ?? throw new ArgumentNullException(nameof(motionRuntime));
        lightInteraction = new(motionRuntime);
        InitializeComponent();
        DangoVisual.ShowHat = false;
        MiddleDangoVisual.ShowHat = false;
        TopDangoVisual.ShowHat = false;
        MiddleDangoVisual.BodyFill = new SolidColorBrush(WpfColor.FromRgb(201, 226, 255));
        TopDangoVisual.BodyFill = new SolidColorBrush(WpfColor.FromRgb(221, 204, 248));
        dangoAboveWindow = new DesktopDangoSceneOverlayWindow(
            point => { var local = PointFromScreen(point); return new LogicalPoint(local.X, local.Y); },
            point => PointToScreen(new System.Windows.Point(point.X, point.Y)),
            BeginOverlayPointer,
            MoveOverlayPointer,
            motionRuntime.LightInteractionsEnabled ? CancelProxyDrag : CompleteProxyDrag,
            motionRuntime.LightInteractionsEnabled ? EndLightPointer : null);

        cushionAboveWindow = new DesktopObjectOverlayWindow(PrototypeDesktopObject.Cushion);
        islandOverlayWindow = new IslandOverlayWindow();
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
        motionRuntime.InputCancelled += ClearHostInput;
    }

    public int DangoCount => motionRuntime.DangoCount;

    public bool AddDango() => motionRuntime.AddDangoInViewport(
        new(0, 0, Math.Max(480, ActualWidth), Math.Max(360, ActualHeight)));

    public bool RemoveDango() => motionRuntime.RemoveDango();

    public DangoSelfPhysicsParameters PhysicsParameters => motionRuntime.Parameters;

    public void ApplyPhysicsParameters(DangoSelfPhysicsParameters parameters) =>
        motionRuntime.ApplyPhysicsParameters(parameters);

    public void RestoreDefaultPhysicsParameters() => motionRuntime.RestoreDefaultPhysicsParameters();

    public void RegenerateDango() => motionRuntime.RegenerateDango();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionScene();
        RebuildHitTestPolicy();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled) motionRuntime.CancelInput();
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
        if (message == 0x02E0 && motionRuntime.LightInteractionsEnabled) motionRuntime.CancelInput();
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
            motionRuntime.LayoutWarning ?? (recoveryState.State == WorkerWRecoveryState.Attached ? "宿主已验收；新物理接入待人工验收" : StateText()),
            motionRuntime.LastError ?? lastError);

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
        MiddleDangoVisual.IsHitTestVisible = isEnabled;
        TopDangoVisual.IsHitTestVisible = isEnabled;
        IslandVisual.IsHitTestVisible = isEnabled;
        foreach (var proxy in inputProxies.Values) proxy.SetEnabled(isEnabled);
        dangoAboveWindow.SetInteractionEnabled(isEnabled);
        ApplyObjectLayerVisibility();
    }

    public DesktopLayeringSettings GetLayeringSettings() => layeringSettings;

    public KeyboardControlSettings GetKeyboardControlSettings() => keyboardControlSettings;

    public string? GetSwitchBlockReason()
    {
        if (motionRuntime.IsDragging || interactionController.IsDragging || motionController?.IsDragging == true)
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
        CancelDrag(DragCancellationReason.WindowDeactivated);
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
            motionRuntime.CreateDiagnosticSummary(),
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
            motionRuntime.Stop();
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
        if (sender is not DeformableDangoVisual { Tag: DangoEntityId id } visual) return;
        var pointer = e.GetPosition(SceneCanvas);
        if (motionRuntime.LightInteractionsEnabled)
        {
            if (!CanContinueLightPointer()) return;
            e.Handled = lightInteraction.TryBegin(id, new(pointer.X, pointer.Y), NowMilliseconds(), ShiftPressed,
                () => { capturedDango = visual; return visual.CaptureMouse(); },
                () =>
                {
                    if (capturedDango == visual) capturedDango = null;
                    if (visual.IsMouseCaptured) visual.ReleaseMouseCapture();
                }, CanContinueLightPointer);
            return;
        }
        if (!TryBeginPhysicalDrag(id, new(pointer.X, pointer.Y))) return;
        capturedDango = visual;
        if (!visual.CaptureMouse()) CancelDrag(DragCancellationReason.LostCapture);
        e.Handled = true;
    }

    private void OnDangoMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            if (!lightInteraction.IsActive) return;
            var point = e.GetPosition(SceneCanvas);
            if (e.LeftButton != MouseButtonState.Pressed) lightInteraction.Cancel();
            else lightInteraction.Move(new(point.X, point.Y), NowMilliseconds(), ShiftPressed);
            e.Handled = true;
            return;
        }
        if (!motionRuntime.IsDragging || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = e.GetPosition(SceneCanvas);
        motionRuntime.MovePhysicalDrag(new(pointer.X, pointer.Y));
        e.Handled = true;
    }

    private void OnDangoMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            var point = e.GetPosition(SceneCanvas);
            EndLightPointer(new(point.X, point.Y));
            e.Handled = true;
            return;
        }
        CompleteProxyDrag();
        var captured = capturedDango;
        capturedDango = null;
        if (captured?.IsMouseCaptured == true) captured.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnDangoLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            if (lightInteraction.IsActive && sender is DeformableDangoVisual visual &&
                capturedDango == visual && !visual.IsMouseCaptured) lightInteraction.Cancel();
            return;
        }
        CancelDrag(DragCancellationReason.LostCapture);
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
        motionRuntime.Stop();
        DisposeSurface();
        currentTarget = null;
        recoveryState.Stop();
        motionRuntime.InputCancelled -= ClearHostInput;
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


        keyboardInputBridge = new WorkerWKeyboardInputBridge(target);
        keyboardInputBridge.ApplySettings(keyboardControlSettings);
        Dispatcher.BeginInvoke(RebuildHitTestPolicy, DispatcherPriority.Loaded);
    }

    private void DisposeSurface()
    {
        isVisible = false;
        Opacity = 0;
        motionTimer.Stop();
        motionRuntime.Stop();
        foreach (var proxy in inputProxies.Values.ToArray()) proxy.Dispose();
        inputProxies.Clear();
        HideLayeredObjectWindows();
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
        motionRuntime.CancelInput();
    }

    private void ClearHostInput()
    {
        lightInteraction.Cancel();
        keyboardInputBridge?.Clear();
        interactionController.CancelDrag(DragCancellationReason.LostCapture);
        var captured = capturedDango;
        capturedDango = null;
        if (captured?.IsMouseCaptured == true) captured.ReleaseMouseCapture();
        foreach (var proxy in inputProxies.Values.ToArray()) proxy.CancelDrag();
        dangoAboveWindow.CancelDrag();
    }

    private void RebuildHitTestPolicy()
    {
        var regions = new[]
        {
            Region(DesktopHitTarget.Dango, DangoVisual),
            Region(DesktopHitTarget.Dango, MiddleDangoVisual),
            Region(DesktopHitTarget.Dango, TopDangoVisual),
            Region(DesktopHitTarget.Island, IslandVisual),
        }.OfType<DesktopHitRegion>();
        hitTestPolicy = new DesktopHitTestPolicy(regions);
        UpdateInputProxyBounds();
    }

    private bool TryBeginPhysicalDrag(DangoEntityId id, LogicalPoint pointer)
    {
        if (!isVisible || !isInteractionEnabled || motionRuntime.LastError is not null ||
            motionRuntime.IsDragging || !motionRuntime.EntityIds.Contains(id)) return false;
        motionRuntime.BeginPhysicalDrag(id, pointer);
        if (interactionController.TryBeginDrag(DesktopHitTarget.Dango)) return true;
        motionRuntime.EndPhysicalDrag();
        return false;
    }

    private bool BeginProxyDrag(DangoEntityId proxyId, double deviceLocalX, double deviceLocalY)
    {
        var frame = motionRuntime.LastFrame;
        if (frame is null) return false;
        var index = frame.EntityIds.ToList().IndexOf(proxyId);
        if (index < 0) return false;
        var bounds = DangoHostFrameBinding.Bounds(frame, index);
        var dpi = VisualTreeHelper.GetDpi(this);
        proxyDragPointer = WorkerWSurfaceCoordinateMapper.OffsetFromDevice(new(bounds.X, bounds.Y),
            deviceLocalX, deviceLocalY, new(dpi.DpiScaleX, dpi.DpiScaleY));
        // A proxy can overlap another body. Resolve the final target in the same
        // back-to-front order as the display, independent of native window order.
        if (DangoHostFrameBinding.HitTest(frame, proxyDragPointer) is not { } id) return false;
        if (motionRuntime.LightInteractionsEnabled)
        {
            if (!CanContinueLightPointer() || !inputProxies.TryGetValue(proxyId, out var proxy)) return false;
            return lightInteraction.TryBegin(id, proxyDragPointer, NowMilliseconds(), ShiftPressed,
                proxy.TryCapturePointer, proxy.ReleasePointerCapture, CanContinueLightPointer);
        }
        if (!TryBeginPhysicalDrag(id, proxyDragPointer)) return false;
        proxyDragMoved = false;
        return true;
    }

    private void MoveProxyDrag(double deviceDeltaX, double deviceDeltaY)
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            lightInteraction.Move(ProxyPoint(deviceDeltaX, deviceDeltaY), NowMilliseconds(), ShiftPressed);
            return;
        }
        if (!motionRuntime.IsDragging) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var deltaX = deviceDeltaX / dpi.DpiScaleX;
        var deltaY = deviceDeltaY / dpi.DpiScaleY;
        if (!proxyDragMoved && Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < 3) return;
        proxyDragMoved = true;
        motionRuntime.MovePhysicalDrag(new(proxyDragPointer.X + deltaX, proxyDragPointer.Y + deltaY));
    }

    private void CompleteProxyDrag()
    {
        interactionController.CompleteDrag();
        motionRuntime.EndPhysicalDrag();
    }

    private void CancelProxyDrag()
    {
        if (motionRuntime.LightInteractionsEnabled)
        {
            motionRuntime.CancelInput();
            return;
        }
        interactionController.CancelDrag(DragCancellationReason.LostCapture);
        motionRuntime.EndPhysicalDrag();
    }

    private void UpdateInputProxyBounds()
    {
        var frame = motionRuntime.LastFrame;
        if (!isVisible || currentTarget is null || frame is null) return;
        foreach (var removed in inputProxies.Keys.Where(id => !frame.EntityIds.Contains(id)).ToArray())
        {
            inputProxies[removed].Dispose();
            inputProxies.Remove(removed);
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        for (var i = 0; i < frame.EntityIds.Count; i++)
        {
            var id = frame.EntityIds[i];
            if (!inputProxies.TryGetValue(id, out var proxy))
            {
                proxy = new WorkerWInputProxy(currentTarget,
                    (x, y) => BeginProxyDrag(id, x, y), MoveProxyDrag, CompleteProxyDrag, CancelProxyDrag,
                    motionRuntime.LightInteractionsEnabled ? (x, y) => EndLightPointer(ProxyPoint(x, y)) : null);
                inputProxies.Add(id, proxy);
            }
            var bounds = DangoHostFrameBinding.Bounds(frame, i);
            proxy.SetBounds((int)Math.Round(bounds.X * dpi.DpiScaleX), (int)Math.Round(bounds.Y * dpi.DpiScaleY),
                (int)Math.Round(bounds.Width * dpi.DpiScaleX), (int)Math.Round(bounds.Height * dpi.DpiScaleY));
            proxy.SetEnabled(isInteractionEnabled && motionRuntime.LastError is null &&
                layeringSettings.DangoLayer == DesktopObjectLayer.BelowDesktopIcons);
        }
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
        var bodyVisuals = new[] { DangoVisual, MiddleDangoVisual, TopDangoVisual };
        var hatVisuals = new[] { DangoHat, MiddleDangoHat, TopDangoHat };
        var frame = motionRuntime.LastFrame;
        for (var i = 0; i < 3; i++)
        {
            var show = !dangoAbove && i < (frame?.Bodies.Count ?? 1);
            bodyVisuals[i].Visibility = hatVisuals[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        CushionVisual.Visibility = cushionAbove ? Visibility.Collapsed : Visibility.Visible;
        IslandVisual.Visibility = Visibility.Collapsed;
        dangoAboveWindow.SetInteractionEnabled(isInteractionEnabled && motionRuntime.LastError is null);
        if (frame is not null) dangoAboveWindow.ApplyFrame(frame, motionRuntime.DisplayHatAppearance, motionRuntime.DisplayHatSize);
        SetWindowVisibility(dangoAboveWindow, dangoAbove && frame is not null);
        // Apply physical window coordinates after SourceInitialized on first Show.
        if (dangoAboveWindow.IsVisible && frame is not null) dangoAboveWindow.ApplyFrame(frame, motionRuntime.DisplayHatAppearance, motionRuntime.DisplayHatSize);
        cushionAboveWindow.SetPosition(WpfCanvas.GetLeft(CushionVisual), WpfCanvas.GetTop(CushionVisual));
        SetWindowVisibility(cushionAboveWindow, cushionAbove);
        islandOverlayWindow.SetDesiredVisibility(true);
        foreach (var proxy in inputProxies.Values)
            proxy.SetEnabled(isInteractionEnabled && !dangoAbove && motionRuntime.LastError is null);
    }

    private void HideLayeredObjectWindows()
    {
        dangoAboveWindow.Hide();
        cushionAboveWindow.Hide();
        islandOverlayWindow.SetDesiredVisibility(false);
        foreach (var proxy in inputProxies.Values) proxy.SetEnabled(false);
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
        motionRuntime.SetViewport(new(0, 0, surfaceWidth, surfaceHeight));
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
        if ((motionRuntime.IsDragging || lightInteraction.IsActive || motionRuntime.HasLightFeedback) && (NativeMethods.IsEscapePressed() ||
            currentTarget is not null && !NativeMethods.IsExplorerDesktopForeground(currentTarget)))
        {
            CancelDrag(DragCancellationReason.WindowDeactivated);
        }
        if (lightInteraction.IsActive) lightInteraction.RefreshButtons(now, ShiftPressed, leftDown: true);
        var step = TimeSpan.FromSeconds(1d / 60d);
        motionRuntime.SetDirectionalControl(
            keyboardInputBridge?.CurrentIntent ?? DirectionalControlIntent.None);
        AdvanceNonDirectionalAction(motionController, now);
        ApplyAuthoritativePosition();
        var frame = motionRuntime.Tick(
            PhysicsPresentationInput.FromSnapshot(
                motionController.Snapshot,
                motionController.SquashAmount,
                motionController.GroundFrictionCue,
                motionController.FacingDirection,
                null),
            step);
        ApplyPresentationFrame(frame);
        RebuildHitTestPolicy();
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
        UpdateInputProxyBounds();
    }

    public void SetDebugSkeletonVisible(bool isVisible)
    {
        DangoVisual.ShowDebugGeometry = isVisible;
        MiddleDangoVisual.ShowDebugGeometry = isVisible;
        TopDangoVisual.ShowDebugGeometry = isVisible;
        dangoAboveWindow.SetDebugSkeletonVisible(isVisible);
    }

    public bool IsDebugSkeletonVisible => DangoVisual.ShowDebugGeometry;

    private void ApplyPresentationFrame(StackedPhysicsPresentationFrame frame)
    {
        DangoHostFrameBinding.Apply(DangoVisual, DangoHat, frame, 0, appearance: motionRuntime.DisplayHatAppearance, hatSize: motionRuntime.DisplayHatSize);
        DangoHostFrameBinding.Apply(MiddleDangoVisual, MiddleDangoHat, frame, 1, appearance: motionRuntime.DisplayHatAppearance, hatSize: motionRuntime.DisplayHatSize);
        DangoHostFrameBinding.Apply(TopDangoVisual, TopDangoHat, frame, 2, appearance: motionRuntime.DisplayHatAppearance, hatSize: motionRuntime.DisplayHatSize);
        ApplyObjectLayerVisibility();
    }


    private static long NowMilliseconds() => Environment.TickCount64;

    private static bool ShiftPressed => NativeMethods.IsInputKeyPressed(0x10);

    private bool LightPointerHostReady() => isVisible && isInteractionEnabled && !isClosed &&
        motionRuntime.IsRunning && motionRuntime.LastError is null && currentTarget is not null &&
        NativeMethods.IsExplorerDesktopForeground(currentTarget) && !NativeMethods.IsEscapePressed();

    private bool CanContinueLightPointer() => LightPointerHostReady() && NativeMethods.IsInputKeyPressed(0x01);

    private bool BeginOverlayPointer(DangoEntityId id, LogicalPoint point)
    {
        if (!motionRuntime.LightInteractionsEnabled) return TryBeginPhysicalDrag(id, point);
        return CanContinueLightPointer() && lightInteraction.TryBegin(id, point, NowMilliseconds(), ShiftPressed,
            dangoAboveWindow.TryCapturePointer, dangoAboveWindow.ReleasePointerCapture, CanContinueLightPointer);
    }

    private void MoveOverlayPointer(LogicalPoint point)
    {
        if (motionRuntime.LightInteractionsEnabled) lightInteraction.Move(point, NowMilliseconds(), ShiftPressed);
        else if (motionRuntime.IsDragging) motionRuntime.MovePhysicalDrag(point);
    }

    private void EndLightPointer(LogicalPoint point)
    {
        if (!LightPointerHostReady()) lightInteraction.Cancel();
        else lightInteraction.End(point, NowMilliseconds(), ShiftPressed);
    }

    private LogicalPoint ProxyPoint(double x, double y)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return WorkerWSurfaceCoordinateMapper.OffsetFromDevice(proxyDragPointer, x, y, new(dpi.DpiScaleX, dpi.DpiScaleY));
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
