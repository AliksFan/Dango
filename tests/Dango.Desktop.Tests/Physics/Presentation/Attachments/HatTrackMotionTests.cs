using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class HatTrackMotionTests
{
    [TestMethod]
    public void RatesAgreeAndNarrowTrackRecoveryIsExplicit()
    {
        double Run(int hz)
        {
            var motion = new HatMotionController(); var shape = AttachmentShapeGeometry.Create(HatTrackGeometryTests.FlatTop());
            motion.Prepare(shape, new(new(0, 0.8), 0));
            for (var i = 0; i < hz; i++)
                for (var j = 0; j < 240 / hz; j++) motion.Advance(1d / 240, 0, 0.3);
            return motion.Current.S;
        }
        Assert.AreEqual(Run(30), Run(60), 1e-10); Assert.AreEqual(Run(60), Run(120), 1e-10);
        var motion = new HatMotionController(); var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        motion.Prepare(shape, new(new(0, 0.8), 0)); motion.Pulse(0.3);
        motion.Prepare(AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(width: 0.015)), new(new(0, 0.8), 0));
        Assert.IsNull(motion.Current.Pose); Assert.AreEqual("Unavailable", motion.Current.Safety);
        motion.Prepare(shape, new(new(0, 0.8), 0)); Assert.IsNotNull(motion.Current.Pose);
        Assert.AreEqual(0, motion.Current.Velocity);
    }
    [TestMethod]
    public void PublicSlideAndPulseReachOutputAndReleaseReturnsHome()
    {
        using var router = new AttachmentCommandRouter(true); var target = router.Add(new DangoEntityId(1));
        var token = router.RegisterPreview(target); var controller = new AttachmentMotionController(router, target);
        Assert.IsTrue(router.Submit(target, token, 1, new SetHatSlide(0.8)).Accepted);
        for (var i = 0; i < 60; i++) controller.Update(EyeMotionTests.Body(), i, 1d / 60);
        Assert.IsNotNull(controller.Current!.Hat, "Hat commands must reach a visible presentation frame.");
        Assert.IsTrue(controller.Current.Hat.S > 0.015);
        Assert.IsTrue(router.Submit(target, token, 2, new PulseHat(-0.2)).Accepted);
        controller.Update(EyeMotionTests.Body(), 60, 1d / 60);
        Assert.AreEqual(1, router.ConsumeResults(target).Count);
        Assert.IsTrue(router.Submit(target, token, 3, new Release(AttachmentChannel.HatSlide)).Accepted);
        for (var i = 61; i < 240; i++) controller.Update(EyeMotionTests.Body(), i, 1d / 60);
        Assert.AreEqual(0, controller.Current.Hat.S, 1e-7);
    }

    [TestMethod]
    public void FlatSlidingTranslatesRootAndImpulseCannotAccumulateBeyondBounds()
    {
        var motion = new HatMotionController();
        var shape = AttachmentShapeGeometry.Create(HatTrackGeometryTests.FlatTop());
        motion.Prepare(shape, new(new(0, 0.5), 0));
        var start = motion.Current.Pose!;
        for (var i = 0; i < 1000; i++) { motion.Pulse(0.3); motion.Advance(1d / 240, 0, 1); }
        var end = motion.Current;
        Assert.IsNotNull(end.Pose); Assert.IsTrue(end.Pose.Center.X - start.Center.X > 0.04);
        Assert.AreEqual(start.Center.Y, end.Pose.Center.Y, 1e-9); Assert.AreEqual(0, end.Pose.Angle, 1e-9);
        Assert.IsTrue(end.S <= end.Maximum && Math.Abs(end.Velocity) <= 0.3);
        for (var i = 0; i < 1000; i++) motion.Advance(1d / 240, 0, 0);
        Assert.AreEqual(0, motion.Current.S, 1e-8);
    }

    [TestMethod]
    public void CalibrationSurvivesCrownMotionAndReleasePreservesVelocity()
    {
        var motion = new HatMotionController();
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        motion.Prepare(shape, new(new(0, 0.8), 0));
        var initial = motion.Current.Pose!;
        var changed = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(height: 0.6));
        motion.Prepare(changed, new(new(0.2, 0.1), 0.3));
        var crownDelta = HatTrackSolver.CreateGeometryGuide(changed)!.Fit(0)!.Center - HatTrackSolver.CreateGeometryGuide(shape)!.Fit(0)!.Center;
        Assert.AreEqual(initial.Center.Y + crownDelta.Y, motion.Current.Pose!.Center.Y, 1e-9);
        motion.Pulse(0.2); var before = motion.Current;
        motion.Advance(1e-6, 0, 0);
        Assert.AreEqual(before.S, motion.Current.S, 1e-6);
        Assert.AreEqual(before.Velocity, motion.Current.Velocity, 1e-4);
    }
}
