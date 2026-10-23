using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

// Renderer overlap policy survives the retired material folding option.
[TestClass]
public sealed class AccessoryFoldingTests
{
    [STATestMethod]
    public void FoldingTextureRenderSupportsReverseWindingWithoutChangingTextureCoordinates()
    {
        var frame = new AccessoryMotionSolver().Refresh(new(80, 80), 32, 32, Item());
        var reversed = frame.TriangleIndices.ToArray();
        for (var triangle = 0; triangle < reversed.Length; triangle += 3)
            (reversed[triangle + 1], reversed[triangle + 2]) = (reversed[triangle + 2], reversed[triangle + 1]);
        var texture = Texture([0x80800000u, 0xff00ff00u, 0x80000080u, 0u]);
        var forward = new AccessoryMeshVisual(); var backward = new AccessoryMeshVisual();
        forward.SetTexture(texture); backward.SetTexture(texture);
        forward.ApplyFrame(frame with { AllowFolding = true });
        var fold = frame with { TriangleIndices = Array.AsReadOnly(reversed), AllowFolding = true };
        backward.ApplyFrame(fold);
        CollectionAssert.AreEqual(Pixels(forward.CaptureBitmap()!), Pixels(backward.CaptureBitmap()!));
        Assert.ThrowsExactly<ArgumentException>(() => backward.ApplyFrame(fold with { AllowFolding = false }),
            "Changing folding policy must not bypass validation through the translation cache.");
        var batch = AccessoryMeshVisual.ApplyBatch([(backward, fold)]);
        Assert.HasCount(0, batch.Errors);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FoldingTextureRenderOverlapsRespectTransparency(bool transparent)
    {
        var positions = Array.AsReadOnly(new Point[] { new(0, 0), new(32, 0), new(0, 32), new(0, 0), new(32, 0), new(0, 32) });
        var uvs = Array.AsReadOnly(new Point[] { new(.125, .5), new(.125, .5), new(.125, .5), new(.875, .5), new(.875, .5), new(.875, .5) });
        var frame = new AccessoryMeshFrame(0, positions, uvs, Array.AsReadOnly(new[] { 0, 1, 2, 3, 5, 4 }),
            new(0, 0, 32, 32), new(), new(), 0)
        { AllowFolding = true };
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture([0x80800000u, 0u, 0u, transparent ? 0u : 0x80000080u]));
        visual.ApplyFrame(frame); var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        Assert.AreEqual(transparent ? 0x80800000u : 0xc0400080u, (uint)pixels[10 * bitmap.PixelWidth + 10]);
    }

    [STATestMethod]
    public void FoldingTextureRenderHasNoDoubleAlphaOnSharedGridEdges()
    {
        var frame = new AccessoryMotionSolver().Refresh(new(32, 32), 32, 32,
            Item());
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture([0x80800000u, 0x80800000u, 0x80800000u, 0x80800000u]));
        visual.ApplyFrame(frame); var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        for (var y = 2; y < 31; y++)
            for (var x = 2; x < 31; x++) Assert.AreEqual(128u, (uint)pixels[y * bitmap.PixelWidth + x] >> 24, $"Alpha seam at {x},{y}.");
    }

    [STATestMethod]
    public void MixedWindingFoldKeepsItsSharedMaterialEdgeVisibleExactlyOnce()
    {
        var frame = new AccessoryMeshFrame(0,
            Array.AsReadOnly(new Point[] { new(0, 0), new(0, 32), new(0, 32), new(32, 32) }),
            Array.AsReadOnly(new Point[] { new(0, 0), new(1, 0), new(0, 1), new(1, 1) }),
            Array.AsReadOnly(new[] { 0, 1, 3, 0, 3, 2 }), new(0, 0, 32, 32), new(), new(), 0)
        { AllowFolding = true };
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture([0x80800000u, 0x80800000u, 0x80800000u, 0x80800000u]));
        visual.ApplyFrame(frame); var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        for (var point = 2; point < 31; point++)
            Assert.AreEqual(128u, (uint)pixels[point * bitmap.PixelWidth + point] >> 24,
                $"Mixed-winding fold lost or duplicated its material seam at {point}.");
        Assert.AreEqual(192u, (uint)pixels[15 * bitmap.PixelWidth + 5] >> 24,
            "Actual overlapping interiors must still compose two translucent faces.");
    }

    private static AccessoryItem Item() => new("fold-raster", "Synthetic chain texture", "builtin-sprout", 64, 64)
    {
        DeformationMode = AccessoryDeformationMode.SingleChain,
        Rig = new([new(.5, .5), new(.5, .8)], 0, 3, 2),
    };

    private static BitmapSource Texture(uint[] colors)
    {
        var source = BitmapSource.Create(colors.Length, 1, 96, 96, PixelFormats.Pbgra32, null, colors.Select(color => (int)color).ToArray(), colors.Length * 4);
        source.Freeze(); return source;
    }

    private static int[] Pixels(BitmapSource bitmap)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }

}
