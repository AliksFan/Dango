using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum ControlCenterWindowPreset
{
    Landscape1080p,
    Landscape1440p,
    Portrait,
}

public readonly record struct ControlCenterWindowPresetSize(double Width, double Height);

public static class ControlCenterWindowPresets
{
    // Landscape dimensions are physical-pixel targets. The portrait target is allowed
    // to grow to its content minimum and is capped by the current work area at runtime.
    public const double PortraitMinimumLogicalWidth = 360;
    public const double PortraitMinimumLogicalHeight = 640;
    public const double PortraitMaximumWorkAreaHeightFraction = 0.9;

    public static ControlCenterWindowPresetSize GetSize(ControlCenterWindowPreset preset) => preset switch
    {
        ControlCenterWindowPreset.Landscape1080p => new(960, 540),
        ControlCenterWindowPreset.Landscape1440p => new(1280, 720),
        ControlCenterWindowPreset.Portrait => new(480, 800),
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };
}

public sealed record ControlCenterWindowSettings
{
    public const int CurrentSchemaVersion = 5;

    [JsonConstructor]
    public ControlCenterWindowSettings(
        double width,
        double height,
        int schemaVersion = CurrentSchemaVersion,
        DesktopLayeringSettings? desktopLayeringExperiment = null,
        KeyboardControlSettings? keyboardControl = null,
        DesktopDisplayMode displayMode = DesktopDisplayMode.Wallpaper,
        SceneWindowSettings? sceneWindow = null,
        bool showDebugSkeleton = false,
        bool interactionEnabled = true,
        string hatPresetId = "sprout",
        bool introSeen = false,
        ControlCenterWindowPreset selectedPreset = ControlCenterWindowPreset.Landscape1080p)
    {
        Width = width;
        Height = height;
        SchemaVersion = schemaVersion == 0 ? CurrentSchemaVersion : schemaVersion;
        DesktopLayeringExperiment = desktopLayeringExperiment ?? DesktopLayeringSettings.CreateDefault();
        KeyboardControl = keyboardControl ?? KeyboardControlSettings.CreateDefault();
        DisplayMode = displayMode;
        SceneWindow = sceneWindow ?? SceneWindowSettings.CreateDefault();
        ShowDebugSkeleton = showDebugSkeleton;
        InteractionEnabled = interactionEnabled;
        HatPresetId = hatPresetId;
        IntroSeen = introSeen;
        SelectedPreset = selectedPreset;
    }

    public double Width { get; init; }
    public double Height { get; init; }
    public int SchemaVersion { get; init; }
    public DesktopLayeringSettings DesktopLayeringExperiment { get; init; }
    public KeyboardControlSettings KeyboardControl { get; init; }
    public DesktopDisplayMode DisplayMode { get; init; }
    public SceneWindowSettings SceneWindow { get; init; }
    public bool ShowDebugSkeleton { get; init; }
    public bool InteractionEnabled { get; init; }
    public string HatPresetId { get; init; }
    public bool IntroSeen { get; init; }
    public ControlCenterWindowPreset SelectedPreset { get; init; }

    // Legacy review windows still use their original starting geometry; Product applies
    // SelectedPreset when its window is created.
    public static ControlCenterWindowSettings CreateDefault() => new(940, 620);
}

public sealed record ControlCenterSettingsLoadResult(
    ControlCenterWindowSettings Settings,
    bool WasMigrated,
    string? Warning,
    bool PreserveOriginal = false);

public static class ControlCenterWindowSettingsValidator
{
    public const double MinimumWidth = 336;
    public const double MinimumHeight = 540;
    public const double MaximumWidth = 7680;
    public const double MaximumHeight = 4320;
    public const double MinimumSceneWidth = 480;
    public const double MinimumSceneHeight = 360;

    public static void Validate(ControlCenterWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSize(settings.Width, settings.Height, MinimumWidth, MinimumHeight, "控制中心窗口尺寸超出安全范围。");
        if (settings.SchemaVersion != ControlCenterWindowSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException("控制中心设置版本不受支持。");
        }

        if (!Enum.IsDefined(settings.SelectedPreset))
            throw new InvalidDataException("控制中心窗口预设不受支持。");

        if (!Enum.IsDefined(settings.DisplayMode))
        {
            throw new InvalidDataException("显示模式不受支持。");
        }
        if (!DangoHatPresets.IsKnown(settings.HatPresetId))
            throw new InvalidDataException("帽型不受支持。");

        var scene = settings.SceneWindow;
        ValidateSize(scene.Width, scene.Height, MinimumSceneWidth, MinimumSceneHeight, "场景窗口尺寸超出安全范围。");
        if (scene.MonitorKey is null || scene.MonitorKey.Length > 128 ||
            !double.IsFinite(scene.NormalizedCenterX) ||
            !double.IsFinite(scene.NormalizedCenterY) ||
            scene.NormalizedCenterX is < 0 or > 1 ||
            scene.NormalizedCenterY is < 0 or > 1)
        {
            throw new InvalidDataException("场景窗口位置超出安全范围。");
        }

        DesktopLayeringSettingsValidator.Validate(settings.DesktopLayeringExperiment);
        KeyboardControlSettingsValidator.Validate(settings.KeyboardControl);
    }

