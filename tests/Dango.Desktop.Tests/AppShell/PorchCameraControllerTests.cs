using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class PorchCameraControllerTests
{
    [TestMethod]
    public void ResetCentersThePetWithoutLookingOutsideTheWorld()
    {
        var camera = new PorchCameraController();
        camera.Reset(1080, 960);
        Assert.AreEqual(600, camera.CameraX, 1e-10);
        Assert.AreEqual(480, camera.WorldToScreen(1080), 1e-10);

        camera.Reset(60, 960);
        Assert.AreEqual(0, camera.CameraX, 1e-10);
        camera.Reset(2100, 960);
        Assert.AreEqual(1200, camera.CameraX, 1e-10);
    }

    [TestMethod]
    public void PetAndDoorsShareTheSameWorldToScreenTransformAndScale()
    {
        var camera = new PorchCameraController();
        camera.Reset(1080, 960);
        foreach (var scale in new[] { .5, .8, 1d, 1.5 })
        {
            var pet = camera.WorldToScreen(1080, scale);
            var door = camera.WorldToScreen(1280, scale);
            Assert.AreEqual(200 * scale, door - pet, 1e-10,
                "A camera that moves only the doors or background must fail this invariant.");
            Assert.AreEqual((1280 - camera.CameraX) * scale, door, 1e-10);
        }
    }

    [TestMethod]
    public void FollowUsesElapsedTimeInsteadOfFrameCount()
    {
        var fast = new PorchCameraController();
        var slow = new PorchCameraController();
        fast.Reset(480, 960);
        slow.Reset(480, 960);
        for (var i = 0; i < 10; i++) fast.Update(1480, 960, 10);
        for (var i = 0; i < 2; i++) slow.Update(1480, 960, 50);

        var expected = 1000 * (1 - Math.Exp(-100d / 140));
        Assert.AreEqual(expected, fast.CameraX, 1e-9);
        Assert.AreEqual(fast.CameraX, slow.CameraX, 1e-9);
        Assert.IsTrue(fast.CameraX > 0 && fast.CameraX < 1000);
    }

    [TestMethod]
    public void LateFramesAreBoundedAndNegativeElapsedTimeCannotRewind()
    {
        var camera = new PorchCameraController();
        camera.Reset(480, 960);
        camera.Update(1480, 960, 10000);
        Assert.AreEqual(1000 * (1 - Math.Exp(-100d / 140)), camera.CameraX, 1e-9);
        var before = camera.CameraX;
        camera.Update(480, 960, -16);
        Assert.AreEqual(before, camera.CameraX, 1e-10);
    }

    [TestMethod]
    public void ReduceMotionSnapsAndResizeReclampsImmediately()
    {
        var camera = new PorchCameraController();
        camera.Reset(480, 960);
        Assert.AreEqual(1000, camera.Update(1480, 960, 0, reduceMotion: true), 1e-10);
        camera.Resize(1480, 420);
        Assert.AreEqual(1270, camera.CameraX, 1e-10);
        Assert.AreEqual(210, camera.WorldToScreen(1480), 1e-10);
        camera.Resize(1480, 2400);
        Assert.AreEqual(0, camera.CameraX, 1e-10);
        Assert.AreEqual(0, camera.Update(5000, 2400, 16), 1e-10);
    }

    [TestMethod]
    public void ViewportChangesCannotLeaveTheCameraPastTheNewRightEdge()
    {
        var camera = new PorchCameraController();
        camera.Reset(2100, 420);
        Assert.AreEqual(1740, camera.CameraX, 1e-10);
        camera.Update(2100, 1280, 0);
        Assert.AreEqual(880, camera.CameraX, 1e-10);
    }

    [TestMethod]
    public void RepeatedFollowNeverOvershootsEitherWorldBoundary()
    {
        var camera = new PorchCameraController();
        camera.Reset(1000, 420);
        foreach (var petX in new[] { -500d, 5000d, 1000d })
        {
            for (var i = 0; i < 50; i++)
            {
                camera.Update(petX, 420, 33);
                Assert.IsTrue(camera.CameraX >= 0 && camera.CameraX <= 1740);
            }
        }
    }

    [TestMethod]
    public void PetClampingReservesTheSameWorldEdgeInsetAtBothEnds()
    {
        Assert.AreEqual(48, PorchCameraController.ClampPetX(-100, 2160, 48), 1e-10);
        Assert.AreEqual(2112, PorchCameraController.ClampPetX(3000, 2160, 48), 1e-10);
        Assert.AreEqual(840, PorchCameraController.ClampPetX(840, 2160, 48), 1e-10);
        Assert.AreEqual(0, PorchCameraController.ClampPetX(-10), 1e-10);
    }

    [TestMethod]
    public void InvalidGeometryCannotPoisonTheCamera()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PorchCameraController(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PorchCameraController(2160, 0));
        var camera = new PorchCameraController();
        camera.Reset(1080, 960);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Reset(double.NaN, 960));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Update(1080, 0, 16));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Update(1080, 960, double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.WorldToScreen(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.WorldToScreen(1280, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PorchCameraController.ClampPetX(100, 2160, 1081));
        Assert.AreEqual(600, camera.CameraX, 1e-10);
    }
}
