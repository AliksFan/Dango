using System.Globalization;
using System.IO;
using Dango.Desktop.Accessories;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.Dangos;

public enum OrdinaryRoomNameSource { DangoName, AccountDisplayName }

public sealed record DangoProfile(Guid Id, string? Name, DangoAppearanceConfiguration Appearance,
    AccessoryConfiguration Accessories, bool RequiresBodyPreview = false)
{
    public Guid Id { get; init; } = Id != Guid.Empty ? Id : throw new InvalidDataException("团子身份不能为空。");
    public string? Name { get; init; } = Name is null || DangoRosterSnapshot.NormalizeName(Name) == Name ?
        Name : throw new InvalidDataException("团子名字不能带首尾空白。");
    public DangoAppearanceConfiguration Appearance { get; init; } = Appearance ?? throw new ArgumentNullException(nameof(Appearance));
    public AccessoryConfiguration Accessories { get; init; } = Accessories ?? throw new ArgumentNullException(nameof(Accessories));
}

/// <summary>Three fixed positions and independent immutable individual snapshots.</summary>
public sealed class DangoRosterSnapshot
{
    public long Revision { get; }
    public Guid LastOperationId { get; }
    public IReadOnlyList<Guid?> Slots { get; }
    public IReadOnlyList<DangoProfile> Profiles { get; }
    public Guid? SelectedId { get; }
    public AccessoryConfiguration Library { get; }
    public OrdinaryRoomNameSource NameSource { get; }
    public Guid? LegacyDangoId { get; }
    internal Guid InitializationId { get; }
    internal string OperationFingerprint { get; }

    internal DangoRosterSnapshot(long revision, Guid lastOperationId, IEnumerable<Guid?> slots,
        IEnumerable<DangoProfile> profiles, Guid? selectedId, AccessoryConfiguration library,
        OrdinaryRoomNameSource nameSource, Guid? legacyDangoId, Guid initializationId, string operationFingerprint)
    {
        Revision = revision; LastOperationId = lastOperationId;
        Slots = Array.AsReadOnly(slots.Take(4).ToArray());
        Profiles = Array.AsReadOnly(profiles.Take(4).ToArray());
        SelectedId = selectedId; Library = library; NameSource = nameSource; LegacyDangoId = legacyDangoId;
        InitializationId = initializationId; OperationFingerprint = operationFingerprint;
        Validate();
    }

    public DangoProfile? Find(Guid id) => Profiles.FirstOrDefault(profile => profile.Id == id);

    public AccessoryConfiguration EffectiveAccessories(Guid id)
    {
        var profile = Find(id) ?? throw new InvalidDataException("团子不存在。");
        var own = profile.Accessories.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return new(Library.Items.Select(template => own.TryGetValue(template.Id, out var item)
            ? item : template with { IsEquipped = false }));
    }

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Trim();
        if (normalized.Length == 0 || normalized.Any(c => char.IsControl(c) ||
                char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) ||
            StringInfo.ParseCombiningCharacters(normalized).Length > 24)
            throw new InvalidDataException("名字须为 1–24 个文字，且不能含控制字符或换行。");
        for (var index = 0; index < normalized.Length; index++)
        {
            if (!char.IsSurrogate(normalized[index])) continue;
            if (!char.IsHighSurrogate(normalized[index]) || index + 1 >= normalized.Length || !char.IsLowSurrogate(normalized[index + 1]))
                throw new InvalidDataException("名字含无效 Unicode 文字。");
            index++;
        }
        return normalized;
    }

    internal void Validate()
    {
        if (Revision < 1 || LastOperationId == Guid.Empty || InitializationId == Guid.Empty ||
            OperationFingerprint is not { Length: 64 } ||
            OperationFingerprint.Any(c => !char.IsAsciiHexDigit(c)) || !Enum.IsDefined(NameSource))
            throw new InvalidDataException("团子目录版本或操作身份无效。");
        if (Slots.Count != 3 || Profiles.Count > 3 || Profiles.Any(p => p is null))
            throw new InvalidDataException("团子目录必须有三个固定栏位。");
        var ids = Profiles.Select(p => p.Id).ToHashSet();
        var occupied = Slots.Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        if (ids.Contains(Guid.Empty) || ids.Count != Profiles.Count || occupied.Distinct().Count() != occupied.Length ||
            !ids.SetEquals(occupied) || SelectedId is { } selected && !ids.Contains(selected) || LegacyDangoId == Guid.Empty)
            throw new InvalidDataException("团子身份与栏位引用无效。");
        ArgumentNullException.ThrowIfNull(Library);
        Library.Validate();
        if (Library.Items.Any(item => item.IsEquipped)) throw new InvalidDataException("共享模板不能携带穿戴状态。");
        var templateIds = Library.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            if (profile.Name is null)
            { if (profile.Id != LegacyDangoId) throw new InvalidDataException("新建团子必须起名。"); }
            else if (NormalizeName(profile.Name) != profile.Name) throw new InvalidDataException("团子名字无效。");
            ArgumentNullException.ThrowIfNull(profile.Appearance);
            ArgumentNullException.ThrowIfNull(profile.Accessories);
            profile.Accessories.Validate();
            if (profile.Accessories.Items.Any(item => !templateIds.Contains(item.Id)))
                throw new InvalidDataException("个体配饰缺少共享模板。");
        }
        // All snapshots of one content-addressed image must agree on its immutable dimensions.
        var dimensions = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var item in Library.Items.Concat(Profiles.SelectMany(p => p.Accessories.Items)))
        {
            if (dimensions.TryGetValue(item.AssetId, out var size) && size != (item.PixelWidth, item.PixelHeight))
                throw new InvalidDataException("同一图片资产不能有不同尺寸。");
            dimensions[item.AssetId] = (item.PixelWidth, item.PixelHeight);
        }
    }
}

public sealed record DangoRosterReadResult(DangoRosterSnapshot? Snapshot, bool IsProtected, string? Warning = null);
public sealed record DangoRosterWriteResult(FurnitureCommitState State, DangoRosterSnapshot? Snapshot = null, string? Warning = null);
