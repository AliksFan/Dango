using System.Windows;
using System.Windows.Input;
using Dango.Desktop.Accessories;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private AccessorySettingsSession? accessorySession;
    private readonly AccessoryEditorPanel accessoryPanel = new();
    private bool pickingAccessoryAnchor;
    private RoomViewController? accessoryCamera;
    private long accessoryCameraFrame;
    private bool accessoryPhysicsConfigured;
    private bool accessoryJumpW;
    private bool accessoryJumpSpace;
    private bool accessoryProjectionRefreshQueued;
    private bool IsAccessoryPreview => IsEditing && SelectedEditor == HomeEditor.Wardrobe;
    internal AccessorySettingsSession? BoundAccessories => accessorySession;

    private void InitializeAccessories()
    {
        WardrobeOptions.Children.Clear(); WardrobeOptions.Children.Add(accessoryPanel);
        accessoryPanel.Children.Remove(accessoryPanel.SaveActions);
        AccessorySaveActionsSlot.Content = accessoryPanel.SaveActions;
        AccessorySectionTabsSlot.Content = accessoryPanel.SectionTabs;
        accessoryPanel.SectionChanged += () => { OptionsBody.ScrollToTop(); Stage.Focus(); };
        AccessoryPreviewNotice.Text = AccessoryNotice;
        accessoryPanel.PresentationChanged += () => AccessoryPreviewNotice.Text = AccessoryNotice;
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
        PetVisualArea.SizeChanged += (_, _) => QueueAccessoryProjectionRefresh();
        PetPreview.SizeChanged += (_, _) => QueueAccessoryProjectionRefresh();
        AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (IsAccessoryPreview && e.NewFocus is (System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox))
                ClearLiveDirectionalInput();
        }), true);
    }

    private string AccessoryNotice => accessoryPanel.PreviewNotice + "\nA/D 移动 · S 压扁 · W/空格 跳跃 · Esc 退出";

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
        accessorySession?.CancelPreview(); accessoryPanel.DiscardInputsAndRefresh();
        if (accessorySession is not null)
            BoundRuntime?.SetAccessories(accessorySession.SavedConfiguration, !accessorySession.IsCompatibilityMode);
        RefreshAccessoryFrame();
    }

    private void UpdateAccessoryPreviewMode()
    {
        accessoryCamera = null;
        accessoryCameraFrame = 0;
        LiveSceneCanvas.UseLayoutRounding = !IsAccessoryPreview;
        PetShadow.UseLayoutRounding = !IsAccessoryPreview;
        PetShadowTranslation.X = PetShadowTranslation.Y = 0;
        ClearLiveDirectionalInput();
        if (BoundRuntime is null) return;
        UpdateLiveMovementMode();
        if (IsAccessoryPreview || accessoryPhysicsConfigured) ChangeLiveSurface();
        if (BoundRuntime.LastFrame is { } frame && OwnsLiveFrameDriver) ApplyLiveFrame(frame);
        QueueAccessoryProjectionRefresh();
    }

    private void QueueAccessoryProjectionRefresh()
    {
        if (!IsAccessoryPreview || accessoryProjectionRefreshQueued) return;
        accessoryProjectionRefreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            accessoryProjectionRefreshQueued = false;
            if (IsAccessoryPreview) RefreshAccessoryFrame();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyAccessoryPreviewFrame(StackedPhysicsPresentationFrame frame)
    {
        if (liveSurfaceNeedsAlignment || liveSurfaceFailure is not null || BoundRuntime?.LocalGroundY is not { } ground)
        {
            LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
            AccessoryHostBinding.Hide(LiveDangoVisual);
            PetShadow.Visibility = Visibility.Collapsed;
            LiveSceneStatusText.Text = liveSurfaceFailure ?? "配饰预览准备中";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }
        var previewTransform = LiveSceneCanvas.TransformToAncestor(PetVisualArea);
        var previewOrigin = previewTransform.Transform(new Point());
        var scale = previewTransform.Transform(new Point(0, 1)).Y - previewOrigin.Y;
        if (scale <= 0 || !double.IsFinite(scale)) return;
        var viewportHeight = (PetVisualArea.ActualHeight - previewOrigin.Y) / scale;
        var shadowLayoutCenterY = PetShadow.Margin.Top + PetShadow.Height / 2;
        var groundInset = (PetVisualArea.ActualHeight - shadowLayoutCenterY) / scale;
        if (accessoryCamera is null)
        {
            accessoryCamera = new RoomViewController(standardScale: 1);
            accessoryCamera.EnableUnboundedFollow(groundInset, Math.Max(0, groundInset - 1));
        }
        accessoryCamera.Resize(LiveSceneCanvas.ActualWidth, viewportHeight, ground);
        var bounds = DangoHostFrameBinding.Bounds(frame, 0);
        var center = BoundRuntime.CurrentSelectedCenter ?? bounds.Center;
        var grounded = BoundRuntime.LastPhysicsFrame is { Succeeded: true } physics && physics.Bodies[0].IsGrounded;
        if (grounded)
            shadowGroundCenterY = center.Y;
        var alignment = BoundRuntime.NeutralBodyGroundOffset;
        // The shape computes the current painted bottom; its contour never becomes
        // the camera focus. Accessory drawing still consumes this same body frame.
        LiveDangoVisual.ApplyFrame(frame.Bodies[0], frame.Gait);
        if (LiveDangoVisual.LastContourFailure != DangoContourFailure.None)
        {
            LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
            AccessoryHostBinding.Hide(LiveDangoVisual);
            PetShadow.Visibility = Visibility.Collapsed;
            LiveSceneStatusText.Text = "团子画面暂不可用 · 等待有效身体画面";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }
        var renderedBottom = bounds.Y + LiveDangoVisual.RenderedBodyBottom!.Value;
        if (grounded)
            groundedVisualOffset = ground - renderedBottom - alignment - 1 / scale;
        var bodyAlignment = alignment + (groundedVisualOffset ?? 0);
        var bottom = renderedBottom + bodyAlignment;
        var now = Environment.TickCount64;
        var p = accessoryCamera.Update(new(center.X, center.Y + alignment),
            accessoryCameraFrame == 0 ? 0 : now - accessoryCameraFrame,
            ReduceMotion || !SystemParameters.ClientAreaAnimation, bottom);
        accessoryCameraFrame = now;
        DangoHostFrameBinding.Apply(LiveDangoVisual, LiveDangoHat, frame, 0,
            new LogicalVector(p.OffsetX, p.OffsetY + bodyAlignment),
            appearance: BoundRuntime.DisplayHatAppearance, hatSize: BoundRuntime.DisplayHatSize,
            bodyFillOverride: BoundRuntime.BodyPreviewFill, eyeFillOverride: BoundRuntime.EyePreviewFill,
            appearanceEntity: BoundRuntime.AppearanceEntity, runtime: BoundRuntime);
        var floor = p.WorldToScreen(new(center.X, ground));
        var lift = Math.Clamp(((shadowGroundCenterY ?? center.Y) - center.Y) / 120, 0, 1);
        // Keep the shadow in the exact same arranged projection as the body.
        // A layout margin would round each moving world-floor position to pixels.
        var projectedFloor = previewTransform.Transform(new Point(floor.X, floor.Y));
        // Use the arranged slot contract rather than a collapsed element's zero
        // RenderSize, so the first visible frame after a high jump is correct.
        PetShadowTranslation.X = projectedFloor.X - PetVisualArea.ActualWidth / 2;
        PetShadowTranslation.Y = projectedFloor.Y - shadowLayoutCenterY;
        PetShadowScale.ScaleX = 1 - .32 * lift;
        PetShadowScale.ScaleY = 1 - .15 * lift;
        PetShadow.Opacity = .17 - .09 * lift;
        PetShadow.Visibility = floor.Y >= 0 && floor.Y <= viewportHeight + 1 &&
            LiveDangoVisual.LastContourFailure == DangoContourFailure.None ? Visibility.Visible : Visibility.Collapsed;
        UpdateAccessoryPresentationStatus();
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
