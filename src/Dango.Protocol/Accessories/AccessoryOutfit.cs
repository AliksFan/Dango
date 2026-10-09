using System.Text.Json.Serialization;

namespace Dango.Protocol.Accessories;

public enum AccessoryOutfitLayer { Behind, Front }

/// <summary>One equipped instance; asset identity is independent of instance identity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessoryOutfitItem(string Id, string Name, string AssetId, int PixelWidth, int PixelHeight)
{
    public AccessoryOutfitLayer Layer { get; init; } = AccessoryOutfitLayer.Front;
    public int Order { get; init; }
    public double SizePercent { get; init; } = 100;
    public double BaseAngleDegrees { get; init; }
    public double ImagePivotX { get; init; } = .5;
    public double ImagePivotY { get; init; } = .5;
    public double BodyAnchorX { get; init; } = .5;
    public double BodyAnchorY { get; init; }
    public double Softness { get; init; }
    public bool AllowFolding { get; init; }
    public double MaxDeformationPercent { get; init; } = 100;
    public double ConnectionStrength { get; init; } = 100;
    public double MaxConnectionTravelPercent { get; init; } = 75;
    public double RotationDamping { get; init; } = 60;
    public bool LockRotation { get; init; } = true;
    public double ReturnStrength { get; init; } = 60;
    public double MaxSwingDegrees { get; init; } = 45;
    public bool LimitSwing { get; init; } = true;
    public double GravityInfluence { get; init; }

    public static bool IsBuiltInAsset(string? id) => id is "builtin-sprout" or "builtin-top-hat" or "builtin-bowler";
    public static bool IsContentHash(string? id) => id is { Length: 64 } &&
        id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 ||
            Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException("Invalid accessory instance ID.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || Name.Any(char.IsControl))
            throw new InvalidDataException("Invalid accessory name.");
        if (!IsBuiltInAsset(AssetId) && !IsContentHash(AssetId))
            throw new InvalidDataException("Invalid accessory asset ID.");
        ValidateDimensions(PixelWidth, PixelHeight);
        if (IsBuiltInAsset(AssetId) && (PixelWidth != 64 || PixelHeight != 64))
            throw new InvalidDataException("Built-in accessory logical canvas must be 64 x 64.");
        if (!Enum.IsDefined(Layer) || Order < 0) throw new InvalidDataException("Invalid layer or order.");
        Range(SizePercent, 10, 400); Range(BaseAngleDegrees, -180, 180);
        Range(ImagePivotX, 0, 1); Range(ImagePivotY, 0, 1);
        Range(BodyAnchorX, 0, 1); Range(BodyAnchorY, 0, 1);
        Range(Softness, 0, 100); Range(ConnectionStrength, 0, 100);
        Range(MaxDeformationPercent, 0, 200); Range(MaxConnectionTravelPercent, 0, 200);
        Range(RotationDamping, 0, 100); Range(ReturnStrength, 0, 100);
        Range(MaxSwingDegrees, 0, 180); Range(GravityInfluence, 0, 100);
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || width > AccessoryOutfitManifest.MaximumImageEdge ||
            height > AccessoryOutfitManifest.MaximumImageEdge ||
            (long)width * height > AccessoryOutfitManifest.MaximumImagePixels)
            throw new InvalidDataException("Accessory image dimensions exceed the saved-image limits.");
    }

    private static void Range(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidDataException("Accessory parameter is out of range.");
    }
}

/// <summary>Only the committed equipped recipe; no library, paths or runtime state.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessoryOutfitManifest
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumEquippedItems = 8;
    public const int MaximumImageEdge = 4096;
    public const long MaximumImagePixels = 16_000_000;
    public const long MaximumImageBytes = 32L * 1024 * 1024;
    public static AccessoryOutfitManifest Empty { get; } = new(CurrentSchemaVersion, []);
    public int SchemaVersion { get; }
    public IReadOnlyList<AccessoryOutfitItem> Items { get; }

    [JsonConstructor]
    public AccessoryOutfitManifest(int schemaVersion, IReadOnlyList<AccessoryOutfitItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        SchemaVersion = schemaVersion;
        Items = Array.AsReadOnly(items.Take(MaximumEquippedItems + 1).ToArray());
        Validate();
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException("Unsupported accessory outfit schema.");
        if (Items.Count > MaximumEquippedItems) throw new InvalidDataException("At most eight accessories can be equipped.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var dimensions = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            if (item is null) throw new InvalidDataException("Null accessory instance.");
            item.Validate();
            if (!ids.Add(item.Id)) throw new InvalidDataException("Duplicate accessory instance ID.");
            if (dimensions.TryGetValue(item.AssetId, out var size) && size != (item.PixelWidth, item.PixelHeight))
                throw new InvalidDataException("One asset cannot have different dimensions.");
            dimensions[item.AssetId] = (item.PixelWidth, item.PixelHeight);
        }
    }
    public bool Equals(AccessoryOutfitManifest? other) => other is not null &&
        SchemaVersion == other.SchemaVersion && Items.SequenceEqual(other.Items);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        foreach (var item in Items) hash.Add(item);
        return hash.ToHashCode();
    }
}

/// <summary>Revision is assigned by the authenticated account store, never by the outfit.</summary>
public sealed record StoredAccessoryOutfit(long Revision, AccessoryOutfitManifest Outfit)
{
    public void Validate()
    {
        if (Revision < 0 || Outfit is null) throw new InvalidDataException("Invalid stored outfit.");
        Outfit.Validate();
    }
}

/// <summary>Account/pet identity comes from the authenticated endpoint, not this body.</summary>
public sealed record CommitAccessoryOutfitRequest(long ExpectedRevision, string IdempotencyKey, AccessoryOutfitManifest Outfit)
{
    public void Validate()
    {
        if (ExpectedRevision < 0 || IdempotencyKey is not { Length: 32 } ||
            !IdempotencyKey.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') || Outfit is null)
            throw new InvalidDataException("Invalid outfit commit request.");
        Outfit.Validate();
    }
}

/// <summary>The server may reencode PNG bytes; clients must explicitly map local IDs to server IDs.</summary>
public sealed record UploadedAccessoryAsset(string LocalAssetId, string AssetId, int PixelWidth, int PixelHeight, int NormalizationVersion)
{
    public const int CurrentNormalizationVersion = 1;
    public void Validate()
    {
        if (!AccessoryOutfitItem.IsContentHash(LocalAssetId) || !AccessoryOutfitItem.IsContentHash(AssetId) ||
            NormalizationVersion != CurrentNormalizationVersion)
            throw new InvalidDataException("Invalid or unsupported asset upload mapping.");
        AccessoryOutfitItem.ValidateDimensions(PixelWidth, PixelHeight);
    }
}
