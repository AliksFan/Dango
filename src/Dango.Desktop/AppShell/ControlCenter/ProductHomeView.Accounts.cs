using System.Windows;
using System.Windows.Input;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private IInputElement? accountReturnFocus;
    internal event Action? AccountRequested;
    internal event Action? AccountPanelClosed;
    internal bool IsAccountPanelOpen => AccountOverlay.Visibility == Visibility.Visible;
    internal AccountPanelView AccountPanel => AccountContent;

    private void OnUnboundAccountAction(AccountPanelAction action)
    {
        if (action == AccountPanelAction.Close && AccountRequested is null) CloseAccountPanel();
    }

    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        if (IsAccountPanelOpen) return;
        accountReturnFocus = Keyboard.FocusedElement;
        ClearLiveDirectionalInput();
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
        AccountPanelClosed?.Invoke();
        HideAccountPanel();
    }

    internal void SetAccountStatus(bool signedIn, bool configurationReady)
    {
        AccountButton.Content = signedIn ? "账号" : "登录";
        AccountButton.ToolTip = signedIn ? configurationReady ? "已登录，外观已确认" : "已登录，外观尚未确认" : "邮箱登录";
    }

    internal void RefreshAccountAppearance() => RefreshAppearanceControls();
}
