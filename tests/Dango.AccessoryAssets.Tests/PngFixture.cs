using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Dango.AccessoryAssets.Tests;

// A deliberately separate PNG fixture encoder: fixed scanlines using .NET zlib rather than the production codec.
internal static class PngFixture
{
    internal static readonly byte[] Pixels = [255, 0, 0, 255, 0, 80, 120, 0, 10, 20, 30, 100, 0, 0, 0, 0];
    internal static byte[] Rgba(byte[]? pixels = null, int width = 2, int height = 2, bool metadata = false,
        CompressionLevel compression = CompressionLevel.Optimal, bool interlaced = false)
    {
        pixels ??= Pixels;
        using var scanlines = new MemoryStream();
        if (!interlaced)
            for (var y = 0; y < height; y++)
            { scanlines.WriteByte(0); scanlines.Write(pixels.AsSpan(y * width * 4, width * 4)); }
        else
        {
            int[] startsX = [0, 4, 0, 2, 0, 1, 0], startsY = [0, 0, 4, 0, 2, 0, 1],
                stepsX = [8, 8, 4, 4, 2, 2, 1], stepsY = [8, 8, 8, 4, 4, 2, 2];
            for (var pass = 0; pass < 7; pass++)
                for (var y = startsY[pass]; y < height && startsX[pass] < width; y += stepsY[pass])
                { scanlines.WriteByte(0); for (var x = startsX[pass]; x < width; x += stepsX[pass]) scanlines.Write(pixels.AsSpan((y * width + x) * 4, 4)); }
        }
        return Assemble(Header(width, height, 8, 6, interlaced), Compress(scanlines.ToArray(), compression),
            metadata ? [Chunk("tEXt", "owner\0fictional-user"u8)] : []);
    }

    internal static byte[] Header(int width, int height, byte depth = 8, byte color = 6, bool interlaced = false)
    {
        var result = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(result, width);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), height);
        result[8] = depth; result[9] = color; result[12] = interlaced ? (byte)1 : (byte)0; return result;
    }

    internal static byte[] Assemble(byte[] header, byte[] idat, byte[][]? beforeData = null, byte[][]? afterData = null)
    {
        using var png = new MemoryStream(); png.Write([137, 80, 78, 71, 13, 10, 26, 10]); png.Write(Chunk("IHDR", header));
        foreach (var chunk in beforeData ?? []) png.Write(chunk);
        png.Write(Chunk("IDAT", idat)); foreach (var chunk in afterData ?? []) png.Write(chunk);
        png.Write(Chunk("IEND", [])); return png.ToArray();
    }

    internal static byte[] Compress(byte[] bytes, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream(); using (var zlib = new ZLibStream(output, level, leaveOpen: true)) zlib.Write(bytes); return output.ToArray();
    }

    internal static byte[] Chunk(string type, ReadOnlySpan<byte> data)
    {
        var chunk = new byte[data.Length + 12]; BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4); data.CopyTo(chunk.AsSpan(8));
        uint crc = uint.MaxValue;
        foreach (var value in chunk.AsSpan(4, data.Length + 4))
        { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length + 8), ~crc); return chunk;
    }
}
