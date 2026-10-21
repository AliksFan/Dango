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
public sealed partial class PorchSceneView : Grid
{
    internal const double WorldWidth = 2160;
    internal const double WorldHeight = 360;
    internal const double InteractionDistance = 132;
    private sealed record PreviewDoor(string Id, string Name, string Kind, double X);
    private static readonly PreviewDoor[] ReviewDoors =
    [
        new("preview-own", "我的桌宠房", "桌宠房", 560),
        new("preview-visit", "阿圆的桌宠房", "桌宠房", 780),
        new("preview-private", "小茶室", "私密共同房", 1000),
        new("preview-public", "云间", "公共共同房", 1220),
    ];
    private static readonly PreviewDoor[] OrdinaryDoors = [new("local-single", "普通房", "本地单人", 560)];
    private static readonly PreviewDoor[] DesktopPetDoors = [new("local-desktop-pet", "桌宠房", "桌宠房", 560)];
    private static readonly PreviewDoor[] LocalDoors = [.. DesktopPetDoors, OrdinaryDoors[0] with { X = 780 }];
    private bool localRoomEnabled;
    private bool desktopPetRoomEnabled;
    private PreviewDoor[] VisibleDoors => IsReviewMode ? ReviewDoors : catalogProvided ? catalogDoors : localRoomEnabled && desktopPetRoomEnabled ? LocalDoors
        : localRoomEnabled ? OrdinaryDoors : desktopPetRoomEnabled ? DesktopPetDoors : [];
    private readonly PorchCameraController camera = new();
    private readonly Canvas liveLayer = new() { IsHitTestVisible = false };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private bool resetCamera = true;
    private string? selectedDoor;
    private string? status;
    private double liveGroundY = WorldHeight;
    private bool unavailable;
    private Canvas? beltBody;
    private bool beltBodyPresented;
    internal bool HasBeltBody => beltBodyPresented && beltBody?.IsVisible == true;

