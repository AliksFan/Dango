using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;
using Grid = System.Windows.Controls.Grid;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomSceneView : Grid
{
    internal const double WorldWidth = 2160;
    internal const double WorldHeight = 360;
    internal const double SideBoundaryInset = 24;
    internal const double VisualFloorLift = 10;
    private static readonly Brush ExcludedArea = CreateExcludedArea();
    // Fixed conservative envelope for the accepted body, supported hats and native jump.
    internal const double SafeActivityHeight = 260;
    private readonly Canvas entityLayer = new() { IsHitTestVisible = false };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, IsHitTestVisible = false };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false };
    private readonly RoomWheelAccumulator wheel = new();
    private double groundY = WorldHeight;
    private double petX = WorldWidth / 2;
    private long previousFrame;
    private double projectedWidth;
    private double projectedHeight;
    private double projectedGround = double.NaN;
    private string? unavailableMessage;
    private LogicalRect? bodyWorldBounds;
    private Thickness safeInsets;
    private bool inputBlocked;
    internal RoomViewController Camera { get; } = new(WorldWidth, WorldHeight);
    internal Thickness SafeInsets
    {
        get => safeInsets;
        set
        {
            if (safeInsets == value) return;
            InputInvalidated?.Invoke();
            safeInsets = value;
            ClearWheel();
            UpdateProjection(0);
        }
    }
    private RoomProjection Projection => Camera.Projection with
    { OffsetX = Camera.Projection.OffsetX + SafeInsets.Left, OffsetY = Camera.Projection.OffsetY + SafeInsets.Top };
    internal LogicalRect? PetScreenBounds { get; private set; }
    internal bool HasHealthyFrame { get; private set; }
    internal (double Left, double Right, double CollisionY, double FloorY) EnvironmentEdges
    {
        get
        {
            var left = Projection.WorldToScreen(new(SideBoundaryInset, groundY));
            var right = Projection.WorldToScreen(new(WorldWidth - SideBoundaryInset, groundY));
            return (left.X, right.X, left.Y, left.Y - VisualFloorLift);
        }
    }
    internal bool InputBlocked
    {
        get => inputBlocked;
        set
        {
            if (inputBlocked == value) return;
            if (value) InputInvalidated?.Invoke();
            inputBlocked = value;
        }
    }
    internal bool IsHeightLimited => Camera.Preset is RoomViewPreset.Standard or RoomViewPreset.Near &&
        Camera.Scale + .001 < Camera.StandardScale * (Camera.Preset == RoomViewPreset.Near ? 1.3 : 1);
    internal event Action? ViewChanged;
    internal event Action? FrameApplied;
    internal event Action? InputInvalidated;

    internal LocalRoomSceneView()
    {
        Background = new SolidColorBrush(Color.FromRgb(242, 235, 222));
        ClipToBounds = true;
        Focusable = true;
        entityLayer.Children.Add(body);
        entityLayer.Children.Add(hat);
        Children.Add(entityLayer);
        SizeChanged += (_, _) => { InputInvalidated?.Invoke(); ClearWheel(); UpdateProjection(0); };
        MouseLeave += (_, _) => ClearWheel();
        MouseLeftButtonDown += (_, _) => Focus();
        body.Visibility = hat.Visibility = Visibility.Collapsed;
    }

    internal void BeginPresentation()
    {
        InvalidateInputFrame();
        unavailableMessage = null;
        previousFrame = 0;
        ClearWheel();
    }

    internal bool ApplyFrame(StackedPhysicsPresentationFrame frame, StackedDangoSceneRuntime runtime)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || frame.Bodies.Count != 1 || frame.ConstraintsReleased ||
            runtime.LastError is not null || !frame.Bodies[0].IsVisible ||
            frame.Bodies[0].FallbackReason != PhysicsPresentationFallbackReason.None || runtime.LocalGroundY is not { } floor)
        {
            InvalidateInputFrame();
            return false;
        }
        groundY = floor;
        petX = runtime.CurrentSelectedCenter?.X ?? DangoHostFrameBinding.Bounds(frame, 0).Center.X;
        if (runtime.NeutralBodyVisibleWidth is { } neutral) Camera.CalibrateStandardScale(neutral);
        var bodyGroundAlignment = new LogicalVector(0, runtime.NeutralBodyGroundOffset);
        DangoHostFrameBinding.Apply(body, hat, frame, 0, bodyGroundAlignment, appearance: runtime.DisplayHatAppearance,
            hatSize: runtime.DisplayHatSize, bodyFillOverride: runtime.BodyPreviewFill,
            eyeFillOverride: runtime.EyePreviewFill, appearanceEntity: runtime.AppearanceEntity);
        if (body.LastContourFailure != DangoContourFailure.None)
        {
            InvalidateInputFrame();
            return false;
        }
        bodyWorldBounds = DangoHostFrameBinding.Bounds(frame, 0).Translate(bodyGroundAlignment);
        var now = Environment.TickCount64;
        UpdateProjection(previousFrame == 0 ? 0 : now - previousFrame);
        previousFrame = now;
        if (WanderVisibleBounds.Get(frame.Bodies[0], DangoHostFrameBinding.Bounds(frame, 0), runtime.DisplayHatSize) is { } visible)
        {
            visible = visible.Translate(bodyGroundAlignment);
            var topLeft = Projection.WorldToScreen(new(visible.X, visible.Y));
            PetScreenBounds = new(topLeft.X, topLeft.Y, visible.Width * Camera.Scale, visible.Height * Camera.Scale);
        }
        wheel.Expire(now);
        InvalidateVisual();
        HasHealthyFrame = true;
        FrameApplied?.Invoke();
        return true;
    }

    internal void SelectView(RoomViewPreset preset)
    {
        InputInvalidated?.Invoke();
        ClearWheel();
        Camera.SelectPreset(preset);
        UpdateProjection(0);
        ViewChanged?.Invoke();
    }

    internal void ApplyWheel(int nativeDelta)
    {
        InputInvalidated?.Invoke();
        if (!HasHealthyFrame || InputBlocked) { ClearWheel(); return; }
        var steps = wheel.Add(nativeDelta, Environment.TickCount64);
        if (steps != 0 && Camera.ApplyZoomSteps(steps))
        {
            UpdateProjection(0);
            ViewChanged?.Invoke();
        }
    }

    internal void ClearWheel() => wheel.Clear();
    internal LogicalPoint ScreenToWorld(Point sceneDip) => Projection.ScreenToWorld(new(sceneDip.X, sceneDip.Y));
    internal bool IsPetHit(Point sceneDip) => HasHealthyFrame && !InputBlocked && bodyWorldBounds is { } bounds &&
        double.IsFinite(sceneDip.X) && double.IsFinite(sceneDip.Y) && bounds.Contains(ScreenToWorld(sceneDip));

    internal void ShowUnavailable(string message)
    {
        InvalidateInputFrame();
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        unavailableMessage = message;
        ClearWheel();
        InvalidateVisual();
    }

    private void InvalidateInputFrame()
    {
        InputInvalidated?.Invoke();
        HasHealthyFrame = false;
        bodyWorldBounds = null;
        PetScreenBounds = null;
    }

    private void UpdateProjection(double elapsedMs)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var width = ActualWidth - SafeInsets.Left - SafeInsets.Right;
        var height = ActualHeight - SafeInsets.Top - Math.Max(0, SafeInsets.Bottom + 16 - 64);
        if (width <= 0 || height <= 88) return;
        if (projectedWidth != width || projectedHeight != height || projectedGround != groundY)
        {
            Camera.Resize(width, height, groundY, SafeActivityHeight);
            projectedWidth = width;
            projectedHeight = height;
            projectedGround = groundY;
        }
        Camera.Update(petX, elapsedMs, !SystemParameters.ClientAreaAnimation);
        var p = Projection;
        entityLayer.RenderTransform = new MatrixTransform(p.Scale, 0, 0, p.Scale, p.OffsetX, p.OffsetY);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (unavailableMessage is not null)
        {
            var text = new FormattedText(unavailableMessage, System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Microsoft YaHei"), 14, System.Windows.Media.Brushes.DimGray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(Math.Max(24, (ActualWidth - text.Width) / 2), 48));
        }
        // Only the painted floor is lifted; the camera, body and native collision remain unchanged.
        var edges = EnvironmentEdges;
        var left = Math.Clamp(edges.Left, 0, ActualWidth);
        var right = Math.Clamp(edges.Right, left, ActualWidth);
        var floorY = Math.Clamp(edges.FloorY, 0, ActualHeight);
        if (right > left && floorY < ActualHeight)
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(205, 182, 149)), null,
                new Rect(left, floorY, right - left, ActualHeight - floorY));
        if (left > 0) dc.DrawRectangle(ExcludedArea, null, new Rect(0, 0, left, ActualHeight));
        if (right < ActualWidth) dc.DrawRectangle(ExcludedArea, null, new Rect(right, 0, ActualWidth - right, ActualHeight));
        var edgePen = new Pen(new SolidColorBrush(Color.FromRgb(138, 119, 95)), 1.5) { DashStyle = DashStyles.Dash };
        foreach (var x in new[] { edges.Left, edges.Right })
            if (x >= 0 && x <= ActualWidth) dc.DrawLine(edgePen, new Point(x, 0), new Point(x, ActualHeight));
    }

    private static Brush CreateExcludedArea()
    {
        var tile = new DrawingGroup();
        using (var dc = tile.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(183, 174, 160)), null, new Rect(0, 0, 16, 16));
            var line = new Pen(new SolidColorBrush(Color.FromRgb(143, 130, 111)), 1.5);
            dc.DrawLine(line, new Point(0, 16), new Point(16, 0));
        }
        var brush = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }
}
