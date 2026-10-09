using System.Security.Cryptography;
using StbImageSharp;

namespace Dango.AccessoryAssets;

public static class AccessoryPngNormalizer
{
    public const int CurrentNormalizationVersion = 1;
    public const int MaximumFileBytes = 32 * 1024 * 1024;
    public const int MaximumEdge = 4096;
    public const long MaximumPixels = 16_000_000;
    public static CanonicalAccessoryImage Normalize(ReadOnlySpan<byte> source)
    {
        if (source.Length is < 1 or > MaximumFileBytes) throw new InvalidDataException("PNG must be nonempty and no larger than 32 MiB.");
        var prepared = PngPreflight.Prepare(source.ToArray());
        ImageResult image;
        try { image = ImageResult.FromMemory(prepared.Bytes, ColorComponents.RedGreenBlueAlpha); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        { throw new InvalidDataException("PNG pixel decoding failed.", error); }
        if (image.Width != prepared.Width || image.Height != prepared.Height || image.Data.Length != checked(image.Width * image.Height * 4))
            throw new InvalidDataException("Decoded dimensions do not match the verified header.");
        var visible = false; var transparent = false;
        for (var index = 3; index < image.Data.Length; index += 4)
        {
            var alpha = image.Data[index]; visible |= alpha != 0; transparent |= alpha != 255;
            // Hidden RGB contributes neither visible pixels nor asset identity.
            if (alpha == 0) image.Data.AsSpan(index - 3, 3).Clear();
        }
        if (!visible || !transparent) throw new InvalidDataException("PNG must contain both visible and transparent or translucent pixels.");
        using var output = new BoundedPngStream();
        new StbImageWriteSharp.ImageWriter().WritePng(image.Data, image.Width, image.Height,
            StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        if (output.LimitExceeded || output.Length == 0) throw new InvalidDataException("Canonical PNG exceeds the 32 MiB limit or encoding failed.");
        var bytes = output.ToArray();
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), image.Width, image.Height, bytes);
    }

    private sealed class BoundedPngStream : MemoryStream
    {
        public bool LimitExceeded { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        { if (CheckLength(count)) base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (CheckLength(buffer.Length)) base.Write(buffer); }
        public override void WriteByte(byte value)
        { if (CheckLength(1)) base.WriteByte(value); }
        private bool CheckLength(int length)
        {
            // The codec frees its unmanaged PNG buffer after its callback returns. Do not throw from
            // that callback for an ordinary size rejection; discard, let it free, then reject above.
            if (LimitExceeded || Position + length > MaximumFileBytes) { LimitExceeded = true; return false; }
            return true;
        }
    }
}
