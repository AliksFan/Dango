using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class AttachmentShapeGeometryTests
{
    [TestMethod]
    public void NearCircularNoiseStaysNeutralAndInvalidGeometryIsRejected()
    {
        var noisy = Oval(width: 0.65, height: 0.65).Select((p, i) => new PresentationPoint(p.X + 0.00001 * Math.Sin(i * 3), p.Y)).ToArray();
        Assert.AreEqual(0, AttachmentShapeGeometry.Create(noisy).EstimateTilt().Angle, 1e-8);
        Assert.ThrowsExactly<ArgumentException>(() => AttachmentShapeGeometry.Create(Oval().Reverse().ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => AttachmentShapeGeometry.Create(Enumerable.Repeat(new PresentationPoint(0, 0), 16).ToArray()));
    }
    internal static PresentationPoint[] Oval(double width = 0.7, double height = 0.8, double shear = 0) =>
        Enumerable.Range(0, 16).Select(i => { var a = i * Math.PI / 8; var y = height * Math.Sin(a); return new PresentationPoint(width * Math.Cos(a) + shear * y, y); }).ToArray();

    [TestMethod]
    public void CurveUsesTheRenderedQuadraticAndEqualScaleCoordinates()
    {
        var points = Oval();
        var curve = AttachmentShapeGeometry.Create(points);
        Assert.IsTrue(curve.Boundary.Count >= 32);
        for (var i = 0; i < 16; i++)
            for (var k = 0; k <= 10; k++)
            {
                var t = k / 10d; var u = 1 - t;
                var before = points[(i + 15) % 16]; var current = points[i]; var after = points[(i + 1) % 16];
                var x = u * u * (before.X + current.X) / 2 + 2 * u * t * current.X + t * t * (current.X + after.X) / 2;
                var y = u * u * (before.Y + current.Y) / 2 + 2 * u * t * current.Y + t * t * (current.Y + after.Y) / 2;
                var actual = curve.Evaluate(i, t);
                Assert.AreEqual(x * 72 / 160, actual.X, 1e-12);
                Assert.AreEqual(y * 72 / 160, actual.Y, 1e-12);
            }
    }

    [TestMethod]
    [DataRow(0.7, 0.8)]
    [DataRow(0.85, 0.4)]
    [DataRow(0.4, 0.9)]
    public void SymmetricShapesAreNeutral(double width, double height) =>
        Assert.AreEqual(0, AttachmentShapeGeometry.Create(Oval(width, height)).EstimateTilt().Angle, 1e-8);

    [TestMethod]
    public void ShearedMirrorsHaveOppositeTiltAndNodeStartDoesNotMatter()
    {
        var points = Oval(shear: 0.16);
        var first = AttachmentShapeGeometry.Create(points).EstimateTilt();
        var mirror = AttachmentShapeGeometry.Create(Oval(shear: -0.16)).EstimateTilt();
        Assert.IsTrue(first.Angle > 0.02, $"{first}");
        Assert.AreEqual(first.Angle, -mirror.Angle, 1e-8);
        Assert.AreEqual(first.Confidence, mirror.Confidence, 1e-8);
        for (var i = 0; i < 16; i++)
        {
            var shifted = AttachmentShapeGeometry.Create(points.Skip(i).Concat(points.Take(i)).ToArray()).EstimateTilt();
            Assert.AreEqual(first.Angle, shifted.Angle, 1e-8);
        }
    }
}
