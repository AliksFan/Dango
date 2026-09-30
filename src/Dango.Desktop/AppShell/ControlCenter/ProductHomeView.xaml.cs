using System.Windows;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView : System.Windows.Controls.UserControl
{
    public ProductHomeView() => InitializeComponent();

    public event EventHandler? VisibilityRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RetryRequested;
    public event EventHandler? RestoreRequested;

    public void SetState(DesktopHostSnapshot snapshot, bool ready, string? saveStatus)
    {
        DesktopStateText.Text = !ready ? "团子暂时不可用"
            : snapshot.IsVisible ? "团子正在桌面上" : "团子已隐藏";
        var mode = snapshot.Mode switch
        {
            "Wallpaper" => "壁纸层",
            "Window" => "普通窗口",
            _ => snapshot.Mode,
        };
        HostStatusText.Text = $"{mode} · {(snapshot.IsInteractionEnabled ? "可互动" : "互动已关闭")}"
            + (snapshot.LastError is null ? string.Empty : $" · {snapshot.LastError}");
        VisibilityButton.Content = snapshot.IsVisible ? "隐藏桌面团子" : "显示桌面团子";
        VisibilityButton.IsEnabled = ready;
        RetryButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        RestoreButton.IsEnabled = ready;
        SaveStatusText.Text = saveStatus ?? string.Empty;
    }

    public void SetCommandStatus(string status) => CommandStatusText.Text = status;

    private void OnVisibilityClick(object sender, RoutedEventArgs e) => VisibilityRequested?.Invoke(this, EventArgs.Empty);
    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
}
