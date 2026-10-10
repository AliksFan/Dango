namespace Dango.Core.Furniture;

/// <summary>Frozen layout identity and geometry; independent of camera, DPI and runtime origin.</summary>
public sealed class FurnitureLayoutContract
{
    public static FurnitureLayoutContract Trial { get; } = new(
        "local-single-window", "static-platform-p-v1", "furniture-layout.json",
        surfaceWidth: 16, surfaceHeight: 6.72,
        left: LocalFurnitureSceneGeometry.Left, right: LocalFurnitureSceneGeometry.Right,
        ground: LocalFurnitureSceneGeometry.Ground, ceiling: LocalFurnitureSceneGeometry.Ceiling,
        spawnLeft: LocalFurnitureSceneGeometry.SpawnProtectionLeft, spawnRight: LocalFurnitureSceneGeometry.SpawnProtectionRight,
        spawnBottom: LocalFurnitureSceneGeometry.SpawnProtectionBottom, spawnTop: LocalFurnitureSceneGeometry.SpawnProtectionTop,
        platformWidth: LocalFurnitureSceneGeometry.PlatformWidth, platformHeight: LocalFurnitureSceneGeometry.PlatformHeight,
        platformFriction: LocalFurnitureSceneGeometry.PlatformFriction, defaultAnchor: LocalFurnitureSceneGeometry.DefaultAnchor,
        nudgeStep: LocalFurnitureSceneGeometry.NudgeStep, maxNudgePerSecond: LocalFurnitureSceneGeometry.MaxNudgePerSecond);

    public static FurnitureLayoutContract OrdinaryRoom { get; } = new(
        "local-ordinary-room", "room-floor-height-v1", "rooms/local-ordinary-room/furniture-layout.json",
        surfaceWidth: 2160, surfaceHeight: 360,
        left: 24, right: 2136, ground: 0, ceiling: 360,
        spawnLeft: 900, spawnRight: 1260, spawnBottom: 0, spawnTop: 200,
        platformWidth: 272, platformHeight: 21, platformFriction: .72,
        defaultAnchor: new(1440, 120), nudgeStep: 4, maxNudgePerSecond: 20);

    private FurnitureLayoutContract(string sceneId, string coordinateSpace, string layoutRelativePath,
        double surfaceWidth, double surfaceHeight, double left, double right, double ground, double ceiling,
        double spawnLeft, double spawnRight, double spawnBottom, double spawnTop,
        double platformWidth, double platformHeight, double platformFriction,
        FurnitureAnchor defaultAnchor, double nudgeStep, int maxNudgePerSecond)
    {
        SceneId = sceneId; CoordinateSpace = coordinateSpace; LayoutRelativePath = layoutRelativePath;
        SurfaceWidth = surfaceWidth; SurfaceHeight = surfaceHeight;
        Left = left; Right = right; Ground = ground; Ceiling = ceiling;
        SpawnProtectionLeft = spawnLeft; SpawnProtectionRight = spawnRight;
        SpawnProtectionBottom = spawnBottom; SpawnProtectionTop = spawnTop;
        PlatformWidth = platformWidth; PlatformHeight = platformHeight; PlatformFriction = platformFriction;
        DefaultAnchor = defaultAnchor; NudgeStep = nudgeStep; MaxNudgePerSecond = maxNudgePerSecond;
    }

    public string SceneId { get; }
    public string CoordinateSpace { get; }
    public int SceneGeometryRevision => 1;
    public string LayoutRelativePath { get; }
    public double SurfaceWidth { get; }
    public double SurfaceHeight { get; }
    public double Left { get; }
    public double Right { get; }
    public double Ground { get; }
    public double Ceiling { get; }
    public double SpawnProtectionLeft { get; }
    public double SpawnProtectionRight { get; }
    public double SpawnProtectionBottom { get; }
    public double SpawnProtectionTop { get; }
    public double PlatformWidth { get; }
    public double PlatformHeight { get; }
    public double PlatformFriction { get; }
    public FurnitureAnchor DefaultAnchor { get; }
    public double NudgeStep { get; }
    public int MaxNudgePerSecond { get; }

    public void ValidateAnchor(FurnitureAnchor anchor)
    {
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))
            throw new InvalidDataException("家具坐标不是有限值。");
        var left = anchor.X - PlatformWidth / 2;
        var right = anchor.X + PlatformWidth / 2;
        var bottom = anchor.Y - PlatformHeight;
        if (left < Left || right > Right || bottom < Ground || anchor.Y > Ceiling)
            throw new InvalidDataException("家具超出房间边界。");
        // The anchor is the top-center; touching a protected edge is allowed.
        if (left < SpawnProtectionRight && right > SpawnProtectionLeft &&
            bottom < SpawnProtectionTop && anchor.Y > SpawnProtectionBottom)
            throw new InvalidDataException("家具穿入固定出生保护区。");
    }
}
