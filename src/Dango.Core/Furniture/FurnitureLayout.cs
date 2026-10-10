namespace Dango.Core.Furniture;

public readonly record struct FurnitureAnchor(double X, double Y);

public sealed record FurniturePlatformInstance(Guid InstanceId, FurnitureAnchor Anchor)
{
    public const string PresetId = "official-static-platform";
    public const int PresetRevision = 1;

    public void Validate()
    {
        if (InstanceId == Guid.Empty) throw new InvalidDataException("家具实例标识为空。");
        LocalFurnitureSceneGeometry.ValidateAnchor(Anchor);
    }
}

/// <summary>Initial furniture only; runtime bodies, camera and physics revisions are never persisted here.</summary>
public sealed record FurnitureLayout(long LayoutVersion, Guid? LastOperationId, FurniturePlatformInstance? Platform)
{
    public static FurnitureLayout Empty { get; } = new(0, null, null);

    public void Validate()
    {
        if (LayoutVersion < 0 || (LayoutVersion == 0 && LastOperationId is not null) ||
            (LayoutVersion > 0 && (!LastOperationId.HasValue || LastOperationId.Value == Guid.Empty)))
            throw new InvalidDataException("布局版本和最近操作标识不一致。");
        Platform?.Validate();
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

    public static void ValidateAnchor(FurnitureAnchor anchor)
    {
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))
            throw new InvalidDataException("家具坐标不是有限值。");
        var left = anchor.X - PlatformWidth / 2;
        var right = anchor.X + PlatformWidth / 2;
        var bottom = anchor.Y - PlatformHeight;
        if (left < Left || right > Right || bottom < Ground || anchor.Y > Ceiling)
            throw new InvalidDataException("家具超出房间边界。");
        // Touching is allowed; overlap must have positive area in both axes.
        if (left < SpawnProtectionRight && right > SpawnProtectionLeft &&
            bottom < SpawnProtectionTop && anchor.Y > SpawnProtectionBottom)
            throw new InvalidDataException("家具穿入固定出生保护区。");
    }
}
