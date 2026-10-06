using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class EyeLayoutNativeTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Bounds = new(95, 35, 240, 180);

    [TestMethod]
    public void ActualRestingNativeContourSupportsEveryMaximumRoundControlGridWithoutHidingOrClosing()
    {
        using var scene = new DesktopPhysicsScene(Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"), Bounds);
        DesktopPhysicsSceneFrame? frame = null;
        for (var i = 0; i < 240; i++) frame = scene.Step(Dt);
        Assert.IsNotNull(frame);
        Assert.IsTrue(frame.Physics.Succeeded);
        var shape = AttachmentShapeGeometry.Create(frame.Bodies.Single().Presentation.Contour);
        var sample = new EyeMotionSample(new(0, 0), 1, EyeShape.Round, 0);
        for (var height = 32; height <= 66; height++)
            for (var spacing = 12d; spacing <= 34; spacing += .5)
            {
                var request = new EyeLayoutParameters(140, 0, height, spacing);
                var face = FaceLayoutSolver.Solve(shape, sample, sample, 0, request);
                Assert.IsTrue(face.Left.Visible && face.Right.Visible, $"Native resting eyes hidden for {request}.");
                Assert.AreEqual(1d, face.Left.Openness); Assert.AreEqual(1d, face.Right.Openness);
                EyeLayoutCustomizationTests.AssertSafeVisible(shape, face);
            }
    }

    [TestMethod]
    public void NineHundredNativeStepsPreservePhysicsAndIndependentHatWithCustomEyesThroughBlinkSleepAndRecovery()
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll");
        using var referenceScene = new DesktopPhysicsScene(module, Bounds);
        using var customScene = new DesktopPhysicsScene(module, Bounds);
        using var referenceRouter = new AttachmentCommandRouter(true);
        using var customRouter = new AttachmentCommandRouter(true);
        var referenceTarget = referenceRouter.Add(referenceScene.SelectedEntity);
        var customTarget = customRouter.Add(customScene.SelectedEntity);
        var referenceToken = referenceRouter.RegisterPreview(referenceTarget);
        var token = customRouter.RegisterPreview(customTarget);
        var reference = new AttachmentMotionController(referenceRouter, referenceTarget);
        var custom = new AttachmentMotionController(customRouter, customTarget);
        var request = new EyeLayoutParameters(140, 62.71, 38.413, 19.817);
        custom.SetEyeLayout(request);
        var blinkObserved = false; var sleepObserved = false;
        for (var step = 0; step < 900; step++)
        {
            var input = new DirectionalControlIntent(step is >= 250 and < 280, step is >= 300 and < 330,
                step == 360, step is >= 400 and < 460);
            referenceScene.SetInput(input); customScene.SetInput(input);
            // Both controllers receive identical dynamic inputs, including the
            // blink's exact step subdivisions. Only the saved base layout differs.
            void SubmitBoth(long sequence, AttachmentIntent intent)
            {
                Assert.IsTrue(referenceRouter.Submit(referenceTarget, referenceToken, sequence, intent).Accepted);
                Assert.IsTrue(customRouter.Submit(customTarget, token, sequence, intent).Accepted);
            }
            if (step == 480) SubmitBoth(1, Blink.Default(EyeSelection.Both));
            if (step == 550) SubmitBoth(2, new SetEyes(EyeSelection.Both, 0, EyeShape.Sleeping, 1));
            if (step == 650) SubmitBoth(3, new Release(AttachmentChannel.All));
            var expected = referenceScene.Step(Dt); var actual = customScene.Step(Dt);
            Assert.IsTrue(actual.Physics.Succeeded, $"step {step}");
            Assert.AreEqual(JsonSerializer.Serialize(expected.Physics), JsonSerializer.Serialize(actual.Physics), $"step {step}");
            var physical = actual.Physics.Bodies.Single();
            var motion = new HatBodySample(physical.CoreX, physical.CoreY);
            var oldFrame = reference.Update(expected.Bodies.Single().Presentation, step, Dt.TotalSeconds, bodyMotion: motion)!;
            var face = custom.Update(actual.Bodies.Single().Presentation, step, Dt.TotalSeconds, bodyMotion: motion)!;
            Assert.AreEqual(oldFrame.Hat, face.Hat, $"Independent native hat step {step}");
            Assert.IsTrue(face.Face.Left.Visible && face.Face.Right.Visible, $"Ordinary native step {step} cannot hide customized eyes.");
            EyeLayoutCustomizationTests.AssertSafeVisible(AttachmentShapeGeometry.Create(actual.Bodies.Single().Presentation.Contour), face.Face);
            blinkObserved |= step is >= 480 and < 550 && face.Face.Left.Openness < .05;
            sleepObserved |= step is >= 600 and < 650 && face.Face.Left.Shape == EyeShape.Sleeping && face.Face.Left.Openness < 1e-8;
        }
        Assert.IsTrue(blinkObserved && sleepObserved);
        Assert.AreEqual(EyeShape.Round, custom.Current!.Face.Left.Shape);
        Assert.AreEqual(1d, custom.Current.Face.Left.Openness, 1e-8);
        Assert.AreEqual("Normal", custom.Current.Face.Safety);
        Assert.AreEqual(12d / 160 * request.SizePercent / 100 * (1 - request.OvalPercent / 200), custom.Current.Face.Left.Width, 1e-12);
        Assert.AreEqual(12d / 160 * request.SizePercent / 100, custom.Current.Face.Left.Height, 1e-12);
    }
}
