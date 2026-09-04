using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Animation;

[TestClass]
public sealed class SingleDangoMotionControllerTests
{
    [TestMethod]
    public void DirectionalControlStartsIdleAndAcceleratesThenDecelerates()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));

        controller.AdvanceDirectionalControl(new DirectionalControlIntent(false, true, false, false), TimeSpan.FromMilliseconds(500), 500);
        Assert.AreEqual(DangoActionState.Wander, controller.Snapshot.Dango.State);
        Assert.AreEqual(55, controller.HorizontalVelocityDipsPerSecond, 0.001);

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(500), 1000);
        Assert.AreEqual(DangoActionState.Idle, controller.Snapshot.Dango.State);
        Assert.AreEqual(0, controller.HorizontalVelocityDipsPerSecond, 0.001);
    }

    [TestMethod]
    public void HorizontalMovementUpdatesFacingAndKeepsLastFacingAfterStopping()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        Assert.AreEqual(DangoFacingDirection.Center, controller.FacingDirection);

        controller.AdvanceDirectionalControl(
            new DirectionalControlIntent(true, false, false, false),
            TimeSpan.FromMilliseconds(16),
            16);
        Assert.AreEqual(DangoFacingDirection.Left, controller.FacingDirection);

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(250), 266);
        Assert.AreEqual(0, controller.HorizontalVelocityDipsPerSecond, 0.001);
        Assert.AreEqual(DangoFacingDirection.Left, controller.FacingDirection);

        controller.AdvanceDirectionalControl(
            new DirectionalControlIntent(false, true, false, false),
            TimeSpan.FromMilliseconds(16),
            282);
        Assert.AreEqual(DangoFacingDirection.Right, controller.FacingDirection);
    }

    [TestMethod]
    public void DirectionalJumpMovesForwardAndIgnoresAirSteering()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var startX = controller.Snapshot.Dango.VisualBounds.X;

        controller.AdvanceDirectionalControl(
            new DirectionalControlIntent(false, true, true, false),
            TimeSpan.FromMilliseconds(16),
            0);
        controller.AdvanceDirectionalControl(
            new DirectionalControlIntent(true, false, true, false),
            TimeSpan.FromMilliseconds(320),
            320);

        Assert.IsGreaterThan(startX, controller.Snapshot.Dango.VisualBounds.X);
        Assert.AreEqual(DangoFacingDirection.Right, controller.FacingDirection);

        controller.AdvanceDirectionalControl(
            new DirectionalControlIntent(true, false, true, false),
            TimeSpan.FromMilliseconds(320),
            640);
        Assert.AreEqual(startX + 35.2, controller.Snapshot.Dango.VisualBounds.X, 0.001);
        Assert.AreEqual(1, controller.TakeoffCount);
    }

    [TestMethod]
    public void BothHorizontalDirectionsProduceAStationaryJump()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var startX = controller.Snapshot.Dango.VisualBounds.X;
        var held = new DirectionalControlIntent(true, true, true, false);

        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(16), 0);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(320), 320);

        Assert.AreEqual(startX, controller.Snapshot.Dango.VisualBounds.X, 0.001);
        Assert.AreEqual(DangoFacingDirection.Center, controller.FacingDirection);
    }

    [TestMethod]
    public void JumpCannotRepeatInAirAndHeldJumpWaitsForLandingCooldown()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var held = new DirectionalControlIntent(false, false, true, false);

        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(16), 0);
        Assert.AreEqual(DangoActionState.Jumping, controller.Snapshot.Dango.State);
        var firstTakeoff = controller.TakeoffCount;

        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(320), 320);
        Assert.AreEqual(firstTakeoff, controller.TakeoffCount);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(320), 640);
        Assert.AreEqual(DangoActionState.Landing, controller.Snapshot.Dango.State);

        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(449), 1089);
        Assert.AreEqual(firstTakeoff, controller.TakeoffCount);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(1), 1090);
        Assert.AreEqual(firstTakeoff + 1, controller.TakeoffCount);
        Assert.AreEqual(DangoActionState.Jumping, controller.Snapshot.Dango.State);
    }

    [TestMethod]
    public void JumpPressedAfterReleaseInAirIsNotQueuedForLanding()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var held = new DirectionalControlIntent(false, false, true, false);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(16), 0);
        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(16), 100);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(16), 200);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(16), 640);
        controller.AdvanceDirectionalControl(held, TimeSpan.FromMilliseconds(16), 1090);

        Assert.AreEqual(1, controller.TakeoffCount);
        Assert.AreEqual(DangoActionState.Landing, controller.Snapshot.Dango.State);
    }

    [TestMethod]
    public void SquashIsBoundedAndNeverChangesAuthoritativeBounds()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var original = controller.Snapshot.Dango.VisualBounds;

        controller.AdvanceDirectionalControl(new DirectionalControlIntent(false, false, false, true), TimeSpan.FromMilliseconds(120), 120);
        Assert.AreEqual(1, controller.SquashAmount, 0.001);
        Assert.AreEqual(original, controller.Snapshot.Dango.VisualBounds);

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(160), 280);
        Assert.AreEqual(0, controller.SquashAmount, 0.001);
        Assert.AreEqual(original, controller.Snapshot.Dango.VisualBounds);
    }

    [TestMethod]
    public void HorizontalInputCanLeaveLandingWithoutAnInvalidTransition()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        controller.BeginDrag(new LogicalPoint(125, 140), 0);
        controller.ReleaseDrag(10);
        controller.ConfirmLanding(new LogicalPoint(150, 200), 20);

        controller.AdvanceDirectionalControl(
            new DirectionalControlIntent(false, true, false, false),
            TimeSpan.FromMilliseconds(16),
            36);

        Assert.AreEqual(DangoActionState.Wander, controller.Snapshot.Dango.State);
    }

    [TestMethod]
    public void DefaultGroundFrictionStopsFromMaximumSpeedWithinTwoHundredMilliseconds()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var right = new DirectionalControlIntent(false, true, false, false);
        controller.AdvanceDirectionalControl(right, TimeSpan.FromSeconds(1), 1000);

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(150), 1150);
        Assert.IsGreaterThan(0, controller.HorizontalVelocityDipsPerSecond);
        Assert.IsGreaterThan(0, controller.GroundFrictionCue);

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(50), 1200);
        Assert.AreEqual(0, controller.HorizontalVelocityDipsPerSecond, 0.001);
        Assert.IsGreaterThan(0, controller.GroundFrictionCue);

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(140), 1340);
        Assert.AreEqual(0, controller.GroundFrictionCue, 0.001);
    }

    [TestMethod]
    public void SurfaceFrictionProfileChangesStoppingDistanceWithoutChangingInputSemantics()
    {
        var slick = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var carpet = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        slick.SetGroundSurfaceProfile(new GroundSurfaceMotionProfile("slick-floor", 0.25));
        carpet.SetGroundSurfaceProfile(new GroundSurfaceMotionProfile("carpet", 1.5));
        var right = new DirectionalControlIntent(false, true, false, false);
        slick.AdvanceDirectionalControl(right, TimeSpan.FromSeconds(1), 1000);
        carpet.AdvanceDirectionalControl(right, TimeSpan.FromSeconds(1), 1000);

        slick.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(100), 1100);
        carpet.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(100), 1100);

        Assert.IsGreaterThan(carpet.HorizontalVelocityDipsPerSecond, slick.HorizontalVelocityDipsPerSecond);
        Assert.AreEqual("slick-floor", slick.GroundSurfaceProfile.SurfaceId);
        Assert.AreEqual("carpet", carpet.GroundSurfaceProfile.SurfaceId);
    }

    [TestMethod]
    public void SurfaceFrictionProfileRejectsUnsafeCoefficient()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new GroundSurfaceMotionProfile("invalid", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new GroundSurfaceMotionProfile("invalid", 3.1));
    }

    [TestMethod]
    public void DragReleaseAndLandingKeepFinalPositionAuthoritativeAndInsideWorkArea()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));

        controller.BeginDrag(new LogicalPoint(125, 140), 100);
        controller.MoveDrag(new LogicalPoint(900, 700));
        controller.ReleaseDrag(200);

        Assert.AreEqual(DangoActionState.Falling, controller.Snapshot.Dango.State);
        Assert.AreEqual(700, controller.Snapshot.Dango.VisualBounds.X);
        Assert.AreEqual(500, controller.Snapshot.Dango.VisualBounds.Y);

        controller.ConfirmLanding(new LogicalPoint(520, 580), 300);

        Assert.AreEqual(DangoActionState.Landing, controller.Snapshot.Dango.State);
        Assert.AreEqual(new LogicalPoint(520, 580), controller.Snapshot.Dango.GroundContactPoint);
        Assert.AreEqual(470, controller.Snapshot.Dango.VisualBounds.X);
        Assert.AreEqual(480, controller.Snapshot.Dango.VisualBounds.Y);
    }

    [TestMethod]
    public void ReleasedDragFallsContinuouslyBeforeLanding()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        controller.BeginDrag(new LogicalPoint(125, 140), 100);
        controller.MoveDrag(new LogicalPoint(225, 90));
        controller.ReleaseDrag(200);
        var releasedTop = controller.Snapshot.Dango.VisualBounds.Y;

        controller.AdvanceDirectionalControl(DirectionalControlIntent.None, TimeSpan.FromMilliseconds(16), 216);

        Assert.AreEqual(DangoActionState.Falling, controller.Snapshot.Dango.State);
        Assert.IsGreaterThan(releasedTop, controller.Snapshot.Dango.VisualBounds.Y);
        Assert.IsGreaterThan(controller.Snapshot.Dango.VisualBounds.Y, 500);

        var previousTop = controller.Snapshot.Dango.VisualBounds.Y;
        for (var index = 0; index < 120 && controller.Snapshot.Dango.State == DangoActionState.Falling; index++)
        {
            controller.AdvanceDirectionalControl(
                DirectionalControlIntent.None,
                TimeSpan.FromMilliseconds(16),
                232 + (index * 16));
            Assert.IsGreaterThanOrEqualTo(previousTop, controller.Snapshot.Dango.VisualBounds.Y);
            previousTop = controller.Snapshot.Dango.VisualBounds.Y;
        }

        Assert.AreEqual(DangoActionState.Landing, controller.Snapshot.Dango.State);
        Assert.AreEqual(600, controller.Snapshot.Dango.VisualBounds.Bottom, 0.001);
    }

    [TestMethod]
    public void FallingDoesNotBecomeLandingWithoutAnExplicitCoreLandingCommand()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        controller.BeginDrag(new LogicalPoint(125, 140), 100);
        controller.ReleaseDrag(200);

        for (var index = 0; index < 600; index++) controller.ObservePresentationFrame();

        Assert.AreEqual(DangoActionState.Falling, controller.Snapshot.Dango.State);
    }

    [TestMethod]
    public void HideAndExitCancelDragAndPreventFurtherMotion()
    {
        var hidden = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        hidden.BeginDrag(new LogicalPoint(125, 140), 100);
        hidden.Hide(200);
        Assert.AreEqual(DangoActionState.Hidden, hidden.Snapshot.Dango.State);
        Assert.IsFalse(hidden.IsDragging);

        var exited = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        exited.Exit(300);
        Assert.IsTrue(exited.IsExited);
        Assert.ThrowsExactly<InvalidActionTransitionException>(() => exited.StartAction(DangoActionState.Idle, 400));
    }

    [TestMethod]
    public void CompleteSingleDangoActionPathUsesTheCoreStateMachineAndUpdatesClips()
    {
        var controller = new SingleDangoMotionController(CreateScene(), new LogicalRect(0, 0, 800, 600));
        var path = new[]
        {
            DangoActionState.Wander,
            DangoActionState.ApproachIsland,
            DangoActionState.ClimbUp,
            DangoActionState.OnIsland,
            DangoActionState.ClimbDown,
            DangoActionState.Falling,
            DangoActionState.Landing,
            DangoActionState.Idle,
            DangoActionState.Reacting,
            DangoActionState.Idle,
        };

        for (var index = 0; index < path.Length; index++)
        {
            controller.StartAction(path[index], index + 1);
        }

        Assert.AreEqual(DangoActionState.Idle, controller.Snapshot.Dango.State);
        Assert.AreEqual("idle", controller.Snapshot.Dango.ClipId);
        Assert.ThrowsExactly<InvalidActionTransitionException>(
            () => controller.StartAction(DangoActionState.Landing, 20));
    }

    private static SceneSnapshot CreateScene()
    {
        var dango = new DangoSnapshot(
            "motion-test", new LogicalRect(100, 100, 100, 100), DangoActionState.Idle, 0, 17,
            new LogicalPoint(150, 200), "idle", "click", "drag", "collision", "island-grab");
        var island = new IslandSnapshot(
            "island", new LogicalRect(300, 300, 300, 100), new LogicalRect(320, 300, 260, 20),
            new LogicalRect(300, 285, 300, 30), new LogicalRect(320, 300, 260, 30), new LogicalRect(300, 330, 300, 70));
        return new SceneSnapshot(1, 0, 17, "test", dango, island);
    }
}
