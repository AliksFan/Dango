using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.DesktopHost.Overlay;
using Dango.Desktop.DesktopHost.Win32;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfColor = System.Windows.Media.Color;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.DesktopHost.WorkerW;

public partial class WorkerWWindow : WpfUserControl, IDesktopHostControl, IDesktopLayeringControl
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly OverlayInteractionController interactionController = new();
    private readonly WorkerWRecoveryStateMachine recoveryState = new(TimeSpan.FromSeconds(8), 8);
    private readonly DispatcherTimer shellTimer;
    private readonly DesktopObjectOverlayWindow dangoAboveWindow;
    private readonly DesktopObjectOverlayWindow cushionAboveWindow;
    private readonly IslandOverlayWindow islandOverlayWindow;
    private OverlayHitTestPolicy hitTestPolicy = new([]);
    private System.Windows.Point dragPointerOrigin;
    private double dragVisualLeft;
    private double dragVisualTop;
    private nint windowHandle;
    private WorkerWNativeHost? nativeHost;
    private WorkerWInputProxy? inputProxy;
    private HwndSource? windowSource;
    private WorkerWDesktopTarget? currentTarget;
    private bool isInteractionEnabled = true;
    private bool isVisible;
    private bool isClosed;
    private bool scenePositionInitialized;
    private DesktopLayeringSettings layeringSettings = DesktopLayeringSettings.CreateDefault();
    private string? lastError;

    public WorkerWWindow()
    {
        InitializeComponent();
        dangoAboveWindow = new DesktopObjectOverlayWindow(PrototypeDesktopObject.Dango, OnAboveDangoMoved);
        cushionAboveWindow = new DesktopObjectOverlayWindow(PrototypeDesktopObject.Cushion);
        islandOverlayWindow = new IslandOverlayWindow();
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
            "WorkerW",
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
        DangoVisual.IsHitTestVisible = isEnabled;
        IslandVisual.IsHitTestVisible = isEnabled;
        inputProxy?.SetEnabled(isEnabled);
        dangoAboveWindow.SetInteractionEnabled(isEnabled);
        ApplyObjectLayerVisibility();
    }

    public DesktopLayeringSettings GetLayeringSettings() => layeringSettings;

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
            lastError = "未发现兼容的 WorkerW 桌面层，已恢复透明覆盖层。";
            return;
        }

        try
        {
            CreateSurface(target!);
            currentTarget = target;
            recoveryState.AttachSucceeded(target!.ParentHandle);
            Opacity = 1;
            isVisible = true;
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
        WpfCanvas.SetLeft(
            DangoVisual,
            Math.Clamp(dragVisualLeft + pointer.X - dragPointerOrigin.X, 0, SceneCanvas.ActualWidth - DangoVisual.ActualWidth));
        WpfCanvas.SetTop(
            DangoVisual,
            Math.Clamp(dragVisualTop + pointer.Y - dragPointerOrigin.Y, 0, SceneCanvas.ActualHeight - DangoVisual.ActualHeight));
        RebuildHitTestPolicy();
    }

    private void OnDangoMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (interactionController.CompleteDrag() && DangoVisual.IsMouseCaptured)
        {
            DangoVisual.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private void OnDangoLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) =>
        interactionController.CancelDrag(DragCancellationReason.LostCapture);

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
        Dispatcher.BeginInvoke(RebuildHitTestPolicy, DispatcherPriority.Loaded);
    }

    private void DisposeSurface()
    {
        isVisible = false;
        Opacity = 0;
        inputProxy?.Dispose();
        inputProxy = null;
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
    }

    private void RebuildHitTestPolicy()
    {
        var regions = new[]
        {
            Region(OverlayHitTarget.Dango, DangoVisual),
            Region(OverlayHitTarget.Island, IslandVisual),
        }.OfType<OverlayHitRegion>();
        hitTestPolicy = new OverlayHitTestPolicy(regions);
        UpdateInputProxyBounds();
    }

    private bool BeginProxyDrag()
    {
        if (!isInteractionEnabled || !interactionController.TryBeginDrag(OverlayHitTarget.Dango))
        {
            return false;
        }

        dragVisualLeft = WpfCanvas.GetLeft(DangoVisual);
        dragVisualTop = WpfCanvas.GetTop(DangoVisual);
        return true;
    }

    private void MoveProxyDrag(double deviceDeltaX, double deviceDeltaY)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(0, SceneCanvas.ActualWidth - DangoVisual.ActualWidth);
        var height = Math.Max(0, SceneCanvas.ActualHeight - DangoVisual.ActualHeight);
        WpfCanvas.SetLeft(DangoVisual, Math.Clamp(dragVisualLeft + deviceDeltaX / dpi.DpiScaleX, 0, width));
        WpfCanvas.SetTop(DangoVisual, Math.Clamp(dragVisualTop + deviceDeltaY / dpi.DpiScaleY, 0, height));
        RebuildHitTestPolicy();
    }

    private void CompleteProxyDrag() => interactionController.CompleteDrag();

    private void CancelProxyDrag() =>
        interactionController.CancelDrag(DragCancellationReason.LostCapture);

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
        var maximumLeft = Math.Max(0, ActualWidth - DangoVisual.ActualWidth);
        var maximumTop = Math.Max(0, ActualHeight - DangoVisual.ActualHeight);
        WpfCanvas.SetLeft(DangoVisual, Math.Clamp(left, 0, maximumLeft));
        WpfCanvas.SetTop(DangoVisual, Math.Clamp(top, 0, maximumTop));
        RebuildHitTestPolicy();
    }

    private static OverlayHitRegion? Region(OverlayHitTarget target, FrameworkElement element) =>
        element.Visibility != Visibility.Visible
            ? null
            : OverlayHitRegionFactory.TryCreate(
                target,
                WpfCanvas.GetLeft(element),
                WpfCanvas.GetTop(element),
                element.ActualWidth,
                element.ActualHeight);
}
