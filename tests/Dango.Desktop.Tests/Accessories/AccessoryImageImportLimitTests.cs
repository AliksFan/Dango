using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
[DoNotParallelize]
public sealed class AccessoryImageImportLimitTests
{
    private const long StorageBytes = 32L * 1024 * 1024;
    private const long SourceBytes = 64L * 1024 * 1024;
    private static readonly uint[] CrcTable = CreateCrcTable();
    private string root = null!;

    [TestInitialize]
    public void CreateEvidenceRoot()
    {
        root = Path.Combine(AppContext.BaseDirectory, "artifacts", "accessory-image-import-limit-fixtures",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TestMethod]
    public void WithinAllStorageLimitsKeepsCanvasPixelsAndNormalizesDpiMetadataAndAssetId()
    {
        var first = WritePng("within-limits-with-metadata.png", 4096, 3906, FillSampleRow, includeMetadata: true);
        var second = WritePng("same-pixels-without-metadata.png", 4096, 3906, FillSampleRow);
        var originalHash = Digest(first);
        var image = AccessoryImageAsset.ImportPng(first);

        Assert.AreEqual(4096, image.PixelWidth);
        Assert.AreEqual(3906, image.PixelHeight);
        AssertFrozenNormalizedImage(image);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 20, 40, 90, 128, 20, 40, 90, 255 },
            ReadPixels(image, 0, 0, 3, 1));
        AssertNoSourceMetadata(image);
        Assert.AreEqual(originalHash, Digest(first), "Import must not rewrite the selected source file.");
        AssertImportInfo(image, 4096, 3906, new FileInfo(first).Length, resized: false, notice: false);

