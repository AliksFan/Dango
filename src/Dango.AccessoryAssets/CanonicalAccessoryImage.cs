namespace Dango.AccessoryAssets;

/// <summary>Detached pixels re-encoded by the trusted normalizer; no client metadata or source path.</summary>
public sealed class CanonicalAccessoryImage
{
    private readonly byte[] bytes;
    internal CanonicalAccessoryImage(string digest, int width, int height, byte[] bytes)
    { Digest = digest; Width = width; Height = height; this.bytes = bytes; }
    public string Digest { get; }
    public int Width { get; }
    public int Height { get; }
    public int ByteLength => bytes.Length;
    public int NormalizationVersion { get; } = AccessoryPngNormalizer.CurrentNormalizationVersion;
    public byte[] CopyPngBytes() => (byte[])bytes.Clone();
}
