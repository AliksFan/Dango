using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;

namespace Dango.Desktop.DesktopHost;

public sealed class DesktopSceneSession
{
    private SingleDangoMotionController? controller;
    private LogicalRect? workArea;

    public SingleDangoMotionController? Controller => controller;

    public SceneSnapshot? Snapshot => controller?.Snapshot;

    public SceneSnapshot AttachSurface(LogicalRect targetWorkArea, long nowMilliseconds)
    {
        if (controller is null)
        {
            var scene = CreateInitialScene(targetWorkArea, nowMilliseconds);
            controller = new SingleDangoMotionController(scene, targetWorkArea);
            workArea = targetWorkArea;
            return scene;
        }

        var sourceWorkArea = workArea ?? throw new InvalidOperationException("场景工作区状态不完整。");
        if (sourceWorkArea != targetWorkArea)
        {
            var mapped = MapSnapshot(controller.Snapshot, sourceWorkArea, targetWorkArea);
            controller.Rehost(mapped, targetWorkArea);
            workArea = targetWorkArea;
        }

        return controller.Snapshot;
    }

    public void Exit(long nowMilliseconds)
    {
        if (controller is { IsExited: false })
        {
            controller.Exit(nowMilliseconds);
        }
    }

    private static SceneSnapshot CreateInitialScene(LogicalRect workArea, long nowMilliseconds)
    {
        const double dangoWidth = 160;
        const double dangoHeight = 120;
        var islandBounds = new LogicalRect(
            Math.Max(workArea.X, workArea.Right - 440),
            Math.Max(workArea.Y, workArea.Bottom - 130),
            360,
            100).ClampInside(workArea);
        var dangoBounds = new LogicalRect(
            Math.Max(workArea.X, islandBounds.X - dangoWidth - 24),
            Math.Max(workArea.Y, workArea.Bottom - dangoHeight),
            dangoWidth,
            dangoHeight).ClampInside(workArea);

        return new SceneSnapshot(
            SceneSnapshot.CurrentVersion,
            nowMilliseconds,
            17,
            "p2d-006",
            new DangoSnapshot(
                "primary-dango",
                dangoBounds,
                DangoActionState.Idle,
                nowMilliseconds,
                17,
                new LogicalPoint(dangoBounds.Center.X, dangoBounds.Bottom),
                "idle",
                "click",
                "drag",
                "collision",
                "island-grab"),
            new IslandSnapshot(
                "primary-island",
                islandBounds,
                new LogicalRect(islandBounds.X + 40, islandBounds.Y, 280, 20),
                new LogicalRect(islandBounds.X + 24, islandBounds.Y, 312, 20),
                new LogicalRect(islandBounds.X + 48, islandBounds.Y, 264, 30),
                new LogicalRect(islandBounds.X, islandBounds.Y + 30, 360, 70)));
    }

    private static SceneSnapshot MapSnapshot(
        SceneSnapshot snapshot,
        LogicalRect source,
        LogicalRect target)
    {
        var dangoBounds = MapBounds(snapshot.Dango.VisualBounds, source, target);
        var islandBounds = MapBounds(snapshot.Island.VisualBounds, source, target);
        var dangoOffset = new LogicalVector(
            dangoBounds.X - snapshot.Dango.VisualBounds.X,
            dangoBounds.Y - snapshot.Dango.VisualBounds.Y);
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

    private static LogicalRect MapBounds(LogicalRect bounds, LogicalRect source, LogicalRect target)
    {
        var normalizedX = Math.Clamp((bounds.Center.X - source.X) / source.Width, 0, 1);
        var normalizedY = Math.Clamp((bounds.Center.Y - source.Y) / source.Height, 0, 1);
        return new LogicalRect(
            target.X + (normalizedX * target.Width) - (bounds.Width / 2),
            target.Y + (normalizedY * target.Height) - (bounds.Height / 2),
            bounds.Width,
            bounds.Height).ClampInside(target);
    }
}
