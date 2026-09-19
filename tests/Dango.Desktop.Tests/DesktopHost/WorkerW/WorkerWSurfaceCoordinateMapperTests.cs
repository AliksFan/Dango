using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.WorkerW;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWSurfaceCoordinateMapperTests
{
    [TestMethod]
    public void PhysicalWorkerWSurfaceIsConvertedToWpfLogicalBounds()
    {
        var bounds = WorkerWSurfaceCoordinateMapper.ToLogicalBounds(
            2_560,
            1_440,
            new DpiScale(1.25, 1.25));

        Assert.AreEqual(new LogicalRect(0, 0, 2_048, 1_152), bounds);
        Assert.IsTrue(bounds.Contains(new LogicalRect(1_864, 1_032, 160, 120)));
    }
}
