using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Dangos;

/// <summary>The roster is the only authoritative individual manifest. Legacy files are read only.</summary>
public sealed class DangoRosterStore
{
    public const string RosterRelativePath = "dangos/roster.json";
    public const string InitializationRelativePath = "dangos/initialization.json";
    public const long MaximumManifestBytes = 32L * 1024 * 1024;
    private readonly string root;
    private readonly IAtomicTextWriter writer;
    private bool uncertain;

    public DangoRosterStore(string root, IAtomicTextWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.writer = writer ?? new DurableRosterWriter();
    }

    public DangoRosterReadResult LoadOrInitialize()
    {
        try
        {
            using var held = Acquire();
            var bytes = Read(RosterRelativePath);
            var intentBytes = Read(InitializationRelativePath);
            if (bytes is not null)
            {
                var snapshot = Decode(bytes);
                if (intentBytes is null) throw new InvalidDataException("目录缺少初始化保护记录。");
                var intent = RoomJson.Read<DangoInitialization>(intentBytes); intent.Validate();
                if (snapshot.InitializationId != intent.InitializationId || snapshot.LegacyDangoId != intent.LegacyDangoId)
                    throw new InvalidDataException("目录与初始化保护记录不匹配。");
                uncertain = false;
                return new(snapshot, false, CheckLegacySourceChanges(intent));
            }
            if (intentBytes is not null || uncertain)
                return Protected("已有初始化意图但团子目录缺失；已保护现场，不能重新导入。");
            return Initialize();
        }
        catch (Exception error) when (IsStorageError(error))
        { return Protected("团子资料不可读或无法识别；原件已保护（" + error.GetType().Name + "）。"); }
    }

