using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Protocol;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum RoomPanelPage { Directory, Create, Detail, Edit, ConfirmDissolve, Pending, Conflict }
public enum RoomPanelAction
{
    Refresh, ActiveDirectory, DissolvedDirectory, NextPage, SelectRoom, OpenCreate, Create, OpenEdit,
    Update, OpenDissolve, Dissolve, Restore, RetryPending, RefreshConflict, Back, Close, OpenAccount
}
public sealed record RoomPanelRequest(RoomPanelAction Action, Guid? RoomId = null);
public sealed record RoomPanelState(RoomPanelPage Page, string DirectoryState = "active",
    RoomManagementView[]? Rooms = null, Guid? Next = null, RoomManagementView? Selected = null,
    string? Message = null, bool Busy = false);

/// <summary>Own-room management presentation only. The host performs validation, requests, and reconciliation.</summary>
public sealed class RoomPanelView : UserControl
{
    private readonly StackPanel body = new() { Margin = new Thickness(18) };
    private readonly TextBox nameInput = Input("RoomName", "房间名称", 128);
    private readonly TextBox capacityInput = Input("RoomCapacity", "容量", 3);
    private readonly ComboBox kindInput = new() { Name = "RoomKind", MinHeight = 36, Margin = new Thickness(0) };
    private readonly Button primary = new() { Name = "RoomPrimary", MinHeight = 40, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button close = new() { Name = "RoomClose", Content = "关闭", MinHeight = 36, Padding = new Thickness(8, 4, 8, 4) };
    private readonly TextBlock message = Text(string.Empty);
    private readonly List<string> transientNames = [];
    private RoomPanelState state = new(RoomPanelPage.Directory);
    private RoomPanelRequest? primaryRequest;
    private RoomPanelPage? draftPage;
    private Guid? editDraftId;
    private string editDraftKind = "privateShared";

    public RoomPanelView()
    {
        MaxWidth = 560;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
        FontSize = 14;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "HomeInk");
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.Cycle);
        NameScope.SetNameScope(this, new NameScope());
        foreach (var field in new FrameworkElement[] { nameInput, capacityInput, kindInput, primary, close }) RegisterName(field.Name, field);
        message.Name = "RoomMessage"; RegisterName(message.Name, message);
        message.SetResourceReference(TextBlock.ForegroundProperty, "HomeInk");
        kindInput.Items.Add(new ComboBoxItem { Content = "私密共同房", Tag = "privateShared" });
        kindInput.Items.Add(new ComboBoxItem { Content = "公共共同房", Tag = "publicShared" });
        kindInput.SelectedIndex = 0;
        AutomationProperties.SetName(kindInput, "房间类型");
        kindInput.SetResourceReference(ComboBox.ForegroundProperty, "HomeInk");
        kindInput.SetResourceReference(ComboBox.BackgroundProperty, "HomeSoft");
        kindInput.SetResourceReference(ComboBox.BorderBrushProperty, "HomeLine");
        primary.SetResourceReference(Button.BackgroundProperty, "HomeSoft");
        primary.SetResourceReference(Button.BorderBrushProperty, "HomeAccent");
        primary.Click += (_, _) => { if (primary.IsEnabled && primaryRequest is { } request) Request(request); };
        close.Click += (_, _) => Request(new(RoomPanelAction.Close));
        AutomationProperties.SetName(close, "关闭房间管理");
        PreviewKeyDown += OnPreviewKeyDown;
        AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnGameplayKey), handledEventsToo: true);
        AddHandler(Keyboard.KeyUpEvent, new KeyEventHandler(OnGameplayKey), handledEventsToo: true);
        var scroll = new ScrollViewer { Name = "RoomScroll", Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        RegisterName(scroll.Name, scroll);
        var surface = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = scroll };
        surface.SetResourceReference(Border.BackgroundProperty, "HomeSoft");
        surface.SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        Content = surface;
        Apply(state);
    }

    public event Action<RoomPanelRequest>? Requested;
    public string RoomName => nameInput.Text.Trim();
    public string CapacityText => capacityInput.Text.Trim();
    public string Kind => draftPage == RoomPanelPage.Edit ? editDraftKind : (kindInput.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    public void Apply(RoomPanelState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!Enum.IsDefined(next.Page)) throw new ArgumentOutOfRangeException(nameof(next));
        var changedPage = next.Page != state.Page;
        PrepareDraft(next);
        state = next;
        if (close.Parent is System.Windows.Controls.Panel previousHeader) previousHeader.Children.Remove(close);
        body.Children.Clear();
        foreach (var fieldName in transientNames) UnregisterName(fieldName);
        transientNames.Clear();
        primaryRequest = null;
        primary.IsEnabled = false;
        nameInput.IsEnabled = !state.Busy;
        capacityInput.IsEnabled = !state.Busy && !(state.Page == RoomPanelPage.Edit && state.Selected?.Kind == "desktopPet");
        kindInput.IsEnabled = !state.Busy && state.Page == RoomPanelPage.Create;
        close.IsEnabled = true;
        AddHeader();
        message.Text = state.Message ?? (state.Busy ? "正在处理，请稍候…" : string.Empty);
        message.Visibility = string.IsNullOrEmpty(message.Text) ? Visibility.Collapsed : Visibility.Visible;
        body.Children.Add(message);
        switch (state.Page)
        {
            case RoomPanelPage.Directory: AddDirectory(); break;
            case RoomPanelPage.Create: AddForm(create: true); break;
            case RoomPanelPage.Edit: AddForm(create: false); break;
            case RoomPanelPage.Detail: AddDetail(); break;
            case RoomPanelPage.ConfirmDissolve: AddDissolveConfirmation(); break;
            case RoomPanelPage.Pending:
                AddNote("上次请求的结果尚未确认。仅重试保留的同一请求；不要重新创建或覆盖数据。");
                AddPrimary("重试保留的请求", new(RoomPanelAction.RetryPending));
                break;
            case RoomPanelPage.Conflict:
                AddNote("服务器数据已经变化。先读取最新状态，再决定是否重新提交；当前表单输入仍保留。");
                AddPrimary("读取最新状态", new(RoomPanelAction.RefreshConflict));
                AddButton("返回", new(RoomPanelAction.Back));
                break;
        }
        if (changedPage) ((ScrollViewer)FindName("RoomScroll")).ScrollToTop();
    }

    private void PrepareDraft(RoomPanelState next)
    {
        if (next.Page is RoomPanelPage.Directory or RoomPanelPage.Detail) draftPage = null;
        if (next.Page == RoomPanelPage.Create && draftPage != RoomPanelPage.Create)
        {
            nameInput.Text = string.Empty; capacityInput.Text = "6"; kindInput.SelectedIndex = 0;
            draftPage = RoomPanelPage.Create; editDraftId = null;
        }
        if (next.Page == RoomPanelPage.Edit && (draftPage != RoomPanelPage.Edit || editDraftId != next.Selected?.RoomId))
        {
            nameInput.Text = next.Selected?.Name ?? string.Empty;
            capacityInput.Text = next.Selected?.Capacity.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            editDraftKind = next.Selected?.Kind ?? string.Empty;
            kindInput.SelectedIndex = editDraftKind switch { "privateShared" => 0, "publicShared" => 1, _ => -1 };
            draftPage = RoomPanelPage.Edit; editDraftId = next.Selected?.RoomId;
        }
    }

    private void AddHeader()
    {
        var title = state.Page switch
        {
            RoomPanelPage.Directory => state.DirectoryState == "dissolved" ? "已解散的房间" : "我的在线房间",
            RoomPanelPage.Create => "创建共同房", RoomPanelPage.Detail => "房间详情", RoomPanelPage.Edit => "修改房间",
            RoomPanelPage.ConfirmDissolve => "确认解散", RoomPanelPage.Pending => "请求结果待确认", RoomPanelPage.Conflict => "数据有冲突", _ => "房间管理"
        };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Text(title); label.FontSize = 22; label.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(label); close.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(close, 1); header.Children.Add(close); body.Children.Add(header);
    }

    private void AddDirectory()
    {
        AddNote("这里管理本人房间。房间管理不等于进入在线场景；本地单人入口仍可使用。");
        AddButton("当前房间", new(RoomPanelAction.ActiveDirectory));
        AddButton("已解散的房间", new(RoomPanelAction.DissolvedDirectory));
        AddPrimary("刷新当前目录", new(RoomPanelAction.Refresh));
        if (state.Rooms is null) AddNote("尚未读取房间目录。");
        else if (state.Rooms.Length == 0) AddNote("本页没有房间。");
        else foreach (var room in state.Rooms)
        {
            var label = $"{room.Name}\n{KindLabel(room.Kind)} · 容量 {room.Capacity} · {StatusLabel(room.Status)}";
            AddButton(label, new(RoomPanelAction.SelectRoom, room.RoomId));
        }
        if (state.Next is not null) AddButton("下一页", new(RoomPanelAction.NextPage));
        if (state.DirectoryState == "active") AddButton("创建共同房", new(RoomPanelAction.OpenCreate));
        AddButton("账号与登录", new(RoomPanelAction.OpenAccount));
    }

    private void AddForm(bool create)
    {
        if (!create && state.Selected is null)
        {
            AddNote("尚未读取房间详情，无法提交修改。"); AddButton("返回", new(RoomPanelAction.Back)); return;
        }
        AddNote(create ? "创建私密或公共共同房，容量为 1–12，默认 6。服务器会检查账号权限与房间额度。"
            : "房间类型不能修改。提交时服务器会检查版本、容量与席位约束。");
        AddField("房间名称", nameInput);
        if (create || state.Selected?.Kind != "desktopPet") AddField("房间类型", kindInput);
        else AddNote("桌宠房间 · 容量固定为 2，可修改名称，不能解散。");
        AddField("容量", capacityInput);
        AddPrimary(create ? "创建房间" : "保存修改", new(create ? RoomPanelAction.Create : RoomPanelAction.Update, create ? null : state.Selected?.RoomId));
        AddButton("返回", new(RoomPanelAction.Back));
    }

    private void AddDetail()
    {
        var room = state.Selected;
        if (room is null) { AddNote("尚未读取房间详情。"); AddButton("返回目录", new(RoomPanelAction.Back)); return; }
        var name = Text(room.Name, new Thickness(0, 8, 0, 8)); name.FontSize = 18; body.Children.Add(name);
        AddNote($"{KindLabel(room.Kind)} · {StatusLabel(room.Status)}\n容量 {room.Capacity} · 当前在房 {room.Occupancy} · 保留席位 {room.ReservedSeats}");
        if (room.Status == "active")
        {
            if (room.Kind == "desktopPet") AddNote("桌宠房间容量固定为 2，可修改名称，不能解散。");
            AddPrimary("修改房间", new(RoomPanelAction.OpenEdit, room.RoomId));
            if (IsShared(room)) AddButton("解散房间…", new(RoomPanelAction.OpenDissolve, room.RoomId));
        }
        else if (room.Status == "dissolved")
        {
            var deadline = room.RecoverUntil is { } until ? until.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "未提供";
            AddNote($"恢复截止时间：{deadline}。恢复时服务器会重新检查 7 天期限与当前额度；不会迁回团子，也不会恢复旧公共码。");
            if (room.Recoverable && IsShared(room)) AddPrimary("恢复房间", new(RoomPanelAction.Restore, room.RoomId));
            else AddNote("该房间当前不可恢复。");
        }
        AddButton("返回目录", new(RoomPanelAction.Back));
    }

    private void AddDissolveConfirmation()
    {
        var room = state.Selected;
        if (room is null || room.Status != "active" || !IsShared(room))
        {
            AddNote("当前房间不能解散。"); AddButton("返回", new(RoomPanelAction.Back)); return;
        }
        AddNote($"确定解散“{room.Name}”吗？");
        AddNote("解散会清除在房团子的当前占位，旧公共访问码立即失效。7 天内可请求恢复，服务器仍会检查期限与额度；恢复不会把团子迁回，也不会复活旧访问码。");
        AddPrimary("确认解散房间", new(RoomPanelAction.Dissolve, room.RoomId));
        AddButton("取消，返回详情", new(RoomPanelAction.Back));
    }

    private void AddPrimary(string label, RoomPanelRequest request)
    {
        primary.Content = Text(label); AutomationProperties.SetName(primary, label);
        primaryRequest = request; primary.IsEnabled = Allowed(request); body.Children.Add(primary);
    }

    private void AddButton(string label, RoomPanelRequest request)
    {
        var baseName = $"Room{request.Action}" + (request.RoomId is { } id ? "_" + id.ToString("N") : string.Empty);
        var fieldName = baseName;
        for (var suffix = 2; transientNames.Contains(fieldName); suffix++) fieldName = baseName + "_" + suffix;
        var button = new Button { Name = fieldName, Content = Text(label), MinHeight = 36,
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6), IsEnabled = Allowed(request) };
        AutomationProperties.SetName(button, label);
        button.SetResourceReference(Button.BorderBrushProperty, "HomeLine");
        button.Click += (_, _) => { if (button.IsEnabled) Request(request); };
        RegisterName(fieldName, button); transientNames.Add(fieldName); body.Children.Add(button);
    }

    private void Request(RoomPanelRequest request)
    {
        if (Allowed(request)) Requested?.Invoke(request);
    }

    private bool Allowed(RoomPanelRequest request)
    {
        if (request.Action == RoomPanelAction.Close) return true;
        if (state.Busy) return false;
        var room = state.Selected;
        var selected = room is not null && room.RoomId == request.RoomId;
        return state.Page switch
        {
            RoomPanelPage.Directory => request.Action switch
            {
                RoomPanelAction.Refresh or RoomPanelAction.ActiveDirectory or RoomPanelAction.DissolvedDirectory or RoomPanelAction.OpenAccount => true,
                RoomPanelAction.OpenCreate => state.DirectoryState == "active",
                RoomPanelAction.NextPage => state.Next is not null,
                RoomPanelAction.SelectRoom => state.Rooms?.Any(r => r.RoomId == request.RoomId) == true,
                _ => false
            },
            RoomPanelPage.Create => request.Action is RoomPanelAction.Create or RoomPanelAction.Back,
            RoomPanelPage.Edit => request.Action == RoomPanelAction.Back || request.Action == RoomPanelAction.Update && selected && room!.Status == "active" && IsKnownKind(room.Kind),
            RoomPanelPage.Detail => request.Action == RoomPanelAction.Back || selected && (request.Action switch
            {
                RoomPanelAction.OpenEdit => room!.Status == "active" && IsKnownKind(room.Kind),
                RoomPanelAction.OpenDissolve => room!.Status == "active" && IsShared(room),
                RoomPanelAction.Restore => room!.Status == "dissolved" && room.Recoverable && IsShared(room),
                _ => false
            }),
            RoomPanelPage.ConfirmDissolve => request.Action == RoomPanelAction.Back || request.Action == RoomPanelAction.Dissolve && selected && room!.Status == "active" && IsShared(room),
            RoomPanelPage.Pending => request.Action == RoomPanelAction.RetryPending,
            RoomPanelPage.Conflict => request.Action is RoomPanelAction.RefreshConflict or RoomPanelAction.Back,
            _ => false
        };
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (!e.IsRepeat) Request(new(RoomPanelAction.Close));
    }

    private static void OnGameplayKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Bubbling runs after text/button handling, so typing and Space activation remain available.
        if (e.Key is Key.W or Key.A or Key.D or Key.S or Key.Space) e.Handled = true;
    }

    private void AddField(string label, Control field)
    {
        body.Children.Add(Text(label, new Thickness(0, 12, 0, 5))); body.Children.Add(field);
    }
    private void AddNote(string value)
    {
        var note = Text(value, new Thickness(0, 8, 0, 8)); note.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted"); body.Children.Add(note);
    }
    private static TextBlock Text(string value, Thickness? margin = null) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
    private static bool IsShared(RoomManagementView room) => room.Kind is "privateShared" or "publicShared";
    private static bool IsKnownKind(string value) => value is "desktopPet" or "privateShared" or "publicShared";
    private static string KindLabel(string value) => value switch { "desktopPet" => "桌宠房间", "privateShared" => "私密共同房", "publicShared" => "公共共同房", _ => "未知类型" };
    private static string StatusLabel(string value) => value switch { "active" => "可用", "dissolved" => "已解散", "expired" => "恢复已过期", _ => "未知状态" };
    private static TextBox Input(string fieldName, string label, int maximum)
    {
        var field = new TextBox { Name = fieldName, MaxLength = maximum, MinHeight = 36,
            Padding = new Thickness(8, 6, 8, 6), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(field, label);
        field.SetResourceReference(TextBox.ForegroundProperty, "HomeInk");
        field.SetResourceReference(TextBox.BackgroundProperty, "HomeSoft");
        field.SetResourceReference(TextBox.BorderBrushProperty, "HomeLine");
        return field;
    }
}
