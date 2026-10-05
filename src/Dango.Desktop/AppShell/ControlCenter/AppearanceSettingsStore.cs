using System.IO;
using System.Text.Json;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Strict, independent persistence for the locally selected body and eye colors.</summary>
public sealed record AppearanceSettings(OpaqueSrgbColor BodyColor, OpaqueSrgbColor EyeColor)
{
    public const int CurrentSchemaVersion = 2;
    public static OpaqueSrgbColor DefaultEyeColor { get; } = OpaqueSrgbColor.Parse("#2B201E");

    public AppearanceSettings(OpaqueSrgbColor bodyColor) : this(bodyColor, DefaultEyeColor) { }

    public static AppearanceSettings CreateDefault() => new(OpaqueSrgbColor.Default, DefaultEyeColor);
}

public sealed record AppearanceSettingsLoadResult(
    AppearanceSettings Settings,
    string? Warning,
    bool PreserveOriginal = false,
    bool RequiresMigration = false);

public sealed class AppearanceSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IAtomicTextWriter writer;

    public AppearanceSettingsStore(IAtomicTextWriter? writer = null) =>
        this.writer = writer ?? new AtomicTextFileWriter();

    public void Save(string path, AppearanceSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        writer.Write(path, JsonSerializer.Serialize(
            new PersistedSettings(AppearanceSettings.CurrentSchemaVersion,
                settings.BodyColor.ToHex(), settings.EyeColor.ToHex()), SerializerOptions));
    }

    public AppearanceSettings Load(string path) => LoadOrDefault(path).Settings;

    public AppearanceSettingsLoadResult LoadOrDefault(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return SafeDefault("外观设置不是对象，已使用安全默认颜色。");

            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!fields.TryAdd(property.Name, property.Value))
                    return SafeDefault("外观设置存在重复字段，已使用安全默认颜色。");

            if (!fields.TryGetValue("appearanceSchemaVersion", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out var schema))
                return SafeDefault("外观设置版本无法识别，已使用安全默认颜色。");
            if (schema is not 1 and not AppearanceSettings.CurrentSchemaVersion)
                return SafeDefault("外观设置版本无法识别，已使用安全默认颜色。");
            var isLegacy = schema == 1;
            var expectedCount = isLegacy ? 2 : 3;
            if (fields.Count != expectedCount ||
                !fields.TryGetValue("bodyColorHex", out var bodyElement) ||
                bodyElement.ValueKind != JsonValueKind.String ||
                (!isLegacy && (!fields.TryGetValue("eyeColorHex", out var eyeElement) ||
                    eyeElement.ValueKind != JsonValueKind.String)))
                return SafeDefault("外观设置字段不完整或含未知字段，已使用安全默认颜色。");

            var body = OpaqueSrgbColor.Parse(bodyElement.GetString()!);
            var eye = isLegacy ? AppearanceSettings.DefaultEyeColor :
                OpaqueSrgbColor.Parse(fields["eyeColorHex"].GetString()!);

            return new(new AppearanceSettings(body, eye), null, RequiresMigration: isLegacy);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(AppearanceSettings.CreateDefault(), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            return SafeDefault($"外观设置无法读取，已使用安全默认颜色（{exception.GetType().Name}）。");
        }
    }

    private static AppearanceSettingsLoadResult SafeDefault(string warning) =>
        new(AppearanceSettings.CreateDefault(), warning, PreserveOriginal: true);

    private sealed record PersistedSettings(int AppearanceSchemaVersion, string BodyColorHex, string EyeColorHex);
}
