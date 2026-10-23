using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>Metadata-free bounded working PNG, including opaque/blank creative drafts. No source path is retained.</summary>
public sealed class AccessoryEditorSourceAsset
{
    private readonly byte[] png;
    internal AccessoryEditorSourceAsset(BitmapSource bitmap, byte[] png, AccessoryImageImportInfo? info)
    { Bitmap = bitmap; this.png = (byte[])png.Clone(); AssetId = Convert.ToHexStringLower(SHA256.HashData(png)); ImportInfo = info; }
    public string AssetId { get; }
    public BitmapSource Bitmap { get; }
    public int PixelWidth => Bitmap.PixelWidth;
    public int PixelHeight => Bitmap.PixelHeight;
    public long EncodedByteLength => png.LongLength;
    public AccessoryImageImportInfo? ImportInfo { get; }
    public byte[] CopyPngBytes() => (byte[])png.Clone();

    public static AccessoryEditorSourceAsset FromPng(byte[] png)
    {
        try { return AccessoryImageAsset.ReadEditorPng(png, importing: false); }
        catch (OutOfMemoryException) { throw ResourceFailure(); }
    }
    public static AccessoryEditorSourceAsset FromBitmap(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        try { return AccessoryImageAsset.NormalizeEditorFrame(bitmap, 0, allowResize: false); }
        catch (OutOfMemoryException) { throw ResourceFailure(); }
    }

    public static AccessoryEditorSourceAsset Import(string path)
    {
        try { return ImportCore(path); }
        catch (OutOfMemoryException) { throw ResourceFailure(); }
    }

    private static AccessoryEditorSourceAsset ImportCore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 1 or > AccessoryImageAsset.MaximumSourceFileBytes)
                throw new InvalidDataException("源图片须大于 0 且不超过 64 MiB。");
            bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
        }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("无法读取选定图片。"); }
        catch (IOException) { throw new IOException("无法读取选定图片。"); }
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return AccessoryImageAsset.ReadEditorPng(bytes, importing: true);
        if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 216) return ImportJpeg(bytes);
        throw new InvalidDataException("仅支持按真实文件头识别的静态 PNG 或 JPEG。");
    }

    private static AccessoryEditorSourceAsset ImportJpeg(byte[] bytes)
    {
        var source = AccessoryJpegSource.Prepare(bytes);
        try
        {
            using var input = new MemoryStream(source.Bytes, writable: false);
            var decoder = new JpegBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnDemand);
            if (decoder.Frames.Count != 1) throw new InvalidDataException("不支持多帧 JPEG。");
            BitmapSource frame = decoder.Frames[0];
            if (frame.PixelWidth != source.Width || frame.PixelHeight != source.Height)
                throw new InvalidDataException("JPEG 解码尺寸与文件头不一致。");
            frame = ApplyOrientation(frame, source.Orientation);
            return AccessoryImageAsset.NormalizeEditorFrame(frame, bytes.LongLength, allowResize: true);
        }
        catch (Exception error) when (error is FileFormatException or NotSupportedException or ArgumentException or
            InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { throw new InvalidDataException("JPEG 解码或方向转换失败。", error); }
    }

    internal static BitmapSource ApplyOrientation(BitmapSource frame, int orientation)
    {
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        var matrix = orientation switch
            {
                2 => new Matrix(-1, 0, 0, 1, width, 0),
                3 => new Matrix(-1, 0, 0, -1, width, height),
                4 => new Matrix(1, 0, 0, -1, 0, height),
                5 => new Matrix(0, 1, 1, 0, 0, 0),
                6 => new Matrix(0, 1, -1, 0, height, 0),
                7 => new Matrix(0, -1, -1, 0, height, width),
                8 => new Matrix(0, -1, 1, 0, 0, width),
                _ => Matrix.Identity,
            };
        return matrix.IsIdentity ? frame : new TransformedBitmap(frame, new MatrixTransform(matrix));
    }

    private static InvalidDataException ResourceFailure() => new("图片处理内存不足，当前草稿未更改；请关闭其他编辑窗口或使用较小素材后重试。");
}
