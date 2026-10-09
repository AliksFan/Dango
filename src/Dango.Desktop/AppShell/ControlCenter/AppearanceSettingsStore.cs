using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;
using Dango.Desktop.AppShell.Persistence;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Strict, independent persistence for the local Dango's colors and base eye controls.</summary>
public sealed record AppearanceSettings
{
    public const int CurrentSchemaVersion = 3;
    public static OpaqueSrgbColor DefaultEyeColor => DangoAppearanceConfiguration.DefaultEyeColor;
    public DangoAppearanceConfiguration Configuration { get; }
    public OpaqueSrgbColor BodyColor => Configuration.BodyColor;
    public OpaqueSrgbColor EyeColor => Configuration.EyeColor;
    public EyeLayoutParameters EyeLayout => Configuration.EyeLayout;

    public AppearanceSettings(OpaqueSrgbColor bodyColor) : this(bodyColor, DefaultEyeColor) { }
    public AppearanceSettings(OpaqueSrgbColor bodyColor, OpaqueSrgbColor eyeColor)
        : this(bodyColor, eyeColor, EyeLayoutParameters.Default) { }
    public AppearanceSettings(OpaqueSrgbColor bodyColor, OpaqueSrgbColor eyeColor, EyeLayoutParameters eyeLayout)
        : this(new DangoAppearanceConfiguration(bodyColor, eyeColor, eyeLayout)) { }
    public AppearanceSettings(DangoAppearanceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration;
    }

    public static AppearanceSettings CreateDefault() => new(DangoAppearanceConfiguration.Default);
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
        this.writer = writer ?? new DurableAtomicTextWriter();

    public void Save(string path, AppearanceSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        // Validate again at the persistence boundary, independently of UI slider constraints.
        var eyes = settings.EyeLayout;
        _ = new EyeLayoutParameters(eyes.SizePercent, eyes.OvalPercent,
            eyes.HeightControlPercent, eyes.SpacingControlPercent);
        writer.Write(path, JsonSerializer.Serialize(
            new PersistedSettings(AppearanceSettings.CurrentSchemaVersion,
                settings.BodyColor.ToHex(), settings.EyeColor.ToHex(),
                eyes.SizePercent, eyes.OvalPercent, eyes.HeightControlPercent, eyes.SpacingControlPercent), SerializerOptions));
    }

    public void Save(string path, DangoAppearanceConfiguration configuration) => Save(path, new AppearanceSettings(configuration));

    public AppearanceSettings Load(string path) => LoadOrDefault(path).Settings;

    public AppearanceSettingsLoadResult LoadOrDefault(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var snapshot = DurableAtomicTextWriter.Capture(path);
            return snapshot.Exists ? Parse(snapshot.Bytes) : new(AppearanceSettings.CreateDefault(), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return SafeDefault($"外观设置无法读取，已使用安全默认外观（{exception.GetType().Name}）。");
        }
    }

    public AppearanceSettingsLoadResult Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            // Match the bounded capture used by configuration association; parse exactly the archived bytes.
            if (bytes.Length > 65536) throw new InvalidDataException("appearance_file_too_large");
            // Preserve File.ReadAllText's existing UTF-8/16/32 BOM compatibility while archiving raw bytes.
            using var reader = new StreamReader(new MemoryStream(bytes.ToArray()), detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return SafeDefault("外观设置不是对象，已使用安全默认外观。");

            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!fields.TryAdd(property.Name, property.Value))
                    return SafeDefault("外观设置存在重复字段，已使用安全默认外观。");

            if (!fields.TryGetValue("appearanceSchemaVersion", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out var schema))
                return SafeDefault("外观设置版本无法识别，已使用安全默认外观。");
            if (schema is not 1 and not 2 and not AppearanceSettings.CurrentSchemaVersion)
                return SafeDefault("外观设置版本无法识别，已使用安全默认外观。");
            var expectedCount = schema switch { 1 => 2, 2 => 3, _ => 7 };
            if (fields.Count != expectedCount ||
                !fields.TryGetValue("bodyColorHex", out var bodyElement) ||
                bodyElement.ValueKind != JsonValueKind.String ||
                (schema != 1 && (!fields.TryGetValue("eyeColorHex", out var eyeElement) ||
                    eyeElement.ValueKind != JsonValueKind.String)))
                return SafeDefault("外观设置字段不完整或含未知字段，已使用安全默认外观。");

            var body = OpaqueSrgbColor.Parse(bodyElement.GetString()!);
            var eye = schema == 1 ? AppearanceSettings.DefaultEyeColor :
                OpaqueSrgbColor.Parse(fields["eyeColorHex"].GetString()!);
            var eyes = schema < AppearanceSettings.CurrentSchemaVersion ? EyeLayoutParameters.Default :
                new EyeLayoutParameters(ReadControl(fields, "eyeSizePercent"), ReadControl(fields, "eyeOvalityPercent"),
                    ReadControl(fields, "eyeHeightControlPercent"), ReadControl(fields, "eyeSpacingControlPercent"));

            return new(new AppearanceSettings(body, eye, eyes), null,
                RequiresMigration: schema < AppearanceSettings.CurrentSchemaVersion);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(AppearanceSettings.CreateDefault(), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException or
            ArgumentOutOfRangeException or IOException or UnauthorizedAccessException)
        {
            return SafeDefault($"外观设置无法读取，已使用安全默认外观（{exception.GetType().Name}）。");
        }
    }

    private static double ReadControl(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException("外观设置眼睛参数缺失或不是有限数字。");
        return value;
    }

    private static AppearanceSettingsLoadResult SafeDefault(string warning) =>
        new(AppearanceSettings.CreateDefault(), warning, PreserveOriginal: true);

    private sealed record PersistedSettings(int AppearanceSchemaVersion, string BodyColorHex, string EyeColorHex,
        double EyeSizePercent, double EyeOvalityPercent, double EyeHeightControlPercent, double EyeSpacingControlPercent);
}
