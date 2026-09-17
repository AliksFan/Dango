using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class EyeArtworkTests
{
    private static EyePresentationFrame Eye(EyeShape shape, double openness = 1) =>
        new(new(0, 0), 12d / 160, 12d / 160, 0, openness, shape, 1, true);

    [TestMethod]
    public void FinalClosureMeetsBelowCenterWithoutLiftingUpperLid()
    {
        var previousTop = double.NegativeInfinity;
        for (var i = 50; i >= 0; i--)
        {
            var points = EyeOutline.Create(Eye(EyeShape.Round, i / 100d));
            var top = -points.Max(p => p.Y) * 160;
            Assert.IsTrue(top >= previousTop - 1e-9, $"Upper lid reversed upward at openness {i}%.");
            previousTop = top;
        }
        var closed = EyeOutline.Create(Eye(EyeShape.Round, 0));
        Assert.IsTrue(-closed.Max(p => p.Y) * 160 > 2, "Closed ink must stay below the original eye center.");
    }

    [TestMethod]
    public void ClosedExpressionsKeepArtworkAndAllShapesAcceptCommands()
    {
        foreach (var shape in Enum.GetValues<EyeShape>())
            Assert.IsTrue(new SetEyes(EyeSelection.Both, 1, shape).IsValid);
        Assert.IsFalse(new SetEyes(EyeSelection.Both, 1, (EyeShape)999).IsValid);
        foreach (var shape in new[] { EyeShape.NaturalClosed, EyeShape.Tight, EyeShape.Happy, EyeShape.Deadpan })
            CollectionAssert.AreEqual(EyeOutline.Create(Eye(shape, 0)).ToArray(), EyeOutline.Create(Eye(shape, 1)).ToArray());
        foreach (var shape in new[] { EyeShape.Tight, EyeShape.Angry })
        {
            var left = EyeOutline.Create(Eye(shape));
            var right = EyeOutline.Create(Eye(shape) with { IsRight = true });
            foreach (var point in left)
                Assert.IsTrue(right.Any(p => Math.Abs(p.X + point.X) < 1e-10 && Math.Abs(p.Y - point.Y) < 1e-10));
        }
    }

    [TestMethod]
    public void RedrawnKeyframesAreContinuousAndContoursDoNotCross()
    {
        foreach (var shape in new[] { EyeShape.Round, EyeShape.Soft, EyeShape.Angry, EyeShape.Sleepy, EyeShape.Surprised })
        {
            for (var step = 0; step <= 100; step++)
            {
                var points = EyeArtwork.Create(Eye(shape, step / 100d)).Fills.Single();
                for (var i = 0; i < points.Count; i++)
                    for (var j = i + 2; j < points.Count; j++)
                    {
                        if (i == 0 && j == points.Count - 1) continue;
                        var a = points[i]; var b = points[(i + 1) % points.Count];
                        var c = points[j]; var d = points[(j + 1) % points.Count];
                        static double Cross(AttachmentPoint p, AttachmentPoint q) => p.X * q.Y - p.Y * q.X;
                        Assert.IsFalse(Cross(b - a, c - a) * Cross(b - a, d - a) < -1e-20 &&
                            Cross(d - c, a - c) * Cross(d - c, b - c) < -1e-20, $"{shape} {step}: contour crossing");
                    }
            }
            foreach (var key in new[] { 0.2, 0.5, 0.75 })
            {
                var before = EyeOutline.Create(Eye(shape, key - 1e-6));
                var after = EyeOutline.Create(Eye(shape, key + 1e-6));
                Assert.IsTrue(before.Zip(after).All(pair => (pair.First - pair.Second).Length < 1e-5));
            }
        }
    }

    [TestMethod]
    public void HalfClosedUsesRedrawnLidRatherThanSymmetricEllipseOrHeightScaling()
    {
        var points = EyeOutline.Create(Eye(EyeShape.Round, 0.5));
        Assert.IsTrue(Math.Abs(points.Max(p => p.Y) + points.Min(p => p.Y)) > 0.005, "Half closure must have a lid and round lower edge, not a compressed ellipse.");
        var input = new EyeMotionSample(new(0, 0), 0.5, EyeShape.Round, 0);
        var frame = FaceLayoutSolver.Solve(AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval()), input, input, 0);
        Assert.AreEqual(12d / 160, frame.Left.Height, "Openness does not scale the nominal drawing box.");
    }

    [TestMethod]
    public void NaturalClosedHasRaisedMiddleAndClassicShapesAreDistinct()
    {
        var closed = EyeOutline.Create(Eye(EyeShape.Round, 0));
        var middle = closed.Where(p => Math.Abs(p.X) < 1d / 160).Average(p => p.Y);
        var ends = closed.Where(p => Math.Abs(p.X) > 4d / 160).Average(p => p.Y);
        Assert.IsTrue(middle - ends > 0.003, "The closed eye must arch upwards.");
        var signatures = Enum.GetValues<EyeShape>().Select(shape => string.Join(';', EyeOutline.Create(Eye(shape)).Concat(EyeOutline.Create(Eye(shape) with { IsRight = true })).Select(p => $"{p.X:F6},{p.Y:F6}"))).ToArray();
        Assert.AreEqual(signatures.Length, signatures.Distinct().Count());
    }

    [TestMethod]
    public void ExpressionReachesOnlySelectedEyeAndBlinkReturnsToIt()
    {
        using var router = new AttachmentCommandRouter(); var target = router.Add(new DangoEntityId(1)); var token = router.RegisterPreview(target);
        var controller = new AttachmentMotionController(router, target);
        router.Submit(target, token, 1, new SetEyes(EyeSelection.Left, 1, EyeShape.Angry));
        controller.Update(EyeMotionTests.Body(), 0, 0.01);
        Assert.AreEqual(EyeShape.Angry, controller.Current!.Face.Left.Shape);
        Assert.AreEqual(EyeShape.Round, controller.Current.Face.Right.Shape);
        router.Submit(target, token, 2, Blink.Default(EyeSelection.Left));
        for (var i = 1; i <= 30; i++) controller.Update(EyeMotionTests.Body(), i, 0.01);
        Assert.AreEqual(EyeShape.Angry, controller.Current.Face.Left.Shape);
        Assert.AreEqual(1, controller.Current.Face.Left.Openness, 1e-10);
        Assert.AreEqual(AttachmentEventStatus.Completed, router.ConsumeResults(target).Single().Status);
    }
}
