using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DangoOverflowRenderingTests
{
    [STATestMethod]
    public void AcceptedBeltBoundaryBelowTheBodySlotRemainsPainted()
    {
        // A valid boundary may extend past the 160 x 120 coordinate template.
        // Its core remains inside the body; this is not a fallback geometry.
        var contour = Enumerable.Range(0, 64).Select(index =>
        {
            var angle = index * 2 * Math.PI / 64;
            return PresentationPoint.FromSampledBoundary(.85 * Math.Cos(angle), -.25 + .95 * Math.Sin(angle));
        }).ToArray();
        var frame = new PhysicsPresentationFrame(true, contour, new(new(0, -.05), 0), new(new(0, .82), 0),
            PhysicsPresentationFallbackReason.None,
            new(PhysicsPresentationBackend.ContinuousBelt, 1, PhysicsPresentationDiagnosticCode.ContinuousBeltActive));
        var body = new DeformableDangoVisual { ShowHat = false };
        body.ApplyFrame(frame);
        var root = new Canvas { Width = 400, Height = 360 };
        root.Children.Add(body);
        Canvas.SetLeft(body, 90); Canvas.SetTop(body, 70);
        root.Measure(new(400, 360)); root.Arrange(new(0, 0, 400, 360)); root.UpdateLayout();
        var path = (System.Windows.Shapes.Path)body.FindName("BodyPath");
        Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure);
        Assert.IsTrue(path.Data.Bounds.Bottom > body.Height + 20);
        var bitmap = new RenderTargetBitmap(400, 360, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        foreach (var x in new[] { 60, 80, 100 })
        {
            var local = new Point(x, 135);
            Assert.IsTrue(path.Data.FillContains(local), "The independent native outline must include this interior point.");
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(90 + x, 70 + 135, 1, 1), pixel, 4, 0);
            Assert.IsTrue(pixel[3] >= 250,
                $"Native body interior {local} was cut at the control's {body.RenderSize} layout slot; alpha={pixel[3]}.");
        }
    }
}
