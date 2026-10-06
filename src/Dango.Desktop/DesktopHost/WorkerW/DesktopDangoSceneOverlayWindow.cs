using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Brushes = System.Windows.Media.Brushes;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;

namespace Dango.Desktop.DesktopHost.WorkerW;

// One display-only surface keeps every hat above every body, including overlaps.
internal sealed class DesktopDangoSceneOverlayWindow : Window
{
    private readonly Func<Point, LogicalPoint> screenToScene;
    private readonly Func<LogicalPoint, Point> sceneToScreen;
    private readonly Func<DangoEntityId, LogicalPoint, bool> beginDrag;
    private readonly Action<LogicalPoint> moveDrag;
    private readonly Action endDrag;
    private readonly Action<LogicalPoint>? completePointer;
    private readonly DeformableDangoVisual[] bodies = new DeformableDangoVisual[3];
    private readonly DangoHatVisual[] hats = new DangoHatVisual[3];
    private StackedPhysicsPresentationFrame? frame;
    private nint handle;
    private bool interactionEnabled;
    private bool dragging;

    public DesktopDangoSceneOverlayWindow(
        Func<Point, LogicalPoint> screenToScene,
        Func<LogicalPoint, Point> sceneToScreen,
        Func<DangoEntityId, LogicalPoint, bool> beginDrag,
        Action<LogicalPoint> moveDrag,
        Action endDrag,
        Action<LogicalPoint>? completePointer = null)
    {
        this.screenToScene = screenToScene;
        this.sceneToScreen = sceneToScreen;
        this.beginDrag = beginDrag;
        this.moveDrag = moveDrag;
        this.endDrag = endDrag;
        this.completePointer = completePointer;
        Width = 160; Height = 120;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var canvas = new Canvas();
        for (var i = 0; i < 3; i++)
        {
            bodies[i] = new DeformableDangoVisual { ShowHat = false, IsHitTestVisible = false };
            canvas.Children.Add(bodies[i]);
        }
        for (var i = 0; i < 3; i++)
        {
            hats[i] = new DangoHatVisual { IsHitTestVisible = false };
            canvas.Children.Add(hats[i]);
        }
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            handle = source.Handle;
            NativeMethods.ApplyToolWindowStyle(handle);
            source.AddHook(WindowMessageHook);
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelDrag(); };
        Closed += (_, _) => CancelDrag();
        DpiChanged += (_, _) => { if (completePointer is not null) endDrag(); };
    }

    public void SetInteractionEnabled(bool value)
    {
        interactionEnabled = value;
        if (!value) CancelDrag();
    }

    public void SetDebugSkeletonVisible(bool value)
    {
        foreach (var body in bodies) body.ShowDebugGeometry = value;
    }

    public void ApplyFrame(StackedPhysicsPresentationFrame value, HatAppearance appearance = HatAppearance.Sprout, double hatSize = 1, System.Windows.Media.Brush? bodyFill = null, System.Windows.Media.Brush? eyeFill = null, DangoEntityId? appearanceEntity = null)
    {
        frame = value;
        if (value.Bodies.Count == 0) return;
        var bounds = Enumerable.Range(0, value.Bodies.Count).Select(i => DangoHostFrameBinding.Bounds(value, i)).ToArray();
        var left = bounds.Min(b => b.X);
        var top = bounds.Min(b => b.Y) - 16; // Retain room for the hat above the body control.
        var width = Math.Max(1, bounds.Max(b => b.Right) - left);
        var height = Math.Max(1, bounds.Max(b => b.Bottom) - top);
        Width = width; Height = height;
        for (var i = 0; i < 3; i++)
            DangoHostFrameBinding.Apply(bodies[i], hats[i], value, i, new(-left, -top), appearance, hatSize, bodyFill, eyeFill, appearanceEntity);
        var screen = sceneToScreen(new(left, top));
        var screenEnd = sceneToScreen(new(left + width, top + height));
        if (handle != 0)
        {
            if (!SetWindowPos(handle, 0, (int)Math.Round(screen.X), (int)Math.Round(screen.Y),
                Math.Max(1, (int)Math.Round(screenEnd.X - screen.X)),
                Math.Max(1, (int)Math.Round(screenEnd.Y - screen.Y)), 0x0014))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        else
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = screen.X / dpi.DpiScaleX;
            Top = screen.Y / dpi.DpiScaleY;
        }
    }

    public void CancelDrag()
    {
        if (!dragging) return;
        ReleasePointerCapture();
        endDrag();
    }

    public bool TryCapturePointer()
    {
        if (!interactionEnabled || !IsVisible || handle == 0) return false;
        _ = NativeMethods.SetCapture(handle);
        return dragging = GetCapture() == handle;
    }

    public void ReleasePointerCapture()
    {
        dragging = false;
        if (handle != 0 && GetCapture() == handle) _ = NativeMethods.ReleaseCapture();
    }

    private nint WindowMessageHook(nint window, int message, nint wp, nint lp, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE
        if (message == 0x0084)
        {
            handled = true;
            return TryHit(out _, out _) ? 1 : -1; // Blank area passes through.
        }
        if (message == 0x0201 && TryHit(out var id, out var point))
        {
            if (beginDrag(id, point) && completePointer is null)
            {
                _ = NativeMethods.SetCapture(window);
                dragging = GetCapture() == window;
                if (!dragging) endDrag();
            }
            handled = true;
        }
        if (message == 0x0200 && dragging && NativeMethods.GetCursorPos(out var cursor))
        {
            moveDrag(screenToScene(new(cursor.X, cursor.Y)));
            handled = true;
        }
        if (message == 0x0202 && dragging)
        {
            if (completePointer is not null && NativeMethods.GetCursorPos(out var end))
                completePointer(screenToScene(new(end.X, end.Y)));
            else CancelDrag();
            handled = true;
        }
        if (message == 0x0215 && dragging && GetCapture() != handle)
        {
            dragging = false;
            endDrag();
        }
        return 0;
    }

    private bool TryHit(out DangoEntityId id, out LogicalPoint point)
    {
        id = default; point = default;
        if (!interactionEnabled || frame is null || frame.ConstraintsReleased || !NativeMethods.GetCursorPos(out var cursor)) return false;
        point = screenToScene(new(cursor.X, cursor.Y));
        if (DangoHostFrameBinding.HitTest(frame, point) is not { } found) return false;
        id = found;
        return true;
    }

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
