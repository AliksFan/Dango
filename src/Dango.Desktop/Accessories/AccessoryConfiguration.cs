using System.IO;
using System.Text.Json.Serialization;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Accessories;

public enum AccessoryLayer { Behind, Front }
public enum AccessoryDeformationMode { Rigid, SingleChain, Skeleton }

/// <summary>An independent immutable library entry and its local wearing parameters.</summary>
public sealed record AccessoryItem(string Id, string Name, string AssetId, int PixelWidth, int PixelHeight)
{
    public string? ProjectId { get; init; }
    public int ProjectVersion { get; init; }
    public long EditRevision { get; init; }
    public AccessoryDeformationMode DeformationMode { get; init; }
    public AccessoryRigConfiguration? Rig { get; init; }
    public AccessorySkeletonConfiguration? Skeleton { get; init; }
    [JsonIgnore] public bool IsPendingCompletion { get; init; }
    public bool IsEquipped { get; init; } = true;
    public AccessoryLayer Layer { get; init; } = AccessoryLayer.Front;
    public int Order { get; init; }
    public double SizePercent { get; init; } = 100;
    public double BaseAngleDegrees { get; init; }
    public double ImagePivotX { get; init; } = .5;
    public double ImagePivotY { get; init; } = .5;
    public double BodyAnchorX { get; init; } = .5;
    public double BodyAnchorY { get; init; }
    public double ConnectionStrength { get; init; } = 100;
    public double MaxConnectionTravelPercent { get; init; } = 75;
    public double RotationDamping { get; init; } = 60;
    public bool LockRotation { get; init; } = true;
    public double ReturnStrength { get; init; } = 60;
    public double MaxSwingDegrees { get; init; } = 45;
    public bool LimitSwing { get; init; } = true;
    public double GravityInfluence { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 ||
            Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException("配饰 ID 无效。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || Name.Any(char.IsControl))
            throw new InvalidDataException("配饰名称无效。");
        if (!IsValidAssetId(AssetId)) throw new InvalidDataException("配饰图片 ID 无效。");
        if (!Enum.IsDefined(DeformationMode) || EditRevision < 0 ||
            (ProjectId is not null && (!AccessoryEditorProject.IsHash(ProjectId) || ProjectVersion != AccessoryEditorProject.CurrentVersion)) ||
            (ProjectId is null && ProjectVersion != 0)) throw new InvalidDataException("配饰工程引用无效。");
        if (DeformationMode == AccessoryDeformationMode.Skeleton && Skeleton is null)
            throw new InvalidDataException("分组骨架配置缺失。");
        Skeleton?.Validate(PixelWidth, PixelHeight);
        if (DeformationMode == AccessoryDeformationMode.SingleChain && Rig is null)
            throw new InvalidDataException("骨架柔性必须有有效单链。");
        Rig?.Validate(PixelWidth, PixelHeight, ImagePivotX, ImagePivotY);
        AccessoryImageAsset.ValidateDimensions(PixelWidth, PixelHeight);
        if (IsBuiltInAsset(AssetId) && (PixelWidth != 64 || PixelHeight != 64))
            throw new InvalidDataException("内置图片画布必须为 64 × 64。");
        if (!Enum.IsDefined(Layer) || Order < 0) throw new InvalidDataException("配饰图层或排序无效。");
        Range(SizePercent, 10, 400, "尺寸");
        Range(BaseAngleDegrees, -180, 180, "基础角度");
        Range(ImagePivotX, 0, 1, "图片支点 X"); Range(ImagePivotY, 0, 1, "图片支点 Y");
        Range(BodyAnchorX, 0, 1, "身体附着 X"); Range(BodyAnchorY, 0, 1, "身体附着 Y");
        Range(ConnectionStrength, 0, 100, "连接强度");
        Range(MaxConnectionTravelPercent, 0, 200, "连接行程上限");
        Range(RotationDamping, 0, 100, "旋转阻尼"); Range(ReturnStrength, 0, 100, "回正强度");
        Range(MaxSwingDegrees, 0, 180, "最大摆角"); Range(GravityInfluence, 0, 100, "重力影响");
    }

    public static bool IsBuiltInAsset(string? id) => id is "builtin-sprout" or "builtin-top-hat" or "builtin-bowler";

    public static bool IsValidAssetId(string? id) => IsBuiltInAsset(id) ||
        id is { Length: 64 } && id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Range(double value, double minimum, double maximum, string label)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidDataException($"配饰{label}超出有效范围。");
    }
}

/// <summary>A defensive immutable snapshot. Ordering is stable and comparison uses every entry.</summary>
public sealed record AccessoryConfiguration
{
    public const int MaximumEquippedItems = 8;
    public const int MaximumLibraryItems = 4096;
    public static AccessoryConfiguration Empty { get; } = new([]);
    public IReadOnlyList<AccessoryItem> Items { get; }

    public AccessoryConfiguration(IEnumerable<AccessoryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.Take(MaximumLibraryItems + 1).Select(item => item is null ? item! :
            item with { Skeleton = item.Skeleton?.Snapshot() }).ToArray();
        Items = Array.AsReadOnly(snapshot);
        Validate();
    }

    public void Validate()
    {
        if (Items.Count > MaximumLibraryItems) throw new InvalidDataException("配饰库超出资源上限。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var dimensions = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        var equipped = 0;
        foreach (var item in Items)
        {
            if (item is null) throw new InvalidDataException("配饰条目为空。");
            item.Validate();
            if (!ids.Add(item.Id)) throw new InvalidDataException("配饰 ID 重复。");
            if (dimensions.TryGetValue(item.AssetId, out var size) && size != (item.PixelWidth, item.PixelHeight))
                throw new InvalidDataException("同一图片资产不能有不同画布尺寸。");
            dimensions[item.AssetId] = (item.PixelWidth, item.PixelHeight);
            if (item.IsEquipped && ++equipped > MaximumEquippedItems)
                throw new InvalidDataException("最多同时佩戴 8 件配饰。");
        }
    }

    public static AccessoryConfiguration FromLegacyHat(string legacyHatPresetId)
    {
        var selectedAsset = legacyHatPresetId switch
        {
            "top-hat" => "builtin-top-hat",
            "bowler" => "builtin-bowler",
            _ => "builtin-sprout",
        };
        return new(new[] { "builtin-sprout", "builtin-top-hat", "builtin-bowler" }
            .OrderByDescending(asset => asset == selectedAsset)
            .Select((asset, index) => new AccessoryItem(asset + "-entry",
                asset switch { "builtin-top-hat" => "小礼帽", "builtin-bowler" => "圆顶帽", _ => "叶芽" }, asset, 64, 64)
            {
                IsEquipped = asset == selectedAsset,
                Order = index,
                ImagePivotX = .5,
                ImagePivotY = .9375,
            }));
    }

    public bool Equals(AccessoryConfiguration? other) => other is not null && Items.Count == other.Items.Count && Items.Zip(other.Items).All(pair =>
        (pair.First with { Skeleton = null }) == (pair.Second with { Skeleton = null }) &&
        SkeletonEquals(pair.First.Skeleton, pair.Second.Skeleton));

    internal static bool SkeletonEquals(AccessorySkeletonConfiguration? a, AccessorySkeletonConfiguration? b) =>
        a == b;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items) { hash.Add(item with { Skeleton = null }); hash.Add(item.Skeleton); }
        return hash.ToHashCode();
    }
}
