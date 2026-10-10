using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Protocol;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using PasswordBox = System.Windows.Controls.PasswordBox;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum OnlineRoomPage { Status, ConfirmEnter, ResolveCode, ConfirmLeave, ConfirmResetCode, Code }
public enum OnlineRoomAction
{
    Refresh, OpenResolve, Resolve, OpenEnter, Enter, Control, OpenLeave, Leave, EndLease,
    OpenResetCode, ResetCode, RetryPending, Back, Directory, Account, Close
}
public sealed record OnlineRoomRequest(OnlineRoomAction Action, Guid? RoomId = null);
public sealed record OnlineRoomPanelState(OnlineRoomPage Page = OnlineRoomPage.Status,
    string Status = "尚未读取服务器状态。", RoomDoorView? Target = null,
    PersonalRoomSnapshot? Personal = null, SharedRoomSnapshot? Shared = null,
    bool Busy = false, bool ConfigurationReady = false, bool OwnsTarget = false, bool Controller = false,
    bool HasLease = false, bool Connected = false, bool Pending = false, bool RequiresReconciliation = false, string? Message = null,
    RoomCodeResult? Code = null);

/// <summary>Shows server facts; it never claims that a playable shared scene is ready.</summary>
public sealed class OnlineRoomPanelView : UserControl
{
    private readonly StackPanel body = new() { Margin = new Thickness(18) };
    private readonly PasswordBox codeInput = new() { Name = "OnlineRoomCodeInput", MaxLength = 43, MinHeight = 36 };
    private readonly TextBox codeOutput = new() { Name = "OnlineRoomCodeOutput", IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap, MinHeight = 36 };
    private readonly List<string> names = [];
    private OnlineRoomPanelState state = new();

