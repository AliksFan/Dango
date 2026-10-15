using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.AppShell.Rooms.Storage;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using StackPanel = System.Windows.Controls.StackPanel;
using Grid = System.Windows.Controls.Grid;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using SystemColors = System.Windows.SystemColors;
using Color = System.Windows.Media.Color;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private LocalRoomCatalogStore? roomCatalogStore;
    private LocalRoomCatalog? roomCatalog;
    private Border? roomDialog;
    private StackPanel? roomDialogBody, roomDialogActions;
    private TextBlock? roomDialogError;
    private bool roomCommandBusy;
    private bool roomCatalogRefreshing;
    internal bool IsRoomDialogOpen => roomDialog is not null;
    internal event Action<Guid, bool>? CatalogRoomRequested;
    internal LocalRoomCatalog? CatalogSnapshot => roomCatalog;
    internal Border? RoomDialog => roomDialog;
    internal bool RoomEntryPending { get; set; }
    private void InitializeRoomCatalogUi()
    {
        PorchScene.CatalogRoomRequested += id => CatalogRoomRequested?.Invoke(id, false);
        PorchScene.CatalogFurnitureRequested += id => CatalogRoomRequested?.Invoke(id, true);
        PorchScene.CatalogCreateRequested += OpenCreateRoom;
        PorchScene.CatalogRecoveryRequested += OpenRoomRecovery;
        PorchScene.CatalogDissolveRequested += OpenDissolveRoom;
        PorchScene.CatalogReloadRequested += async () => await RecheckRoomCatalogAsync();
    }
    internal async Task BindRoomCatalogAsync(LocalRoomCatalogStore store)
    {
        roomCatalogStore = store;
        PorchScene.SetCatalog(null, message: "正在读取本地房间目录…");
        await RecheckRoomCatalogAsync();
    }
    private async Task RecheckRoomCatalogAsync()
    {
        if (roomCatalogStore is null || roomCatalogRefreshing) return;
        roomCatalogRefreshing = true;
        PorchScene.CatalogInputBlocked = true;
        try
        {
            var loaded = await Task.Run(() =>
            {
                var result = roomCatalogStore.Refresh(); Guid? selection = null; string? warning = null;
                if (result.Success) selection = new RoomResumeStore(roomCatalogStore).LoadSelection(out warning);
                return (Result: result, Selection: selection, Warning: warning);
            }).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                roomCatalog = loaded.Result.Success ? loaded.Result.Catalog : null;
                PorchScene.SetCatalog(roomCatalog, loaded.Selection, loaded.Result.Warning ?? loaded.Warning);
                if (!loaded.Result.Success) SetNotice("房间目录受保护 · 查看", loaded.Result.Warning);
            });
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { await Dispatcher.InvokeAsync(() => { roomCatalog = null; PorchScene.SetCatalog(null, message: "目录暂不可读，原件保留。请从标牌重新读取。"); }); }
        finally
        {
            if (!Dispatcher.HasShutdownStarted) await Dispatcher.InvokeAsync(() =>
            { roomCatalogRefreshing = false; PorchScene.CatalogInputBlocked = IsRoomDialogOpen || RoomEntryPending; });
        }
    }
    private void OpenRoomDialog(string title, bool showHeading = true)
    {
        if (roomDialog is not null || !IsInPorch || RoomEntryPending) return;
        ClearLiveDirectionalInput();
        if (OwnsLiveFrameDriver) BoundRuntime?.CancelInput();
        PorchScene.CatalogInputBlocked = true;
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (showHeading)
            grid.Children.Add(new TextBlock { Text = title, FontSize = 16, Margin = new(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap });
        roomDialogBody = new() { Margin = new(0, 0, 0, 8) };
        var scroll = new ScrollViewer
        {
            Content = roomDialogBody,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(110, ActualHeight - 260)
        };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        roomDialogError = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6), MinHeight = 22 };
        AutomationProperties.SetLiveSetting(roomDialogError, AutomationLiveSetting.Polite);
        Grid.SetRow(roomDialogError, 2); grid.Children.Add(roomDialogError);
        roomDialogActions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(roomDialogActions, 3); grid.Children.Add(roomDialogActions);
        var card = new Border
        {
            Child = grid,
            Padding = new(22),
            Margin = new(20),
            MaxWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new(1),
            CornerRadius = new(9)
        };
        card.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Dango.Desktop;component/AppShell/Rooms/RoomCatalogDialogResources.xaml", UriKind.Relative) });
        card.SetResourceReference(BackgroundProperty, "HomeBackground"); card.SetResourceReference(BorderBrushProperty, "HomeLine");
        AutomationProperties.SetName(card, title);
        roomDialog = new Border { Child = card, Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(75, 68, 61, 52)) };
        roomDialog.MouseDown += (_, e) => e.Handled = true;
        PorchContent.Children.Add(roomDialog);
        AddDialogAction("× 取消", CloseRoomDialog);
    }
    internal void CloseRoomDialog()
    {
        if (roomCommandBusy || roomDialog is null) return;
        PorchContent.Children.Remove(roomDialog); roomDialog = null;
        roomDialogBody = roomDialogActions = null; roomDialogError = null;
        PorchScene.CatalogInputBlocked = false; ClearLiveDirectionalInput(); PorchScene.FocusCatalogOrigin();
    }
    private Button AddDialogAction(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new(8, 0, 0, 0), MinHeight = 40 };
        AutomationProperties.SetName(button, label);
        button.Click += (_, e) => { e.Handled = true; if (!roomCommandBusy) action(); };
        roomDialogActions!.Children.Add(button); return button;
    }
    private void AddRoomText(string text) => roomDialogBody!.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
    private void OpenCreateRoom()
    {
        if (roomCatalog is null || roomCatalog.ActiveCount >= 3 || IsRoomDialogOpen) return;
        OpenRoomDialog("新建普通房", showHeading: false); if (roomDialog is null) return;
        var name = new TextBox { Text = "普通房 " + (roomCatalog.Rooms.Length + 1), MaxLength = 64, MinHeight = 36, Margin = new(0, 0, 0, 12) };
        AddRoomText("名称"); AutomationProperties.SetName(name, "房间名称"); roomDialogBody!.Children.Add(name);
        var kind = new ComboBox { ItemsSource = new[] { "私密", "公共" }, SelectedIndex = 0, MinHeight = 36, Margin = new(0, 0, 0, 12) };
        AddRoomText("类型"); AutomationProperties.SetName(kind, "房间类型"); roomDialogBody.Children.Add(kind);
        var capacity = new TextBox { Text = "6", MinHeight = 36, Margin = new(0, 0, 0, 12) };
        AddRoomText("容量 · 1–12"); AutomationProperties.SetName(capacity, "房间容量"); roomDialogBody.Children.Add(capacity);
        AddRoomText("仅创建到本地，尚未开放他人进入。");
        var operation = Guid.NewGuid(); var id = Guid.NewGuid();
        AddDialogAction("＋ 创建", async () =>
        {
            if (!int.TryParse(capacity.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 12 || string.IsNullOrWhiteSpace(name.Text))
            { roomDialogError!.Text = "请填写名称及1–12之间的整数容量。"; return; }
            var title = name.Text.Trim(); var type = kind.SelectedIndex == 1 ? LocalRoomKind.Public : LocalRoomKind.Private;
            await RunRoomCommandAsync(() => roomCatalogStore!.Create(operation, id, title, type, count), id, "已创建到本地，请主动进入房间。");
        });
        name.Focus(); name.SelectAll();
    }
    private void OpenDissolveRoom(Guid id)
    {
        if (roomCatalog?.Find(id) is not { Life: LocalRoomLife.Active, Source: LocalRoomSource.LocalOnly } room || IsRoomDialogOpen) return;
        OpenRoomDialog("解散「" + room.Name + "」"); if (roomDialog is null) return;
        AddRoomText("解散后立即停止进入和互动，并释放1个普通房名额。7天内可恢复，恢复需有空余名额且不自动入房。到期本机存档仍保留。");
        var operation = Guid.NewGuid();
        AddDialogAction("继续确认", () =>
        {
            roomDialogBody!.Children.Clear(); AddRoomText("再次确认解散「" + room.Name + "」？确认后该房门腾出，存档进入7天常规恢复期。");
            roomDialogActions!.Children.Clear(); AddDialogAction("× 取消", CloseRoomDialog);
            AddDialogAction("确认解散", async () => await RunRoomCommandAsync(() => roomCatalogStore!.Dissolve(operation, id), null, "房间已解散，7天内可从标牌恢复。"));
        });
    }
    private void OpenRoomRecovery()
    {
        if (roomCatalog is null || IsRoomDialogOpen) return;
        OpenRoomDialog("已解散房间"); if (roomDialog is null) return;
        AddRoomText("恢复后保留房间身份与内容，不自动入房。到期只关闭常规恢复，本机存档保留。");
        var dissolved = roomCatalog.Rooms.Where(room => room.Life == LocalRoomLife.Dissolved).ToArray();
        if (dissolved.Length == 0) AddRoomText("没有已解散房间。");
        foreach (var room in dissolved)
        {
            var availability = roomCatalogStore!.RestoreAvailability(roomCatalog, room);
            var text = availability switch
            {
                RoomRestoreAvailability.Expired => "已到期 · 存档保留",
                RoomRestoreAvailability.TimeUncertain => "本机时间异常，请校准",
                RoomRestoreAvailability.RequiresOnline => "需要联网确认",
                _ => $"剩余 {Math.Max(1, (int)Math.Ceiling((room.RestoreUntilUtc!.Value - DateTimeOffset.UtcNow).TotalDays))} 天",
            };
            var row = new DockPanel { Margin = new(0, 4, 0, 14), LastChildFill = true };
            var restore = new Button { Content = "↶ 恢复", MinHeight = 40, IsEnabled = availability is RoomRestoreAvailability.Available or RoomRestoreAvailability.QuotaFull };
            DockPanel.SetDock(restore, Dock.Right); row.Children.Add(restore);
            row.Children.Add(new TextBlock { Text = room.Name + "\n" + text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 12, 0) });
            AutomationProperties.SetName(restore, "恢复" + room.Name);
            var operation = Guid.NewGuid();
            restore.Click += async (_, e) => { e.Handled = true; if (!roomCommandBusy) await RunRoomCommandAsync(() => roomCatalogStore.Restore(operation, room.RoomId), null, "房间已恢复，请主动进入。"); };
            roomDialogBody!.Children.Add(row);
        }
    }
    private async Task RunRoomCommandAsync(Func<RoomCatalogResult> action, Guid? selected, string success)
    {
        if (roomCommandBusy || roomDialog is null) return;
        roomCommandBusy = true; roomDialogBody!.IsEnabled = roomDialogActions!.IsEnabled = false;
        var result = await Task.Run(action).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            roomCommandBusy = false;
            if (result.Success)
            {
                roomCatalog = result.Catalog; PorchScene.SetCatalog(roomCatalog, selected, success); CloseRoomDialog();
            }
            else if (roomDialog is not null)
            {
                roomDialogBody!.IsEnabled = roomDialogActions!.IsEnabled = true;
                roomDialogError!.Text = result.Warning ?? "保存结果待核对。";
                if (!roomDialogActions.Children.OfType<Button>().Any(button => Equals(button.Content, "↶ 重新读取")))
                    AddDialogAction("↶ 重新读取", async () => { CloseRoomDialog(); await RecheckRoomCatalogAsync(); });
            }
        });
    }
}
