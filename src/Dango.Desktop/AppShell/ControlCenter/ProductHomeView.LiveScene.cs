using System.Windows;
using System.Windows.Input;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private static readonly LogicalRect HomeSurface = new(0, 0, 480, 360);
    private HostFrameScheduler? liveScheduler;
    private DirectionalControlIntent liveIntent;
    private LogicalPoint? visualAnchor;
    private double? shadowGroundCenterY;
    private double? groundedVisualOffset;
    private Window? ownerWindow;
    private bool liveSceneSuspended;
    private long porchReturnGeneration;
    private EventHandler? porchConfirmation;
    private bool porchFrameHealthy;
    internal event Action<long>? HealthyPorchFrameReady;

    internal DesktopSceneSession? BoundSceneSession { get; private set; }
    internal StackedDangoSceneRuntime? BoundRuntime { get; private set; }
    internal bool OwnsLiveFrameDriver => liveScheduler?.OwnsRuntime == true;
    internal void ReleaseLiveScene() => StopLiveScene();

    internal void SuspendForRoom()
    {
        if (IsInPorch) porchResumeX = PorchScene.PetWorldX;
        ClearLiveDirectionalInput();
        if (OwnsLiveFrameDriver) BoundRuntime!.CancelInput();
        liveSceneSuspended = true;
        StopLiveScene();
    }

    internal void ResumePorch(long generation = 0)
    {
        SetDangoAway(false);
        porchReturnGeneration = generation;
        ShowHomePage();
        EnterPorch();
        ChangeLiveSurface();
        liveSceneSuspended = false;
        RefreshLiveSceneOwnership();
    }

    internal void ResumeHomeAfterRoom()
    {
        SetDangoAway(false);
        porchReturnGeneration = 0;
        ShowHomePage();
        porchPhysicsConfigured = true; // The room also owns native sides; Home must explicitly clear them.
        ChangeLiveSurface();
        liveSceneSuspended = false;
        RefreshLiveSceneOwnership();
    }

    internal void BindLiveScene(DesktopSceneSession session, StackedDangoSceneRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(runtime);
        if (BoundRuntime is not null) throw new InvalidOperationException("主页已绑定实时场景。");
        BoundSceneSession = session;
        BoundRuntime = runtime;
        PetShadow.Visibility = Visibility.Collapsed;
        liveScheduler = new HostFrameScheduler(runtime, PrepareLiveFrame, CreateLiveInput, ApplyLiveFrame);
        liveScheduler.CallbackCompleted += PresentAccessoryPreview;
        if (appearanceSession is not null) runtime.SetAppearance(appearanceSession.DraftAppearance);
        else { standaloneDraft = runtime.EffectiveAppearance; runtime.SetAppearance(standaloneDraft); }
        RefreshAppearanceControls();
        PetPreview.ToolTip = "同一只实时物理团子";
        if (IsAccessoryPreview) ChangeLiveSurface();
        RefreshLiveSceneOwnership();
    }

    private void AttachLiveFocusSafety()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, ownerWindow)) return;
        DetachLiveFocusSafety();
        ownerWindow = window;
        if (window is not null) window.Deactivated += OnOwnerDeactivated;
    }

    private void DetachLiveFocusSafety()
    {
        if (ownerWindow is not null) ownerWindow.Deactivated -= OnOwnerDeactivated;
        ownerWindow = null;
    }

    private void OnOwnerDeactivated(object? sender, EventArgs e) => ClearLiveDirectionalInput();

    private void RefreshLiveSceneOwnership()
    {
        if (liveSceneSuspended || IsDangoAway || BoundRuntime is null || !IsLoaded || !IsVisible || IsSettingsOpen)
        {
            StopLiveScene();
            return;
        }
        if (BoundRuntime.RequiresInitialization && BoundRuntime.InitializationState != SceneInitializationState.Ready)
        {
            PorchScene.ShowUnavailable();
            LiveSceneStatusText.Text = "团子暂不可用 · 请在设置中重试";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }
        if (liveScheduler?.IsRunning == true) return;
        if (BoundRuntime.FrameDriver is not null && !ReferenceEquals(BoundRuntime.FrameDriver, liveScheduler))
        {
            PorchScene.ShowUnavailable("场景正在其他窗口显示");
            LiveSceneStatusText.Text = "场景正在其他窗口显示";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }

        var surface = IsInPorch ? PorchSurface : HomeSurface;
        BoundSceneSession!.AttachSurface(surface, Environment.TickCount64);
        BoundRuntime.SetViewport(surface);
        BoundRuntime.Start();
        if (IsAccessoryPreview) liveSurfaceNeedsAlignment = liveSurfacePlacementPending = true;
        BoundRuntime.AttachQuietIdleHost(this, true);
        BoundRuntime.UpdateWanderScreenWidth(this, IsInPorch ? PorchScene.ViewportWorldWidth : null);
        visualAnchor = null;
        shadowGroundCenterY = null;
        groundedVisualOffset = null;
        ResetAccessoryPresentation();
        liveScheduler!.CallbackCompleted -= PresentAccessoryPreview;
        liveScheduler.CallbackCompleted += PresentAccessoryPreview;
        liveScheduler!.Start();
    }

    private void StopLiveScene()
    {
        var mayStopRuntime = liveScheduler?.OwnsRuntime == true || BoundRuntime?.FrameDriver is null;
        if (mayStopRuntime && BoundRuntime is { PreviewJumpSpeedScale: not 1 })
        {
            try { BoundRuntime.SetPreviewJumpSpeedScale(1); }
            catch (Dango.Physics.DangoPhysicsException error)
            {
                liveSurfacePhysicsError = error;
                ShowLiveSurfaceUnavailable("配饰预览物理模块不可用 · 请返回设置重试");
            }
        }
        liveScheduler?.Stop();
        porchFrameHealthy = false;
        if (porchConfirmation is not null) System.Windows.Media.CompositionTarget.Rendering -= porchConfirmation;
        porchConfirmation = null;
        liveIntent = DirectionalControlIntent.None;
        ResetAccessoryPresentation();
        visualAnchor = null;
        shadowGroundCenterY = null;
        groundedVisualOffset = null;
        PetShadow.Visibility = Visibility.Collapsed;
        if (mayStopRuntime && BoundRuntime?.IsRunning == true) BoundRuntime.Stop();
    }

    private bool PrepareLiveFrame()
    {
        var runtime = BoundRuntime;
        if (!IsVisible || IsSettingsOpen || !OwnsLiveFrameDriver || !runtime!.IsRunning || BoundSceneSession?.Controller is null)
            return false;
        if (liveSurfacePlacementPending) ResetAccessoryPresentation();
        PrepareRequestedLiveSurface();
        if (!TryAlignLiveSurface()) return false;
        runtime.SetDirectionalControl(liveSurfaceNeedsAlignment ? DirectionalControlIntent.None : liveIntent);
        runtime.UpdateQuietIdleHost(this, !IsAccessoryPreview);
        return true;
    }

    private PhysicsPresentationInput CreateLiveInput()
    {
        var controller = BoundSceneSession!.Controller!;
        return PhysicsPresentationInput.FromSnapshot(controller.Snapshot, controller.SquashAmount,
            controller.GroundFrictionCue, controller.FacingDirection, null);
    }

    private void ApplyLiveFrame(StackedPhysicsPresentationFrame frame)
    {
        porchFrameHealthy = false;
        if (BoundRuntime is null) return;
        if (frame.Bodies.Count == 0 || !frame.Bodies[0].IsVisible || frame.ConstraintsReleased || BoundRuntime.LastError is not null ||
            frame.Bodies[0].FallbackReason != PhysicsPresentationFallbackReason.None)
        {
            ResetAccessoryPresentation();
            LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
            Dango.Desktop.Accessories.AccessoryHostBinding.Hide(LiveDangoVisual);
            PetShadow.Visibility = Visibility.Collapsed;
            PorchScene.ShowUnavailable();
            LiveSceneStatusText.Text = "团子暂不可用 · 请在设置中重试";
            LiveSceneStatusText.Visibility = Visibility.Visible;
            return;
        }
        if (IsInPorch)
        {
            PetShadow.Visibility = Visibility.Collapsed;
            if (liveSurfaceNeedsAlignment || liveSurfaceFailure is not null)
            {
                PorchScene.ShowUnavailable(liveSurfaceFailure ?? "场景准备中");
                return;
            }
            var now = Environment.TickCount64;
            PorchScene.ApplyFrame(frame, BoundRuntime, now - lastPorchFrame, ReduceMotion || !SystemParameters.ClientAreaAnimation);
            lastPorchFrame = now;
            UpdateAccessoryPresentationStatus();
            porchFrameHealthy = PorchScene.IsVisible && PorchScene.ActualWidth > 0 && PorchScene.ActualHeight > 0 &&
                PorchScene.LiveBody.IsVisible && PorchScene.LiveBody.LastContourFailure == DangoContourFailure.None;
            ConfirmHealthyPorchFrame();
            return;
        }
        if (IsAccessoryPreview)
        {
            ApplyAccessoryPreviewFrame(frame);
            return;
        }
        var bounds = DangoHostFrameBinding.Bounds(frame, 0);
        visualAnchor ??= bounds.Center;
        var grounded = BoundRuntime.LastPhysicsFrame is { Succeeded: true } physics &&
            physics.Bodies.Any(body => body.EntityId == BoundRuntime.EntityIds[0] && body.IsGrounded);
        if (grounded)
        {
            // Startup and porch return can attach while still falling. Use the
            // real grounded frame for both anchors, including settling, so
            // that home has one floor after either surface attaches.
            visualAnchor = bounds.Center;
            shadowGroundCenterY = bounds.Center.Y;
        }
        const double dx = 0;
        var dy = Math.Clamp((bounds.Center.Y - visualAnchor.Value.Y) * .4, -42, 42);
        DangoHostFrameBinding.Apply(LiveDangoVisual, LiveDangoHat, frame, 0,
            new LogicalVector(-bounds.X + dx, -bounds.Y + dy),
            appearance: BoundRuntime.DisplayHatAppearance,
            hatSize: BoundRuntime.DisplayHatSize,
            bodyFillOverride: BoundRuntime.BodyPreviewFill, eyeFillOverride: BoundRuntime.EyePreviewFill, appearanceEntity: BoundRuntime.AppearanceEntity,
            runtime: BoundRuntime);
        if (grounded && LiveDangoVisual.RenderedBodyBottom is { } bodyBottom && PetPreview.Width > 0)
        {
            // Align the rendered contour, rather than its larger preview box,
            // to the fixed shadow. Keep one DIP of softness at the contact.
            var scale = PetPreview.Width / LiveSceneCanvas.Width;
            var contactY = LiveSceneCanvas.Height + (PetShadow.Height / 2 - 1) / scale;
            groundedVisualOffset = contactY - bodyBottom;
        }
        System.Windows.Controls.Canvas.SetTop(LiveDangoVisual, dy + (groundedVisualOffset ?? 0));
        System.Windows.Controls.Canvas.SetTop(LiveDangoHat, dy + (groundedVisualOffset ?? 0));
        Dango.Desktop.Accessories.AccessoryHostBinding.Position(LiveDangoVisual, dx, dy + (groundedVisualOffset ?? 0));
        var lift = shadowGroundCenterY is { } floor
            ? Math.Clamp((floor - bounds.Center.Y) * .4 / 42, 0, 1) : 0;
        PetShadowScale.ScaleX = 1 - .32 * lift;
        PetShadowScale.ScaleY = 1 - .15 * lift;
        PetShadow.Opacity = .17 - .09 * lift;
        PetShadow.Visibility = shadowGroundCenterY.HasValue && LiveDangoVisual.LastContourFailure == DangoContourFailure.None
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateAccessoryPresentationStatus();
    }

    private void ConfirmHealthyPorchFrame()
    {
        if (!porchFrameHealthy || porchReturnGeneration == 0 || porchConfirmation is not null) return;
        var attempt = porchReturnGeneration;
        porchConfirmation = (_, _) =>
        {
            System.Windows.Media.CompositionTarget.Rendering -= porchConfirmation;
            porchConfirmation = null;
            Dispatcher.BeginInvoke(() =>
            {
                if (!porchFrameHealthy || porchReturnGeneration != attempt || !IsInPorch || !IsVisible || !OwnsLiveFrameDriver ||
                    !PorchScene.IsVisible || PorchScene.ActualWidth <= 0 || PorchScene.ActualHeight <= 0 || !PorchScene.LiveBody.IsVisible ||
                    liveSurfaceNeedsAlignment || liveSurfaceFailure is not null || BoundRuntime?.LastError is not null ||
                    PorchScene.LiveBody.LastContourFailure != DangoContourFailure.None ||
                    PresentationSource.FromVisual(PorchScene) is null) return;
                porchReturnGeneration = 0;
                HealthyPorchFrameReady?.Invoke(attempt);
            }, System.Windows.Threading.DispatcherPriority.Render);
        };
        System.Windows.Media.CompositionTarget.Rendering += porchConfirmation;
    }

    private void SetLivePreviewColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        LiveDangoVisual.BodyFill = brush;
        BoundRuntime?.SetBodyPreviewColor(color);
    }

    private bool SetLiveDirectionalKey(Key key, bool down, bool repeat)
    {
        if (liveScheduler?.OwnsRuntime != true) return false;
        if (!IsInPorch && !IsAccessoryPreview && key is (Key.A or Key.D)) return false;
        // W is reserved inside the accessory preview; it must not navigate or
        // change a Space jump that is already being held.
        if (IsAccessoryPreview && key == Key.W) return true;
        // A new Space belongs to the button, but release of our already-held
        // jump must still clear intent after focus moves within this view.
        if (key == Key.Space && Keyboard.FocusedElement is System.Windows.Controls.Button && (down || !liveIntent.Jump)) return false;
        if (repeat && down) return key is Key.A or Key.D or Key.S or Key.Space;
        liveIntent = key switch
        {
            Key.A => liveIntent with { Left = down },
            Key.D => liveIntent with { Right = down },
            Key.S => liveIntent with { Squash = down },
            Key.Space => liveIntent with { Jump = down },
            _ => liveIntent,
        };
        return key is Key.A or Key.D or Key.S or Key.Space;
    }

    private void ClearLiveDirectionalInput()
    {
        liveIntent = DirectionalControlIntent.None;
        if (OwnsLiveFrameDriver) BoundRuntime!.SetDirectionalControl(liveIntent);
    }

    private void UpdateLiveMovementMode()
    {
        ResetAccessoryPresentation();
        if (!OwnsLiveFrameDriver) return;
        BoundRuntime!.UpdateWanderScreenWidth(this, IsInPorch ? PorchScene.ViewportWorldWidth : null);
        BoundRuntime.UpdateQuietIdleHost(this, !IsAccessoryPreview);
        visualAnchor = null;
        shadowGroundCenterY = null;
        groundedVisualOffset = null;
        PetShadow.Visibility = Visibility.Collapsed;
    }

    private void OnStageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSettingsOpen && (ReferenceEquals(e.OriginalSource, Stage) || IsInPorch ||
            IsAccessoryPreview && e.OriginalSource is DependencyObject source && Presence.IsAncestorOf(source))) Stage.Focus();
    }
}
