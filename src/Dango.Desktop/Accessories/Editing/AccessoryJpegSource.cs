using System.Buffers.Binary;
using System.IO;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>Validates dimensions/framing before WIC, extracts only EXIF orientation and strips other metadata.</summary>
internal sealed record AccessoryJpegSource(byte[] Bytes, int Width, int Height, int Orientation)
{
    internal static AccessoryJpegSource Prepare(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) throw Invalid();
        using var clean = new MemoryStream(); clean.Write(bytes, 0, 2);
        var offset = 2; var width = 0; var height = 0; var orientation = 1;
        var sawExif = false; var sawScan = false; var markers = 0;
        while (offset < bytes.Length)
        {
            if (++markers > 4096 || bytes[offset++] != 255) throw Invalid();
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) throw Invalid();
            var marker = bytes[offset++];
            if (marker == 217)
            {
                if (offset != bytes.Length || width == 0 || !sawScan) throw Invalid();
                clean.WriteByte(255); clean.WriteByte(217);
                return new(clean.ToArray(), width, height, orientation);
            }
            if (marker is 0 or 216 or >= 208 and <= 215) throw Invalid();
            if (marker == 1) { clean.WriteByte(255); clean.WriteByte(marker); continue; }
            if (offset + 2 > bytes.Length) throw Invalid();
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) throw Invalid();
            var payload = bytes.AsSpan(offset + 2, length - 2);
            var retain = (marker < 224 || marker > 239) && marker != 254;
            if (marker == 225 && payload.StartsWith("Exif\0\0"u8))
            {
                if (sawExif) throw Invalid();
                sawExif = true; orientation = ReadOrientation(payload[6..]);
            }
            if (marker == 226 && payload.StartsWith("MPF\0"u8))
                throw new InvalidDataException("不支持多图片 JPEG／MPO。");
            // Adobe's compact color-transform marker is needed for CMYK/YCCK decoding, not user metadata.
            if (marker == 238 && payload.Length == 12 && payload.StartsWith("Adobe"u8)) retain = true;
            if (marker is 192 or 193 or 194)
            {
                if (width != 0 || payload.Length < 6 || payload[0] != 8 || payload[5] is not (1 or 3 or 4) ||
                    payload.Length != 6 + payload[5] * 3) throw Invalid();
                height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]); width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
                if (width < 1 || height < 1 || width > AccessoryImageAsset.MaximumSourceEdge || height > AccessoryImageAsset.MaximumSourceEdge ||
                    (long)width * height > AccessoryImageAsset.MaximumSourcePixels || (long)width * height * 4 > AccessoryImageAsset.MaximumSourceDecodedBytes)
                    throw new InvalidDataException("JPEG 超出源尺寸或 256 MiB 解码像素面上限。");
            }
            else if (marker is >= 195 and <= 207 && marker is not (196 or 200 or 204))
                throw new InvalidDataException("不支持此 JPEG 编码方式。");
            if (marker == 218 && (width == 0 || payload.Length < 4)) throw Invalid();
            if (retain) { clean.WriteByte(255); clean.WriteByte(marker); clean.Write(bytes, offset, length); }
            offset += length;
            if (marker != 218) continue;
            sawScan = true;
            var scanStart = offset;
            while (offset < bytes.Length)
            {
                if (bytes[offset] != 255) { offset++; continue; }
                if (offset + 1 >= bytes.Length) throw Invalid();
                var next = bytes[offset + 1];
                if (next == 0 || next is >= 208 and <= 215) { offset += 2; continue; }
                if (next == 255) { offset++; continue; }
                break;
            }
            clean.Write(bytes, scanStart, offset - scanStart);
        }
        throw Invalid();
    }

    internal static int ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) throw Invalid();
        var little = tiff[..2].SequenceEqual("II"u8);
        if (!little && !tiff[..2].SequenceEqual("MM"u8)) throw Invalid();
        if (U16(tiff, 2, little) != 42) throw Invalid();
        var offset = U32(tiff, 4, little);
        if (offset > int.MaxValue || offset < 8 || offset + 2L > tiff.Length) throw Invalid();
        var start = (int)offset; var count = U16(tiff, start, little);
        if (count > 2048 || start + 2L + count * 12L + 4 > tiff.Length) throw Invalid();
        int? result = null;
        for (var i = 0; i < count; i++)
        {
            var at = start + 2 + i * 12;
            if (U16(tiff, at, little) != 274) continue;
            if (result is not null || U16(tiff, at + 2, little) != 3 || U32(tiff, at + 4, little) != 1) throw Invalid();
            result = U16(tiff, at + 8, little);
            if (result is < 1 or > 8) throw Invalid();
        }
        return result ?? 1;
    }
    private static ushort U16(ReadOnlySpan<byte> bytes, int at, bool little) => little ?
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(at, 2)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));
    private static uint U32(ReadOnlySpan<byte> bytes, int at, bool little) => little ?
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
    private static InvalidDataException Invalid() => new("JPEG 结构、方向或单帧边界无效。");
}
