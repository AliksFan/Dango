using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.AccessoryAssets;
using Dango.Desktop.Accessories;
using Dango.Protocol.Accessories;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Real WPF/file/portable-codec integration; no account service or network is simulated as production.</summary>
[TestClass]
public sealed class AccessoryCanonicalImageIntegrationTests
{
    private string root = null!;

    [TestInitialize]
    public void Initialize() => root = Path.Combine(Path.GetTempPath(), "Dango-accessory-canonical-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        var resolved = Path.GetFullPath(root);
        if (Path.GetDirectoryName(resolved) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Path.GetFileName(resolved).StartsWith("Dango-accessory-canonical-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected integration test root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    [TestMethod]
    public async Task WpfSavedPngMapsToServerHashAndReloadsWithoutPixelOrCanvasLoss()
    {
        var source = WriteSource();
        var imported = AccessoryImageAsset.ImportPng(source);
        var blobs = new FileAccessoryBlobStore(Path.Combine(root, "blobs"));
        var result = await blobs.ImportAsync(new MemoryStream(imported.CopyPngBytes()), imported.AssetId);
        Assert.AreEqual(imported.AssetId, result.SourceDigest);
        Assert.AreNotEqual(imported.AssetId, result.BlobDigest, "The test must exercise a genuine cross-encoder ID change.");
        Assert.AreEqual(UploadedAccessoryAsset.CurrentNormalizationVersion, result.NormalizationVersion);
        var canonical = await blobs.ReadAsync(result.BlobDigest);
        var restoredPath = Path.Combine(root, "restored.png");
        File.WriteAllBytes(restoredPath, canonical.CopyPngBytes());
        File.Move(source, Path.Combine(root, "source-moved.png"));
        var restored = AccessoryImageAsset.ReadStored(restoredPath, result.BlobDigest);
        Assert.AreEqual(imported.PixelWidth, restored.PixelWidth);
        Assert.AreEqual(imported.PixelHeight, restored.PixelHeight);
        Assert.AreEqual(result.BlobDigest, restored.AssetId);
        var before = Pixels(imported); var after = Pixels(restored);
        for (var index = 0; index < before.Length; index += 4)
        {
            Assert.AreEqual(before[index + 3], after[index + 3]);
            if (before[index + 3] != 0)
                CollectionAssert.AreEqual(before[index..(index + 4)], after[index..(index + 4)]);
        }
    }

    [TestMethod]
    public async Task ReuploadedCanonicalBytesDeduplicateToSameFile()
    {
        var image = AccessoryImageAsset.ImportPng(WriteSource());
        var blobsRoot = Path.Combine(root, "blobs");
        var blobs = new FileAccessoryBlobStore(blobsRoot);
        var original = await blobs.ImportAsync(new MemoryStream(image.CopyPngBytes()), image.AssetId);
        var canonical = await blobs.ReadAsync(original.BlobDigest);
        var repeated = await blobs.ImportAsync(new MemoryStream(canonical.CopyPngBytes()), canonical.Digest);
        Assert.IsFalse(repeated.Created);
        Assert.AreEqual(original.BlobDigest, repeated.BlobDigest);
        Assert.HasCount(1, Directory.GetFiles(blobsRoot, "*.png", SearchOption.AllDirectories));
    }

    private string WriteSource()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.png");
        byte[] pixels = [50, 60, 70, 0, 22, 40, 60, 255, 77, 90, 100, 128];
        var bitmap = BitmapSource.Create(3, 1, 144, 144, PixelFormats.Bgra32, null, pixels, 12);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static byte[] Pixels(AccessoryImageAsset image)
    {
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.Bitmap.CopyPixels(bytes, image.PixelWidth * 4, 0);
        return bytes;
    }
}
