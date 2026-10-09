using System.IO;
using System.Text;
using System.Text.Json;

namespace Dango.Desktop.AppShell.Persistence;

public sealed record LocalRoomSaveLoadResult(
    LocalRoomSaveState State,
    ExactFileSnapshot? Original,
    bool PreserveOriginal,
    string? Warning);

/// <summary>Strict bounded schema parsing; unreadable originals remain protected and are never replaced with defaults.</summary>
public sealed class LocalRoomSaveStore
{
    public const int MaximumFileBytes = 65_536;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly DurableAtomicTextWriter writer;

    public LocalRoomSaveStore(DurableAtomicTextWriter? writer = null) => this.writer = writer ?? new();

    public LocalRoomSaveLoadResult LoadOrDefault(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ExactFileSnapshot? original = null;
        try
        {
            original = DurableAtomicTextWriter.Capture(path, MaximumFileBytes);
            if (!original.Exists)
                return new(LocalRoomSaveState.CreateDefault(), original, false, null);
            using var document = JsonDocument.Parse(original.Bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var fields = ReadObject(document.RootElement, "schemaVersion", "ordinaryRoom", "desktopPetRoom");
            var state = new LocalRoomSaveState(ReadInt(fields["schemaVersion"]),
                ReadRoom(fields["ordinaryRoom"]), ReadRoom(fields["desktopPetRoom"]));
            state.Validate();
            return new(state, original, false, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException or
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new(LocalRoomSaveState.CreateDefault(), original, true,
                $"本地房间存档无法读取；原文件已保护，本次使用安全默认房间（{exception.GetType().Name}）。");
        }
    }

    /// <summary>Only replaces the exact bytes loaded by this session among cooperating Dango writers.</summary>
    public bool TrySave(string path, LocalRoomSaveState state, ExactFileSnapshot expected, out ExactFileSnapshot saved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expected);
        state.Validate();
        var content = JsonSerializer.Serialize(state, SerializerOptions);
        if (!writer.TryReplace(path, expected, content, MaximumFileBytes))
        {
            saved = expected;
            return false;
        }
        saved = ExactFileSnapshot.FromBytes(Encoding.UTF8.GetBytes(content));
        return true;
    }

    private static LocalRoomSaveData ReadRoom(JsonElement element)
    {
        var fields = ReadObject(element, "roomId", "roomType", "templateId", "templateVersion", "resumeCenterX", "camera");
        if (fields["roomId"].ValueKind != JsonValueKind.String || !fields["roomId"].TryGetGuid(out var id))
            throw new InvalidDataException("本地房间标识无法识别。");
        var camera = ReadObject(fields["camera"], "mode", "preset", "requestedZoomPercent", "manualX", "manualY");
        return new(id, ReadString(fields["roomType"]), ReadString(fields["templateId"]), ReadInt(fields["templateVersion"]),
            fields["resumeCenterX"].ValueKind == JsonValueKind.Null ? null : ReadDouble(fields["resumeCenterX"]),
            new(ReadString(camera["mode"]), ReadString(camera["preset"]), ReadDouble(camera["requestedZoomPercent"]),
                ReadDouble(camera["manualX"]), ReadDouble(camera["manualY"])));
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("本地房间字段必须是对象。");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("本地房间存档含未知或重复字段。");
        if (fields.Count != names.Length)
            throw new InvalidDataException("本地房间存档缺少必要字段。");
        return fields;
    }

    private static int ReadInt(JsonElement element) => element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
        ? value : throw new InvalidDataException("本地房间字段必须是整数。");

    private static double ReadDouble(JsonElement element) => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value)
        ? value : throw new InvalidDataException("本地房间字段必须是数字。");

    private static string ReadString(JsonElement element) => element.ValueKind == JsonValueKind.String && element.GetString() is string value
        ? value : throw new InvalidDataException("本地房间字段必须是字符串。");
}
