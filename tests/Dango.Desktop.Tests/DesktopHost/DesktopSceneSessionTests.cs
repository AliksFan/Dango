using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class DesktopSceneSessionTests
{
    [TestMethod]
    public void RehostingKeepsOneControllerAndMapsTheSameEntity()
    {
        var session = new DesktopSceneSession();
        var first = session.AttachSurface(new LogicalRect(0, 0, 1920, 1080), 10);
        var controller = session.Controller;
        var id = first.Dango.Id;
        var sourceCenter = first.Dango.VisualBounds.Center;

        var second = session.AttachSurface(new LogicalRect(0, 0, 800, 600), 20);

        Assert.AreSame(controller, session.Controller);
        Assert.AreEqual(id, second.Dango.Id);
        Assert.AreEqual(sourceCenter.X / 1920d, second.Dango.VisualBounds.Center.X / 800d, 0.001);
        var expectedMappedY = Math.Min(
            sourceCenter.Y / 1080d * 600d,
            600d - (second.Dango.VisualBounds.Height / 2d));
        Assert.AreEqual(expectedMappedY, second.Dango.VisualBounds.Center.Y, 0.001);
    }

    [TestMethod]
    public void InvalidSurfaceDoesNotReplaceTheCurrentSnapshot()
    {
        var session = new DesktopSceneSession();
        var before = session.AttachSurface(new LogicalRect(0, 0, 800, 600), 10);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => session.AttachSurface(new LogicalRect(0, 0, double.NaN, 600), 20));
        Assert.AreEqual(before, session.Snapshot);
    }
}
