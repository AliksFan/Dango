using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
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
    [TestCategory("RasterKernelDiagnostic")]
    [DataRow(false, "0d5b1c3469954fd269a99a628e2ddfb8242e37dbafe6596915a78865380462ea")]
    [DataRow(true, "b8efbfbcc8fc993e0b84ecc997ca8ff4bc84db32b2ae6fe9202517e5929febd5")]
    public void OfflineFixedFrameReplayReportsKernelCostAndPreservesCompletePixels(bool varying, string expected)
    {
        // No window, compositor, native stepping, warmup extension or FPS gate. Both implementations
        // consume the same 48 fully deforming frames, and every measured sample is retained.
        const int count = 48, edge = 2048;
        var texture = varying ? Texture(edge, edge, out _) : CheckerTexture(edge);
        var visuals = Enumerable.Range(0, 8).Select(_ => new AccessoryMeshVisual()).ToArray();
        foreach (var visual in visuals) visual.SetTexture(texture);
        // Material V5 intentionally changes motion. Keep this rendering oracle on its
        // original independent V4 geometry instead of replacing the recorded pixel hashes.
        var solvers = Enumerable.Range(0, 8).Select(_ => new Reference.AccessoryMaterialV4ReferenceSolver()).ToArray();
        var item = new AccessoryItem("offline-replay", "Fictional offline replay", "builtin-sprout", 64, 64)
        {
            Softness = 100,
            ConnectionStrength = 10,
            GravityInfluence = 100,
            LockRotation = false,
            RotationDamping = 70,
            ReturnStrength = 80,
            ImagePivotX = .4,
            ImagePivotY = .4,
        };
        var replay = new (AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)[count][];
        for (var step = 0; step < count; step++)
        {
            replay[step] = new (AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)[8];
            for (var index = 0; index < 8; index++)
                replay[step][index] = (visuals[index], solvers[index].Update(step, 1d / 60,
                    new(250 + 24 * Math.Sin(step / 60d * 9 + index), 250 + 24 * Math.Cos(step / 60d * 11 + index)), 256, 256, item));
        }
        var preparation = new List<double>(); var raster = new List<double>(); var total = new List<double>();
        foreach (var requests in replay)
        {
            var start = Stopwatch.GetTimestamp();
            var result = AccessoryMeshVisual.ApplyBatch(requests, new DpiScale(1.5, 1.5));
            total.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Assert.HasCount(0, result.Errors); preparation.Add(result.PreparationMilliseconds); raster.Add(result.RasterMilliseconds);
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var visual in visuals)
        {
            var bitmap = visual.CaptureBitmap()!; var metadata = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(metadata, bitmap.PixelWidth);
            BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(4), bitmap.PixelHeight); hash.AppendData(metadata);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); hash.AppendData(pixels);
        }
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        TestContext.WriteLine($"Offline fixed 48-frame eight-texture replay (varying={varying}): complete pixels {actual}; prepare {Stats(preparation)}; raster {Stats(raster)}; raster+prepare+upload {Stats(total)}.");
        Assert.AreEqual(expected, actual, "Fixed replay output must match the original JIT-version renderer.");
        static string Stats(List<double> values)
        {
            var sorted = values.Order().ToArray();
            return $"median {sorted[sorted.Length / 2]:F3} ms / p95 {sorted[(int)(sorted.Length * .95)]:F3} ms / mean {values.Average():F3} ms";
        }
    }

    private static BitmapSource CheckerTexture(int edge)
    {
        var pixels = new int[edge * edge];
        for (var y = edge / 16; y < edge * 15 / 16; y++)
            for (var x = edge / 16; x < edge * 15 / 16; x++)
                pixels[y * edge + x] = unchecked((int)(((x / (edge / 8) + y / (edge / 8)) % 2 == 0) ? 0x80143c6e : 0x806e3c14));
        var bitmap = BitmapSource.Create(edge, edge, 96, 96, PixelFormats.Pbgra32, null, pixels, edge * 4);
        bitmap.Freeze(); return bitmap;
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

    // Captured from the pre-optimization renderer, including every capacity pixel and all eight
    // actual flexible frames. The mixed texture contains flat regions and varying RGBA samples.
    [STATestMethod]
    [DataRow(31, 19, 1d, 1d, "4fa00c24ae536469a0a219e3d050c9dcce6f82f2167b698144f1ad54aef22537")]
    [DataRow(97, 89, 1.5d, 1.5d, "9528a545df450f4d5d575f05b37a2842100726dc24ed65d8315e5324602da2a6")]
    [DataRow(127, 113, 1.25d, 1.75d, "afbc2eef9089f2d74f902392deff797ec19bc670d2e940ccc6408510ba927c8a")]
    [DataRow(2048, 2048, 1.5d, 1.5d, "a32401bb1645aa62b3c1376996f620610720d29e74dbfa6112ec54d4722172f5")]
    [DataRow(4096, 3906, 1.5d, 1.5d, "bbf4aa9a9849d3e5cbcd38154a5ad5e1f175122e45fd52c7cf58a909f70400a9")]
    public void CompleteFlexibleFramesMatchFrozenOriginalRasterPixels(int width, int height,
        double dpiX, double dpiY, string expected)
    {
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture(width, height, out _));
        var solver = new Reference.AccessoryMaterialV4ReferenceSolver();
        var item = new AccessoryItem("equivalent-raster", "Fictional equivalent raster", "builtin-sprout", 64, 64)
        {
            Softness = 100,
            ConnectionStrength = 10,
            GravityInfluence = 100,
            LockRotation = false,
            RotationDamping = 70,
            ReturnStrength = 80,
            ImagePivotX = .37,
            ImagePivotY = .41,
            BaseAngleDegrees = 37,
        };
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var step = 0; step < 8; step++)
        {
            var frame = solver.Update(step, 1d / 60, new(250 + step * 7, 250 - step * step),
                step < 4 ? 256 : 192, step < 4 ? 190 : 141, item);
            visual.ApplyFrame(frame, new DpiScale(dpiX, dpiY));
            var bitmap = visual.CaptureBitmap()!;
            var metadata = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(metadata, bitmap.PixelWidth);
            BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(4), bitmap.PixelHeight); hash.AppendData(metadata);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); hash.AppendData(pixels);
        }
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        TestContext.WriteLine($"Frozen original raster {width}x{height}, DPI {dpiX}/{dpiY}: {actual}");
        Assert.AreEqual(expected, actual, "Complete raster bytes must remain identical to the original renderer.");
    }

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
