using Dango.Core.Animation;
using Dango.Core.Coordinates;

namespace Dango.Core.Scene;

public sealed record DangoSnapshot(
    string Id,
    LogicalRect VisualBounds,
    DangoActionState State,
    long StateStartedAtMilliseconds,
    int VariantSeed,
    LogicalPoint GroundContactPoint,
    string ClipId,
    string ClickShapeId,
    string DragShapeId,
    string CollisionShapeId,
    string IslandGrabAnchorId);

public sealed record IslandSnapshot(
    string Id,
    LogicalRect VisualBounds,
    LogicalRect StandingSurface,
    LogicalRect GrabbableEdge,
    LogicalRect AllowedLandingArea,
    LogicalRect OcclusionBounds);

public sealed record SceneSnapshot(
    int SnapshotVersion,
    long StartedAtMilliseconds,
    int VariantSeed,
    string AssetVersion,
    DangoSnapshot Dango,
    IslandSnapshot Island)
{
    public const int CurrentVersion = 1;
}

public static class SceneLayoutMigrator
{
    public static SceneSnapshot Migrate(SceneSnapshot snapshot, DisplayLayout availableDisplays)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(availableDisplays);

        var dangoBounds = availableDisplays.PlaceWithinAvailableWorkArea(snapshot.Dango.VisualBounds);
        var dangoOffset = new LogicalVector(
            dangoBounds.X - snapshot.Dango.VisualBounds.X,
            dangoBounds.Y - snapshot.Dango.VisualBounds.Y);
        var islandBounds = availableDisplays.PlaceWithinAvailableWorkArea(snapshot.Island.VisualBounds);
        var islandOffset = new LogicalVector(
            islandBounds.X - snapshot.Island.VisualBounds.X,
            islandBounds.Y - snapshot.Island.VisualBounds.Y);

        return snapshot with
        {
            Dango = snapshot.Dango with
            {
                VisualBounds = dangoBounds,
                GroundContactPoint = snapshot.Dango.GroundContactPoint + dangoOffset,
            },
            Island = snapshot.Island with
            {
                VisualBounds = islandBounds,
                StandingSurface = snapshot.Island.StandingSurface.Translate(islandOffset),
                GrabbableEdge = snapshot.Island.GrabbableEdge.Translate(islandOffset),
                AllowedLandingArea = snapshot.Island.AllowedLandingArea.Translate(islandOffset),
                OcclusionBounds = snapshot.Island.OcclusionBounds.Translate(islandOffset),
            },
        };
    }
}
