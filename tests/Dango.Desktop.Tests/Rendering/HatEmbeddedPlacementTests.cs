using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class HatEmbeddedPlacementTests
{
    [STATestMethod]
    [DataRow(0.0, 0.8, 0.0)]
    [DataRow(0.04, 0.75, 0.2)]
    [DataRow(-0.03, 0.6, -0.25)]
    [DataRow(0.3, 0.3, 0.7)]
    public void NeutralRootMatchesBothCompiledLegacyPaths(double x, double y, double angle)
    {
        var reference = new PresentationAttachment(new(x, y), angle);
        var contour = AttachmentShapeGeometryTests.Oval();
        var placement = HatPlacementSolver.Create(AttachmentShapeGeometry.Create(contour), reference)!;
        var pose = placement.Fit(0)!;
        var frame = new PhysicsPresentationFrame(true, contour, new(new(0, -0.05), 0), reference,
            PhysicsPresentationFallbackReason.None, new(PhysicsPresentationBackend.DeterministicFake, 0, PhysicsPresentationDiagnosticCode.FakeActive));
        var body = new DeformableDangoVisual(); body.ApplyFrame(frame);
        var hat = new DangoHatVisual(); hat.ApplyFrame(frame);
        foreach (var visual in new FrameworkElement[] { body, hat })
        {
            visual.Measure(new(160, 120)); visual.Arrange(new Rect(0, 0, 160, 120)); visual.UpdateLayout();
            var layer = (Canvas)visual.FindName("HatLayer");
            var transform = layer.TransformToAncestor(visual);
            var expected = transform.Transform(new Point(80, 25));
            Assert.AreEqual(expected.X, 80 + pose.Center.X * 160, 1e-9);
            Assert.AreEqual(expected.Y, 66 - pose.Center.Y * 160, 1e-9);
            var left = transform.Transform(new Point(77, 25)); var right = transform.Transform(new Point(83, 25));
            Assert.AreEqual(-Math.Atan2(right.Y - left.Y, right.X - left.X), pose.Angle, 1e-9);
            var candidate = AttachmentGeometryFactory.CreateStaticHat(pose);
            foreach (var point in new[] { new Point(77, 25), new Point(83, 26), new Point(62, 4), new Point(98, 4), new Point(80, 12) })
            {
                var oldPoint = transform.Transform(point); var newPoint = candidate.Transform.Transform(point);
                Assert.AreEqual(oldPoint.X, newPoint.X, 1e-9);
                Assert.AreEqual(oldPoint.Y, newPoint.Y, 1e-9);
            }
        }
    }

    [TestMethod]
    public void FlatGuideSlidesWithoutLiftingTheEmbeddedRootToTheSurface()
    {
        var shape = AttachmentShapeGeometry.Create(HatTrackGeometryTests.FlatTop());
        var placement = HatPlacementSolver.Create(shape, new(new(0, 0.5), 0))!;
        var a = placement.Fit(-0.04)!; var b = placement.Fit(0.04)!;
        Assert.IsTrue(b.Center.X - a.Center.X > 0.07);
        Assert.AreEqual(a.Center.Y, b.Center.Y, 1e-9);
        Assert.AreEqual(0, a.Angle, 1e-9); Assert.AreEqual(0, b.Angle, 1e-9);
        var surface = HatTrackSolver.CreateGeometryGuide(shape)!.Fit(0)!;
        Assert.AreEqual((66 - 36.6) / 160, placement.Fit(0)!.Center.Y, 1e-9, "Preserve the actual clamped legacy root, including on a flat fixture.");
        Assert.IsTrue(Math.Abs(surface.Center.Y - placement.Fit(0)!.Center.Y) > 0.001);
        var round = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var roundPlacement = HatPlacementSolver.Create(round, new(new(0, 0.8), 0))!;
        Assert.IsTrue(HatTrackSolver.CreateGeometryGuide(round)!.Fit(0)!.Center.Y - roundPlacement.Fit(0)!.Center.Y > 0.02, "Preserve the round reference's visible inset.");
    }

    [TestMethod]
    public void SurfaceDistanceIsNotAVisualPlacementGateAndInvalidAnglesAreRejected()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(width: 0.1));
        Assert.IsNull(HatTrackSolver.Create(shape), "The old strict geometry gate remains unchanged.");
        var placement = HatPlacementSolver.Create(shape, new(new(0, 0.8), 0));
        Assert.IsNotNull(placement);
        var pose = placement.Fit(0); Assert.IsNotNull(pose);
        Assert.IsNull(placement.Fit(double.NaN)); Assert.IsNull(placement.Fit(placement.Maximum + 0.01));
        Assert.ThrowsExactly<ArgumentException>(() => AttachmentGeometryFactory.CreateStaticHat(pose with { Angle = 1 }));
        Assert.ThrowsExactly<ArgumentException>(() => AttachmentGeometryFactory.CreateStaticHat(pose with { Center = new(double.NaN, 0) }));
    }
}
