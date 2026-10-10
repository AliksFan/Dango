using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private IInputElement? accountReturnFocus;
    internal event Action? AccountRequested;
    internal event Action? AccountPanelClosed;
    internal event Action? RoomDirectoryRequested;
    internal event Action? RoomPanelClosed;
    internal event Action? OnlineRoomPanelClosed;
    private bool porchAccountBound;
    private bool porchSignedIn;
    private bool porchDirectoryInteraction;
    private RoomPanelState porchDirectory = new(RoomPanelPage.Directory);
    internal bool IsAccountPanelOpen => AccountOverlay.Visibility == Visibility.Visible || PorchOperations.Visibility == Visibility.Visible;
    internal AccountPanelView AccountPanel => AccountContent;
    internal RoomPanelView RoomPanel => RoomContent;
    internal OnlineRoomPanelView OnlineRoomPanel => OnlineRoomContent;
    internal bool IsRoomPanelOpen => PorchOperations.Visibility == Visibility.Visible && RoomContent.Visibility == Visibility.Visible;
    internal bool IsOnlineRoomPanelOpen => PorchOperations.Visibility == Visibility.Visible && OnlineRoomContent.Visibility == Visibility.Visible;

    internal void EnablePorchAccountInteractions()
    {
        if (porchAccountBound) return;
        porchAccountBound = true;
        PorchScene.EnableBusinessTargets();
        PorchScene.TargetRequested += OnPorchTargetRequested;
        PorchScene.ProjectionChanged += PositionPorchOperations;
        RoomContent.Applied += ApplyPorchRoomState;
        PorchContent.SizeChanged += (_, _) => PositionPorchOperations();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { RoomPanelClosed?.Invoke(); OnlineRoomPanelClosed?.Invoke(); }
        };
    }

    private void OnPorchTargetRequested(Dango.Desktop.AppShell.Rooms.PorchTargetRequest target)
    {
        ClearLiveDirectionalInput();
        if (!porchSignedIn) { ShowAccountPanel(); return; }
        porchDirectoryInteraction = target.Kind == Dango.Desktop.AppShell.Rooms.PorchTargetKind.Archive;
        switch (target.Kind)
        {
            case Dango.Desktop.AppShell.Rooms.PorchTargetKind.Room:
                ShowRoomPanel(); RoomContent.RequestFromScene(new(RoomPanelAction.SelectRoom, target.RoomId)); break;
            case Dango.Desktop.AppShell.Rooms.PorchTargetKind.Create:
                ShowRoomPanel(); RoomContent.RequestFromScene(new(RoomPanelAction.OpenCreate)); break;
            case Dango.Desktop.AppShell.Rooms.PorchTargetKind.Archive:
                ShowRoomPanel(); RoomContent.RequestFromScene(new(RoomPanelAction.DissolvedDirectory)); break;
            case Dango.Desktop.AppShell.Rooms.PorchTargetKind.Visit:
                ShowOnlineRoomPanel(); OnlineRoomContent.RequestFromScene(OnlineRoomAction.OpenResolve); break;
            case Dango.Desktop.AppShell.Rooms.PorchTargetKind.Online:
                ShowOnlineRoomPanel(); OnlineRoomContent.RequestFromScene(OnlineRoomAction.Refresh); break;
        }
    }

    private void OnPorchEntered()
    {
        if (!porchAccountBound || !porchSignedIn) return;
        porchDirectoryInteraction = false;
        if (RoomDirectoryRequested is { } readDirectory) readDirectory();
        else RoomContent.RequestFromScene(new(RoomPanelAction.ActiveDirectory));
    }

    private void ApplyPorchRoomState(RoomPanelState state)
    {
        if (state.Page == RoomPanelPage.Directory)
        {
            porchDirectory = state;
            PorchScene.ApplyDirectory(state.Rooms, state.DirectoryState, state.Busy, state.Message);
            if (!porchDirectoryInteraction) HideAccountPanel();
        }
        else if (state.Selected is { } selected && porchDirectory.Rooms is { } rooms)
        {
            var updated = rooms.Select(r => r.RoomId == selected.RoomId ? selected : r)
                .Where(r => porchDirectory.DirectoryState == "dissolved" ? r.Status == "dissolved" : r.Status == "active").ToArray();
            porchDirectory = porchDirectory with { Rooms = updated };
            PorchScene.ApplyDirectory(updated, porchDirectory.DirectoryState, state.Busy, state.Message);
        }
        if (state.Page != RoomPanelPage.Directory && IsInPorch && AccountOverlay.Visibility != Visibility.Visible)
        {
            OnlineRoomContent.Visibility = Visibility.Collapsed;
            RoomContent.Visibility = PorchOperations.Visibility = Visibility.Visible;
            PositionPorchOperations();
        }
    }

    private void PositionPorchOperations()
    {
        if (PorchOperations.Visibility != Visibility.Visible) return;
        var width = Math.Min(360, Math.Max(180, PorchContent.ActualWidth - 24));
        var height = Math.Min(300, Math.Max(140, PorchContent.ActualHeight - 96));
        PorchOperations.Width = width; PorchOperations.MaxHeight = height;
        var targetX = PorchScene.InteractionScreenX;
        var preferred = targetX < PorchContent.ActualWidth / 2 ? targetX + 70 * PorchScene.SceneScale : targetX - width - 70 * PorchScene.SceneScale;
        PorchOperations.Margin = new Thickness(Math.Clamp(preferred, 12, Math.Max(12, PorchContent.ActualWidth - width - 12)),
            Math.Clamp(PorchScene.BackWallGroundY - height, 48, Math.Max(48, PorchContent.ActualHeight - height - 36)), 0, 0);
    }

    internal void ShowAccountPanel()
    {
        if (IsOnlineRoomPanelOpen) { OnlineRoomPanelClosed?.Invoke(); HideAccountPanel(); }
        if (IsRoomPanelOpen) { RoomPanelClosed?.Invoke(); HideAccountPanel(); }
        OnAccountClick(this, new RoutedEventArgs());
    }

    internal void ShowRoomPanel()
    {
        if (IsOnlineRoomPanelOpen) { OnlineRoomPanelClosed?.Invoke(); HideAccountPanel(); }
        if (IsAccountPanelOpen && !IsRoomPanelOpen) { AccountPanelClosed?.Invoke(); HideAccountPanel(); }
        if (IsAccountPanelOpen) return;
        accountReturnFocus = Keyboard.FocusedElement;
        ClearLiveDirectionalInput();
        if (!IsInPorch) EnterPorch();
        AccountContent.Visibility = Visibility.Collapsed;
        OnlineRoomContent.Visibility = Visibility.Collapsed;
        AccountContent.ClearSecrets();
        RoomContent.Visibility = PorchOperations.Visibility = Visibility.Visible;
        TopNavigation.IsEnabled = HomeFooter.IsEnabled = false;
        PositionPorchOperations();
        Dispatcher.BeginInvoke(() => { if (IsRoomPanelOpen) RoomContent.MoveFocus(new(FocusNavigationDirection.First)); });
    }

    internal void ShowOnlineRoomPanel()
    {
        if (IsRoomPanelOpen) { RoomPanelClosed?.Invoke(); HideAccountPanel(); }
        if (IsAccountPanelOpen && !IsOnlineRoomPanelOpen) { AccountPanelClosed?.Invoke(); HideAccountPanel(); }
        if (IsAccountPanelOpen) return;
        accountReturnFocus = Keyboard.FocusedElement;
        ClearLiveDirectionalInput(); AccountContent.ClearSecrets();
        if (!IsInPorch) EnterPorch();
        AccountContent.Visibility = RoomContent.Visibility = Visibility.Collapsed;
        OnlineRoomContent.Visibility = PorchOperations.Visibility = Visibility.Visible;
        TopNavigation.IsEnabled = HomeFooter.IsEnabled = false;
        PositionPorchOperations();
        Dispatcher.BeginInvoke(() => { if (IsOnlineRoomPanelOpen) OnlineRoomContent.MoveFocus(new(FocusNavigationDirection.First)); });
    }

    private void OnUnboundAccountAction(AccountPanelAction action)
    {
        if (action == AccountPanelAction.Close && AccountRequested is null) CloseAccountPanel();
    }

    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        if (IsAccountPanelOpen) return;
        accountReturnFocus = Keyboard.FocusedElement;
        ClearLiveDirectionalInput();
        AccountContent.Visibility = Visibility.Visible;
        RoomContent.Visibility = Visibility.Collapsed;
        OnlineRoomContent.Visibility = Visibility.Collapsed;
        AccountOverlay.Visibility = Visibility.Visible;
        TopNavigation.IsEnabled = HomeContent.IsEnabled = SettingsPage.IsEnabled = HomeFooter.IsEnabled = false;
        AccountContent.Apply(new(AccountPanelPage.Login, "账号服务尚未绑定。本地单人仍可使用。", true));
        AccountRequested?.Invoke();
        Dispatcher.BeginInvoke(() => { if (IsAccountPanelOpen) AccountContent.MoveFocus(new(FocusNavigationDirection.First)); });
    }

    internal void HideAccountPanel()
    {
        if (!IsAccountPanelOpen) return;
        AccountOverlay.Visibility = Visibility.Collapsed;
        PorchOperations.Visibility = Visibility.Collapsed;
        RoomContent.Visibility = Visibility.Collapsed;
        OnlineRoomContent.Visibility = Visibility.Collapsed;
        OnlineRoomContent.ClearSecrets();
        AccountContent.Visibility = Visibility.Visible;
        AccountContent.ClearSecrets();
        TopNavigation.IsEnabled = !IsInPorch;
        HomeContent.IsEnabled = SettingsPage.IsEnabled = HomeFooter.IsEnabled = true;
        ClearLiveDirectionalInput();
        if (accountReturnFocus is UIElement { IsVisible: true, IsEnabled: true } previous) Keyboard.Focus(previous);
        else Stage.Focus();
        accountReturnFocus = null;
    }

    private void CloseAccountPanel()
    {
        if (IsOnlineRoomPanelOpen) OnlineRoomPanelClosed?.Invoke();
        else if (IsRoomPanelOpen) RoomPanelClosed?.Invoke();
        else AccountPanelClosed?.Invoke();
        HideAccountPanel();
    }

    internal void SetAccountStatus(bool signedIn, bool configurationReady, string? nickname = null)
    {
        if (porchSignedIn != signedIn)
        {
            porchSignedIn = signedIn;
            porchDirectory = new(RoomPanelPage.Directory);
            PorchScene.ApplyDirectory(null, "active", false, null);
        }
        var label = signedIn ? string.IsNullOrWhiteSpace(nickname) ? "设置昵称" : nickname : "登录";
        AccountNicknameText.Text = label;
        AutomationProperties.SetName(AccountButton, signedIn ? label : "邮箱账号");
        AccountButton.ToolTip = signedIn
            ? label + "\n" + (configurationReady ? "已登录，外观已确认" : "已登录，外观尚未确认")
            : "邮箱登录";
        UpdateAccountNicknameWidth();
    }

    private void OnAccountNavigationSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAccountNicknameWidth();

    private void UpdateAccountNicknameWidth()
    {
        if (AccountButton is null || TopNavigation.ActualWidth <= 0) return;
        var remaining = TopNavigation.ActualWidth - ToolsNavigation.Margin.Left - ToolsNavigation.Margin.Right
            - ToolsNavigation.Children.OfType<FrameworkElement>().Where(child => !ReferenceEquals(child, AccountButton))
                .Sum(child => child.DesiredSize.Width);
        if (Grid.GetColumnSpan(ToolsNavigation) == 1)
        {
            remaining -= Math.Max(Brand.Visibility == Visibility.Visible ? Brand.DesiredSize.Width : 0,
                FaceBrandIcon.Visibility == Visibility.Visible ? FaceBrandIcon.DesiredSize.Width : 0);
            remaining -= MainNavigation.DesiredSize.Width;
        }
        var limit = Math.Clamp(remaining, 48, 144);
        if (Math.Abs(AccountButton.MaxWidth - limit) > .5) AccountButton.MaxWidth = limit;
    }

    internal void RefreshAccountAppearance() => RefreshAppearanceControls();
}
