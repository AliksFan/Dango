using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Overlay;

[TestClass]
public sealed class DesktopHitTestPolicyTests
{
    [TestMethod]
    public void DistinguishesInteractiveSceneRegionsFromTransparentPixels()
    {
        var policy = new DesktopHitTestPolicy(
        [
            new DesktopHitRegion(
                DesktopHitTarget.Dango,
                new LogicalRect(160, 40, 140, 120)),
            new DesktopHitRegion(
                DesktopHitTarget.Island,
                new LogicalRect(80, 170, 320, 100)),
        ]);

        Assert.AreEqual(
            DesktopHitTarget.Dango,
            policy.HitTest(new LogicalPoint(200, 100)));
        Assert.AreEqual(
            DesktopHitTarget.Island,
            policy.HitTest(new LogicalPoint(100, 200)));
        Assert.AreEqual(
            DesktopHitTarget.PassThrough,
            policy.HitTest(new LogicalPoint(20, 20)));
    }

    [TestMethod]
    public void CollapsedOrUnarrangedVisualDoesNotCreateAnInvalidRegion()
    {
        Assert.IsNull(DesktopHitRegionFactory.TryCreate(DesktopHitTarget.Dango, 10, 20, 0, 135));
        Assert.IsNull(DesktopHitRegionFactory.TryCreate(DesktopHitTarget.Island, 10, 20, 360, 0));
        Assert.IsNotNull(DesktopHitRegionFactory.TryCreate(DesktopHitTarget.Dango, 10, 20, 150, 135));
    }
}
