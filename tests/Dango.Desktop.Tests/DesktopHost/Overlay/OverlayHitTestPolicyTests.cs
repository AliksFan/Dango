using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Overlay;

[TestClass]
public sealed class OverlayHitTestPolicyTests
{
    [TestMethod]
    public void DistinguishesInteractiveSceneRegionsFromTransparentPixels()
    {
        var policy = new OverlayHitTestPolicy(
        [
            new OverlayHitRegion(
                OverlayHitTarget.Dango,
                new LogicalRect(160, 40, 140, 120)),
            new OverlayHitRegion(
                OverlayHitTarget.Island,
                new LogicalRect(80, 170, 320, 100)),
        ]);

        Assert.AreEqual(
            OverlayHitTarget.Dango,
            policy.HitTest(new LogicalPoint(200, 100)));
        Assert.AreEqual(
            OverlayHitTarget.Island,
            policy.HitTest(new LogicalPoint(100, 200)));
        Assert.AreEqual(
            OverlayHitTarget.PassThrough,
            policy.HitTest(new LogicalPoint(20, 20)));
    }

    [TestMethod]
    public void CollapsedOrUnarrangedVisualDoesNotCreateAnInvalidRegion()
    {
        Assert.IsNull(OverlayHitRegionFactory.TryCreate(OverlayHitTarget.Dango, 10, 20, 0, 135));
        Assert.IsNull(OverlayHitRegionFactory.TryCreate(OverlayHitTarget.Island, 10, 20, 360, 0));
        Assert.IsNotNull(OverlayHitRegionFactory.TryCreate(OverlayHitTarget.Dango, 10, 20, 150, 135));
    }
}
