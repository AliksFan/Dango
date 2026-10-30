using System.Windows;
using System.Windows.Automation;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private bool updatingDesktopPetDisplay;
    internal event Action<int>? DesktopPetSizeRequested;

    internal void UpdateDesktopPetDisplaySettings(int percent, string? status, bool connected)
    {
        updatingDesktopPetDisplay = true;
        try
        {
            DesktopPetSizeSlider.Value = percent;
            DesktopPetSizeSlider.IsEnabled = connected;
            DesktopPetSizeValue.Text = $"{percent}%";
            AutomationProperties.SetItemStatus(DesktopPetSizeSlider, $"{percent}%");
            DesktopPetSizeStatus.Text = status ?? string.Empty;
            DesktopPetSizeStatus.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
            RetryDesktopPetSizeButton.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
            RetryDesktopPetSizeButton.IsEnabled = connected;
        }
        finally { updatingDesktopPetDisplay = false; }
    }

    private void OnDesktopPetSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingDesktopPetDisplay || !IsInitialized || DesktopPetSizeSlider is null) return;
        var percent = (int)Math.Round(e.NewValue);
        DesktopPetSizeValue.Text = $"{percent}%";
        DesktopPetSizeRequested?.Invoke(percent);
    }

    private void OnRetryDesktopPetSizeClick(object sender, RoutedEventArgs e) =>
        DesktopPetSizeRequested?.Invoke((int)Math.Round(DesktopPetSizeSlider.Value));
}
