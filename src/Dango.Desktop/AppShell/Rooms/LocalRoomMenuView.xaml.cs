using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed partial class LocalRoomMenuView : UserControl
{
    internal event Action? ReturnRequested;
    internal event Action? ControlCenterRequested;
    internal event Action<LocalRoomPresentation>? PresentationRequested;
    internal event Action? RecoverRequested;

    internal LocalRoomMenuView() => InitializeComponent();

    internal void RefreshStatus(LocalRoomSceneView scene) => RoomStatusLabel.Text =
        $"普通房 · 本地单人   视距 {scene.Camera.ZoomPercent:0}%" +
        (scene.IsHeightLimited ? " · 受窗口高度限制" : "");

    private void OnReturn(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke();
    private void OnControlCenter(object sender, RoutedEventArgs e) => ControlCenterRequested?.Invoke();
    private void OnWindow(object sender, RoutedEventArgs e) => PresentationRequested?.Invoke(LocalRoomPresentation.Window);
    private void OnWallpaper(object sender, RoutedEventArgs e) => PresentationRequested?.Invoke(LocalRoomPresentation.Wallpaper);
    private void OnRecover(object sender, RoutedEventArgs e) => RecoverRequested?.Invoke();
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = true;
}
