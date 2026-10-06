using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed partial class LocalRoomMenuView : UserControl
{
    internal event Action? ReturnRequested;
    internal event Action? ControlCenterRequested;
    internal event Action<LocalRoomPresentation>? PresentationRequested;
    internal event Action? RecoverRequested;
    internal event Action<RoomCameraMode>? CameraModeRequested;
    private bool showCameraSettings;
    private bool refreshingCamera;
    private const string Instructions = "A / D 移动    ·    S 轻压\n空格 跳跃    ·    滚轮 调视距\nShift + 左键横移 抚摸\nEsc 打开 / 收起菜单";

    internal bool ShowCameraSettings
    {
        get => showCameraSettings;
        set
        {
            showCameraSettings = value;
            RoomCameraSettings.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            RoomInstructions.Text = Instructions + (value ? "\n不跟随时 · 右键拖动视角" : "");
        }
    }
    internal bool IsCameraDropDownOpen => RoomCameraModeSelector.IsDropDownOpen;

    internal LocalRoomMenuView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RoomMenuCard.MaxHeight = Math.Max(1, ActualHeight - 32);
    }

    internal void RefreshStatus(LocalRoomSceneView scene)
    {
        RoomStatusLabel.Text = $"普通房 · 本地单人   视距 {scene.Camera.ZoomPercent:0}%" +
            (scene.IsHeightLimited ? " · 受窗口高度限制" : "");
        refreshingCamera = true;
        try { RoomCameraModeSelector.SelectedIndex = (int)scene.Camera.CameraMode; }
        finally { refreshingCamera = false; }
    }

    internal bool CloseCameraDropDown()
    {
        if (!IsCameraDropDownOpen) return false;
        RoomCameraModeSelector.IsDropDownOpen = false;
        RoomCameraModeSelector.Focus();
        return true;
    }

    private void OnCameraModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!showCameraSettings || refreshingCamera || RoomCameraModeSelector is null) return;
        var mode = RoomCameraModeSelector.SelectedIndex switch
        {
            0 => RoomCameraMode.Follow,
            1 => RoomCameraMode.EdgeFollow,
            2 => RoomCameraMode.Manual,
            _ => (RoomCameraMode?)null,
        };
        if (mode is { } selected) CameraModeRequested?.Invoke(selected);
    }

    private void OnReturn(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke();
    private void OnControlCenter(object sender, RoutedEventArgs e) => ControlCenterRequested?.Invoke();
    private void OnWindow(object sender, RoutedEventArgs e) => PresentationRequested?.Invoke(LocalRoomPresentation.Window);
    private void OnWallpaper(object sender, RoutedEventArgs e) => PresentationRequested?.Invoke(LocalRoomPresentation.Wallpaper);
    private void OnRecover(object sender, RoutedEventArgs e) => RecoverRequested?.Invoke();
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsCameraDropDownOpen) return;
        RoomMenuScroll.ScrollToVerticalOffset(RoomMenuScroll.VerticalOffset - e.Delta / 3d);
        e.Handled = true;
    }
}
