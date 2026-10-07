using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Accessories;

public enum AccessorySettingsLoadState { Missing, Valid, Protected }

public sealed record AccessorySettingsLoadResult(AccessoryConfiguration Configuration,
    AccessorySettingsLoadState State, string? Warning = null, bool IsTransientReadFailure = false);

/// <summary>Images are immutable; the manifest is the sole commit point.</summary>
public sealed class AccessorySettingsStore
{
    public const int CurrentSchemaVersion = 1;
    public const long MaximumManifestBytes = 8L * 1024 * 1024;
    private static readonly string[] ItemFields =
    [
        "id", "name", "assetId", "pixelWidth", "pixelHeight", "isEquipped", "layer", "order",
        "sizePercent", "baseAngleDegrees", "imagePivotX", "imagePivotY", "bodyAnchorX", "bodyAnchorY",
        "softness", "connectionStrength", "rotationDamping", "lockRotation", "returnStrength",
        "maxSwingDegrees", "limitSwing", "gravityInfluence",
    ];
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<AccessoryLayer>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
    private readonly IAtomicTextWriter writer;
    private readonly Action<string, byte[]> assetWriter;

    public AccessorySettingsStore(IAtomicTextWriter? writer = null, Action<string, byte[]>? assetWriter = null)
    {
        this.writer = writer ?? new AtomicTextFileWriter();
        this.assetWriter = assetWriter ?? WriteImmutableAsset;
    }

