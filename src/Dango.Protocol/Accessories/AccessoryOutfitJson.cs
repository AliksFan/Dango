using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dango.Protocol.Accessories;

/// <summary>Bounded, complete wire recipes; rejects duplicate fields instead of accepting last-write-wins JSON.</summary>
public static class AccessoryOutfitJson
{
    public const int MaximumManifestBytes = 32 * 1024;
    private static readonly string[] ItemFields =
    [
        "id", "name", "assetId", "pixelWidth", "pixelHeight", "layer", "order", "sizePercent",
        "baseAngleDegrees", "imagePivotX", "imagePivotY", "bodyAnchorX", "bodyAnchorY", "softness",
        "allowFolding", "maxDeformationPercent", "connectionStrength", "maxConnectionTravelPercent",
        "rotationDamping", "lockRotation", "returnStrength", "maxSwingDegrees", "limitSwing", "gravityInfluence",
    ];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<AccessoryOutfitLayer>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string Serialize(AccessoryOutfitManifest outfit)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        outfit.Validate();
        var json = JsonSerializer.Serialize(outfit, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumManifestBytes)
            throw new InvalidDataException("Accessory manifest exceeds the wire size limit.");
        return json;
    }

    public static AccessoryOutfitManifest Deserialize(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length is < 1 or > MaximumManifestBytes)
            throw new InvalidDataException("Accessory manifest exceeds the wire size limit.");
        using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        ExactFields(root, ["schemaVersion", "items"]);
        var items = root.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > AccessoryOutfitManifest.MaximumEquippedItems)
            throw new InvalidDataException("Invalid accessory wire item list.");
        foreach (var item in items.EnumerateArray()) ExactFields(item, ItemFields);
        var outfit = root.Deserialize<AccessoryOutfitManifest>(Options) ?? throw new InvalidDataException("Missing accessory outfit.");
        outfit.Validate();
        return outfit;
    }

    private static void ExactFields(JsonElement value, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Accessory wire data must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!names.Add(field.Name) || !required.Contains(field.Name, StringComparer.Ordinal))
                throw new InvalidDataException("Duplicate or unknown accessory wire field.");
        if (names.Count != required.Length) throw new InvalidDataException("Incomplete accessory wire recipe.");
    }
}
