using System.Windows;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class EyeLayoutRenderingTests
{
    [STATestMethod]
    public void OneHundredFortyPercentRoundAndOvalEyesActuallyRenderWhileExcessAxisIsRejected()
    {
        foreach (var (width, height) in new[] { (.105, .105), (.105, .0525), (.0525, .105) })
        {
            var eye = new EyePresentationFrame(new(0, 0), width, height, .17, 1, EyeShape.Round, 0, true);
            var geometry = AttachmentGeometryFactory.CreateEye(eye);
            Assert.IsFalse(geometry.IsEmpty());
            Assert.IsTrue(geometry.FillContains(new Point(80, 66)));
            Assert.IsTrue(geometry.Bounds.Width > 8 && geometry.Bounds.Height > 8);
            var polygon = EyeOutline.Create(eye);
            // Independent exact ellipse extrema must fit the shared conservative
            // chord-error budget at the newly supported maximum radius .0525.
            var exactX = .5 * Math.Sqrt(Math.Pow(width * Math.Cos(eye.Angle), 2) + Math.Pow(height * Math.Sin(eye.Angle), 2));
            var exactY = .5 * Math.Sqrt(Math.Pow(width * Math.Sin(eye.Angle), 2) + Math.Pow(height * Math.Cos(eye.Angle), 2));
            Assert.IsTrue(exactX - polygon.Max(p => p.X) <= EyeOutline.ChordError);
            Assert.IsTrue(exactY - polygon.Max(p => p.Y) <= EyeOutline.ChordError);
            Assert.ThrowsExactly<ArgumentException>(() => AttachmentGeometryFactory.CreateEye(eye with { Width = .105000001 }));
            Assert.ThrowsExactly<ArgumentException>(() => AttachmentGeometryFactory.CreateEye(eye with { Height = .105000001 }));
        }
    }

    [STATestMethod]
    public void MaximumCustomExpressionInkAndExactEllipsesRemainInsideBodyWithActualTwoDipGap()
    {
        var contour = AttachmentShapeGeometryTests.Oval(height: .18, shear: .16);
        var shape = AttachmentShapeGeometry.Create(contour);
        var body = DangoStreamGeometryFactory.TryCreate(contour, 160, 120).Geometry!;
        foreach (var expression in Enum.GetValues<EyeShape>())
            foreach (var oval in new[] { -100d, 0d, 100d })
            {
                var left = new EyeMotionSample(new(1, 1), 1, expression, 1);
                var right = left with { Gaze = new(-1, -1), Openness = .4 };
                var face = FaceLayoutSolver.Solve(shape, left, right, .17, new EyeLayoutParameters(140, oval, 32.11, 12.23), 7);
                EyeLayoutCustomizationTests.AssertSafeVisible(shape, face);
                var geometries = new[] { face.Left, face.Right }.Where(e => e.Visible).Select(AttachmentGeometryFactory.CreateEye).ToArray();
                foreach (var eye in geometries)
                    for (var x = eye.Bounds.Left; x <= eye.Bounds.Right; x += .25)
                        for (var y = eye.Bounds.Top; y <= eye.Bounds.Bottom; y += .25)
                            if (eye.FillContains(new Point(x, y))) Assert.IsTrue(body.FillContains(new Point(x, y)));
                if (geometries.Length == 2)
                    Assert.IsTrue(geometries[1].Bounds.Left - geometries[0].Bounds.Right >= 2 - .000001,
                        "Drawn exact ellipse bounds must preserve the actual two DIP joint gap.");
            }
    }
}