        var equivalent = AccessoryImageAsset.ImportPng(second);
        Assert.AreEqual(image.AssetId, equivalent.AssetId, "DPI and user metadata must not change the normalized asset ID.");
        CollectionAssert.AreEqual(image.CopyPngBytes(), equivalent.CopyPngBytes());
    }

    [TestMethod]
    public void LongestEdgeResizesRealTransparentPngAndDoesNotMixHiddenRgbIntoVisibleEdges()
    {
        var path = WritePng("8192-by-4096-transparent-checker.png", 8192, 4096, FillAlphaCheckerRow);
        var originalHash = Digest(path);
        var image = AccessoryImageAsset.ImportPng(path);

        Assert.AreEqual(4096, image.PixelWidth);
        Assert.AreEqual(2048, image.PixelHeight);
        AssertFrozenNormalizedImage(image);
        var pixel = ReadPixels(image, image.PixelWidth / 2, image.PixelHeight / 2, 1, 1);
        Assert.IsTrue(pixel[0] <= 2 && pixel[1] <= 2 && pixel[2] >= 250,
            "Opaque red plus fully transparent blue must remain red after alpha-aware resampling.");
        Assert.IsTrue(pixel[3] is >= 126 and <= 129, "Half-covered pixels must retain approximately half alpha.");
        Assert.AreEqual(originalHash, Digest(path));
        AssertImportInfo(image, 8192, 4096, new FileInfo(path).Length, resized: true, notice: true);
    }

    [TestMethod]
    public void PixelLimitResizes4096SquareTo4000SquareEvenWhenLongestEdgeAlreadyFits()
    {
        var path = WritePng("4096-square.png", 4096, 4096, FillSampleRow);
        var originalHash = Digest(path);
        var image = AccessoryImageAsset.ImportPng(path);

        Assert.AreEqual(4000, image.PixelWidth);
        Assert.AreEqual(4000, image.PixelHeight);
        Assert.AreEqual(16_000_000L, (long)image.PixelWidth * image.PixelHeight);
        Assert.AreEqual(originalHash, Digest(path));
        AssertImportInfo(image, 4096, 4096, new FileInfo(path).Length, resized: true, notice: true);
    }

    [TestMethod]
    public void SourceAbove32MiBWithOnlyMetadataExcessIsReencodedWithoutLosingPixelDimensions()
    {
        var path = WritePng("33MiB-metadata-with-three-pixels.png", 3, 1, FillSampleRow,
            includeMetadata: true, largeTextBytes: 33 * 1024 * 1024);
        var originalBytes = new FileInfo(path).Length;
        Assert.IsTrue(originalBytes > StorageBytes && originalBytes <= SourceBytes);
        var originalHash = Digest(path);
        var image = AccessoryImageAsset.ImportPng(path);

        Assert.AreEqual(3, image.PixelWidth);
        Assert.AreEqual(1, image.PixelHeight);
        Assert.IsTrue(image.EncodedByteLength <= StorageBytes);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 20, 40, 90, 128, 20, 40, 90, 255 },
            ReadPixels(image, 0, 0, 3, 1));
        AssertNoSourceMetadata(image);
        Assert.AreEqual(originalHash, Digest(path));
        AssertImportInfo(image, 3, 1, originalBytes, resized: false, notice: true);
        Assert.IsTrue(ReadInfo<bool>(image, "WasCompressed"));
        StringAssert.Contains(ReadNotice(image)!, "编码");
    }

    [TestMethod]
    public void NormalizedEncodingAbove32MiBTriggersFurtherProportionalResize()
    {
        var random = new Random(0x5A17D4);
        var path = WritePng("4000-square-fixed-seed-random-bgra.png", 4000, 4000, (_, row) =>
        {
            random.NextBytes(row.AsSpan(1));
            // The generated values are BGRA; PNG scanlines store RGBA.
            for (var offset = 1; offset < row.Length; offset += 4)
                (row[offset], row[offset + 2]) = (row[offset + 2], row[offset]);
        }, compressionLevel: CompressionLevel.NoCompression);
        var originalBytes = new FileInfo(path).Length;
        Assert.IsTrue(originalBytes > StorageBytes && originalBytes <= SourceBytes,
            $"The real incompressible source must exceed 32 MiB within the 64 MiB source budget; actual {originalBytes} bytes.");
        var originalHash = Digest(path);
        var image = AccessoryImageAsset.ImportPng(path);

        Assert.IsTrue(image.PixelWidth is > 0 and < 4000 && image.PixelHeight is > 0 and < 4000);
        Assert.AreEqual(image.PixelWidth, image.PixelHeight, "Additional reduction must preserve the square aspect ratio.");
        Assert.IsTrue(image.EncodedByteLength is > 0 and <= StorageBytes);
        Assert.IsTrue((long)image.PixelWidth * image.PixelHeight <= 16_000_000);
        AssertFrozenNormalizedImage(image);
        Assert.AreEqual(originalHash, Digest(path));
        AssertImportInfo(image, 4000, 4000, originalBytes, resized: true, notice: true);
        Assert.IsTrue(ReadInfo<bool>(image, "WasCompressed"));
    }

    [TestMethod]
    public void SourceFileAbove64MiBIsRejectedBySourceBudgetBeforeInvalidPayloadIsParsed()
    {
        var path = Path.Combine(root, "64MiB-plus-one-with-invalid-payload.png");
        using (var stream = File.Create(path)) stream.SetLength(SourceBytes + 1);

        AssertSourceBudgetRejection(path);
        Assert.AreEqual(SourceBytes + 1, new FileInfo(path).Length);
    }

    [TestMethod]
    [DataRow(16385, 2, 8)]
    [DataRow(16384, 4000, 8)]
    [DataRow(8192, 4100, 16)]
    [DataRow(int.MaxValue, int.MaxValue, 8)]
    public void SourceDimensionPixelAndDecodedSurfaceBudgetsRejectBeforeInflatingPayload(int width, int height, int bitDepth)
    {
        var path = WritePngWithRawData($"source-budget-{width}-{height}-{bitDepth}.png", width, height,
            bitDepth, [0]);
        AssertSourceBudgetRejection(path);
    }

    [TestMethod]
    public void BadChunkCrcIsRejectedByPreflightWithSpecificReason()
    {
        var path = WritePng("bad-IHDR-crc.png", 3, 1, FillSampleRow);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = 32;
            var value = stream.ReadByte();
            stream.Position = 32;
            stream.WriteByte((byte)(value ^ 1));
        }

        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        StringAssert.Contains(error.Message, "CRC");
        StringAssert.Contains(error.Message, "校验");
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(10)]
    public void IdatInflatedLengthMustMatchHeaderBeforeWpfDecode(int rawLength)
    {
        // A 2x1 8-bit RGBA image requires exactly one filter byte plus eight channel bytes.
        byte[] validRow = [0, 0, 0, 0, 0, 90, 40, 20, 255];
        var raw = new byte[rawLength];
        validRow.AsSpan(0, Math.Min(validRow.Length, raw.Length)).CopyTo(raw);
        var path = WritePngWithRawData($"IDAT-expanded-length-{rawLength}.png", 2, 1, 8, raw);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        StringAssert.Contains(error.Message, "IDAT");
        StringAssert.Contains(error.Message, "展开数据长度");
    }

    [TestMethod]
    [DataRow("PLTE", 2)]
    [DataRow("PLTE", 769)]
    [DataRow("tRNS", 1)]
    [DataRow("tRNS", 65536)]
    public void RetainedPixelDescriptionChunksMustHaveLegalBoundedPayloads(string type, int length)
    {
        var baseline = WritePng("small-valid-rgba.png", 3, 1, FillSampleRow);
        var path = InsertChunk(baseline, $"invalid-{type}-{length}.png", type, new byte[length]);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        StringAssert.Contains(error.Message, type);
    }

    [TestMethod]
    [DataRow("PLTE", false)]
    [DataRow("PLTE", true)]
    [DataRow("tRNS", false)]
    [DataRow("tRNS", true)]
    public void RetainedPixelDescriptionChunksCannotRepeatOrFollowIdat(string type, bool afterIdat)
    {
        var baseline = type == "PLTE"
            ? WritePng("small-valid-rgba.png", 3, 1, FillSampleRow)
            : WritePngWithRawData("small-valid-rgb.png", 3, 1, 8, [0, 90, 40, 20, 90, 40, 20, 90, 40, 20], colorType: 2);
        var payload = new byte[type == "PLTE" ? 3 : 6];
        var path = InsertChunk(baseline, $"invalid-order-{type}-{afterIdat}.png", type, payload,
            afterIdat: afterIdat, duplicate: !afterIdat);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        StringAssert.Contains(error.Message, type);
    }

    [TestMethod]
    public void IndexedColorRequiresPaletteAndCannotExceedItsBitDepthPaletteCapacity()
    {
        var missing = WritePngWithRawData("indexed-without-palette.png", 3, 1, 8, [0, 0, 1, 0], colorType: 3);
        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(missing));
        StringAssert.Contains(error.Message, "PLTE");

        var oneBit = WritePngWithRawData("one-bit-indexed-source.png", 3, 1, 1, [0, 0], colorType: 3);
        var excessive = InsertChunk(oneBit, "one-bit-indexed-with-three-palette-entries.png", "PLTE", new byte[9]);
        error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(excessive));
        StringAssert.Contains(error.Message, "PLTE");
    }

    [TestMethod]
    public void ValidTwoPixelAdam7RgbaPngImportsBothNonemptyPasses()
    {
        // For a 2x1 canvas only Adam7 passes 1 and 6 are nonempty, each with one RGBA pixel.
        var path = WritePngWithRawData("valid-two-pixel-adam7.png", 2, 1, 8,
            [0, 255, 0, 0, 0, 0, 90, 40, 20, 128], interlaced: true);

        AssertSmallTransparentSourceImportsUnchanged(path);
    }

    [TestMethod]
    public void ValidOneBitIndexedPngKeepsLegalPaletteTransparency()
    {
        // Index 0 then index 1, packed in the two most significant bits of the scanline byte.
        var path = WritePngWithRawData("valid-one-bit-indexed-transparent.png", 2, 1, 1, [0, 0x40],
            colorType: 3, palette: [200, 60, 40, 40, 80, 200], transparency: [0, 128]);

        AssertSmallTransparentSourceImportsUnchanged(path);
    }

    [TestMethod]
    public void ValidRgba16PngConvertsTransparentAndHalfAlphaPixelsToBgra8()
    {
        // Channels are big-endian 16-bit samples; their exact 8-bit color rounding is not the contract.
        var path = WritePngWithRawData("valid-rgba16-transparent.png", 2, 1, 16,
            [0, 0xFF, 0xFF, 0, 0, 0x80, 0, 0, 0, 0x40, 0, 0x80, 0, 0xC0, 0, 0x80, 0]);

        AssertSmallTransparentSourceImportsUnchanged(path);
    }

    [TestMethod]
    [DataRow(8192, 4096)]
    [DataRow(4096, 4096)]
    public void StoredAssetsAboveDimensionOrPixelLimitsAreRejectedWithoutAutomaticResize(int width, int height)
    {
        var path = WritePng($"stored-{width}-{height}.png", width, height, FillSampleRow);
        var originalHash = Digest(path);

        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ReadStored(path, originalHash));
        Assert.AreEqual(originalHash, Digest(path));
    }

    [TestMethod]
    public void StoredAssetAbove32MiBIsRejectedEvenWhenImportCanRemoveMetadata()
    {
        var path = WritePng("stored-33MiB-metadata.png", 3, 1, FillSampleRow, largeTextBytes: 33 * 1024 * 1024);
        var originalHash = Digest(path);

        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ReadStored(path, originalHash));
        Assert.AreEqual(originalHash, Digest(path));
    }

    [TestMethod]
    public void ReloadedNormalizedAssetHasNoImportOnlyNoticeOrImportInfo()
    {
        var source = WritePng("source-for-reload.png", 3, 1, FillSampleRow);
        var imported = AccessoryImageAsset.ImportPng(source);
        var stored = Path.Combine(root, imported.AssetId + ".png");
        File.WriteAllBytes(stored, imported.CopyPngBytes());
        var loaded = AccessoryImageAsset.ReadStored(stored, imported.AssetId);

        Assert.AreEqual(imported.AssetId, loaded.AssetId);
        CollectionAssert.AreEqual(ReadPixels(imported, 0, 0, 3, 1), ReadPixels(loaded, 0, 0, 3, 1));
        Assert.IsNull(ReadProperty(loaded, "ImportInfo"));
        Assert.IsNull(ReadNotice(loaded));
    }

    private static void AssertSourceBudgetRejection(string path)
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(() => AccessoryImageAsset.ImportPng(path));
        StringAssert.Contains(error.Message, "源图片");
        StringAssert.Contains(error.Message, "上限");
    }

    private static void AssertSmallTransparentSourceImportsUnchanged(string path)
    {
        Assert.IsTrue(new FileInfo(path).Length < 1024, "Format regressions must use small real PNG fixtures.");
        var originalHash = Digest(path);
        var image = AccessoryImageAsset.ImportPng(path);

        Assert.AreEqual(2, image.PixelWidth);
        Assert.AreEqual(1, image.PixelHeight);
        AssertFrozenNormalizedImage(image);
        var pixels = ReadPixels(image, 0, 0, 2, 1);
        Assert.AreEqual((byte)0, pixels[3], "The first pixel must remain fully transparent.");
        Assert.IsTrue(pixels[7] is > 0 and < 255, "The second pixel must remain visible and partly transparent.");
        Assert.AreEqual(originalHash, Digest(path), "Import must not rewrite the source PNG.");
    }

    private static void AssertFrozenNormalizedImage(AccessoryImageAsset image)
    {
        Assert.IsTrue(image.Bitmap.IsFrozen);
        Assert.AreEqual(PixelFormats.Bgra32, image.Bitmap.Format);
        Assert.AreEqual(96d, image.Bitmap.DpiX);
        Assert.AreEqual(96d, image.Bitmap.DpiY);
    }

    private static byte[] ReadPixels(AccessoryImageAsset image, int x, int y, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        image.Bitmap.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        return pixels;
    }

    private static void AssertImportInfo(AccessoryImageAsset image, int width, int height, long fileBytes, bool resized, bool notice)
    {
        Assert.IsNotNull(ReadProperty(image, "ImportInfo"), "Every successfully imported source requires its import result.");
        Assert.AreEqual(width, ReadInfo<int>(image, "OriginalWidth"));
        Assert.AreEqual(height, ReadInfo<int>(image, "OriginalHeight"));
        Assert.AreEqual(image.PixelWidth, ReadInfo<int>(image, "PixelWidth"));
        Assert.AreEqual(image.PixelHeight, ReadInfo<int>(image, "PixelHeight"));
        Assert.AreEqual(fileBytes, ReadInfo<long>(image, "OriginalFileBytes"));
        Assert.AreEqual(image.EncodedByteLength, ReadInfo<long>(image, "NormalizedFileBytes"));
        Assert.AreEqual(resized, ReadInfo<bool>(image, "WasResized"));
        Assert.AreEqual(image.EncodedByteLength < fileBytes, ReadInfo<bool>(image, "WasCompressed"));
        if (notice) Assert.IsFalse(string.IsNullOrWhiteSpace(ReadNotice(image)));
        else Assert.IsNull(ReadNotice(image), "Ordinary normalization below the storage limit needs no reduction notice.");
    }

    // Reflection keeps the initial failure run compilable against the previous public API.
    private static object? ReadProperty(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(property, "Missing import-result property " + name);
        return property.GetValue(instance);
    }

    private static T ReadInfo<T>(AccessoryImageAsset image, string name)
    {
        var info = ReadProperty(image, "ImportInfo");
        Assert.IsNotNull(info);
        var value = ReadProperty(info, name);
        Assert.IsInstanceOfType<T>(value);
        return (T)value;
    }

    private static string? ReadNotice(AccessoryImageAsset image)
    {
        var info = ReadProperty(image, "ImportInfo");
        return info is null ? null : (string?)ReadProperty(info, "Notice");
    }

    private static void AssertNoSourceMetadata(AccessoryImageAsset image)
    {
        var bytes = image.CopyPngBytes();
        var offset = 8;
        while (offset < bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            Assert.IsFalse(type is "tEXt" or "zTXt" or "iTXt" or "iCCP", "Source metadata remains in normalized PNG: " + type);
            offset = checked(offset + 12 + length);
        }
        Assert.AreEqual(bytes.Length, offset);
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private string WritePng(string name, int width, int height, Action<int, byte[]> fillRow,
        bool includeMetadata = false, int largeTextBytes = 0, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        var path = Path.Combine(root, name);
        using var output = File.Create(path);
        WriteHeader(output, width, height, 8);
        if (includeMetadata)
        {
            WriteChunk(output, "tEXt"u8, "FictionalNote\0accessory-limit-fixture"u8);
            Span<byte> physical = stackalloc byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(physical[..4], 11811);
            BinaryPrimitives.WriteUInt32BigEndian(physical.Slice(4, 4), 11811);
            physical[8] = 1;
            WriteChunk(output, "pHYs"u8, physical);
        }
        if (largeTextBytes > 0) WriteLargeTextChunk(output, largeTextBytes);
        using (var chunks = new IdatChunkStream(output))
        {
            using var compressed = new ZLibStream(chunks, compressionLevel, leaveOpen: true);
            var row = new byte[checked(1 + width * 4)];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0;
                fillRow(y, row);
                compressed.Write(row);
            }
        }
        WriteChunk(output, "IEND"u8, []);
        return path;
    }

    private string WritePngWithRawData(string name, int width, int height, int bitDepth, byte[] raw, int colorType = 6,
        bool interlaced = false, byte[]? palette = null, byte[]? transparency = null)
    {
        var path = Path.Combine(root, name);
        using var output = File.Create(path);
        WriteHeader(output, width, height, bitDepth, colorType, interlaced);
        if (palette is not null) WriteChunk(output, "PLTE"u8, palette);
        if (transparency is not null) WriteChunk(output, "tRNS"u8, transparency);
        using (var chunks = new IdatChunkStream(output))
        {
            using var compressed = new ZLibStream(chunks, CompressionLevel.Fastest, leaveOpen: true);
            compressed.Write(raw);
        }
        WriteChunk(output, "IEND"u8, []);
        return path;
    }

    private string InsertChunk(string baseline, string name, string type, byte[] payload,
        bool afterIdat = false, bool duplicate = false)
    {
        // Baselines are tiny; inserted chunks always carry a correct CRC to isolate semantic validation.
        var bytes = File.ReadAllBytes(baseline);
        var offset = afterIdat ? bytes.Length - 12 : 33;
        var path = Path.Combine(root, name);
        using var output = File.Create(path);
        output.Write(bytes.AsSpan(0, offset));
        WriteChunk(output, Encoding.ASCII.GetBytes(type), payload);
        if (duplicate) WriteChunk(output, Encoding.ASCII.GetBytes(type), payload);
        output.Write(bytes.AsSpan(offset));
        return path;
    }

    private static void FillSampleRow(int _, byte[] row)
    {
        for (var offset = 1; offset < row.Length; offset += 4)
        {
            row[offset] = 90;
            row[offset + 1] = 40;
            row[offset + 2] = 20;
            row[offset + 3] = 128;
        }
        row.AsSpan(1, 4).Clear();
        if (row.Length >= 13) row[12] = 255;
    }

    private static void FillAlphaCheckerRow(int y, byte[] row)
    {
        for (var offset = 1; offset < row.Length; offset += 4)
        {
            var opaque = (((offset - 1) / 4 + y) & 1) == 0;
            row[offset] = opaque ? (byte)255 : (byte)0;
            row[offset + 1] = 0;
            row[offset + 2] = opaque ? (byte)0 : (byte)255;
            row[offset + 3] = opaque ? (byte)255 : (byte)0;
        }
    }

    private static void WriteHeader(Stream output, int width, int height, int bitDepth, int colorType = 6, bool interlaced = false)
    {
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;
        header[12] = interlaced ? (byte)1 : (byte)0;
        WriteChunk(output, "IHDR"u8, header);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~UpdateCrc(UpdateCrc(uint.MaxValue, type), data));
        output.Write(number);
    }

    private static void WriteLargeTextChunk(Stream output, int length)
    {
        ReadOnlySpan<byte> keyword = "FictionalPadding\0"u8;
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)length);
        output.Write(number);
        output.Write("tEXt"u8);
        output.Write(keyword);
        var crc = UpdateCrc(UpdateCrc(uint.MaxValue, "tEXt"u8), keyword);
        var buffer = new byte[16 * 1024];
        Array.Fill(buffer, (byte)'x');
        var remaining = length - keyword.Length;
        while (remaining > 0)
        {
            var count = Math.Min(buffer.Length, remaining);
            output.Write(buffer, 0, count);
            crc = UpdateCrc(crc, buffer.AsSpan(0, count));
            remaining -= count;
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }

    private sealed class IdatChunkStream(Stream output) : Stream
    {
        private readonly byte[] buffer = new byte[64 * 1024];
        private int buffered;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush()
        {
            if (buffered == 0) return;
            WriteChunk(output, "IDAT"u8, buffer.AsSpan(0, buffered));
            buffered = 0;
        }
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                var count = Math.Min(bytes.Length, buffer.Length - buffered);
                bytes[..count].CopyTo(buffer.AsSpan(buffered));
                buffered += count;
                bytes = bytes[count..];
                if (buffered == buffer.Length) Flush();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Flush();
            base.Dispose(disposing);
        }
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
