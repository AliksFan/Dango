using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class EyeMotionTests
{
    [TestMethod]
    public void MirrorShapeTiltsEyesInOppositeDirectionsAndRecoversWithoutUsingFacingAsTilt()
    {
        AttachmentPresentationFrame Run(double shear, DangoFacingDirection facing)
        {
            using var router = new AttachmentCommandRouter(); var target = router.Add(new DangoEntityId(1));
            var controller = new AttachmentMotionController(router, target);
            var body = new PhysicsPresentationFrame(true, AttachmentShapeGeometryTests.Oval(shear: shear), new(new(0, -0.05), 0),
                new(new(0, 0.8), 0), PhysicsPresentationFallbackReason.None, new(PhysicsPresentationBackend.DeterministicFake, 0, PhysicsPresentationDiagnosticCode.FakeActive));
            for (var i = 0; i < 60; i++) controller.Update(body, i, 1d / 60, facing);
            var tilted = controller.Current!;
            for (var i = 60; i < 180; i++) controller.Update(Body(), i, 1d / 60, facing);
            Assert.AreEqual(0, controller.Current!.ShapeTilt.Angle, 1e-8);
            return tilted;
        }
        var right = Run(0.16, DangoFacingDirection.Left); var left = Run(-0.16, DangoFacingDirection.Right);
        Assert.IsTrue(right.ShapeTilt.Angle > 0.01);
        Assert.AreEqual(-left.ShapeTilt.Angle, right.ShapeTilt.Angle, 1e-10);
        Assert.IsTrue(right.Face.Left.Center.Y > right.Face.Right.Center.Y);
        Assert.AreEqual(right.ShapeTilt.Angle, Run(0.16, DangoFacingDirection.Right).ShapeTilt.Angle, 1e-10);
    }

    [TestMethod]
    public void BlinkCadenceAgreesAtCommonTimesAndBothCompleteIndependently()
    {
        double[] Run(int hz)
        {
            using var router = new AttachmentCommandRouter(); var target = router.Add(new DangoEntityId(1));
            var token = router.RegisterPreview(target); var controller = new AttachmentMotionController(router, target);
            router.Submit(target, token, 1, Blink.Default(EyeSelection.Both));
            var result = new List<double>();
            for (var i = 0; i < hz; i++)
            {
                var frame = controller.Update(Body(), i, 1d / hz)!;
                Assert.AreEqual(frame.Face.Left.Openness, frame.Face.Right.Openness, 1e-10);
                if ((i + 1) % (hz / 10) == 0) result.Add(frame.Face.Left.Openness);
            }
            Assert.AreEqual(2, router.ConsumeResults(target).Count);
            return result.ToArray();
        }
        var reference = Run(120);
        foreach (var hz in new[] { 30, 60 })
        {
            var actual = Run(hz);
            for (var i = 0; i < actual.Length; i++) Assert.AreEqual(reference[i], actual[i], 1e-10);
        }
    }

    internal static PhysicsPresentationFrame Body(double height = 0.8) => new(true, AttachmentShapeGeometryTests.Oval(height: height),
        new(new(0, -0.05), 0), new(new(0, 0.8), 0), PhysicsPresentationFallbackReason.None,
        new(PhysicsPresentationBackend.DeterministicFake, 0, PhysicsPresentationDiagnosticCode.FakeActive));

    [TestMethod]
    public void LeftBlinkDoesNotChangeRightAndCompletesAfterAllPhases()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new DangoEntityId(1));
        var token = router.RegisterPreview(target); var controller = new AttachmentMotionController(router, target);
        router.Submit(target, token, 1, Blink.Default(EyeSelection.Left));
        var closed = controller.Update(Body(), 0, 0.04)!;
        Assert.AreEqual(0, closed.Face.Left.Openness, 1e-10);
        Assert.AreEqual(1, closed.Face.Right.Openness, 1e-10);
        Assert.AreEqual(0, router.ConsumeResults(target).Count);
        controller.Update(Body(), 1, 0.01);
        controller.Update(Body(), 2, 0.06);
        Assert.AreEqual(0, router.ConsumeResults(target).Count);
        controller.Update(Body(), 3, 0.02);
        var result = router.ConsumeResults(target);
        Assert.AreEqual(1, result.Count); Assert.AreEqual(AttachmentEventStatus.Completed, result[0].Status);
    }

    [TestMethod]
    public void OppositeGazeAndClosedEyeBlinkKeepIndependentTargets()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new DangoEntityId(1));
        var token = router.RegisterPreview(target); var controller = new AttachmentMotionController(router, target);
        router.Submit(target, token, 1, new SetGaze(EyeSelection.Left, -1, 1));
        router.Submit(target, token, 2, new SetGaze(EyeSelection.Right, 1, -1));
        router.Submit(target, token, 3, new SetEyes(EyeSelection.Left, 0));
        for (var i = 0; i < 100; i++) controller.Update(Body(), i, 0.01);
        var frame = controller.Current!;
        Assert.IsTrue(frame.Face.Left.Center.X < -19d / 160);
        Assert.IsTrue(frame.Face.Right.Center.X > 19d / 160);
        Assert.IsTrue(frame.Face.Left.Center.Y > frame.Face.Right.Center.Y);
        router.Submit(target, token, 4, Blink.Default(EyeSelection.Left));
        frame = controller.Update(Body(), 100, 0.07)!;
        Assert.IsTrue(frame.Face.Left.Openness < 1e-8); Assert.AreEqual(1, frame.Face.Right.Openness, 1e-10);
    }

    [TestMethod]
    public void ReleaseCancelPauseAndDuplicateUpdateDoNotJumpOrReplay()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new DangoEntityId(1));
        var token = router.RegisterPreview(target); var controller = new AttachmentMotionController(router, target);
        router.Submit(target, token, 1, Blink.Default(EyeSelection.Left));
        var frame = controller.Update(Body(), 0, 0.05)!;
        router.Submit(target, token, 2, new Release(AttachmentChannel.LeftBlink));
        var next = controller.Update(Body(), 1, 0.000001)!;
        Assert.AreEqual(frame.Face.Left.Openness, next.Face.Left.Openness, 0.001);
        Assert.AreEqual(AttachmentEventStatus.Cancelled, router.ConsumeResults(target).Single().Status);
        controller.Paused = true;
        Assert.AreSame(next, controller.Update(Body(), 2, 0.1));
        controller.Paused = false;
        Assert.AreSame(next, controller.Update(Body(), 1, 0.01));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => controller.Update(Body(), 2, double.NaN));
        Assert.AreSame(next, controller.Current);
        for (var i = 2; i < 102; i++) controller.Update(Body(), i, 0.01);
        Assert.AreEqual(1, controller.Current!.Face.Left.Openness, 1e-8);
        Assert.AreEqual(0, router.ConsumeResults(target).Count);
    }
}
