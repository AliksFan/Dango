using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryImageTests
{
    private string root = null!;
    [TestInitialize]
    public void Initialize() => root = Path.Combine(Path.GetTempPath(), "Dango-accessory-image-tests", Guid.NewGuid().ToString("N"));
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    [TestMethod]
    public void TransparentPngKeepsCanvasAndPixelsAndIsFrozenWithoutSourceLifetime()
    {
        var path = WritePng("source.png", [0, 0, 0, 0, 22, 40, 60, 255, 77, 90, 100, 128]);
        var original = File.ReadAllBytes(path);
        var image = AccessoryImageAsset.ImportPng(path);
        Assert.AreEqual(3, image.PixelWidth);
        Assert.AreEqual(1, image.PixelHeight);
        Assert.IsTrue(image.Bitmap.IsFrozen);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        File.Delete(path);
        var pixels = new byte[12];
        image.Bitmap.CopyPixels(pixels, 12, 0);
        Assert.AreEqual(0, pixels[3]);
        Assert.AreEqual(255, pixels[7]);
        Assert.AreEqual(128, pixels[11]);
    }

    [TestMethod]
    public void FullyTransparentOpaqueCorruptAndNonPngSourcesAreRejected()
    {
        var transparent = WritePng("transparent.png", [0, 0, 0, 0, 0, 0, 0, 0]);
        var opaque = WritePng("opaque.png", [10, 20, 30, 255, 40, 50, 60, 255]);
        var wrongExtension = WritePng("image.jpg", [0, 0, 0, 0, 0, 0, 1, 255]);
        var corrupt = Path.Combine(root, "corrupt.png");
        File.WriteAllText(corrupt, "not a PNG");
        foreach (var path in new[] { transparent, opaque, wrongExtension, corrupt })
            Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
    }

    [TestMethod]
    public void CorruptedDimensionsAndAnimatedPngAreRejectedBeforeImageDecode()
    {
        var path = WritePng("large.png", [0, 0, 0, 0, 20, 40, 60, 255]);
        var png = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 2049);
        File.WriteAllBytes(path, png);
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        path = WritePng("animated.png", [0, 0, 0, 0, 20, 40, 60, 255]);
        png = File.ReadAllBytes(path);
        var animationChunk = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(animationChunk.AsSpan(0, 4), 8);
        "acTL"u8.CopyTo(animationChunk.AsSpan(4, 4));
        File.WriteAllBytes(path, [.. png.AsSpan(0, 33).ToArray(), .. animationChunk, .. png.AsSpan(33).ToArray()]);
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
    }

    [TestMethod]
    public void CompressedFileLimitIsCheckedBeforeReadingPayload()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.png");
        using (var stream = File.Create(path)) stream.SetLength(AccessoryImageAsset.MaximumSourceFileBytes + 1);
        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        StringAssert.Contains(error.Message, "64 MiB");
    }

    [TestMethod]
    public void EquivalentPixelsProduceSharedImmutableAssetId()
    {
        var first = WritePng("one.png", [0, 0, 0, 0, 20, 40, 60, 255]);
        var second = WritePng("two.png", [0, 0, 0, 0, 20, 40, 60, 255]);
        Assert.AreEqual(AccessoryImageAsset.ImportPng(first).AssetId, AccessoryImageAsset.ImportPng(second).AssetId);
    }

    private string WritePng(string name, byte[] pixels)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        var bitmap = BitmapSource.Create(pixels.Length / 4, 1, 96, 96, PixelFormats.Bgra32, null, pixels, pixels.Length);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
