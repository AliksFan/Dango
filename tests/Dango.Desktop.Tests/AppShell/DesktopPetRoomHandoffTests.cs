using System.Windows;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomHandoffTests
{
    [TestMethod]
    [DataRow(1920d, 1080d, 0d, 0d, 0d, 40d)]
    [DataRow(1280d, 800d, 48d, 0d, 0d, 0d)]
    [DataRow(1440d, 900d, 0d, 56d, 0d, 0d)]
    [DataRow(1600d, 900d, 0d, 0d, 60d, 0d)]
    public void FixedProjectionFitsThePhysicalWallsAndRoundTripsInsideIndependentWorkArea(
        double width, double height, double leftInset, double topInset, double rightInset, double bottomInset)
    {
        const double nativeFloor = LocalRoomSceneView.WorldHeight;
        var mapping = DesktopPetRoomMapping.Create(width, height, new(leftInset, topInset, rightInset, bottomInset), nativeFloor);
        var workArea = new LogicalRect(leftInset, topInset, width - leftInset - rightInset, height - topInset - bottomInset);
        Assert.IsTrue(mapping.SafeSceneBounds.X > workArea.X && mapping.SafeSceneBounds.Y > workArea.Y);
        Assert.IsTrue(mapping.SafeSceneBounds.Right < workArea.Right && mapping.SafeSceneBounds.Bottom < workArea.Bottom,
            "The playable projection must leave a visible safety margin and exclude every taskbar inset.");
        var left = mapping.Projection.WorldToScreen(new(LocalRoomSceneView.SideBoundaryInset, nativeFloor));
        var right = mapping.Projection.WorldToScreen(new(LocalRoomSceneView.WorldWidth - LocalRoomSceneView.SideBoundaryInset, nativeFloor));
        Assert.AreEqual(mapping.SafeSceneBounds.X, left.X, 1e-9);
        Assert.AreEqual(mapping.SafeSceneBounds.Right, right.X, 1e-9);
        Assert.AreEqual(left.Y, right.Y, 1e-9, "The two walls terminate on one physical room floor.");
        Assert.IsTrue(left.Y > mapping.SafeSceneBounds.Y && left.Y < mapping.SafeSceneBounds.Bottom);
        foreach (var world in new[] { new LogicalPoint(24, nativeFloor), new(1080, -180), new(2136, nativeFloor - 90) })
        {
            var projected = mapping.Projection.WorldToScreen(world);
            var inverse = mapping.Projection.ScreenToWorld(projected);
            Assert.AreEqual(world.X, inverse.X, 1e-9);
            Assert.AreEqual(world.Y, inverse.Y, 1e-9,
                "Input must invert the same room projection, including activity above the original room's top.");
        }
        Assert.IsTrue(mapping.Surface.Y <= mapping.SafeWorldBounds.Y);
        Assert.AreEqual(nativeFloor, mapping.Surface.Bottom, 1e-9, "Desktop mapping must retain the existing physical room floor.");
    }

    [TestMethod]
    [DataRow(0d, 1080d, 0d, 0d, 360d)]
    [DataRow(1920d, 0d, 0d, 0d, 360d)]
    [DataRow(1920d, 80d, 0d, 0d, 360d)]
    [DataRow(64d, 1080d, 100d, 0d, 360d)]
    [DataRow(double.NaN, 1080d, 0d, 0d, 360d)]
    [DataRow(1920d, double.PositiveInfinity, 0d, 0d, 360d)]
    [DataRow(1920d, 1080d, double.NaN, 0d, 360d)]
    [DataRow(1920d, 1080d, 0d, double.PositiveInfinity, 360d)]
    [DataRow(1920d, 1080d, 0d, 0d, double.NaN)]
    public void UnsafeOrNonFiniteViewportCannotCreateAPlayableMapping(
        double width, double height, double leftInset, double bottomInset, double floor)
    {
        var failure = Assert.Throws<Exception>(() => DesktopPetRoomMapping.Create(width, height,
            new Thickness(leftInset, 0, 0, bottomInset), floor));
        Assert.IsTrue(failure is ArgumentException or InvalidOperationException,
            "Invalid display geometry must produce a recoverable input/domain rejection, not a null reference or a playable mapping.");
    }

    [TestMethod]
    public void WallpaperOrUnhealthyAcknowledgementCannotCompleteDesktopPetEntry()
    {
        var gate = new LocalRoomHandoffGate();
        var attempt = gate.Begin(LocalRoomPresentation.DesktopPet);
        Assert.IsFalse(gate.TryComplete(attempt, LocalRoomPresentation.Wallpaper, true));
        Assert.IsFalse(gate.TryComplete(attempt, LocalRoomPresentation.DesktopPet, false));
        Assert.IsTrue(gate.Pending);
        Assert.AreEqual(LocalRoomPresentation.Porch, gate.Current);
        Assert.IsTrue(gate.TryComplete(attempt, LocalRoomPresentation.DesktopPet, true));
        Assert.AreEqual(LocalRoomPresentation.DesktopPet, gate.Current);
        Assert.IsFalse(gate.TryComplete(attempt, LocalRoomPresentation.DesktopPet, true));
    }

    [TestMethod]
    public void CancelledEntryAndFailedRestoreCannotAcknowledgeTheNextDesktopPetAttempt()
    {
        var gate = new LocalRoomHandoffGate();
        var cancelled = gate.Begin(LocalRoomPresentation.DesktopPet);
        gate.Hide();
        Assert.IsFalse(gate.TryComplete(cancelled, LocalRoomPresentation.DesktopPet, true));
        var failed = gate.Begin(LocalRoomPresentation.DesktopPet);
        Assert.IsTrue(gate.Fail(failed));
        var current = gate.Begin(LocalRoomPresentation.DesktopPet);
        Assert.IsFalse(gate.TryComplete(cancelled, LocalRoomPresentation.DesktopPet, true));
        Assert.IsFalse(gate.TryComplete(failed, LocalRoomPresentation.DesktopPet, true));
        Assert.IsFalse(gate.Fail(failed));
        Assert.IsTrue(gate.Pending);
        Assert.IsTrue(gate.TryComplete(current, LocalRoomPresentation.DesktopPet, true));
    }
}
