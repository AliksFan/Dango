using System.Windows;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryRigRuntimeContractTests
{
    private const double Dt = 1d / 120;
    private static AccessoryItem Item(double softness = .85, double gravity = 0) =>
        new("rig-runtime", "Synthetic chain", "builtin-sprout", 64, 64)
        {
            ImagePivotX = .1, ImagePivotY = .5,
            DeformationMode = AccessoryDeformationMode.SingleChain,
            Rig = new([new(.1, .5), new(.35, .5), new(.6, .5), new(.9, .5)], softness, 3, 2),
            GravityInfluence = gravity, ConnectionStrength = 100, LockRotation = true,
        };

    [STATestMethod]
    public void BeltConsumerUsesRealCoreTranslationAndRefreshCannotAdvanceBodyOrChain()
    {
        var configuration = new AccessoryConfiguration([Item()]);
        using var equipped = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        using var control = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        equipped.SetAccessories(configuration, _ => null);
        AccessoryMeshFrame? rest = null;
        for (var tick = 0; tick <= 24; tick++)
        {
            var frame = BeltFrame(tick, tick * .004, tick * .008);
            equipped.Update(frame); control.Update(frame);
            var actual = equipped.Inspect(); var bodyOnly = control.Inspect();
            Assert.IsNull(equipped.AccessoryError);
            CollectionAssert.AreEqual(bodyOnly.Shape!.Boundary.ToArray(), actual.Shape!.Boundary.ToArray(),
                "Adding a chain cannot deform the authoritative body shape.");
            Assert.AreEqual(bodyOnly.Body!.Attachments!.StepIndex, actual.Body!.Attachments!.StepIndex);
            rest ??= actual.Accessories.Single().Frame;
        }
        var before = equipped.Inspect(); var mesh = before.Accessories.Single().Frame;
        Assert.IsTrue(MaximumRigidDeviation(mesh, rest!, mesh.ImagePivot - rest!.ImagePivot) > .1,
            "Moving CoreX/CoreY with an unchanged local body contour must drive internal chain deformation.");
        equipped.Update(BeltFrame(24, 99, 99));
        Assert.AreSame(before.Body, equipped.Inspect().Body);
        AssertFrames(mesh, equipped.Inspect().Accessories.Single().Frame);
        for (var paint = 0; paint < 4; paint++) equipped.SetAccessories(configuration, _ => null);
        var refreshed = equipped.Inspect();
        Assert.AreSame(before.Body, refreshed.Body);
        Assert.AreSame(before.Shape, refreshed.Shape);
        Assert.AreEqual(before.Tick, refreshed.Tick);
        AssertFrames(mesh, refreshed.Accessories.Single().Frame);
        Assert.AreEqual(mesh.StepIndex, refreshed.Accessories.Single().Frame.StepIndex);
    }

    [STATestMethod]
    public void StackedConsumerAdvancesOnlyOnSuccessfulOwnerCallAndRefreshPreservesFutureChain()
    {
        using var bodySource = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        bodySource.Update(BeltFrame(0, 0, 0));
        var body = bodySource.Inspect().Body!;
        StackedPhysicsPresentationFrame Frame(int tick) => new([body], [new LogicalRect(100, 100 + tick * .5, 160, 120)],
            [default], 0, [], false, 0) { EntityIds = [new DangoEntityId(1)] };
        var configuration = new AccessoryConfiguration([Item()]);
        var refreshed = new AccessorySceneController(_ => BuiltinAccessoryImages.Get("builtin-sprout"));
        var control = new AccessorySceneController(_ => BuiltinAccessoryImages.Get("builtin-sprout"));
        refreshed.SetConfiguration(configuration, true, Frame(0));
        control.SetConfiguration(configuration, true, Frame(0));
        AccessoryMeshFrame? rest = null;
        for (var tick = 0; tick <= 24; tick++)
        {
            var frame = Frame(tick);
            refreshed.Advance(frame, Dt); control.Advance(frame, Dt);
            Assert.IsNull(refreshed.Error);
            Assert.AreSame(body, frame.Bodies.Single());
            rest ??= refreshed.Frames.Single().Mesh;
        }
        var moving = refreshed.Frames.Single().Mesh;
        Assert.IsTrue(MaximumRigidDeviation(moving, rest!, moving.ImagePivot - rest!.ImagePivot) > .1);
        for (var paint = 0; paint < 8; paint++)
        {
            refreshed.Refresh(Frame(24));
            AssertFrames(moving, refreshed.Frames.Single().Mesh);
            Assert.AreEqual(moving.StepIndex, refreshed.Frames.Single().Mesh.StepIndex);
        }
        refreshed.Advance(Frame(25), Dt); control.Advance(Frame(25), Dt);
        AssertFrames(control.Frames.Single().Mesh, refreshed.Frames.Single().Mesh);
        refreshed.Pause(); refreshed.Refresh(Frame(100)); refreshed.Resume();
        refreshed.Advance(Frame(101), Dt);
        var fresh = new AccessorySceneController(_ => BuiltinAccessoryImages.Get("builtin-sprout"));
        fresh.SetConfiguration(configuration, true, Frame(101)); fresh.Advance(Frame(101), Dt);
        AssertFrames(fresh.Frames.Single().Mesh, refreshed.Frames.Single().Mesh);
    }

    private static BeltBodyAppearanceFrame BeltFrame(long tick, double coreX, double coreY) => new(tick, Dt,
        Enumerable.Range(0, 64).Select(i => new BeltBodyBoundaryPoint(coreX + .75 * Math.Cos(i * Math.Tau / 64),
            coreY + .75 * Math.Sin(i * Math.Tau / 64))).ToArray(), coreX, coreY, 0, 0, true, 0,
        false, false, false, false, PhysicalTick: tick);

    [TestMethod]
    public void ActualRootMotionDeformsChainWithGravityOffWhileRigidKeepsDistances()
    {
        var chain = new AccessoryMotionSolver(); var rigid = new AccessoryMotionSolver();
        var item = Item(); var rigidItem = item with { DeformationMode = AccessoryDeformationMode.Rigid };
        AccessoryMeshFrame? c = null, r = null;
        var c0 = chain.Update(0, Dt, new(100, 100), 96, 64, item);
        var r0 = rigid.Update(0, Dt, new(100, 100), 96, 64, rigidItem);
        for (var tick = 1; tick <= 40; tick++)
        {
            var root = new Point(100, 100 + tick * .5);
            c = chain.Update(tick, Dt, root, 96, 64, item);
            r = rigid.Update(tick, Dt, root, 96, 64, rigidItem);
        }
        Assert.IsTrue(MaximumRigidDeviation(c!, c0, new(0, 20)) > .5,
            "Zero gravity must retain inertial following driven by real root translation.");
        Assert.IsTrue(MaximumRigidDeviation(r!, r0, new(0, 20)) < 1e-8);
        Assert.AreEqual(new Point(100, 120), c!.ImagePivot);
        Assert.IsTrue(c.AllowFolding, "Chain skin may overlap in 2D without reviving the old material folding option.");
    }

    [TestMethod]
    public void ZeroRigSoftnessFixesShapeButPreservesSharedRootMotionAndGravity()
    {
        var solver = new AccessoryMotionSolver();
        var item = Item(0, 100) with { ConnectionStrength = 10, LockRotation = false, LimitSwing = false };
        AccessoryMeshFrame? frame = null;
        for (var tick = 0; tick < 120; tick++) frame = solver.Update(tick, Dt, new(100, 100), 96, 64, item);
        Assert.IsTrue(frame!.ConnectionOffset.Length > .1);
        Assert.IsTrue(Math.Abs(frame.RotationDegrees) > .1);
        var first = frame.Vertices[0]; var last = frame.Vertices[^1];
        Assert.AreEqual(Math.Sqrt(96 * 96 + 64 * 64), (last - first).Length, 1e-8,
            "Softness zero must preserve the complete image while root springs and rotation remain active.");
    }

    [TestMethod]
    public void StationaryChainGravitySagsInternallyWithLockedRoot()
    {
        var gravity = new AccessoryMotionSolver(); var zero = new AccessoryMotionSolver();
        var item = Item(gravity: 100); AccessoryMeshFrame? frame = null, rest = null;
        for (var tick = 0; tick < 120; tick++)
        {
            frame = gravity.Update(tick, Dt, new(100, 100), 96, 64, item);
            rest = zero.Update(tick, Dt, new(100, 100), 96, 64, item with { GravityInfluence = 0 });
        }
        Assert.IsTrue(MaximumRigidDeviation(frame!, rest!, default) > 1);
        Assert.AreEqual(new Point(100, 100), frame!.ImagePivot);
        Assert.AreEqual(0, frame.RotationDegrees, 1e-9);
    }

    [TestMethod]
    public void RepeatedRefreshAndTemporaryConfigurationProjectionCannotChangeFutureMotion()
    {
        var painted = new AccessoryMotionSolver(); var control = new AccessoryMotionSolver(); var item = Item();
        AccessoryMeshFrame? before = null;
        for (var tick = 0; tick < 24; tick++)
        {
            before = painted.Update(tick, Dt, new(100, 100 + tick), 96, 64, item);
            control.Update(tick, Dt, new(100, 100 + tick), 96, 64, item);
        }
        for (var paint = 0; paint < 30; paint++)
        {
            var moved = painted.Refresh(new(200, 300), 96, 64, item);
            Assert.IsTrue(MaximumRigidDeviation(moved, before!, new(100, 177)) < 1e-8);
            painted.Refresh(new(50, 50), 128, 128, item with { DeformationMode = AccessoryDeformationMode.Rigid, BaseAngleDegrees = 45 });
        }
        AssertFrames(control.Update(24, Dt, new(100, 124), 96, 64, item),
            painted.Update(24, Dt, new(100, 124), 96, 64, item));
    }

    [TestMethod]
    public void ConfigurationRefreshShowsNewRestImmediatelyAndNextOwnerStepDropsResidualVelocity()
    {
        var solver = new AccessoryMotionSolver(); var item = Item();
        for (var tick = 0; tick < 20; tick++) solver.Update(tick, Dt, new(100, 100 + tick), 96, 64, item);
        var changed = item with { DeformationMode = AccessoryDeformationMode.Rigid, BaseAngleDegrees = 30 };
        var refresh = solver.Refresh(new(100, 119), 96, 64, changed);
        Assert.AreEqual(19L, refresh.StepIndex);
        Assert.AreEqual(30, refresh.RotationDegrees, 1e-9);
        var duplicate = solver.Update(19, Dt, new(100, 119), 96, 64, changed);
        Assert.AreEqual(19L, duplicate.StepIndex);
        AssertFrames(refresh, duplicate);
        var fresh = new AccessoryMotionSolver();
        AssertFrames(fresh.Update(20, Dt, new(100, 120), 96, 64, changed), solver.Update(20, Dt, new(100, 120), 96, 64, changed));
        AssertFrames(fresh.Update(21, Dt, new(100, 121), 96, 64, item), solver.Update(21, Dt, new(100, 121), 96, 64, item));
        AssertFrames(fresh.Update(22, Dt, new(100, 122), 96, 64, item), solver.Update(22, Dt, new(100, 122), 96, 64, item));
    }

    [TestMethod]
    public void DuplicateTicksDoNotIntegrateAndPauseOrLongFrameRebasesWithoutHiddenImpulse()
    {
        var solver = new AccessoryMotionSolver(); var item = Item() with { ConnectionStrength = 10, LockRotation = false };
        var first = solver.Update(4, Dt, new(100, 100), 96, 64, item);
        Assert.AreSame(first, solver.Update(4, Dt, new(200, 200), 96, 64, item));
        Assert.AreSame(first, solver.Update(3, Dt, new(200, 200), 96, 64, item));
        solver.Update(5, Dt, new(100, 101), 96, 64, item);
        solver.Paused = true; solver.Paused = false;
        var resumed = solver.Update(6, Dt, new(500, 500), 96, 64, item);
        AssertFrames(new AccessoryMotionSolver().Update(6, Dt, new(500, 500), 96, 64, item), resumed);
        var longFrame = solver.Update(7, .08, new(800, 800), 96, 64, item);
        AssertFrames(new AccessoryMotionSolver().Update(7, Dt, new(800, 800), 96, 64, item), longFrame);
        solver.Update(8, Dt, new(800, 801), 96, 64, item);
        var teleported = solver.Update(9, Dt, new(8000, 8000), 96, 64, item);
        AssertFrames(new AccessoryMotionSolver().Update(9, Dt, new(8000, 8000), 96, 64, item), teleported);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Update(8, .00000001, new(), 96, 64, item));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => solver.Update(8, .101, new(), 96, 64, item));
    }

    [TestMethod]
    public void PublicMotionBoundaryRejectsInvalidNodeCollectionsBeforeDereferencing()
    {
        var solver = new AccessoryMotionSolver();
        foreach (var nodes in new AccessoryRigNode[][]
        {
            [new(.1, .5)],
            [new(.1, .5), null!],
            Enumerable.Range(0, 18).Select(i => new AccessoryRigNode(.1 + i * .01, .5)).ToArray(),
        })
        {
            var item = Item() with { Rig = new(nodes) };
            Assert.ThrowsExactly<ArgumentException>(() => solver.Update(0, Dt, new(100, 100), 96, 64, item));
        }
    }

    [TestMethod]
    public void RigTopologyAndDisplaySizeChangesRebaseOnlyAtNextOwnerStep()
    {
        var solver = new AccessoryMotionSolver(); var item = Item(gravity: 100);
        for (var tick = 0; tick < 40; tick++) solver.Update(tick, Dt, new(100, 100), 96, 64, item);
        var changed = item with { Rig = new([new(.1, .5), new(.4, .55), new(.9, .7)]) };
        var refreshed = solver.Refresh(new(200, 200), 128, 80, changed);
        Assert.AreEqual(39L, refreshed.StepIndex);
        AssertFrames(new AccessoryMotionSolver().Refresh(new(200, 200), 128, 80, changed), refreshed);
        AssertFrames(new AccessoryMotionSolver().Update(40, Dt, new(200, 200), 128, 80, changed),
            solver.Update(40, Dt, new(200, 200), 128, 80, changed));
    }

    private static double MaximumRigidDeviation(AccessoryMeshFrame actual, AccessoryMeshFrame rest, Vector translation) =>
        actual.Vertices.Select((point, index) => (point - rest.Vertices[index] - translation).Length).Max();

    private static void AssertFrames(AccessoryMeshFrame expected, AccessoryMeshFrame actual)
    {
        Assert.AreEqual(expected.ImagePivot, actual.ImagePivot);
        Assert.AreEqual(expected.RotationDegrees, actual.RotationDegrees, 1e-8);
        CollectionAssert.AreEqual(expected.TextureCoordinates.ToArray(), actual.TextureCoordinates.ToArray());
        Assert.IsTrue(MaximumRigidDeviation(actual, expected, default) < 1e-8);
    }
}
