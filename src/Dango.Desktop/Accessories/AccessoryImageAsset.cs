using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Accessories;

/// <summary>Import-only conversion details; contains no source path or user metadata.</summary>
public sealed record AccessoryImageImportInfo(int OriginalWidth, int OriginalHeight, int PixelWidth,
    int PixelHeight, long OriginalFileBytes, long NormalizedFileBytes)
{
    public bool WasResized => OriginalWidth != PixelWidth || OriginalHeight != PixelHeight;
    public bool WasCompressed => NormalizedFileBytes < OriginalFileBytes;
    public string? Notice => !WasResized && OriginalFileBytes <= AccessoryImageAsset.MaximumFileBytes ? null :
        $"图片已压缩（{(WasResized ? "等比例缩小并重新编码" : "去除元数据并重新编码")}）：{OriginalWidth} × {OriginalHeight} → {PixelWidth} × {PixelHeight}；" +
        $"{OriginalFileBytes / (1024d * 1024):0.##} MiB → {NormalizedFileBytes / (1024d * 1024):0.##} MiB。源文件未修改。";
}

/// <summary>Detached, metadata-free immutable PNG pixels. No source path is retained.</summary>
public sealed class AccessoryImageAsset
{
    public const long MaximumFileBytes = 32L * 1024 * 1024;
    public const int MaximumEdge = 4096;
    public const long MaximumPixels = 16_000_000;
    public const long MaximumSourceFileBytes = 64L * 1024 * 1024;
    public const int MaximumSourceEdge = 16384;
    public const long MaximumSourcePixels = 64_000_000;
    public const long MaximumSourceDecodedBytes = 256L * 1024 * 1024;
    private const int MaximumChunks = 4096;
    private static readonly uint[] CrcTable = CreateCrcTable();
    private readonly byte[] pngBytes;

    private AccessoryImageAsset(string assetId, BitmapSource bitmap, byte[] pngBytes, AccessoryImageImportInfo? importInfo = null)
    {
        AssetId = assetId;
        Bitmap = bitmap;
        this.pngBytes = pngBytes;
        ImportInfo = importInfo;
    }

    public string AssetId { get; }
    public int PixelWidth => Bitmap.PixelWidth;
    public int PixelHeight => Bitmap.PixelHeight;
    public BitmapSource Bitmap { get; }
    public long EncodedByteLength => pngBytes.LongLength;
    public AccessoryImageImportInfo? ImportInfo { get; }
    internal byte[] CopyPngBytes() => (byte[])pngBytes.Clone();

