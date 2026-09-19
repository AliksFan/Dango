using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class HatDeformationGeometryTests
{
    [TestMethod]
    public void BaseStaysFixedTipMovesAndMappingNeverFolds()
    {
        foreach (var bend in new[] { -HatBodyFollow.DeformationLimit, 0, HatBodyFollow.DeformationLimit })
            foreach (var stretch in new[] { -HatBodyFollow.DeformationLimit, 0, HatBodyFollow.DeformationLimit })
            {
                for (var x = 77; x <= 83; x++)
                    foreach (var y in new[] { 25, 26 }) Assert.AreEqual(new Point(x, y), HatDeformationGeometry.Deform(new(x, y), bend, stretch));
                var prior = double.NegativeInfinity;
                for (var y = 2d; y <= 26; y += 0.05)
                {
                    var point = HatDeformationGeometry.Deform(new(80, y), bend, stretch);
                    Assert.IsTrue(point.Y > prior); prior = point.Y;
                }
            }
        Assert.IsTrue((HatDeformationGeometry.Deform(new(80, 3), 0.04, 0.04) - new Point(80, 3)).Length >
            (HatDeformationGeometry.Deform(new(80, 20), 0.04, 0.04) - new Point(80, 20)).Length);
    }

    [STATestMethod]
    public void BothRenderPathsShareDeformationAndZeroRestoresOriginalGeometry()
    {
        var body = new DeformableDangoVisual(); var hat = new DangoHatVisual();
        var a = (Canvas)body.FindName("HatLayer"); var b = (Canvas)hat.FindName("HatLayer");
        var original = b.Children.OfType<System.Windows.Shapes.Path>().Select(p => p.Data).ToArray();
        var frame = new HatMotionFrame(new(new(0, 0.2), 0, 0), 0, 0, -0.08, 0.08, "Normal") { Follow = new(0, 0.04, -0.04, 0) };
        AttachmentGeometryFactory.ApplyHatFrame(a, frame); AttachmentGeometryFactory.ApplyHatFrame(b, frame);
        for (var i = 0; i < original.Length; i++)
        {
            var left = ((System.Windows.Shapes.Path)a.Children[i]).Data; var right = ((System.Windows.Shapes.Path)b.Children[i]).Data;
            Assert.AreEqual(left.ToString(), right.ToString()); Assert.AreNotEqual(original[i].ToString(), right.ToString());
        }
        AttachmentGeometryFactory.ApplyHatFrame(b, null);
        for (var i = 0; i < original.Length; i++) Assert.AreSame(original[i], ((System.Windows.Shapes.Path)b.Children[i]).Data);
    }
}
