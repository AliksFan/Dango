using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
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
    private sealed record PreviewDoor(string Id, string Name, string Kind, double X,
        PorchTargetKind Action = PorchTargetKind.Preview, Guid? RoomId = null);
    private static readonly PreviewDoor[] ReviewDoors =
    [
        new("preview-own", "我的桌宠房", "桌宠房", 560),
        new("preview-visit", "阿圆的桌宠房", "桌宠房", 780),
        new("preview-private", "小茶室", "私密共同房", 1000),
        new("preview-public", "云间", "公共共同房", 1220),
    ];
    private static readonly PreviewDoor[] OrdinaryDoors = [new("local-single", "普通房", "本地单人", 560, PorchTargetKind.LocalRoom)];
    private static readonly PreviewDoor[] DesktopPetDoors = [new("local-desktop-pet", "桌宠房", "本地单人", 780, PorchTargetKind.DesktopPetRoom)];
    private static readonly PreviewDoor[] LocalDoors = [.. OrdinaryDoors, .. DesktopPetDoors];
    private bool localRoomEnabled;
    private bool desktopPetRoomEnabled;
    private PreviewDoor[] roomDoors = [];
    private readonly PreviewDoor[] facilities =
    [
        new("porch-create", "建房牌", "创建共同房", 160, PorchTargetKind.Create),
        new("porch-visit", "访客信箱", "公共访问码", 350, PorchTargetKind.Visit),
        new("porch-archive", "旧门册", "房门排列与恢复", 1800, PorchTargetKind.Archive),
        new("porch-online", "在线状态牌", "连接与成员", 2000, PorchTargetKind.Online),
    ];
    private bool businessEnabled;
    private string directoryStatus = "登录后可读取账号房门 · 本地单人始终可玩";
    private PreviewDoor[] VisibleDoors { get; set; } = [];
    private PreviewDoor[] BuildTargets() => IsReviewMode ? ReviewDoors :
        [.. (localRoomEnabled && desktopPetRoomEnabled ? LocalDoors : localRoomEnabled ? OrdinaryDoors : desktopPetRoomEnabled ? DesktopPetDoors : []),
         .. (businessEnabled ? roomDoors : []), .. (businessEnabled ? facilities : [])];
    private readonly PorchCameraController camera = new();
    private readonly Canvas liveLayer = new() { IsHitTestVisible = false };
    private readonly Canvas targetLayer = new();
    private readonly Dictionary<string, System.Windows.Controls.Button> targetButtons = [];
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
        Children.Add(targetLayer);
        SizeChanged += (_, _) => { camera.Resize(PetWorldX, ViewportWorldWidth); UpdateProjection(); UpdateTargets(); InvalidateVisual(); ProjectionChanged?.Invoke(); };
        AutomationProperties.SetName(this, "二维门廊，尚未接入房间业务");
    }

    internal event Action<string>? DoorSelected;
    internal event Action? GameplayFocusRequested;
    internal event Action? LocalRoomRequested;
    internal event Action? DesktopPetRoomRequested;
    internal event Action<PorchTargetRequest>? TargetRequested;
    internal event Action? ProjectionChanged;
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
    internal IReadOnlyList<System.Windows.Controls.Button> TargetButtons => targetButtons.Values.ToArray();
    internal double InteractionScreenX => ((VisibleDoors.FirstOrDefault(d => d.Id == selectedDoor)?.X ?? PetWorldX) - CameraX) * SceneScale;
    internal DeformableDangoVisual LiveBody => body;
    internal void SetLocalRoomEnabled(bool enabled)
    {
        localRoomEnabled = enabled;
        AutomationProperties.SetName(this, "二维门廊，普通房为本地单人");
        UpdateTargets(rebuild: true);
        InvalidateVisual();
    }

    internal void SetDesktopPetRoomEnabled(bool enabled)
    {
        desktopPetRoomEnabled = enabled;
        AutomationProperties.SetName(this, "二维门廊，普通房与桌宠房为本地单人");
        UpdateTargets(rebuild: true);
        InvalidateVisual();
    }

    internal void SetReviewMode(bool enabled)
    {
        IsReviewMode = enabled;
        selectedDoor = status = null;
        AutomationProperties.SetName(this, enabled ? "门廊场景预览，虚构房门，未接房间业务" : "二维门廊，房间目录尚未接入");
        UpdateTargets(rebuild: true);
        InvalidateVisual();
    }

    internal void EnableBusinessTargets()
    {
        businessEnabled = true;
        AutomationProperties.SetName(this, "门廊，走近房门或设施按 W 互动，本地房与账号在线房分别显示");
        UpdateTargets(rebuild: true); InvalidateVisual();
    }

    internal void ApplyDirectory(RoomManagementView[]? rooms, string state, bool busy, string? message)
    {
        // Four doors fit the fixed physics world. The controller requests bounded pages of four.
        roomDoors = (rooms ?? []).Take(4).Select((room, index) => new PreviewDoor(
            "room-" + room.RoomId.ToString("N"), room.Name,
            room.Status == "dissolved" ? "已解散 · 可核对恢复" : room.Kind switch
            { "desktopPet" => "账号桌宠房", "privateShared" => "私密共同房", "publicShared" => "公共共同房", _ => "未知房间" },
            1000 + index * 200, PorchTargetKind.Room, room.RoomId)).ToArray();
        directoryStatus = message ?? (busy ? "正在读取房门…" : rooms is null ? "尚未确认账号房门 · 本地房仍可使用" :
            $"最后确认的{(state == "dissolved" ? "已解散" : "当前")}房门 · 本页 {rooms.Length} 扇 · 旧门册可换页或刷新");
        AutomationProperties.SetHelpText(this, directoryStatus);
        UpdateTargets(rebuild: true); InvalidateVisual();
    }

    internal bool InteractTarget(string id)
    {
        var target = VisibleDoors.FirstOrDefault(d => d.Id == id);
        return target is not null && SelectPreviewDoor(target);
    }

    private void UpdateTargets(bool rebuild = false)
    {
        if (rebuild) VisibleDoors = BuildTargets();
        var visible = VisibleDoors;
        if (rebuild)
            foreach (var removed in targetButtons.Keys.Except(visible.Select(d => d.Id)).ToArray())
            { targetLayer.Children.Remove(targetButtons[removed]); targetButtons.Remove(removed); }
        foreach (var target in visible)
        {
            if (!targetButtons.TryGetValue(target.Id, out var button))
            {
                var id = target.Id;
                var hitSurface = new FrameworkElementFactory(typeof(Border));
                hitSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                button = new System.Windows.Controls.Button
                {
                    Name = "PorchTarget_" + id.Replace('-', '_'), Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0), Focusable = true,
                    Template = new ControlTemplate(typeof(System.Windows.Controls.Button))
                    { VisualTree = hitSurface },
                };
                // A visible hit surface supplies native ButtonAutomationPeer/InvokePattern.
                button.Click += (_, _) => { GameplayFocusRequested?.Invoke(); InteractTarget(id); };
                targetButtons.Add(id, button); targetLayer.Children.Add(button);
            }
            if (rebuild)
            {
                AutomationProperties.SetAutomationId(button, "PorchTarget_" + target.Id);
                AutomationProperties.SetName(button, target.Name + " · " + target.Kind);
                button.ToolTip = target.Name + " · 走近后按 W 或点击";
            }
            var bounds = DoorRect(target);
            Canvas.SetLeft(button, bounds.Left); Canvas.SetTop(button, bounds.Top);
            button.Width = bounds.Width; button.Height = bounds.Height;
        }
    }

    internal void ResetProjection(double petX)
    {
        PetWorldX = petX;
        resetCamera = true;
        selectedDoor = status = null;
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        Dango.Desktop.Accessories.AccessoryHostBinding.Hide(body);
        UpdateTargets(); ProjectionChanged?.Invoke();
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
        UpdateTargets(); ProjectionChanged?.Invoke();
        InvalidateVisual();
    }

    internal void ShowUnavailable(string? message = null)
    {
        Dango.Desktop.Accessories.AccessoryHostBinding.Hide(body);
        body.Visibility = hat.Visibility = Visibility.Collapsed;
        unavailable = true;
        status = message ?? "团子暂不可用 · 请返回设置重试";
        InvalidateVisual();
    }

    private void UpdateProjection() => liveLayer.RenderTransform = new MatrixTransform(
        SceneScale, 0, 0, SceneScale, -CameraX * SceneScale, FrontGroundY - liveGroundY * SceneScale);

    internal bool InteractNearestDoor()
    {
        if (NearestDoor is not { } nearest) { status = "房间目录尚未接入"; InvalidateVisual(); return false; }
        return SelectPreviewDoor(nearest);
    }

    private bool SelectPreviewDoor(PreviewDoor door)
    {
        if (Math.Abs(door.X - PetWorldX) > InteractionDistance)
        { status = "走近房门，再按 W"; InvalidateVisual(); return false; }
        selectedDoor = door.Id;
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
        if (door.Action != PorchTargetKind.Preview)
        {
            status = door.Name;
            TargetRequested?.Invoke(new(door.Action, door.RoomId));
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
        GameplayFocusRequested?.Invoke();
        base.OnMouseLeftButtonDown(e);
    }

    private static bool IsFacility(PreviewDoor door) => door.Action is PorchTargetKind.Create or PorchTargetKind.Visit or PorchTargetKind.Archive or PorchTargetKind.Online;
    private Rect DoorRect(PreviewDoor door)
    {
        var height = IsFacility(door) ? 116 : 194;
        return new((door.X - CameraX - 62) * SceneScale,
            BackWallGroundY - height * SceneScale, 124 * SceneScale, height * SceneScale);
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
                if (IsFacility(door))
                {
                    var nearFacility = door.Id == nearbyDoorId;
                    dc.DrawLine(new Pen(line, 4 * SceneScale), new Point(rect.Left + rect.Width / 2, rect.Bottom),
                        new Point(rect.Left + rect.Width / 2, rect.Top + 50 * SceneScale));
                    dc.DrawRoundedRectangle(nearFacility ? ResourceBrush("HomeSelected", soft) : soft,
                        new Pen(nearFacility ? accent : line, 2 * SceneScale),
                        new Rect(rect.Left, rect.Top, rect.Width, 72 * SceneScale), 7 * SceneScale, 7 * SceneScale);
                    Text(dc, door.Name, new Point(rect.Left + rect.Width / 2, rect.Top + 12 * SceneScale), 13, ink, true);
                    Text(dc, door.Kind, new Point(rect.Left + rect.Width / 2, rect.Top + 36 * SceneScale), 10, muted, true);
                    if (nearFacility) Text(dc, "W", new Point(rect.Left + rect.Width / 2, rect.Top - 24 * SceneScale), 13, accent, true);
                    continue;
                }
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
                else dc.DrawRoundedRectangle(null, new Pen(muted, 1), new Rect(iconAt.X - 5, iconAt.Y - 4, 10, 14), 2, 2);
                dc.DrawEllipse(accent, null, new Point(rect.Right - 22 * SceneScale, rect.Top + rect.Height * .66), 3 * SceneScale, 3 * SceneScale);
                Text(dc, door.Name, new Point(rect.Left + rect.Width / 2, rect.Top - 42 * SceneScale), 14, ink, centered: true, maxWidth: 180 * SceneScale);
                Text(dc, door.Kind, new Point(rect.Left + rect.Width / 2, rect.Top - 21 * SceneScale), 11, muted, centered: true);
                if (near) Text(dc, "W", new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height * .58), 13, accent, centered: true);
            }
            if (body.Visibility == Visibility.Visible)
                dc.DrawEllipse(line, null, new Point((PetWorldX - CameraX) * SceneScale, FrontGroundY + 3 * SceneScale), 38 * SceneScale, 4 * SceneScale);
        }
        else Text(dc, "房间目录尚未接入", new Point(ActualWidth / 2, Math.Max(72, BackWallGroundY - 100)), 13, muted, centered: true);
        if (businessEnabled && !IsReviewMode)
            Text(dc, directoryStatus, new Point(12, ActualHeight - 43), 10, muted, maxWidth: Math.Max(1, ActualWidth - 24));
        Text(dc, status ?? "A / D 移动    S 轻压    空格跳跃    W 互动", new Point(ActualWidth / 2, ActualHeight - 22), 11, muted, centered: true);
    }

    private Brush ResourceBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    private void Text(DrawingContext dc, string value, Point at, double size, Brush brush, bool centered = false, double? maxWidth = null)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (maxWidth is { } width) { text.MaxTextWidth = width; text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        dc.DrawText(text, centered ? new Point(at.X - text.Width / 2, at.Y) : at);
    }
}
