using System.Windows;
using System.Windows.Input;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private IInputElement? accountReturnFocus;
    internal event Action? AccountRequested;
    internal event Action? AccountPanelClosed;
    internal event Action? RoomDirectoryRequested;
    internal event Action? RoomPanelClosed;
    internal bool IsAccountPanelOpen => AccountOverlay.Visibility == Visibility.Visible;
    internal AccountPanelView AccountPanel => AccountContent;
    internal RoomPanelView RoomPanel => RoomContent;
    internal bool IsRoomPanelOpen => IsAccountPanelOpen && RoomContent.Visibility == Visibility.Visible;

    private void OnRoomDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (IsAccountPanelOpen) return;
        if (RoomDirectoryRequested is null) OnAccountClick(sender, e);
        else RoomDirectoryRequested.Invoke();
    }

    internal void ShowAccountPanel()
    {
        if (IsRoomPanelOpen) { RoomPanelClosed?.Invoke(); HideAccountPanel(); }
        OnAccountClick(this, new RoutedEventArgs());
    }

    internal void ShowRoomPanel()
    {
        if (IsAccountPanelOpen && !IsRoomPanelOpen) { AccountPanelClosed?.Invoke(); HideAccountPanel(); }
        if (IsAccountPanelOpen) return;
        accountReturnFocus = Keyboard.FocusedElement;
        ClearLiveDirectionalInput();
        AccountContent.Visibility = Visibility.Collapsed;
        AccountContent.ClearSecrets();
        RoomContent.Visibility = AccountOverlay.Visibility = Visibility.Visible;
        TopNavigation.IsEnabled = HomeContent.IsEnabled = SettingsPage.IsEnabled = HomeFooter.IsEnabled = false;
        Dispatcher.BeginInvoke(() => { if (IsRoomPanelOpen) RoomContent.MoveFocus(new(FocusNavigationDirection.First)); });
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
        RoomContent.Visibility = Visibility.Collapsed;
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
        if (IsRoomPanelOpen) RoomPanelClosed?.Invoke();
        else AccountPanelClosed?.Invoke();
        HideAccountPanel();
    }

    internal void SetAccountStatus(bool signedIn, bool configurationReady)
    {
        AccountButton.Content = signedIn ? "账号" : "登录";
        AccountButton.ToolTip = signedIn ? configurationReady ? "已登录，外观已确认" : "已登录，外观尚未确认" : "邮箱登录";
    }

    internal void RefreshAccountAppearance() => RefreshAppearanceControls();
}
