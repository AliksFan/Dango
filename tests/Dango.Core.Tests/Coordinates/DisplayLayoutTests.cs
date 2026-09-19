using Dango.Core.Coordinates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Coordinates;

[TestClass]
public sealed class DisplayLayoutTests
{
    [TestMethod]
    public void ConvertsBetweenLogicalAndPhysicalCoordinatesPerDisplayDpi()
    {
        var display = new DisplayWorkArea(
            "secondary",
            new LogicalRect(-1280, 0, 1280, 720),
            new PixelRect(-1600, 0, 1600, 900),
            new DpiScale(1.25, 1.25));

        var physical = display.ToPhysical(new LogicalPoint(-640, 360));
        var logical = display.ToLogical(physical);

        Assert.AreEqual(new PixelPoint(-800, 450), physical);
        Assert.AreEqual(new LogicalPoint(-640, 360), logical);
    }

    [TestMethod]
    public void SupportsAWorkAreaWithNegativeVirtualDesktopCoordinates()
    {
        var layout = CreateDualDisplayLayout();

        var display = layout.FindContaining(new LogicalPoint(-1200, 100));

        Assert.IsNotNull(display);
        Assert.AreEqual("left", display.Id);
    }

    [TestMethod]
    public void MigratesBoundsFromARemovedDisplayIntoThePrimarySafeArea()
    {
        var primary = new DisplayWorkArea(
            "primary",
            new LogicalRect(0, 0, 1920, 1040),
            new PixelRect(0, 0, 1920, 1040),
            DpiScale.Identity);
        var layoutAfterRemoval = new DisplayLayout([primary], primary.Id);
        var removedDisplayBounds = new LogicalRect(-900, 600, 160, 120);

        var migrated = layoutAfterRemoval.PlaceWithinAvailableWorkArea(removedDisplayBounds);

        Assert.IsTrue(primary.LogicalBounds.Contains(migrated));
        Assert.AreEqual(new LogicalRect(0, 600, 160, 120), migrated);
    }

    private static DisplayLayout CreateDualDisplayLayout()
    {
        var primary = new DisplayWorkArea(
            "primary",
            new LogicalRect(0, 0, 1920, 1040),
            new PixelRect(0, 0, 1920, 1040),
            DpiScale.Identity);
        var left = new DisplayWorkArea(
            "left",
            new LogicalRect(-1280, 0, 1280, 720),
            new PixelRect(-1600, 0, 1600, 900),
            new DpiScale(1.25, 1.25));

        return new DisplayLayout([primary, left], primary.Id);
    }
}
