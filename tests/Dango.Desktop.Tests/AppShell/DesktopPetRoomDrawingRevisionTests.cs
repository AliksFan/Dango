using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomDrawingRevisionTests
{
    private static readonly Thickness WorkInsets = new(48, 32, 60, 40);
    private static readonly Rect WorkArea = new(48, 32, 1920 - 48 - 60, 1080 - 32 - 40);

    [STATestMethod]
    public void DesktopPetActualDrawingOmitsFullHeightDashedWallsAndDiagonalExcludedTiles()
    {
        var scene = OwnScene(desktopPet: true);
        var drawing = ActualEnvironmentDrawing(scene);
        var geometry = Geometry(drawing, Matrix.Identity).ToArray();
        Assert.IsFalse(geometry.Any(item => FullHeightDashedWall(item, scene.ActualHeight)),
            "The desktop-pet environment must not paint either full-height dashed side boundary.");
        Assert.IsFalse(geometry.Any(UsesDiagonalExcludedTile),
            "An actual painted fill must not use the diagonal excluded-space hatch.");
        AssertRetainsRealWallAndFloor(scene, geometry);
    }

    [STATestMethod]
    public void DesktopPetActualFloorCoversUsableHorizontalEdgesAndMeetsNativeWorkAreaBottom()
    {
        var scene = OwnScene(desktopPet: true);
        var geometry = Geometry(ActualEnvironmentDrawing(scene), Matrix.Identity).ToArray();
        var floor = PaintedFloor(scene, geometry);
        Assert.AreEqual(WorkArea.Left, floor.Left, 1e-9);
        Assert.AreEqual(WorkArea.Right, floor.Right, 1e-9,
            "The actual opaque floor must span the whole usable work area instead of the old cropped room walls.");
        Assert.IsTrue(floor.Contains(new Point(WorkArea.Left + 1, WorkArea.Bottom - 1)) &&
            floor.Contains(new Point(WorkArea.Right - 1, WorkArea.Bottom - 1)),
            "Both usable bottom corners must be part of the real painted floor.");
        Assert.AreEqual(WorkArea.Bottom, scene.EnvironmentEdges.CollisionY, 1e-9,
            "The collision floor must map to the work-area bottom; the old forty-eight-DIP gap is removed.");
        AssertRetainsRealWallAndFloor(scene, geometry);
    }

    [STATestMethod]
    public void OrdinaryRoomStillPaintsItsOwnDashedBoundariesAndExcludedEnvironment()
    {
        var scene = OwnScene(desktopPet: false);
        var geometry = Geometry(ActualEnvironmentDrawing(scene), Matrix.Identity).ToArray();
        Assert.AreEqual(2, geometry.Count(item => FullHeightDashedWall(item, scene.ActualHeight)),
            "Removing desktop-pet boundaries cannot remove the ordinary room's two accepted physical boundary drawings.");
        Assert.IsTrue(geometry.Any(UsesDiagonalExcludedTile),
            "The ordinary room must retain its actual excluded-space drawing.");
        AssertRetainsRealWallAndFloor(scene, geometry);
    }

    private static LocalRoomSceneView OwnScene(bool desktopPet)
    {
        var scene = new LocalRoomSceneView { Width = 1920, Height = 1080, SafeInsets = WorkInsets };
        if (desktopPet) scene.EnableDesktopPetPresentation();
        scene.Measure(new(1920, 1080));
        scene.Arrange(new(0, 0, 1920, 1080));
        scene.UpdateLayout();
        if (!desktopPet) scene.SelectView(RoomViewPreset.Whole);
        return scene;
    }

    private static DrawingGroup ActualEnvironmentDrawing(LocalRoomSceneView scene)
    {
        // Invoke the production scene's actual drawing callback into an owned drawing, with its actual arranged size.
        // This creates no HWND, reads no external visual and copies none of the drawing implementation into the test.
        var render = typeof(LocalRoomSceneView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly) ?? throw new AssertFailedException("The scene must provide its real environment render callback.");
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) render.Invoke(scene, [context]);
        Assert.IsTrue(drawing.Children.Count > 0, "An empty scene cannot pass absence-of-boundary assertions.");
        return drawing;
    }

    private readonly record struct PaintedGeometry(GeometryDrawing Drawing, Matrix Transform)
    {
        internal Rect Bounds => new MatrixTransform(Transform).TransformBounds(Drawing.Geometry.Bounds);
    }

    private static IEnumerable<PaintedGeometry> Geometry(Drawing drawing, Matrix parent)
    {
        if (drawing is DrawingGroup group)
        {
            var transform = group.Transform?.Value ?? Matrix.Identity;
            transform.Append(parent);
            foreach (var child in group.Children)
                foreach (var geometry in Geometry(child, transform)) yield return geometry;
        }
        else if (drawing is GeometryDrawing geometry) yield return new(geometry, parent);
    }

    private static bool FullHeightDashedWall(PaintedGeometry item, double sceneHeight)
    {
        if (item.Drawing.Pen is not { DashStyle.Dashes.Count: > 0 } || item.Drawing.Geometry is not LineGeometry line) return false;
        var first = item.Transform.Transform(line.StartPoint);
        var second = item.Transform.Transform(line.EndPoint);
        return Math.Abs(first.X - second.X) < 1e-9 && Math.Abs(first.Y - second.Y) >= sceneHeight - 1;
    }

    private static bool UsesDiagonalExcludedTile(PaintedGeometry item)
    {
        if (item.Drawing.Brush is not DrawingBrush { TileMode: TileMode.Tile, Drawing: { } tile }) return false;
        return Geometry(tile, Matrix.Identity).Any(part => part.Drawing.Pen is not null &&
            part.Drawing.Geometry is LineGeometry line &&
            Math.Abs(line.StartPoint.X - line.EndPoint.X) > 1e-9 && Math.Abs(line.StartPoint.Y - line.EndPoint.Y) > 1e-9);
    }

    private static Rect PaintedFloor(LocalRoomSceneView scene, IReadOnlyList<PaintedGeometry> geometry)
    {
        var opaqueBottomStrips = geometry.Where(item => item.Drawing.Brush is SolidColorBrush { Color.A: > 0 } &&
            item.Drawing.Pen is null && item.Drawing.Geometry is RectangleGeometry &&
            item.Bounds.Top > scene.ActualHeight / 2 && item.Bounds.Bottom >= WorkArea.Bottom &&
            item.Bounds.Width > WorkArea.Width / 2).ToArray();
        Assert.HasCount(1, opaqueBottomStrips,
            "The actual drawing must contain its opaque physical floor; background-only or blank output is insufficient.");
        return opaqueBottomStrips.Single().Bounds;
    }

    private static void AssertRetainsRealWallAndFloor(LocalRoomSceneView scene, IReadOnlyList<PaintedGeometry> geometry)
    {
        var floor = PaintedFloor(scene, geometry);
        Assert.IsTrue(floor.Top < WorkArea.Bottom, "A real floor strip must remain visible inside the work area.");
        Assert.IsTrue(geometry.Any(item => item.Drawing.Pen is { DashStyle.Dashes.Count: 0 } &&
            item.Drawing.Geometry is LineGeometry && item.Bounds.Top >= WorkArea.Top && item.Bounds.Bottom < floor.Top &&
            item.Bounds.Width <= 20 && item.Bounds.Height <= 20),
            "The wall must retain actual small registration marks; removing borders cannot erase the real room environment.");
    }
}
