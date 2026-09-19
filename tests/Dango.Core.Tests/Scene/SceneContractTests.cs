using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Scene;

[TestClass]
public sealed class SceneContractTests
{
    [TestMethod]
    public void RectangleAndEllipseUseDeterministicLogicalCollision()
    {
        HitShape rectangle = new RectangleHitShape(
            "drag",
            HitShapePurpose.Drag,
            new LogicalRect(10, 20, 40, 30));
        HitShape ellipse = new EllipseHitShape(
            "click",
            HitShapePurpose.Click,
            new LogicalRect(0, 0, 100, 50));

        Assert.IsTrue(rectangle.Contains(new LogicalPoint(30, 35)));
        Assert.IsFalse(rectangle.Contains(new LogicalPoint(51, 35)));
        Assert.IsTrue(ellipse.Contains(new LogicalPoint(50, 25)));
        Assert.IsFalse(ellipse.Contains(new LogicalPoint(0, 0)));
    }

    [TestMethod]
    public void SceneMigrationMovesDangoAndIslandSafelyAfterDisplayRemoval()
    {
        var snapshot = new SceneSnapshot(
            SceneSnapshot.CurrentVersion,
            1000,
            42,
            "1",
            new DangoSnapshot(
                "dango-placeholder",
                new LogicalRect(-1000, 600, 160, 120),
                DangoActionState.Idle,
                1000,
                42,
                new LogicalPoint(-920, 720),
                "idle",
                "click",
                "drag",
                "collision",
                "island-grab"),
            new IslandSnapshot(
                "island-placeholder",
                new LogicalRect(-1100, 400, 320, 120),
                new LogicalRect(-1060, 410, 240, 24),
                new LogicalRect(-1050, 400, 220, 30),
                new LogicalRect(-1040, 408, 200, 24),
                new LogicalRect(-1100, 432, 320, 88)));
        var primary = new DisplayWorkArea(
            "primary",
            new LogicalRect(0, 0, 1920, 1040),
            new PixelRect(0, 0, 1920, 1040),
            DpiScale.Identity);

        var migrated = SceneLayoutMigrator.Migrate(
            snapshot,
            new DisplayLayout([primary], primary.Id));

        Assert.IsTrue(primary.LogicalBounds.Contains(migrated.Dango.VisualBounds));
        Assert.IsTrue(primary.LogicalBounds.Contains(migrated.Island.VisualBounds));
        Assert.IsTrue(primary.LogicalBounds.Contains(migrated.Island.AllowedLandingArea));
        Assert.IsTrue(primary.LogicalBounds.Contains(migrated.Island.OcclusionBounds));
        Assert.AreEqual("click", migrated.Dango.ClickShapeId);
        Assert.AreEqual("drag", migrated.Dango.DragShapeId);
        Assert.AreEqual("collision", migrated.Dango.CollisionShapeId);
        Assert.AreEqual("island-grab", migrated.Dango.IslandGrabAnchorId);
        Assert.AreEqual(snapshot.StartedAtMilliseconds, migrated.StartedAtMilliseconds);
        Assert.AreEqual(snapshot.VariantSeed, migrated.VariantSeed);
    }
}
