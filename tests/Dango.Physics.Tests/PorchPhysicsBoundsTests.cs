using System.Text.Json;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class PorchPhysicsBoundsTests
{
    private static string Module => PhysicsTestLibraries.Unified;
    private static string Baseline => Environment.GetEnvironmentVariable("DANGO_PHYSICS_BASELINE_PATH") ??
        throw new InvalidOperationException("Set DANGO_PHYSICS_BASELINE_PATH to the unchanged accepted module.");
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);

    [TestMethod]
    public void DefaultWorldRemainsFrameForFrameIdenticalToAcceptedModule()
    {
        using var actual = DangoPhysicsWorld.Create(Module);
        using var reference = DangoPhysicsWorld.Create(Baseline);
        var a = actual.Add(0, 0);
        var b = reference.Add(0, 0);
        for (var i = 0; i < 900; i++)
        {
            var x = i is >= 240 and < 300 ? 1 : i is >= 310 and < 360 ? -1 : 0;
            var squash = i is >= 480 and < 500 ? 1 : 0;
            var jump = i == 600;
            var expected = reference.Step(Step, [new(b, x, squash, jump)]);
            var frame = actual.Step(Step, [new(a, x, squash, jump)]);
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(frame), $"frame={i}");
        }
    }

    [TestMethod]
    public void OldModuleIsUsableButCannotPretendToSupportLocalSceneBounds()
    {
        using var world = DangoPhysicsWorld.Create(Baseline);
        var id = world.Add(0, 0);
        Assert.IsTrue(world.Step(Step, [new(id)]).Succeeded);
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetHorizontalBounds(-4, 23)).Kind);
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.ClearHorizontalBounds()).Kind);
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.TranslateHorizontal(-1)).Kind);
        Assert.IsTrue(world.Step(Step, [new(id)]).Succeeded);
    }

    [TestMethod]
    public void ExpandedFloorAndPhysicalSidesKeepLongWalksInsideThePorch()
    {
        using var world = DangoPhysicsWorld.Create(Module);
        world.SetHorizontalBounds(-4, 23);
        var id = world.Add(0, 0);
        DangoBodyState? body = null;
        for (var i = 0; i < 2400; i++)
        {
            var frame = world.Step(Step, [new(id, Horizontal: 1)]);
            Assert.IsTrue(frame.Succeeded, $"right frame={i}, error={frame.ErrorCode}");
            body = frame.Bodies.Single();
            Assert.IsTrue(body.CoreY >= DangoPhysicsDefaults.Current.GroundHeight - .1, $"fall at right frame={i}");
            Assert.IsTrue(body.CoreX <= 23.05, $"escaped right side at frame={i}");
        }
        Assert.IsNotNull(body);
        Assert.IsTrue(body.CoreX > 18, "The expanded floor must support positions beyond the old +8 edge.");
        for (var i = 0; i < 3000; i++)
        {
            var frame = world.Step(Step, [new(id, Horizontal: -1)]);
            Assert.IsTrue(frame.Succeeded, $"left frame={i}, error={frame.ErrorCode}");
            body = frame.Bodies.Single();
            Assert.IsTrue(body.CoreY >= DangoPhysicsDefaults.Current.GroundHeight - .1, $"fall at left frame={i}");
            Assert.IsTrue(body.CoreX >= -4.05, $"escaped left side at frame={i}");
        }
        Assert.IsTrue(body.CoreX < -2);
    }

    [TestMethod]
    public void RepeatingBoundsDoesNotRebuildContactsOrChangeShape()
    {
        using var a = DangoPhysicsWorld.Create(Module);
        using var b = DangoPhysicsWorld.Create(Module);
        a.SetHorizontalBounds(-4, 23);
        b.SetHorizontalBounds(-4, 23);
        var ai = a.Add(0, 0);
        var bi = b.Add(0, 0);
        for (var i = 0; i < 600; i++)
        {
            a.SetHorizontalBounds(-4, 23);
            var actual = a.Step(Step, [new(ai, Horizontal: i > 300 ? 1 : 0)]);
            var expected = b.Step(Step, [new(bi, Horizontal: i > 300 ? 1 : 0)]);
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), $"frame={i}");
        }
    }

    [TestMethod]
    public void InvalidBoundsDoNotMutateTheExistingWorld()
    {
        using var world = DangoPhysicsWorld.Create(Module);
        var id = world.Add(0, 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.SetHorizontalBounds(double.NaN, 23));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.SetHorizontalBounds(4, -4));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetHorizontalBounds(-.1, .1));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetHorizontalBounds(5, 10));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetHorizontalBounds(-1025, 23));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetHorizontalBounds(-1e20, 1e20));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.TranslateHorizontal(1e20));
        Assert.IsTrue(world.Step(Step, [new(id)]).Succeeded);
    }

    [TestMethod]
    public void HorizontalTranslationPreservesIdsRelativeShapeAndVelocityThenDefaultFloorRestores()
    {
        using var world = DangoPhysicsWorld.Create(Module);
        using var reference = DangoPhysicsWorld.Create(Module);
        world.SetHorizontalBounds(-4, 23);
        reference.SetHorizontalBounds(-4, 23);
        var id = world.Add(12, 0);
        var ri = reference.Add(12, 0);
        DangoBodyState? previous = null;
        for (var i = 0; i < 400; i++)
        {
            previous = world.Step(Step, [new(id, Horizontal: i > 330 ? 1 : 0)]).Bodies.Single();
            reference.Step(Step, [new(ri, Horizontal: i > 330 ? 1 : 0)]);
        }
        var delta = -(double)previous!.CoreX;
        // The host handoff makes all coordinate changes without any intervening step.
        world.ClearHorizontalBounds();
        world.TranslateHorizontal(delta);
        world.ClearHorizontalBounds();
        var actual = world.Step(Step, [new(id)]).Bodies.Single();
        var expected = reference.Step(Step, [new(ri)]).Bodies.Single();
        Assert.AreEqual(id, actual.EntityId);
        // SetTransform and floor SetPolygon invalidate spatial/contact caches;
        // float32 integration after handoff is not bitwise translation-invariant.
        // 2e-4 world units is <= .016 logical pixel, not a visual movement budget.
        Assert.AreEqual(expected.CoreX + delta, actual.CoreX, 2e-4);
        Assert.AreEqual(expected.CoreY, actual.CoreY, 2e-4);
        Assert.AreEqual(expected.MaximumSpeed, actual.MaximumSpeed, 2e-4);
        for (var i = 0; i < 32; i++) Assert.AreEqual(expected.Contour[i], actual.Contour[i], 2e-4, $"point={i}");
        for (var i = 0; i < 600; i++)
        {
            var frame = world.Step(Step, [new(id)]);
            Assert.IsTrue(frame.Succeeded);
            Assert.IsTrue(frame.Bodies.Single().CoreY >= DangoPhysicsDefaults.Current.GroundHeight - .1);
        }
    }

    [TestMethod]
    public void AirborneWallContactIsNotGroundAndCannotCreateRepeatedWallJumps()
    {
        using var jumping = DangoPhysicsWorld.Create(Module);
        using var reference = DangoPhysicsWorld.Create(Module);
        jumping.SetHorizontalBounds(-2, 2);
        reference.SetHorizontalBounds(-2, 2);
        var id = jumping.Add(0, 5);
        var ri = reference.Add(0, 5);
        var airborneWallFrames = 0;
        for (var i = 0; i < 120; i++)
        {
            var frame = jumping.Step(Step, [new(id, Horizontal: 1, JumpPressed: true)]);
            var normal = reference.Step(Step, [new(ri, Horizontal: 1)]);
            Assert.IsTrue(frame.Succeeded && normal.Succeeded, $"frame={i}");
            var body = frame.Bodies.Single();
            var expected = normal.Bodies.Single();
            var floor = DangoPhysicsDefaults.Current.GroundHeight;
            var lowest = expected.CoreY + Enumerable.Range(0, 16).Min(n => expected.Contour[2 * n + 1]);
            if (lowest <= floor + .08) break;
            var right = expected.CoreX + Enumerable.Range(0, 16).Max(n => expected.Contour[2 * n]);
            if (right < 1.94) continue;
            airborneWallFrames++;
            Assert.AreEqual(0, body.GroundContactCount, $"A side wall is not floor support: frame={i}");
            Assert.IsFalse(body.IsGrounded, $"Airborne side contact must not enable ground controls: frame={i}");
            Assert.AreEqual(expected.CoreY, body.CoreY, .02,
                $"Repeated jump edges must not launch from a wall: frame={i}");
        }
        Assert.IsTrue(airborneWallFrames > 0, "The fixture must actually touch the wall while clear of the floor.");
    }
}
