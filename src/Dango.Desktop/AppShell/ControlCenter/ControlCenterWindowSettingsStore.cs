using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

public sealed record ControlCenterWindowSettings
{
    public const int CurrentSchemaVersion = 3;

    [JsonConstructor]
    public ControlCenterWindowSettings(
        double width,
        double height,
        int schemaVersion = CurrentSchemaVersion,
        DesktopLayeringSettings? desktopLayeringExperiment = null,
        KeyboardControlSettings? keyboardControl = null,
        DesktopDisplayMode displayMode = DesktopDisplayMode.Wallpaper,
        SceneWindowSettings? sceneWindow = null,
        bool showDebugSkeleton = false)
    {
        Width = width;
        Height = height;
        SchemaVersion = schemaVersion == 0 ? CurrentSchemaVersion : schemaVersion;
        DesktopLayeringExperiment = desktopLayeringExperiment ?? DesktopLayeringSettings.CreateDefault();
        KeyboardControl = keyboardControl ?? KeyboardControlSettings.CreateDefault();
        DisplayMode = displayMode;
        SceneWindow = sceneWindow ?? SceneWindowSettings.CreateDefault();
        ShowDebugSkeleton = showDebugSkeleton;
    }

    public double Width { get; }
    public double Height { get; }
    public int SchemaVersion { get; }
    public DesktopLayeringSettings DesktopLayeringExperiment { get; }
    public KeyboardControlSettings KeyboardControl { get; }
    public DesktopDisplayMode DisplayMode { get; }
    public SceneWindowSettings SceneWindow { get; }
    public bool ShowDebugSkeleton { get; }

    public static ControlCenterWindowSettings CreateDefault() => new(940, 620);
}

public sealed record ControlCenterSettingsLoadResult(
    ControlCenterWindowSettings Settings,
    bool WasMigrated,
    string? Warning);

public static class ControlCenterWindowSettingsValidator
{
    public const double MinimumWidth = 820;
    public const double MinimumHeight = 560;
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

        if (!Enum.IsDefined(settings.DisplayMode))
        {
            throw new InvalidDataException("显示模式不受支持。");
        }

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
        if (!File.Exists(path))
        {
            return new(ControlCenterWindowSettings.CreateDefault(), false, null);
        }

        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(path), SerializerOptions)
                ?? throw new InvalidDataException("控制中心设置为空。");
            var sourceSchema = persisted.SchemaVersion ?? 1;
            if (sourceSchema is not (1 or 2 or ControlCenterWindowSettings.CurrentSchemaVersion))
            {
                return SafeDefault("设置版本无法识别，已使用安全默认值。");
            }

            var (displayMode, modeWarning) = ParseDisplayMode(persisted.DisplayMode);
            var defaults = ControlCenterWindowSettings.CreateDefault();
            var settings = new ControlCenterWindowSettings(
                persisted.Width ?? defaults.Width,
                persisted.Height ?? defaults.Height,
                ControlCenterWindowSettings.CurrentSchemaVersion,
                persisted.DesktopLayeringExperiment,
                persisted.KeyboardControl,
                displayMode,
                persisted.SceneWindow,
                persisted.ShowDebugSkeleton ?? false);
            ControlCenterWindowSettingsValidator.Validate(settings);
            return new(settings, sourceSchema != ControlCenterWindowSettings.CurrentSchemaVersion, modeWarning);
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

    private static ControlCenterSettingsLoadResult SafeDefault(string warning) =>
        new(ControlCenterWindowSettings.CreateDefault(), false, warning);

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
    }
}
