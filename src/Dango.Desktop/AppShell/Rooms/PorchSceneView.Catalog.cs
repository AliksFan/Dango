using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Dango.Desktop.AppShell.Rooms.Storage;
using Button = System.Windows.Controls.Button;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;

namespace Dango.Desktop.AppShell.Rooms;

public sealed partial class PorchSceneView
{
    private bool catalogProvided, catalogHealthy, catalogInputBlocked;
    private PreviewDoor[] catalogDoors = [];
    private Dictionary<string, LocalRoomRecord> catalogRooms = [];
    private readonly StackPanel catalogControls = new()
    {
        Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top, Margin = new(8, 12, 16, 0)
    };
    private readonly TextBlock catalogFeedback = new()
    {
        Margin = new(16, 0, 16, 12),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        IsHitTestVisible = false,
        FontSize = 12,
        Visibility = Visibility.Collapsed
    };
    private readonly StackPanel catalogActions = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private string? actionDoorId;
    internal event Action<Guid>? CatalogRoomRequested, CatalogFurnitureRequested, CatalogDissolveRequested;
    internal event Action? CatalogCreateRequested, CatalogRecoveryRequested, CatalogReloadRequested;
    internal int CatalogOrdinaryCount => catalogRooms.Count;
    internal bool IsCatalogEntryFocused => catalogProvided && (catalogControls.IsKeyboardFocusWithin || catalogActions.IsKeyboardFocusWithin);
    internal bool CatalogInputBlocked
    {
        get => catalogInputBlocked;
        set { catalogInputBlocked = value; UpdateCatalogControls(); }
    }
    internal void SetCatalog(LocalRoomCatalog? catalog, Guid? selection = null, string? message = null)
    {
        catalogProvided = true; catalogHealthy = catalog is not null;
        catalogFeedback.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AutomationProperties.SetLiveSetting(catalogFeedback, AutomationLiveSetting.Polite);
        catalogRooms = catalog?.Rooms.Where(room => room.Life == LocalRoomLife.Active).ToDictionary(room => room.RoomId.ToString("D")) ?? [];
        var active = catalogRooms.Values.ToArray();
        var doors = new List<PreviewDoor> { new("local-desktop-pet", "桌宠房", "桌宠房", 560) };
        for (var i = 0; i < active.Length; i++)
            doors.Add(new(active[i].RoomId.ToString("D"), active[i].Name, active[i].CanEditOffline ? "本地单人" : "家具只读", 780 + i * 220));
        doors.Add(new("new-room", catalogHealthy ? "新建普通房" : "目录待核对", "空门位", 780 + active.Length * 220));
        catalogDoors = doors.ToArray(); selectedDoor = selection?.ToString("D"); status = message; actionDoorId = null;
        catalogControls.Children.Clear(); catalogActions.Children.Clear();
        var recovery = new Button { Content = catalogHealthy ? "↶ 已解散房间" : "↶ 重新读取", MinHeight = 40, Padding = new(14, 8, 14, 8) };
        AutomationProperties.SetName(recovery, catalogHealthy ? "查看与恢复已解散房间" : "重新读取房间目录");
        recovery.Click += (_, e) =>
        {
            e.Handled = true;
            if (catalogInputBlocked || unavailable) return;
            if (catalogHealthy) CatalogRecoveryRequested?.Invoke(); else CatalogReloadRequested?.Invoke();
        };
        catalogControls.Children.Add(recovery);
        AutomationProperties.SetName(this, "门廊场景，桌宠房、普通房与一个新建入口；恢复入口位于工具区");
        UpdateCatalogControls(); InvalidateVisual();
    }
    internal void SetCatalogStatus(string? message) { status = message; UpdateCatalogControls(); InvalidateVisual(); }
    private bool ActivateCatalogDoor(PreviewDoor door)
    {
        if (catalogInputBlocked || unavailable) return false;
        GameplayFocusRequested?.Invoke();
        if (door.Id == "local-desktop-pet") { DesktopPetRoomRequested?.Invoke(); return true; }
        if (!catalogHealthy) { SetCatalogStatus("目录受保护，请先重新读取；桌宠仍可使用。"); return false; }
        if (door.Kind == "空门位")
        {
            if (catalogRooms.Count >= 3) { SetCatalogStatus("普通房已满（3/3），可先解散房间再新建。"); return false; }
            CatalogCreateRequested?.Invoke();
        }
        else if (catalogRooms.TryGetValue(door.Id, out var room))
        { DoorSelected?.Invoke(door.Id); CatalogRoomRequested?.Invoke(room.RoomId); }
        UpdateCatalogControls(); InvalidateVisual(); return true;
    }
    private bool RequestCatalogFurniture()
    {
        if (catalogInputBlocked || unavailable || !catalogHealthy || NearbyDoorId is not { } id || !catalogRooms.TryGetValue(id, out var room)) return false;
        if (!room.CanEditOffline) { SetCatalogStatus("离线缓存布局只读，可单人游玩。"); return false; }
        selectedDoor = id; CatalogFurnitureRequested?.Invoke(room.RoomId); return true;
    }
    private void UpdateCatalogControls()
    {
        if (!catalogProvided) return;
        catalogControls.Visibility = IsReviewMode ? Visibility.Collapsed : Visibility.Visible;
        if (IsReviewMode)
        {
            catalogActions.Visibility = catalogFeedback.Visibility = Visibility.Collapsed;
            return;
        }
        catalogFeedback.Text = status;
        catalogFeedback.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        catalogControls.IsEnabled = !catalogInputBlocked && !unavailable;
        var nearby = NearbyDoorId;
        if (nearby is null || unavailable || catalogInputBlocked)
        { catalogActions.Visibility = Visibility.Collapsed; return; }
        var door = catalogDoors.Single(value => value.Id == nearby);
        if (actionDoorId != nearby)
        {
            actionDoorId = nearby; catalogActions.Children.Clear();
            if (catalogRooms.TryGetValue(nearby, out var room))
            {
                AddAction("↗ 进入", "进入" + room.Name, () => SelectPreviewDoor(door), shortcut: "F");
                AddAction("◇ 布置", "布置" + room.Name, () => RequestCatalogFurniture(), room.CanEditOffline, shortcut: "E");
                AddAction("× 解散", "解散" + room.Name, () => RequestCatalogDissolve(room.RoomId), room.Source == LocalRoomSource.LocalOnly);
            }
            else if (door.Kind == "空门位")
                AddAction("＋ 新建", "新建普通房", () => SelectPreviewDoor(door), catalogHealthy && catalogRooms.Count < 3,
                    catalogHealthy ? "普通房已满（3/3）" : "目录受保护，请先重新读取", shortcut: "F");
            else AddAction("↗ 进入", "进入桌宠房", () => SelectPreviewDoor(door), shortcut: "F");
        }
        catalogActions.Visibility = Visibility.Visible;
        catalogActions.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        catalogActions.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        var target = DoorRect(catalogDoors.Single(door => door.Id == nearby));
        catalogActions.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var margin = catalogActions.Margin;
        var actions = new System.Windows.Size(
            Math.Max(0, catalogActions.DesiredSize.Width - margin.Left - margin.Right),
            Math.Max(0, catalogActions.DesiredSize.Height - margin.Top - margin.Bottom));
        var left = Math.Clamp(target.Left + target.Width / 2 - actions.Width / 2, 8, Math.Max(8, ActualWidth - actions.Width - 8));
        var top = Math.Max(8, target.Top - 42 * SceneScale - actions.Height - 8);
        catalogControls.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolsMargin = catalogControls.Margin;
        var toolsWidth = Math.Max(0, catalogControls.DesiredSize.Width - toolsMargin.Left - toolsMargin.Right);
        var toolsHeight = Math.Max(0, catalogControls.DesiredSize.Height - toolsMargin.Top - toolsMargin.Bottom);
        var tools = new Rect(Math.Max(0, ActualWidth - toolsMargin.Right - toolsWidth), toolsMargin.Top, toolsWidth, toolsHeight);
        if (new Rect(left, top, actions.Width, actions.Height).IntersectsWith(tools))
        {
            // Move the action row, never the door geometry, to clear top tools.
            var beside = tools.Left - actions.Width - 8;
            if (beside >= 8) left = Math.Min(left, beside);
            else top = Math.Max(top, tools.Bottom + 8);
        }
        catalogActions.Margin = new(left, top, 0, 0);
    }
    private void AddAction(string label, string name, Action action, bool enabled = true, string? disabledReason = null, string? shortcut = null)
    {
        var button = new Button { Content = label, MinHeight = 40, Padding = new(8, 6, 8, 6), Margin = new(0, 0, 6, 0), IsEnabled = enabled };
        button.ToolTip = enabled ? name : disabledReason ?? "离线不可执行，需要联网确认资格";
        if (shortcut is not null)
        {
            var keycap = new Border();
            keycap.SetResourceReference(StyleProperty, "ShortcutKeycap");
            keycap.Child = new TextBlock { Text = shortcut, FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(keycap);
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            button.ToolTip = shortcut + " · " + button.ToolTip;
            AutomationProperties.SetAcceleratorKey(button, shortcut);
        }
        AutomationProperties.SetName(button, name);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (!catalogInputBlocked && actionDoorId == NearbyDoorId && !unavailable) action();
        };
        catalogActions.Children.Add(button);
    }
    private void RequestCatalogDissolve(Guid roomId)
    {
        if (catalogInputBlocked || unavailable || !catalogHealthy || NearbyDoorId != roomId.ToString("D") ||
            !catalogRooms.TryGetValue(roomId.ToString("D"), out var room) || room.Source != LocalRoomSource.LocalOnly) return;
        CatalogDissolveRequested?.Invoke(roomId);
    }
    internal void FocusCatalogOrigin()
    {
        UpdateCatalogControls();
        var button = catalogActions.Children.Cast<Button>().FirstOrDefault(value => value.IsEnabled);
        if (catalogActions.Visibility == Visibility.Visible && button is not null) button.Focus();
        else GameplayFocusRequested?.Invoke();
    }
}