    public PorchSceneView()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        liveLayer.Children.Add(body);
        liveLayer.Children.Add(hat);
        Children.Add(liveLayer);
        Children.Add(catalogControls);
        Children.Add(catalogActions);
        Children.Add(catalogFeedback);
        SizeChanged += (_, _) => { camera.Resize(PetWorldX, ViewportWorldWidth); UpdateProjection(); InvalidateVisual(); };
        AutomationProperties.SetName(this, "二维门廊，尚未接入房间业务");
    }

    internal event Action<string>? DoorSelected;
    internal event Action? GameplayFocusRequested;
    internal event Action? LocalRoomRequested;
    internal event Action? LocalFurnitureRequested;
    internal event Action? DesktopPetRoomRequested;
    internal bool IsReviewMode { get; private set; }
    internal int DoorCount => VisibleDoors.Length;
    internal bool CanEnterRoom => false;
    internal double PetWorldX { get; private set; } = 560;
    internal double CameraX => camera.CameraX;
    internal double SceneScale => Math.Clamp(ActualHeight / 420, .45, 1.5);
    internal double ViewportWorldWidth => ActualWidth / SceneScale;
    internal double BackWallGroundY => ActualHeight - 132 * SceneScale;
    internal double DoorBottomY => BackWallGroundY;
    internal double FrontGroundY => ActualHeight - 48 * SceneScale;
    internal string? SelectedDoorId => selectedDoor;
    private PreviewDoor? NearestDoor => VisibleDoors.MinBy(door => Math.Abs(door.X - PetWorldX));
    internal string? NearbyDoorId => NearestDoor is { } door && Math.Abs(door.X - PetWorldX) <= InteractionDistance ? door.Id : null;
    internal IReadOnlyList<Rect> DoorBounds => Array.AsReadOnly(VisibleDoors.Select(DoorRect).ToArray());
    internal DeformableDangoVisual LiveBody => body;
    internal void SetLocalRoomEnabled(bool enabled)
    {
        localRoomEnabled = enabled;
        AutomationProperties.SetName(this, "二维门廊，普通房为本地单人");
        InvalidateVisual();
    }

    internal void SetDesktopPetRoomEnabled(bool enabled)
    {
        desktopPetRoomEnabled = enabled;
        AutomationProperties.SetName(this, "二维门廊，普通房与桌宠房为本地单人");
        InvalidateVisual();
    }

    internal void SetReviewMode(bool enabled)
    {
        IsReviewMode = enabled;
        selectedDoor = status = null;
        AutomationProperties.SetName(this, enabled ? "门廊场景预览，虚构房门，未接房间业务" : "二维门廊，房间目录尚未接入");
        UpdateCatalogControls();
        InvalidateVisual();
    }

    internal void ResetProjection(double petX)
    {
        PetWorldX = petX;
        resetCamera = true;
        selectedDoor = status = null;
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        Dango.Desktop.Accessories.AccessoryHostBinding.Hide(body);
        UpdateProjection();
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
            hatSize: runtime.DisplayHatSize, bodyFillOverride: runtime.BodyPreviewFill,
            eyeFillOverride: runtime.EyePreviewFill, appearanceEntity: runtime.AppearanceEntity, runtime: runtime);
        UpdateProjection();
        InvalidateVisual();
    }

    internal void AttachBeltBody(Canvas view, double petX)
    {
        DetachBeltBody();
        ResetProjection(petX);
        beltBody = view;
        beltBodyPresented = false;
        liveGroundY = WorldHeight;
        liveLayer.Children.Add(view);
    }

    internal void DetachBeltBody()
    {
        if (beltBody is not null) liveLayer.Children.Remove(beltBody);
        beltBody = null;
        beltBodyPresented = false;
    }

    internal void PresentBeltBody(double petX, double elapsedMs, bool reduceMotion)
    {
        if (beltBody is null) return;
        PetWorldX = petX;
        beltBodyPresented = true;
        if (unavailable) { status = null; unavailable = false; }
        if (resetCamera) { camera.Reset(PetWorldX, ViewportWorldWidth); resetCamera = false; }
        else camera.Update(PetWorldX, ViewportWorldWidth, elapsedMs, reduceMotion);
        UpdateProjection();
        InvalidateVisual();
    }

    internal void ShowUnavailable(string? message = null)
    {
        Dango.Desktop.Accessories.AccessoryHostBinding.Hide(body);
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        beltBodyPresented = false;
        unavailable = true;
        status = message ?? "团子暂不可用 · 请返回设置重试";
        UpdateCatalogControls();
        InvalidateVisual();
    }

    private void UpdateProjection()
    {
        liveLayer.RenderTransform = new MatrixTransform(SceneScale, 0, 0, SceneScale, -CameraX * SceneScale, FrontGroundY - liveGroundY * SceneScale);
        UpdateCatalogControls();
    }

    internal bool RequestNearbyFurniture()
    {
        if (catalogProvided) return RequestCatalogFurniture();
        if (catalogInputBlocked || !localRoomEnabled || unavailable || NearbyDoorId != "local-single") return false;
        selectedDoor = "local-single";
        LocalFurnitureRequested?.Invoke();
        return true;
    }

    internal bool InteractNearestDoor()
    {
        if (NearestDoor is not { } nearest) { status = "房间目录尚未接入"; InvalidateVisual(); return false; }
        return SelectPreviewDoor(nearest);
    }

    private bool SelectPreviewDoor(PreviewDoor door)
    {
        if (catalogInputBlocked || unavailable) return false;
        if (Math.Abs(door.X - PetWorldX) > InteractionDistance)
        { status = "走近房门，再按 F"; InvalidateVisual(); return false; }
        selectedDoor = door.Id;
        if (catalogProvided) return ActivateCatalogDoor(door);
        if (door.Id == "local-single")
        {
            status = "准备进入普通房 · 本地单人";
            LocalRoomRequested?.Invoke();
            InvalidateVisual();
            return true;
        }
        if (door.Id == "local-desktop-pet")
        {
            status = "准备进入桌宠房";
            DesktopPetRoomRequested?.Invoke();
            InvalidateVisual();
            return true;
        }
        status = $"{door.Name} · 仅选门预览，未接入房间";
        DoorSelected?.Invoke(door.Id);
        InvalidateVisual();
        return true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (catalogInputBlocked || e.Handled) return;
        GameplayFocusRequested?.Invoke();
        base.OnMouseLeftButtonDown(e);
    }

    private Rect DoorRect(PreviewDoor door)
    {
        var top = BackWallGroundY - 194 * SceneScale;
        // Preserve the original door height; tools must fit around the doors.
        if (catalogProvided) top = Math.Max(top, 56 + 42 * SceneScale);
        return new((door.X - CameraX - 62) * SceneScale, top, 124 * SceneScale, Math.Max(0, BackWallGroundY - top));
    }

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
        if (VisibleDoors.Length != 0)
        {
            var nearbyDoorId = NearbyDoorId;
            foreach (var door in VisibleDoors)
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
                var near = door.Id == nearbyDoorId;
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
                else if (door.Kind == "空门位") Text(dc, "+", iconAt, 22, muted, centered: true);
                else dc.DrawRoundedRectangle(null, new Pen(muted, 1), new Rect(iconAt.X - 5, iconAt.Y - 4, 10, 14), 2, 2);
                dc.DrawEllipse(accent, null, new Point(rect.Right - 22 * SceneScale, rect.Top + rect.Height * .66), 3 * SceneScale, 3 * SceneScale);
                Text(dc, door.Name, new Point(rect.Left + rect.Width / 2, rect.Top - 42 * SceneScale), 14, ink, centered: true,
                    maxWidth: Math.Max(80, rect.Width + 32));
                Text(dc, door.Kind, new Point(rect.Left + rect.Width / 2, rect.Top - 21 * SceneScale), 11, muted, centered: true);
                if (near)
                {
                    var creationBlocked = catalogProvided && door.Kind == "空门位" && (!catalogHealthy || catalogRooms.Count >= 3);
                    var hint = creationBlocked ? catalogHealthy ? "已满" : "待核对" : "F";
                    Text(dc, hint, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height * .58), 13, creationBlocked ? muted : accent, centered: true);
                }
            }
            if (body.Visibility == Visibility.Visible || HasBeltBody)
                dc.DrawEllipse(line, null, new Point((PetWorldX - CameraX) * SceneScale, FrontGroundY + 3 * SceneScale), 38 * SceneScale, 4 * SceneScale);
        }
        else Text(dc, "房间目录尚未接入", new Point(ActualWidth / 2, Math.Max(72, BackWallGroundY - 100)), 13, muted, centered: true);
        if (catalogProvided) Text(dc, catalogHealthy ? $"普通房 {catalogRooms.Count}/3" : "普通房 · 目录待核对", new(ActualWidth - 92, 68), 12, muted, centered: true);
        if (!catalogProvided || status is null)
            Text(dc, status ?? "A / D 移动    W 拉伸    S 轻压    空格跳跃    F 进入", new Point(ActualWidth / 2, ActualHeight - 22), 11, muted, centered: true);
    }

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private void Text(DrawingContext dc, string value, Point at, double size, Brush brush, bool centered = false, double? maxWidth = null)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (maxWidth is { } limit) { text.MaxTextWidth = limit; text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        dc.DrawText(text, centered ? new Point(at.X - text.Width / 2, at.Y) : at);
    }
}
