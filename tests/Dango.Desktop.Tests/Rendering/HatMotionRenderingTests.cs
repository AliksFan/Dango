using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class HatMotionRenderingTests
{
    [TestMethod]
    public void SettledNativeBaselinePreservesAcceptedEmbeddedRootAfterStartup()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");
        using var scene = new DesktopPhysicsScene(module, new LogicalRect(135, 180, 170, 127.5));
        for (var i = 0; i < 240; i++) Assert.IsTrue(scene.Step(TimeSpan.FromSeconds(1d / 60)).Physics.Succeeded);
        var baseline = scene.LastFrame!.Bodies.Single().Presentation;
        using var router = new AttachmentCommandRouter(true); var target = router.Add(new(1));
        var controller = new AttachmentMotionController(router, target, baseline);
        var expected = HatPlacementSolver.Create(AttachmentShapeGeometry.Create(baseline.Contour), baseline.Hat)!.Fit(0)!;
        var current = controller.Update(baseline, 0, 1d / 60)!.Hat!.Pose!;
        Assert.AreEqual(expected.Center.X, current.Center.X, 1e-7);
        Assert.AreEqual(expected.Center.Y, current.Center.Y, 1e-7);
        // An alternate initial shape must still use the supplied stable calibration.
        var changed = EyeMotionTests.Body(0.6);
        var calibrated = HatPlacementSolver.Create(AttachmentShapeGeometry.Create(baseline.Contour), baseline.Hat)!.Remap(AttachmentShapeGeometry.Create(changed.Contour))!.Fit(0)!;
        var next = new HatMotionController(baseline); next.Prepare(AttachmentShapeGeometry.Create(changed.Contour), changed.Hat);
        Assert.AreEqual(calibrated.Center, next.Current.Pose!.Center);
    }
    [STATestMethod]
    public void BothCompiledHatPathsUseSameTransformAndRestoreLegacy()
    {
        using var router = new AttachmentCommandRouter(true); var target = router.Add(new(1)); var token = router.RegisterPreview(target);
        var controller = new AttachmentMotionController(router, target); var frame = EyeMotionTests.Body();
        router.Submit(target, token, 1, new SetHatSlide(0.8));
        for (var i = 0; i < 60; i++) controller.Update(frame, i, 1d / 60);
        var body = new DeformableDangoVisual(); var hat = new DangoHatVisual();
        body.ApplyFrame(frame); hat.ApplyFrame(frame);
        var bodyLayer = (Canvas)body.FindName("HatLayer"); var hatLayer = (Canvas)hat.FindName("HatLayer");
        var child = hatLayer.Children[0]; var original = hatLayer.RenderTransform.Value;
        var candidate = frame with { Attachments = controller.Current };
        body.ApplyFrame(candidate); hat.ApplyFrame(candidate);
        foreach (var visual in new FrameworkElement[] { body, hat }) { visual.Measure(new(160, 120)); visual.Arrange(new Rect(0, 0, 160, 120)); visual.UpdateLayout(); }
        var a = bodyLayer.TransformToAncestor(body); var b = hatLayer.TransformToAncestor(hat);
        var expected = AttachmentGeometryFactory.CreateStaticHat(controller.Current!.Hat!.Pose!).Transform;
        foreach (var point in new[] { new Point(77, 25), new Point(83, 26), new Point(62, 4), new Point(98, 4) })
        {
            Assert.AreEqual(a.Transform(point), b.Transform(point));
            Assert.AreEqual(expected.Transform(point), b.Transform(point));
        }
        Assert.AreSame(child, hatLayer.Children[0], "Sliding must retain the original leaf paths.");
        hat.ApplyFrame(frame); Assert.AreEqual(original, hatLayer.RenderTransform.Value);
        Assert.AreEqual(new Point(0.5, 0.125), hatLayer.RenderTransformOrigin);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void NativeMotionAndSupportRemovalKeepPhysicsIdentical(int count)
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");
        using var reference = new DesktopPhysicsScene(module, new LogicalRect(95, 35, 240, 180));
        using var candidate = new DesktopPhysicsScene(module, new LogicalRect(95, 35, 240, 180));
        for (var i = 1; i < count; i++) { reference.Add(i * 0.025, i * 1.65); candidate.Add(i * 0.025, i * 1.65); }
        using var router = new AttachmentCommandRouter(true);
        var motions = candidate.EntityIds.ToDictionary(id => id, id =>
        { var target = router.Add(id); return (Target: target, Token: router.RegisterPreview(target), Motion: new AttachmentMotionController(router, target)); });
        long sequence = 0; var missing = 0; double maximumLift = 0, maximumStretch = 0;
        for (var step = 0; step < 900; step++)
        {
            var input = new DirectionalControlIntent(step is >= 250 and < 275, step is >= 300 and < 325, step == 360, step is >= 400 and < 430);
            reference.SetInput(input); candidate.SetInput(input);
            if (step == 470) { reference.BeginDrag(reference.SelectedEntity, reference.ToHost(new(0.3, 1.5))); candidate.BeginDrag(candidate.SelectedEntity, candidate.ToHost(new(0.3, 1.5))); }
            if (step == 490) { reference.CancelInput(); candidate.CancelInput(); }
            if (step == 550 && count > 1)
            { var id = candidate.EntityIds[0]; reference.Remove(id); candidate.Remove(id); router.Remove(motions[id].Target); motions.Remove(id); }
            if (step is 240 or 500 or 600)
                foreach (var a in motions.Values)
                    Assert.IsTrue(router.Submit(a.Target, a.Token, ++sequence, step == 240 ? new SetHatSlide(0.65) : step == 500 ? new PulseHat(-0.2) : new Release(AttachmentChannel.HatSlide)).Accepted);
            var before = reference.Step(TimeSpan.FromSeconds(1d / 60)); var after = candidate.Step(TimeSpan.FromSeconds(1d / 60));
            Assert.IsTrue(after.Physics.Succeeded, $"{count} bodies step {step}");
            Assert.AreEqual(JsonSerializer.Serialize(before.Physics), JsonSerializer.Serialize(after.Physics));
            foreach (var body in after.Bodies)
            {
                var physical = after.Physics.Bodies.Single(b => b.EntityId == body.EntityId);
                var hat = motions[body.EntityId].Motion.Update(body.Presentation, step, 1d / 60,
                    bodyMotion: new HatBodySample(physical.CoreX, physical.CoreY))!.Hat!;
                maximumLift = Math.Max(maximumLift, Math.Abs(hat.Follow.Lift));
                maximumStretch = Math.Max(maximumStretch, Math.Abs(hat.Follow.Stretch));
                Assert.IsTrue(Math.Abs(hat.Follow.Lift) <= HatBodyFollow.LiftLimit);
                Assert.IsTrue(Math.Abs(hat.Follow.Bend) <= HatBodyFollow.DeformationLimit && Math.Abs(hat.Follow.Stretch) <= HatBodyFollow.DeformationLimit);
                if (hat.Pose is null) missing++;
                Assert.IsTrue(Math.Abs(hat.Velocity) <= 0.3);
                Assert.IsTrue(hat.S >= hat.Minimum && hat.S <= hat.Maximum);
            }
        }
        Assert.AreEqual(0, missing, "Ordinary real motion must not pass by hiding the hat.");
        Assert.IsTrue(maximumLift > 0.0001 && maximumStretch > 0.0001, "Real body motion must actually drive the new channels.");
    }
}
