namespace Dango.Server.Persistence.Entities;

// Physical content identity never grants a user access. Account references are separate rows.
public sealed class AccessoryBlob
{
    public string AssetId { get; set; } = string.Empty;
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public int NormalizationVersion { get; set; }
    public long ByteLength { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AccessoryAssetReference
{
    public Guid PetId { get; set; }
    public string AssetId { get; set; } = string.Empty;
    public DateTimeOffset LeaseUntil { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class AccessoryCurrentOutfit
{
    public Guid PetId { get; set; }
    public long Revision { get; set; }
    public string OutfitJson { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

// Receipt and current outfit are committed in one PostgreSQL transaction. A missing receipt
// proves nothing about an earlier request, including after future receipt retention changes.
public sealed class AccessoryCommitReceipt
{
    public Guid PetId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestDigest { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string OutfitJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
