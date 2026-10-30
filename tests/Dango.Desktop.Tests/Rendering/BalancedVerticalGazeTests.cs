using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass, DoNotParallelize]
public sealed class BalancedVerticalGazeTests
{
    [STATestMethod]
    [DataRow(true, false, 1)]
    [DataRow(false, true, -1)]
    [DataRow(true, true, 0)]
    [DataRow(false, false, 0)]
    public void HomePresenterMovesRenderedEyesInTheRequestedDirectionAndReleases(bool w, bool s, int direction)
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        var boundary = Enumerable.Range(0, 256).Select(i => new BeltBodyBoundaryPoint(
            .75 * Math.Cos(i * Math.Tau / 256), .65 * Math.Sin(i * Math.Tau / 256))).ToArray();
        BeltBodyAppearanceFrame Frame(int tick, bool up, bool down) =>
            new(tick, 1d / 60, boundary, 0, 0, 0, 0, true, 0, down, up, false, false);
        presenter.Update(Frame(0, false, false));
        var neutral = presenter.Inspect().Body!.Attachments!.Face.Left;
        var neutralScreenY = AttachmentGeometryFactory.CreateEye(neutral).Bounds.Top;
        for (var tick = 1; tick <= 60; tick++) presenter.Update(Frame(tick, w, s));
        var held = presenter.Inspect().Body!.Attachments!.Face;
        Assert.AreEqual(neutral.Center.Y + direction * 4d / 160, held.Left.Center.Y, .0001);
        Assert.AreEqual(held.Left.Center.Y, held.Right.Center.Y, 1e-10);
        var screenY = AttachmentGeometryFactory.CreateEye(held.Left).Bounds.Top;
        if (direction > 0) Assert.IsTrue(screenY < neutralScreenY - 3, "W must draw the eyes upwards on screen.");
        if (direction < 0) Assert.IsTrue(screenY > neutralScreenY + 3, "S must draw the eyes downwards on screen.");
        for (var tick = 61; tick <= 120; tick++) presenter.Update(Frame(tick, false, false));
        Assert.AreEqual(neutral.Center.Y, presenter.Inspect().Body!.Attachments!.Face.Left.Center.Y, .0001,
            "Release must restore automatic gaze, without retaining a keyboard override.");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public void SharedEyeConsumerOverridesVerticalVelocityAndExplicitGazeButKeepsEyePoseAndHorizontalGaze(int direction)
    {
        using var router = new AttachmentCommandRouter();
        var target = router.Add(new Dango.Physics.DangoEntityId(1));
        var token = router.RegisterPreview(target);
        var controller = new AttachmentMotionController(router, target);
        var baselineY = controller.Update(EyeMotionTests.Body(), 0, 1d / 60)!.Face.Left.Center.Y;
        Assert.IsTrue(router.Submit(target, token, 1, new SetGaze(EyeSelection.Both, .5, -direction)).Accepted);
        Assert.IsTrue(router.Submit(target, token, 2, new SetEyes(EyeSelection.Both, 0, EyeShape.Sleeping, 1)).Accepted);
        for (var tick = 1; tick <= 60; tick++)
            controller.Update(EyeMotionTests.Body(), tick, 1d / 60, bodyMotion: new(0, -direction * tick / 60d), verticalDeformationAxis: direction);
        var held = controller.Current!.Face;
        Assert.AreEqual(direction * 4d / 160, held.Left.Center.Y - baselineY, .001);
        Assert.IsTrue(held.Left.Center.X > -19d / 160, "Horizontal gaze remains composed with the keyboard Y override.");
        Assert.AreEqual(EyeShape.Sleeping, held.Left.Shape);
        Assert.IsTrue(held.Left.Openness < .001);
        for (var tick = 61; tick <= 120; tick++)
            controller.Update(EyeMotionTests.Body(), tick, 1d / 60, verticalDeformationAxis: 0);
        Assert.IsTrue(direction * (controller.Current!.Face.Left.Center.Y - baselineY) < 0,
            "Clearing keyboard control must expose the prior explicit gaze again.");
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    public void ActualRoomRuntimeUsesTheSameGazeRuleAndClearsBothHeldAndCancelledInput()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll");
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.Start();
        var primary = new PhysicsPresentationInput("fictional-balanced-gaze", new LogicalRect(400, 500, 160, 120),
            DangoActionState.Idle, 1000, 17, "fictional-ground", new(0, -1), 0, 1, false, true, []);
        StackedPhysicsPresentationFrame Advance(DirectionalControlIntent intent)
        {
            runtime.SetDirectionalControl(intent);
            StackedPhysicsPresentationFrame frame = null!;
            for (var i = 0; i < 50; i++)
            {
                frame = runtime.Tick(primary, TimeSpan.FromSeconds(1d / 60));
                Assert.IsNull(runtime.LastError, runtime.LastError);
            }
            return frame;
        }
        Advance(DirectionalControlIntent.None);
        foreach (var direction in new[] { 1, -1 })
        {
            var held = Advance(new(false, false, false, direction < 0) { Stretch = direction > 0 });
            var attachment = held.Bodies[0].Attachments!;
            var shape = attachment.SourceShape!;
            var neutral = FaceLayoutSolver.Solve(shape, new(new(0, 0), 1, EyeShape.Round, 0),
                new(new(0, 0), 1, EyeShape.Round, 0), attachment.ShapeTilt.Angle, DangoAppearanceConfiguration.Default.EyeLayout, horizontalRange: 7);
            Assert.IsTrue(direction * (attachment.Face.Left.Center.Y - neutral.Left.Center.Y) > .01,
                "The real room runtime must forward its selected body's keyboard direction to the shared eye consumer.");
            Advance(new(false, false, false, true) { Stretch = true });
            Advance(DirectionalControlIntent.None);
        }
        runtime.CancelInput();
        Advance(DirectionalControlIntent.None);
    }
}
