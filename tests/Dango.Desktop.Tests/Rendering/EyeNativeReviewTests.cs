using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class EyeNativeReviewTests
{
    [TestMethod]
    public void RealJumpDrivesEyesBothWaysEvenWithHatFollowDisabled()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");
        using var scene = new DesktopPhysicsScene(module, new LogicalRect(95, 35, 240, 180));
        using var router = new AttachmentCommandRouter(true);
        var moving = new AttachmentMotionController(router, router.Add(new(101)));
        var neutral = new AttachmentMotionController(router, router.Add(new(102)));
        double maximum = 0, minimum = 0, last = 0;
        for (var i = 0; i < 480; i++)
        {
            scene.SetInput(new(false, false, i == 240, false));
            var frame = scene.Step(TimeSpan.FromSeconds(1d / 60));
            Assert.IsTrue(frame.Physics.Succeeded);
            var body = frame.Bodies.Single().Presentation;
            var physical = frame.Physics.Bodies.Single();
            var a = moving.Update(body, i, 1d / 60, bodyMotion: new(physical.CoreX, physical.CoreY), hatBodyFollowEnabled: false)!;
            var b = neutral.Update(body, i, 1d / 60)!;
            Assert.IsTrue(a.Face.Left.Visible && a.Face.Right.Visible);
            Assert.AreEqual(0, a.Hat!.Follow.Lift, 1e-12);
            if (i < 240) continue;
            last = a.Face.Left.Center.Y - b.Face.Left.Center.Y;
            maximum = Math.Max(maximum, last); minimum = Math.Min(minimum, last);
        }
        Assert.IsTrue(maximum > 0.001 && minimum < -0.001, $"Actual jump must look both ways: {minimum}, {maximum}");
        Assert.AreEqual(0, last, 0.0001, "After landing the eyes should return to neutral.");
    }

    [TestMethod]
    public void NineHundredRealStepsKeepPhysicsIdenticalAndEyesVisibleThroughRecovery()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");
        using var reference = new DesktopPhysicsScene(module, new LogicalRect(95, 35, 240, 180));
        using var candidate = new DesktopPhysicsScene(module, new LogicalRect(95, 35, 240, 180));
        using var router = new AttachmentCommandRouter(); var target = router.Add(candidate.SelectedEntity);
        var token = router.RegisterPreview(target); var controller = new AttachmentMotionController(router, target);
        var dt = TimeSpan.FromSeconds(1d / 60);
        var hidden = 0; long sequence = 0;
        for (var i = 0; i < 900; i++)
        {
            var input = new DirectionalControlIntent(i is >= 250 and < 280, i is >= 300 and < 330, i == 360, i is >= 400 and < 460);
            reference.SetInput(input); candidate.SetInput(input);
            if (i == 500)
            {
                reference.BeginDrag(reference.SelectedEntity, reference.ToHost(new(0.3, 1.5)));
                candidate.BeginDrag(candidate.SelectedEntity, candidate.ToHost(new(0.3, 1.5)));
            }
            if (i == 530) { reference.CancelInput(); candidate.CancelInput(); }
            if (i == 240) { router.Submit(target, token, ++sequence, new SetGaze(EyeSelection.Left, 1, 1)); router.Submit(target, token, ++sequence, new SetGaze(EyeSelection.Right, -1, -1)); }
            if (i == 400) router.Submit(target, token, ++sequence, Blink.Default(EyeSelection.Left));
            if (i == 600) router.Submit(target, token, ++sequence, new Release(AttachmentChannel.All));
            if (i < 600 && i % 60 == 0) router.Submit(target, token, ++sequence, new SetEyes(EyeSelection.Both, 1, Enum.GetValues<EyeShape>()[i / 60], 1));
            var before = reference.Step(dt); var body = candidate.Step(dt);
            Assert.IsTrue(body.Physics.Succeeded, $"step {i}");
            var expected = JsonSerializer.Serialize(before.Physics);
            var eyes = controller.Update(body.Bodies.Single().Presentation, i, dt.TotalSeconds)!;
            Assert.AreEqual(expected, JsonSerializer.Serialize(body.Physics), $"step {i}");
            if (!eyes.Face.Left.Visible || !eyes.Face.Right.Visible) hidden++;
            Assert.IsTrue(eyes.Face.Left.Center.X < eyes.Face.Right.Center.X);
            Assert.IsTrue(double.IsFinite(eyes.Face.Left.Center.X) && double.IsFinite(eyes.ShapeTilt.Angle));
        }
        Assert.AreEqual(0, hidden, "Ordinary physical motion must not pass by hiding eyes.");
        Assert.AreEqual("Normal", controller.Current!.Face.Safety);
        Assert.AreEqual(1, controller.Current.Face.Left.Openness, 1e-8);
        Assert.AreEqual(AttachmentEventStatus.Completed, router.ConsumeResults(target).Single().Status);
    }
}
