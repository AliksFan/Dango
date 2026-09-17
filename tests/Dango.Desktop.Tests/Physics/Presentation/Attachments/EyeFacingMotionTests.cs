using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class EyeFacingMotionTests
{
    [TestMethod]
    public void VerticalMotionLooksUpAndDownThenSpringsBackWithoutResumeKick()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new(1));
        var motion = new AttachmentMotionController(router, target);
        var body = EyeMotionTests.Body(); long step = 0; double y = 0;
        FaceLayout Tick(double delta)
        {
            y += delta;
            return motion.Update(body, step++, 1d / 60, bodyMotion: new(0, y))!.Face;
        }
        var baseline = Tick(0);
        for (var i = 0; i < 30; i++) Tick(0.05);
        var up = motion.Current!.Face;
        Assert.IsTrue(up.Left.Center.Y > baseline.Left.Center.Y + 2d / 160, "Ascent should look up.");
        Assert.AreEqual(up.Left.Center.Y - baseline.Left.Center.Y, up.Right.Center.Y - baseline.Right.Center.Y, 1e-9);
        for (var i = 0; i < 30; i++) Tick(-0.05);
        Assert.IsTrue(motion.Current!.Face.Left.Center.Y < baseline.Left.Center.Y - 2d / 160, "Descent should look down.");
        var crossed = false;
        for (var i = 0; i < 180; i++) crossed |= Tick(0).Left.Center.Y > baseline.Left.Center.Y + 1e-5;
        Assert.IsTrue(crossed, "Landing should spring back through neutral.");
        Assert.AreEqual(baseline.Left.Center.Y, motion.Current!.Face.Left.Center.Y, 1e-8);
        motion.Paused = true; y += 100; motion.Paused = false;
        Assert.AreEqual(baseline.Left.Center.Y, Tick(0).Left.Center.Y, 1e-8, "Resume must discard the old position sample.");
        var token = router.RegisterPreview(target);
        router.Submit(target, token, 1, new SetGaze(EyeSelection.Both, 0, -1));
        for (var i = 0; i < 120; i++) Tick(0.05);
        Assert.AreEqual(baseline.Left.Center.Y - 4d / 160, motion.Current!.Face.Left.Center.Y, 1e-8,
            "Explicit gaze must override upward motion.");
    }

    [TestMethod]
    public void FacingMovesTrailingEyeFurtherNarrowsSpacingAndReleaseSpringsToCenter()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new(1));
        var motion = new AttachmentMotionController(router, target);
        var body = EyeMotionTests.Body(); var baseline = motion.Update(body, 0, 1d / 60)!.Face;
        for (var i = 1; i <= 60; i++) motion.Update(body, i, 1d / 60, DangoFacingDirection.Left);
        var left = motion.Current!.Face;
        var dxLeft = left.Left.Center.X - baseline.Left.Center.X;
        var dxRight = left.Right.Center.X - baseline.Right.Center.X;
        Assert.AreEqual(-5.25 / 160, dxLeft, 1e-8);
        Assert.AreEqual(-7d / 160, dxRight, 1e-8);
        Assert.IsTrue(dxRight < dxLeft - 1d / 160 && dxLeft < 0, "Facing left must move the right eye further left.");
        Assert.IsTrue(left.Right.Center.X - left.Left.Center.X < baseline.Right.Center.X - baseline.Left.Center.X,
            "Turning must narrow eye spacing.");
        var released = motion.Update(body, 61, 1e-6, DangoFacingDirection.Center)!.Face;
        Assert.AreEqual(left.Left.Center.X, released.Left.Center.X, 1e-6, "Releasing must not snap.");
        var crossed = false;
        for (var i = 62; i < 182; i++)
        {
            var frame = motion.Update(body, i, 1d / 60, DangoFacingDirection.Center)!.Face;
            crossed |= frame.Left.Center.X > baseline.Left.Center.X + 1e-5;
        }
        Assert.IsTrue(crossed, "A small spring rebound should be visible in the trajectory.");
        Assert.AreEqual(baseline.Left.Center.X, motion.Current!.Face.Left.Center.X, 1e-8);
        Assert.AreEqual(baseline.Right.Center.X, motion.Current.Face.Right.Center.X, 1e-8);
    }

    [TestMethod]
    public void RightFacingMirrorsLeftAndExplicitGazeKeepsPriority()
    {
        FaceLayout Run(DangoFacingDirection facing)
        {
            using var router = new AttachmentCommandRouter(); var target = router.Add(new(1)); var token = router.RegisterPreview(target);
            var motion = new AttachmentMotionController(router, target);
            for (var i = 0; i < 60; i++) motion.Update(EyeMotionTests.Body(), i, 1d / 60, facing);
            var result = motion.Current!.Face;
            router.Submit(target, token, 1, new SetGaze(EyeSelection.Both, 0, 0));
            for (var i = 60; i < 180; i++) motion.Update(EyeMotionTests.Body(), i, 1d / 60, facing);
            Assert.AreEqual(-19d / 160, motion.Current.Face.Left.Center.X, 1e-8);
            router.Submit(target, token, 2, new SetGaze(EyeSelection.Both, 1, 0));
            for (var i = 180; i < 300; i++) motion.Update(EyeMotionTests.Body(), i, 1d / 60, facing);
            Assert.AreEqual((-19d + 5) / 160, motion.Current.Face.Left.Center.X, 1e-8);
            return result;
        }
        var a = Run(DangoFacingDirection.Left); var b = Run(DangoFacingDirection.Right);
        Assert.IsTrue(b.Left.Center.X + 19d / 160 > b.Right.Center.X - 19d / 160 + 1d / 160,
            "Facing right must move the left eye further right.");
        Assert.IsTrue(b.Right.Center.X - b.Left.Center.X < 38d / 160);
        Assert.AreEqual(-a.Left.Center.X, b.Right.Center.X, 1e-9);
        Assert.AreEqual(-a.Right.Center.X, b.Left.Center.X, 1e-9);
    }
}
