using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dango.SafeSpawnAdmission.Core;

public static class RecoveryRecordCodec
{
    public const int MaximumBytes = 65536;
    public const uint CurrentVersion = 1;
    public static byte[] Encode(RecoveryRecord record)
    {
        if (!ContractValidation.Record(record)) throw new ArgumentException("Invalid recovery metadata.", nameof(record));
        byte[] payload = Write(record, null);
        byte[] encoded = Write(record, Convert.ToHexStringLower(SHA256.HashData(payload)));
        if (encoded.Length > MaximumBytes) throw new ArgumentException("Metadata exceeds 64 KiB.", nameof(record));
        return encoded;
    }
    public static bool TryDecode(ReadOnlyMemory<byte> bytes, ConfigurationContract expected,
        out RecoveryRecord? record, out string error)
    {
        record = null; error = "Invalid metadata";
        if (bytes.Length == 0 || bytes.Length > MaximumBytes || !ContractValidation.Configuration(expected)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            Shape(root, "recordVersion", "roomId", "runtimeRevision", "recordRevision", "playEpoch", "lastSuccessfulTick", "configuration", "members", "integrityHash");
            JsonElement configuration = root.GetProperty("configuration");
            Shape(configuration, "geometryId", "geometryVersion", "simulationVersion", "parameterHash");
            var config = new ConfigurationContract(Text(configuration, "geometryId"), configuration.GetProperty("geometryVersion").GetUInt32(),
                configuration.GetProperty("simulationVersion").GetUInt32(), Text(configuration, "parameterHash"));
            JsonElement members = root.GetProperty("members");
            if (members.ValueKind != JsonValueKind.Array || members.GetArrayLength() > 2) throw new FormatException("Member capacity");
            var list = ImmutableArray.CreateBuilder<MemberIdentity>();
            foreach (JsonElement member in members.EnumerateArray())
            {
                Shape(member, "petId", "presenceGeneration");
                list.Add(new(GuidValue(member, "petId"), member.GetProperty("presenceGeneration").GetUInt64()));
            }
            var parsed = new RecoveryRecord(root.GetProperty("recordVersion").GetUInt32(), GuidValue(root, "roomId"),
                root.GetProperty("runtimeRevision").GetUInt64(), root.GetProperty("recordRevision").GetUInt64(),
                GuidValue(root, "playEpoch"), root.GetProperty("lastSuccessfulTick").GetUInt64(), config, list.ToImmutable());
            if (!ContractValidation.Record(parsed) || config != expected) throw new FormatException("Version, configuration or identity mismatch");
            string hash = Text(root, "integrityHash");
            string calculated = Convert.ToHexStringLower(SHA256.HashData(Write(parsed, null)));
            if (hash.Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(calculated)))
                throw new FormatException("Integrity mismatch");
            record = parsed; error = string.Empty; return true;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException)
        { error = e.GetType().Name; return false; }
    }
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new FormatException("Null string");
    private static Guid GuidValue(JsonElement element, string name)
    {
        string text = Text(element, name);
        if (!Guid.TryParseExact(text, "D", out Guid value) || value == Guid.Empty || text != value.ToString("D")) throw new FormatException("Noncanonical identity");
        return value;
    }
    private static void Shape(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("Object required");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name) || property.Value.ValueKind == JsonValueKind.Null)
                throw new FormatException("Unknown, duplicate or null field");
        if (found.Count != names.Length) throw new FormatException("Missing field");
    }
    private static byte[] Write(RecoveryRecord r, string? hash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("recordVersion", r.RecordVersion); writer.WriteString("roomId", r.RoomId.ToString("D"));
            writer.WriteNumber("runtimeRevision", r.RuntimeRevision); writer.WriteNumber("recordRevision", r.RecordRevision);
            writer.WriteString("playEpoch", r.PlayEpoch.ToString("D")); writer.WriteNumber("lastSuccessfulTick", r.LastSuccessfulTick);
            writer.WriteStartObject("configuration"); writer.WriteString("geometryId", r.Configuration.GeometryId);
            writer.WriteNumber("geometryVersion", r.Configuration.GeometryVersion); writer.WriteNumber("simulationVersion", r.Configuration.SimulationVersion);
            writer.WriteString("parameterHash", r.Configuration.ParameterHash); writer.WriteEndObject(); writer.WriteStartArray("members");
            foreach (MemberIdentity m in ContractValidation.CanonicalMembers(r.Members))
            { writer.WriteStartObject(); writer.WriteString("petId", m.PetId.ToString("D")); writer.WriteNumber("presenceGeneration", m.PresenceGeneration); writer.WriteEndObject(); }
            writer.WriteEndArray(); if (hash is not null) writer.WriteString("integrityHash", hash); writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
