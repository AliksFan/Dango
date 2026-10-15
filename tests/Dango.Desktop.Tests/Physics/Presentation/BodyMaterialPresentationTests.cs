using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class BodyMaterialPresentationTests
{
    [TestMethod]
    public void PresentationCoordinatesAcceptFiniteMaterialExtensionAndRejectNonFiniteValues()
    {
        Assert.AreEqual(1.18, new PresentationPoint(1.18, -1.2).X);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PresentationPoint(double.NaN, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PresentationPoint(0, double.PositiveInfinity));
    }

    [TestMethod]
    [DataRow(0.8)]
    [DataRow(1.0)]
    [DataRow(1.2)]
    public void SizeNormalizationPreservesMaterialStrainAndScalesDiagnostics(double size)
    {
        var parameters = DangoSelfPhysicsParameters.CreateDefault() with { RadialRestLength = .68 * size };
        var points = Enumerable.Range(0, 16).SelectMany(i => new[] { (float)(1.18 * size * Math.Cos(i * Math.PI / 8)), (float)(.62 * size * Math.Sin(i * Math.PI / 8)) }).ToArray();
        var state = new DangoBodyState(new(1), points, 0, 0, (float)(2 * size * size), (float)(.1 * size), (float)(-.05 * size), 0, 0, true, -1, 16);
        var frame = new DangoPhysicsPresentationAdapter(parameters).Present(state, TimeSpan.FromSeconds(1d / 60), DangoFacingDirection.Center);
        Assert.AreEqual(1.18, frame.Contour.Max(p => p.X), 1e-6, "Size compensation must not shrink material extension into a .98 box.");
        Assert.AreEqual(.62, frame.Contour.Max(p => p.Y), 1e-6);
        Assert.AreEqual(2, frame.Diagnostics.SoftBody!.SignedArea, 1e-6);
    }

    [TestMethod]
    [DataRow(.8)]
    [DataRow(1.2)]
    public void DragInverseRestoresNativeSizeAndDoesNotClampExtendedContour(double size)
    {
        var canvas = AttachmentShapeGeometry.MapToCanvas(new(1.18, -.62), 160 * size, 120 * size);
        var native = DragGrabPointMapper.FromVisualPoint(canvas.X, canvas.Y, 160 * size, 120 * size, size);
        Assert.AreEqual(1.18 * size, native.X, 1e-12);
        Assert.AreEqual(-.62 * size, native.Y, 1e-12);
    }

    [TestMethod]
    public void CircleAndDragMappingUseOnePhysicalScaleOnBothAxes()
    {
        var circle = Enumerable.Range(0, 16).Select(i => new PresentationPoint(.7 * Math.Cos(i * Math.PI / 8), .7 * Math.Sin(i * Math.PI / 8))).ToArray();
        var shape = AttachmentShapeGeometry.Create(circle);
        Assert.AreEqual(shape.Width, shape.Height, 1e-12);
        var horizontal = AttachmentShapeGeometry.MapToCanvas(new(.5, 0), 160, 120);
        var vertical = AttachmentShapeGeometry.MapToCanvas(new(0, .5), 160, 120);
        Assert.AreEqual(horizontal.X - 80, 66 - vertical.Y, 1e-12);
        var inverse = DragGrabPointMapper.FromVisualPoint(vertical.X, vertical.Y, 160, 120);
        Assert.AreEqual(.5, inverse.Y, 1e-12);
    }
}
