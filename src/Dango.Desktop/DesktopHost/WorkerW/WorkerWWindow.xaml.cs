using System.ComponentModel;
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

namespace Dango.Desktop.DesktopHost.WorkerW;

public partial class WorkerWWindow : Window, IDesktopHostControl
{
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;
    private readonly OverlayInteractionController interactionController = new();
    private readonly WorkerWRecoveryStateMachine recoveryState = new(TimeSpan.FromSeconds(8), 8);
    private readonly DispatcherTimer shellTimer;
    private OverlayHitTestPolicy hitTestPolicy = new([]);
    private System.Windows.Point dragPointerOrigin;
    private double dragVisualLeft;
    private double dragVisualTop;
    private nint windowHandle;
    private bool isInteractionEnabled = true;
    private string? lastError;

    public WorkerWWindow()
    {
        InitializeComponent();
        shellTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        shellTimer.Tick += OnShellTimerTick;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        windowHandle = source.Handle;
        NativeMethods.ApplyToolWindowStyle(windowHandle);
        source.AddHook(WindowMessageHook);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildHitTestPolicy();
        BeginAttachment();
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
            IsVisible && recoveryState.State == WorkerWRecoveryState.Attached,
            isInteractionEnabled,
            recoveryState.State == WorkerWRecoveryState.Attached ? "待人工验收" : StateText(),
            lastError);

    public void SetVisibility(bool isVisible)
    {
        if (!isVisible)
        {
            CancelDrag(DragCancellationReason.WindowDeactivated);
            shellTimer.Stop();
            Opacity = 0;
            NativeMethods.DetachFromWorkerW(windowHandle);
            recoveryState.EnterSafeHidden();
            Hide();
            return;
        }

        Opacity = 0;
        Show();
        BeginAttachment();
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
            $"恢复尝试：{recoveryState.RecoveryAttempts}",
            $"窗口逻辑尺寸：{ActualWidth:F0} × {ActualHeight:F0}",
            $"DPI 比例：{dpi.DpiScaleX:F2} × {dpi.DpiScaleY:F2}",
            $"桌面显示：{GetSnapshot().IsVisible}",
            $"允许交互：{isInteractionEnabled}",
            $"拖动中：{interactionController.IsDragging}",
            $"最近错误：{lastError ?? "无"}");
    }

    private void BeginAttachment()
    {
        if (!IsLoaded || windowHandle == nint.Zero)
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

        var workerW = NativeMethods.FindWorkerW();
        if (!NativeMethods.IsValidWindow(workerW))
        {
            lastError = "尚未发现有效 WorkerW，正在有界重试。";
            return;
        }

        try
        {
            NativeMethods.AttachToWorkerW(windowHandle, workerW);
            recoveryState.AttachSucceeded(workerW);
            Opacity = 1;
            lastError = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            lastError = $"WorkerW 挂载失败：{exception.GetType().Name}。";
        }
    }

    private void OnShellTimerTick(object? sender, EventArgs e)
    {
        if (recoveryState.State == WorkerWRecoveryState.Attached &&
            !NativeMethods.IsValidWindow(recoveryState.ParentHandle))
        {
            Opacity = 0;
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
        Opacity = 0;
        NativeMethods.DetachFromWorkerW(windowHandle);
        recoveryState.EnterSafeHidden();
        lastError = error;
        Hide();
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

        Activate();
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

    private void OnDeactivated(object? sender, EventArgs e) =>
        CancelDrag(DragCancellationReason.WindowDeactivated);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        shellTimer.Stop();
        CancelDrag(DragCancellationReason.WindowClosing);
        NativeMethods.DetachFromWorkerW(windowHandle);
        recoveryState.Stop();
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
        hitTestPolicy = new OverlayHitTestPolicy(
        [
            Region(OverlayHitTarget.Dango, DangoVisual),
            Region(OverlayHitTarget.Island, IslandVisual),
        ]);
    }

    private static OverlayHitRegion Region(OverlayHitTarget target, FrameworkElement element) =>
        new(
            target,
            new LogicalRect(
                WpfCanvas.GetLeft(element),
                WpfCanvas.GetTop(element),
                element.ActualWidth,
                element.ActualHeight));
}
