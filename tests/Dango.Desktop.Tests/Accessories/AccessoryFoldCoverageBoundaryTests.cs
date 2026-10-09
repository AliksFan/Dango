using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryFoldCoverageBoundaryTests
{
    [STATestMethod]
    public void EarlierDegenerateFaceCannotClaimTheSurvivingFacesMaterialEdge()
    {
        var vertices = Array.AsReadOnly(new Point[]
        {
            new(0, 0), new(16, 16), new(32, 0), new(32, 32),
        });
        var uvs = Array.AsReadOnly(new Point[]
        {
            new(0, 0), new(.5, .5), new(1, 0), new(1, 1),
        });
        // The first face has zero area and shares the diagonal with the valid face.
        // The latter includes that boundary when drawn by itself.
        var valid = Frame(vertices, uvs, [0, 2, 3]);
        var withDegenerate = Frame(vertices, uvs, [0, 3, 1, 0, 2, 3]);
        var expected = Render(valid);
        var actual = Render(withDegenerate);
        Assert.AreEqual(expected.PixelWidth, actual.PixelWidth);
        Assert.AreEqual(expected.PixelHeight, actual.PixelHeight);
        var expectedPixels = Pixels(expected);
        var actualPixels = Pixels(actual);
        for (var point = 2; point < 31; point++)
        {
            Assert.AreEqual(128u, (uint)expectedPixels[point * expected.PixelWidth + point] >> 24,
                "The surviving-face fixture must really paint the shared edge.");
            Assert.AreEqual(128u, (uint)actualPixels[point * actual.PixelWidth + point] >> 24,
                $"An earlier zero-area face stole the surviving material edge at {point}.");
        }
        CollectionAssert.AreEqual(expectedPixels, actualPixels,
            "Adding a zero-area face must not change the surviving material's pixels.");
    }

    [STATestMethod]
    public void SharedVertexAtPixelCenterIsPaintedOnceEvenWhenOppositeFacesComeFirst()
    {
        var vertices = Array.AsReadOnly(new Point[]
        {
            new(0, 0), new(32, 0), new(32, 32), new(0, 32), new(16.5, 16.5),
        });
        var uvs = Array.AsReadOnly(new Point[]
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(.5, .5),
        });
        // Opposite faces precede their neighbors. Each can own both incident edges,
        // so edge ownership alone would paint their common vertex twice.
        var frame = Frame(vertices, uvs, [0, 1, 4, 2, 3, 4, 1, 2, 4, 3, 0, 4]);
        var bitmap = Render(frame);
        var pixels = Pixels(bitmap);
        Assert.AreEqual(128u, (uint)pixels[17 * bitmap.PixelWidth + 17] >> 24,
            "The shared vertex at raster pixel center must retain one material face's alpha.");
        for (var y = 2; y < 31; y++)
            for (var x = 2; x < 31; x++)
                Assert.AreEqual(128u, (uint)pixels[y * bitmap.PixelWidth + x] >> 24,
                    $"A material boundary or vertex lost or duplicated coverage at {x},{y}.");
    }

    private static AccessoryMeshFrame Frame(IReadOnlyList<Point> vertices,
        IReadOnlyList<Point> uvs, int[] indices) =>
        new(0, vertices, uvs, Array.AsReadOnly(indices), new(0, 0, 32, 32), new(), new(), 0)
        { AllowFolding = true };

    private static BitmapSource Render(AccessoryMeshFrame frame)
    {
        var color = unchecked((int)0x80800000u);
        var texture = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null,
            new[] { color, color, color, color }, 8);
        texture.Freeze();
        var visual = new AccessoryMeshVisual();
        visual.SetTexture(texture);
        visual.ApplyFrame(frame);
        return visual.CaptureBitmap()!;
    }

    private static int[] Pixels(BitmapSource bitmap)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }
}
