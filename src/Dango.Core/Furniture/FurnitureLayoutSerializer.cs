using System.Buffers;
using System.Text.Json;

namespace Dango.Core.Furniture;

public static class FurnitureLayoutSerializer
{
    public const int MaximumBytes = 64 * 1024;
    public const int SchemaVersion = 1;
    public const int OrdinaryRoomSchemaVersion = 2;
    public const string SceneId = "local-single-window";
    public const string CoordinateSpace = "static-platform-p-v1";
    public const int SceneGeometryRevision = 1;

    public static byte[] Serialize(FurnitureLayout layout, FurnitureLayoutContract? contract = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        contract ??= FurnitureLayoutContract.Trial;
        layout.Validate(contract);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", ReferenceEquals(contract, FurnitureLayoutContract.OrdinaryRoom) ? OrdinaryRoomSchemaVersion : SchemaVersion);
            writer.WriteString("sceneId", contract.SceneId);
            writer.WriteString("coordinateSpace", contract.CoordinateSpace);
            writer.WriteNumber("sceneGeometryRevision", contract.SceneGeometryRevision);
            writer.WriteNumber("layoutVersion", layout.LayoutVersion);
            if (layout.LastOperationId is { } operation) writer.WriteString("lastOperationId", operation);
            else writer.WriteNull("lastOperationId");
            writer.WriteStartArray("items");
            foreach (var item in layout.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("instanceId", item.InstanceId);
                writer.WriteString("presetId", item.PresetId);
                writer.WriteNumber("presetRevision", item.PresetRevision);
                writer.WriteStartObject("anchor");
                writer.WriteNumber("x", item.Anchor.X);
                writer.WriteNumber("y", item.Anchor.Y);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        if (buffer.WrittenCount > MaximumBytes) throw new InvalidDataException("布局文件超出大小上限。");
        return buffer.WrittenSpan.ToArray();
    }

    public static FurnitureLayout Deserialize(ReadOnlySpan<byte> bytes, FurnitureLayoutContract? contract = null)
    {
        contract ??= FurnitureLayoutContract.Trial;
        if (bytes.Length is < 1 or > MaximumBytes) throw new InvalidDataException("布局文件大小无效。");
        using var document = JsonDocument.Parse(bytes.ToArray());
        var root = ExactObject(document.RootElement,
            ["schemaVersion", "sceneId", "coordinateSpace", "sceneGeometryRevision", "layoutVersion", "lastOperationId", "items"]);
        if (root["schemaVersion"].ValueKind != JsonValueKind.Number || !root["schemaVersion"].TryGetInt32(out var schema) ||
            !(schema == SchemaVersion || (schema == OrdinaryRoomSchemaVersion && ReferenceEquals(contract, FurnitureLayoutContract.OrdinaryRoom))))
            throw new InvalidDataException("布局版本不受支持。");
        RequireString(root["sceneId"], contract.SceneId);
        RequireString(root["coordinateSpace"], contract.CoordinateSpace);
        RequireInt(root["sceneGeometryRevision"], contract.SceneGeometryRevision);
        if (root["layoutVersion"].ValueKind != JsonValueKind.Number || !root["layoutVersion"].TryGetInt64(out var version))
            throw new InvalidDataException("布局版本无效。");
        Guid? operation = root["lastOperationId"].ValueKind == JsonValueKind.Null ? null : ReadGuid(root["lastOperationId"]);
        var items = root["items"];
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > (schema == SchemaVersion ? 1 : contract.MaximumItems))
            throw new InvalidDataException("家具数量超出本场景上限。");
        var instances = new List<FurnitureItemInstance>(items.GetArrayLength());
        foreach (var element in items.EnumerateArray())
        {
            var item = ExactObject(element, ["instanceId", "presetId", "presetRevision", "anchor"]);
            if (item["presetId"].ValueKind != JsonValueKind.String) throw new InvalidDataException("家具预设无效。");
            var kind = item["presetId"].GetString() switch
            {
                FurniturePlatformInstance.PresetId => FurnitureItemKind.StaticPlatform,
                FurnitureItemInstance.RoundBallPresetId when schema == OrdinaryRoomSchemaVersion => FurnitureItemKind.RoundBall,
                _ => throw new InvalidDataException("家具预设不受支持。"),
            };
            RequireInt(item["presetRevision"], FurniturePlatformInstance.PresetRevision);
            var anchor = ExactObject(item["anchor"], ["x", "y"]);
            instances.Add(new(ReadGuid(item["instanceId"]), kind, new(ReadDouble(anchor["x"]), ReadDouble(anchor["y"]))));
        }
        var layout = FurnitureLayout.ForItems(version, operation, instances);
        layout.Validate(contract);
        return layout;
    }

    private static Dictionary<string, JsonElement> ExactObject(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("布局字段不是对象。");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!properties.TryAdd(property.Name, property.Value)) throw new InvalidDataException("布局含重复字段。");
        if (properties.Count != names.Length || names.Any(name => !properties.ContainsKey(name)))
            throw new InvalidDataException("布局字段缺失或含未知字段。");
        return properties;
    }

    private static void RequireInt(JsonElement element, int expected)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value != expected)
            throw new InvalidDataException("布局版本不受支持。");
    }

    private static void RequireString(JsonElement element, string expected)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() != expected)
            throw new InvalidDataException("布局场景或预设不受支持。");
    }

    private static Guid ReadGuid(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || !Guid.TryParseExact(element.GetString(), "D", out var value) || value == Guid.Empty)
            throw new InvalidDataException("布局操作或家具标识无效。");
        return value;
    }

    private static double ReadDouble(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException("家具坐标无效。");
        return value;
    }
}