    public OnlineRoomPanelView()
    {
        MaxWidth = 560; FontSize = 14;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
        HorizontalAlignment = HorizontalAlignment.Stretch; UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "HomeInk");
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        NameScope.SetNameScope(this, new NameScope());
        RegisterName(codeInput.Name, codeInput); RegisterName(codeOutput.Name, codeOutput);
        AutomationProperties.SetName(codeInput, "公共房访问码");
        AutomationProperties.SetName(codeOutput, "本次生成的公共房访问码");
        foreach (var input in new Control[] { codeInput, codeOutput })
        {
            input.SetResourceReference(Control.BackgroundProperty, "HomeSoft");
            input.SetResourceReference(Control.ForegroundProperty, "HomeInk");
            input.SetResourceReference(Control.BorderBrushProperty, "HomeLine");
        }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true; if (!e.IsRepeat) Requested?.Invoke(new(OnlineRoomAction.Close));
        };
        AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(StopGameplayKeys), true);
        AddHandler(Keyboard.KeyUpEvent, new KeyEventHandler(StopGameplayKeys), true);
        Content = new Border { CornerRadius = new(12), BorderThickness = new(1), Child =
            new ScrollViewer { Name = "OnlineRoomScroll", Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        ((Border)Content).SetResourceReference(Border.BackgroundProperty, "HomeSoft");
        ((Border)Content).SetResourceReference(Border.BorderBrushProperty, "HomeLine");
        RegisterName("OnlineRoomScroll", ((Border)Content).Child);
        Apply(state);
    }

    public event Action<OnlineRoomRequest>? Requested;
    internal event Action<OnlineRoomAction>? SceneRequested;
    internal void RequestFromScene(OnlineRoomAction action) => SceneRequested?.Invoke(action);
    public string AccessCode => codeInput.Password.Trim();
    public void ClearSecrets()
    { codeInput.Clear(); codeOutput.Clear(); state = state with { Code = null }; }

    public void Apply(OnlineRoomPanelState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!Enum.IsDefined(next.Page)) throw new ArgumentOutOfRangeException(nameof(next));
        if (next.Page != OnlineRoomPage.ResolveCode) codeInput.Clear();
        if (next.Page != OnlineRoomPage.Code) codeOutput.Clear();
        state = next;
        body.Children.Clear();
        foreach (var name in names) UnregisterName(name);
        names.Clear(); codeInput.IsEnabled = !next.Busy;
        Note(next.Page switch
        {
            OnlineRoomPage.ConfirmEnter => "确认在线入房", OnlineRoomPage.ResolveCode => "通过公共码找到房间",
            OnlineRoomPage.ConfirmLeave => "确认正式离房", OnlineRoomPage.ConfirmResetCode => "确认重置公共访问码",
            OnlineRoomPage.Code => "本次公共访问码", _ => "在线连接与成员"
        }, title: true);
        Button("关闭", OnlineRoomAction.Close);
        Note(next.Message ?? next.Status);
        if (next.RequiresReconciliation) Note("操作已经确认，当前状态尚未读取完成。请先读取当前状态，再申请入房、接管或离房。");
        if (next.Pending)
        {
            Note("上次操作结果尚未确认。保留了原请求；请先核对，不要重新提交另一笔操作。");
            Button("核对保留的请求", OnlineRoomAction.RetryPending);
        }
        else switch (next.Page)
        {
            case OnlineRoomPage.Status: Status(); break;
            case OnlineRoomPage.ConfirmEnter:
                Target();
                Note("服务器会检查权限、容量与当前归属，并为本设备申请控制租约。连接成功后可查看成员；多人场景操作尚未接入。");
                Button("确认申请入房", OnlineRoomAction.Enter, next.Target?.RoomId);
                Button("取消", OnlineRoomAction.Back); break;
            case OnlineRoomPage.ResolveCode:
                Note("粘贴房主提供的公共访问码。找到房门后还需要明确确认入房；解析访问码不会入房。");
                body.Children.Add(codeInput); Button("读取房门", OnlineRoomAction.Resolve);
                Button("返回状态", OnlineRoomAction.Back); break;
            case OnlineRoomPage.ConfirmLeave:
                Note("正式离房会清除账号团子在当前房间的归属，并结束其所有设备的租约。仅控制设备可以请求；关闭浮层只暂停本机连接。");
                Button("确认正式离房", OnlineRoomAction.Leave);
                Button("取消", OnlineRoomAction.Back); break;
            case OnlineRoomPage.ConfirmResetCode:
                Target(); Note("重置后旧公共访问码立即失效。新码有效期为 24 小时，请只分享给希望邀请的人。");
                Button("确认重置访问码", OnlineRoomAction.ResetCode, next.Target?.RoomId);
                Button("取消", OnlineRoomAction.Back); break;
            case OnlineRoomPage.Code:
                if (next.Code is { } code)
                {
                    codeOutput.Text = code.Code; body.Children.Add(codeOutput);
                    Note($"本次回执的有效期至 {code.ExpiresAt.ToLocalTime():yyyy-MM-dd HH:mm}。服务器入房时仍会检查是否已重置或撤销。此码仅在本次浮层显示；关闭或返回后隐藏。");
                }
                Button("隐藏并返回", OnlineRoomAction.Back); break;
        }
        Button("返回门廊房门", OnlineRoomAction.Directory);
        Button("账号与外观", OnlineRoomAction.Account);
    }

    private void Status()
    {
        if (state.Personal is { } personal)
            Note(personal.RoomId is null ? "服务器当前归属：未在房间。" :
                $"服务器当前归属：{Kind(personal.RoomKind)} · {(personal.IsSleeping ? "离线睡眠" : "在线")}");
        Note(state.Connected ? state.Controller ? "本设备连接有效 · 控制设备" : "本设备连接有效 · 观察设备" : "本设备尚无有效在线连接。");
        Note("成员信息来自服务器快照。多人场景操作尚未接入，本地单人可继续游玩。");
        if (state.Shared is { } shared)
        {
            Note($"{shared.Name} · {shared.Occupants.Length}/{shared.Capacity} 位团子");
            foreach (var occupant in shared.Occupants)
                Note($"{(occupant.PetId == state.Personal?.PetId ? "我的团子" : "团子 " + occupant.PetId.ToString("N")[..8])} · {(occupant.IsSleeping ? "睡眠" : "在线")}");
        }
        Target();
        Button("读取当前状态／恢复连接", OnlineRoomAction.Refresh);
        if (state.Target is not null) Button("申请进入此房…", OnlineRoomAction.OpenEnter, state.Target.RoomId);
        if (!state.ConfigurationReady) Note("申请入房前请到账号页明确选择外观来源。房间管理和本地单人仍可使用。");
        Button("使用公共访问码", OnlineRoomAction.OpenResolve);
        if (state.HasLease)
        {
            if (!state.Controller) Button("申请接管控制", OnlineRoomAction.Control);
            if (state.Controller) Button("正式离房…", OnlineRoomAction.OpenLeave);
            Button("结束本设备租约", OnlineRoomAction.EndLease);
        }
        if (state.OwnsTarget && state.Target?.Kind == "publicShared") Button("重置公共访问码…", OnlineRoomAction.OpenResetCode, state.Target.RoomId);
    }

    private void Target()
    {
        if (state.Target is { } room) Note($"目标房间：{room.Name} · {Kind(room.Kind)} · 容量 {room.Capacity}");
    }
    private bool Allowed(OnlineRoomRequest request)
    {
        if (request.Action == OnlineRoomAction.Close) return true;
        if (state.Busy) return false;
        if (state.Pending) return request.Action is OnlineRoomAction.RetryPending or OnlineRoomAction.Directory or OnlineRoomAction.Account;
        var target = request.RoomId is { } id && state.Target?.RoomId == id;
        return request.Action switch
        {
            OnlineRoomAction.Directory or OnlineRoomAction.Account => true,
            OnlineRoomAction.Refresh or OnlineRoomAction.OpenResolve => state.Page == OnlineRoomPage.Status,
            OnlineRoomAction.OpenEnter => !state.RequiresReconciliation && state.Page == OnlineRoomPage.Status && target && state.ConfigurationReady,
            OnlineRoomAction.Enter => !state.RequiresReconciliation && state.Page == OnlineRoomPage.ConfirmEnter && target && state.ConfigurationReady,
            OnlineRoomAction.Resolve => state.Page == OnlineRoomPage.ResolveCode,
            OnlineRoomAction.Control => !state.RequiresReconciliation && state.Page == OnlineRoomPage.Status && state.HasLease && !state.Controller,
            OnlineRoomAction.OpenLeave => !state.RequiresReconciliation && state.Page == OnlineRoomPage.Status && state.HasLease && state.Controller,
            OnlineRoomAction.Leave => !state.RequiresReconciliation && state.Page == OnlineRoomPage.ConfirmLeave && state.HasLease && state.Controller,
            OnlineRoomAction.EndLease => !state.RequiresReconciliation && state.Page == OnlineRoomPage.Status && state.HasLease,
            OnlineRoomAction.OpenResetCode => state.Page == OnlineRoomPage.Status && state.OwnsTarget && target && state.Target?.Kind == "publicShared",
            OnlineRoomAction.ResetCode => state.Page == OnlineRoomPage.ConfirmResetCode && state.OwnsTarget && target && state.Target?.Kind == "publicShared",
            OnlineRoomAction.Back => state.Page != OnlineRoomPage.Status,
            _ => false
        };
    }
    private void Button(string text, OnlineRoomAction action, Guid? roomId = null)
    {
        var request = new OnlineRoomRequest(action, roomId);
        var name = "OnlineRoom" + action;
        var button = new Button { Name = name, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            MinHeight = 36, Margin = new(0, 8, 0, 0), Padding = new(8, 6, 8, 6), IsEnabled = Allowed(request) };
        AutomationProperties.SetName(button, text);
        button.SetResourceReference(Control.BorderBrushProperty, "HomeLine");
        button.Click += (_, _) => { if (Allowed(request)) Requested?.Invoke(request); };
        RegisterName(name, button); names.Add(name); body.Children.Add(button);
    }
    private void Note(string text, bool title = false)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = title ? 22 : 14,
            Margin = new(0, 6, 0, 6) };
        note.SetResourceReference(TextBlock.ForegroundProperty, title ? "HomeInk" : "HomeMuted");
        body.Children.Add(note);
    }
    private static string Kind(string? kind) => kind switch
    { "desktopPet" => "桌宠房间", "privateShared" => "私密共同房", "publicShared" => "公共共同房", _ => "房间" };
    private static void StopGameplayKeys(object sender, System.Windows.Input.KeyEventArgs e)
    { if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Space) e.Handled = true; }
}
