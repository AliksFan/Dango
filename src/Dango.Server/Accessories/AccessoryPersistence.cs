using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Protocol.Accessories;
using Dango.Server.Accounts;

namespace Dango.Server.Accessories;

internal static class AccessoryPersistence
{
    internal const int MaximumJsonBytes = 32 * 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
        Converters = { new CanonicalDoubleConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    private static readonly string[] ItemFields = ["id", "name", "assetId", "pixelWidth", "pixelHeight", "layer", "order",
        "sizePercent", "baseAngleDegrees", "imagePivotX", "imagePivotY", "bodyAnchorX", "bodyAnchorY", "softness", "allowFolding",
        "maxDeformationPercent", "connectionStrength", "maxConnectionTravelPercent", "rotationDamping", "lockRotation", "returnStrength",
        "maxSwingDegrees", "limitSwing", "gravityInfluence"];

    internal static CommitAccessoryOutfitRequest ParseCommit(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumJsonBytes) throw new AccountFault("accessory_request_invalid");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            Fields(document.RootElement, ["expectedRevision", "idempotencyKey", "outfit"]);
            RequireOutfit(document.RootElement.GetProperty("outfit"));
            var request = document.RootElement.Deserialize<CommitAccessoryOutfitRequest>(Json) ?? throw new InvalidDataException();
            request.Validate();
            return request;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { throw new AccountFault("accessory_request_invalid"); }
    }

    internal static AccessoryOutfitManifest ReadOutfit(string json, long revision)
    {
        if (revision < 1 || string.IsNullOrEmpty(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            throw InvalidState();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            RequireOutfit(document.RootElement);
            var outfit = document.RootElement.Deserialize<AccessoryOutfitManifest>(Json) ?? throw InvalidState();
            outfit.Validate();
            return outfit;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { throw InvalidState(); }
    }

    internal static string Serialize(AccessoryOutfitManifest outfit) => JsonSerializer.Serialize(outfit, Json);
    internal static string Digest(CommitAccessoryOutfitRequest request) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, Json)));
    internal static StoredAccessoryOutfit ReadReceipt(long revision, string key, string digest, string outfitJson)
    {
        if (key is not { Length: 32 } || key.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            || !AccessoryOutfitItem.IsContentHash(digest)) throw InvalidState();
        var outfit = ReadOutfit(outfitJson, revision);
        if (Digest(new(revision - 1, key, outfit)) != digest) throw InvalidState();
        return new(revision, outfit);
    }
    internal static void RequireKey(string key)
    {
        if (key is not { Length: 32 } || key.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new AccountFault("accessory_request_invalid");
    }
    private static void RequireOutfit(JsonElement value)
    {
        Fields(value, ["schemaVersion", "items"]);
        var items = value.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > AccessoryOutfitManifest.MaximumEquippedItems)
            throw new InvalidDataException();
        foreach (var item in items.EnumerateArray())
        {
            Fields(item, ItemFields);
            if (item.GetProperty("layer").ValueKind != JsonValueKind.String || item.GetProperty("layer").GetString() is not ("front" or "behind"))
                throw new InvalidDataException();
        }
    }
    private static void Fields(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!actual.Add(property.Name)) throw new InvalidDataException();
        if (actual.Count != expected.Length || expected.Any(name => !actual.Contains(name))) throw new InvalidDataException();
    }
    internal static AccountFault InvalidState() => new("accessory_invalid_state", 503);
    private sealed class CanonicalDoubleConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value)) throw new JsonException();
            return value == 0 ? 0 : value;
        }
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (!double.IsFinite(value)) throw new JsonException();
            // jsonb normalizes signed zero. Digest and stored recipe must make the same choice.
            writer.WriteNumberValue(value == 0 ? 0d : value);
        }
    }
}
