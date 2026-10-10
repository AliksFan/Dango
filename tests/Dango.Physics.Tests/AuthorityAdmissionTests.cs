using System.Text.Json;
using Dango.Physics;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class AuthorityAdmissionTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static DangoPhysicsWorld Create() => DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 2);

    [TestMethod]
    public void AuthorityStartsAtZeroTickAndRejectsUnsupportedRoles()
    {
        using var world = Create();
        Assert.AreEqual(DangoAuthorityCapabilities.SemanticFrames, world.AuthorityCapabilities);
        world.EnableAuthority();
        var empty = world.ReadAuthorityFrame();
        Assert.AreEqual(0ul, empty.Tick);
        Assert.AreEqual(0, empty.Bodies.Count);
        var id = world.Add(0, 0);
        Assert.AreEqual(0ul, world.ReadAuthorityFrame().Tick);
        world.Step(Dt, [new(id)]);
        Assert.AreEqual(1ul, world.ReadAuthorityFrame().Tick);
        var late = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 2);
        using (late)
        {
            var lateId = late.Add(0, 0);
            late.Step(Dt, [new(lateId)]);
            Assert.ThrowsExactly<DangoPhysicsException>(late.EnableAuthority);
        }
        using var three = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 3);
        Assert.AreEqual(DangoPhysicsFailureKind.UnsupportedCapability,
            Assert.ThrowsExactly<DangoPhysicsException>(three.EnableAuthority).Kind);
    }

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public void EnabledAndRepeatedSemanticReadsMatchIndependentSourceBaselineBitForBit(int count, bool stack)
    {
        var baselinePath = Environment.GetEnvironmentVariable("DANGO_AUTHORITY_BASELINE_MODULE_PATH") ??
            throw new InvalidOperationException("Set DANGO_AUTHORITY_BASELINE_MODULE_PATH to the independently rebuilt frozen source module.");
        using var baseline = DangoPhysicsWorld.Create(baselinePath, 2);
        using var candidate = Create();
        candidate.EnableAuthority();
        var baselineIds = Enumerable.Range(0, count).Select(index => baseline.Add(
            index == 0 ? 0 : stack ? 0 : 2.8, index == 1 && stack ? 1.65 : 0)).ToArray();
        var ids = Enumerable.Range(0, count).Select(index => candidate.Add(
            index == 0 ? 0 : stack ? 0 : 2.8, index == 1 && stack ? 1.65 : 0)).ToArray();
        for (var tick = 0; tick < 1800; tick++)
        {
            DangoPhysicsInput Input(DangoEntityId id, int index) => new(id,
                Horizontal: index == 0 ? tick is >= 300 and < 420 ? 1 : tick is >= 420 and < 540 ? -1 : 0 :
                    !stack && tick is >= 1000 and < 1180 ? -1 : 0,
                Downward: index == 0 && tick is >= 600 and < 660 ? 1 : 0,
                JumpPressed: index == 0 && tick == 720 || index == 1 && stack && tick == 1200,
                DragActive: index == 0 && tick is >= 840 and < 960,
                DragTargetX: index == 0 ? -.5 : .5, DragTargetY: 1.4);
            var expected = baseline.Step(Dt, baselineIds.Select((id, index) => Input(id, index)).ToArray());
            var actual = candidate.Step(Dt, ids.Select((id, index) => Input(id, index)).ToArray());
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), $"Source trajectory changed at tick {tick}.");
            for (var body = 0; body < count; body++)
                CollectionAssert.AreEqual(Raw(expected.Bodies[body]).Select(BitConverter.SingleToInt32Bits).ToArray(),
                    Raw(actual.Bodies[body]).Select(BitConverter.SingleToInt32Bits).ToArray(), $"Float bits changed at tick {tick}, body {body}.");
            var authority = candidate.ReadAuthorityFrame();
            Assert.IsTrue(authority.Succeeded);
            Assert.AreEqual((ulong)tick + 1, authority.Tick);
            Assert.AreEqual(JsonSerializer.Serialize(authority), JsonSerializer.Serialize(candidate.ReadAuthorityFrame()));
            foreach (var body in authority.Bodies)
            {
                var legacy = actual.Bodies.Single(value => value.EntityId == body.EntityId);
                Assert.AreEqual(legacy.CoreX, body.CoreX);
                Assert.AreEqual(legacy.CoreY, body.CoreY);
                Assert.AreEqual(legacy.IsGrounded, body.FloorGrounded);
            }
        }
    }

    [TestMethod]
    public void SupportedBodyCanJumpWithRealDispositionAndRemovalClearsItsSupport()
    {
        using var world = Create();
        world.EnableAuthority();
        var bottom = world.Add(0, 0);
        var top = world.Add(0, 1.65);
        DangoAuthorityFrame? frame = null;
        for (var tick = 0; tick < 180; tick++)
        {
            world.Step(Dt, [new(bottom), new(top)]);
            frame = world.ReadAuthorityFrame();
            var upper = frame.Bodies.Single(value => value.EntityId == top);
            if (upper.SupportEntityId == bottom && upper.CanJump) break;
        }
        Assert.IsNotNull(frame);
        var supported = frame.Bodies.Single(value => value.EntityId == top);
        Assert.AreEqual(bottom, supported.SupportEntityId);
        Assert.IsFalse(supported.FloorGrounded, "Standing on another Dango must not be mislabeled as floor contact.");
        world.Step(Dt, [new(bottom), new(top, JumpPressed: true)]);
        Assert.AreEqual(DangoAuthorityJumpResult.AppliedSupport, world.ReadAuthorityFrame().Bodies.Single(value => value.EntityId == top).JumpResult);
        world.Step(Dt, [new(bottom), new(top, JumpPressed: true)]);
        Assert.IsTrue(world.ReadAuthorityFrame().Bodies.Single(value => value.EntityId == top).JumpResult is
            DangoAuthorityJumpResult.RejectedAirborne or DangoAuthorityJumpResult.RejectedCooldown);
        world.Reset();
        for (var tick = 0; tick < 60; tick++) world.Step(Dt, [new(bottom), new(top)]);
        var previousTick = world.ReadAuthorityFrame().Tick;
        world.Remove(bottom);
        var afterRemoval = world.ReadAuthorityFrame();
        Assert.AreEqual(previousTick, afterRemoval.Tick);
        Assert.AreEqual(top, afterRemoval.Bodies.Single().EntityId);
        Assert.IsNull(afterRemoval.Bodies.Single().SupportEntityId);
        Assert.AreEqual(0, afterRemoval.Pairs.Count);
        for (var tick = 0; tick < 180; tick++) world.Step(Dt, [new(top)]);
        Assert.IsTrue(world.ReadAuthorityFrame().Bodies.Single().FloorGrounded);
    }

    [TestMethod]
    public void CompleteCheckpointIsExplicitlyUnsupportedAndImportPreservesTheLiveWorld()
    {
        using var world = Create();
        world.EnableAuthority();
        var id = world.Add(0, 0);
        for (var tick = 0; tick < 100; tick++) world.Step(Dt, [new(id, Horizontal: 1)]);
        var before = JsonSerializer.Serialize(world.ReadAuthorityFrame());
        Assert.AreEqual(DangoPhysicsFailureKind.UnsupportedCapability, Assert.ThrowsExactly<DangoPhysicsException>(() => world.ExportCheckpoint()).Kind);
        Assert.AreEqual(DangoPhysicsFailureKind.UnsupportedCapability,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.ImportCheckpoint([1, 2, 3, 4])).Kind);
        Assert.AreEqual(before, JsonSerializer.Serialize(world.ReadAuthorityFrame()));
        Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
    }

    [TestMethod]
    public void TargetsAreAtomicAndPresentationCannotSubmitActionsOrReadAuthority()
    {
        using var world = Create();
        var a = world.Add(0, 0);
        var b = world.Add(2.8, 0);
        world.Step(Dt, [new(a), new(b)]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.ApplyPresentationTargets([
            new(a, 3, 2, 1, 0), new(b, float.NaN, 0, 0, 0)]));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPresentationTargets([new(a, 3, 2, 1, 0)]));
        world.ApplyPresentationTargets([new(a, 0, 0, 0, 0), new(b, 2.8f, 0, 0, 0)]);
        Assert.AreEqual(DangoPhysicsFailureKind.ModeConflict, Assert.ThrowsExactly<DangoPhysicsException>(world.EnableAuthority).Kind);
        Assert.AreEqual(DangoPhysicsFailureKind.ModeConflict, Assert.ThrowsExactly<DangoPhysicsException>(() => world.ReadAuthorityFrame()).Kind);
        Assert.AreEqual(DangoPhysicsFailureKind.ModeConflict,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.Step(Dt, [new(a, Horizontal: 1), new(b)])).Kind);
        Assert.IsTrue(world.Step(Dt, [new(a), new(b)]).Succeeded);
        world.Remove(b);
        world.ApplyPresentationTargets([new(a, 0, 0, 0, 0)]);
        var replacement = world.Add(2.8, 0);
        world.ApplyPresentationTargets([new(a, 0, 0, 0, 0), new(replacement, 2.8f, 0, 0, 0)]);
    }

    [TestMethod]
    public void AuthorityRejectsPresentationTargetsAndCrossThreadReads()
    {
        using var world = Create();
        world.EnableAuthority();
        var id = world.Add(0, 0);
        Assert.AreEqual(DangoPhysicsFailureKind.ModeConflict,
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPresentationTargets([new(id, 0, 0, 0, 0)])).Kind);
        Task.Run(() => Assert.ThrowsExactly<InvalidOperationException>(() => world.ReadAuthorityFrame())).GetAwaiter().GetResult();
    }

    private static float[] Raw(DangoBodyState body) => [.. body.Contour,
        body.CoreX, body.CoreY, body.SignedArea, body.CentroidX, body.CentroidY, body.MaximumSpeed,
        body.GroundContactCount, body.IsGrounded ? 1 : 0, body.SelectedDragNode, body.PointCount];
}
