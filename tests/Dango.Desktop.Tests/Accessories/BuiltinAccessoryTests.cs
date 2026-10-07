using System.Windows;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class BuiltinAccessoryTests
{
    [STATestMethod]
    [DataRow("builtin-sprout")]
    [DataRow("builtin-top-hat")]
    [DataRow("builtin-bowler")]
    public void EveryBuiltinHasVisibleTransparentPixelsAndSurvivesTheActualTextureRenderer(string assetId)
    {
        var texture = BuiltinAccessoryImages.Get(assetId);
        Assert.IsNotNull(texture); Assert.IsTrue(texture.IsFrozen);
        Assert.AreSame(texture, BuiltinAccessoryImages.Get(assetId));
        Assert.AreEqual(64, texture.PixelWidth); Assert.AreEqual(64, texture.PixelHeight);
        var sourcePixels = Pixels(texture);
        Assert.IsTrue(sourcePixels.Count(pixel => (pixel >>> 24) > 0) > 100, "Authored art must have actual nonempty pixels.");
        Assert.AreEqual(0u, sourcePixels[0] >>> 24, "The outer canvas must remain transparent.");
        var item = AccessoryConfiguration.FromLegacyHat("sprout").Items.Single(item => item.AssetId == assetId) with { IsEquipped = true };
        var frame = new AccessoryMotionSolver().Refresh(new Point(100, 100), 64, 64, item);
        var visual = new AccessoryMeshVisual(); visual.SetTexture(texture); visual.ApplyFrame(frame);
        var rendered = visual.CaptureBitmap(); Assert.IsNotNull(rendered);
        Assert.IsTrue(Pixels(rendered).Count(pixel => (pixel >>> 24) > 0) > 100, "The host raster must show the authored accessory.");
        Assert.AreEqual(new Point(100, 100), frame.ImagePivot);
    }

    private static uint[] Pixels(BitmapSource bitmap)
    {
        var pixels = new uint[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
}