    private static void ValidateSize(double width, double height, double minimumWidth, double minimumHeight, string message)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) ||
            width < minimumWidth || height < minimumHeight ||
            width > MaximumWidth || height > MaximumHeight)
        {
            throw new InvalidDataException(message);
        }
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

    static ControlCenterWindowSettingsStore() =>
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());

    private readonly IAtomicTextWriter writer;

    public ControlCenterWindowSettingsStore(IAtomicTextWriter? writer = null) =>
        this.writer = writer ?? new AtomicTextFileWriter();

    public void Save(string path, ControlCenterWindowSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ControlCenterWindowSettingsValidator.Validate(settings);
        writer.Write(path, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    public ControlCenterWindowSettings Load(string path) => LoadOrDefault(path).Settings;

    public ControlCenterSettingsLoadResult LoadOrDefault(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(path), SerializerOptions)
                ?? throw new InvalidDataException("控制中心设置为空。");
            var sourceSchema = persisted.SchemaVersion ?? 1;
            if (sourceSchema is not (1 or 2 or 3 or 4 or ControlCenterWindowSettings.CurrentSchemaVersion))
            {
                return SafeDefault("设置版本无法识别，已使用安全默认值。");
            }

            var (displayMode, modeWarning) = ParseDisplayMode(persisted.DisplayMode);
            var (selectedPreset, presetWarning) = ParsePreset(persisted.SelectedPreset);
            var defaults = ControlCenterWindowSettings.CreateDefault();
            var validHat = persisted.HatPresetId is null || DangoHatPresets.IsKnown(persisted.HatPresetId);
            var legacyArrowKeyboard = persisted.KeyboardControl?.Scheme == KeyboardControlScheme.ArrowKeys;
            var keyboardControl = legacyArrowKeyboard
                ? persisted.KeyboardControl! with { Scheme = KeyboardControlScheme.Wasd }
                : persisted.KeyboardControl;
            var settings = new ControlCenterWindowSettings(
                persisted.Width ?? defaults.Width,
                persisted.Height ?? defaults.Height,
                ControlCenterWindowSettings.CurrentSchemaVersion,
                persisted.DesktopLayeringExperiment,
                keyboardControl,
                displayMode,
                persisted.SceneWindow,
                persisted.ShowDebugSkeleton ?? false,
                persisted.InteractionEnabled ?? true,
                validHat ? persisted.HatPresetId ?? "sprout" : "sprout",
                persisted.IntroSeen ?? false,
                selectedPreset);
            ControlCenterWindowSettingsValidator.Validate(settings);
            var warnings = new[]
            {
                validHat ? null : "帽型无法识别，已使用叶芽；其余有效设置已保留。",
                modeWarning,
                presetWarning,
            }.Where(message => message is not null);
            return new(settings, sourceSchema != ControlCenterWindowSettings.CurrentSchemaVersion || !validHat ||
                modeWarning is not null || presetWarning is not null || legacyArrowKeyboard,
                string.Join(" ", warnings) is { Length: > 0 } warning ? warning : null);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(ControlCenterWindowSettings.CreateDefault(), false, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return SafeDefault($"设置无法读取，已使用安全默认值（{exception.GetType().Name}）。");
        }
    }

    private static (DesktopDisplayMode Mode, string? Warning) ParseDisplayMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Overlay", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, nameof(DesktopDisplayMode.Wallpaper), StringComparison.OrdinalIgnoreCase))
        {
            return (DesktopDisplayMode.Wallpaper, null);
        }

        if (string.Equals(value, nameof(DesktopDisplayMode.Window), StringComparison.OrdinalIgnoreCase))
        {
            return (DesktopDisplayMode.Window, null);
        }

        return (DesktopDisplayMode.Wallpaper, "显示模式无法识别，已切换到壁纸层安全默认值。");
    }

    private static (ControlCenterWindowPreset Preset, string? Warning) ParsePreset(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return (ControlCenterWindowPreset.Landscape1080p, null);
        if (value.Value.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ControlCenterWindowPreset>(value.Value.GetString(), ignoreCase: true, out var preset) &&
            Enum.IsDefined(preset))
            return (preset, null);
        return (ControlCenterWindowPreset.Landscape1080p,
            "窗口尺寸档位无法识别，已使用 1080p 横向档；其余有效设置已保留。");
    }

    private static ControlCenterSettingsLoadResult SafeDefault(string warning) =>
        new(ControlCenterWindowSettings.CreateDefault(), false, warning, PreserveOriginal: true);

    private sealed class PersistedSettings
    {
        public double? Width { get; init; }
        public double? Height { get; init; }
        public int? SchemaVersion { get; init; }
        public DesktopLayeringSettings? DesktopLayeringExperiment { get; init; }
        public KeyboardControlSettings? KeyboardControl { get; init; }
        public string? DisplayMode { get; init; }
        public SceneWindowSettings? SceneWindow { get; init; }
        public bool? ShowDebugSkeleton { get; init; }
        public bool? InteractionEnabled { get; init; }
        public string? HatPresetId { get; init; }
        public bool? IntroSeen { get; init; }
        public JsonElement? SelectedPreset { get; init; }
    }
}
