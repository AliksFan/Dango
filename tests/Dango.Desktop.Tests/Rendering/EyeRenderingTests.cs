using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class EyeRenderingTests
{
    [STATestMethod]
    public void SurprisedEyeHasEmptyCenterAndExpressionsStayInsideBody()
    {
        var body = DangoStreamGeometryFactory.TryCreate(AttachmentShapeGeometryTests.Oval(), 160, 120).Geometry!;
        foreach (var expression in Enum.GetValues<EyeShape>())
        {
            var sample = new EyeMotionSample(new(0, 0), 1, expression, 1);
            var layout = FaceLayoutSolver.Solve(AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval()), sample, sample, 0);
            foreach (var eye in new[] { layout.Left, layout.Right })
            {
                Assert.IsTrue(eye.Visible);
                var geometry = AttachmentGeometryFactory.CreateEye(eye);
                for (var x = geometry.Bounds.Left; x <= geometry.Bounds.Right; x += 0.25)
                    for (var y = geometry.Bounds.Top; y <= geometry.Bounds.Bottom; y += 0.25)
                        if (geometry.FillContains(new Point(x, y))) Assert.IsTrue(body.FillContains(new Point(x, y)));
                if (expression == EyeShape.Surprised)
                    Assert.IsFalse(geometry.FillContains(new Point(80 + eye.Center.X * 160, 66 - eye.Center.Y * 160)));
            }
        }
    }
    [STATestMethod]
    public void NeutralCompiledEyesMatchLegacyPixelsAndSwitchBackRestoresOriginalChildren()
    {
        var frame = EyeMotionTests.Body();
        var sample = new EyeMotionSample(new(0, 0), 1, EyeShape.Round, 0);
        var layout = FaceLayoutSolver.Solve(AttachmentShapeGeometry.Create(frame.Contour), sample, sample, 0);
        var visual = new DeformableDangoVisual(); visual.ApplyFrame(frame);
        var oldImage = Pixels(visual);
        var body = (System.Windows.Shapes.Path)visual.FindName("BodyPath");
        var oldBody = body.Data.ToString();
        var eyeLayer = (Canvas)visual.FindName("LeftEyeLayer"); var original = eyeLayer.Children[0];
        var attachment = new AttachmentPresentationFrame(1, 1, new(0, 1, "Test"), layout);
        visual.ApplyFrame(frame with { Attachments = attachment });
        CollectionAssert.AreEqual(oldImage, Pixels(visual));
        Assert.AreEqual(oldBody, body.Data.ToString());
        for (var i = 0; i < 10; i++) visual.ApplyFrame(frame with { Attachments = attachment });
        Assert.AreEqual(1L, attachment.StepIndex);
        visual.ApplyFrame(frame);
        Assert.AreSame(original, eyeLayer.Children[0]); CollectionAssert.AreEqual(oldImage, Pixels(visual));
    }

    [STATestMethod]
    public void TiltedSoftContourStaysInsideBodyAndClosedEyeKeepsThickness()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(height: 0.18));
        var sample = new EyeMotionSample(new(1, 1), 0.4, EyeShape.Soft, 1);
        var layout = FaceLayoutSolver.Solve(shape, sample, sample with { Gaze = new(-1, -1) }, 0.17);
        Assert.IsTrue(layout.Left.Visible && layout.Right.Visible);
        var bodyGeometry = DangoStreamGeometryFactory.TryCreate(AttachmentShapeGeometryTests.Oval(height: 0.18), 160, 120).Geometry!;
        foreach (var eye in new[] { layout.Left, layout.Right }.Where(e => e.Visible))
        {
            var geometry = AttachmentGeometryFactory.CreateEye(eye);
            var flattened = geometry.GetFlattenedPathGeometry(0.005, ToleranceType.Absolute);
            foreach (var figure in flattened.Figures)
                foreach (var segment in figure.Segments)
                {
                    var points = segment is PolyLineSegment poly ? poly.Points.ToArray() : segment is LineSegment line ? new[] { line.Point } : [];
                    foreach (var point in points) Assert.IsTrue(bodyGeometry.FillContains(point), "Compiled eye outline left the unchanged body.");
                }
            Assert.IsTrue(geometry.Bounds.Height > 0);
        }
        var closed = layout.Left with { Visible = true, Angle = 0, Openness = 0, Width = 12d / 160, Height = 12d / 160, Shape = EyeShape.Round };
        var outline = EyeOutline.Create(closed);
        var middle = outline.Where(p => Math.Abs(p.X - closed.Center.X) < 1e-10).ToArray();
        Assert.AreEqual(1.5, (middle.Max(p => p.Y) - middle.Min(p => p.Y)) * 160, 1e-10);
        Assert.IsTrue(AttachmentGeometryFactory.CreateEye(closed).Bounds.Height > 1.5, "The arched lid bounds include curvature as well as ink thickness.");
    }

    private static byte[] Pixels(FrameworkElement visual)
    {
        visual.Measure(new(160, 120)); visual.Arrange(new Rect(0, 0, 160, 120)); visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var pixels = new byte[160 * 120 * 4]; bitmap.CopyPixels(pixels, 160 * 4, 0); return pixels;
    }
}
