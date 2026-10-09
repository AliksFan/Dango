using System.Text;
using System.Text.Json;

namespace Dango.Protocol;

public sealed class AppearanceContractException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// Data contract only; neither WPF nor server persistence belongs in this assembly.
public static class AppearanceContract
{
    public const int SchemaVersion = 3;
    public const int MaximumBytes = 4096;
    private static readonly string[] Names = ["appearanceSchemaVersion", "bodyColorHex", "eyeColorHex", "eyeSizePercent", "eyeOvalityPercent", "eyeHeightControlPercent", "eyeSpacingControlPercent"];

    public static JsonElement ValidateAndClone(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(input.GetRawText()) > MaximumBytes)
            throw new AppearanceContractException("appearance_invalid");
        var fields = input.EnumerateObject().ToArray();
        if (fields.Length != Names.Length || fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Names.Length
            || fields.Any(p => !Names.Contains(p.Name, StringComparer.Ordinal))) throw new AppearanceContractException("appearance_invalid");
        var version = input.GetProperty("appearanceSchemaVersion");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != SchemaVersion)
            throw new AppearanceContractException("appearance_schema_unsupported");
        foreach (var name in Names.Skip(1).Take(2))
        {
            var color = input.GetProperty(name);
            var text = color.ValueKind == JsonValueKind.String ? color.GetString() : null;
            if (text is null || text.Length != 7 || text[0] != '#' || text.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
                throw new AppearanceContractException("appearance_invalid");
        }
        (string Name, double Min, double Max)[] ranges = [("eyeSizePercent", 70, 140), ("eyeOvalityPercent", -100, 100), ("eyeHeightControlPercent", 32, 66), ("eyeSpacingControlPercent", 12, 34)];
        foreach (var (name, min, max) in ranges)
        {
            var number = input.GetProperty(name);
            if (number.ValueKind != JsonValueKind.Number || !number.TryGetDouble(out var value) || !double.IsFinite(value) || value < min || value > max)
                throw new AppearanceContractException("appearance_invalid");
        }
        return input.Clone();
    }

    public static bool Equivalent(JsonElement left, JsonElement right)
    {
        left = ValidateAndClone(left);
        right = ValidateAndClone(right);
        return Names.All(name => name is "bodyColorHex" or "eyeColorHex"
            ? string.Equals(left.GetProperty(name).GetString(), right.GetProperty(name).GetString(), StringComparison.OrdinalIgnoreCase)
            : left.GetProperty(name).GetDouble() == right.GetProperty(name).GetDouble());
    }
}

public sealed record PetConfigurationSnapshot(Guid UserId, Guid PetId, long ConfigurationVersion, JsonElement Configuration);
public sealed record PetConfigurationUpdate(long ExpectedVersion, JsonElement Configuration);
