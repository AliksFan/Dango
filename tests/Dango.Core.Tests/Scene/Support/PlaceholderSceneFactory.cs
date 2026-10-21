using Dango.Core.Animation;
using Dango.Core.Assets;
using Dango.Core.Coordinates;

using Dango.Core.Scene;

namespace Dango.Core.Tests.Scene.Support;

internal static class PlaceholderSceneFactory
{
    private const double IslandWidth = 320;
    private const double IslandHeight = 120;

    public static SceneSnapshot Create(
        AssetPackage assetPackage,
        DisplayLayout displayLayout,
        int variantSeed,
        long startedAtMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(assetPackage);
        ArgumentNullException.ThrowIfNull(displayLayout);

        var workArea = displayLayout.Primary.LogicalBounds;
        var islandBounds = displayLayout.PlaceWithinAvailableWorkArea(new LogicalRect(
            workArea.Center.X - (IslandWidth / 2),
            workArea.Bottom - IslandHeight - 80,
            IslandWidth,
            IslandHeight));
        var dangoBounds = displayLayout.PlaceWithinAvailableWorkArea(new LogicalRect(
            islandBounds.Center.X - (assetPackage.Canvas.Width / 2),
            islandBounds.Y - assetPackage.Canvas.Height,
            assetPackage.Canvas.Width,
            assetPackage.Canvas.Height));
        var idleClip = assetPackage.ClipFor(DangoActionState.Idle);
        var clickShapeId = assetPackage.HitShapes.Single(shape => shape.Purpose == HitShapePurpose.Click).Id;
        var dragShapeId = assetPackage.HitShapes.Single(shape => shape.Purpose == HitShapePurpose.Drag).Id;
        var collisionShapeId = assetPackage.HitShapes.Single(shape => shape.Purpose == HitShapePurpose.Collision).Id;

        return new SceneSnapshot(
            SceneSnapshot.CurrentVersion,
            startedAtMilliseconds,
            variantSeed,
            assetPackage.AssetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new DangoSnapshot(
                "dango-placeholder",
                dangoBounds,
                DangoActionState.Idle,
                startedAtMilliseconds,
                variantSeed,
                new LogicalPoint(dangoBounds.Center.X, dangoBounds.Bottom),
                idleClip.Id,
                clickShapeId,
                dragShapeId,
                collisionShapeId,
                "island-grab"),
            new IslandSnapshot(
                "island-placeholder",
                islandBounds,
                new LogicalRect(islandBounds.X + 40, islandBounds.Y + 8, IslandWidth - 80, 24),
                new LogicalRect(islandBounds.X + 24, islandBounds.Y - 12, IslandWidth - 48, 28),
                new LogicalRect(islandBounds.X + 48, islandBounds.Y, IslandWidth - 96, 32),
                new LogicalRect(islandBounds.X, islandBounds.Y + 32, IslandWidth, IslandHeight - 32)));
    }
}
