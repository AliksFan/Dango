using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorSourceTests
{
    [STATestMethod]
    public void OpaqueAndBlankPngCanEnterCreativeDraftButCannotBecomeFinalAssets()
    {
        foreach (var alpha in new byte[] { 0, 255 })
        {
            var png = Png(Texture(12, 8, alpha));
            var source = AccessoryEditorSourceAsset.FromPng(png);
            Assert.AreEqual(12, source.PixelWidth); Assert.AreEqual(8, source.PixelHeight);
            Assert.IsTrue(source.Bitmap.IsFrozen);
            Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.FromRenderedPng(source.CopyPngBytes()));
        }
        var valid = AccessoryEditorSourceAsset.FromPng(Png(Texture(12, 8, 128)));
        var final = AccessoryImageAsset.FromRenderedPng(valid.CopyPngBytes());
        Assert.AreEqual(valid.AssetId, final.AssetId);
    }

    [STATestMethod]
    public void FileHeaderChoosesTheDecoderAndSourceBytesRemainUnchanged()
    {
        var root = Root(); var path = Path.Combine(root, "fictional-source.data");
        var bytes = Png(Texture(10, 6, 255)); File.WriteAllBytes(path, bytes);
        var hash = SHA256.HashData(bytes);
        var source = AccessoryEditorSourceAsset.Import(path);
        CollectionAssert.AreEqual(hash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.AreEqual(10, source.PixelWidth);
        var copy = source.CopyPngBytes(); copy[0] = 0;
        Assert.AreEqual(137, source.CopyPngBytes()[0]);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(source.CopyPngBytes())), source.AssetId);
        File.WriteAllBytes(path, "GIF89a"u8.ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorSourceAsset.Import(path));
    }

    [STATestMethod]
    public void WorkingAssetLimitsAreStrictWhileSelectedSourcesMayBeResizedOnce()
    {
        var path = Path.Combine(Root(), "fictional-large.png");
        var bytes = Png(Texture(4097, 1, 255)); File.WriteAllBytes(path, bytes);
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorSourceAsset.FromPng(bytes));
        var source = AccessoryEditorSourceAsset.Import(path);
        Assert.AreEqual(4096, source.PixelWidth); Assert.AreEqual(1, source.PixelHeight);
        Assert.IsTrue(source.ImportInfo!.WasResized);
        var reopened = AccessoryEditorSourceAsset.FromPng(source.CopyPngBytes());
        Assert.AreEqual(source.AssetId, reopened.AssetId);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void SourceMetadataAndOriginalDpiAreNotRetained()
    {
        var metadata = new BitmapMetadata("png");
        metadata.SetQuery("/tEXt/{str=Comment}", "fictional-editor-source-comment");
        var bitmap = Texture(12, 8, 128, 300);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var encoded = new MemoryStream(); encoder.Save(encoded);
        var bytes = encoded.ToArray();
        Assert.IsTrue(System.Text.Encoding.UTF8.GetString(bytes).Contains("fictional-editor-source-comment", StringComparison.Ordinal));
        var source = AccessoryEditorSourceAsset.FromPng(bytes);
        Assert.AreEqual(96d, source.Bitmap.DpiX, .01); Assert.AreEqual(96d, source.Bitmap.DpiY, .01);
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(source.CopyPngBytes()).Contains("fictional-editor-source-comment", StringComparison.Ordinal));
    }

    [STATestMethod]
    [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)]
    public void JpegExifOrientationsAreAppliedToPixelsBeforeMetadataIsDiscarded(int orientation)
    {
        const int width = 24, height = 16;
        var jpeg = Jpeg(Texture(width, height, 255));
        var root = Root(); var originalPath = Path.Combine(root, "fictional-original.jpg"); File.WriteAllBytes(originalPath, jpeg);
        var baseline = AccessoryEditorSourceAsset.Import(originalPath);
        var orientedBytes = AddSegment(jpeg, 225, Exif(orientation));
        var path = Path.Combine(root, "fictional-oriented.png"); File.WriteAllBytes(path, orientedBytes);
        var result = AccessoryEditorSourceAsset.Import(path);
        var swap = orientation >= 5;
        Assert.AreEqual(swap ? height : width, result.PixelWidth);
        Assert.AreEqual(swap ? width : height, result.PixelHeight);
        var before = Pixels(baseline.Bitmap); var after = Pixels(result.Bitmap);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var (atX, atY) = orientation switch
            {
                2 => (width - 1 - x, y), 3 => (width - 1 - x, height - 1 - y),
                4 => (x, height - 1 - y), 5 => (y, x), 6 => (height - 1 - y, x),
                7 => (height - 1 - y, width - 1 - x), 8 => (y, width - 1 - x), _ => (x, y),
            };
            for (var channel = 0; channel < 4; channel++)
                Assert.IsTrue(Math.Abs(before[(y * width + x) * 4 + channel] - after[(atY * result.PixelWidth + atX) * 4 + channel]) <= 2,
                    $"Orientation {orientation} failed at synthetic pixel {x},{y} channel {channel}.");
        }
        Assert.IsFalse(System.Text.Encoding.ASCII.GetString(result.CopyPngBytes()).Contains("Exif", StringComparison.Ordinal));
        CollectionAssert.AreEqual(orientedBytes, File.ReadAllBytes(path));
    }

    [STATestMethod]
    public void MultipleJpegImagesMalformedExifAndOversizedHeadersAreRejected()
    {
        var jpeg = Jpeg(Texture(24, 16, 255));
        var huge = (byte[])jpeg.Clone();
        var frame = Enumerable.Range(0, huge.Length - 1).First(i => huge[i] == 255 && huge[i + 1] is 192 or 193 or 194);
        huge[frame + 5] = 64; huge[frame + 6] = 0; huge[frame + 7] = 64; huge[frame + 8] = 0;
        var malformed = new[]
        {
            jpeg.Concat(jpeg).ToArray(), AddSegment(jpeg, 226, "MPF\0"u8.ToArray()),
            AddSegment(jpeg, 225, Exif(9)), AddSegment(AddSegment(jpeg, 225, Exif(1)), 225, Exif(1)),
            AddSegment(jpeg, 225, "Exif\0\0broken"u8.ToArray()), huge, jpeg[..^3],
        };
        foreach (var bytes in malformed)
        {
            var path = Path.Combine(Root(), "fictional-invalid.jpg"); File.WriteAllBytes(path, bytes);
            Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorSourceAsset.Import(path));
        }
    }

    [STATestMethod]
    public void PngExifOrientationIsAppliedForSourceAndRejectedAsUnnormalizedWorkingAsset()
    {
        var png = Png(Texture(24, 16, 128)); var exif = Exif(6)[6..];
        var chunk = new byte[exif.Length + 12]; BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)exif.Length);
        "eXIf"u8.CopyTo(chunk.AsSpan(4)); exif.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (var b in chunk.AsSpan(4, exif.Length + 4))
        {
            crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + exif.Length), crc ^ uint.MaxValue);
        var oriented = png[..33].Concat(chunk).Concat(png[33..]).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorSourceAsset.FromPng(oriented));
        var path = Path.Combine(Root(), "fictional-oriented.png"); File.WriteAllBytes(path, oriented);
        var source = AccessoryEditorSourceAsset.Import(path);
        Assert.AreEqual(16, source.PixelWidth); Assert.AreEqual(24, source.PixelHeight);
        var final = AccessoryImageAsset.ImportPng(path);
        Assert.AreEqual(source.PixelWidth, final.PixelWidth); Assert.AreEqual(source.PixelHeight, final.PixelHeight);
        CollectionAssert.AreEqual(Pixels(source.Bitmap), Pixels(final.Bitmap));
        var original = Pixels(AccessoryEditorSourceAsset.FromPng(png).Bitmap); var actual = Pixels(source.Bitmap);
        for (var y = 0; y < 16; y++) for (var x = 0; x < 24; x++) for (var c = 0; c < 4; c++)
            Assert.AreEqual(original[(y * 24 + x) * 4 + c], actual[(x * 16 + 15 - y) * 4 + c]);
        Assert.AreEqual(source.AssetId, AccessoryEditorSourceAsset.FromPng(source.CopyPngBytes()).AssetId);
    }

    [STATestMethod]
    public void PngCorruptionAndInputFileByteBudgetAreRejectedBeforeDraftCreation()
    {
        var png = Png(Texture(12, 8, 255)); png[20] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorSourceAsset.FromPng(png));
        var path = Path.Combine(Root(), "fictional-excess-source.png");
        using (var file = File.Create(path)) file.SetLength(AccessoryImageAsset.MaximumSourceFileBytes + 1);
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorSourceAsset.Import(path));
    }

    private static string Root()
    {
        var path = Path.Combine(TestRepository.Root, "artifacts", "accessory-source-e1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static BitmapSource Texture(int width, int height, byte alpha, double dpi = 96)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            pixels[at] = (byte)(x < width / 2 ? 16 : 210); pixels[at + 1] = (byte)(y < height / 2 ? 32 : 180);
            pixels[at + 2] = (byte)(x < width / 2 ? 230 : 48); pixels[at + 3] = alpha;
        }
        var result = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * 4); result.Freeze(); return result;
    }
    private static byte[] Pixels(BitmapSource bitmap)
    { var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes; }
    private static byte[] Png(BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = new MemoryStream(); encoder.Save(output); return output.ToArray(); }
    private static byte[] Jpeg(BitmapSource bitmap) { var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = new MemoryStream(); encoder.Save(output); return output.ToArray(); }
    private static byte[] Exif(int orientation) => [69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
    private static byte[] AddSegment(byte[] jpeg, byte marker, byte[] payload)
    {
        var result = new byte[jpeg.Length + payload.Length + 4]; result[0] = 255; result[1] = 216; result[2] = 255; result[3] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), (ushort)(payload.Length + 2));
        payload.CopyTo(result, 6); jpeg.AsSpan(2).CopyTo(result.AsSpan(payload.Length + 6)); return result;
    }
}
