using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;
using DpiScale = System.Windows.DpiScale;

namespace Dango.Desktop.AppShell.Rooms;

internal readonly record struct DesktopPetWindowApplyTiming
{
    internal double TotalMilliseconds { get; init; }
    internal double BindingMilliseconds { get; init; }
    internal double FirstLayoutAndBoundsMilliseconds { get; init; }
    internal double PositionAndSizeMilliseconds { get; init; }
    internal double SecondLayoutMilliseconds { get; init; }
    internal double NativeRegionMilliseconds { get; init; }
    internal double QualificationMilliseconds { get; init; }
    internal bool Succeeded { get; init; }
}

// Display and native capture only. The room driver remains the only physics owner.
internal sealed class DesktopPetDangoWindow : Window
{
    private readonly LocalRoomSceneView scene;
    private readonly Func<Point, Point> screenPixelsToScene;
    private readonly Func<Point, Point> sceneToScreenPixels;
    private readonly Rect? stableViewportPixels;
    private readonly Canvas canvas = new() { IsHitTestVisible = false };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, IsHitTestVisible = false };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false };
    private readonly DesktopPetPointerGestureBridge gesture;
    private LocalRoomSceneDriver? driver;
    private Point pixelOrigin;
    private bool captured;
    private bool detailedApplyTimingEnabled;
    private bool stableViewportPositioned;
    internal nint Handle { get; private set; }
    internal bool InteractionEnabled { get; private set; }
    internal bool IsPointerCaptured => captured && NativeMethods.DesktopPetCapturedWindow == Handle;
    internal bool IsGestureListening => gesture.IsListening;
    internal bool IsHookReleasePending => gesture.IsHookReleasePending;
    internal bool HasHealthyFrame { get; private set; }
    internal string LastFrameRejection { get; private set; } = string.Empty;
    internal int LastSurfaceInvalidationMessage { get; private set; }
    internal StackedPhysicsPresentationFrame? LastAppliedFrame { get; private set; }
    internal DesktopPetPointerGestureGuard? CurrentPointerGuard { get; private set; }
    internal DesktopPetWindowApplyTiming? LastApplyTiming { get; private set; }
    internal bool DetailedApplyTimingEnabled
    {
        get => detailedApplyTimingEnabled;
        set { detailedApplyTimingEnabled = value; LastApplyTiming = null; }
    }
    internal event Action<Point>? ContextRequested;
    internal event Action? SurfaceInvalidated;

    internal DesktopPetDangoWindow(LocalRoomSceneView scene, Func<Point, Point> screenPixelsToScene,
        Func<Point, Point> sceneToScreenPixels, IDesktopPetPointerGesturePlatform? gesturePlatform = null,
        Rect? stableViewportPixels = null)
    {
        this.scene = scene;
        this.screenPixelsToScene = screenPixelsToScene;
        this.sceneToScreenPixels = sceneToScreenPixels;
        if (stableViewportPixels is { } viewport && (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0 ||
            !double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) ||
            !double.IsFinite(viewport.Right) || !double.IsFinite(viewport.Bottom)))
            throw new ArgumentException("独立团子层固定显示范围不可用。", nameof(stableViewportPixels));
        this.stableViewportPixels = stableViewportPixels;
        gesture = new(Dispatcher, () => InteractionEnabled && HasHealthyFrame && IsVisible && IsPointerCaptured,
            (screen, shift) => { _ = driver?.PointerMove(screenPixelsToScene(screen), shift); },
            (screen, shift) =>
            {
                try { _ = driver?.PointerUp(screenPixelsToScene(screen), shift); }
                finally { ReleasePointerCapture(); }
            }, CancelPointer, gesturePlatform);
        Width = Height = 1;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        canvas.Children.Add(body);
        canvas.Children.Add(hat);
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            Handle = source.Handle;
            NativeMethods.ApplyDesktopPetWindowStyles(Handle);
            NativeMethods.SetDesktopPetRegion(Handle, []);
            source.AddHook(MessageHook);
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { HasHealthyFrame = false; CurrentPointerGuard = null; CancelPointer(); } };
        Closed += (_, _) => { CancelPointer(); gesture.Dispose(); Handle = 0; HasHealthyFrame = false; CurrentPointerGuard = null; };
    }

    internal void Bind(LocalRoomSceneDriver value) => driver = value;
    internal bool HitTestScenePoint(Point point) => HasHealthyFrame && scene.IsPetHit(point);

    // Called by the room host immediately after Show, inside its preparation
    // failure boundary. Motion subsequently changes only our canvas and region.
    internal void PositionStableViewport()
    {
        if (stableViewportPixels is not { } viewport || stableViewportPositioned) return;
        var left = (int)Math.Floor(viewport.Left);
        var top = (int)Math.Floor(viewport.Top);
        NativeMethods.PositionDesktopPetWindow(Handle, left, top,
            (int)Math.Ceiling(viewport.Right) - left, (int)Math.Ceiling(viewport.Bottom) - top);
        pixelOrigin = new(left, top);
        stableViewportPositioned = true;
    }

    internal bool ApplyFrame(StackedPhysicsPresentationFrame frame, StackedDangoSceneRuntime runtime)
    {
        var observe = detailedApplyTimingEnabled;
        var started = observe ? Stopwatch.GetTimestamp() : 0;
        var stage = started;
        var timing = default(DesktopPetWindowApplyTiming);
        try
        {
            HasHealthyFrame = false;
            CurrentPointerGuard = null;
            LastFrameRejection = string.Empty;
            if (Handle == 0 || !IsVisible) { LastFrameRejection = "pet window missing or hidden"; return false; }
            if (!ReferenceEquals(scene.LastAppliedFrame, frame)) { LastFrameRejection = "scene frame changed"; return false; }
            if (frame.Bodies.Count != 1 || frame.ConstraintsReleased) { LastFrameRejection = "invalid physical frame"; return false; }
            DangoHostFrameBinding.Apply(body, hat, frame, 0, new(0, runtime.NeutralBodyGroundOffset),
                runtime.DisplayHatAppearance, runtime.DisplayHatSize, runtime.BodyPreviewFill, runtime.EyePreviewFill, runtime.AppearanceEntity);
            if (body.LastContourFailure != DangoContourFailure.None)
            { LastFrameRejection = $"contour rejected: {body.LastContourFailure}"; return false; }
            if (observe)
            {
                var finished = Stopwatch.GetTimestamp();
                timing = timing with { BindingMilliseconds = Stopwatch.GetElapsedTime(stage, finished).TotalMilliseconds };
                stage = finished;
            }
            // Resolve current visual transforms before measuring their painted world bounds.
            // A healthy drag frame can have no native hat pose; DangoHatVisual then uses
            // its established frame.Hat fallback. The actual paths remain the authority.
            canvas.Measure(new(Math.Max(1, Width), Math.Max(1, Height)));
            canvas.Arrange(new(0, 0, Math.Max(1, Width), Math.Max(1, Height)));
            UpdateLayout();
            var paths = PaintedPaths(canvas).ToArray();
            var painted = Rect.Empty;
            foreach (var path in paths)
            {
                var geometry = path.Data ?? throw new InvalidOperationException("当前团子绘制几何已失效。");
                painted.Union(path.TransformToAncestor(canvas).TransformBounds(geometry.Bounds));
            }
            if (painted.IsEmpty || painted.Width <= 0 || painted.Height <= 0 ||
                !double.IsFinite(painted.X) || !double.IsFinite(painted.Y) || !double.IsFinite(painted.Width) || !double.IsFinite(painted.Height))
            { LastFrameRejection = "painted path bounds unavailable"; return false; }
            scene.SetDesktopPetPaintedWorldBounds(frame, new(painted.X - 2, painted.Y - 2, painted.Width + 4, painted.Height + 4));
            if (scene.PetScreenBounds is not { } bounds) { LastFrameRejection = "pet screen bounds unavailable"; return false; }
            if (observe)
            {
                var finished = Stopwatch.GetTimestamp();
                timing = timing with { FirstLayoutAndBoundsMilliseconds = Stopwatch.GetElapsedTime(stage, finished).TotalMilliseconds };
                stage = finished;
            }
            if (stableViewportPixels is not null)
            {
                if (!stableViewportPositioned) { LastFrameRejection = "fixed overlay viewport not positioned"; return false; }
            }
            else
            {
                var first = sceneToScreenPixels(new(bounds.X - 4, bounds.Y - 4));
                var last = sceneToScreenPixels(new(bounds.Right + 4, bounds.Bottom + 4));
                var left = (int)Math.Floor(first.X);
                var top = (int)Math.Floor(first.Y);
                var width = Math.Max(1, (int)Math.Ceiling(last.X) - left);
                var height = Math.Max(1, (int)Math.Ceiling(last.Y) - top);
                pixelOrigin = new(left, top);
                NativeMethods.PositionDesktopPetWindow(Handle, left, top, width, height);
            }
            var dpi = VisualTreeHelper.GetDpi(this);
            // WPF Window.WmSizeChanged synchronizes Width/Height from the native
            // size while suppressing its property-to-HWND callbacks. Setting them
            // here again can issue additional native resizes during one frame.
            var p = scene.CurrentProjection;
            var zero = sceneToScreenPixels(new(p.OffsetX, p.OffsetY));
            var unit = sceneToScreenPixels(new(p.OffsetX + p.Scale, p.OffsetY + p.Scale));
            canvas.RenderTransform = new MatrixTransform((unit.X - zero.X) / dpi.DpiScaleX, 0, 0,
                (unit.Y - zero.Y) / dpi.DpiScaleY, (zero.X - pixelOrigin.X) / dpi.DpiScaleX, (zero.Y - pixelOrigin.Y) / dpi.DpiScaleY);
            if (observe)
            {
                var finished = Stopwatch.GetTimestamp();
                timing = timing with { PositionAndSizeMilliseconds = Stopwatch.GetElapsedTime(stage, finished).TotalMilliseconds };
                stage = finished;
            }
            canvas.Measure(new(Width, Height));
            canvas.Arrange(new(0, 0, Width, Height));
            UpdateLayout();
            if (observe)
            {
                var finished = Stopwatch.GetTimestamp();
                timing = timing with { SecondLayoutMilliseconds = Stopwatch.GetElapsedTime(stage, finished).TotalMilliseconds };
                stage = finished;
            }
            UpdateNativeRegion(dpi, paths);
            if (observe)
            {
                var finished = Stopwatch.GetTimestamp();
                timing = timing with { NativeRegionMilliseconds = Stopwatch.GetElapsedTime(stage, finished).TotalMilliseconds };
                stage = finished;
            }
            if (driver is null || !driver.TryCaptureDesktopPetPointerClamp(out _))
            { LastFrameRejection = "pointer qualification clamp unavailable"; return false; }
            var offset = new LogicalVector(scene.PresentationOffset.X,
                scene.PresentationOffset.Y + runtime.NeutralBodyGroundOffset);
            Rect ScreenRectangle(LogicalRect world)
            {
                world = world.Translate(offset);
                var a = scene.RoomToScene(new(world.X, world.Y));
                var b = scene.RoomToScene(new(world.Right, world.Bottom));
                return new(sceneToScreenPixels(new(a.X, a.Y)), sceneToScreenPixels(new(b.X, b.Y)));
            }
            var guard = new DesktopPetPointerGestureGuard(ScreenRectangle(DangoHostFrameBinding.Bounds(frame, 0)),
                Math.Abs(unit.Y - zero.Y) * 8);
            if (!guard.IsValid) { LastFrameRejection = "pointer qualification bounds unavailable"; return false; }
            CurrentPointerGuard = guard;
            gesture.UpdateGuard(guard);
            canvas.InvalidateVisual();
            InvalidateVisual();
            LastAppliedFrame = frame;
            HasHealthyFrame = true;
            if (observe)
                timing = timing with { QualificationMilliseconds = Stopwatch.GetElapsedTime(stage).TotalMilliseconds };
            return true;
        }
        finally
        {
            if (observe)
                LastApplyTiming = timing with
                {
                    TotalMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    Succeeded = HasHealthyFrame,
                };
        }
    }

    private static IEnumerable<Path> PaintedPaths(DependencyObject visual)
    {
        if (visual is Path { IsVisible: true, Fill: not null, Data: not null } path) yield return path;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            foreach (var child in PaintedPaths(VisualTreeHelper.GetChild(visual, i))) yield return child;
    }

    private void UpdateNativeRegion(DpiScale dpi, IReadOnlyList<Path> paths)
    {
        var polygons = new List<NativeMethods.NativePoint[]>();
        foreach (var path in paths)
        {
            var geometry = path.Data ?? throw new InvalidOperationException("当前团子透明轮廓已失效。");
            var flattened = geometry.GetFlattenedPathGeometry(.15, ToleranceType.Absolute);
            var geometryTransform = flattened.Transform ?? System.Windows.Media.Transform.Identity;
            var transform = path.TransformToAncestor(this);
            foreach (var figure in flattened.Figures)
            {
                var points = new List<Point> { figure.StartPoint };
                foreach (var segment in figure.Segments)
                {
                    if (segment is PolyLineSegment line) points.AddRange(line.Points);
                    else if (segment is LineSegment single) points.Add(single.Point);
                }
                polygons.Add(points.Select(point =>
                {
                    var transformed = transform.Transform(geometryTransform.Transform(point));
                    return new NativeMethods.NativePoint
                    {
                        X = (int)Math.Round(transformed.X * dpi.DpiScaleX),
                        Y = (int)Math.Round(transformed.Y * dpi.DpiScaleY),
                    };
                }).ToArray());
            }
        }
        if (!polygons.Any(polygon => polygon.Length >= 3))
            throw new InvalidOperationException("独立团子层没有可呈现的健康轮廓。");
        NativeMethods.SetDesktopPetRegion(Handle, polygons);
    }

    internal void SetInteractionEnabled(bool enabled)
    {
        InteractionEnabled = enabled && HasHealthyFrame && IsVisible;
        if (Handle != 0) NativeMethods.SetDesktopPetInteraction(Handle, InteractionEnabled);
        if (!InteractionEnabled) CancelPointer();
    }

    internal void ReleasePointerCapture()
    {
        gesture.Stop();
        captured = false;
        if (Handle != 0 && NativeMethods.DesktopPetCapturedWindow == Handle) _ = NativeMethods.ReleaseCapture();
    }

    internal void CancelPointer()
    {
        ReleasePointerCapture();
        driver?.CancelPointer();
    }

    private Point MouseScreenPoint(nint parameter) => new(pixelOrigin.X + unchecked((short)((long)parameter & 0xffff)),
        pixelOrigin.Y + unchecked((short)(((long)parameter >> 16) & 0xffff)));

    private nint MessageHook(nint window, int message, nint w, nint l, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE
        if (message == 0x0084)
        {
            var screen = new Point(unchecked((short)((long)l & 0xffff)), unchecked((short)(((long)l >> 16) & 0xffff)));
            handled = true;
            return InteractionEnabled && HitTestScenePoint(screenPixelsToScene(screen)) ? 1 : -1;
        }
        if (message is 0x0201 or 0x0200 or 0x0202 or 0x0205)
        {
            if (message is 0x0200 or 0x0202 && NativeMethods.DesktopPetCurrentMessageIsPointerInput &&
                gesture.MatchesNativeMessage(message, NativeMethods.DesktopPetMessageTimestamp))
            { handled = true; return 0; }
            var screen = MouseScreenPoint(l);
            var point = screenPixelsToScene(screen);
            var shift = ((long)w & 0x0004) != 0;
            if (message == 0x0201 && InteractionEnabled && ((long)w & 0x0008) == 0 && HitTestScenePoint(point))
            {
                if (driver?.PointerDown(point, shift) == true)
                {
                    _ = NativeMethods.SetCapture(window);
                    captured = NativeMethods.DesktopPetCapturedWindow == window;
                    if (!captured || !gesture.Start(shift, screen, CurrentPointerGuard)) CancelPointer();
                }
                handled = true;
            }
            else if (message == 0x0200 && captured) { _ = driver?.PointerMove(point, shift); handled = true; }
            else if (message == 0x0202 && captured)
            {
                try { _ = driver?.PointerUp(point, shift); }
                finally { ReleasePointerCapture(); }
                handled = true;
            }
            else if (message == 0x0205 && InteractionEnabled && HitTestScenePoint(point))
            { CancelPointer(); ContextRequested?.Invoke(screen); handled = true; }
        }
        if (message == 0x0215 && captured && NativeMethods.DesktopPetCapturedWindow != Handle)
        { CancelPointer(); }
        if (message == 0x001F) CancelPointer();
        if (message is 0x02E0 or 0x007E)
        { LastSurfaceInvalidationMessage = message; CancelPointer(); SurfaceInvalidated?.Invoke(); }
        return 0;
    }
}
