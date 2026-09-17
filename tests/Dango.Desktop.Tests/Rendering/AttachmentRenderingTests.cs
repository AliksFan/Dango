using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class AttachmentRenderingTests
{
    [STATestMethod]
    public void CalibratedAssetMatchesBothExistingCompiledHatPathsAtZeroDeformation()
    {
        var pose = new HatContactPose(new(0, 41d / 160), 0, 0, 6d / 160, 0, 0, []);
        var drawing = AttachmentGeometryFactory.CreateStaticHat(pose);
        var body = new DeformableDangoVisual(); var hat = new DangoHatVisual();
        foreach (var visual in new FrameworkElement[] { body, hat })
        {
            var paths = ((Canvas)visual.FindName("HatLayer")).Children.Cast<System.Windows.Shapes.Path>().ToArray();
            Assert.AreEqual(3, paths.Length);
            for (var i = 0; i < paths.Length; i++)
            {
                var actual = (GeometryDrawing)drawing.Children[i];
                Assert.AreEqual(paths[i].Data.ToString(), actual.Geometry.ToString());
                Assert.AreEqual(((SolidColorBrush)paths[i].Fill).Color, ((SolidColorBrush)actual.Brush).Color);
            }
        }
        var baseLeft = drawing.Transform.Transform(new(77, 25));
        Assert.AreEqual(77, baseLeft.X, 1e-12); Assert.AreEqual(25, baseLeft.Y, 1e-12);
    }

    [STATestMethod]
    public void ContactTransformMovesBothBaseEndpointsAndNeverSilentlyClamps()
    {
        var track = HatTrackSolver.Create(AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval()))!;
        var pose = track.Fit(0.025)!;
        var drawing = AttachmentGeometryFactory.CreateStaticHat(pose);
        var left = drawing.Transform.Transform(new(77, 25)); var right = drawing.Transform.Transform(new(83, 25));
        Assert.AreEqual(6, (right - left).Length, 1e-10);
        Assert.AreEqual(80 + pose.Center.X * 160, (left.X + right.X) / 2, 1e-10);
        Assert.AreEqual(66 - pose.Center.Y * 160, (left.Y + right.Y) / 2, 1e-10);
        Assert.AreEqual(-pose.Angle, Math.Atan2(right.Y - left.Y, right.X - left.X), 1e-10);
        Assert.ThrowsExactly<ArgumentException>(() => AttachmentGeometryFactory.CreateStaticHat(pose with { Angle = 1 }));
    }

    [STATestMethod]
    public void AttachmentBoundaryMatchesTheCompiledBodyCurveWithinTheDeclaredError()
    {
        var points = AttachmentShapeGeometryTests.Oval(shear: 0.1);
        var shape = AttachmentShapeGeometry.Create(points);
        var geometry = DangoStreamGeometryFactory.TryCreate(points, 160, 120).Geometry!;
        var flattened = geometry.GetFlattenedPathGeometry(0.0001, ToleranceType.Absolute).Figures.Single();
        var boundary = new[] { flattened.StartPoint }.Concat(flattened.Segments.SelectMany(s => s switch
        {
            LineSegment line => new[] { line.Point },
            PolyLineSegment poly => poly.Points.ToArray(),
            _ => throw new AssertFailedException("Expected a flattened line segment."),
        })).ToArray();
        foreach (var p in shape.Boundary)
        {
            var canvas = new Point(80 + p.X * 160, 66 - p.Y * 160);
            double Distance(Point a, Point b)
            {
                var d = b - a; var t = d.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(canvas - a, d) / d.LengthSquared, 0, 1);
                return (canvas - (a + d * t)).Length;
            }
            var distance = Enumerable.Range(0, boundary.Length - 1).Min(i => Distance(boundary[i], boundary[i + 1]));
            Assert.IsTrue(distance / 160 <= 0.0005, $"Distance {distance / 160}");
        }
    }
}
