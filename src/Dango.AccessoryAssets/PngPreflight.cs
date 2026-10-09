using System.Buffers.Binary;
using System.IO.Compression;

namespace Dango.AccessoryAssets;

/// <summary>Structural/resource boundary before the external decoder. Metadata is never decompressed.</summary>
internal static class PngPreflight
{
    private const int MaximumChunks = 4096;
    private static readonly uint[] CrcTable = CreateCrcTable();
    private sealed record Header(int Width, int Height, int BitDepth, int Channels, int ColorType, bool Interlaced);
    internal sealed record Prepared(int Width, int Height, byte[] Bytes);
    private readonly record struct Chunk(int Offset, int Length);

    internal static Prepared Prepare(byte[] bytes)
    {
        ReadOnlySpan<byte> png = bytes;
        if (png.Length < 33 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Invalid PNG signature.");
        var offset = 8; Header? header = null; var imageData = false; var end = false;
        var kept = new List<Chunk>(); var idat = new List<Chunk>(); var keptLength = 8;
        var count = 0; var dataEnded = false; var paletteEntries = 0; var transparencySeen = false;
        while (offset < png.Length)
        {
            if (++count > MaximumChunks || png.Length - offset < 12) throw new InvalidDataException("PNG chunk count or framing is invalid.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            if ((long)offset + length + 12 > png.Length) throw new InvalidDataException("PNG chunk length is invalid.");
            var type = png.Slice(offset + 4, 4);
            foreach (var character in type)
                if (character is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                    throw new InvalidDataException("PNG chunk type is invalid.");
            if ((type[2] & 32) != 0) throw new InvalidDataException("PNG chunk reserved bit is invalid.");
            var payloadLength = (int)length; var chunkLength = payloadLength + 12;
            if (ComputeCrc(png.Slice(offset + 4, payloadLength + 4)) != BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8 + payloadLength, 4)))
                throw new InvalidDataException("PNG chunk CRC failed.");
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                throw new InvalidDataException("Animated PNG is not supported.");
            if (header is null)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) throw new InvalidDataException("PNG must begin with one IHDR.");
                var width = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 12, 4));
                var depth = png[offset + 16]; var color = png[offset + 17];
                var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                var validDepth = color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => false };
                if (!validDepth || png[offset + 18] != 0 || png[offset + 19] != 0 || png[offset + 20] > 1)
                    throw new InvalidDataException("Invalid PNG pixel format or compression.");
                if (width < 1 || height < 1 || width > AccessoryPngNormalizer.MaximumEdge || height > AccessoryPngNormalizer.MaximumEdge ||
                    (long)width * height > AccessoryPngNormalizer.MaximumPixels)
                    throw new InvalidDataException("PNG dimensions exceed the 4096 edge or 16,000,000 pixel limit.");
                header = new((int)width, (int)height, depth, channels, color, png[offset + 20] == 1);
            }
            else if (type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("Duplicate PNG IHDR.");
            if (type.SequenceEqual("PLTE"u8))
            {
                if (imageData || transparencySeen || paletteEntries != 0 || header.ColorType is 0 or 4 || payloadLength is < 3 or > 768 ||
                    payloadLength % 3 != 0 || header.ColorType == 3 && payloadLength / 3 > 1 << header.BitDepth)
                    throw new InvalidDataException("Invalid PNG palette.");
                paletteEntries = payloadLength / 3;
            }
            if (type.SequenceEqual("tRNS"u8))
            {
                var validLength = header.ColorType switch { 0 => payloadLength == 2, 2 => payloadLength == 6, 3 => paletteEntries != 0 && payloadLength >= 1 && payloadLength <= paletteEntries, _ => false };
                if (imageData || transparencySeen || !validLength) throw new InvalidDataException("Invalid PNG transparency.");
                if (header.ColorType is 0 or 2)
                    for (var sample = 0; sample < payloadLength; sample += 2)
                        if (BinaryPrimitives.ReadUInt16BigEndian(png.Slice(offset + 8 + sample, 2)) >= 1 << header.BitDepth)
                            throw new InvalidDataException("PNG transparency exceeds bit depth.");
                transparencySeen = true;
            }
            var isData = type.SequenceEqual("IDAT"u8);
            if (isData)
            {
                if (dataEnded || header.ColorType == 3 && paletteEntries == 0) throw new InvalidDataException("PNG IDAT order or palette is invalid.");
                imageData = true; idat.Add(new(offset + 8, payloadLength));
            }
            else if (imageData) dataEnded = true;
            var keep = type.SequenceEqual("IHDR"u8) || type.SequenceEqual("PLTE"u8) || type.SequenceEqual("tRNS"u8) || isData || type.SequenceEqual("IEND"u8);
            if (!keep && (type[0] & 32) == 0) throw new InvalidDataException("Unknown critical PNG chunk.");
            if (keep) { kept.Add(new(offset, chunkLength)); keptLength += chunkLength; }
            offset += chunkLength;
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || offset != png.Length) throw new InvalidDataException("Invalid PNG end.");
                end = true; break;
            }
        }
        if (!imageData || !end) throw new InvalidDataException("Incomplete PNG data.");
        ValidateInflatedPixels(bytes, idat, header!);
        var clean = new byte[keptLength]; png[..8].CopyTo(clean); var destination = 8;
        foreach (var chunk in kept)
        { png.Slice(chunk.Offset, chunk.Length).CopyTo(clean.AsSpan(destination)); destination += chunk.Length; }
        return new(header!.Width, header.Height, clean);
    }

    private static void ValidateInflatedPixels(byte[] bytes, IReadOnlyList<Chunk> chunks, Header header)
    {
        using var compressed = new IdatStream(bytes, chunks);
        using var inflated = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[checked((header.Width * header.Channels * header.BitDepth + 7) / 8)];
        try
        {
            if (!header.Interlaced) ReadRows(header.Width, header.Height);
            else
            {
                ReadOnlySpan<int> startsX = [0, 4, 0, 2, 0, 1, 0], startsY = [0, 0, 4, 0, 2, 0, 1], stepsX = [8, 8, 4, 4, 2, 2, 1], stepsY = [8, 8, 8, 4, 4, 2, 2];
                for (var pass = 0; pass < 7; pass++)
                {
                    var width = Math.Max(0, (header.Width - startsX[pass] + stepsX[pass] - 1) / stepsX[pass]);
                    var height = Math.Max(0, (header.Height - startsY[pass] + stepsY[pass] - 1) / stepsY[pass]);
                    if (width != 0 && height != 0) ReadRows(width, height);
                }
            }
            if (inflated.ReadByte() != -1) throw new InvalidDataException("PNG inflated bytes exceed its dimension budget.");
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Incomplete PNG inflated pixels.", error); }
        void ReadRows(int width, int height)
        {
            var rowBytes = checked((width * header.Channels * header.BitDepth + 7) / 8);
            for (var y = 0; y < height; y++)
            {
                var filter = inflated.ReadByte();
                if (filter is < 0 or > 4) throw new InvalidDataException("Invalid PNG scanline filter.");
                inflated.ReadExactly(row.AsSpan(0, rowBytes));
            }
        }
    }

    private sealed class IdatStream(byte[] bytes, IReadOnlyList<Chunk> chunks) : Stream
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
                var chunk = chunks[chunkIndex]; var count = Math.Min(buffer.Length - read, chunk.Length - chunkPosition);
                bytes.AsSpan(chunk.Offset + chunkPosition, count).CopyTo(buffer[read..]); read += count; chunkPosition += count;
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
        var crc = uint.MaxValue; foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8); return ~crc;
    }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        { var crc = index; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1; table[index] = crc; }
        return table;
    }
}
