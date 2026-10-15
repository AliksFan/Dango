using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.AppShell.Rooms.Storage;
using Button = System.Windows.Controls.Button;
using Canvas = System.Windows.Controls.Canvas;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace Dango.Desktop.AppShell.Rooms;

public sealed partial class PorchSceneView
{
    private bool catalogProvided, catalogHealthy, catalogInputBlocked;
    private PreviewDoor[] catalogDoors = [];
    private Dictionary<string, LocalRoomRecord> catalogRooms = [];
    private readonly Canvas catalogControls = new();
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
        set { catalogInputBlocked = value; catalogControls.IsEnabled = catalogActions.IsEnabled = !value; }
    }
    internal void SetCatalog(LocalRoomCatalog? catalog, Guid? selection = null, string? message = null)
    {
        catalogProvided = true; catalogHealthy = catalog is not null;
        catalogFeedback.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted");
        AutomationProperties.SetLiveSetting(catalogFeedback, AutomationLiveSetting.Polite);
        catalogRooms = catalog?.Rooms.Where(room => room.Life == LocalRoomLife.Active).ToDictionary(room => room.RoomId.ToString("D")) ?? [];
        var active = catalogRooms.Values.ToArray(); var doors = new List<PreviewDoor>();
        for (var i = 0; i < 3; i++) doors.Add(i < active.Length ?
            new(active[i].RoomId.ToString("D"), active[i].Name, active[i].CanEditOffline ? "本地单人" : "家具只读", 560 + i * 220) :
            new("new-room-" + i, catalogHealthy ? "新建普通房" : "目录待核对", "空门位", 560 + i * 220));
        doors.Add(new("local-desktop-pet", "桌宠房", "桌宠房", 1220));
        doors.Add(catalogHealthy ? new("room-archive", "已解散房间", "恢复", 1440) : new("room-retry", "重新读取目录", "恢复", 1440));
        catalogDoors = doors.ToArray(); selectedDoor = selection?.ToString("D"); status = message; actionDoorId = null;
        catalogControls.Children.Clear(); catalogActions.Children.Clear();
        foreach (var door in catalogDoors)
        {
            var button = new Button { Tag = door.Id, Background = Brushes.Transparent, BorderThickness = new(0), Style = new(typeof(Button)), Focusable = true };
            AutomationProperties.SetName(button, door.Name);
            button.ToolTip = door.Name + " · 走近后激活";
            button.Click += (_, e) => { e.Handled = true; SelectPreviewDoor(door); };
            button.GotKeyboardFocus += (_, _) => { selectedDoor = door.Id; InvalidateVisual(); };
            catalogControls.Children.Add(button);
        }
        AutomationProperties.SetName(this, "门廊场景，三个普通房门位、桌宠房及恢复标牌");
        UpdateCatalogControls(); InvalidateVisual();
    }
    internal void SetCatalogStatus(string? message) { status = message; UpdateCatalogControls(); InvalidateVisual(); }
    private bool ActivateCatalogDoor(PreviewDoor door)
    {
        if (unavailable) return false;
        GameplayFocusRequested?.Invoke();
        if (door.Id == "local-desktop-pet") { DesktopPetRoomRequested?.Invoke(); return true; }
        if (door.Id == "room-retry") { CatalogReloadRequested?.Invoke(); return true; }
        if (!catalogHealthy) { SetCatalogStatus("目录受保护，请先重新读取；桌宠仍可使用。"); return false; }
        if (door.Id == "room-archive") CatalogRecoveryRequested?.Invoke();
        else if (door.Kind == "空门位") CatalogCreateRequested?.Invoke();
        else if (catalogRooms.TryGetValue(door.Id, out var room))
        { DoorSelected?.Invoke(door.Id); CatalogRoomRequested?.Invoke(room.RoomId); }
        UpdateCatalogControls(); InvalidateVisual(); return true;
    }
    private bool RequestCatalogFurniture()
    {
        if (catalogInputBlocked || unavailable || NearbyDoorId is not { } id || !catalogRooms.TryGetValue(id, out var room)) return false;
        if (!room.CanEditOffline) { SetCatalogStatus("离线缓存布局只读，可单人游玩。"); return false; }
        selectedDoor = id; CatalogFurnitureRequested?.Invoke(room.RoomId); return true;
    }
    private void UpdateCatalogControls()
    {
        if (!catalogProvided) return;
        catalogFeedback.Text = status;
        catalogFeedback.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        foreach (Button button in catalogControls.Children)
        {
            var door = catalogDoors.Single(value => value.Id == (string)button.Tag);
            var rect = DoorRect(door);
            button.Width = rect.Width; button.Height = rect.Height;
            Canvas.SetLeft(button, rect.Left); Canvas.SetTop(button, rect.Top);
            button.IsEnabled = !catalogInputBlocked && (catalogHealthy || door.Id is "local-desktop-pet" or "room-retry");
        }
        var nearby = NearbyDoorId;
        if (nearby is null || !catalogRooms.TryGetValue(nearby, out var room) || unavailable || catalogInputBlocked)
        { catalogActions.Visibility = Visibility.Collapsed; return; }
        if (actionDoorId != nearby)
        {
            actionDoorId = nearby; catalogActions.Children.Clear();
            AddAction("↗ 进入", "进入" + room.Name, () => CatalogRoomRequested?.Invoke(room.RoomId));
            AddAction("◇ 布置", "布置" + room.Name, () => CatalogFurnitureRequested?.Invoke(room.RoomId), room.CanEditOffline);
            AddAction("× 解散", "解散" + room.Name, () => CatalogDissolveRequested?.Invoke(room.RoomId), room.Source == LocalRoomSource.LocalOnly);
        }
        catalogActions.Visibility = Visibility.Visible;
        catalogActions.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        catalogActions.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        var target = DoorRect(catalogDoors.Single(door => door.Id == nearby));
        catalogActions.Margin = new(Math.Clamp(target.Left + target.Width / 2 - 121, 8, Math.Max(8, ActualWidth - 250)), target.Bottom + 8, 0, 0);
    }
    private void AddAction(string label, string name, Action action, bool enabled = true)
    {
        var button = new Button { Content = label, MinHeight = 40, Padding = new(8, 6, 8, 6), Margin = new(0, 0, 6, 0), IsEnabled = enabled };
        button.ToolTip = enabled ? name : "离线不可执行，需要联网确认资格";
        AutomationProperties.SetName(button, name);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (!catalogInputBlocked && actionDoorId == NearbyDoorId && !unavailable) action();
        };
        catalogActions.Children.Add(button);
    }
    internal void FocusCatalogOrigin()
    {
        var id = selectedDoor ?? NearbyDoorId;
        var button = catalogControls.Children.Cast<Button>().FirstOrDefault(value => (string)value.Tag == id);
        if (button?.IsVisible == true) button.Focus(); else GameplayFocusRequested?.Invoke();
    }
}
