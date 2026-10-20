using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.Accessories;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private AccessorySettingsSession? accessorySession;
    private readonly AccessoryEditorPanel accessoryPanel = new();
    private bool pickingAccessoryAnchor;
    private RoomViewController? accessoryCamera;
    private readonly RoomPositionInterpolator accessoryPositions = new();
    private HostFrameDiagnostics accessoryPresentationSnapshot;
    private bool hasAccessoryPositionSample;
    private long? previousAccessoryPresentationTicks;
    private LogicalPoint? accessoryNativeCenter;
    private LogicalVector accessoryPresentationOffset;
    private bool accessoryNativeGrounded;
    private bool accessoryPhysicsConfigured;
    private bool accessoryProjectionRefreshQueued;
    private bool IsAccessoryPreview => IsEditing && SelectedEditor == HomeEditor.Wardrobe;
    internal AccessorySettingsSession? BoundAccessories => accessorySession;
    internal bool IsPickingAccessoryAnchor => pickingAccessoryAnchor;

    private void InitializeAccessories()
    {
        WardrobeOptions.Children.Clear(); WardrobeOptions.Children.Add(accessoryPanel);
        accessoryPanel.Children.Remove(accessoryPanel.SaveActions);
        AccessorySaveActionsSlot.Content = accessoryPanel.SaveActions;
        AccessorySectionTabsSlot.Content = accessoryPanel.SectionTabs;
        AccessoryActionToolsSlot.Content = accessoryPanel.ActionTools;
        accessoryPanel.SectionChanged += () => { OptionsBody.ScrollToTop(); Stage.Focus(); };
        AccessoryPreviewNotice.Text = AccessoryNotice;
        accessoryPanel.PresentationChanged += () => AccessoryPreviewNotice.Text = AccessoryNotice;
        accessoryPanel.PreviewChanged += configuration =>
        {
            if (!IsEditing || SelectedEditor != HomeEditor.Wardrobe) return;
            ClearLiveDirectionalInput();
            SetAccessoryPresentation(configuration);
            RefreshAccessoryFrame();
        };
        accessoryPanel.Applied += configuration =>
        {
            SetAccessoryPresentation(configuration, !(accessorySession?.IsCompatibilityMode ?? true));
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

    private string AccessoryNotice => accessoryPanel.PreviewNotice + "\nA/D 移动 · S 压扁 · 空格 跳跃 · Esc 退出";

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
        SynchronizeBeltAccessories();
    }

    internal void SynchronizeBeltAccessories()
    {
        if (accessorySession is null || beltPresenter is null) return;
        beltPresenter.SetAccessories(IsAccessoryPreview ? accessorySession.DraftConfiguration : accessorySession.SavedConfiguration,
            accessorySession.TryGetImage);
        RefreshAccessoryFrame();
    }

    private void SetAccessoryPresentation(AccessoryConfiguration configuration, bool useNewConfiguration = true)
    {
        BoundRuntime?.SetAccessories(configuration, useNewConfiguration);
        if (beltPresenter is not null && accessorySession is not null)
            beltPresenter.SetAccessories(configuration, accessorySession.TryGetImage);
    }

    internal void CancelAccessoryPreview()
    {
        pickingAccessoryAnchor = false; PetPreview.Cursor = Cursors.Arrow;
        PetPreview.ToolTip = "同一只实时物理团子";
        accessorySession?.CancelPreview(); accessoryPanel.DiscardInputsAndRefresh();
        if (accessorySession is not null)
            SetAccessoryPresentation(accessorySession.SavedConfiguration, !accessorySession.IsCompatibilityMode);
        RefreshAccessoryFrame();
    }

    private void UpdateAccessoryPreviewMode()
    {
        ResetAccessoryPresentation();
        accessoryCamera = null;
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
            ResetAccessoryPresentation();
            LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
            AccessoryHostBinding.Hide(LiveDangoVisual);
            PetShadow.Visibility = Visibility.Collapsed;
            LiveSceneStatusText.Text = liveSurfaceFailure ?? "配饰预览准备中";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }
        if (!TryGetAccessoryPreviewLayout(out _, out var scale, out _, out _, out _)) return;
        var bounds = DangoHostFrameBinding.Bounds(frame, 0);
        var center = BoundRuntime.CurrentSelectedCenter ?? bounds.Center;
        var grounded = BoundRuntime.LastPhysicsFrame is { Succeeded: true } physics && physics.Bodies[0].IsGrounded;
        if (grounded)
            shadowGroundCenterY = center.Y;
        var alignment = BoundRuntime.NeutralBodyGroundOffset;
        // Draw once, then read this same contour for contact alignment. Drawing
        // a preliminary body here rebuilt both eyes and the path a second time.
        DangoHostFrameBinding.Apply(LiveDangoVisual, LiveDangoHat, frame, 0,
            new LogicalVector(0, alignment + (groundedVisualOffset ?? 0)),
            appearance: BoundRuntime.DisplayHatAppearance, hatSize: BoundRuntime.DisplayHatSize,
            bodyFillOverride: BoundRuntime.BodyPreviewFill, eyeFillOverride: BoundRuntime.EyePreviewFill,
            appearanceEntity: BoundRuntime.AppearanceEntity, runtime: BoundRuntime);
        if (LiveDangoVisual.LastContourFailure != DangoContourFailure.None)
        {
            ResetAccessoryPresentation();
            LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
            AccessoryHostBinding.Hide(LiveDangoVisual);
            PetShadow.Visibility = Visibility.Collapsed;
            LiveSceneStatusText.Text = "团子画面暂不可用 · 等待有效身体画面";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }
        var renderedBottom = bounds.Y + LiveDangoVisual.RenderedBodyBottom!.Value;
        if (grounded)
        {
            groundedVisualOffset = ground - renderedBottom - alignment - 1 / scale;
            var left = bounds.X;
            var top = bounds.Y + alignment + groundedVisualOffset.Value;
            System.Windows.Controls.Canvas.SetTop(LiveDangoVisual, top);
            System.Windows.Controls.Canvas.SetTop(LiveDangoHat, top);
            AccessoryHostBinding.Position(LiveDangoVisual, left, top);
        }
        accessoryNativeCenter = center;
        accessoryNativeGrounded = grounded;
        UpdateAccessoryProjection(0, advanceCamera: false);
        UpdateAccessoryPresentationStatus();
    }

    private void PresentAccessoryPreview(object? sender, EventArgs args)
    {
        if (liveScheduler is null) return;
        var now = liveScheduler.Snapshot;
        var before = accessoryPresentationSnapshot;
        accessoryPresentationSnapshot = now;
        if (!IsAccessoryPreview || now.DuplicateCallbacks != before.DuplicateCallbacks) return;
        if (!OwnsLiveFrameDriver || !liveScheduler.IsRunning || !IsVisible || BoundRuntime?.LastError is not null ||
            liveSurfaceNeedsAlignment || liveSurfaceFailure is not null || accessoryNativeCenter is not { } center ||
            LiveDangoVisual.Visibility != Visibility.Visible || LiveDangoVisual.LastContourFailure != DangoContourFailure.None)
        {
            ResetAccessoryPresentation();
            return;
        }
        var simulationTicks = now.SuccessfulSteps * HostFrameClock.FixedStep.Ticks;
        var clockTicks = simulationTicks + now.RemainderTicks;
        var discontinuity = now.Overloads != before.Overloads || now.LongPauses != before.LongPauses ||
            now.ClockResets != before.ClockResets || now.Faults != before.Faults ||
            now.RefreshFrames != before.RefreshFrames ||
            previousAccessoryPresentationTicks is { } previous && clockTicks < previous;
        if (!hasAccessoryPositionSample || discontinuity)
        {
            accessoryPositions.Reset(center, simulationTicks);
            hasAccessoryPositionSample = true;
            previousAccessoryPresentationTicks = null;
            accessoryCamera?.ResetMotion();
        }
        else if (now.AppliedFrames != before.AppliedFrames)
            accessoryPositions.Add(center, simulationTicks);
        var elapsedMs = previousAccessoryPresentationTicks is { } last ?
            Math.Clamp((clockTicks - last) / (double)TimeSpan.TicksPerMillisecond, 0, 100) : 0;
        previousAccessoryPresentationTicks = clockTicks;
        var displayed = accessoryPositions.At(clockTicks - HostFrameClock.FixedStep.Ticks);
        // The latest grounded contour already carries exact contact correction.
        // Interpolating native core Y there would move that painted contact.
        accessoryPresentationOffset = new(displayed.X - center.X, accessoryNativeGrounded ? 0 : displayed.Y - center.Y);
        UpdateAccessoryProjection(elapsedMs);
    }

    private void ResetAccessoryPresentation()
    {
        accessoryPositions.Clear();
        hasAccessoryPositionSample = false;
        previousAccessoryPresentationTicks = null;
        accessoryNativeCenter = null;
        accessoryPresentationOffset = default;
        accessoryPresentationSnapshot = liveScheduler?.Snapshot ?? default;
        accessoryCamera?.ResetMotion();
        LiveEntityLayer.RenderTransform = Transform.Identity;
    }

    private bool TryGetAccessoryPreviewLayout(out GeneralTransform previewTransform, out double scale,
        out double viewportHeight, out double shadowLayoutCenterY, out double groundInset)
    {
        // This fixed outer canvas never receives the world transform, so measuring
        // the arranged preview cannot feed camera motion back into its viewport.
        previewTransform = LiveSceneCanvas.TransformToAncestor(PetVisualArea);
        var previewOrigin = previewTransform.Transform(new Point());
        scale = previewTransform.Transform(new Point(0, 1)).Y - previewOrigin.Y;
        viewportHeight = shadowLayoutCenterY = groundInset = 0;
        if (scale <= 0 || !double.IsFinite(scale)) return false;
        viewportHeight = (PetVisualArea.ActualHeight - previewOrigin.Y) / scale;
        shadowLayoutCenterY = PetShadow.Margin.Top + PetShadow.Height / 2;
        groundInset = (PetVisualArea.ActualHeight - shadowLayoutCenterY) / scale;
        return true;
    }

    private void UpdateAccessoryProjection(double elapsedMs, bool advanceCamera = true)
    {
        if (accessoryNativeCenter is not { } native || BoundRuntime?.LocalGroundY is not { } ground ||
            !TryGetAccessoryPreviewLayout(out var previewTransform, out _, out var viewportHeight,
                out var shadowLayoutCenterY, out var groundInset)) return;
        var firstCamera = accessoryCamera is null;
        if (firstCamera)
        {
            accessoryCamera = new RoomViewController(standardScale: 1);
            accessoryCamera.EnableUnboundedFollow(groundInset, Math.Max(0, groundInset - 1));
        }
        accessoryCamera!.Resize(LiveSceneCanvas.ActualWidth, viewportHeight, ground);
        var displayed = new LogicalPoint(native.X + accessoryPresentationOffset.X, native.Y + accessoryPresentationOffset.Y);
        // Use the same callback clock and whole-entity interpolation as ordinary
        // windows, while retaining this preview's fixed vertical ground framing.
        var p = advanceCamera || firstCamera
            ? accessoryCamera.Update(displayed.X, elapsedMs, ReduceMotion || !SystemParameters.ClientAreaAnimation)
            : accessoryCamera.Projection;
        LiveEntityLayer.RenderTransform = new MatrixTransform(p.Scale, 0, 0, p.Scale,
            p.OffsetX + accessoryPresentationOffset.X * p.Scale,
            p.OffsetY + accessoryPresentationOffset.Y * p.Scale);
        var floor = p.WorldToScreen(new(displayed.X, ground));
        var lift = Math.Clamp(((shadowGroundCenterY ?? native.Y) - displayed.Y) / 120, 0, 1);
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
    }

    private void RefreshAccessoryFrame()
    {
        if (BoundRuntime?.LastFrame is { } frame && OwnsLiveFrameDriver) ApplyLiveFrame(frame);
        accessoryPanel.SetPreviewError(beltPresenter?.AccessoryError ?? BoundRuntime?.AccessoryError);
    }

    private void UpdateAccessoryPresentationStatus()
    {
        var error = beltPresenter?.AccessoryError ?? BoundRuntime?.AccessoryError;
        accessoryPanel.SetPreviewError(error);
        if (error is not null) LiveSceneStatusText.Text = error;
        LiveSceneStatusText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnAccessoryBodyAnchorPick(object sender, MouseButtonEventArgs e)
    {
        if (!pickingAccessoryAnchor || !IsEditing || SelectedEditor != HomeEditor.Wardrobe) return;
        if (beltPresenter is not null)
        {
            e.Handled = true; ClearLiveDirectionalInput();
            if (!beltPresenter.TryPickBodyAnchor(e.GetPosition(beltPresenter.View), out var anchor))
            { PetPreview.ToolTip = "等待有效身体画面后重试"; return; }
            accessoryPanel.SetBodyAnchor(anchor);
            pickingAccessoryAnchor = false; PetPreview.Cursor = Cursors.Arrow;
            PetPreview.ToolTip = "同一只实时物理团子";
            return;
        }
        if (BoundRuntime?.LastFrame is not { Bodies.Count: > 0 } frame) return;
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
