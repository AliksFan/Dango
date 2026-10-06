using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class EyeLayoutCustomizationTests
{
    private static readonly EyeMotionSample Neutral = new(new(0, 0), 1, EyeShape.Round, 0);

    private static FaceLayout Solve(AttachmentShapeGeometry shape, EyeLayoutParameters parameters,
        EyeMotionSample? left = null, EyeMotionSample? right = null, double angle = 0)
        => FaceLayoutSolver.Solve(shape, left ?? Neutral, right ?? Neutral, angle, parameters, 7);

    [TestMethod]
    public void ExplicitDefaultPreservesCompleteLegacyExpressionAndFallbackPaths()
    {
        foreach (var contour in new[] { AttachmentShapeGeometryTests.Oval(),
            AttachmentShapeGeometryTests.Oval(width: .28, height: .3, shear: .16),
            AttachmentShapeGeometryTests.Oval(height: .015), AttachmentShapeGeometryTests.Oval(shear: -.16) })
        {
            var shape = AttachmentShapeGeometry.Create(contour);
            foreach (var expression in Enum.GetValues<EyeShape>())
                foreach (var openness in new[] { 0d, .4, 1d })
                    foreach (var angle in new[] { -.17, 0d, .17 })
                    {
                        var left = Neutral with { Gaze = new(1, 1), Openness = openness, Shape = expression, Strength = .8 };
                        var right = Neutral with { Gaze = new(-1, -1), Shape = expression, Strength = 1 };
                        var actual = Solve(shape, EyeLayoutParameters.Default, left, right, angle);
                        Assert.AreEqual(FaceLayoutSolver.Solve(shape, left, right, angle, 7), actual);
                        AssertSafeVisible(shape, actual);
                    }
        }
    }

    [TestMethod]
    public void MaximumRoundEyesKeepAllThirtyFiveByFortyFiveNeutralControlPositionsVisibleAndUnreduced()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var checks = 0;
        for (var height = 32; height <= 66; height++)
            for (var spacing = 12d; spacing <= 34; spacing += .5)
            {
                var request = new EyeLayoutParameters(140, 0, height, spacing);
                var face = Solve(shape, request);
                AssertNeutralRequest(shape, request, face);
                checks++;
            }
        Assert.AreEqual(35 * 45, checks);
    }

    [TestMethod]
    public void LegalContinuousValuesAndAllOvalDirectionsStayVisibleWithRequestedMapping()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        foreach (var size in new[] { 70d, 99.37, 100d, 123.456, 140d })
            foreach (var oval in new[] { -100d, -72.71, -.001, 0d, .001, 43.79, 100d })
                foreach (var height in new[] { 32d, 32.413, 50.999, 51d, 51.001, 65.723, 66d })
                    foreach (var spacing in new[] { 12d, 12.317, 24.5, 33.829, 34d })
                    {
                        var request = new EyeLayoutParameters(size, oval, height, spacing);
                        AssertNeutralRequest(shape, request, Solve(shape, request));
                    }
        var baseline = Solve(shape, new(100, 0));
        var horizontal = Solve(shape, new(100, -100));
        var vertical = Solve(shape, new(100, 100));
        Assert.AreEqual(baseline.Left.Center, horizontal.Left.Center);
        Assert.AreEqual(baseline.Left.Center, vertical.Left.Center);
        Assert.AreEqual(horizontal.Left.Width, vertical.Left.Height);
        Assert.AreEqual(horizontal.Left.Height, vertical.Left.Width);
        var minus = Solve(shape, new(100, -.001)); var plus = Solve(shape, new(100, .001));
        Assert.IsLessThan(.000001, Math.Abs(minus.Left.Width - plus.Left.Width));
        Assert.IsLessThan(.000001, Math.Abs(minus.Left.Height - plus.Left.Height));
    }

    [TestMethod]
    public void SquashedShearedTiltedExpressionsAndOpposingGazeProduceOnlyJointlySafeVisibleEyes()
    {
        var observedReduction = false;
        foreach (var contour in new[] { AttachmentShapeGeometryTests.Oval(height: .18),
            AttachmentShapeGeometryTests.Oval(width: .28, height: .3, shear: .16),
            AttachmentShapeGeometryTests.Oval(height: .015), AttachmentShapeGeometryTests.Oval(shear: -.16) })
        {
            var shape = AttachmentShapeGeometry.Create(contour);
            foreach (var expression in Enum.GetValues<EyeShape>())
                foreach (var openness in new[] { 0d, .4, 1d })
                    foreach (var oval in new[] { -100d, 0d, 100d })
                    {
                        var request = new EyeLayoutParameters(140, oval, 32.17, 12.27);
                        var left = Neutral with { Gaze = new(1, 1), Openness = openness, Shape = expression, Strength = 1 };
                        var right = Neutral with { Gaze = new(-1, -1), Openness = 1 - openness, Shape = expression, Strength = .8 };
                        var face = Solve(shape, request, left, right, .17);
                        AssertSafeVisible(shape, face);
                        observedReduction |= face.Safety != "Normal";
                        Assert.AreEqual(140d, request.SizePercent, "Safety cannot overwrite the saved request.");
                    }
        }
        Assert.IsTrue(observedReduction, "These fixtures must exercise real safety retreat.");
    }

    [TestMethod]
    public void MotionEditDoesNotAdvanceStepOrReplaceBlinkSleepChannelsAndRestoresBaseLayout()
    {
        using var router = new AttachmentCommandRouter();
        var target = router.Add(new(1)); var token = router.RegisterPreview(target);
        var motion = new AttachmentMotionController(router, target);
        var previous = motion.Update(EyeMotionTests.Body(), 0, 1d / 60)!;
        var parameters = new EyeLayoutParameters(140, 77.3, 42.7, 26.15);
        motion.SetEyeLayout(parameters);
        Assert.AreSame(previous, motion.Current, "Editing a request must not advance an attachment step.");
        Assert.IsTrue(router.Submit(target, token, 1, Blink.Default(EyeSelection.Both)).Accepted);
        var blink = motion.Update(EyeMotionTests.Body(), 1, .04)!;
        Assert.AreEqual(0, blink.Face.Left.Openness, 1e-10);
        for (var step = 2; step < 100; step++) motion.Update(EyeMotionTests.Body(), step, 1d / 60);
        Assert.AreEqual(Solve(AttachmentShapeGeometry.Create(EyeMotionTests.Body().Contour), parameters), motion.Current!.Face);
        Assert.IsTrue(router.Submit(target, token, 2, new SetEyes(EyeSelection.Both, 0, EyeShape.Sleeping, 1)).Accepted);
        for (var step = 100; step < 180; step++) motion.Update(EyeMotionTests.Body(), step, 1d / 60);
        Assert.AreEqual(EyeShape.Sleeping, motion.Current!.Face.Left.Shape);
        Assert.IsTrue(motion.Current.Face.Left.Openness < 1e-8);
        Assert.IsTrue(router.Submit(target, token, 3, new Release(AttachmentChannel.LeftEyePose)).Accepted);
        Assert.IsTrue(router.Submit(target, token, 4, new Release(AttachmentChannel.RightEyePose)).Accepted);
        for (var step = 180; step < 300; step++) motion.Update(EyeMotionTests.Body(), step, 1d / 60);
        var restored = Solve(AttachmentShapeGeometry.Create(EyeMotionTests.Body().Contour), parameters);
        Assert.AreEqual(restored.Left.Width, motion.Current!.Face.Left.Width);
        Assert.AreEqual(restored.Left.Height, motion.Current.Face.Left.Height);
        Assert.AreEqual(restored.Left.Center, motion.Current.Face.Left.Center);
        Assert.AreEqual(EyeShape.Round, motion.Current.Face.Left.Shape);
        Assert.AreEqual(1, motion.Current.Face.Left.Openness, 1e-8);
    }

    [TestMethod]
    public void SceneStoresEntityRequestsAcrossResetAndSynchronizeWithoutAdvancingOrTouchingOtherEntity()
    {
        var selected = new DangoEntityId(1); var other = new DangoEntityId(2);
        using var scene = new AttachmentSceneController(EyeMotionTests.Body());
        scene.Synchronize([selected, other]);
        var before = scene.Advance(Frame(selected, other), 1d / 60, selected, DangoFacingDirection.Center);
        var request = new EyeLayoutParameters(137, -81, 39.2, 23.8);
        scene.SetEyeLayout(selected, request);
        Assert.AreEqual(0L, before[0].Attachments!.StepIndex);
        var edited = scene.Advance(Frame(selected, other), 1d / 60, selected, DangoFacingDirection.Center);
        Assert.AreEqual(before[1].Attachments!.Face, edited[1].Attachments!.Face);
        Assert.AreEqual(1L, edited[0].Attachments!.StepIndex);
        var expected = edited[0].Attachments!.Face;
        scene.Reset();
        var reset = scene.Advance(Frame(selected, other), 1d / 60, selected, DangoFacingDirection.Center);
        Assert.AreEqual(expected, reset[0].Attachments!.Face);
        scene.Synchronize([other]); scene.Synchronize([selected, other]);
        var readded = scene.Advance(Frame(selected, other), 1d / 60, selected, DangoFacingDirection.Center);
        Assert.AreEqual(expected, readded[0].Attachments!.Face);
    }

    [TestMethod]
    public void BaseEyeEditsLeaveIndependentHatMotionAndSourceContourExactlyUnchanged()
    {
        using var referenceRouter = new AttachmentCommandRouter(true);
        using var editedRouter = new AttachmentCommandRouter(true);
        var referenceTarget = referenceRouter.Add(new(1)); var editedTarget = editedRouter.Add(new(1));
        var reference = new AttachmentMotionController(referenceRouter, referenceTarget, EyeMotionTests.Body());
        var edited = new AttachmentMotionController(editedRouter, editedTarget, EyeMotionTests.Body());
        edited.SetEyeLayout(new(140, -68, 34, 31));
        for (var step = 0; step < 100; step++)
        {
            var body = EyeMotionTests.Body(step is >= 30 and < 65 ? .3 : .8);
            var originalContour = body.Contour.ToArray();
            var motion = new HatBodySample(Math.Sin(step / 15d), Math.Cos(step / 20d));
            var expected = reference.Update(body, step, 1d / 60, DangoFacingDirection.Right, motion)!;
            var actual = edited.Update(body, step, 1d / 60, DangoFacingDirection.Right, motion)!;
            Assert.AreEqual(expected.Hat, actual.Hat, "Base eye layout cannot alter independent hat motion.");
            Assert.AreEqual(expected.ShapeTilt, actual.ShapeTilt);
            CollectionAssert.AreEqual(originalContour, body.Contour.ToArray());
        }
    }

    private static DesktopPhysicsSceneFrame Frame(params DangoEntityId[] ids) => new(
        new(ids.Select(id => new DangoBodyState(id, [], 0, 0, 1, 0, 0, 0, 1, true, -1, 16)).ToArray(), 1, 0),
        ids.Select(id => new DesktopPhysicsBody(id, new LogicalRect(0, 0, 160, 120), EyeMotionTests.Body())).ToArray());

    private static void AssertNeutralRequest(AttachmentShapeGeometry shape, EyeLayoutParameters request, FaceLayout face)
    {
        Assert.IsTrue(face.Left.Visible && face.Right.Visible, $"Neutral request {request} cannot hide either eye.");
        Assert.AreEqual(1d, face.Left.Openness); Assert.AreEqual(1d, face.Right.Openness);
        Assert.AreEqual("Normal", face.Safety, $"Neutral supported controls should not retreat: {request}");
        var diameter = 12d / 160 * request.SizePercent / 100;
        var shortAxis = 1 - Math.Abs(request.OvalPercent) / 200;
        Assert.AreEqual(diameter * (request.OvalPercent > 0 ? shortAxis : 1), face.Left.Width, 1e-12);
        Assert.AreEqual(diameter * (request.OvalPercent < 0 ? shortAxis : 1), face.Left.Height, 1e-12);
        var y = Math.Clamp(1d / 160, shape.Bottom + .35 * shape.Height, shape.Bottom + .65 * shape.Height)
            - (request.HeightControlPercent - 51) / 100 * shape.Height;
        Assert.AreEqual(y, face.Left.Center.Y, 1e-12);
        Assert.AreEqual(38d / 160 + (request.SpacingControlPercent - 24.5) / 100 * shape.Width,
            face.Right.Center.X - face.Left.Center.X, 1e-12);
        AssertSafeVisible(shape, face);
    }

    internal static void AssertSafeVisible(AttachmentShapeGeometry shape, FaceLayout face)
    {
        foreach (var eye in new[] { face.Left, face.Right }.Where(e => e.Visible))
            Assert.IsTrue(FaceLayoutSolver.IsSafe(shape, eye), "Every visible eye must be inside the real quadratic contour.");
        if (face.Left.Visible && face.Right.Visible)
        {
            var left = EyeOutline.Create(face.Left); var right = EyeOutline.Create(face.Right);
            Assert.IsTrue(right.Min(p => p.X) - left.Max(p => p.X) >= 2d / 160, "Final eyes require joint 2 DIP clearance.");
        }
    }
}
