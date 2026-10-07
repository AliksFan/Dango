using System.Windows;
using Dango.Desktop.AppShell.Rooms;
using Dango.Core.Coordinates;
using Dango.Physics;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private static readonly LogicalRect PorchSurface = new(0, 0, PorchSceneView.WorldWidth, PorchSceneView.WorldHeight);
    private readonly RoomNavigationCoordinator roomNavigation = new();
    private double porchResumeX = 560;
    private long lastPorchFrame;
    private bool liveSurfaceNeedsAlignment;
    private bool liveSurfacePlacementPending;
    private bool porchPhysicsConfigured;
    private bool liveSurfaceRuntimeFault;
    private string? liveSurfaceFailure;
    private DangoPhysicsException? liveSurfacePhysicsError;

    internal RoomNavigationCoordinator RoomNavigation => roomNavigation;
    internal event Action? LocalRoomRequested;
    internal void EnableLocalRoomDoor() => PorchScene.SetLocalRoomEnabled(true);

    internal void SetPorchReviewMode(bool enabled)
    {
        PorchScene.SetReviewMode(enabled);
        PorchReviewNotice.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InitializePorchNavigation()
    {
        PorchScene.DoorSelected += id => roomNavigation.SelectDoor(id);
        PorchScene.GameplayFocusRequested += () => Stage.Focus();
        PorchScene.LocalRoomRequested += () => LocalRoomRequested?.Invoke();
    }

    private void ChangeLiveSurface()
    {
        if (BoundRuntime is null) return;
        liveSurfacePlacementPending = true;
        liveSurfaceFailure = null;
        liveSurfacePhysicsError = null;
        liveSurfaceNeedsAlignment = IsInPorch || porchPhysicsConfigured || IsAccessoryPreview || accessoryPhysicsConfigured;
        PorchScene.ResetProjection(porchResumeX);
        if (!OwnsLiveFrameDriver)
        {
            if (BoundRuntime is not null && IsInPorch) PorchScene.ShowUnavailable();
            return;
        }
        PrepareRequestedLiveSurface();
    }

    private void PrepareRequestedLiveSurface()
    {
        if (!liveSurfacePlacementPending) return;
        var target = IsInPorch ? PorchSurface : HomeSurface;
        BoundSceneSession!.AttachSurface(target, Environment.TickCount64);
        var bounds = BoundSceneSession.Controller!.Snapshot.Dango.VisualBounds;
        BoundSceneSession.Controller.MoveTo(new LogicalPoint(IsInPorch ? porchResumeX - bounds.Width / 2 : 80,
            target.Bottom - bounds.Height));
        BoundRuntime!.SetViewport(target);
        liveSurfacePlacementPending = false;
        TryAlignLiveSurface();
        lastPorchFrame = Environment.TickCount64;
    }

    private bool TryAlignLiveSurface()
    {
        // A latched runtime fault is expected recovery state, not a rehost error
        // to invoke through EnsureHealthy. Keep local navigation available.
        if (BoundRuntime!.LastError is not null)
        {
            liveSurfaceRuntimeFault = true;
            return ShowLiveSurfaceUnavailable("团子暂不可用 · 请在设置中重试");
        }
        if (liveSurfaceRuntimeFault)
        {
            // Only the explicit runtime recovery clears its fault latch. This
            // must not release a separate missing-native-capability failure.
            liveSurfaceRuntimeFault = false;
            liveSurfaceFailure = null;
            liveSurfaceNeedsAlignment = IsInPorch || porchPhysicsConfigured || IsAccessoryPreview || accessoryPhysicsConfigured;
        }
        if (!liveSurfaceNeedsAlignment) return liveSurfaceFailure is null;
        try
        {
            var controller = BoundSceneSession!.Controller!;
            var bounds = controller.Snapshot.Dango.VisualBounds;
            var center = bounds.Center;
            if (IsInPorch && BoundRuntime.LastFrame is not null)
            {
                // The body can relax while Home has no walls. Fit the actual
                // node-circle footprint once on handoff, never during walking.
                if (!BoundRuntime.TryGetHorizontalPlacement(center, 24, PorchSurface.Right - 24, out var safeCenter))
                    return ShowLiveSurfaceUnavailable("门廊放置不可用 · 请返回设置重试");
                controller.MoveTo(new LogicalPoint(safeCenter.X - bounds.Width / 2, bounds.Y));
                center = controller.Snapshot.Dango.VisualBounds.Center;
                porchResumeX = center.X;
                PorchScene.ResetProjection(center.X);
            }
            if (accessoryPhysicsConfigured) BoundRuntime.SetUnboundedHorizontalPreview(false);
            if (!BoundRuntime!.RehostSelectedEntityAt(center)) return true; // No first native frame yet; keep input clear.
            if (IsInPorch)
            {
                BoundRuntime.SetHorizontalBounds(24, PorchSurface.Right - 24);
                porchPhysicsConfigured = true;
            }
            else
            {
                BoundRuntime.ClearHorizontalBounds();
                porchPhysicsConfigured = false;
            }
            BoundRuntime.SetUnboundedHorizontalPreview(IsAccessoryPreview);
            accessoryPhysicsConfigured = IsAccessoryPreview;
            liveSurfaceNeedsAlignment = false;
            return true;
        }
        catch (DangoPhysicsException error)
        {
            liveSurfacePhysicsError = error;
            liveSurfaceNeedsAlignment = false;
            return ShowLiveSurfaceUnavailable(IsAccessoryPreview ? "配饰预览物理模块不可用 · 请返回设置重试" : "门廊物理模块不可用 · 请返回设置重试");
        }
    }

    private bool ShowLiveSurfaceUnavailable(string message)
    {
        liveSurfaceFailure = message;
        LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
        Dango.Desktop.Accessories.AccessoryHostBinding.Hide(LiveDangoVisual);
        PetShadow.Visibility = Visibility.Collapsed;
        LiveSceneStatusText.Text = message;
        LiveSceneStatusText.Visibility = Visibility.Visible;
        PorchScene.ShowUnavailable(message);
        ClearLiveDirectionalInput();
        return false;
    }
}
