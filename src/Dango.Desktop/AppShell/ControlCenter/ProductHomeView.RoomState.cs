using System.Windows;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    internal bool IsDangoAway { get; private set; }
    private bool isDesktopPetRoomContext;

    internal void SetDesktopPetRoomContext(bool active)
    {
        isDesktopPetRoomContext = active;
        if (settingsState is { } state) UpdateSettings(state);
    }

    internal void SetDangoAway(bool away)
    {
        if (away)
        {
            SuspendForRoom();
            CloseEditor(focusStage: false);
        }
        IsDangoAway = away;
        UpdateTransition(false);
    }

    private void UpdateRoomAwayPresentation()
    {
        RoomAwayNotice.Visibility = IsDangoAway ? Visibility.Visible : Visibility.Collapsed;
        Presence.Visibility = IsDangoAway ? Visibility.Collapsed : Visibility.Visible;
        Launchers.Visibility = IsDangoAway || IsEditing ? Visibility.Collapsed : Visibility.Visible;
        Launchers.IsEnabled = Launchers.IsHitTestVisible = !IsDangoAway && !IsInPorch && !IsEditing;
        if (IsDangoAway)
        {
            EditorPanel.Visibility = FaceEditorTools.Visibility = PorchContent.Visibility = Visibility.Collapsed;
            FaceEditorTools.IsEnabled = FaceEditorTools.IsHitTestVisible = false;
            EnterRoomButton.Visibility = Visibility.Collapsed;
            EnterRoomButton.IsEnabled = false;
            LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
            HomeFooter.Visibility = Visibility.Collapsed;
        }
        else if (!IsSettingsOpen) HomeFooter.Visibility = Visibility.Visible;
    }
}
