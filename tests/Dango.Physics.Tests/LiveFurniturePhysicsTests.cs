using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class LiveFurniturePhysicsTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly DangoStaticPlatformDefinition Platform = new(0, 1.2, 3.4, .35);

    [TestMethod]
    public void ProbePrepareReadAndCancelPreserveCompletePublishedAndLiveState()
    {
        using var world = Create(out var id);
        world.Step(Dt, [new(id, Horizontal: .5, JumpPressed: true, DragActive: true, DragTargetX: -4, DragTargetY: 1)]);
        var original = Json(world.ReadFurnitureFrame());
        var revision = world.ReadFurnitureFrame().SceneRevision;
        for (var i = 0; i < 20; i++)
        {
            Assert.IsTrue(world.ProbeStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, revision).Allowed);
            var prepared = world.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, revision);
            Assert.IsTrue(prepared.Probe.Allowed);
            Assert.AreEqual(original, Json(world.ReadFurnitureFrame()));
            world.CancelPreparedStaticPlatformChange(prepared.Token!.Value);
            Assert.AreEqual(original, Json(world.ReadFurnitureFrame()));
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPreparedStaticPlatformChange(prepared.Token.Value));
        }
    }

    [TestMethod]
    public void InsertMoveAndDeleteChangeOneRevisionWithoutStepOrRigRebuild()
    {
        using var world = Create(out var id);
        world.Step(Dt, [new(id, Horizontal: .3, DragActive: true, DragTargetX: -4, DragTargetY: 1)]);
        var before = world.ReadFurnitureFrame();
        var inserted = Apply(world, DangoStaticPlatformChangeKind.Insert, Platform);
        AssertRawPreserved(before, inserted);
        Assert.AreEqual(before.SceneRevision + 1, inserted.SceneRevision);
        Assert.AreEqual(before.PhysicsStep, inserted.PhysicsStep);
        Assert.AreNotEqual(0UL, inserted.PlatformInstanceId);
        var moved = Apply(world, DangoStaticPlatformChangeKind.Move, Platform with { CenterX = 1 });
        AssertRawPreserved(inserted, moved);
        Assert.AreEqual(inserted.PlatformInstanceId, moved.PlatformInstanceId);
        var deleted = Apply(world, DangoStaticPlatformChangeKind.Delete, null);
        AssertRawPreserved(moved, deleted);
        Assert.IsNull(deleted.Platform);
        Assert.AreEqual(0UL, deleted.PlatformInstanceId);
        var reinserted = Apply(world, DangoStaticPlatformChangeKind.Insert, Platform);
        Assert.IsTrue(reinserted.PlatformInstanceId > inserted.PlatformInstanceId, "Deleted instance identities must not be reused.");
    }

    [TestMethod]
    public void RejectionsKeepOldPlatformRawIdsPositionsVelocitiesSupportAndCooldown()
    {
        using var world = Create(out var id);
        Apply(world, DangoStaticPlatformChangeKind.Insert, Platform);
        world.Step(Dt, [new(id, Horizontal: .6, JumpPressed: true)]);
        var old = world.ReadFurnitureFrame();
        var expected = Json(old);
        var overlapping = new DangoStaticPlatformDefinition(old.Physics.Bodies[0].CoreX, old.Physics.Bodies[0].CoreY, 1, .5);
        var cases = new[]
        {
            (DangoStaticPlatformChangeKind.Move, old.PlatformInstanceId, overlapping, old.SceneRevision, DangoStaticPlatformChangeRejection.Penetration),
            (DangoStaticPlatformChangeKind.Move, old.PlatformInstanceId, Platform with { CenterX = 8 }, old.SceneRevision, DangoStaticPlatformChangeRejection.OutsideRoom),
            (DangoStaticPlatformChangeKind.Move, old.PlatformInstanceId + 1, Platform, old.SceneRevision, DangoStaticPlatformChangeRejection.InvalidTarget),
            (DangoStaticPlatformChangeKind.Move, old.PlatformInstanceId, Platform, old.SceneRevision - 1, DangoStaticPlatformChangeRejection.StaleScene),
            (DangoStaticPlatformChangeKind.Insert, 0UL, Platform, old.SceneRevision, DangoStaticPlatformChangeRejection.InvalidTarget)
        };
        foreach (var (kind, target, candidate, revision, reason) in cases)
        {
            var result = world.PrepareStaticPlatformChange(kind, target, candidate, revision);
            Assert.AreEqual(reason, result.Probe.Rejection);
            Assert.IsNull(result.Token);
            Assert.AreEqual(expected, Json(world.ReadFurnitureFrame()));
        }
    }

    [TestMethod]
    public void StepInvalidatesPrepareAndOldProbeDoesNotAuthorizeCurrentPenetration()
    {
        using var world = Create(out var id, x: 0, y: 2.5);
        var candidate = new DangoStaticPlatformDefinition(0, 1.8, 1, .2);
        var frame = world.ReadFurnitureFrame();
        Assert.IsTrue(world.ProbeStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, candidate, frame.SceneRevision).Allowed);
        var token = world.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, candidate, frame.SceneRevision).Token!.Value;
        world.Step(Dt, [new(id)]);
        var afterStep = Json(world.ReadFurnitureFrame());
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPreparedStaticPlatformChange(token));
        Assert.AreEqual(afterStep, Json(world.ReadFurnitureFrame()));
        var found = false;
        for (var step = 0; step < 60; step++)
        {
            var body = world.Step(Dt, [new(id)]).Bodies[0];
            if (StaticPlatformGeometry.Penetration(body, candidate) <= .001) continue;
            found = true;
            var old = Json(world.ReadFurnitureFrame());
            var rejected = world.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, candidate, frame.SceneRevision);
            Assert.AreEqual(DangoStaticPlatformChangeRejection.Penetration, rejected.Probe.Rejection);
            Assert.AreEqual(old, Json(world.ReadFurnitureFrame()));
            break;
        }
        Assert.IsTrue(found, "The body must actually enter the old permitted candidate, verified by the independent oracle.");
    }

    [TestMethod]
    public void ActualPaintedTangencyIsAdmittedButSmallPenetrationIsRejected()
    {
        using var world = Create(out _, x: 0);
        var frame = world.ReadFurnitureFrame();
        var body = frame.Physics.Bodies[0];
        var edge = StaticPlatformGeometry.PaintedContour(body).Max(p => p.X);
        var tangent = new DangoStaticPlatformDefinition(edge + .5, body.CoreY, 1, 2);
        Assert.IsTrue(StaticPlatformGeometry.Penetration(body, tangent) <= 1e-7);
        Assert.IsTrue(world.ProbeStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, tangent, frame.SceneRevision).Allowed);
        var penetrated = tangent with { CenterX = tangent.CenterX - .0001 };
        Assert.IsTrue(StaticPlatformGeometry.Penetration(body, penetrated) > .00009);
        Assert.AreEqual(DangoStaticPlatformChangeRejection.Penetration,
            world.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, penetrated, frame.SceneRevision).Probe.Rejection);
    }

    [TestMethod]
    public void DeleteUnderTouchingBodyReleasesOnlyPlatformSupportWithoutImpulseOrGhostJump()
    {
        using var actual = Create(out var a, x: 0, y: 2.5);
        using var reference = Create(out var b, x: 0, y: 2.5);
        actual.SetStaticPlatform(Platform); reference.SetStaticPlatform(Platform);
        for (var step = 0; step < 180; step++)
        {
            actual.Step(Dt, [new(a)]); reference.Step(Dt, [new(b)]);
        }
        var before = actual.ReadFurnitureFrame();
        Assert.IsTrue(before.BodyDiagnostics[0].PlatformContact.IsSupported);
        var deleted = Apply(actual, DangoStaticPlatformChangeKind.Delete, null);
        AssertRawPreserved(before, deleted);
        Assert.IsFalse(deleted.BodyDiagnostics[0].IsGrounded);
        reference.ClearStaticPlatform();
        var initialY = deleted.Physics.Bodies[0].CoreY;
        for (var step = 0; step < 40; step++)
        {
            var observed = actual.Step(Dt, [new(a, JumpPressed: true)]);
            var expected = reference.Step(Dt, [new(b)]);
            Assert.AreEqual(Json(expected), Json(observed), $"Deleted platform left stale jump eligibility or injected motion at {step}.");
            Assert.IsFalse(actual.ReadFurnitureFrame().BodyDiagnostics[0].PlatformContact.HasPlatform);
        }
        Assert.IsTrue(actual.ReadFurnitureFrame().Physics.Bodies[0].CoreY < initialY - .1);
    }

    [TestMethod]
    public void DeletePreservesRealFloorSupport()
    {
        using var world = Create(out var id);
        Apply(world, DangoStaticPlatformChangeKind.Insert, Platform);
        for (var step = 0; step < 180; step++) world.Step(Dt, [new(id)]);
        Assert.IsTrue(world.ReadFurnitureFrame().BodyDiagnostics[0].IsGrounded);
        Assert.IsTrue(Apply(world, DangoStaticPlatformChangeKind.Delete, null).BodyDiagnostics[0].IsGrounded);
        var y = world.ReadFurnitureFrame().Physics.Bodies[0].CoreY;
        for (var step = 0; step < 10; step++) world.Step(Dt, [new(id, JumpPressed: step == 0)]);
        Assert.IsTrue(world.ReadFurnitureFrame().Physics.Bodies[0].CoreY > y + .1, "The real floor must still allow normal jumping.");
    }

    [TestMethod]
    public void TokensAreWorldBoundSingleUseAndDelayedCancellationCannotCancelNewerToken()
    {
        using var first = Create(out _);
        using var second = Create(out _);
        var initial = first.ReadFurnitureFrame();
        var old = first.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, initial.SceneRevision).Token!.Value;
        Assert.ThrowsExactly<DangoPhysicsException>(() => second.ApplyPreparedStaticPlatformChange(old));
        Assert.ThrowsExactly<DangoPhysicsException>(() => second.CancelPreparedStaticPlatformChange(old));
        var newer = first.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, initial.SceneRevision).Token!.Value;
        first.CancelPreparedStaticPlatformChange(old);
        var applied = first.ApplyPreparedStaticPlatformChange(newer);
        var saved = Json(applied);
        Assert.AreEqual(newer.TokenId, applied.LastAppliedToken);
        Assert.ThrowsExactly<DangoPhysicsException>(() => first.ApplyPreparedStaticPlatformChange(newer));
        Assert.AreEqual(saved, Json(first.ReadFurnitureFrame()));
    }

    [TestMethod]
    [DataRow("bounds")]
    [DataRow("reset")]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("set")]
    [DataRow("clear")]
    public void SceneChangesInvalidatePreparedToken(string change)
    {
        using var world = Create(out var id);
        if (change == "clear") world.SetStaticPlatform(Platform);
        var before = world.ReadFurnitureFrame();
        var kind = change == "clear" ? DangoStaticPlatformChangeKind.Move : DangoStaticPlatformChangeKind.Insert;
        var token = world.PrepareStaticPlatformChange(kind, before.PlatformInstanceId, Platform, before.SceneRevision).Token!.Value;
        switch (change)
        {
            case "bounds": world.SetHorizontalBounds(-9, 9); break;
            case "reset": world.Reset(); break;
            case "add": world.Add(5, 0); break;
            case "remove": world.Remove(id); break;
            case "set": world.SetStaticPlatform(Platform); break;
            case "clear": world.ClearStaticPlatform(); break;
        }
        var old = Json(world.ReadFurnitureFrame());
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPreparedStaticPlatformChange(token));
        Assert.AreEqual(old, Json(world.ReadFurnitureFrame()));
    }

    [TestMethod]
    public void MissingCapabilityAndUnsupportedCompositionCannotPretendToEdit()
    {
        using var old = DangoPhysicsWorld.Create(PhysicsTestLibraries.StaticPlatformBaseline);
        var id = old.Add(0, 0);
        Assert.IsFalse(old.SupportsLiveFurnitureEditing);
        Assert.ThrowsExactly<DangoPhysicsException>(() => old.ReadFurnitureFrame());
        Assert.IsTrue(old.Step(Dt, [new(id)]).Succeeded);
        using var two = Create(out _);
        two.Add(5, 0);
        var frame = two.ReadFurnitureFrame();
        Assert.AreEqual(DangoStaticPlatformChangeRejection.UnsupportedComposition,
            two.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, frame.SceneRevision).Probe.Rejection);
        using var ball = Create(out _);
        ball.AddRoundBall(new(3, 1));
        var ballFrame = ball.ReadFurnitureFrame();
        Assert.AreEqual(DangoStaticPlatformChangeRejection.UnsupportedComposition,
            ball.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, ballFrame.SceneRevision).Probe.Rejection);
        using var unbounded = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var empty = unbounded.ReadFurnitureFrame();
        Assert.AreEqual(DangoStaticPlatformChangeRejection.MissingBounds,
            unbounded.ProbeStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, empty.SceneRevision).Rejection);
    }

    [TestMethod]
    public void SeparateNativeModuleCopiesCannotShareOwnerGenerationOrPreparedTokens()
    {
        var directory = Path.Combine(PhysicsTestLibraries.Root, "artifacts", "furniture", "live-native-c1", "copy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "first.dll");
        var secondPath = Path.Combine(directory, "second.dll");
        File.Copy(PhysicsTestLibraries.Unified, firstPath);
        File.Copy(PhysicsTestLibraries.Unified, secondPath);
        using var first = DangoPhysicsWorld.Create(firstPath);
        using var second = DangoPhysicsWorld.Create(secondPath);
        first.SetHorizontalBounds(-8, 8); second.SetHorizontalBounds(-8, 8);
        first.Add(-5, 0); second.Add(-5, 0);
        var a = first.ReadFurnitureFrame(); var b = second.ReadFurnitureFrame();
        Assert.AreNotEqual(a.WorldGeneration, b.WorldGeneration, "DLL-local Box2D IDs alone can collide across candidate copies.");
        var token = first.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, a.SceneRevision).Token!.Value;
        _ = second.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, b.SceneRevision);
        var original = Json(second.ReadFurnitureFrame());
        Assert.ThrowsExactly<DangoPhysicsException>(() => second.ApplyPreparedStaticPlatformChange(token));
        Assert.AreEqual(original, Json(second.ReadFurnitureFrame()));
        first.ApplyPreparedStaticPlatformChange(token);
    }

    [TestMethod]
    public void AllNewOperationsEnforceOwnerAndDisposedLifetime()
    {
        var world = Create(out _);
        var frame = world.ReadFurnitureFrame();
        var token = world.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, frame.SceneRevision).Token!.Value;
        Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(world.ReadFurnitureFrame).GetAwaiter().GetResult());
        Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => world.ApplyPreparedStaticPlatformChange(token)).GetAwaiter().GetResult());
        world.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.ReadFurnitureFrame());
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.CancelPreparedStaticPlatformChange(token));
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.ProbeStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0, Platform, frame.SceneRevision));
    }

    private static DangoPhysicsWorld Create(out DangoEntityId id, double x = -5, double y = 0)
    {
        var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        world.SetHorizontalBounds(-8, 8);
        id = world.Add(x, y);
        return world;
    }
    private static DangoFurnitureFrame Apply(DangoPhysicsWorld world, DangoStaticPlatformChangeKind kind, DangoStaticPlatformDefinition? candidate)
    {
        var old = world.ReadFurnitureFrame();
        var prepared = world.PrepareStaticPlatformChange(kind, kind == DangoStaticPlatformChangeKind.Insert ? 0 : old.PlatformInstanceId,
            candidate, old.SceneRevision);
        Assert.IsTrue(prepared.Probe.Allowed, prepared.Probe.Rejection.ToString());
        return world.ApplyPreparedStaticPlatformChange(prepared.Token!.Value);
    }
    private static void AssertRawPreserved(DangoFurnitureFrame before, DangoFurnitureFrame after)
    {
        Assert.AreEqual(before.WorldGeneration, after.WorldGeneration);
        Assert.AreEqual(before.PhysicsStep, after.PhysicsStep);
        Assert.AreEqual(before.Physics.Bodies.Count, after.Physics.Bodies.Count);
        for (var index = 0; index < before.BodyDiagnostics.Count; index++)
        {
            var a = before.BodyDiagnostics[index]; var b = after.BodyDiagnostics[index];
            Assert.AreEqual(a.EntityId, b.EntityId);
            Assert.AreEqual(Json(a.Nodes), Json(b.Nodes), "Every raw ID, position and velocity must survive the operation.");
            Assert.AreEqual(a.SecondsSinceJump, b.SecondsSinceJump);
            Assert.AreEqual(a.DragActive, b.DragActive);
            Assert.AreEqual(before.Physics.Bodies[index].SelectedDragNode, after.Physics.Bodies[index].SelectedDragNode);
            Assert.AreEqual(Json(before.Physics.Bodies[index].Contour), Json(after.Physics.Bodies[index].Contour));
        }
    }
    private static string Json(object value) => JsonSerializer.Serialize(value);
}