    private string? CheckLegacySourceChanges(DangoInitialization intent)
    {
        var changed = new List<string>();
        var unreadable = new List<string>();
        // settings.json still contains actively saved device preferences. Comparing that whole file would
        // report normal product saves as legacy conflicts; compatibility hats in it are not checked here.
        foreach (var source in new[] { (Path: "appearance.json", Label: "外观", Hash: intent.AppearanceSha256),
                     (Path: "accessories.json", Label: "配饰", Hash: intent.AccessoriesSha256) })
        {
            try
            {
                if (Hash(Read(source.Path)) != source.Hash) changed.Add(source.Label);
            }
            catch (Exception error) when (IsStorageError(error)) { unreadable.Add(source.Label); }
        }
        var warnings = new List<string>();
        if (changed.Count > 0)
            warnings.Add("旧版本的" + string.Join("／", changed) + "存档在承接后发生变化，修改尚未合并；当前团子资料保持不变。");
        if (unreadable.Count > 0)
            warnings.Add("旧" + string.Join("／", unreadable) + "来源暂不可读，无法核对旧版本修改；当前团子资料已正常载入，未合并旧来源。");
        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    private DangoRosterReadResult Initialize()
    {
        var appearanceBytes = Read("appearance.json");
        var accessoryBytes = Read("accessories.json");
        var settingsBytes = Read("settings.json");
        var hasLegacy = appearanceBytes is not null || accessoryBytes is not null || settingsBytes is not null;
        var appearance = DangoAppearanceConfiguration.Default;
        var requiresPreview = false;
        if (appearanceBytes is not null)
        {
            var result = new AppearanceSettingsStore().LoadOrDefault(PathFor("appearance.json"));
            if (result.PreserveOriginal) throw new InvalidDataException("旧外观受保护。");
            appearance = result.Settings.Configuration;
            requiresPreview = result.SourceBodyPhysicsModel == DangoBodyMaterial.LegacyModelVersion;
        }
        var hat = "sprout";
        if (settingsBytes is not null)
        {
            ValidateLegacySettings(settingsBytes);
            var result = new ControlCenterWindowSettingsStore().LoadOrDefault(PathFor("settings.json"));
            if (result.PreserveOriginal) throw new InvalidDataException("旧设置受保护。");
            hat = result.Settings.HatPresetId;
        }
        var accessories = AccessoryConfiguration.Empty;
        if (accessoryBytes is not null)
        {
            var result = new AccessorySettingsStore().Load(root);
            if (result.State != AccessorySettingsLoadState.Valid) throw new InvalidDataException("旧配饰受保护。");
            accessories = result.Configuration;
        }
        else if (hasLegacy) accessories = AccessoryConfiguration.FromLegacyHat(hat);
        var intent = new DangoInitialization(1, Guid.NewGuid(), hasLegacy ? Guid.NewGuid() : null,
            Hash(appearanceBytes), Hash(accessoryBytes), Hash(settingsBytes));
        intent.Validate();
        var profiles = hasLegacy ? new[] { new DangoProfile(intent.LegacyDangoId!.Value, null, appearance, accessories, requiresPreview) } : [];
        var snapshot = new DangoRosterSnapshot(1, intent.InitializationId, [intent.LegacyDangoId, null, null], profiles,
            intent.LegacyDangoId, Templates(accessories), OrdinaryRoomNameSource.DangoName, intent.LegacyDangoId,
            intent.InitializationId, Fingerprint(new { Action = "Initialize", intent.InitializationId }));
        // Persist and observe the permanent intent before attempting publication. A later missing roster is protected.
        var intentResult = WriteObserved(InitializationRelativePath, null, RoomJson.Write(intent));
        if (intentResult.State != FurnitureCommitState.Committed) return Protected(intentResult.Warning!);
        if (!Same(appearanceBytes, Read("appearance.json")) || !Same(accessoryBytes, Read("accessories.json")) ||
            !Same(settingsBytes, Read("settings.json"))) return Protected("旧来源在初始化期间发生变化；已保护现场。");
        var resultWrite = WriteObserved(RosterRelativePath, null, Encode(snapshot));
        if (resultWrite.State != FurnitureCommitState.Committed)
        { uncertain = resultWrite.State == FurnitureCommitState.Unknown; return Protected(resultWrite.Warning!); }
        return new(snapshot, false);
    }

    public DangoRosterWriteResult Create(DangoRosterSnapshot expected, int slotIndex, string name,
        DangoAppearanceConfiguration appearance, Guid operationId) => Mutate(expected, operationId,
        new { Action = "Create", slotIndex, name, Appearance = DangoAppearanceDocument.From(appearance) }, current =>
        {
            if (slotIndex is < 0 or > 2 || current.Slots[slotIndex] is not null)
                throw new InvalidDataException("创建目标栏位无效或已占用。");
            // Idempotence is resolved before this callback; every new creation gets a fresh identity.
            var id = Guid.NewGuid();
            var slots = current.Slots.ToArray(); slots[slotIndex] = id;
            var profile = new DangoProfile(id, DangoRosterSnapshot.NormalizeName(name), appearance, AccessoryConfiguration.Empty);
            return Copy(current, slots: slots, profiles: current.Profiles.Append(profile), selectedId: id, replaceSelected: true);
        });

    public DangoRosterWriteResult Select(DangoRosterSnapshot expected, Guid? id, Guid operationId) =>
        Mutate(expected, operationId, new { Action = "Select", id }, current =>
        {
            if (id.HasValue) Require(current, id.Value);
            return Copy(current, selectedId: id, replaceSelected: true);
        });

    public DangoRosterWriteResult Rename(DangoRosterSnapshot expected, Guid id, string name, Guid operationId) =>
        Mutate(expected, operationId, new { Action = "Rename", id, name }, current =>
            Replace(current, Require(current, id) with { Name = DangoRosterSnapshot.NormalizeName(name) }));

    public DangoRosterWriteResult SaveAppearance(DangoRosterSnapshot expected, Guid id,
        DangoAppearanceConfiguration appearance, Guid operationId) =>
        Mutate(expected, operationId, new { Action = "SaveAppearance", id, Appearance = DangoAppearanceDocument.From(appearance) },
            current => Replace(current, Require(current, id) with { Appearance = appearance, RequiresBodyPreview = false }));

    public DangoRosterWriteResult SaveAccessories(DangoRosterSnapshot expected, Guid id, AccessoryConfiguration configuration,
        IReadOnlyDictionary<string, AccessoryImageAsset>? pendingAssets, Guid operationId) =>
        Mutate(expected, operationId, new { Action = "SaveAccessories", id,
            Items = (configuration ?? throw new ArgumentNullException(nameof(configuration))).Items }, current =>
        {
            Require(current, id); configuration.Validate();
            var ids = configuration.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var removed = current.Library.Items.Where(item => !ids.Contains(item.Id)).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (current.Profiles.Where(p => p.Id != id).Any(p => p.Accessories.Items.Any(item => removed.Contains(item.Id))))
                throw new InvalidDataException("其他团子仍引用该配饰，不能删除共享模板。");
            ValidateAndWriteAssets(current, configuration, pendingAssets);
            var profile = Require(current, id) with { Accessories = configuration };
            return Copy(current, profiles: current.Profiles.Select(p => p.Id == id ? profile : p), library: Templates(configuration));
        });

    public DangoRosterWriteResult Delete(DangoRosterSnapshot expected, Guid id, Guid operationId) =>
        Mutate(expected, operationId, new { Action = "Delete", id }, current =>
        {
            Require(current, id);
            return Copy(current, slots: current.Slots.Select(slot => slot == id ? null : slot),
                profiles: current.Profiles.Where(p => p.Id != id), selectedId: current.SelectedId == id ? null : current.SelectedId,
                replaceSelected: true);
        });

    public DangoRosterWriteResult SetNameSource(DangoRosterSnapshot expected, OrdinaryRoomNameSource source, Guid operationId) =>
        Mutate(expected, operationId, new { Action = "SetNameSource", source }, current => Copy(current, nameSource: source));

    private DangoRosterWriteResult Mutate(DangoRosterSnapshot expected, Guid operationId, object operation,
        Func<DangoRosterSnapshot, DangoRosterSnapshot> change)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(expected);
            if (operationId == Guid.Empty) throw new InvalidDataException("操作身份不能为空。");
            if (uncertain) return new(FurnitureCommitState.Unknown, Warning: "上次保存待核对，请重新读取资料。");
            using var held = Acquire();
            var baseline = Read(RosterRelativePath) ?? throw new InvalidDataException("目录缺失，禁止重新初始化。");
            var current = Decode(baseline);
            var intent = RoomJson.Read<DangoInitialization>(Read(InitializationRelativePath) ?? throw new InvalidDataException("初始化保护记录缺失。"));
            intent.Validate();
            if (intent.InitializationId != current.InitializationId || intent.LegacyDangoId != current.LegacyDangoId)
                throw new InvalidDataException("初始化身份不匹配。");
            var fingerprint = Fingerprint(new { ExpectedRevision = expected.Revision, Operation = operation });
            if (current.LastOperationId == operationId)
                return current.OperationFingerprint == fingerprint ? new(FurnitureCommitState.Committed, current) :
                    new(FurnitureCommitState.NotCommitted, current, "同一操作身份不能改变内容。");
            if (current.Revision != expected.Revision || !Encode(expected).AsSpan().SequenceEqual(Encode(current)))
                return new(FurnitureCommitState.NotCommitted, current, "资料已变化，请重新读取后重试。");
            var candidate = change(current);
            candidate = new(checked(current.Revision + 1), operationId, candidate.Slots, candidate.Profiles, candidate.SelectedId,
                candidate.Library, candidate.NameSource, candidate.LegacyDangoId, current.InitializationId, fingerprint);
            var result = WriteObserved(RosterRelativePath, baseline, Encode(candidate));
            if (result.State == FurnitureCommitState.Unknown) uncertain = true;
            return result with { Snapshot = result.State == FurnitureCommitState.Committed ? candidate : current };
        }
        catch (Exception error) when (IsStorageError(error))
        { return new(FurnitureCommitState.NotCommitted, Warning: "未保存；原件保留（" + error.GetType().Name + "）。"); }
    }

    private void ValidateAndWriteAssets(DangoRosterSnapshot current, AccessoryConfiguration configuration,
        IReadOnlyDictionary<string, AccessoryImageAsset>? pendingAssets)
    {
        var known = current.Library.Items.Concat(current.Profiles.SelectMany(p => p.Accessories.Items))
            .Select(item => (item.AssetId, item.PixelWidth, item.PixelHeight)).ToHashSet();
        foreach (var item in configuration.Items.GroupBy(item => item.AssetId).Select(group => group.First()))
        {
            if (AccessoryItem.IsBuiltInAsset(item.AssetId)) continue;
            AccessoryImageAsset? image = null;
            if (pendingAssets is not null && pendingAssets.TryGetValue(item.AssetId, out var pending))
            {
                if (pending.AssetId != item.AssetId || pending.PixelWidth != item.PixelWidth || pending.PixelHeight != item.PixelHeight)
                    throw new InvalidDataException("待保存图片与条目不一致。");
                var relative = "accessory-assets/" + item.AssetId + ".png";
                var path = PathFor(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                path = PathFor(relative);
                if (!File.Exists(path))
                {
                    var temporary = PathFor("accessory-assets/.roster-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { output.Write(pending.CopyPngBytes()); output.Flush(true); }
                        _ = AccessoryImageAsset.ReadStored(temporary, item.AssetId);
                        File.Move(temporary, path, false);
                    }
                    finally { File.Delete(temporary); }
                }
                image = new AccessorySettingsStore().LoadImage(root, item.AssetId);
            }
            else if (!known.Contains((item.AssetId, item.PixelWidth, item.PixelHeight)))
                image = new AccessorySettingsStore().LoadImage(root, item.AssetId);
            if (image is not null && (image.PixelWidth != item.PixelWidth || image.PixelHeight != item.PixelHeight))
                throw new InvalidDataException("图片尺寸不一致。");
        }
    }

    private DangoRosterWriteResult WriteObserved(string relative, byte[]? baseline, byte[] candidate)
    {
        if (candidate.LongLength > MaximumManifestBytes) throw new InvalidDataException("目录超过资源预算。");
        if (!Same(Read(relative), baseline)) return new(FurnitureCommitState.Unknown, Warning: "存档已被外部修改，保存结果待核对。");
        string? writeFailure = null;
        try { writer.Write(PathFor(relative), Encoding.UTF8.GetString(candidate)); }
        catch (Exception error) when (IsStorageError(error))
        { writeFailure = $"{error.GetType().Name}, HRESULT 0x{error.HResult:X8}"; }
        try
        {
            var observed = Read(relative);
            if (Same(observed, candidate)) return new(FurnitureCommitState.Committed);
            return Same(observed, baseline) ? new(FurnitureCommitState.NotCommitted, Warning: "保存未完成；原件保留" +
                    (writeFailure is null ? "。" : "（" + writeFailure + "）。")) :
                new(FurnitureCommitState.Unknown, Warning: "保存结果待核对，禁止继续覆盖。");
        }
        catch (Exception error) when (IsStorageError(error))
        { return new(FurnitureCommitState.Unknown, Warning: "保存后无法读取结果，请重新读取核对。"); }
    }

    private IDisposable Acquire()
    {
        var path = PathFor("dangos/roster.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(PathFor("dangos/roster.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private string PathFor(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("路径越界。");
        var current = path;
        while (current.Length >= root.Length)
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || current == path && (attributes & FileAttributes.Directory) != 0)
                    throw new InvalidDataException("存档路径不能是链接或目录。");
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current)!;
        }
        return path;
    }

    private byte[]? Read(string relative)
    {
        try
        {
            using var input = new FileStream(PathFor(relative), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 1 or > MaximumManifestBytes) throw new InvalidDataException("存档大小无效。");
            var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); return bytes;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private static DangoRosterSnapshot Decode(byte[] bytes)
    {
        // Enforce complete individual item snapshots, including optional fields of the legacy manifest.
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.TryGetProperty("library", out var library)) CheckItems(library);
        if (json.RootElement.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array)
            foreach (var profile in profiles.EnumerateArray())
                if (profile.ValueKind == JsonValueKind.Object && profile.TryGetProperty("accessories", out var items)) CheckItems(items);
        return RoomJson.Read<DangoRosterDocument>(bytes).ToSnapshot();
    }

    private static void CheckItems(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array) throw new InvalidDataException("配饰列表无效。");
        string[] required = ["id", "name", "assetId", "pixelWidth", "pixelHeight", "isEquipped", "layer", "order", "sizePercent",
            "baseAngleDegrees", "imagePivotX", "imagePivotY", "bodyAnchorX", "bodyAnchorY", "softness", "allowFolding",
            "maxDeformationPercent", "connectionStrength", "maxConnectionTravelPercent", "rotationDamping", "lockRotation",
            "returnStrength", "maxSwingDegrees", "limitSwing", "gravityInfluence"];
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != required.Length ||
                required.Any(field => !item.TryGetProperty(field, out _))) throw new InvalidDataException("配饰快照字段不完整。");
    }

    private static void ValidateLegacySettings(byte[] bytes)
    {
        // The old loader accepts unknown members. Migration must protect them rather than silently defaulting.
        using var json = JsonDocument.Parse(bytes);
        var element = json.RootElement;
        string[] fields = ["width", "height", "schemaVersion", "desktopLayeringExperiment", "keyboardControl", "displayMode",
            "sceneWindow", "showDebugSkeleton", "interactionEnabled", "hatPresetId", "introSeen", "selectedPreset"];
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(p => !fields.Contains(p.Name)))
            throw new InvalidDataException("旧设置含未知字段。");
        CheckDuplicateFields(element);
        CheckLegacyObject(element, "desktopLayeringExperiment", ["dangoLayer", "furnitureLayers"]);
        CheckLegacyObject(element, "keyboardControl", ["isEnabled", "scheme"]);
        CheckLegacyObject(element, "sceneWindow", ["width", "height", "monitorKey", "normalizedCenterX", "normalizedCenterY"]);
        if (element.TryGetProperty("displayMode", out var mode) && mode.ValueKind != JsonValueKind.Null &&
            (mode.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(mode.GetString()) &&
                !new[] { "WorkerW", "Overlay", "Wallpaper", "Window" }.Contains(mode.GetString(), StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("旧显示模式无法识别。");
        if (element.TryGetProperty("hatPresetId", out var hat) && hat.ValueKind != JsonValueKind.Null && (hat.ValueKind != JsonValueKind.String ||
            hat.GetString() is not ("sprout" or "top-hat" or "bowler"))) throw new InvalidDataException("旧帽型无效。");
    }

    private static void CheckLegacyObject(JsonElement root, string field, string[] allowed)
    {
        if (!root.TryGetProperty(field, out var element) || element.ValueKind == JsonValueKind.Null) return;
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(p => !allowed.Contains(p.Name)))
            throw new InvalidDataException("旧设置含未知嵌套字段。");
    }

    private static void CheckDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            { if (!fields.Add(p.Name)) throw new InvalidDataException("字段重复。"); CheckDuplicateFields(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckDuplicateFields(item);
    }

    private static DangoRosterSnapshot Copy(DangoRosterSnapshot s, IEnumerable<Guid?>? slots = null,
        IEnumerable<DangoProfile>? profiles = null, Guid? selectedId = null, bool replaceSelected = false,
        AccessoryConfiguration? library = null, OrdinaryRoomNameSource? nameSource = null) =>
        new(s.Revision, s.LastOperationId, slots ?? s.Slots, profiles ?? s.Profiles, replaceSelected ? selectedId : s.SelectedId,
            library ?? s.Library, nameSource ?? s.NameSource, s.LegacyDangoId, s.InitializationId, s.OperationFingerprint);
    private static DangoRosterSnapshot Replace(DangoRosterSnapshot s, DangoProfile p) => Copy(s, profiles: s.Profiles.Select(old => old.Id == p.Id ? p : old));
    private static DangoProfile Require(DangoRosterSnapshot s, Guid id) => s.Find(id) ?? throw new InvalidDataException("团子不存在。");
    private static AccessoryConfiguration Templates(AccessoryConfiguration config) => new(config.Items.Select(item => item with { IsEquipped = false }));
    private static byte[] Encode(DangoRosterSnapshot s) => RoomJson.Write(DangoRosterDocument.From(s));
    private static string? Hash(byte[]? bytes) => bytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Fingerprint(object value) => Hash(RoomJson.Write(value))!;
    private static bool Same(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static bool IsStorageError(Exception e) => RoomFileStore.IsStorageError(e) || e is FormatException or ArgumentNullException;
    private static DangoRosterReadResult Protected(string warning) => new(null, true, warning);

    private sealed class DurableRosterWriter : IAtomicTextWriter
    {
        public void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(Encoding.UTF8.GetBytes(content)); output.Flush(true); }
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
    }
}
