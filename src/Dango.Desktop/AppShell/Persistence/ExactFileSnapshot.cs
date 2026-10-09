namespace Dango.Desktop.AppShell.Persistence;

/// <summary>Immutable raw bytes, keeping a missing file distinct from an existing empty file.</summary>
public sealed class ExactFileSnapshot
{
    private readonly byte[] bytes;

    private ExactFileSnapshot(bool exists, byte[] bytes)
    {
        Exists = exists;
        this.bytes = bytes;
    }

    public bool Exists { get; }

    // ReadOnlyMemory alone does not protect an array exposed through MemoryMarshal.TryGetArray.
    public ReadOnlyMemory<byte> Bytes => bytes.ToArray();

    public static ExactFileSnapshot Missing { get; } = new(false, []);

    public static ExactFileSnapshot FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new ExactFileSnapshot(true, bytes.ToArray());
    }

    internal bool Matches(ExactFileSnapshot other) =>
        Exists == other.Exists && bytes.AsSpan().SequenceEqual(other.bytes);
}
