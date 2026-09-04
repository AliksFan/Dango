using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

public sealed record ControlCenterWindowSettings
{
    [JsonConstructor]
    public ControlCenterWindowSettings(
        double width,
        double height,
        int schemaVersion = 1,
        DesktopLayeringSettings? desktopLayeringExperiment = null,
        KeyboardControlSettings? keyboardControl = null)
    {
        Width = width;
        Height = height;
        SchemaVersion = schemaVersion == 0 ? 1 : schemaVersion;
        DesktopLayeringExperiment = desktopLayeringExperiment ?? DesktopLayeringSettings.CreateDefault();
        KeyboardControl = keyboardControl ?? KeyboardControlSettings.CreateDefault();
    }

    public double Width { get; }

    public double Height { get; }

    public int SchemaVersion { get; }

    public DesktopLayeringSettings DesktopLayeringExperiment { get; }

    public KeyboardControlSettings KeyboardControl { get; }
}

public static class ControlCenterWindowSettingsValidator
{
    public const double MinimumWidth = 820;
    public const double MinimumHeight = 560;
    public const double MaximumWidth = 7680;
    public const double MaximumHeight = 4320;

    public static void Validate(ControlCenterWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(settings.Width) ||
            !double.IsFinite(settings.Height) ||
            settings.Width < MinimumWidth ||
            settings.Height < MinimumHeight ||
            settings.Width > MaximumWidth ||
            settings.Height > MaximumHeight)
        {
            throw new InvalidDataException("控制中心窗口尺寸超出安全范围。");
        }

        if (settings.SchemaVersion != 1)
        {
            throw new InvalidDataException("控制中心设置版本不受支持。");
        }

        DesktopLayeringSettingsValidator.Validate(settings.DesktopLayeringExperiment);
        KeyboardControlSettingsValidator.Validate(settings.KeyboardControl);
    }
}

public sealed class ControlCenterWindowSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    static ControlCenterWindowSettingsStore()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly IAtomicTextWriter writer;

    public ControlCenterWindowSettingsStore(IAtomicTextWriter? writer = null)
    {
        this.writer = writer ?? new AtomicTextFileWriter();
    }

    public void Save(string path, ControlCenterWindowSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ControlCenterWindowSettingsValidator.Validate(settings);
        writer.Write(path, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    public ControlCenterWindowSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var settings = JsonSerializer.Deserialize<ControlCenterWindowSettings>(
            File.ReadAllText(path),
            SerializerOptions) ?? throw new InvalidDataException("控制中心设置为空。");
        ControlCenterWindowSettingsValidator.Validate(settings);
        return settings;
    }
}
