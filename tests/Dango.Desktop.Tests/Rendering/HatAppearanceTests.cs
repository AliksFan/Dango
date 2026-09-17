using System.Windows.Controls;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class HatAppearanceTests
{
    [STATestMethod]
    public void EnlargingBothRenderPathsKeepsEmbeddedRootFixed()
    {
        var frame = Dango.Desktop.Tests.Physics.Presentation.Attachments.EyeMotionTests.Body();
        using var router = new AttachmentCommandRouter(true); var target = router.Add(new(1));
        var motion = new AttachmentMotionController(router, target); var candidate = frame with { Attachments = motion.Update(frame, 0, 1d / 60) };
        foreach (var sample in new[] { frame, candidate })
        {
            var body = new DeformableDangoVisual(); var hat = new DangoHatVisual();
            foreach (var visual in new System.Windows.FrameworkElement[] { body, hat })
            {
                void Render(double size)
                {
                    body.HatSize = hat.HatSize = size; body.ApplyFrame(sample); hat.ApplyFrame(sample);
                    visual.Measure(new(160, 120)); visual.Arrange(new System.Windows.Rect(0, 0, 160, 120)); visual.UpdateLayout();
                }
                var layer = (Canvas)visual.FindName("HatLayer"); Render(1);
                var before = layer.TransformToAncestor(visual); var root = before.Transform(new(80, 25)); var tip = before.Transform(new(80, 3));
                Render(1.35); var after = layer.TransformToAncestor(visual);
                Assert.AreEqual(root.X, after.Transform(new(80, 25)).X, 1e-9); Assert.AreEqual(root.Y, after.Transform(new(80, 25)).Y, 1e-9);
                Assert.AreEqual((tip - root).Length * 1.35, (after.Transform(new(80, 3)) - root).Length, 1e-9);
            }
        }
    }
    [STATestMethod]
    public void SwitchingDuringDeformationRefreshesAssetsAndRestoresSprout()
    {
        var visual = new DangoHatVisual(); var body = new DeformableDangoVisual();
        var layer = (Canvas)visual.FindName("HatLayer"); var other = (Canvas)body.FindName("HatLayer");
        static string Signature(Canvas canvas) => string.Join(';', canvas.Children.OfType<System.Windows.Shapes.Path>().Select(p => p.Data.ToString()));
        var original = Signature(layer); var shapes = new HashSet<string>();
        var frame = new HatMotionFrame(new(new(0, 0.2), 0, 0), 0, 0, -0.08, 0.08, "Normal") { Follow = new(0, 0.04, -0.04, 0) };
        foreach (var style in new[] { HatAppearance.TopHat, HatAppearance.Bowler, HatAppearance.Sprout })
        {
            visual.Appearance = body.Appearance = style;
            var clean = Signature(layer); Assert.IsTrue(shapes.Add(clean));
            AttachmentGeometryFactory.ApplyHatFrame(layer, frame); AttachmentGeometryFactory.ApplyHatFrame(other, frame);
            Assert.AreEqual(Signature(layer), Signature(other)); Assert.AreNotEqual(clean, Signature(layer));
            AttachmentGeometryFactory.ApplyHatFrame(layer, null);
            Assert.AreEqual(clean, Signature(layer));
            // Leave it deformed before the next switch to detect stale cached originals.
            AttachmentGeometryFactory.ApplyHatFrame(layer, frame);
        }
        AttachmentGeometryFactory.ApplyHatFrame(layer, null); Assert.AreEqual(original, Signature(layer));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => visual.Appearance = (HatAppearance)99);
    }
}
