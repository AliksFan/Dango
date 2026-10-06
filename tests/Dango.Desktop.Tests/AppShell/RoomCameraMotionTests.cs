using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomCameraMotionTests
{
    private const double Width = 640;
    private const double InitialPet = 1080;
    private const double InitialPetY = 300;

    private static RoomViewController Create(RoomCameraMode mode = RoomCameraMode.Follow)
    {
        var camera = new RoomViewController();
        camera.EnableWindowCamera();
        camera.SelectCameraMode(mode);
        camera.Resize(Width, 600, 360);
        camera.Update(new LogicalPoint(InitialPet, InitialPetY), 0);
        return camera;
    }

    [TestMethod]
    public void WindowCameraIsExplicitOptInAndLegacyDeadZoneRemainsUnchanged()
    {
        var camera = new RoomViewController();
        camera.Resize(Width, 600, 360);
        camera.Update(InitialPet, 0);
        camera.SelectCameraMode(RoomCameraMode.Manual);
        var before = camera.CameraX;
        var visibleWidth = Width / camera.Scale;
        camera.Update(before + visibleWidth * .55, 100, true);
        Assert.AreEqual(before, camera.CameraX, 1e-10);
        camera.Update(before + visibleWidth * .7, 0, true);
        Assert.AreEqual(before + visibleWidth * .1, camera.CameraX, 1e-10,
            "Selecting a camera mode must not enable the new window algorithm on another host.");
        Assert.IsFalse(camera.PanByScreenDelta(50));
    }

    [TestMethod]
    [DataRow((int)RoomCameraMode.Follow)]
    [DataRow((int)RoomCameraMode.EdgeFollow)]
    [DataRow((int)RoomCameraMode.Manual)]
    public void FirstWindowFrameCentersTheRealPetInEveryMode(int modeValue)
    {
        var mode = (RoomCameraMode)modeValue;
        var camera = new RoomViewController();
        camera.EnableWindowCamera();
        camera.SelectCameraMode(mode);
        camera.Resize(Width, 600, 417);
        camera.Update(700, 0);
        Assert.AreEqual(Width / 2, camera.Projection.WorldToScreen(new(700, 417)).X, 1e-10);
        Assert.AreEqual(536, camera.Projection.WorldToScreen(new(700, 417)).Y, 1e-10);
    }

    [TestMethod]
    public void FollowAllowsMovingLagAndReturnsAStoppedPetToTheCenter()
    {
        var camera = Create();
        var before = camera.CameraX;
        const double pet = InitialPet + 40; // Inside the legacy dead zone, but Follow must still respond.
        camera.Update(pet, 100);
        Assert.IsTrue(camera.CameraX > before);
        Assert.IsTrue(camera.Projection.WorldToScreen(new(pet, 360)).X > Width / 2,
            "A moving target must be allowed to lead the camera rather than snap to the center.");
        for (var i = 0; i < 40; i++) camera.Update(pet, 100);
        Assert.AreEqual(Width / 2, camera.Projection.WorldToScreen(new(pet, 360)).X, .001,
            "Once the pet stops, Follow must finish returning it to the center.");
    }

    [TestMethod]
    public void EdgeFollowWaitsUntilTwentyEightyAndCompletesItsRetainedTarget()
    {
        var camera = Create(RoomCameraMode.EdgeFollow);
        var initial = camera.CameraX;
        var visibleWidth = Width / camera.Scale;
        camera.Update(initial + visibleWidth * .75, 100);
        Assert.AreEqual(initial, camera.CameraX, 1e-10,
            "The less active mode must not reuse the old 40–60 percent dead zone.");
        var pet = initial + visibleWidth * .85;
        camera.Update(pet, 100);
        Assert.IsTrue(camera.CameraX > initial && camera.CameraX < pet - visibleWidth * .8);
        var afterTrigger = camera.CameraX;
        for (var i = 0; i < 40; i++) camera.Update(initial + visibleWidth * .7, 100);
        Assert.IsTrue(camera.CameraX > afterTrigger);
        Assert.AreEqual(pet - visibleWidth * .8, camera.CameraX, .001,
            "Moving inside the safe area must not erase the outstanding follow target.");
    }

    [TestMethod]
    public void ManualPanUsesNaturalScreenDirectionAndDoesNotFollowThePet()
    {
        var camera = Create(RoomCameraMode.Manual);
        var before = camera.CameraX;
        Assert.IsTrue(camera.PanByScreenDelta(56));
        Assert.AreEqual(before - 56 / camera.Scale, camera.CameraX, 1e-10);
        var afterPan = camera.CameraX;
        for (var i = 0; i < 20; i++) camera.Update(2000, 100);
        Assert.AreEqual(afterPan, camera.CameraX, 1e-10);
        Assert.IsTrue(camera.PanByScreenDelta(-28));
        Assert.AreEqual(afterPan + 28 / camera.Scale, camera.CameraX, 1e-10);
        Assert.IsFalse(camera.PanByScreenDelta(0));
        camera.SelectCameraMode(RoomCameraMode.Follow);
        Assert.IsFalse(camera.PanByScreenDelta(28));
    }

    [TestMethod]
    public void ChangingModeKeepsTheViewAndClearsMomentum()
    {
        var camera = Create();
        for (var i = 1; i <= 30; i++) camera.Update(InitialPet + i * 3, 1000d / 60);
        var before = camera.CameraX;
        camera.SelectCameraMode(RoomCameraMode.Manual);
        Assert.AreEqual(before, camera.CameraX, 1e-10);
        camera.Update(1500, 100);
        Assert.AreEqual(before, camera.CameraX, 1e-10);
        camera.SelectCameraMode(RoomCameraMode.EdgeFollow);
        camera.Update(before + Width / camera.Scale / 2, 100);
        Assert.AreEqual(before, camera.CameraX, 1e-10,
            "The previous follow velocity must not leak into the newly selected mode.");
    }

    [TestMethod]
    public void ResetMotionKeepsTheViewButDoesNotReplayOldVelocity()
    {
        var camera = Create();
        for (var i = 1; i <= 30; i++) camera.Update(InitialPet + i * 4, 1000d / 60);
        var before = camera.CameraX;
        camera.ResetMotion();
        Assert.AreEqual(before, camera.CameraX, 1e-10);
        camera.Update(1200, 0);
        camera.Update(1200, .1);
        Assert.IsTrue(camera.CameraX - before < .001,
            "Recovery must begin from rest rather than continue the old moving camera's velocity.");
    }

    [TestMethod]
    public void ManualZoomResizeAndPresetChangesPreserveTheWorldCenterBeforeClamping()
    {
        var camera = Create(RoomCameraMode.Manual);
        camera.PanByScreenDelta(-280);
        var center = Center(camera, Width);
        Assert.IsTrue(camera.ApplyZoomSteps(1));
        Assert.AreEqual(center, Center(camera, Width), 1e-10);
        camera.Resize(960, 600, 360);
        Assert.AreEqual(center, Center(camera, 960), 1e-10);
        camera.SelectPreset(RoomViewPreset.Near);
        Assert.AreEqual(center, Center(camera, 960), 1e-10);
        Assert.AreEqual(RoomCameraMode.Manual, camera.CameraMode);
        Assert.IsTrue(Math.Abs(center - InitialPet) > 100,
            "A view change must not silently recenter a manually panned camera on the pet.");
        camera.PanByScreenDelta(-double.MaxValue);
        var boundaryCenter = Center(camera, 960);
        camera.Resize(1400, 600, 360);
        Assert.AreEqual(Math.Clamp(boundaryCenter - 1400 / camera.Scale / 2, 0,
            camera.WorldWidth - 1400 / camera.Scale), camera.CameraX, 1e-10);
    }

    [TestMethod]
    public void LinearMovingAndReversingFollowTargetsAgreeAtSixtyOneTwentyAndOneFortyFourHz()
    {
        var samples = new List<(double X, double Y)[]>();
        foreach (var rate in new[] { 60, 120, 144 })
        {
            var camera = Create();
            var checkpoints = new List<(double X, double Y)>();
            for (var frame = 1; frame <= rate * 2; frame++)
            {
                var seconds = frame / (double)rate;
                var pet = seconds <= 1 ? InitialPet + 120 * seconds : InitialPet + 120 * (2 - seconds);
                var petY = seconds <= 1 ? InitialPetY - 60 * seconds : InitialPetY - 60 * (2 - seconds);
                camera.Update(new LogicalPoint(pet, petY), 1000d / rate);
                if (frame % (rate / 2) == 0) checkpoints.Add((camera.CameraX, camera.CameraY));
            }
            samples.Add(checkpoints.ToArray());
        }
        for (var rateIndex = 1; rateIndex < samples.Count; rateIndex++)
            for (var checkpoint = 0; checkpoint < samples[0].Length; checkpoint++)
            {
                Assert.AreEqual(samples[0][checkpoint].X, samples[rateIndex][checkpoint].X, 1e-7,
                    "The same piecewise-linear presentation trajectory must not depend on the render sampling rate.");
                Assert.AreEqual(samples[0][checkpoint].Y, samples[rateIndex][checkpoint].Y, 1e-7);
            }
        Assert.IsTrue(samples[0][1].X > samples[0][0].X);
        Assert.IsTrue(samples[0][3].X < samples[0][2].X, "The camera must eventually reverse with the returning pet.");
        Assert.IsTrue(samples[0][1].Y < samples[0][0].Y);
        Assert.IsTrue(samples[0][3].Y > samples[0][2].Y);
    }

    [TestMethod]
    public void ATargetReversalDoesNotInstantlyReplaceTheCameraVelocity()
    {
        var camera = Create();
        for (var i = 1; i <= 30; i++) camera.Update(InitialPet + i * 4, 1000d / 60);
        var before = camera.CameraX;
        camera.Update(1200.024, .1);
        var velocityBeforeReversal = (camera.CameraX - before) / .0001;
        before = camera.CameraX;
        camera.Update(1160, .1);
        var velocityAfterReversal = (camera.CameraX - before) / .0001;
        Assert.IsTrue(velocityBeforeReversal > 0);
        Assert.IsTrue(Math.Abs(velocityAfterReversal - velocityBeforeReversal) < 2,
            "A changed target may change acceleration, but must not instantly replace the stored camera velocity.");
    }

    [TestMethod]
    [DataRow((int)RoomCameraMode.Follow)]
    [DataRow((int)RoomCameraMode.EdgeFollow)]
    [DataRow((int)RoomCameraMode.Manual)]
    public void EveryModeClampsToWorldEdgesAndWholeViewHasNoHorizontalMotion(int modeValue)
    {
        var mode = (RoomCameraMode)modeValue;
        var camera = Create(mode);
        foreach (var pet in new[] { -500d, 5000d, -500d })
            for (var frame = 0; frame < 40; frame++)
            {
                if (mode == RoomCameraMode.Manual) camera.PanByScreenDelta(pet < 0 ? 10000 : -10000);
                camera.Update(pet, 100);
                Assert.IsTrue(camera.CameraX >= 0 && camera.CameraX <= camera.WorldWidth - Width / camera.Scale);
            }
        camera.SelectPreset(RoomViewPreset.Whole);
        var previousY = camera.CameraY;
        camera.Update(2000, 100);
        Assert.AreEqual(0, camera.CameraX, 1e-10);
        Assert.AreEqual(previousY, camera.CameraY, 1e-10, "The compatible X-only call must not invent vertical input.");
        Assert.IsFalse(camera.PanByScreenDelta(50));
    }

    [TestMethod]
    public void InvalidModeAndPanCannotPoisonTheProjectionAndNonpositiveTimeCannotMoveIt()
    {
        var camera = Create(RoomCameraMode.Manual);
        var before = camera.Projection;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.SelectCameraMode((RoomCameraMode)42));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.PanByScreenDelta(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.PanByScreenDelta(0, double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Update(new LogicalPoint(1300, double.NaN), 16));
        Assert.AreEqual(before, camera.Projection);
        Assert.AreEqual(RoomCameraMode.Manual, camera.CameraMode);
        camera.SelectCameraMode(RoomCameraMode.Follow);
        camera.Update(1300, 0);
        camera.Update(1300, -16);
        Assert.AreEqual(before, camera.Projection);
    }

    private static double Center(RoomViewController camera, double width) =>
        camera.Projection.ScreenToWorld(new LogicalPoint(width / 2, 0)).X;

    [TestMethod]
    public void ManualPanMovesBothAxesNaturallyAndStaysWithinFiniteVerticalBounds()
    {
        var camera = Create(RoomCameraMode.Manual);
        var beforeX = camera.CameraX;
        var beforeY = camera.CameraY;
        var beforeScreen = camera.Projection.WorldToScreen(new(InitialPet, InitialPetY));
        Assert.IsTrue(camera.PanByScreenDelta(56, 28));
        Assert.AreEqual(beforeX - 56 / camera.Scale, camera.CameraX, 1e-10);
        Assert.AreEqual(beforeY - 28 / camera.Scale, camera.CameraY, 1e-10);
        var afterScreen = camera.Projection.WorldToScreen(new(InitialPet, InitialPetY));
        Assert.AreEqual(beforeScreen.X + 56, afterScreen.X, 1e-10);
        Assert.AreEqual(beforeScreen.Y + 28, afterScreen.Y, 1e-10);
        var afterX = camera.CameraX;
        var afterY = camera.CameraY;
        camera.Update(new LogicalPoint(2000, -500), 100);
        Assert.AreEqual(afterX, camera.CameraX, 1e-10);
        Assert.AreEqual(afterY, camera.CameraY, 1e-10);
        var anchor = 360 - (600 - 64) / camera.Scale;
        Assert.IsTrue(camera.PanByScreenDelta(0, double.MaxValue));
        Assert.AreEqual(anchor - camera.WorldHeight, camera.CameraY, 1e-10);
        Assert.IsTrue(camera.PanByScreenDelta(0, -double.MaxValue));
        Assert.AreEqual(anchor + camera.WorldHeight, camera.CameraY, 1e-10);
    }

    [TestMethod]
    public void FollowTracksCoreElevationAndReturnsToTheGroundCompositionAfterLanding()
    {
        var camera = Create();
        var anchor = camera.CameraY;
        var initialScreenY = camera.Projection.WorldToScreen(new(InitialPet, InitialPetY)).Y;
        for (var frame = 1; frame <= 30; frame++)
            camera.Update(new LogicalPoint(InitialPet, InitialPetY - frame * 4), 1000d / 60);
        Assert.IsTrue(camera.CameraY < anchor);
        Assert.IsTrue(camera.Projection.WorldToScreen(new(InitialPet, 180)).Y < initialScreenY,
            "Elevation must be allowed to lead the vertical camera while it catches up.");
        for (var frame = 0; frame < 40; frame++) camera.Update(new LogicalPoint(InitialPet, 180), 100);
        Assert.AreEqual(initialScreenY, camera.Projection.WorldToScreen(new(InitialPet, 180)).Y, .001);
        camera.Update(new LogicalPoint(InitialPet, InitialPetY), 100);
        Assert.IsTrue(camera.CameraY < anchor, "Landing must return smoothly, not reset the floor instantly.");
        for (var frame = 0; frame < 40; frame++) camera.Update(new LogicalPoint(InitialPet, InitialPetY), 100);
        Assert.AreEqual(anchor, camera.CameraY, .001);
        Assert.AreEqual(536, camera.Projection.WorldToScreen(new(InitialPet, 360)).Y, .001);
    }

    [TestMethod]
    public void EdgeFollowWaitsForVerticalTwentyEightyAndRetainsTheTriggeredTarget()
    {
        var camera = Create(RoomCameraMode.EdgeFollow);
        var initial = camera.CameraY;
        var visibleHeight = 600 / camera.Scale;
        camera.Update(new LogicalPoint(InitialPet, initial + visibleHeight * .25), 100);
        Assert.AreEqual(initial, camera.CameraY, 1e-10);
        var petY = initial + visibleHeight * .15;
        camera.Update(new LogicalPoint(InitialPet, petY), 100);
        Assert.IsTrue(camera.CameraY < initial && camera.CameraY > petY - visibleHeight * .2);
        for (var frame = 0; frame < 40; frame++)
            camera.Update(new LogicalPoint(InitialPet, initial + visibleHeight * .5), 100);
        Assert.AreEqual(petY - visibleHeight * .2, camera.CameraY, .001);
        var beforeLowerTrigger = camera.CameraY;
        camera.Update(new LogicalPoint(InitialPet, beforeLowerTrigger + visibleHeight * .85), 100);
        Assert.IsTrue(camera.CameraY > beforeLowerTrigger);
    }

    [TestMethod]
    public void ManualZoomAndResizeKeepTwoDimensionalCenterAndFollowRestoresGroundGently()
    {
        var camera = Create(RoomCameraMode.Manual);
        camera.PanByScreenDelta(-280, -56);
        var center = camera.Projection.ScreenToWorld(new(Width / 2, 300));
        camera.ApplyZoomSteps(1);
        var zoomCenter = camera.Projection.ScreenToWorld(new(Width / 2, 300));
        Assert.AreEqual(center.X, zoomCenter.X, 1e-10);
        Assert.AreEqual(center.Y, zoomCenter.Y, 1e-10);
        camera.Resize(960, 720, 417);
        var resizeCenter = camera.Projection.ScreenToWorld(new(480, 360));
        Assert.AreEqual(center.X, resizeCenter.X, 1e-10);
        Assert.AreEqual(center.Y, resizeCenter.Y, 1e-10);
        var beforeY = camera.CameraY;
        camera.SelectCameraMode(RoomCameraMode.Follow);
        Assert.AreEqual(beforeY, camera.CameraY, 1e-10);
        camera.Update(new LogicalPoint(InitialPet, InitialPetY), 100);
        Assert.IsTrue(Math.Abs(camera.Projection.WorldToScreen(new(InitialPet, 417)).Y - 656) > .001,
            "Mode selection must restore the ground framing through damping rather than a snap.");
        for (var frame = 0; frame < 40; frame++) camera.Update(new LogicalPoint(InitialPet, InitialPetY), 100);
        Assert.AreEqual(656, camera.Projection.WorldToScreen(new(InitialPet, 417)).Y, .001);
    }

    [TestMethod]
    public void WholeFixesOnlyHorizontalCameraAndStillAllowsFiniteVerticalFollowing()
    {
        var camera = Create();
        camera.SelectPreset(RoomViewPreset.Whole);
        var beforeY = camera.CameraY;
        camera.Update(new LogicalPoint(2000, InitialPetY - 100), 100);
        Assert.AreEqual(0, camera.CameraX, 1e-10);
        Assert.IsTrue(Math.Abs(beforeY - camera.CameraY) > .001);
        AssertVerticalBounds(camera, 600, 360);
    }

    [TestMethod]
    [DataRow((int)RoomCameraMode.Follow)]
    [DataRow((int)RoomCameraMode.EdgeFollow)]
    [DataRow((int)RoomCameraMode.Manual)]
    public void VerticalModesRemainBoundedAndSupportATallerFutureRoom(int modeValue)
    {
        var mode = (RoomCameraMode)modeValue;
        var camera = new RoomViewController(worldHeight: 1440);
        camera.EnableWindowCamera();
        camera.SelectCameraMode(mode);
        camera.Resize(Width, 600, 1440);
        camera.Update(new LogicalPoint(InitialPet, 1380), 0);
        var initial = camera.CameraY;
        foreach (var petY in new[] { -5000d, 5000d, -5000d })
            for (var frame = 0; frame < 40; frame++)
            {
                if (mode == RoomCameraMode.Manual) camera.PanByScreenDelta(0, petY < 0 ? 10000 : -10000);
                camera.Update(new LogicalPoint(InitialPet, petY), 100);
                AssertVerticalBounds(camera, 600, 1440);
            }
        Assert.IsTrue(Math.Abs(camera.CameraY - initial) > 100,
            "Vertical movement must not be permanently disabled by today's short room.");
    }

    [TestMethod]
    public void ResetMotionAlsoClearsVerticalMomentumWithoutChangingTheView()
    {
        var camera = Create();
        for (var frame = 1; frame <= 30; frame++)
            camera.Update(new LogicalPoint(InitialPet, InitialPetY - frame * 4), 1000d / 60);
        var before = camera.CameraY;
        camera.ResetMotion();
        Assert.AreEqual(before, camera.CameraY, 1e-10);
        camera.Update(new LogicalPoint(InitialPet, 180), 0);
        camera.Update(new LogicalPoint(InitialPet, 180), .1);
        Assert.IsTrue(Math.Abs(camera.CameraY - before) < .001,
            "A resumed vertical camera must start from rest rather than replay pre-hide velocity.");
    }

    private static void AssertVerticalBounds(RoomViewController camera, double height, double ground)
    {
        var anchor = ground - (height - 64) / camera.Scale;
        Assert.IsTrue(camera.CameraY >= anchor - camera.WorldHeight && camera.CameraY <= anchor + camera.WorldHeight);
    }
}
