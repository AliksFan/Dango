using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class HatTrackGeometryTests
{
    [TestMethod]
    public void FittedAxisBalancesTheWholeRegionAndSinglePointTangentFailsTheCriterion()
    {
        var points = AttachmentShapeGeometryTests.Oval();
        points[4] = new(points[4].X + 0.035, points[4].Y);
        var track = HatTrackSolver.Create(AttachmentShapeGeometry.Create(points))!;
        var pose = track.Fit(0.02)!;
        var samples = pose.FittingSamples;
        var mean = samples.Select((p, i) => p * (i is 0 or 8 ? 1 : 2)).Aggregate((a, b) => a + b) * (1d / 16);
        double CrossMoment(double angle)
        {
            var t = new AttachmentPoint(Math.Cos(angle), Math.Sin(angle)); var n = new AttachmentPoint(-t.Y, t.X);
            return Math.Abs(samples.Select((p, i) => (i is 0 or 8 ? 1 : 2) * (p - mean).Dot(t) * (p - mean).Dot(n)).Sum());
        }
        Assert.IsTrue(CrossMoment(pose.Angle) < 1e-12);
        var local = samples[5] - samples[3];
        Assert.IsTrue(CrossMoment(Math.Atan2(local.Y, local.X)) > 1e-9, "Fixture must reject a single local tangent.");
        var tangent = new AttachmentPoint(Math.Cos(pose.Angle), Math.Sin(pose.Angle));
        var normal = new AttachmentPoint(-tangent.Y, tangent.X);
        Assert.AreEqual(-3d / 160, (pose.ContactSamples[0] - pose.Center).Dot(tangent), 1e-10);
        Assert.AreEqual(3d / 160, (pose.ContactSamples[^1] - pose.Center).Dot(tangent), 1e-10);
        foreach (var sample in pose.ContactSamples)
        {
            var residual = (sample - pose.Center).Dot(normal);
            Assert.IsTrue(-residual <= 0.005 && residual + 1d / 160 <= 0.007);
        }
    }

    [TestMethod]
    public void TooNarrowCrownCannotShrinkTheBaseToAPoint()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(width: 0.015));
        Assert.IsNull(HatTrackSolver.Create(shape));
    }
    internal static PresentationPoint[] FlatTop() => AttachmentShapeGeometryTests.Oval().Select(p => new PresentationPoint(p.X, Math.Min(p.Y, 0.5))).ToArray();

    [TestMethod]
    public void FlatTopSlidesInPositionInsteadOfRotatingAroundAPivot()
    {
        var track = HatTrackSolver.Create(AttachmentShapeGeometry.Create(FlatTop()));
        Assert.IsNotNull(track);
        var a = track.Fit(-0.04); var b = track.Fit(0.04);
        Assert.IsNotNull(a); Assert.IsNotNull(b);
        Assert.IsTrue(b.Center.X - a.Center.X > 0.07);
        Assert.AreEqual(0, a.Angle, 1e-8); Assert.AreEqual(0, b.Angle, 1e-8);
        Assert.AreEqual(6d / 160, a.ContactWidth, 1e-12);
    }

    [TestMethod]
    public void CurvedCrownUsesAnEntireFiniteContactRegionAndIsNodeOrderIndependent()
    {
        var points = AttachmentShapeGeometryTests.Oval();
        var track = HatTrackSolver.Create(AttachmentShapeGeometry.Create(points));
        Assert.IsNotNull(track);
        var pose = track.Fit(0.025); Assert.IsNotNull(pose);
        Assert.IsTrue(pose.Angle < -0.01);
        Assert.IsTrue(pose.ContactSamples.Count >= 33);
        Assert.IsTrue(pose.MaximumGap <= 0.005);
        Assert.IsTrue(pose.MaximumPenetration <= 0.007);
        for (var offset = 1; offset < 16; offset++)
        {
            var shifted = HatTrackSolver.Create(AttachmentShapeGeometry.Create(points.Skip(offset).Concat(points.Take(offset)).ToArray()))!.Fit(0.025)!;
            Assert.AreEqual(pose.Center.X, shifted.Center.X, 1e-10);
            Assert.AreEqual(pose.Center.Y, shifted.Center.Y, 1e-10);
            Assert.AreEqual(pose.Angle, shifted.Angle, 1e-10);
        }
        Assert.IsNull(track.Fit(track.Maximum + 0.01));
    }
}
