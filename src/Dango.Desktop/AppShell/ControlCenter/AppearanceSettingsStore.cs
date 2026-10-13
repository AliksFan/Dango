using System.IO;
using System.Text.Json;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Strict, independent persistence for the local Dango's colors and base eye controls.</summary>
public sealed record AppearanceSettings
{
    public const int CurrentSchemaVersion = 4;
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
        this.writer = writer ?? new AtomicTextFileWriter();

    public void Save(string path, AppearanceSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        // Validate again at the persistence boundary, independently of UI slider constraints.
        var eyes = settings.EyeLayout;
        _ = new EyeLayoutParameters(eyes.SizePercent, eyes.OvalPercent,
            eyes.HeightControlPercent, eyes.SpacingControlPercent);
        var body = settings.Configuration.Body;
        _ = new DangoBodyControls(body.SizePercent, body.Softness, body.LandingBuild);
        writer.Write(path, JsonSerializer.Serialize(
            new PersistedSettings(AppearanceSettings.CurrentSchemaVersion,
                settings.BodyColor.ToHex(), settings.EyeColor.ToHex(),
                eyes.SizePercent, eyes.OvalPercent, eyes.HeightControlPercent, eyes.SpacingControlPercent,
                body.SizePercent, body.Softness, body.LandingBuild), SerializerOptions));
    }

    public void Save(string path, DangoAppearanceConfiguration configuration) => Save(path, new AppearanceSettings(configuration));

    public AppearanceSettings Load(string path) => LoadOrDefault(path).Settings;

    public AppearanceSettingsLoadResult LoadOrDefault(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return SafeDefault("外观设置不是对象，已使用安全默认外观。");

            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!fields.TryAdd(property.Name, property.Value))
                    return SafeDefault("外观设置存在重复字段，已使用安全默认外观。");

            if (!fields.TryGetValue("appearanceSchemaVersion", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out var schema))
                return SafeDefault("外观设置版本无法识别，已使用安全默认外观。");
            if (schema is not 1 and not 2 and not 3 and not AppearanceSettings.CurrentSchemaVersion)
                return SafeDefault("外观设置版本无法识别，已使用安全默认外观。");
            var expectedCount = schema switch { 1 => 2, 2 => 3, 3 => 7, _ => 10 };
            if (fields.Count != expectedCount ||
                !fields.TryGetValue("bodyColorHex", out var bodyElement) ||
                bodyElement.ValueKind != JsonValueKind.String ||
                (schema != 1 && (!fields.TryGetValue("eyeColorHex", out var eyeElement) ||
                    eyeElement.ValueKind != JsonValueKind.String)))
                return SafeDefault("外观设置字段不完整或含未知字段，已使用安全默认外观。");

            var body = OpaqueSrgbColor.Parse(bodyElement.GetString()!);
            var eye = schema == 1 ? AppearanceSettings.DefaultEyeColor :
                OpaqueSrgbColor.Parse(fields["eyeColorHex"].GetString()!);
            // Schema 3 already carries the four eye controls; only Schema 1/2 fall back to default eyes.
            var eyes = schema < 3 ? EyeLayoutParameters.Default :
                new EyeLayoutParameters(ReadControl(fields, "eyeSizePercent"), ReadControl(fields, "eyeOvalityPercent"),
                    ReadControl(fields, "eyeHeightControlPercent"), ReadControl(fields, "eyeSpacingControlPercent"));
            var bodyControls = schema < 4 ? DangoBodyControls.Default :
                new DangoBodyControls(ReadControl(fields, "bodySizePercent"), ReadControl(fields, "bodySoftness"),
                    ReadControl(fields, "bodyLandingBuild"));

            return new(new AppearanceSettings(new DangoAppearanceConfiguration(body, eye, eyes, bodyControls)), null,
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
        double EyeSizePercent, double EyeOvalityPercent, double EyeHeightControlPercent, double EyeSpacingControlPercent,
        double BodySizePercent, double BodySoftness, double BodyLandingBuild);
}
