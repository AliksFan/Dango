using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.DesktopHost.Overlay;

public partial class OverlayWindow : Window
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private readonly OverlayInteractionController interactionController = new();
    private OverlayHitTestPolicy hitTestPolicy = new([]);
    private OverlayTrayIcon? trayIcon;
    private System.Windows.Point dragPointerOrigin;
    private double dragVisualLeft;
    private double dragVisualTop;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        Closing += OnClosing;
        Closed += OnClosed;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        NativeMethods.ApplyToolWindowStyle(source.Handle);
        source.AddHook(WindowMessageHook);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 24;
        RebuildHitTestPolicy();
        trayIcon ??= new OverlayTrayIcon(this, interactionController, Close);
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message != WmMouseActivate)
        {
            return 0;
        }

        handled = true;
        return MaNoActivate;
    }

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
        var left = Math.Clamp(
            dragVisualLeft + pointer.X - dragPointerOrigin.X,
            0,
            SceneCanvas.ActualWidth - DangoVisual.ActualWidth);
        var top = Math.Clamp(
            dragVisualTop + pointer.Y - dragPointerOrigin.Y,
            0,
            SceneCanvas.ActualHeight - DangoVisual.ActualHeight);
        WpfCanvas.SetLeft(DangoVisual, left);
        WpfCanvas.SetTop(DangoVisual, top);
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
        if (e.Key != Key.Escape)
        {
            return;
        }

        CancelDrag(DragCancellationReason.Escape);
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e) =>
        CancelDrag(DragCancellationReason.WindowDeactivated);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CancelDrag(DragCancellationReason.WindowClosing);
        trayIcon?.Dispose();
        trayIcon = null;
    }

    private void OnClosed(object? sender, EventArgs e) => System.Windows.Application.Current.Shutdown();

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