    public AccessorySettingsLoadResult Load(string settingsRoot)
    {
        var path = GetManifestPath(settingsRoot);
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return Protected("配饰清单不能是目录或链接；原件已保护。");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length < 1 || input.Length > MaximumManifestBytes)
                return Protected("配饰清单大小无效；原件已保护。");
            using var document = JsonDocument.Parse(input);
            var rootFields = ReadExactObject(document.RootElement, ["accessorySchemaVersion", "items"]);
            if (!rootFields["accessorySchemaVersion"].TryGetInt32(out var version) || version != CurrentSchemaVersion)
                return Protected("配饰清单版本无法识别；原件已保护。");
            var items = rootFields["items"];
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > AccessoryConfiguration.MaximumLibraryItems)
                return Protected("配饰条目列表无效；原件已保护。");
            var entries = new List<AccessoryItem>();
            foreach (var item in items.EnumerateArray())
            {
                _ = ReadExactObject(item, ItemFields);
                entries.Add(JsonSerializer.Deserialize<AccessoryItem>(item, SerializerOptions) ??
                    throw new InvalidDataException("配饰条目为空。"));
            }
            return new(new AccessoryConfiguration(entries), AccessorySettingsLoadState.Valid);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return new(AccessoryConfiguration.Empty, AccessorySettingsLoadState.Missing); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Protected($"配饰清单暂时无法读取；原件已保护（{error.GetType().Name}）。", transient: true); }
        catch (Exception error) when (error is JsonException or InvalidDataException
            or ArgumentException or InvalidOperationException)
        { return Protected($"配饰清单无法读取；原件已保护（{error.GetType().Name}）。"); }
    }

    public void Save(string settingsRoot, AccessoryConfiguration configuration,
        IReadOnlyDictionary<string, AccessoryImageAsset>? pendingAssets = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        var previous = Load(settingsRoot);
        if (previous.State == AccessorySettingsLoadState.Protected)
        {
            if (previous.IsTransientReadFailure) throw new IOException("旧配饰清单暂时无法读取，可稍后重试。");
            throw new InvalidDataException("配饰原清单受保护，不能普通覆盖。");
        }
        var oldImages = previous.Configuration.Items.GroupBy(item => item.AssetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var usedImages = configuration.Items.GroupBy(item => item.AssetId, StringComparer.Ordinal).Select(group => group.First());
        foreach (var item in usedImages)
        {
            if (AccessoryItem.IsBuiltInAsset(item.AssetId)) continue;
            if (pendingAssets is not null && pendingAssets.TryGetValue(item.AssetId, out var asset))
            {
                if (asset.AssetId != item.AssetId || asset.PixelWidth != item.PixelWidth || asset.PixelHeight != item.PixelHeight)
                    throw new InvalidDataException("配饰图片资产与条目不一致。");
                var path = GetAssetPath(settingsRoot, item.AssetId);
                EnsureAssetDirectory(settingsRoot);
                if (!File.Exists(path)) assetWriter(path, asset.CopyPngBytes());
                var stored = AccessoryImageAsset.ReadStored(path, item.AssetId);
                ValidateImageDimensions(item, stored);
            }
            else if (oldImages.TryGetValue(item.AssetId, out var original) &&
                original.PixelWidth == item.PixelWidth && original.PixelHeight == item.PixelHeight)
            {
                // A previously saved missing/corrupt image remains isolated, never dropped by unrelated edits.
                continue;
            }
            else ValidateImageDimensions(item, LoadImage(settingsRoot, item.AssetId));
        }
        var text = JsonSerializer.Serialize(new PersistedSettings(CurrentSchemaVersion, configuration.Items), SerializerOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumManifestBytes)
            throw new InvalidDataException("配饰清单超出资源上限。");
        // Recheck immediately before committing: a future/corrupt file appearing during image writes stays protected.
        var beforeCommit = Load(settingsRoot);
        if (beforeCommit.State == AccessorySettingsLoadState.Protected)
        {
            if (beforeCommit.IsTransientReadFailure) throw new IOException("旧配饰清单暂时无法读取，可稍后重试。");
            throw new InvalidDataException("配饰原清单受保护，不能普通覆盖。");
        }
        try { writer.Write(GetManifestPath(settingsRoot), text); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or InvalidOperationException)
        {
            // A writer can throw after an atomic replacement. A confirmed target snapshot is still a successful commit.
            var committed = Load(settingsRoot);
            if (committed.State != AccessorySettingsLoadState.Valid || !committed.Configuration.Equals(configuration)) throw;
        }
    }

    public AccessoryImageAsset LoadImage(string settingsRoot, string assetId)
    {
        EnsureAssetDirectoryIsSafe(settingsRoot);
        return AccessoryImageAsset.ReadStored(GetAssetPath(settingsRoot, assetId), assetId);
    }

    public static string GetManifestPath(string settingsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsRoot);
        return Path.Combine(Path.GetFullPath(settingsRoot), "accessories.json");
    }

    public static string GetAssetPath(string settingsRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsRoot);
        if (!AccessoryItem.IsValidAssetId(assetId) || AccessoryItem.IsBuiltInAsset(assetId))
            throw new InvalidDataException("内部图片引用无效。");
        return Path.Combine(Path.GetFullPath(settingsRoot), "accessory-assets", assetId + ".png");
    }

    private static Dictionary<string, JsonElement> ReadExactObject(JsonElement element, string[] requiredFields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("配饰数据不是对象。");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.TryAdd(property.Name, property.Value)) throw new InvalidDataException("配饰数据存在重复字段。");
        if (fields.Count != requiredFields.Length || requiredFields.Any(name => !fields.ContainsKey(name)))
            throw new InvalidDataException("配饰数据字段缺失或包含未知字段。");
        return fields;
    }

    private static void ValidateImageDimensions(AccessoryItem item, AccessoryImageAsset asset)
    {
        if (item.PixelWidth != asset.PixelWidth || item.PixelHeight != asset.PixelHeight)
            throw new InvalidDataException("配饰图片尺寸与清单不一致。");
    }

    private static void EnsureAssetDirectory(string settingsRoot)
    {
        EnsureAssetDirectoryIsSafe(settingsRoot);
        Directory.CreateDirectory(Path.Combine(Path.GetFullPath(settingsRoot), "accessory-assets"));
    }

    private static void EnsureAssetDirectoryIsSafe(string settingsRoot)
    {
        var directory = Path.Combine(Path.GetFullPath(settingsRoot), "accessory-assets");
        try
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("内部图片目录不能是普通文件或链接。");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void WriteImmutableAsset(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, ".accessory-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            // Only the temporary file created by this operation is removed; committed assets are never deleted.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static AccessorySettingsLoadResult Protected(string warning, bool transient = false) =>
        new(AccessoryConfiguration.Empty, AccessorySettingsLoadState.Protected, warning, transient);

    private sealed record PersistedSettings(int AccessorySchemaVersion, IReadOnlyList<AccessoryItem> Items);
}
