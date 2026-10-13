namespace Dango.Core.Furniture;

public readonly record struct FurnitureAnchor(double X, double Y);

public enum FurnitureItemKind { StaticPlatform = 1, RoundBall = 2 }

/// <summary>Confirmed placement, not a dynamic body's current pose or native ID.</summary>
public sealed record FurnitureItemInstance(Guid InstanceId, FurnitureItemKind Kind, FurnitureAnchor Anchor)
{
    public const string RoundBallPresetId = "official-round-ball";
    public const int RoundBallPresetRevision = 1;
    public string PresetId => Kind switch
    {
        FurnitureItemKind.StaticPlatform => FurniturePlatformInstance.PresetId,
        FurnitureItemKind.RoundBall => RoundBallPresetId,
        _ => throw new InvalidDataException("家具类型不受支持。"),
    };
    public int PresetRevision => Kind switch
    {
        FurnitureItemKind.StaticPlatform => FurniturePlatformInstance.PresetRevision,
        FurnitureItemKind.RoundBall => RoundBallPresetRevision,
        _ => throw new InvalidDataException("家具类型不受支持。"),
    };

    public void Validate(FurnitureLayoutContract? contract = null)
    {
        if (InstanceId == Guid.Empty) throw new InvalidDataException("家具实例标识为空。");
        (contract ?? FurnitureLayoutContract.Trial).ValidateItem(this);
    }
}

/// <summary>A copied, immutable sequence with value equality for file re-read checks.</summary>
public sealed class FurnitureItemCollection : IReadOnlyList<FurnitureItemInstance>, IEquatable<FurnitureItemCollection>
{
    private readonly FurnitureItemInstance[] items;
    internal FurnitureItemCollection(IEnumerable<FurnitureItemInstance> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        items = source.ToArray();
        if (items.Any(item => item is null)) throw new InvalidDataException("家具实例不能为空。");
    }
    public int Count => items.Length;
    public FurnitureItemInstance this[int index] => items[index];
    public IEnumerator<FurnitureItemInstance> GetEnumerator() => ((IEnumerable<FurnitureItemInstance>)items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(FurnitureItemCollection? other) => other is not null && items.AsSpan().SequenceEqual(other.items);
    public override bool Equals(object? obj) => obj is FurnitureItemCollection other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in items) hash.Add(item);
        return hash.ToHashCode();
    }
}

public sealed record FurniturePlatformInstance(Guid InstanceId, FurnitureAnchor Anchor)
{
    public const string PresetId = "official-static-platform";
    public const int PresetRevision = 1;

    public void Validate(FurnitureLayoutContract? contract = null)
    {
        if (InstanceId == Guid.Empty) throw new InvalidDataException("家具实例标识为空。");
        (contract ?? FurnitureLayoutContract.Trial).ValidateAnchor(Anchor);
    }
}

/// <summary>Initial furniture only; runtime bodies, camera and physics revisions are never persisted here.</summary>
public sealed record FurnitureLayout
{
    private FurnitureItemCollection items = new([]);
    public FurnitureLayout(long LayoutVersion, Guid? LastOperationId, FurniturePlatformInstance? Platform)
    {
        this.LayoutVersion = LayoutVersion;
        this.LastOperationId = LastOperationId;
        this.Platform = Platform;
    }
    public long LayoutVersion { get; init; }
    public Guid? LastOperationId { get; init; }
    public FurnitureItemCollection Items => items;
    // Legacy callers have a single-platform view, never a silently selected
    // first item from a multi-item layout.
    public FurniturePlatformInstance? Platform
    {
        get => items.Count == 1 && items[0].Kind == FurnitureItemKind.StaticPlatform
            ? new(items[0].InstanceId, items[0].Anchor) : null;
        init
        {
            if (items.Count > 1 || items.Any(item => item.Kind != FurnitureItemKind.StaticPlatform))
                throw new InvalidOperationException("多家具或圆球布局不能通过旧平台视图替换。");
            items = new(value is null ? [] : [new(value.InstanceId, FurnitureItemKind.StaticPlatform, value.Anchor)]);
        }
    }
    public static FurnitureLayout Empty { get; } = new(0, null, null);

    public static FurnitureLayout ForItems(long layoutVersion, Guid? lastOperationId, IEnumerable<FurnitureItemInstance> items) =>
        new(layoutVersion, lastOperationId, null) { items = new(items) };

    public FurnitureItemInstance? Find(Guid instanceId) => items.FirstOrDefault(item => item.InstanceId == instanceId);

    public void Validate(FurnitureLayoutContract? contract = null)
    {
        if (LayoutVersion < 0 || (LayoutVersion == 0 && LastOperationId is not null) ||
            (LayoutVersion > 0 && (!LastOperationId.HasValue || LastOperationId.Value == Guid.Empty)))
            throw new InvalidDataException("布局版本和最近操作标识不一致。");
        contract ??= FurnitureLayoutContract.Trial;
        if (items.Count > contract.MaximumItems) throw new InvalidDataException("家具数量超出本场景上限。");
        var identifiers = new HashSet<Guid>();
        foreach (var item in items)
        {
            item.Validate(contract);
            if (!identifiers.Add(item.InstanceId)) throw new InvalidDataException("家具实例标识重复。");
        }
        for (var first = 0; first < items.Count; first++)
            for (var second = first + 1; second < items.Count; second++)
                if (contract.ItemsOverlap(items[first], items[second]))
                    throw new InvalidDataException("家具布置锚点的碰撞形状重叠。");
    }
}

/// <summary>The frozen p-unit room contract, independent of window size, DPI and camera.</summary>
public static class LocalFurnitureSceneGeometry
{
    public const double Left = -8;
    public const double Right = 8;
    public const double Ground = -.72;
    public const double Ceiling = 6;
    public const double SpawnX = -5;
    public const double SpawnY = 0;
    public const double SpawnProtectionLeft = -6.3;
    public const double SpawnProtectionRight = -3.7;
    public const double SpawnProtectionBottom = -.72;
    public const double SpawnProtectionTop = 2;
    public const double PlatformWidth = 3.4;
    public const double PlatformHeight = .35;
    public const double PlatformFriction = .72;
    public const double NudgeStep = .05;
    public const int MaxNudgePerSecond = 20;
    public static FurnitureAnchor DefaultAnchor { get; } = new(0, 1.375);

    public static void ValidateAnchor(FurnitureAnchor anchor) => FurnitureLayoutContract.Trial.ValidateAnchor(anchor);
}
