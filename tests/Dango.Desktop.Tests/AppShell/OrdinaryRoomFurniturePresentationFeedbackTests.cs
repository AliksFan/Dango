using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Furniture;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Furniture;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class OrdinaryRoomFurniturePresentationFeedbackTests
{
    [STATestMethod]
    public void GroundedBallArtworkIsVisibleInTheLiftedFloorBand()
    {
        var scene = Scene(960, 600);
        var anchor = new FurnitureAnchor(1080, FurnitureLayoutContract.OrdinaryRoom.BallRadius);
        scene.SetFurnitureFrame([new(Guid.NewGuid(), FurnitureItemKind.RoundBall, anchor, 0)], null, false);
        var center = scene.AnchorToScreen(anchor);
        var radius = FurnitureLayoutContract.OrdinaryRoom.BallRadius * scene.CurrentProjection.Scale;
        var sample = new Point(center.X, scene.EnvironmentEdges.FloorY + 4);
        Assert.IsTrue(sample.Y > center.Y && sample.Y < center.Y + radius - 2,
            "The sample must be inside real ball artwork and the ten-DIP raised floor strip.");

        var expectedDrawing = new DrawingGroup();
        using (var context = expectedDrawing.Open())
            context.DrawImage(OfficialRoundBallVisual.CurrentImage,
                new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        var expected = Pixel(Render(expectedDrawing, scene), sample);
        Assert.IsTrue(expected.A > 240, "The chosen sample cannot be a transparent part of the artwork.");
        var actual = Pixel(Render(ActualDrawing(scene), scene), sample);
        Assert.AreEqual(expected, actual,
            "The actual room render must keep the grounded ball in front of the raised decorative floor.");
        var exposedFloor = Pixel(Render(ActualDrawing(scene), scene), new Point(center.X + radius + 10, sample.Y));
        Assert.AreEqual(Color.FromRgb(205, 182, 149), exposedFloor, "The real painted floor must remain beside the ball.");
    }

    [STATestMethod]
    public void EditorRightOcclusionRevealsTheRoomEdgeWithoutChangingScaleOrWorld()
    {
        const double occlusion = 244;
        foreach (var size in new[] { new Size(640, 420), new Size(960, 600), new Size(640, 800) })
        {
            var scene = Scene(size.Width, size.Height);
            scene.SelectCameraMode(RoomCameraMode.Manual);
            var scale = scene.CurrentProjection.Scale;
            SetOcclusion(scene, occlusion);
            scene.PanCamera(-100000);
            AssertRightEdge(scene, occlusion);
            Assert.AreEqual(scale, scene.CurrentProjection.Scale, 1e-9, "Editing cannot shrink the body or furniture.");
            Assert.AreEqual(LocalRoomSceneView.WorldWidth, scene.Camera.WorldWidth);
            Assert.AreEqual(LocalRoomSceneView.WorldHeight, scene.Camera.WorldHeight);
            var anchor = new FurnitureAnchor(2070, 120);
            var restored = scene.ScreenToFurnitureAnchor(scene.AnchorToScreen(anchor));
            Assert.AreEqual(anchor.X, restored.X, 1e-9);
            Assert.AreEqual(anchor.Y, restored.Y, 1e-9);
        }
    }

    [STATestMethod]
    public void WholeViewAllowsTheNecessaryRightPanWithoutZoomingOut()
    {
        var scene = Scene(960, 600);
        scene.SelectView(RoomViewPreset.Whole);
        scene.SelectCameraMode(RoomCameraMode.Manual);
        var scale = scene.CurrentProjection.Scale;
        SetOcclusion(scene, 244);
        Assert.IsTrue(scene.PanCamera(-100000),
            "The old full-window WholeWorldVisible check must not prohibit revealing the sidebar-covered right edge.");
        AssertRightEdge(scene, 244);
        Assert.AreEqual(scale, scene.CurrentProjection.Scale, 1e-9);
        Assert.AreEqual(RoomViewPreset.Whole, scene.Camera.Preset);
    }

    [STATestMethod]
    public void WideWholeViewCentersTheRoomInTheUnobstructedAreaWithoutAddingUnnecessaryPan()
    {
        var scene = Scene(4000, 800);
        scene.SelectView(RoomViewPreset.Whole);
        var scale = scene.CurrentProjection.Scale;
        SetOcclusion(scene, 244);
        Assert.IsFalse(scene.PanCamera(-100000), "A room already fully visible before the sidebar needs no additional pan.");
        Assert.AreEqual(0, scene.Camera.CameraX, 1e-9);
        var left = scene.RoomToScene(new(0, 0)).X;
        var right = scene.RoomToScene(new(LocalRoomSceneView.WorldWidth, 0)).X;
        Assert.AreEqual((scene.ActualWidth - 244) / 2, (left + right) / 2, 1e-9);
        Assert.IsTrue(left >= 0 && right <= scene.ActualWidth - 244);
        Assert.AreEqual(scale, scene.CurrentProjection.Scale, 1e-9);
    }

    [STATestMethod]
    public void ResizeReclampsEditingRangeAndExitRestoresThePreviousCamera()
    {
        var scene = Scene(960, 600);
        scene.SelectCameraMode(RoomCameraMode.Manual);
        scene.Camera.ApplyZoomSteps(-2);
        scene.PanCamera(-90, -25);
        var before = scene.CaptureFurnitureCamera();
        var scale = scene.CurrentProjection.Scale;
        SetOcclusion(scene, 244);
        scene.PanCamera(-100000);
        Arrange(scene, new Size(640, 600));
        scene.PanCamera(-100000);
        AssertRightEdge(scene, 244);
        Assert.AreEqual(scale, scene.CurrentProjection.Scale, 1e-9);
        SetOcclusion(scene, 0);
        Arrange(scene, new Size(960, 600));
        scene.RestoreFurnitureCamera(before);
        Assert.AreEqual(before, scene.CaptureFurnitureCamera(), "Leaving editing must restore the full pre-edit camera snapshot.");
        scene.PanCamera(-100000);
        Assert.AreEqual(960 - LocalRoomSceneView.SideBoundaryInset * scale, scene.EnvironmentEdges.Right, 1e-8,
            "The additional presentation range must be removed after editing.");
    }

    private static void AssertRightEdge(LocalRoomSceneView scene, double occlusion)
    {
        var unobstructedRight = scene.ActualWidth - occlusion;
        Assert.AreEqual(unobstructedRight - LocalRoomSceneView.SideBoundaryInset * scene.CurrentProjection.Scale,
            scene.EnvironmentEdges.Right, 1e-8,
            "The physical right boundary must be revealed before the actual sidebar plus its visible gap.");
        Assert.IsTrue(scene.EnvironmentEdges.Right < unobstructedRight);
    }

    private static LocalRoomSceneView Scene(double width, double height)
    {
        var scene = new LocalRoomSceneView();
        scene.EnableWindowCamera();
        Arrange(scene, new Size(width, height));
        scene.Camera.Update(1080, 0, true);
        typeof(LocalRoomSceneView).GetProperty(nameof(LocalRoomSceneView.HasHealthyFrame),
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, true);
        scene.SelectCameraMode(RoomCameraMode.Manual);
        return scene;
    }

    private static void Arrange(LocalRoomSceneView scene, Size size)
    {
        scene.Width = size.Width; scene.Height = size.Height;
        scene.Measure(size); scene.Arrange(new Rect(new Point(), size)); scene.UpdateLayout();
    }

    private static void SetOcclusion(LocalRoomSceneView scene, double width)
    {
        // Allow the unchanged baseline to execute the same behavioral assertions:
        // missing accommodation leaves its old camera range rather than failing only on API presence.
        typeof(LocalRoomSceneView).GetMethod("SetFurnitureEditorOcclusion", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(scene, [width]);
    }

    private static DrawingGroup ActualDrawing(LocalRoomSceneView scene)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            typeof(LocalRoomSceneView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly)!.Invoke(scene, [context]);
        return drawing;
    }

    private static RenderTargetBitmap Render(Drawing drawing, LocalRoomSceneView scene)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawDrawing(drawing);
        var bitmap = new RenderTargetBitmap((int)scene.ActualWidth, (int)scene.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static Color Pixel(BitmapSource bitmap, Point point)
    {
        var bytes = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
}
