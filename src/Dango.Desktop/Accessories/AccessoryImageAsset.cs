using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dango.Desktop.Accessories;

/// <summary>Detached, metadata-free immutable PNG pixels. No source path is retained.</summary>
public sealed class AccessoryImageAsset
{
    public const long MaximumFileBytes = 16L * 1024 * 1024;
    public const int MaximumEdge = 2048;
    public const long MaximumPixels = 4_194_304;
    private readonly byte[] pngBytes;

    private AccessoryImageAsset(string assetId, BitmapSource bitmap, byte[] pngBytes)
    {
        AssetId = assetId;
        Bitmap = bitmap;
        this.pngBytes = pngBytes;
    }

    public string AssetId { get; }
    public int PixelWidth => Bitmap.PixelWidth;
    public int PixelHeight => Bitmap.PixelHeight;
    public BitmapSource Bitmap { get; }
    public long EncodedByteLength => pngBytes.LongLength;
    internal byte[] CopyPngBytes() => (byte[])pngBytes.Clone();

    public static AccessoryImageAsset ImportPng(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("首版仅支持静态透明 PNG 图片。");
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = ReadLimited(stream);
        }
        catch (InvalidDataException) { throw; }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("无法读取选定图片。"); }
        catch (IOException) { throw new IOException("无法读取选定图片。"); }
        var bitmap = DecodePixels(bytes);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        var normalized = output.ToArray();
        if (normalized.LongLength > MaximumFileBytes) throw new InvalidDataException("规范化图片超出 16 MiB 上限。");
        return new(Convert.ToHexStringLower(SHA256.HashData(normalized)), bitmap, normalized);
    }

    internal static AccessoryImageAsset ReadStored(string path, string expectedAssetId)
    {
        if (!AccessoryItem.IsValidAssetId(expectedAssetId) || AccessoryItem.IsBuiltInAsset(expectedAssetId))
            throw new InvalidDataException("内部图片 ID 无效。");
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("内部图片不能是目录或链接。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = ReadLimited(stream);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedAssetId)
            throw new InvalidDataException("内部图片校验不一致。");
        return new(expectedAssetId, DecodePixels(bytes), bytes);
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaximumEdge || height > MaximumEdge || (long)width * height > MaximumPixels)
            throw new InvalidDataException("图片尺寸必须在 2048 像素及 4,194,304 总像素上限内。");
    }

    private static byte[] ReadLimited(Stream stream)
    {
        if (stream.Length < 1 || stream.Length > MaximumFileBytes)
            throw new InvalidDataException("PNG 文件必须大于 0 且不超过 16 MiB。");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static BitmapSource DecodePixels(byte[] png)
    {
        ValidatePngStructure(png);
        try
        {
            using var input = new MemoryStream(png, writable: false);
            var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) throw new InvalidDataException("不支持多帧 PNG。");
            var frame = decoder.Frames[0];
            ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = checked(frame.PixelWidth * 4);
            var pixels = new byte[checked(stride * frame.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            var visible = false; var transparent = false;
            for (var index = 3; index < pixels.Length; index += 4)
            {
                visible |= pixels[index] != 0;
                transparent |= pixels[index] != 255;
            }
            if (!visible || !transparent)
                throw new InvalidDataException("图片须包含透明或半透明像素，并至少包含一个可见像素。");
            // Rebuild from pixels: original metadata, palette, DPI and file lifetime cannot leak through.
            var bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception error) when (error is FileFormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("PNG 图片解码失败。"); }
    }

    private static void ValidatePngStructure(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 33 || !png[..8].SequenceEqual(signature)) throw new InvalidDataException("PNG 文件头无效。");
        var offset = 8; var header = false; var imageData = false; var end = false;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12) throw new InvalidDataException("PNG 区块不完整。");
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            if ((long)offset + length + 12 > png.Length) throw new InvalidDataException("PNG 区块长度无效。");
            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                throw new InvalidDataException("首版不支持动画 PNG。");
            if (!header)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) throw new InvalidDataException("PNG 尺寸头无效。");
                var width = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 12, 4));
                if (width > int.MaxValue || height > int.MaxValue) throw new InvalidDataException("PNG 尺寸无效。");
                ValidateDimensions((int)width, (int)height);
                header = true;
            }
            else if (type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG 存在重复尺寸头。");
            if (type.SequenceEqual("IDAT"u8)) imageData = true;
            offset += checked((int)length + 12);
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || offset != png.Length) throw new InvalidDataException("PNG 结束区块无效。");
                end = true;
                break;
            }
        }
        if (!imageData || !end) throw new InvalidDataException("PNG 图片内容不完整。");
    }
}
