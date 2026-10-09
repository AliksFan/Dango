using System.Buffers.Binary;
using System.Security.Cryptography;
using Dango.Protocol.Accessories;

namespace Dango.Client.Sync.Accessories;

/// <summary>A defensive copy of stored PNG bytes. Full decoding is required at the image/store boundary.</summary>
public sealed class AccessoryImageSnapshot
{
    public const int MaximumByteCount = 32 * 1024 * 1024;
    private readonly byte[] _pngBytes;

    public AccessoryImageSnapshot(string assetId, int pixelWidth, int pixelHeight, ReadOnlyMemory<byte> pngBytes)
    {
        if (!IsHash(assetId) || pngBytes.Length is < 33 or > MaximumByteCount ||
            pixelWidth is < 1 or > 4096 || pixelHeight is < 1 or > 4096 || (long)pixelWidth * pixelHeight > 16_000_000)
            throw new InvalidDataException("invalid_accessory_image_snapshot");
        _pngBytes = pngBytes.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(_pngBytes)) != assetId ||
            !_pngBytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(_pngBytes.AsSpan(8, 4)) != 13 ||
            !_pngBytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(_pngBytes.AsSpan(16, 4)) != (uint)pixelWidth ||
            BinaryPrimitives.ReadUInt32BigEndian(_pngBytes.AsSpan(20, 4)) != (uint)pixelHeight)
            throw new InvalidDataException("accessory_image_reference_mismatch");
        AssetId = assetId;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public string AssetId { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    // A ReadOnlyMemory backed by our array would still expose it through MemoryMarshal.TryGetArray.
    public ReadOnlyMemory<byte> PngBytes => _pngBytes.ToArray();

    internal static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsBuiltIn(string value) => value is "builtin-sprout" or "builtin-top-hat" or "builtin-bowler";
}

/// <summary>Only the committed equipped outfit and exactly its required custom images.</summary>
public sealed class AccessoryLocalSnapshot
{
    public AccessoryLocalSnapshot(AccessoryOutfitManifest outfit, IReadOnlyList<AccessoryImageSnapshot> images,
        long localCommitVersion)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        ArgumentNullException.ThrowIfNull(images);
        outfit.Validate();
        if (localCommitVersion < 0) throw new ArgumentOutOfRangeException(nameof(localCommitVersion));
        if (images.Count > AccessoryOutfitManifest.MaximumEquippedItems)
            throw new InvalidDataException("accessory_snapshot_image_set_mismatch");
        var uniqueImages = new Dictionary<string, AccessoryImageSnapshot>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            if (image is null || !uniqueImages.TryAdd(image.AssetId, image))
                throw new InvalidDataException("duplicate_accessory_image_snapshot");
        }
        var required = outfit.Items.Where(item => !AccessoryImageSnapshot.IsBuiltIn(item.AssetId))
            .GroupBy(item => item.AssetId, StringComparer.Ordinal).ToArray();
        if (uniqueImages.Count != required.Length)
            throw new InvalidDataException("accessory_snapshot_image_set_mismatch");
        foreach (var group in required)
        {
            if (!uniqueImages.TryGetValue(group.Key, out var image) ||
                group.Any(item => item.PixelWidth != image.PixelWidth || item.PixelHeight != image.PixelHeight))
                throw new InvalidDataException("accessory_snapshot_image_set_mismatch");
        }
        Outfit = outfit;
        Images = Array.AsReadOnly(images.ToArray());
        LocalCommitVersion = localCommitVersion;
    }

    public AccessoryOutfitManifest Outfit { get; }
    public IReadOnlyList<AccessoryImageSnapshot> Images { get; }
    public long LocalCommitVersion { get; }
}

public sealed record AccessoryRestoreGuard(long LocalCommitVersion, bool HasDirtyDraft);

/// <summary>A temporary, fully verified restore. Disposing must leave the active local outfit intact.</summary>
public interface IAccessoryRestoreStage : IAsyncDisposable;

public interface IAccessoryLocalRepository
{
    Task<AccessoryRestoreGuard> CaptureRestoreGuardAsync(CancellationToken cancellationToken);

    /// <summary>Decode and validate every image and stage all writes without changing the active outfit.</summary>
    Task<IAccessoryRestoreStage> StageRestoreAsync(AccessoryLocalSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// Call authorization.TryCommit around the atomic commit, and inside it recheck local version and
    /// dirty draft. A false result must preserve the old active outfit and local library.
    /// </summary>
    Task<bool> TryCommitRestoreAsync(IAccessoryRestoreStage stage, AccessoryRestoreGuard guard,
        AccessoryRestoreAuthorization authorization, CancellationToken cancellationToken);
}

/// <summary>Serializes the final atomic local write with account detachment; no credentials or IDs.</summary>
public sealed class AccessoryRestoreAuthorization
{
    private readonly AccessorySessionFence _fence;
    private readonly CancellationToken _cancellationToken;

    internal AccessoryRestoreAuthorization(AccessorySessionFence fence, CancellationToken cancellationToken)
    {
        _fence = fence;
        _cancellationToken = cancellationToken;
    }

    public bool TryCommit(Func<bool> atomicCommit)
    {
        ArgumentNullException.ThrowIfNull(atomicCommit);
        lock (_fence.Gate)
        {
            if (!_fence.IsActive || _cancellationToken.IsCancellationRequested) return false;
            return atomicCommit();
        }
    }
}

internal sealed class AccessorySessionFence
{
    internal object Gate { get; } = new();
    internal bool IsActive { get; private set; } = true;

    internal void Invalidate()
    {
        lock (Gate) IsActive = false;
    }
}
