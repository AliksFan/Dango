using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryRasterEquivalenceTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void MutableTexturesKeepIndependentPixelSnapshotsWhenAnotherVisualLoadsANewerVersion()
    {
        var texture = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null);
        var firstPixel = unchecked((int)0x80008000); var secondPixel = unchecked((int)0x80400080);
        texture.WritePixels(new Int32Rect(0, 0, 2, 2), new[] { firstPixel, firstPixel, firstPixel, firstPixel }, 8, 0);
        var first = new AccessoryMeshVisual(); first.SetTexture(texture);
        texture.WritePixels(new Int32Rect(0, 0, 2, 2), new[] { secondPixel, secondPixel, secondPixel, secondPixel }, 8, 0);
        var second = new AccessoryMeshVisual(); second.SetTexture(texture);
        var sampleMethod = typeof(AccessoryMeshVisual).GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.AreEqual(firstPixel, sampleMethod.CreateDelegate<Func<double, double, int>>(first)(.4, .6));
        Assert.AreEqual(secondPixel, sampleMethod.CreateDelegate<Func<double, double, int>>(second)(.4, .6));
    }

    [STATestMethod]
    public void FlatAndVaryingBilinearSamplesMatchIndependentRoundedChannelOracle()
    {
        const int width = 97, height = 89;
        var texture = Texture(width, height, out var source);
        var visual = new AccessoryMeshVisual(); visual.SetTexture(texture);
        var sample = typeof(AccessoryMeshVisual).GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<double, double, int>>(visual);
        foreach (var u in new[] { -1d, 0, 1d / 256, 255d / 256, 1, width - 1d, width + 1d })
            foreach (var v in new[] { -1d, 0, 1d / 256, 255d / 256, 1, height - 1d, height + 1d })
                Assert.AreEqual(Oracle(source, width, height, u, v), sample(u, v), $"Border sample {u},{v}.");
        uint state = 0x7f4a7c15;
        for (var index = 0; index < 20_000; index++)
        {
            state = Next(state); var u = (state % (uint)((width + 4) * 256)) / 256d - 2;
            state = Next(state); var v = (state % (uint)((height + 4) * 256)) / 256d - 2;
            Assert.AreEqual(Oracle(source, width, height, u, v), sample(u, v), $"Mixed sample {index} at {u},{v}.");
        }
    }

    [STATestMethod]
    public void EveryFractionPreservesIdenticalNeighbourPixelsIncludingAlphaEndpoints()
    {
        foreach (var alpha in new byte[] { 0, 1, 127, 128, 254, 255 })
        {
            var pixel = (int)((uint)alpha << 24 | (uint)alpha << 16 | (uint)(alpha / 2) << 8 | alpha / 3u);
            var pixels = new[] { pixel, pixel, pixel, pixel };
            var texture = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, pixels, 8);
            texture.Freeze(); var visual = new AccessoryMeshVisual(); visual.SetTexture(texture);
            var sample = typeof(AccessoryMeshVisual).GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<double, double, int>>(visual);
            for (var fx = 0; fx < 256; fx++)
                for (var fy = 0; fy < 256; fy++)
                    Assert.AreEqual(pixel, sample(fx / 256d, fy / 256d), $"Alpha {alpha}; fractions {fx},{fy}.");
        }
    }

    // V4 motion-dependent frozen hashes and replay budgets are retired with the material solver.
    // The independent per-channel oracle below remains the renderer sampling contract.
    private static uint Next(uint value)
    { value ^= value << 13; value ^= value >> 17; return value ^ value << 5; }

    private static BitmapSource Texture(int width, int height, out int[] pixels)
    {
        pixels = new int[checked(width * height)];
        ReadOnlySpan<byte> alphas = [0, 1, 32, 127, 128, 192, 254, 255];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var flat = ((x >> 3) + (y >> 3)) % 3 == 0;
                var px = flat ? x >> 3 : x; var py = flat ? y >> 3 : y;
                var value = Next(unchecked((uint)(px * 73856093 ^ py * 19349663)) ^ 0x5ad3e291);
                var alpha = alphas[(int)(value & 7)];
                var blue = (value & 255) * alpha / 255; var green = ((value >> 8) & 255) * alpha / 255;
                var red = ((value >> 16) & 255) * alpha / 255;
                pixels[y * width + x] = (int)((uint)alpha << 24 | red << 16 | green << 8 | blue);
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze(); return bitmap;
    }

    private static int Oracle(int[] source, int width, int height, double u, double v)
    {
        u = Math.Clamp(u, 0, width - 1d); v = Math.Clamp(v, 0, height - 1d);
        var x = (int)u; var y = (int)v; var fx = (int)((u - x) * 256); var fy = (int)((v - y) * 256);
        var right = Math.Min(x + 1, width - 1); var bottom = Math.Min(y + 1, height - 1);
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var top = Lerp(Channel(source[y * width + x], shift), Channel(source[y * width + right], shift), fx);
            var lower = Lerp(Channel(source[bottom * width + x], shift), Channel(source[bottom * width + right], shift), fx);
            result |= (uint)Lerp(top, lower, fy) << shift;
        }
        return (int)result;
        static int Channel(int pixel, int shift) => (int)((uint)pixel >> shift & 255);
        static int Lerp(int a, int b, int fraction) => (a * (256 - fraction) + b * fraction + 128) / 256;
    }
}
