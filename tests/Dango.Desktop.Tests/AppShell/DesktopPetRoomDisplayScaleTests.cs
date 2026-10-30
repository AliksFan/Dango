using System.Windows;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomDisplayScaleTests
{
    [TestMethod]
    [DataRow(50)]
    [DataRow(75)]
    [DataRow(100)]
    [DataRow(150)]
    [DataRow(200)]
    public void DisplayPercentScalesTheSharedProjectionAndRetainsSafeFloorAndInverse(int percent)
    {
        var insets = new Thickness(48, 24, 0, 40);
        var original = DesktopPetRoomMapping.Create(1920, 1080, insets, 360);
        var scaled = DesktopPetRoomMapping.Create(1920, 1080, insets, 360, percent);
        Assert.AreEqual(original.Projection.Scale * percent / 100d, scaled.Projection.Scale, 1e-9);
        Assert.AreEqual(original.SafeSceneBounds, scaled.SafeSceneBounds);
        Assert.AreEqual(original.Surface.Width * 100d / percent, scaled.Surface.Width, 1e-8);
        Assert.AreEqual(1040, scaled.Projection.WorldToScreen(new(1080, 360)).Y, 1e-9);
        foreach (var point in new[] { new LogicalPoint(1080, 360), new(500, -120), new(1600, 250) })
        {
            var result = scaled.Projection.ScreenToWorld(scaled.Projection.WorldToScreen(point));
            Assert.AreEqual(point.X, result.X, 1e-8);
            Assert.AreEqual(point.Y, result.Y, 1e-8);
        }
    }

    [TestMethod]
    [DataRow(49)]
    [DataRow(201)]
    [DataRow(0)]
    public void InvalidDisplayPercentCannotProduceAProjection(int percent) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DesktopPetRoomMapping.Create(1920, 1080, default, 360, percent));

    [STATestMethod]
    public void ChangingScaleInvalidatesInputAndUpdatesTheSameSceneProjectionWithoutAnHwnd()
    {
        var scene = new LocalRoomSceneView { Width = 1920, Height = 1080 };
        scene.EnableDesktopPetPresentation();
        scene.Measure(new(1920, 1080)); scene.Arrange(new(0, 0, 1920, 1080));
        var initial = scene.CurrentProjection;
        var invalidated = 0;
        scene.InputInvalidated += () => invalidated++;
        scene.DesktopPetDisplayPercent = 150;
        Assert.AreEqual(1, invalidated);
        Assert.AreEqual(initial.Scale * 1.5, scene.CurrentProjection.Scale, 1e-9);
        scene.DesktopPetDisplayPercent = 150;
        Assert.AreEqual(1, invalidated, "Re-reading the saved setting cannot repeatedly cancel gestures.");
        Assert.IsNull(PresentationSource.FromVisual(scene));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scene.DesktopPetDisplayPercent = 500);
        Assert.AreEqual(150, scene.DesktopPetDisplayPercent);
    }

    [TestMethod]
    [DataRow(50)]
    [DataRow(100)]
    [DataRow(200)]
    public void FullArtworkFitIncludesOutlyingAccessoriesAndTopEdgeWithoutMovingThePhysicalFloor(int percent)
    {
        var mapping = DesktopPetRoomMapping.Create(1920, 1080, new(0, 0, 0, 40), 360, percent);
        var safe = mapping.SafeWorldBounds;
        // Complete measured artwork: an accessory extends beyond the body on both axes.
        var artwork = new LogicalRect(safe.X - 60, safe.Y - 80, 300, 180);
        var offset = mapping.FitDisplayOffset(artwork);
        var displayed = artwork.Translate(offset);
        Assert.AreEqual(60, offset.X, 1e-8);
        Assert.AreEqual(80, offset.Y, 1e-8);
        Assert.IsTrue(displayed.X >= safe.X && displayed.Y >= safe.Y);
        Assert.IsTrue(displayed.Right <= safe.Right && displayed.Bottom <= safe.Bottom);
        var physical = new LogicalPoint(artwork.X + 100, artwork.Y + 100);
        var pixel = mapping.Projection.WorldToScreen(physical + offset);
        var inverse = mapping.Projection.ScreenToWorld(pixel);
        Assert.AreEqual(physical.X, inverse.X - offset.X, 1e-8);
        Assert.AreEqual(physical.Y, inverse.Y - offset.Y, 1e-8);
        Assert.AreEqual(1040, mapping.Projection.WorldToScreen(new(1080, 360)).Y, 1e-8);
        Assert.AreEqual(default(LogicalVector), mapping.FitDisplayOffset(new(safe.X + 20, safe.Y + 20, 100, 100)),
            "Once the artwork returns inside the safe area, no presentation translation remains.");
    }
}
