using System.Collections;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
[DoNotParallelize]
public sealed class FurnitureCollectionTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static DangoPhysicsWorld Create(out DangoEntityId id, double x = -6, double y = 0)
    {
        var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        world.SetHorizontalBounds(-15, 15); id = world.Add(x, y); return world;
    }
    private static DangoFurnitureDefinition Platform(double x = 2, double y = .5, double width = 3.4) =>
        new(DangoFurnitureKind.StaticPlatform, new(x, y, width, .35));
    private static DangoFurnitureDefinition Ball(double x = 2, double y = 3) =>
        new(DangoFurnitureKind.RoundBall, Ball: new(x, y, RadiusQ: 1.1, Mass: .02));
    private static DangoFurnitureCollectionFrame Apply(DangoPhysicsWorld world, DangoFurnitureChangeKind kind,
        DangoFurnitureDefinition? definition, ulong id = 0)
    {
        var old = world.ReadFurnitureCollectionFrame();
        var prepared = world.PrepareFurnitureChange(kind, id, definition, old.SceneRevision);
        Assert.IsTrue(prepared.Probe.Allowed, prepared.Probe.Rejection.ToString());
        var frame = world.ApplyPreparedFurnitureChange(prepared.Token!.Value);
        Assert.AreEqual(old.SceneRevision + 1, frame.SceneRevision); Assert.AreEqual(old.PhysicsStep, frame.PhysicsStep);
        return frame;
    }

    [TestMethod]
    public void CollectionSupportsThirtyDistinctInstancesAndRejectsThirtyFirstWithoutMutation()
    {
        using var world = Create(out _, -13);
        for (var i = 0; i < 30; i++) Apply(world, DangoFurnitureChangeKind.Insert, Platform(-10 + i % 10 * 2, 1 + i / 10, .5));
        var before = world.ReadFurnitureCollectionFrame(); var bytes = Json(before);
        Assert.AreEqual(30, before.Items.Count); Assert.AreEqual(30, before.Items.Select(i => i.InstanceId).Distinct().Count());
        var result = world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0, Platform(11, 1, .5), before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.CapacityExceeded, result.Probe.Rejection); Assert.IsNull(result.Token);
        Assert.AreEqual(bytes, Json(world.ReadFurnitureCollectionFrame()));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList)before.Items).Add(before.Items[0]));
    }

    [TestMethod]
    public void MixedInsertMoveDeletePreserveEveryUntargetedEntityAndRawRig()
    {
        using var world = Create(out var id);
        Apply(world, DangoFurnitureChangeKind.Insert, Platform());
        var before = Apply(world, DangoFurnitureChangeKind.Insert, Ball(7));
        for (var step = 0; step < 15; step++) world.Step(Dt, [new(id, Horizontal: .4)]);
        before = world.ReadFurnitureCollectionFrame();
        var target = before.Items.Single(i => i.Definition.Kind == DangoFurnitureKind.RoundBall);
        var after = Apply(world, DangoFurnitureChangeKind.Move, Ball(8), target.InstanceId);
        Assert.AreEqual(Json(before.BodyDiagnostics), Json(after.BodyDiagnostics), "Moving furniture must preserve every raw body ID, transform, velocity and input state.");
        Assert.AreEqual(Json(before.Physics), Json(after.Physics));
        Assert.AreEqual(Json(before.Items.Single(i => i.Definition.Kind == DangoFurnitureKind.StaticPlatform)),
            Json(after.Items.Single(i => i.Definition.Kind == DangoFurnitureKind.StaticPlatform)));
        var moved = after.Items.Single(i => i.InstanceId == target.InstanceId).BallState!;
        Assert.AreEqual(8d, moved.CenterQX); Assert.AreEqual(3d, moved.CenterQY);
        Assert.AreEqual(0d, moved.VelocityQX); Assert.AreEqual(0d, moved.VelocityQY); Assert.AreEqual(0d, moved.AngularVelocity);
        Assert.AreEqual(0UL, moved.IntegrationCount);
        var final = Apply(world, DangoFurnitureChangeKind.Delete, null, target.InstanceId);
        Assert.AreEqual(1, final.Items.Count);
        var oldTarget = world.ProbeFurnitureChange(DangoFurnitureChangeKind.Delete, target.InstanceId, null, final.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.InvalidTarget, oldTarget.Rejection);
        var replaced = Apply(world, DangoFurnitureChangeKind.Insert, Ball(8));
        Assert.AreNotEqual(target.InstanceId, replaced.Items.Single(i => i.Definition.Kind == DangoFurnitureKind.RoundBall).InstanceId);
    }

    [TestMethod]
    public void ProbePrepareReadAndCancelPreserveCompleteFrameAndStepsInvalidateTokens()
    {
        using var world = Create(out var id);
        var before = world.ReadFurnitureCollectionFrame();
        for (var i = 0; i < 20; i++)
        {
            var result = world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0, Ball(), before.SceneRevision);
            Assert.IsTrue(result.Probe.Allowed); Assert.AreEqual(Json(before), Json(world.ReadFurnitureCollectionFrame()));
            world.CancelPreparedFurnitureChange(result.Token!.Value);
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPreparedFurnitureChange(result.Token.Value));
            Assert.AreEqual(Json(before), Json(world.ReadFurnitureCollectionFrame()));
        }
        var token = world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0, Ball(), before.SceneRevision).Token!.Value;
        world.Step(Dt, [new(id)]);
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.ApplyPreparedFurnitureChange(token));
        Assert.AreEqual(0, world.ReadFurnitureCollectionFrame().Items.Count);
    }

    [TestMethod]
    public void CollisionsRejectCurrentBodiesOtherFurnitureAndSavedBirthAnchors()
    {
        using var world = Create(out _);
        var initial = world.ReadFurnitureCollectionFrame();
        var body = initial.Physics.Bodies.Single();
        var overlapBody = world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0,
            Ball(body.CoreX * 4d / 3, body.CoreY), initial.SceneRevision);
        Assert.IsFalse(overlapBody.Probe.Allowed); Assert.IsNull(overlapBody.Token);
        var before = Apply(world, DangoFurnitureChangeKind.Insert, Platform());
        var overlap = world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0, Ball(8d / 3, .8), before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.Penetration, overlap.Probe.Rejection);
        Assert.AreEqual(Json(before), Json(world.ReadFurnitureCollectionFrame()));
        before = Apply(world, DangoFurnitureChangeKind.Insert, Ball(7));
        var existing = before.Items.Single(i => i.Definition.Kind == DangoFurnitureKind.RoundBall);
        var duplicate = world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0, existing.Definition, before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.Penetration, duplicate.Probe.Rejection);
        var outside = world.ProbeFurnitureChange(DangoFurnitureChangeKind.Move, existing.InstanceId, Ball(100), before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.OutsideRoom, outside.Rejection);
        var stale = world.ProbeFurnitureChange(DangoFurnitureChangeKind.Delete, existing.InstanceId, null, before.SceneRevision - 1);
        Assert.AreEqual(DangoFurnitureChangeRejection.StaleScene, stale.Rejection);
    }

    [TestMethod]
    public void NativeBallFallsOntoPlatformAndFallsAgainAfterItsDeletion()
    {
        using var world = Create(out var id);
        Apply(world, DangoFurnitureChangeKind.Insert, Platform());
        var before = Apply(world, DangoFurnitureChangeKind.Insert, Ball(8d / 3));
        for (var step = 0; step < 480; step++) Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
        before = world.ReadFurnitureCollectionFrame();
        var ball = before.Items.Single(i => i.BallState is not null).BallState!;
        Assert.IsTrue(ball.CenterQY >= .675 + 1.1 - .001, "A drawn platform without native ball contact must fail.");
        Assert.IsTrue(ball.CenterQY < .675 + 1.1 + .01);
        var platform = before.Items.Single(i => i.Definition.Kind == DangoFurnitureKind.StaticPlatform);
        var after = Apply(world, DangoFurnitureChangeKind.Delete, null, platform.InstanceId);
        var retained = after.Items.Single().BallState!;
        Assert.AreEqual(ball.CenterQY, retained.CenterQY); Assert.AreEqual(ball.VelocityQY, retained.VelocityQY);
        for (var step = 0; step < 180; step++) Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
        Assert.IsTrue(world.ReadFurnitureCollectionFrame().Items.Single().BallState!.CenterQY < .5);
    }

    [TestMethod]
    public void MultipleBallsExchangeMomentumRemainSeparateAndResetToConfirmedAnchors()
    {
        using var world = Create(out var id);
        Apply(world, DangoFurnitureChangeKind.Insert, Ball(2, .38));
        Apply(world, DangoFurnitureChangeKind.Insert, Ball(4.1, 2));
        var separatedFrames = 0;
        for (var step = 0; step < 480; step++)
        {
            Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
            var frame = world.ReadFurnitureCollectionFrame(); var a = frame.Items[0].BallState!; var b = frame.Items[1].BallState!;
            Assert.IsTrue(Math.Sqrt(Math.Pow(a.CenterQX - b.CenterQX, 2) + Math.Pow(a.CenterQY - b.CenterQY, 2)) >= 2.2 - .001);
            Assert.AreEqual(frame.PhysicsStep, a.Step); Assert.AreEqual(frame.SceneRevision, b.SceneRevision);
            separatedFrames++;
        }
        var final = world.ReadFurnitureCollectionFrame();
        Assert.IsTrue(Math.Abs(final.Items[1].BallState!.CenterQX - 4.1) > .01,
            "The falling second ball must actually contact and move, not remain a static drawing.");
        var ids = final.Items.Select(i => i.InstanceId).ToArray();
        world.Reset(); var reset = world.ReadFurnitureCollectionFrame();
        CollectionAssert.AreEqual(ids, reset.Items.Select(i => i.InstanceId).ToArray());
        Assert.AreEqual(2d, reset.Items[0].BallState!.CenterQX); Assert.AreEqual(4.1d, reset.Items[1].BallState!.CenterQX);
        Assert.AreEqual(0d, reset.Items[1].BallState!.VelocityQX); Assert.AreEqual(480, separatedFrames);
    }

    [TestMethod]
    public void LegacySingleFurnitureApisRefuseCollectionAndEmptyCollectionCanDetach()
    {
        using var world = Create(out _);
        var before = Apply(world, DangoFurnitureChangeKind.Insert, Ball());
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.ReadRoundBallFrame());
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.ReadFurnitureFrame());
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetStaticPlatform(new(0, 1, 3.4, .35)));
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.AddRoundBall(new(5, 2)));
        Apply(world, DangoFurnitureChangeKind.Delete, null, before.Items.Single().InstanceId);
        Assert.AreEqual(0, world.ReadFurnitureCollectionFrame().Items.Count);
        world.ReadFurnitureFrame(); world.ReadRoundBallFrame(); world.ClearHorizontalBounds(); world.TranslateHorizontal(1);
        Assert.AreEqual(0, world.ReadFurnitureCollectionFrame().Items.Count);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void NoCollectionPreservesAcceptedModuleEveryPublicBodyFieldAtZeroTolerance(int count)
    {
        using var candidate = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var baselinePath = Environment.GetEnvironmentVariable("DANGO_FURNITURE_COLLECTION_BASELINE_PATH") ??
            throw new InvalidOperationException("Set DANGO_FURNITURE_COLLECTION_BASELINE_PATH to the independent accepted ordinary-room furniture module.");
        using var baseline = DangoPhysicsWorld.Create(baselinePath);
        var actualIds = Enumerable.Range(0, count).Select(i => candidate.Add(-2 + i, i * .5)).ToArray();
        var expectedIds = Enumerable.Range(0, count).Select(i => baseline.Add(-2 + i, i * .5)).ToArray();
        for (var step = 0; step < 600; step++)
        {
            var horizontal = step % 240 < 120 ? .7 : -.7; var jump = step % 90 == 0;
            var actual = candidate.Step(Dt, actualIds.Select(id => new DangoPhysicsInput(id, horizontal, JumpPressed: jump)).ToArray());
            var expected = baseline.Step(Dt, expectedIds.Select(id => new DangoPhysicsInput(id, horizontal, JumpPressed: jump)).ToArray());
            Assert.AreEqual(Json(expected), Json(actual), $"No collection frame {step} changed.");
        }
    }

    [TestMethod]
    public void WorstThirtyMixedCollectionRunsRealStepsWithoutLosingEntitiesOrContactSeparation()
    {
        using var world = Create(out var id, -13);
        for (var i = 0; i < 10; i++) Apply(world, DangoFurnitureChangeKind.Insert, Platform(-10 + i * 2, 1, 1));
        for (var i = 0; i < 20; i++)
            Apply(world, DangoFurnitureChangeKind.Insert, new(DangoFurnitureKind.RoundBall,
                Ball: new(-11 + i % 10 * 2.3, 3 + i / 10 * 1.2, .4, .02)));
        var originalIds = world.ReadFurnitureCollectionFrame().Items.Select(i => i.InstanceId).ToArray();
        var times = new List<double>();
        for (var step = 0; step < 600; step++)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded, $"Worst collection native step {step} failed.");
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            var frame = world.ReadFurnitureCollectionFrame();
            CollectionAssert.AreEqual(originalIds, frame.Items.Select(i => i.InstanceId).ToArray());
            foreach (var a in frame.Items.Where(i => i.BallState is not null))
                foreach (var b in frame.Items.Where(i => i.BallState is not null && i.InstanceId < a.InstanceId))
                {
                    var state = a.BallState!; var other = b.BallState!;
                    Assert.IsTrue(Math.Sqrt(Math.Pow(state.CenterQX - other.CenterQX, 2) + Math.Pow(state.CenterQY - other.CenterQY, 2)) >= .8 - .001);
                }
        }
        times.Sort(); TestContext.WriteLine($"Real 30-item mixed native workload: 600 steps; step P95={times[569]:R} ms, max={times[^1]:R} ms; no rendering/FPS claim.");
    }

    [TestMethod]
    public void IndependentNativeByteLayoutAndWrongSizeCannotWritePastCallerBuffer()
    {
        using var world = Create(out _);
        var frame = Apply(world, DangoFurnitureChangeKind.Insert, Ball(7));
        var library = NativeLibrary.Load(PhysicsTestLibraries.Unified); var buffer = Marshal.AllocHGlobal(13976);
        try
        {
            var handle = (nint)typeof(DangoPhysicsWorld).GetField("world", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(world)!;
            var getter = Marshal.GetDelegateForFunctionPointer<RawCollectionGetter>(NativeLibrary.GetExport(library, "dango_physics_world_get_furniture_collection_frame"));
            var size = Marshal.GetDelegateForFunctionPointer<RawIntegerGetter>(NativeLibrary.GetExport(library, "dango_physics_get_furniture_collection_frame_size"));
            Assert.AreEqual(13944, size(), "Independently frozen C offsets: 1936-byte body frame + 8 header + 30 * 400 items.");
            var sentinel = Enumerable.Repeat((byte)0xCD, 13976).ToArray(); Marshal.Copy(sentinel, 0, buffer, sentinel.Length);
            Assert.AreNotEqual(0, getter(handle, buffer, 13943));
            var bytes = new byte[sentinel.Length]; Marshal.Copy(buffer, bytes, 0, bytes.Length); CollectionAssert.AreEqual(sentinel, bytes);
            Assert.AreEqual(0, getter(handle, buffer, 13944)); Marshal.Copy(buffer, bytes, 0, bytes.Length);
            Assert.AreEqual(frame.WorldGeneration, BitConverter.ToUInt64(bytes, 0));
            Assert.AreEqual(frame.SceneRevision, BitConverter.ToUInt64(bytes, 8));
            Assert.AreEqual(1, BitConverter.ToInt32(bytes, 1936));
            Assert.AreEqual(frame.Items.Single().InstanceId, BitConverter.ToUInt64(bytes, 1944));
            Assert.AreEqual(2, BitConverter.ToInt32(bytes, 1952));
            Assert.AreEqual(1.1, BitConverter.ToDouble(bytes, 1944 + 56));
            Assert.AreEqual(.02, BitConverter.ToDouble(bytes, 1944 + 64));
            Assert.AreEqual(7d, BitConverter.ToDouble(bytes, 1944 + 144));
            Assert.AreEqual(frame.SceneRevision, BitConverter.ToUInt64(bytes, 1944 + 120));
            Assert.IsTrue(bytes.AsSpan(13944).SequenceEqual(sentinel.AsSpan(13944)));
            Assert.AreEqual(Json(frame), Json(world.ReadFurnitureCollectionFrame()), "Raw reads do not integrate.");
        }
        finally { Marshal.FreeHGlobal(buffer); NativeLibrary.Free(library); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RawCollectionGetter(nint handle, nint buffer, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RawIntegerGetter();

    [TestMethod]
    public void OwnerThreadAndDisposedWorldChecksApplyToCollection()
    {
        var world = Create(out _);
        Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(world.ReadFurnitureCollectionFrame).GetAwaiter().GetResult());
        world.Dispose(); Assert.ThrowsExactly<ObjectDisposedException>(() => world.ReadFurnitureCollectionFrame());
    }

    [TestMethod]
    public void OfficialMixedFurnitureRespondsToRealDirectionalInputWithNativeRotationAndBodyReaction()
    {
        using var actual = Create(out var id, -3);
        using var reference = Create(out var referenceId, -3);
        Apply(actual, DangoFurnitureChangeKind.Insert, Platform(2, 1));
        Apply(reference, DangoFurnitureChangeKind.Insert, Platform(2, 1));
        Apply(actual, DangoFurnitureChangeKind.Insert, Ball(-1, DangoPhysicsDefaults.Current.GroundHeight + 1.1));
        Apply(actual, DangoFurnitureChangeKind.Insert, Ball(7, DangoPhysicsDefaults.Current.GroundHeight + 1.1));
        var impulse = 0d; var rotation = 0d; var displacement = 0d; var bodyDifference = 0d;
        for (var step = 0; step < 480; step++)
        {
            var horizontal = step < 300 ? 1d : -.5;
            Assert.IsTrue(actual.Step(Dt, [new(id, horizontal)]).Succeeded, $"Mixed directional input native step {step} failed.");
            Assert.IsTrue(reference.Step(Dt, [new(referenceId, horizontal)]).Succeeded);
            var frame = actual.ReadFurnitureCollectionFrame();
            foreach (var ball in frame.Items.Where(i => i.BallState is not null))
            {
                impulse = Math.Max(impulse, ball.DangoContact!.NormalImpulse);
                rotation = Math.Max(rotation, Math.Abs(ball.BallState!.AngleRadians));
                displacement = Math.Max(displacement, Math.Abs(ball.BallState.CenterQX - ball.Definition.Ball!.CenterQX));
            }
            bodyDifference = Math.Max(bodyDifference, Math.Abs(frame.Physics.Bodies.Single().CoreX - reference.ReadFurnitureCollectionFrame().Physics.Bodies.Single().CoreX));
        }
        Assert.IsTrue(impulse > 1e-5); Assert.IsTrue(rotation > .01); Assert.IsTrue(displacement > .1); Assert.IsTrue(bodyDifference > .02);
        TestContext.WriteLine($"Official r1.1/mass.02/gravity.5 mixed real input: impulse={impulse:R}, angle={rotation:R}, movement={displacement:R}, body difference={bodyDifference:R}.");
    }

    [TestMethod]
    public void BallTopContactDoesNotGrantJumpEligibility()
    {
        using var world = Create(out var id, 0, 2.2);
        Apply(world, DangoFurnitureChangeKind.Insert, Ball(0, DangoPhysicsDefaults.Current.GroundHeight + 1.1));
        var checkedContacts = 0;
        for (var step = 0; step < 300; step++)
        {
            var before = world.ReadFurnitureCollectionFrame();
            var contact = before.Items.Single().DangoContact!;
            var testJump = contact.Touching && contact.NormalQY < -.7 && !before.Physics.Bodies.Single().IsGrounded;
            Assert.IsTrue(world.Step(Dt, [new(id, JumpPressed: testJump)]).Succeeded);
            if (testJump)
            {
                var after = world.ReadFurnitureCollectionFrame();
                Assert.IsTrue(after.BodyDiagnostics.Single().SecondsSinceJump >= before.BodyDiagnostics.Single().SecondsSinceJump,
                    "A ball-only contact must not reset the native accepted-jump timer.");
                checkedContacts++;
            }
        }
        Assert.IsTrue(checkedContacts > 5, "The fixture must exercise actual top-of-ball contacts.");
    }

    [TestMethod]
    public void DistantFloorBallSleepsWhileAnotherBallRestsOnAnElevatedPlatform()
    {
        using var world = Create(out var id, 13);
        Apply(world, DangoFurnitureChangeKind.Insert, Platform(-6, 1));
        Apply(world, DangoFurnitureChangeKind.Insert, Ball(-8, 2.28));
        var inserted = Apply(world, DangoFurnitureChangeKind.Insert,
            Ball(8, DangoPhysicsDefaults.Current.GroundHeight + 1.1));
        var floorId = inserted.Items.Last().InstanceId;
        for (var step = 0; step < 180; step++) Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
        var frame = world.ReadFurnitureCollectionFrame();
        var floor = frame.Items.Single(i => i.InstanceId == floorId).BallState!;
        Assert.IsTrue(frame.Items.Single(i => i.BallState is not null && i.InstanceId != floorId).BallState!.CenterQY > 2,
            "The other ball must actually be supported by the elevated platform.");
        Assert.IsTrue(floor.IsSleeping, "Unrelated platform contact must not reset a distant floor ball's sleep timer.");
        for (var step = 0; step < 60; step++) Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
        Assert.AreEqual(floor.IntegrationCount, world.ReadFurnitureCollectionFrame().Items.Single(i => i.InstanceId == floorId).BallState!.IntegrationCount,
            "Native sleeping must stop this ball's integration even while another ball continues to contact a platform.");
    }
}
