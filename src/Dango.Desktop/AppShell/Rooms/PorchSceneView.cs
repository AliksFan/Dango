using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;

namespace Dango.Desktop.AppShell.Rooms;

/// <summary>Local world projection only. Preview doors never perform room admission.</summary>
public sealed class PorchSceneView : Grid
{
    internal const double WorldWidth = 2160;
    internal const double WorldHeight = 360;
    internal const double InteractionDistance = 132;
    private sealed record PreviewDoor(string Id, string Name, string Kind, double X);
    private static readonly PreviewDoor[] ReviewDoors =
    [
        new("preview-own", "我的桌宠房", "桌宠房", 560),
        new("preview-visit", "阿圆的桌宠房", "桌宠房", 940),
        new("preview-private", "小茶室", "私密共同房", 1320),
        new("preview-public", "云间", "公共共同房", 1700),
    ];
    private readonly PorchCameraController camera = new();
    private readonly Canvas liveLayer = new() { IsHitTestVisible = false };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private bool resetCamera = true;
    private string? selectedDoor;
    private string? status;
    private double liveGroundY = WorldHeight;
    private bool unavailable;

    public PorchSceneView()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        liveLayer.Children.Add(body);
        liveLayer.Children.Add(hat);
        Children.Add(liveLayer);
        SizeChanged += (_, _) => { camera.Resize(PetWorldX, ViewportWorldWidth); UpdateProjection(); InvalidateVisual(); };
        AutomationProperties.SetName(this, "二维门廊，尚未接入房间业务");
    }

    internal event Action<string>? DoorSelected;
    internal event Action? GameplayFocusRequested;
    internal bool IsReviewMode { get; private set; }
    internal int DoorCount => IsReviewMode ? ReviewDoors.Length : 0;
    internal bool CanEnterRoom => false;
    internal double PetWorldX { get; private set; } = 560;
    internal double CameraX => camera.CameraX;
    internal double SceneScale => Math.Clamp(ActualHeight / 420, .45, 1.5);
    internal double ViewportWorldWidth => ActualWidth / SceneScale;
    internal double BackWallGroundY => ActualHeight - 132 * SceneScale;
    internal double DoorBottomY => BackWallGroundY;
    internal double FrontGroundY => ActualHeight - 48 * SceneScale;
    internal string? SelectedDoorId => selectedDoor;
    internal DeformableDangoVisual LiveBody => body;

    internal void SetReviewMode(bool enabled)
    {
        IsReviewMode = enabled;
        selectedDoor = status = null;
        AutomationProperties.SetName(this, enabled ? "门廊场景预览，虚构房门，未接房间业务" : "二维门廊，房间目录尚未接入");
        InvalidateVisual();
    }

    internal void ResetProjection(double petX)
    {
        PetWorldX = petX;
        resetCamera = true;
        selectedDoor = status = null;
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        InvalidateVisual();
    }

    internal void ApplyFrame(StackedPhysicsPresentationFrame frame, StackedDangoSceneRuntime runtime, double elapsedMs, bool reduceMotion)
    {
        PetWorldX = DangoHostFrameBinding.Bounds(frame, 0).Center.X;
        if (unavailable) { status = null; unavailable = false; }
        liveGroundY = runtime.LocalGroundY ?? WorldHeight;
        if (resetCamera) { camera.Reset(PetWorldX, ViewportWorldWidth); resetCamera = false; }
        else camera.Update(PetWorldX, ViewportWorldWidth, elapsedMs, reduceMotion);
        DangoHostFrameBinding.Apply(body, hat, frame, 0, appearance: runtime.DisplayHatAppearance,
            hatSize: runtime.DisplayHatSize, bodyFillOverride: runtime.BodyPreviewFill);
        UpdateProjection();
        InvalidateVisual();
    }

    internal void ShowUnavailable(string? message = null)
    {
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        unavailable = true;
        status = message ?? "团子暂不可用 · 请返回设置重试";
        InvalidateVisual();
    }

    private void UpdateProjection() => liveLayer.RenderTransform = new MatrixTransform(
        SceneScale, 0, 0, SceneScale, -CameraX * SceneScale, FrontGroundY - liveGroundY * SceneScale);

    internal bool InteractNearestDoor()
    {
        if (!IsReviewMode) { status = "房间目录尚未接入"; InvalidateVisual(); return false; }
        var nearest = ReviewDoors.MinBy(door => Math.Abs(door.X - PetWorldX))!;
        return SelectPreviewDoor(nearest);
    }

    private bool SelectPreviewDoor(PreviewDoor door)
    {
        if (Math.Abs(door.X - PetWorldX) > InteractionDistance)
        { status = "走近房门，再按 W"; InvalidateVisual(); return false; }
        selectedDoor = door.Id;
        status = $"{door.Name} · 仅选门预览，未接入房间";
        DoorSelected?.Invoke(door.Id);
        InvalidateVisual();
        return true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        GameplayFocusRequested?.Invoke();
        base.OnMouseLeftButtonDown(e);
        if (!IsReviewMode) return;
        var pointer = e.GetPosition(this);
        foreach (var door in ReviewDoors)
            if (DoorRect(door).Contains(pointer)) { SelectPreviewDoor(door); e.Handled = true; break; }
    }

    private Rect DoorRect(PreviewDoor door) => new((door.X - CameraX - 62) * SceneScale,
        BackWallGroundY - 194 * SceneScale, 124 * SceneScale, 194 * SceneScale);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var ink = ResourceBrush("HomeInk", Brushes.DimGray);
        var muted = ResourceBrush("HomeMuted", Brushes.Gray);
        var line = ResourceBrush("HomeLine", Brushes.LightGray);
        var soft = ResourceBrush("HomeSoft", Brushes.WhiteSmoke);
        var accent = ResourceBrush("HomeAccent", Brushes.SaddleBrown);
        if (!SystemParameters.HighContrast)
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(250, 247, 242)), null,
                new Point(ActualWidth * .48, BackWallGroundY + 20), ActualWidth * .46, ActualHeight * .75);
        dc.DrawRectangle(soft, null, new Rect(0, Math.Max(0, BackWallGroundY), ActualWidth, Math.Max(0, ActualHeight - BackWallGroundY)));
        dc.DrawLine(new Pen(line, 1), new Point(0, BackWallGroundY), new Point(ActualWidth, BackWallGroundY));
        if (IsReviewMode)
        {
            foreach (var door in ReviewDoors)
            {
                var rect = DoorRect(door);
                if (rect.Right < 0 || rect.Left > ActualWidth) continue;
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
                    g.LineTo(new Point(rect.Left, rect.Top + rect.Width / 2), true, false);
                    g.ArcTo(new Point(rect.Right, rect.Top + rect.Width / 2), new Size(rect.Width / 2, rect.Width / 2), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                }
                var near = Math.Abs(door.X - PetWorldX) <= InteractionDistance;
                var fill = near || selectedDoor == door.Id ? ResourceBrush("HomeSelected", soft) : Brushes.Transparent;
                var frameBrush = SystemParameters.HighContrast ? ink : new SolidColorBrush(Color.FromRgb(219, 198, 170));
                if (near || selectedDoor == door.Id) frameBrush = SystemParameters.HighContrast ? accent : new SolidColorBrush(Color.FromRgb(180, 144, 102));
                dc.DrawGeometry(fill, new Pen(frameBrush, 4 * SceneScale), geometry);
                var iconAt = new Point(rect.Left + rect.Width / 2, rect.Top + 40 * SceneScale);
                if (door.Kind == "桌宠房")
                {
                    dc.DrawRoundedRectangle(null, new Pen(muted, 1), new Rect(iconAt.X - 7, iconAt.Y - 4, 14, 10), 1, 1);
                    dc.DrawLine(new Pen(muted, 1), new Point(iconAt.X, iconAt.Y + 6), new Point(iconAt.X, iconAt.Y + 9));
                    dc.DrawLine(new Pen(muted, 1), new Point(iconAt.X - 4, iconAt.Y + 9), new Point(iconAt.X + 4, iconAt.Y + 9));
                }
                else dc.DrawRoundedRectangle(null, new Pen(muted, 1), new Rect(iconAt.X - 5, iconAt.Y - 4, 10, 14), 2, 2);
                dc.DrawEllipse(accent, null, new Point(rect.Right - 22 * SceneScale, rect.Top + rect.Height * .66), 3 * SceneScale, 3 * SceneScale);
                Text(dc, door.Name, new Point(rect.Left + rect.Width / 2, rect.Top - 42 * SceneScale), 14, ink, centered: true);
                Text(dc, door.Kind, new Point(rect.Left + rect.Width / 2, rect.Top - 21 * SceneScale), 11, muted, centered: true);
                if (near) Text(dc, "W", new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height * .58), 13, accent, centered: true);
            }
            if (body.Visibility == Visibility.Visible)
                dc.DrawEllipse(line, null, new Point((PetWorldX - CameraX) * SceneScale, FrontGroundY + 3 * SceneScale), 38 * SceneScale, 4 * SceneScale);
        }
        else Text(dc, "房间目录尚未接入", new Point(ActualWidth / 2, Math.Max(72, BackWallGroundY - 100)), 13, muted, centered: true);
        Text(dc, status ?? "A / D 移动    S 轻压    空格跳跃    W 选门", new Point(ActualWidth / 2, ActualHeight - 22), 11, muted, centered: true);
    }

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private void Text(DrawingContext dc, string value, Point at, double size, Brush brush, bool centered = false)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, centered ? new Point(at.X - text.Width / 2, at.Y) : at);
    }
}