    /// <summary>Validates a native editor export at final-asset limits; never silently resizes it.</summary>
    public static AccessoryImageAsset FromRenderedPng(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.LongLength is < 1 or > MaximumFileBytes) throw new InvalidDataException("成品 PNG 文件须大于 0 且不超过 32 MiB。");
        var prepared = PreparePng((byte[])png.Clone(), source: false);
        try
        {
            using var input = new MemoryStream(prepared.Bytes, writable: false);
            var bitmap = CopyNormalizedPixels(DecodeFrame(input, prepared.Header), prepared.Header.Width, prepared.Header.Height);
            var encoded = TryEncode(bitmap) ?? throw new InvalidDataException("成品 PNG 重编码超出 32 MiB 上限。");
            return new(Convert.ToHexStringLower(SHA256.HashData(encoded)), bitmap, encoded);
        }
        catch (OutOfMemoryException) { throw new InvalidDataException("成品图片处理内存不足，草稿仍保留，可使用较小画布后重试。"); }
        catch (Exception error) when (IsDecodeFault(error)) { throw new InvalidDataException("成品 PNG 解码失败。", error); }
    }

    internal static AccessoryEditorSourceAsset ReadEditorPng(byte[] png, bool importing)
    {
        ArgumentNullException.ThrowIfNull(png);
        var maximum = importing ? MaximumSourceFileBytes : MaximumFileBytes;
        if (png.LongLength < 1 || png.LongLength > maximum) throw new InvalidDataException("编辑源 PNG 文件大小超出上限。");
        var prepared = PreparePng((byte[])png.Clone(), source: importing);
        try
        {
            using var input = new MemoryStream(prepared.Bytes, writable: false);
            var frame = AccessoryEditorSourceAsset.ApplyOrientation(DecodeFrame(input, prepared.Header), prepared.Header.Orientation);
            return NormalizeEditorFrame(frame, png.LongLength, importing);
        }
        catch (Exception error) when (IsDecodeFault(error)) { throw new InvalidDataException("编辑源 PNG 解码失败。", error); }
    }

    internal static AccessoryEditorSourceAsset NormalizeEditorFrame(BitmapSource frame, long originalFileBytes, bool allowResize)
    {
        var originalWidth = frame.PixelWidth; var originalHeight = frame.PixelHeight;
        var scale = allowResize ? Math.Min(1, Math.Min((double)MaximumEdge / Math.Max(originalWidth, originalHeight),
            Math.Sqrt((double)MaximumPixels / ((long)originalWidth * originalHeight)))) : 1;
        if (!allowResize) ValidateDimensions(originalWidth, originalHeight);
        while (true)
        {
            var width = Math.Max(1, (int)Math.Floor(originalWidth * scale));
            var height = Math.Max(1, (int)Math.Floor(originalHeight * scale));
            var bitmap = CopyNormalizedPixels(frame, width, height, requireTransparency: false);
            if (TryEncode(bitmap) is { } normalized)
                return new(bitmap, normalized, new(originalWidth, originalHeight, width, height, originalFileBytes, normalized.LongLength));
            if (!allowResize || width == 1 && height == 1) throw new InvalidDataException("编辑源无法编码到 32 MiB 工作图像上限内。");
            scale *= .8;
        }
    }

    public static AccessoryImageAsset ImportPng(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("首版仅支持静态透明 PNG 图片。");
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = ReadLimited(stream, MaximumSourceFileBytes, source: true);
        }
        catch (InvalidDataException) { throw; }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("无法读取选定图片。"); }
        catch (IOException) { throw new IOException("无法读取选定图片。"); }
        var prepared = PreparePng(bytes, source: true);
        var header = prepared.Header;
        try
        {
            using var input = new MemoryStream(prepared.Bytes, writable: false);
            var frame = AccessoryEditorSourceAsset.ApplyOrientation(DecodeFrame(input, header), header.Orientation);
            var originalWidth = frame.PixelWidth; var originalHeight = frame.PixelHeight;
            var scale = Math.Min(1, Math.Min((double)MaximumEdge / Math.Max(originalWidth, originalHeight),
                Math.Sqrt((double)MaximumPixels / ((long)originalWidth * originalHeight))));
            while (true)
            {
                var width = Math.Max(1, (int)Math.Floor(originalWidth * scale));
                var height = Math.Max(1, (int)Math.Floor(originalHeight * scale));
                var bitmap = CopyNormalizedPixels(frame, width, height);
                if (TryEncode(bitmap) is { } normalized)
                {
                    var info = new AccessoryImageImportInfo(originalWidth, originalHeight, width, height, bytes.LongLength, normalized.LongLength);
                    return new(Convert.ToHexStringLower(SHA256.HashData(normalized)), bitmap, normalized, info);
                }
                // Always resample the original decoded frame, never a previously reduced bitmap.
                if (width == 1 && height == 1) throw new InvalidDataException("图片无法转换到 32 MiB 保存上限内。");
                scale *= .8;
            }
        }
        catch (Exception error) when (IsDecodeFault(error))
        { throw new InvalidDataException("PNG 图片解码或转换失败。", error); }
    }

    internal static AccessoryImageAsset ReadStored(string path, string expectedAssetId)
    {
        if (!AccessoryItem.IsValidAssetId(expectedAssetId) || AccessoryItem.IsBuiltInAsset(expectedAssetId))
            throw new InvalidDataException("内部图片 ID 无效。");
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("内部图片不能是目录或链接。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = ReadLimited(stream, MaximumFileBytes, source: false);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedAssetId)
            throw new InvalidDataException("内部图片校验不一致。");
        var prepared = PreparePng(bytes, source: false);
        try
        {
            using var input = new MemoryStream(prepared.Bytes, writable: false);
            return new(expectedAssetId, CopyNormalizedPixels(DecodeFrame(input, prepared.Header),
                prepared.Header.Width, prepared.Header.Height), bytes);
        }
        catch (Exception error) when (IsDecodeFault(error))
        { throw new InvalidDataException("PNG 图片解码失败。", error); }
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaximumEdge || height > MaximumEdge || (long)width * height > MaximumPixels)
            throw new InvalidDataException("保存图片尺寸必须在 4096 像素及 16,000,000 总像素上限内。");
    }

    private static byte[] ReadLimited(Stream stream, long maximumBytes, bool source)
    {
        if (stream.Length < 1 || stream.Length > maximumBytes)
            throw new InvalidDataException(source ? "源图片文件必须大于 0 且不超过 64 MiB 安全上限。" : "保存 PNG 文件必须大于 0 且不超过 32 MiB 上限。");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static BitmapFrame DecodeFrame(Stream input, PngHeader header)
    {
        // Header, CRC, inflated scanline budget and metadata removal run before invoking WIC.
        var decoder = new PngBitmapDecoder(input,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count != 1) throw new InvalidDataException("不支持多帧 PNG。");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth != header.Width || frame.PixelHeight != header.Height)
            throw new InvalidDataException("PNG 解码尺寸与文件头不一致。");
        return frame;
    }

    private static BitmapSource CopyNormalizedPixels(BitmapSource frame, int width, int height, bool requireTransparency = true)
    {
        ValidateDimensions(width, height);
        BitmapSource converted;
        if (width == frame.PixelWidth && height == frame.PixelHeight)
            converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        else
        {
            // Filter premultiplied channels so invisible RGB cannot bleed into visible edges.
            var premultiplied = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var scaled = new TransformedBitmap(premultiplied,
                new ScaleTransform((double)width / frame.PixelWidth, (double)height / frame.PixelHeight));
            if (scaled.PixelWidth != width || scaled.PixelHeight != height)
                throw new InvalidDataException("PNG 缩放后的尺寸与目标不一致。");
            converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        }
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);
        if (requireTransparency)
        {
            var visible = false; var transparent = false;
            for (var index = 3; index < pixels.Length; index += 4)
            {
                visible |= pixels[index] != 0;
                transparent |= pixels[index] != 255;
            }
            if (!visible || !transparent)
                throw new InvalidDataException("图片须包含透明或半透明像素，并至少包含一个可见像素；转换后也须保持此条件。");
        }
        // Pixel rebuilding detaches the source stream, metadata, palette and original DPI.
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[]? TryEncode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new LimitedPngStream();
        try { encoder.Save(output); }
        catch (Exception) when (output.LimitExceeded) { return null; }
        return output.LimitExceeded ? null : output.ToArray();
    }

    private sealed class LimitedPngStream : MemoryStream
    {
        public bool LimitExceeded { get; private set; }
        private void CheckLength(long length)
        {
            if (length <= MaximumFileBytes) return;
            LimitExceeded = true;
            throw new IOException("PNG 编码超出 32 MiB 保存上限。");
        }
        public override void Write(byte[] buffer, int offset, int count)
        { CheckLength(checked(Position + count)); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { CheckLength(checked(Position + buffer.Length)); base.Write(buffer); }
        public override void WriteByte(byte value)
        { CheckLength(checked(Position + 1)); base.WriteByte(value); }
        public override void SetLength(long value)
        { CheckLength(value); base.SetLength(value); }
        public override long Position
        {
            get => base.Position;
            set { CheckLength(value); base.Position = value; }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(Position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            CheckLength(position); return base.Seek(offset, origin);
        }
    }

    private sealed record PngHeader(int Width, int Height, int BitDepth, int Channels, int ColorType, bool Interlaced, int Orientation = 1);
    private sealed record PreparedPng(PngHeader Header, byte[] Bytes);
    private readonly record struct PngChunk(int Offset, int Length);

    private static PreparedPng PreparePng(byte[] bytes, bool source)
    {
        ReadOnlySpan<byte> png = bytes;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 33 || !png[..8].SequenceEqual(signature)) throw new InvalidDataException("PNG 文件头无效。");
        var offset = 8; PngHeader? header = null; var imageData = false; var end = false;
        var kept = new List<PngChunk>(); var idat = new List<PngChunk>(); var keptLength = 8;
        var chunkCount = 0; var imageDataEnded = false;
        var paletteEntries = 0; var transparencySeen = false; var exifSeen = false;
        while (offset < png.Length)
        {
            if (++chunkCount > MaximumChunks) throw new InvalidDataException("PNG 区块数量超出安全上限。");
            if (png.Length - offset < 12) throw new InvalidDataException("PNG 区块不完整。");
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            if ((long)offset + length + 12 > png.Length) throw new InvalidDataException("PNG 区块长度无效。");
            var type = png.Slice(offset + 4, 4);
            var payloadLength = checked((int)length);
            var chunkLength = checked(payloadLength + 12);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8 + payloadLength, 4));
            if (ComputeCrc(png.Slice(offset + 4, payloadLength + 4)) != expectedCrc)
                throw new InvalidDataException("PNG 区块 CRC 校验失败。");
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                throw new InvalidDataException("首版不支持动画 PNG。");
            if (header is null)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) throw new InvalidDataException("PNG 尺寸头无效。");
                var width = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 12, 4));
                var depth = png[offset + 16]; var color = png[offset + 17];
                var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                var validDepth = color switch
                {
                    0 => depth is 1 or 2 or 4 or 8 or 16,
                    2 or 4 or 6 => depth is 8 or 16,
                    3 => depth is 1 or 2 or 4 or 8,
                    _ => false,
                };
                if (!validDepth || png[offset + 18] != 0 || png[offset + 19] != 0 || png[offset + 20] > 1)
                    throw new InvalidDataException("PNG 像素格式或压缩方式无效。");
                if (source)
                {
                    if (width < 1 || height < 1 || width > MaximumSourceEdge || height > MaximumSourceEdge ||
                        (long)width * height > MaximumSourcePixels ||
                        (long)width * height * Math.Max(4, (channels * depth + 7) / 8) > MaximumSourceDecodedBytes)
                        throw new InvalidDataException("源图片超出安全解码上限：最长边 16384、64,000,000 总像素或 256 MiB 像素面。请先在外部缩小。");
                }
                else
                {
                    if (width > int.MaxValue || height > int.MaxValue) throw new InvalidDataException("PNG 尺寸无效。");
                    ValidateDimensions((int)width, (int)height);
                }
                header = new((int)width, (int)height, depth, channels, color, png[offset + 20] == 1);
            }
            else if (type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG 存在重复尺寸头。");
            if (type.SequenceEqual("eXIf"u8))
            {
                if (exifSeen || imageData) throw new InvalidDataException("PNG 方向元数据位置或数量无效。");
                exifSeen = true;
                var orientation = AccessoryJpegSource.ReadOrientation(png.Slice(offset + 8, payloadLength));
                if (!source && orientation != 1) throw new InvalidDataException("工作／成品 PNG 必须已将方向应用到实际像素。");
                header = header with { Orientation = orientation };
            }
            if (type.SequenceEqual("PLTE"u8))
            {
                if (imageData || transparencySeen || paletteEntries != 0 || header.ColorType is 0 or 4 ||
                    payloadLength is < 3 or > 768 || payloadLength % 3 != 0 ||
                    header.ColorType == 3 && payloadLength / 3 > 1 << header.BitDepth)
                    throw new InvalidDataException("PNG PLTE 调色板长度、位置或数量无效。");
                paletteEntries = payloadLength / 3;
            }
            if (type.SequenceEqual("tRNS"u8))
            {
                var validLength = header.ColorType switch
                {
                    0 => payloadLength == 2,
                    2 => payloadLength == 6,
                    3 => paletteEntries != 0 && payloadLength >= 1 && payloadLength <= paletteEntries,
                    _ => false,
                };
                if (imageData || transparencySeen || !validLength)
                    throw new InvalidDataException("PNG tRNS 透明描述长度、位置或数量无效。");
                if (header.ColorType is 0 or 2)
                    for (var sample = 0; sample < payloadLength; sample += 2)
                        if (BinaryPrimitives.ReadUInt16BigEndian(png.Slice(offset + 8 + sample, 2)) >= 1 << header.BitDepth)
                            throw new InvalidDataException("PNG tRNS 透明样本超出像素位深。");
                transparencySeen = true;
            }
            var isData = type.SequenceEqual("IDAT"u8);
            if (isData)
            {
                if (imageDataEnded) throw new InvalidDataException("PNG 图片数据区块必须连续。");
                if (header.ColorType == 3 && paletteEntries == 0) throw new InvalidDataException("PNG 索引颜色缺少 PLTE 调色板。");
                imageData = true; idat.Add(new(offset + 8, payloadLength));
            }
            else if (imageData) imageDataEnded = true;
            var keep = type.SequenceEqual("IHDR"u8) || type.SequenceEqual("PLTE"u8) || type.SequenceEqual("tRNS"u8) ||
                isData || type.SequenceEqual("IEND"u8);
            if (!keep && (type[0] & 32) == 0) throw new InvalidDataException("PNG 包含未知的必要区块。");
            if (keep) { kept.Add(new(offset, chunkLength)); keptLength = checked(keptLength + chunkLength); }
            offset += chunkLength;
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || offset != png.Length) throw new InvalidDataException("PNG 结束区块无效。");
                end = true;
                break;
            }
        }
        if (!imageData || !end) throw new InvalidDataException("PNG 图片内容不完整。");
        ValidateInflatedPixels(bytes, idat, header!);
        // WIC never receives compressed text/profile metadata, preventing metadata decompression bombs.
        if (keptLength == bytes.Length) return new(header!, bytes);
        var clean = new byte[keptLength]; png[..8].CopyTo(clean); var destination = 8;
        foreach (var chunk in kept)
        { png.Slice(chunk.Offset, chunk.Length).CopyTo(clean.AsSpan(destination)); destination += chunk.Length; }
        return new(header!, clean);
    }

    private static void ValidateInflatedPixels(byte[] bytes, IReadOnlyList<PngChunk> idat, PngHeader header)
    {
        using var compressed = new IdatStream(bytes, idat);
        using var inflated = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[checked((header.Width * header.Channels * header.BitDepth + 7) / 8)];
        try
        {
            if (!header.Interlaced) ReadRows(header.Width, header.Height);
            else
            {
                ReadOnlySpan<int> startsX = [0, 4, 0, 2, 0, 1, 0], startsY = [0, 0, 4, 0, 2, 0, 1];
                ReadOnlySpan<int> stepsX = [8, 8, 4, 4, 2, 2, 1], stepsY = [8, 8, 8, 4, 4, 2, 2];
                for (var pass = 0; pass < 7; pass++)
                {
                    var width = Math.Max(0, (header.Width - startsX[pass] + stepsX[pass] - 1) / stepsX[pass]);
                    var height = Math.Max(0, (header.Height - startsY[pass] + stepsY[pass] - 1) / stepsY[pass]);
                    if (width != 0 && height != 0) ReadRows(width, height);
                }
            }
            if (inflated.ReadByte() != -1) throw new InvalidDataException("PNG IDAT 展开数据长度超过尺寸头预算。");
        }
        catch (Exception error) when (error is EndOfStreamException or InvalidDataException)
        { throw new InvalidDataException("PNG IDAT 展开数据长度或过滤数据无效。", error); }

        void ReadRows(int width, int height)
        {
            var rowBytes = checked((width * header.Channels * header.BitDepth + 7) / 8);
            for (var y = 0; y < height; y++)
            {
                var filter = inflated.ReadByte();
                if (filter is < 0 or > 4) throw new InvalidDataException("PNG IDAT 过滤数据无效。");
                inflated.ReadExactly(row.AsSpan(0, rowBytes));
            }
        }
    }

    private sealed class IdatStream(byte[] bytes, IReadOnlyList<PngChunk> chunks) : Stream
    {
        private int chunkIndex, chunkPosition;
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => chunks.Sum(chunk => (long)chunk.Length);
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = 0;
            while (read < buffer.Length && chunkIndex < chunks.Count)
            {
                var chunk = chunks[chunkIndex];
                var count = Math.Min(buffer.Length - read, chunk.Length - chunkPosition);
                bytes.AsSpan(chunk.Offset + chunkPosition, count).CopyTo(buffer[read..]);
                read += count; chunkPosition += count;
                if (chunkPosition == chunk.Length) { chunkIndex++; chunkPosition = 0; }
            }
            position += read; return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc ^ uint.MaxValue;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            table[index] = crc;
        }
        return table;
    }

    private static bool IsDecodeFault(Exception error) => error is FileFormatException or NotSupportedException or
        ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException;
}
