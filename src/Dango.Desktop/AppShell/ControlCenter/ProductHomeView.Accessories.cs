using System.Windows;
using System.Windows.Input;
using Dango.Desktop.Accessories;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private AccessorySettingsSession? accessorySession;
    private readonly AccessoryEditorPanel accessoryPanel = new();
    private bool pickingAccessoryAnchor;
    internal AccessorySettingsSession? BoundAccessories => accessorySession;

    private void InitializeAccessories()
    {
        WardrobeOptions.Children.Clear(); WardrobeOptions.Children.Add(accessoryPanel);
        accessoryPanel.Children.Remove(accessoryPanel.SaveActions);
        AccessorySaveActionsSlot.Content = accessoryPanel.SaveActions;
        AccessoryPreviewNotice.Text = accessoryPanel.PreviewNotice;
        accessoryPanel.PresentationChanged += () => AccessoryPreviewNotice.Text = accessoryPanel.PreviewNotice;
        accessoryPanel.PreviewChanged += configuration =>
        {
            if (!IsEditing || SelectedEditor != HomeEditor.Wardrobe) return;
            ClearLiveDirectionalInput();
            BoundRuntime?.SetAccessories(configuration);
            RefreshAccessoryFrame();
        };
        accessoryPanel.Applied += configuration =>
        {
            BoundRuntime?.SetAccessories(configuration, !(accessorySession?.IsCompatibilityMode ?? true));
            RefreshAccessoryFrame();
        };
        accessoryPanel.CancelRequested += CloseEditor;
        accessoryPanel.BodyAnchorPickRequested = () =>
        {
            if (!IsEditing || SelectedEditor != HomeEditor.Wardrobe) return;
            ClearLiveDirectionalInput(); pickingAccessoryAnchor = true;
            PetPreview.Cursor = Cursors.Cross;
            PetPreview.ToolTip = "点击团子选择附着位置";
        };
        PetPreview.PreviewMouseLeftButtonDown += OnAccessoryBodyAnchorPick;
    }

    private void OnAccessorySaveActionsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Wardrobe) return;
        UpdateStageSizing();
        UpdateTransition(true);
    }

    internal void BindAccessories(AccessorySettingsSession session)
    {
        accessorySession = session;
        accessoryPanel.Bind(session);
    }

    internal void CancelAccessoryPreview()
    {
        pickingAccessoryAnchor = false; PetPreview.Cursor = Cursors.Arrow;
        PetPreview.ToolTip = "同一只实时物理团子";
        accessorySession?.CancelPreview(); accessoryPanel.Refresh();
        if (accessorySession is not null)
            BoundRuntime?.SetAccessories(accessorySession.SavedConfiguration, !accessorySession.IsCompatibilityMode);
        RefreshAccessoryFrame();
    }

    private void RefreshAccessoryFrame()
    {
        if (BoundRuntime?.LastFrame is { } frame && OwnsLiveFrameDriver) ApplyLiveFrame(frame);
        accessoryPanel.SetPreviewError(BoundRuntime?.AccessoryError);
    }

    private void UpdateAccessoryPresentationStatus()
    {
        var error = BoundRuntime?.AccessoryError;
        accessoryPanel.SetPreviewError(error);
        if (error is not null) LiveSceneStatusText.Text = error;
        LiveSceneStatusText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnAccessoryBodyAnchorPick(object sender, MouseButtonEventArgs e)
    {
        if (!pickingAccessoryAnchor || !IsEditing || SelectedEditor != HomeEditor.Wardrobe ||
            BoundRuntime?.LastFrame is not { Bodies.Count: > 0 } frame) return;
        e.Handled = true; ClearLiveDirectionalInput();
        var point = e.GetPosition(LiveDangoVisual);
        try
        {
            var uv = AccessoryBodyMapper.Unmap(frame.Bodies[0], frame.Gait, point);
            accessoryPanel.SetBodyAnchor(new Point(uv.X, uv.Y));
            pickingAccessoryAnchor = false; PetPreview.Cursor = Cursors.Arrow;
            PetPreview.ToolTip = "同一只实时物理团子";
        }
        catch (ArgumentException) { PetPreview.ToolTip = "等待有效身体画面后重试"; }
    }
}
