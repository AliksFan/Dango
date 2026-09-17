using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class FaceLayoutSafetyTests
{
    [TestMethod]
    public void SoftStrengthChangesOpenEyeAndZeroStrengthRestoresRound()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var round = new EyeMotionSample(new(0, 0), 1, EyeShape.Round, 0);
        var baseline = FaceLayoutSolver.Solve(shape, round, round, 0);
        var soft = FaceLayoutSolver.Solve(shape, round with { Shape = EyeShape.Soft, Strength = 1 }, round, 0);
        Assert.AreEqual(baseline.Left.Height, soft.Left.Height);
        static double Span(EyePresentationFrame eye) { var points = EyeOutline.Create(eye); return points.Max(p => p.Y) - points.Min(p => p.Y); }
        Assert.IsTrue(Span(soft.Left) < Span(baseline.Left));
        Assert.AreEqual(baseline.Right, soft.Right);
        var zero = FaceLayoutSolver.Solve(shape, round with { Shape = EyeShape.Soft }, round, 0);
        CollectionAssert.AreEqual(EyeOutline.Create(baseline.Left).ToArray(), EyeOutline.Create(zero.Left).ToArray());
    }

    [TestMethod]
    public void NeutralKeepsOriginalTwelveDipEyesAndExtremeSquashDegrades()
    {
        var neutral = new EyeMotionSample(new(0, 0), 1, EyeShape.Round, 0);
        var layout = FaceLayoutSolver.Solve(AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval()), neutral, neutral, 0);
        Assert.AreEqual(new AttachmentPoint(-19d / 160, 1d / 160), layout.Left.Center);
        Assert.AreEqual(12d / 160, layout.Left.Width); Assert.AreEqual(12d / 160, layout.Left.Height);
        var flat = FaceLayoutSolver.Solve(AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(height: 0.015)), neutral, neutral, 0.17);
        Assert.IsTrue(!flat.Left.Visible || flat.Left.Openness == 0, "A center-only safety check must fail this fixture.");
        Assert.AreNotEqual("Normal", flat.Safety);
    }

    [TestMethod]
    public void StrongOpposingGazeNeverExchangesIdentityAndSoftZeroIsRound()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(width: 0.28));
        var left = new EyeMotionSample(new(1, 1), 0.4, EyeShape.Soft, 0);
        var right = new EyeMotionSample(new(-1, -1), 1, EyeShape.Round, 0);
        var layout = FaceLayoutSolver.Solve(shape, left, right, 0.17);
        Assert.IsTrue(layout.Left.Center.X < layout.Right.Center.X);
        Assert.IsTrue(layout.GazeScale < 1 || layout.SpacingScale < 1 || layout.SizeScale < 1 || !layout.Left.Visible);
        var a = EyeOutline.Create(layout.Left); var b = EyeOutline.Create(layout.Left with { Shape = EyeShape.Round });
        CollectionAssert.AreEqual(a.ToArray(), b.ToArray());
    }
}
