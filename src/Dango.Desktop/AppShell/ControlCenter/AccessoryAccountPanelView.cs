using System.Windows;
using System.Windows.Controls;
using Dango.Client.Sync.Accessories;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

internal enum AccessoryAccountAction { BackupLocal, RestoreAccount, RefreshAccount, RetryBackup, ReconcileCommit }

internal sealed record AccessoryAccountPanelState(string Message, bool IsLoggedIn, bool Busy = false,
    bool CanBackup = false, bool CanRestore = false, bool CanRefresh = false, bool CanRetry = false, bool CanReconcile = false);

/// <summary>Private accessory backup controls, independent of the appearance configuration choice.</summary>
internal sealed class AccessoryAccountPanelView : UserControl
{
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
    private readonly Dictionary<AccessoryAccountAction, Button> buttons = new();

    internal AccessoryAccountPanelView()
    {
        NameScope.SetNameScope(this, new NameScope());
        var body = new StackPanel { Margin = new Thickness(0, 14, 0, 4) };
        body.Children.Add(new TextBlock { Text = "配饰穿搭", FontSize = 17 });
        message.Name = "AccessoryAccountMessage";
        RegisterName(message.Name, message);
        body.Children.Add(message);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (action, label, name) in new[]
        {
            (AccessoryAccountAction.BackupLocal, "备份本机当前穿搭", "AccessoryBackupLocal"),
            (AccessoryAccountAction.RestoreAccount, "恢复账号穿搭", "AccessoryRestoreAccount"),
            (AccessoryAccountAction.RefreshAccount, "重新读取账号穿搭", "AccessoryRefreshAccount"),
            (AccessoryAccountAction.RetryBackup, "重试备份", "AccessoryRetryBackup"),
            (AccessoryAccountAction.ReconcileCommit, "核对账号保存结果", "AccessoryReconcileCommit"),
        })
        {
            var button = new Button { Name = name, Content = label, MinHeight = 34, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 4, 8, 4) };
            button.SetResourceReference(Button.BackgroundProperty, "HomeSoft");
            button.SetResourceReference(Button.BorderBrushProperty, "HomeLine");
            button.Click += (_, _) => { if (button.IsEnabled) Requested?.Invoke(action); };
            RegisterName(name, button);
            buttons.Add(action, button);
            actions.Children.Add(button);
        }
        body.Children.Add(actions);
        Content = body;
        Apply(new("登录后可将当前穿搭私有备份到账号。本机佩戴不受影响。", false));
    }

    internal event Action<AccessoryAccountAction>? Requested;
    internal string Message => message.Text;

    internal void Apply(AccessoryAccountPanelState state)
    {
        message.Text = state.Message;
        Visibility = state.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (action, button) in buttons)
        {
            var allowed = action switch
            {
                AccessoryAccountAction.BackupLocal => state.CanBackup,
                AccessoryAccountAction.RestoreAccount => state.CanRestore,
                AccessoryAccountAction.RefreshAccount => state.CanRefresh,
                AccessoryAccountAction.RetryBackup => state.CanRetry,
                AccessoryAccountAction.ReconcileCommit => state.CanReconcile,
                _ => false,
            };
            button.IsEnabled = allowed && !state.Busy;
            button.Visibility = action is AccessoryAccountAction.RetryBackup or AccessoryAccountAction.ReconcileCommit
                ? allowed ? Visibility.Visible : Visibility.Collapsed : Visibility.Visible;
        }
    }
}
