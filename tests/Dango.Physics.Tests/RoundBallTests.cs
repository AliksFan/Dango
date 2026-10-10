using System.Collections;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RoundBallTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly DangoRoundBallDefinition FloorBall = new(0, -.32);
    private static string BaselinePath => Environment.GetEnvironmentVariable("DANGO_ROUND_BALL_BASELINE_PATH") ??
        throw new InvalidOperationException("Set DANGO_ROUND_BALL_BASELINE_PATH to the independent, read-only accepted r3 module.");
    private const string LegacyRoundBallSha256 = "9A2A7ED50EEE62080A3A6F9C3DB345652C68829C0DA5E9822969ED6F1BCA8B39";
    private static string LegacyRoundBallPath => Environment.GetEnvironmentVariable("DANGO_ROUND_BALL_LEGACY_PATH") ??
        throw new InvalidOperationException("Set DANGO_ROUND_BALL_LEGACY_PATH to the independent, read-only accepted ABI-1 ball module without the gravity-scale export.");
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RealExportBytesMatchManagedFieldsAtIndependentCLayoutOffsets()
    {
        using var native = new RawWorld();
        using var managed = Create(-2, 0, out var id);
        var nativeId = native.Add(-2, 0);
        Assert.AreEqual(id.Value, nativeId);
        Assert.AreEqual(1, native.RoundBallAbiVersion);
        Assert.AreEqual(RawWorld.FrameSize, native.ReportedFrameSize,
            "The independently derived ABI-1 C frame is 912 bytes; managed sizeof alone is not a layout oracle.");
        AssertFrameBytes(native.Read(), managed.ReadRoundBallFrame());
        Assert.AreEqual(managed.AddRoundBall(FloorBall).Value, native.AddBall(FloorBall));
        AssertFrameBytes(native.Read(), managed.ReadRoundBallFrame());
        var contactFrames = 0;
        var maximumAngle = 0d;
        for (var step = 0; step < 360; step++)
        {
            var horizontal = step < 240 ? 1 : 0;
            native.Step(nativeId, horizontal);
            var frame = Advance(managed, new(id, Horizontal: horizontal));
            AssertFrameBytes(native.Read(), frame);
            if (frame.DangoContact.NormalImpulse > 1e-6) contactFrames++;
            maximumAngle = Math.Max(maximumAngle, Math.Abs(frame.Ball!.AngleRadians));
        }
        Assert.IsTrue(contactFrames > 0, "The ABI fixture must decode actual nonzero Dango contact impulses.");
        Assert.IsTrue(maximumAngle > .01, "The ABI fixture must decode actual native rotation.");
        TestContext.WriteLine($"Independent C byte layout: 362 real frames; impulse frames={contactFrames}; maxAngle={maximumAngle:R}.");
    }

    [TestMethod]
    public void NativeFrameSizeRejectionCannotWriteOutputOrAdvanceTheWorld()
    {
        using var native = new RawWorld();
        native.Add(-2, 0);
        native.AddBall(FloorBall);
        var before = native.Read();
        foreach (var size in new[] { RawWorld.FrameSize - 1, RawWorld.FrameSize + 1 })
        {
            native.AssertRejectedReadDoesNotWrite(size);
            CollectionAssert.AreEqual(before, native.Read(), "A rejected read changed the published native frame.");
        }
        for (var read = 0; read < 20; read++) CollectionAssert.AreEqual(before, native.Read());
    }

    [TestMethod]
    public void CandidateDoesNotExportContractFixtureMutators()
    {
        var library = NativeLibrary.Load(PhysicsTestLibraries.Unified);
        try
        {
            foreach (var name in new[] { "dango_round_ball_test_initial_velocity", "dango_round_ball_test_fault",
                "dango_round_ball_test_add", "dango_round_ball_test_remove", "dango_round_ball_test_read",
                "dango_round_ball_test_point", "dango_round_ball_test_floor", "dango_round_ball_test_geometry",
                "dango_round_ball_test_world_geometry", "dango_round_ball_test_owner_count" })
                Assert.IsFalse(NativeLibrary.TryGetExport(library, name, out _), $"Production candidate exposes test-only entry point: {name}.");
        }
        finally { NativeLibrary.Free(library); }
    }

    [TestMethod]
    public void AcceptedOldModuleRunsOriginalPhysicsButExplicitlyRejectsEveryBallEntryPoint()
    {
        var baseline = BaselinePath;
        AssertIndependentBaseline(baseline);
        using var world = DangoPhysicsWorld.Create(baseline);
        using var reference = DangoPhysicsWorld.Create(baseline);
        var id = world.Add(-2, 0);
        var expectedId = reference.Add(-2, 0);
        Assert.IsFalse(world.SupportsRoundBall);
        AssertMissing(() => world.AddRoundBall(FloorBall));
        AssertMissing(() => world.RemoveRoundBall(new(1)));
        AssertMissing(() => world.ReadRoundBallFrame());
        AssertMissing(() => world.StepWithRoundBall(Dt, [new(id, Horizontal: 1)]));
        AssertMissing(() => { _ = world.RoundBallGravityScale; });
        for (var step = 0; step < 30; step++)
            Assert.AreEqual(Json(reference.Step(Dt, [new(expectedId)])), Json(world.Step(Dt, [new(id)])),
                "Capability failure must occur before Step and leave the old physics path unchanged.");
        world.Reset();
        reference.Reset();
        Assert.AreEqual(Json(reference.Step(Dt, [new(expectedId)])), Json(world.Step(Dt, [new(id)])));
    }

    [TestMethod]
    public void NativeGravityScaleDoubleExportAndManagedGetterAreReadOnlyWithoutAdvancingAnyFrame()
    {
        Assert.IsFalse(typeof(DangoPhysicsWorld).GetProperty(nameof(DangoPhysicsWorld.RoundBallGravityScale))!.CanWrite,
            "The gravity multiplier must be native material information rather than a writable client control.");
        using var native = new RawWorld();
        var nativeId = native.Add(-2, 0);
        native.AddBall(FloorBall);
        using var managed = Create(-2, 0, out var id);
        managed.AddRoundBall(FloorBall);
        for (var phase = 0; phase < 2; phase++)
        {
            var nativeBefore = native.Read();
            var managedBefore = Json(managed.ReadRoundBallFrame());
            for (var read = 0; read < 40; read++)
            {
                Assert.AreEqual(.5, native.ReadGravityScale(), "The real C export must return the double-precision native multiplier.");
                Assert.AreEqual(.5, managed.RoundBallGravityScale, "The wrapper must expose the actual native material multiplier.");
                CollectionAssert.AreEqual(nativeBefore, native.Read(), "Reading the native gravity scale changed the real 912-byte frame.");
                Assert.AreEqual(managedBefore, Json(managed.ReadRoundBallFrame()), "Reading the managed gravity scale mutated or advanced the scene.");
            }
            if (phase == 0) { native.Step(nativeId, 0); Advance(managed, new(id)); }
        }
    }

    [TestMethod]
    public void AcceptedLegacyBallModuleWithoutGravityExportKeepsUnitGravityAndItsBallLifecycle()
    {
        var legacy = AssertLegacyRoundBallModule();
        var library = NativeLibrary.Load(legacy);
        try
        {
            Assert.IsFalse(NativeLibrary.TryGetExport(library, "dango_physics_get_round_ball_gravity_scale", out _),
                "This compatibility fixture must actually lack the optional gravity-scale export.");
        }
        finally { NativeLibrary.Free(library); }
        using var world = DangoPhysicsWorld.Create(legacy);
        Assert.IsTrue(world.SupportsRoundBall, "An absent optional material export must not remove the existing ABI-1 ball capability.");
        Assert.AreEqual(1d, world.RoundBallGravityScale);
        world.SetHorizontalBounds(-4, 4);
        var id = world.AddRoundBall(new(0, 4, RadiusQ: 1.1, Mass: .02));
        var initial = world.ReadRoundBallFrame();
        var initialJson = Json(initial);
        for (var read = 0; read < 40; read++)
        {
            Assert.AreEqual(1d, world.RoundBallGravityScale);
            Assert.AreEqual(initialJson, Json(world.ReadRoundBallFrame()), "The compatibility fallback changed the published frame.");
        }
        for (var step = 0; step < 18; step++)
        {
            var frame = world.StepWithRoundBall(Dt, []);
            Assert.AreEqual(0, frame.ErrorCode);
            Assert.AreEqual((ulong)step + 1, frame.Step);
            AssertVersions(frame);
        }
        var falling = world.ReadRoundBallFrame();
        Assert.IsTrue(falling.Ball!.CenterQY < initial.Ball!.CenterQY - .3,
            "The older ball module must truly fall instead of merely advertising compatibility.");
        Assert.IsTrue(falling.Ball.VelocityQY < -2.5);
        world.Reset();
        var reset = world.ReadRoundBallFrame();
        Assert.AreEqual(id, reset.Ball!.Id);
        Assert.AreEqual(initial.Ball.CenterQY, reset.Ball.CenterQY);
        Assert.AreEqual(0d, reset.Ball.VelocityQY);
        Assert.AreEqual(1d, world.RoundBallGravityScale);
        world.RemoveRoundBall(id);
        Assert.IsNull(world.ReadRoundBallFrame().Ball);
    }

    [TestMethod]
    public void LegalZeroVelocityFreeFallReallyUsesHalfGravityInsteadOfOnlyReportingIt()
    {
        var legacy = AssertLegacyRoundBallModule();
        using var previous = DangoPhysicsWorld.Create(legacy);
        using var candidate = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var definition = new DangoRoundBallDefinition(0, 4, RadiusQ: 1.1, Mass: .02);
        previous.SetHorizontalBounds(-4, 4); candidate.SetHorizontalBounds(-4, 4);
        previous.AddRoundBall(definition); candidate.AddRoundBall(definition);
        var previousBirth = previous.ReadRoundBallFrame();
        var candidateBirth = candidate.ReadRoundBallFrame();
        Assert.AreEqual(0, candidateBirth.Physics.Bodies.Count, "This fixture isolates ball gravity using a legal ball-only scene.");
        Assert.AreEqual(0d, previousBirth.Ball!.VelocityQY);
        Assert.AreEqual(0d, candidateBirth.Ball!.VelocityQY, "The test must use an ordinary legal spawn without injected initial velocity.");
        Assert.AreEqual(0UL, candidateBirth.Ball.IntegrationCount);
        var fullGravity = (double)(float)DangoPhysicsDefaults.Current.Gravity;
        var nativeDt = (double)(float)Dt.TotalSeconds;
        for (var step = 1; step <= 18; step++)
        {
            var oldFrame = previous.StepWithRoundBall(Dt, []);
            var newFrame = candidate.StepWithRoundBall(Dt, []);
            Assert.AreEqual(0, oldFrame.ErrorCode); Assert.AreEqual(0, newFrame.ErrorCode);
            Assert.AreEqual((ulong)step, newFrame.Step);
            AssertVersions(oldFrame); AssertVersions(newFrame);
            var oldBall = oldFrame.Ball!; var newBall = newFrame.Ball!;
            Assert.IsFalse(oldFrame.DangoContact.Touching || oldFrame.FloorContact.Touching ||
                newFrame.DangoContact.Touching || newFrame.FloorContact.Touching,
                "The early free-flight oracle must remain free of contact impulses.");
            Assert.IsFalse(oldBall.IsSleeping || newBall.IsSleeping);
            Assert.IsTrue(newBall.IntegrationCount > 0);
            Assert.AreEqual(-fullGravity * nativeDt * step, oldBall.VelocityQY, 1e-6,
                "The independently pinned older module must retain physical unit gravity.");
            Assert.AreEqual(-.5 * fullGravity * nativeDt * step, newBall.VelocityQY, 1e-6,
                "An export reporting .5 is insufficient if the actual native ball still accelerates at full gravity.");
            Assert.AreEqual(.5 * oldBall.VelocityQY, newBall.VelocityQY, 1e-9,
                "The actual candidate velocity must be half the independent older module's velocity.");
            Assert.AreEqual(.5 * (definition.CenterQY - oldBall.CenterQY), definition.CenterQY - newBall.CenterQY, 1e-9,
                "The actual early descent must also halve; changing only the velocity snapshot cannot pass.");
            Assert.AreEqual(0d, newBall.VelocityQX); Assert.AreEqual(0d, newBall.AngularVelocity);
        }
        TestContext.WriteLine($"Legal free flight: oldVy={previous.ReadRoundBallFrame().Ball!.VelocityQY:R}; halfVy={candidate.ReadRoundBallFrame().Ball!.VelocityQY:R}; radius=1.1; mass=.02; no injected velocity.");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void NoBallPathMatchesIndependentAcceptedR3Module(int count)
    {
        var baseline = BaselinePath;
        AssertIndependentBaseline(baseline);
        using var expected = DangoPhysicsWorld.Create(baseline);
        using var actual = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        expected.SetHorizontalBounds(-4, 23);
        actual.SetHorizontalBounds(-4, 23);
        var expectedIds = Enumerable.Range(0, count).Select(i => expected.Add((i - 1) * 2.8, 0)).ToList();
        var actualIds = Enumerable.Range(0, count).Select(i => actual.Add((i - 1) * 2.8, 0)).ToList();
        for (var step = 0; step < 900; step++)
        {
            if (step == 450)
            {
                expected.Remove(expectedIds[^1]); actual.Remove(actualIds[^1]);
                expectedIds.RemoveAt(expectedIds.Count - 1); actualIds.RemoveAt(actualIds.Count - 1);
            }
            if (step == 480) { expectedIds.Add(expected.Add(3, 1)); actualIds.Add(actual.Add(3, 1)); }
            if (step == 600) { expected.Reset(); actual.Reset(); }
            if (step == 720) { expected.ClearHorizontalBounds(); actual.ClearHorizontalBounds(); }
            var inputs = expectedIds.Select((entity, i) => new DangoPhysicsInput(entity,
                Horizontal: i == 0 && step is >= 120 and < 200 ? 1 : i == 0 && step is >= 220 and < 260 ? -1 : 0,
                Downward: i == 0 && step is >= 270 and < 290 ? .5 : 0,
                JumpPressed: i == expectedIds.Count - 1 && step == 300,
                DragActive: i == expectedIds.Count - 1 && step is >= 360 and < 420,
                DragTargetX: .5, DragTargetY: 2)).ToArray();
            var expectedFrame = expected.Step(Dt, inputs);
            var actualFrame = actual.Step(Dt, inputs.Select((input, i) => input with { EntityId = actualIds[i] }).ToArray());
            Assert.AreEqual(0, actualFrame.ErrorCode);
            Assert.AreEqual(Json(expectedFrame), Json(actualFrame), $"No-ball behavior changed: count={count}, step={step}.");
            Assert.AreEqual(Json(actualFrame), Json(actual.ReadRoundBallFrame().Physics));
        }
    }

    [TestMethod]
    public void InitialStateAndRepeatedReadAreDeeplyReadOnlyWithoutCallingStep()
    {
        using var world = Create(-2, 0, out var id);
        Assert.IsTrue(world.SupportsRoundBall);
        var noBall = world.ReadRoundBallFrame();
        Assert.AreEqual(0UL, noBall.Step);
        Assert.IsNull(noBall.Ball);
        Assert.IsNull(noBall.Definition);
        Assert.AreEqual(id, noBall.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(-2f, noBall.Physics.Bodies.Single().CoreX);
        Assert.AreEqual(.42f, noBall.Physics.Bodies.Single().CoreY);
        var ballId = world.AddRoundBall(FloorBall);
        var initial = world.ReadRoundBallFrame();
        Assert.IsTrue(initial.SceneRevision > noBall.SceneRevision);
        Assert.AreEqual(0UL, initial.Step);
        Assert.AreEqual(FloorBall, initial.Definition);
        Assert.AreEqual(ballId, initial.Ball!.Id);
        Assert.AreEqual(0d, initial.Ball.CenterQX);
        Assert.AreEqual(-.32, initial.Ball.CenterQY);
        Assert.AreEqual(0d, initial.Ball.VelocityQX);
        Assert.AreEqual(0d, initial.Ball.VelocityQY);
        Assert.AreEqual(0d, initial.Ball.AngleRadians);
        Assert.AreEqual(0d, initial.Ball.AngularVelocity);
        Assert.AreEqual(0UL, initial.Ball.IntegrationCount);
        Assert.IsFalse(initial.Ball.IsSleeping);
        AssertVersions(initial);
        var saved = Json(initial);
        for (var read = 0; read < 40; read++) Assert.AreEqual(saved, Json(world.ReadRoundBallFrame()));
        AssertWriteRejected(initial.Physics.Bodies, initial.Physics.Bodies.Single());
        AssertWriteRejected(initial.Physics.Bodies.Single().Contour, 12345f);
        Assert.AreEqual(saved, Json(initial));
        for (var step = 0; step < 100; step++) Advance(world, new(id, Horizontal: 1));
        world.Reset();
        Assert.AreEqual(saved, Json(initial), "Later Step or Reset rewrote a previously returned snapshot.");
        Assert.AreEqual(Json(noBall.Physics), Json(initial.Physics));
    }

    [TestMethod]
    public void EachSuccessfulStepPublishesOneJointStepIncludingTheOriginalStepEntryPoint()
    {
        using var world = Create(-2, 0, out var id);
        world.AddRoundBall(new(0, 1));
        var previous = world.ReadRoundBallFrame();
        for (var step = 0; step < 80; step++)
        {
            DangoRoundBallFrame current;
            if (step % 2 == 0) current = Advance(world, new(id));
            else
            {
                var physics = world.Step(Dt, [new(id)]);
                current = world.ReadRoundBallFrame();
                Assert.AreEqual(Json(physics), Json(current.Physics));
            }
            Assert.AreEqual(previous.Step + 1, current.Step, "A single caller Step must advance exactly one successful world step.");
            Assert.AreEqual(previous.SceneRevision, current.SceneRevision);
            AssertVersions(current);
            Assert.AreEqual(Json(current), Json(world.ReadRoundBallFrame()), "Read advanced or mixed the scene.");
            previous = current;
        }
    }

    [TestMethod]
    [DataRow(0, double.NaN)]
    [DataRow(0, double.PositiveInfinity)]
    [DataRow(0, 1024.001)]
    [DataRow(1, double.NaN)]
    [DataRow(1, double.NegativeInfinity)]
    [DataRow(1, -1024.001)]
    [DataRow(2, double.NaN)]
    [DataRow(2, .0499)]
    [DataRow(2, 4.001)]
    [DataRow(3, double.PositiveInfinity)]
    [DataRow(3, .0009)]
    [DataRow(3, 10.001)]
    [DataRow(4, double.NaN)]
    [DataRow(4, -.001)]
    [DataRow(4, 1.501)]
    [DataRow(5, double.PositiveInfinity)]
    [DataRow(5, -.001)]
    [DataRow(5, 1.501)]
    [DataRow(6, double.NaN)]
    [DataRow(6, -.001)]
    [DataRow(6, 1.001)]
    [DataRow(7, double.NegativeInfinity)]
    [DataRow(7, -.001)]
    [DataRow(7, 1.001)]
    public void InvalidDefinitionDoesNotMutateThePublishedOrNextRealFrame(int field, double value)
    {
        using var actual = Create(-2, 0, out var id);
        using var expected = Create(-2, 0, out var expectedId);
        var definition = field switch
        {
            0 => FloorBall with { CenterQX = value },
            1 => FloorBall with { CenterQY = value },
            2 => FloorBall with { RadiusQ = value },
            3 => FloorBall with { Mass = value },
            4 => FloorBall with { FloorFriction = value },
            5 => FloorBall with { DangoFriction = value },
            6 => FloorBall with { FloorRestitution = value },
            _ => FloorBall with { DangoRestitution = value }
        };
        var before = Json(actual.ReadRoundBallFrame());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => actual.AddRoundBall(definition));
        Assert.AreEqual(before, Json(actual.ReadRoundBallFrame()));
        Assert.AreEqual(Json(Advance(expected, new(expectedId))), Json(Advance(actual, new(id))));
    }

    [TestMethod]
    [DataRow(.05, .001, 0d, 0d)]
    [DataRow(4d, 10d, 1.5, 1d)]
    public void LegalMaterialAndSizeEndpointsRoundTripThroughNative(double radius, double mass, double friction, double restitution)
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        world.SetHorizontalBounds(-10, 10);
        var definition = new DangoRoundBallDefinition(0, 5, radius, mass, friction, friction, restitution, restitution);
        var id = world.AddRoundBall(definition);
        var frame = world.ReadRoundBallFrame();
        Assert.AreEqual(definition, frame.Definition);
        Assert.AreEqual(id, frame.Ball!.Id);
        Assert.AreEqual(0UL, frame.Step);
        Assert.AreEqual(0, frame.Physics.Bodies.Count);
        AssertVersions(frame);
    }

    [TestMethod]
    public void BallConflictsStaleIdsAndRejectedStepLeaveTheEntireSceneUnchanged()
    {
        using var world = Create(-2, 0, out var id);
        var ballId = world.AddRoundBall(FloorBall);
        var before = Json(world.ReadRoundBallFrame());
        AssertRejectedUnchanged(world, before, () => world.AddRoundBall(new(2, 1)));
        AssertRejectedUnchanged(world, before, () => world.RemoveRoundBall(new(int.MaxValue)), DangoPhysicsFailureKind.InvalidEntity);
        AssertRejectedUnchanged(world, before, () => world.Add(2, 0));
        AssertRejectedUnchanged(world, before, () => world.SetStaticPlatform(new(0, 1, 2, .3)));
        AssertRejectedUnchanged(world, before, () => world.TranslateHorizontal(1));
        AssertRejectedUnchanged(world, before, () => world.StepWithRoundBall(Dt, [new(new(int.MaxValue))]), DangoPhysicsFailureKind.InvalidEntity);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.StepWithRoundBall(TimeSpan.Zero, [new(id)]));
        Assert.AreEqual(before, Json(world.ReadRoundBallFrame()));
        Assert.AreEqual(ballId, world.ReadRoundBallFrame().Ball!.Id);
        Assert.AreEqual(1, world.Count);
        Assert.IsNull(world.StaticPlatform);
    }

    [TestMethod]
    public void BirthOverlapBelowFloorAndOutOfBoundsRejectWithoutPartialMutation()
    {
        using var world = Create(0, 0, out var id);
        var initial = Json(world.ReadRoundBallFrame());
        AssertRejectedUnchanged(world, initial, () => world.AddRoundBall(FloorBall));
        AssertRejectedUnchanged(world, initial, () => world.AddRoundBall(new(2, -.321)));
        AssertRejectedUnchanged(world, initial, () => world.AddRoundBall(new(5, 1)));
        for (var step = 0; step < 180; step++) Advance(world, new(id, Horizontal: 1));
        var moved = world.ReadRoundBallFrame();
        Assert.IsTrue(moved.Physics.Bodies.Single().CoreX > 1, "The fixture must move away from the original birth overlap.");
        AssertRejectedUnchanged(world, Json(moved), () => world.AddRoundBall(FloorBall));
        world.Reset();
        var reset = world.ReadRoundBallFrame();
        Assert.IsNull(reset.Ball);
        Assert.AreEqual(0f, reset.Physics.Bodies.Single().CoreX);
        Assert.AreEqual(.42f, reset.Physics.Bodies.Single().CoreY);
    }

    [TestMethod]
    public void PlatformAndMultipleBodiesCannotBeCombinedWithBallAndRemovalRestoresPlatform()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        world.SetHorizontalBounds(-4, 4);
        var id = world.Add(-2, 0);
        var second = world.Add(2, 0);
        var two = Json(world.ReadRoundBallFrame());
        AssertRejectedUnchanged(world, two, () => world.AddRoundBall(new(0, 2)));
        world.Remove(second);
        var ballId = world.AddRoundBall(FloorBall);
        world.RemoveRoundBall(ballId);
        world.Remove(id);
        var platform = new DangoStaticPlatformDefinition(0, 1, 4, .3);
        world.SetStaticPlatform(platform);
        var onPlatform = world.Add(0, 2.5);
        var platformFrame = Json(world.ReadRoundBallFrame());
        AssertRejectedUnchanged(world, platformFrame, () => world.AddRoundBall(new(3, 2)));
        Assert.AreEqual(platform, world.StaticPlatform);
        for (var step = 0; step < 180; step++) Advance(world, new(onPlatform));
        Assert.IsTrue(world.ReadStaticPlatformContact(onPlatform).IsSupported, "Restored platform capability must actually support the Dango.");
        world.Reset();
        var reset = world.ReadRoundBallFrame();
        var contact = world.ReadStaticPlatformContact(onPlatform);
        Assert.IsFalse(contact.IsSupported, "Reset must refresh the platform diagnostics after a ball has been removed.");
        Assert.IsTrue(contact.Gap > .5, $"Reset returned a stale platform gap: {contact.Gap:R}.");
        Assert.IsNull(reset.Ball);
        Assert.IsNull(reset.Definition);
    }

    [TestMethod]
    public void ClearingARealSupportingPlatformRefreshesThePublishedRowWithoutStep()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var platform = new DangoStaticPlatformDefinition(0, 1, 4, .3);
        world.SetStaticPlatform(platform);
        var id = world.Add(0, 2.5);
        for (var step = 0; step < 180; step++) Advance(world, new(id));
        var before = world.ReadRoundBallFrame();
        Assert.IsTrue(world.ReadStaticPlatformContact(id).IsSupported, "The fixture must actually stand on the elevated platform.");
        Assert.IsTrue(before.Physics.Bodies.Single().IsGrounded);
        Assert.AreEqual(0, before.Physics.Bodies.Single().GroundContactCount, "Elevated support must not be original-floor contact.");
        world.ClearStaticPlatform();
        var cleared = world.ReadRoundBallFrame();
        Assert.AreEqual(before.Step, cleared.Step, "Clearing and reading must not call Step.");
        Assert.IsTrue(cleared.SceneRevision > before.SceneRevision);
        Assert.IsFalse(world.ReadStaticPlatformContact(id).IsSupported);
        Assert.IsFalse(cleared.Physics.Bodies.Single().IsGrounded, "The published row retained removed platform support.");
        Assert.AreEqual(Json(before.Physics.Bodies.Single() with { IsGrounded = false }), Json(cleared.Physics.Bodies.Single()),
            "Only the true support qualification may change without a simulation step.");
        AssertVersions(cleared);
        Assert.AreEqual(Json(cleared), Json(world.ReadRoundBallFrame()));
    }

    [TestMethod]
    public void SettingARealSupportingPlatformRefreshesThePublishedRowWithoutStep()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var platform = new DangoStaticPlatformDefinition(0, 1, 4, .3);
        world.SetStaticPlatform(platform);
        var id = world.Add(0, 2.5);
        for (var step = 0; step < 180; step++) Advance(world, new(id));
        Assert.IsTrue(world.ReadStaticPlatformContact(id).IsSupported, "The fixture must reach real platform support.");
        world.ClearStaticPlatform();
        // One legal, very small setup step refreshes the old path's airborne
        // row while retaining the separated pose above the original platform.
        // The Set/read operation under test performs no simulation step.
        Assert.AreEqual(0, world.Step(TimeSpan.FromTicks(1), [new(id)]).ErrorCode);
        var before = world.ReadRoundBallFrame();
        Assert.IsFalse(before.Physics.Bodies.Single().IsGrounded);
        world.SetStaticPlatform(platform);
        var restored = world.ReadRoundBallFrame();
        Assert.AreEqual(before.Step, restored.Step, "Setting and reading must not call Step.");
        Assert.IsTrue(restored.SceneRevision > before.SceneRevision);
        Assert.IsTrue(world.ReadStaticPlatformContact(id).IsSupported, "The restored platform must really meet support eligibility.");
        Assert.IsTrue(restored.Physics.Bodies.Single().IsGrounded, "The published row omitted newly restored platform support.");
        Assert.AreEqual(Json(before.Physics.Bodies.Single() with { IsGrounded = true }), Json(restored.Physics.Bodies.Single()));
        AssertVersions(restored);
    }

    [TestMethod]
    public void RealNoBallFaultPreservesTheWholeSafeFrameAcrossOldTopologyChangesAndResetRecovers()
    {
        // These are valid public configuration values. The original rig really
        // deforms below its area threshold; no production fault helper is used.
        var configuration = new DangoSelfPhysicsParameters { RadialRestLength = .45, MinimumArea = .74 };
        configuration.Validate();
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, configuration: configuration);
        var oldId = world.Add(0, 0);
        var safe = world.ReadRoundBallFrame();
        Assert.IsTrue(safe.Physics.Bodies.Single().SignedArea >= configuration.MinimumArea);
        DangoRoundBallFrame? fault = null;
        for (var step = 0; step < 80; step++)
        {
            var frame = world.StepWithRoundBall(Dt, [new(oldId)]);
            if (!frame.Succeeded) { fault = frame; break; }
            safe = frame;
        }
        Assert.IsNotNull(fault, "The valid small rig must actually reach a native area fault; a fixture without a fault cannot pass.");
        Assert.AreEqual(-88, fault.ErrorCode);
        Assert.AreEqual(-88, world.LastErrorCode);
        Assert.IsTrue(safe.Step > 0, "The fixture must publish real safe steps before the fault.");
        var expectedFault = safe with { Physics = safe.Physics with { ErrorCode = -88 } };
        var frozen = Json(expectedFault);
        Assert.AreEqual(frozen, Json(fault), "Only error_code may change when the native fault is latched.");
        Assert.AreEqual(oldId, fault.Physics.Bodies.Single().EntityId);
        world.Remove(oldId);
        Assert.AreEqual(0, world.Count);
        Assert.AreEqual(frozen, Json(world.ReadRoundBallFrame()), "Old no-ball removal must not rewrite or invalidate the historical safe frame.");
        Assert.AreEqual(frozen, Json(world.StepWithRoundBall(Dt, [])), "A latched empty world must not advance the safe step.");
        var replacement = world.Add(2, 0);
        Assert.AreNotEqual(oldId, replacement);
        Assert.AreEqual(1, world.Count);
        Assert.AreEqual(frozen, Json(world.ReadRoundBallFrame()), "Old no-ball re-add must keep the original safe ID and complete state.");
        Assert.AreEqual(frozen, Json(world.StepWithRoundBall(Dt, [new(replacement)])));
        for (var read = 0; read < 10; read++) Assert.AreEqual(frozen, Json(world.ReadRoundBallFrame()));
        world.Reset();
        var reset = world.ReadRoundBallFrame();
        Assert.AreEqual(0, reset.ErrorCode);
        Assert.AreEqual(0, world.LastErrorCode);
        Assert.AreEqual(replacement, reset.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(2f, reset.Physics.Bodies.Single().CoreX);
        Assert.AreEqual(.42f, reset.Physics.Bodies.Single().CoreY);
        Assert.AreEqual(safe.Step, reset.Step);
        Assert.IsTrue(reset.SceneRevision > safe.SceneRevision);
        Assert.IsNull(reset.Ball);
        AssertVersions(reset);
        var recovered = world.StepWithRoundBall(Dt, [new(replacement)]);
        Assert.AreEqual(0, recovered.ErrorCode);
        Assert.AreEqual(replacement, recovered.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(reset.Step + 1, recovered.Step);
        TestContext.WriteLine($"Normal-export native fault=-88 after safe step {safe.Step}; old safe ID={oldId.Value}; Reset recovered ID={replacement.Value}.");
    }

    [TestMethod]
    public void ResetRestoresBirthPoseAndBallStateWithoutChangingIdsOrAddingAStep()
    {
        using var world = Create(-2, 0, out var id);
        var ballId = world.AddRoundBall(FloorBall);
        var initial = world.ReadRoundBallFrame();
        for (var step = 0; step < 180; step++) Advance(world, new(id, Horizontal: 1));
        var moving = world.ReadRoundBallFrame();
        Assert.IsTrue(Math.Abs(moving.Ball!.CenterQX) > .1, "Reset fixture must exercise a moved ball.");
        world.Reset();
        var reset = world.ReadRoundBallFrame();
        Assert.IsTrue(reset.SceneRevision > moving.SceneRevision);
        Assert.AreEqual(moving.Step, reset.Step, "Reset is a scene revision, not a successful simulation step.");
        Assert.AreEqual(id, reset.Physics.Bodies.Single().EntityId);
        Assert.AreEqual(Json(initial.Physics), Json(reset.Physics));
        Assert.AreEqual(ballId, reset.Ball!.Id);
        Assert.AreEqual(FloorBall, reset.Definition);
        Assert.AreEqual(FloorBall.CenterQX, reset.Ball.CenterQX);
        Assert.AreEqual(FloorBall.CenterQY, reset.Ball.CenterQY);
        Assert.AreEqual(0d, reset.Ball.VelocityQX);
        Assert.AreEqual(0d, reset.Ball.VelocityQY);
        Assert.AreEqual(0d, reset.Ball.AngleRadians);
        Assert.AreEqual(0d, reset.Ball.AngularVelocity);
        Assert.AreEqual(0UL, reset.Ball.IntegrationCount);
        Assert.IsFalse(reset.Ball.IsSleeping);
        Assert.IsFalse(reset.DangoContact.Touching);
        Assert.IsFalse(reset.FloorContact.Touching);
        Assert.AreEqual(0d, reset.DangoContact.NormalImpulse);
        Assert.AreEqual(0d, reset.FloorContact.NormalImpulse);
        AssertVersions(reset);
    }

    [TestMethod]
    public void RemovedBallNeverResurrectsAndReaddedBallRejectsTheOldId()
    {
        using var world = Create(-2, 0, out var id);
        var oldId = world.AddRoundBall(FloorBall);
        Advance(world, new(id));
        var before = world.ReadRoundBallFrame();
        world.RemoveRoundBall(oldId);
        var removed = world.ReadRoundBallFrame();
        Assert.IsTrue(removed.SceneRevision > before.SceneRevision);
        Assert.AreEqual(before.Step, removed.Step);
        Assert.IsNull(removed.Ball);
        Assert.IsNull(removed.Definition);
        AssertVersions(removed);
        for (var step = 0; step < 10; step++) Assert.IsNull(Advance(world, new(id)).Ball);
        world.Reset();
        Assert.IsNull(world.ReadRoundBallFrame().Ball);
        var replacement = world.AddRoundBall(FloorBall);
        Assert.AreNotEqual(oldId, replacement);
        var readded = Json(world.ReadRoundBallFrame());
        AssertRejectedUnchanged(world, readded, () => world.RemoveRoundBall(oldId), DangoPhysicsFailureKind.InvalidEntity);
        Assert.AreEqual(replacement, Advance(world, new(id)).Ball!.Id);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void RealPushReachesNonzeroContactImpulseMovementAndNativeRotation(int direction)
    {
        using var world = Create(-direction * 2, 0, out var id);
        world.AddRoundBall(FloorBall);
        var impulses = 0d;
        var maximumDisplacement = 0d;
        var maximumAngle = 0d;
        var maximumAngularSpeed = 0d;
        var floorTouched = false;
        var closingImpulseFrames = 0;
        for (var step = 0; step < 480; step++)
        {
            var frame = Advance(world, new(id, Horizontal: step < 300 ? direction : 0));
            AssertVersions(frame);
            var ball = frame.Ball!;
            impulses += frame.DangoContact.NormalImpulse;
            maximumDisplacement = Math.Max(maximumDisplacement, direction * ball.CenterQX);
            maximumAngle = Math.Max(maximumAngle, Math.Abs(ball.AngleRadians));
            maximumAngularSpeed = Math.Max(maximumAngularSpeed, Math.Abs(ball.AngularVelocity));
            floorTouched |= frame.FloorContact.Touching;
            if (frame.DangoContact.NormalImpulse > 1e-6)
            {
                Assert.AreEqual(id.Value, frame.DangoContact.ObjectId);
                if (frame.DangoContact.NormalSpeedBefore < 0) closingImpulseFrames++;
            }
        }
        Assert.IsTrue(impulses > 1e-6, "The Dango must actually contact and impart a nonzero impulse to the ball.");
        Assert.IsTrue(closingImpulseFrames > 0, "The push fixture must contain a real closing impact.");
        Assert.IsTrue(maximumDisplacement > .1, "The ball must move in the requested push direction.");
        Assert.IsTrue(maximumAngle > .01 && maximumAngularSpeed > .01, "Native angular state must record real spin and accumulated angle.");
        Assert.IsTrue(floorTouched, "The fixture must reach the actual floor.");
        TestContext.WriteLine($"direction={direction}; impulse={impulses:R}; displacement={maximumDisplacement:R}; angle={maximumAngle:R}; omega={maximumAngularSpeed:R}.");
    }

    [TestMethod]
    public void BallLifecycleRejectsForeignThreadAndDisposedAccessWithoutPartialMutation()
    {
        var world = Create(-2, 0, out var id);
        try
        {
            var ballId = world.AddRoundBall(FloorBall);
            var before = Json(world.ReadRoundBallFrame());
            Action[] operations =
            [
                () => world.AddRoundBall(FloorBall),
                () => world.RemoveRoundBall(ballId),
                () => world.ReadRoundBallFrame(),
                () => world.StepWithRoundBall(Dt, [new(id)]),
                () => { _ = world.SupportsRoundBall; },
                () => { _ = world.RoundBallGravityScale; },
                world.Dispose
            ];
            foreach (var operation in operations)
            {
                var error = Task.Run(() => Assert.ThrowsExactly<InvalidOperationException>(operation)).GetAwaiter().GetResult();
                StringAssert.Contains(error.Message, "创建它的线程");
                Assert.AreEqual(before, Json(world.ReadRoundBallFrame()));
            }
            world.Dispose();
            world.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => world.AddRoundBall(FloorBall));
            Assert.ThrowsExactly<ObjectDisposedException>(() => world.RemoveRoundBall(ballId));
            Assert.ThrowsExactly<ObjectDisposedException>(() => world.ReadRoundBallFrame());
            Assert.ThrowsExactly<ObjectDisposedException>(() => world.StepWithRoundBall(Dt, [new(id)]));
            Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = world.SupportsRoundBall; });
            Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = world.RoundBallGravityScale; });
        }
        finally { world.Dispose(); }
    }

    private static DangoPhysicsWorld Create(double x, double y, out DangoEntityId id)
    {
        var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        try { world.SetHorizontalBounds(-4, 4); id = world.Add(x, y); return world; }
        catch { world.Dispose(); throw; }
    }

    private static DangoRoundBallFrame Advance(DangoPhysicsWorld world, DangoPhysicsInput input)
    {
        var frame = world.StepWithRoundBall(Dt, [input]);
        Assert.AreEqual(0, frame.ErrorCode, "A last-safe fallback frame is not a successful simulation.");
        Assert.AreEqual(1, frame.Physics.Bodies.Count);
        Assert.AreEqual(input.EntityId, frame.Physics.Bodies.Single().EntityId);
        return frame;
    }

    private static void AssertVersions(DangoRoundBallFrame frame)
    {
        if (frame.Ball is { } ball)
        {
            Assert.AreEqual(frame.SceneRevision, ball.SceneRevision);
            Assert.AreEqual(frame.Step, ball.Step);
        }
        Assert.AreEqual(frame.SceneRevision, frame.DangoContact.SceneRevision);
        Assert.AreEqual(frame.Step, frame.DangoContact.Step);
        Assert.AreEqual(frame.SceneRevision, frame.FloorContact.SceneRevision);
        Assert.AreEqual(frame.Step, frame.FloorContact.Step);
    }

    private static void AssertWriteRejected<T>(IReadOnlyList<T> values, T replacement)
    {
        if (values is IList<T> generic) Assert.ThrowsExactly<NotSupportedException>(() => generic[0] = replacement);
        if (values is IList list) Assert.ThrowsExactly<NotSupportedException>(() => list[0] = replacement);
        Assert.IsFalse(values is T[], "An exposed array remains mutable through a cast.");
    }

    private static void AssertMissing(Action action) =>
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport, Assert.ThrowsExactly<DangoPhysicsException>(action).Kind);

    private static void AssertRejectedUnchanged(DangoPhysicsWorld world, string before, Action action,
        DangoPhysicsFailureKind? expectedKind = null)
    {
        var error = Assert.ThrowsExactly<DangoPhysicsException>(action);
        if (expectedKind is { } kind) Assert.AreEqual(kind, error.Kind);
        Assert.AreEqual(before, Json(world.ReadRoundBallFrame()), "A rejected operation partially changed the published scene.");
    }

    private static void AssertIndependentBaseline(string baseline) =>
        Assert.AreNotEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PhysicsTestLibraries.Unified))),
            "The accepted r3 module must be supplied independently; the candidate cannot serve as its own baseline.");

    private static string AssertLegacyRoundBallModule()
    {
        var legacy = LegacyRoundBallPath;
        Assert.AreEqual(LegacyRoundBallSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacy))),
            "Compatibility and free-flight tests require the pinned accepted ABI-1 ball module, not a candidate copy.");
        Assert.AreNotEqual(LegacyRoundBallSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PhysicsTestLibraries.Unified))),
            "The new candidate cannot be its own legacy compatibility and physical oracle.");
        return legacy;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    // These offsets are derived directly from dango_round_ball_types.h using the
    // C ABI's eight-byte double/uint64 alignment. Do not use wrapper structs or
    // Marshal.OffsetOf here: an identical layout mistake must not pass both sides.
    private static void AssertFrameBytes(byte[] bytes, DangoRoundBallFrame frame)
    {
        Assert.AreEqual(frame.SceneRevision, BitConverter.ToUInt64(bytes, 0));
        Assert.AreEqual(frame.Step, BitConverter.ToUInt64(bytes, 8));
        Assert.AreEqual(frame.ErrorCode, BitConverter.ToInt32(bytes, 376));
        Assert.AreEqual(frame.Physics.ContactStepCount, BitConverter.ToInt32(bytes, 380));
        Assert.AreEqual(frame.Physics.Bodies.Count, BitConverter.ToInt32(bytes, 384));
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, 388));
        Assert.AreEqual(frame.Ball is null ? 0 : 1, BitConverter.ToInt32(bytes, 20));
        Assert.AreEqual(frame.SceneRevision, BitConverter.ToUInt64(bytes, 32));
        Assert.AreEqual(frame.Step, BitConverter.ToUInt64(bytes, 40));
        if (frame.Ball is { } ball)
        {
            Assert.AreEqual(ball.Id.Value, BitConverter.ToInt32(bytes, 16));
            Assert.AreEqual(ball.IsSleeping ? 1 : 0, BitConverter.ToInt32(bytes, 24));
            Assert.AreEqual(ball.IntegrationCount, BitConverter.ToUInt64(bytes, 48));
            double[] state = [ball.CenterQX, ball.CenterQY, ball.VelocityQX, ball.VelocityQY, ball.AngleRadians, ball.AngularVelocity];
            for (var field = 0; field < state.Length; field++) Assert.AreEqual(state[field], BitConverter.ToDouble(bytes, 56 + field * 8));
        }
        if (frame.Definition is { } definition)
        {
            double[] fields = [definition.CenterQX, definition.CenterQY, definition.RadiusQ, definition.Mass,
                definition.FloorFriction, definition.DangoFriction, definition.FloorRestitution, definition.DangoRestitution];
            for (var field = 0; field < fields.Length; field++) Assert.AreEqual(fields[field], BitConverter.ToDouble(bytes, 312 + field * 8));
        }
        else Assert.IsTrue(bytes.AsSpan(312, 64).ToArray().All(value => value == 0));
        AssertContactBytes(bytes, 104, frame.DangoContact);
        AssertContactBytes(bytes, 208, frame.FloorContact);
        for (var bodyIndex = 0; bodyIndex < frame.Physics.Bodies.Count; bodyIndex++)
        {
            var body = frame.Physics.Bodies[bodyIndex];
            Assert.AreEqual(body.EntityId.Value, BitConverter.ToInt32(bytes, 392 + bodyIndex * 4));
            var stateOffset = 404 + bodyIndex * 168;
            for (var field = 0; field < 32; field++) Assert.AreEqual(body.Contour[field], BitConverter.ToSingle(bytes, stateOffset + field * 4));
            float[] tail = [body.CoreX, body.CoreY, body.SignedArea, body.CentroidX, body.CentroidY, body.MaximumSpeed,
                body.GroundContactCount, body.IsGrounded ? 1 : 0, body.SelectedDragNode, body.PointCount];
            for (var field = 0; field < tail.Length; field++) Assert.AreEqual(tail[field], BitConverter.ToSingle(bytes, stateOffset + 128 + field * 4));
        }
        for (var bodyIndex = frame.Physics.Bodies.Count; bodyIndex < 3; bodyIndex++)
        {
            Assert.AreEqual(0, BitConverter.ToInt32(bytes, 392 + bodyIndex * 4));
            Assert.IsTrue(bytes.AsSpan(404 + bodyIndex * 168, 168).ToArray().All(value => value == 0));
        }
    }

    private static void AssertContactBytes(byte[] bytes, int offset, DangoRoundBallContact contact)
    {
        Assert.AreEqual(contact.ObjectId, BitConverter.ToInt32(bytes, offset));
        Assert.AreEqual(contact.Touching ? 1 : 0, BitConverter.ToInt32(bytes, offset + 4));
        Assert.AreEqual(contact.Sample, BitConverter.ToInt32(bytes, offset + 8));
        Assert.AreEqual(contact.SceneRevision, BitConverter.ToUInt64(bytes, offset + 16));
        Assert.AreEqual(contact.Step, BitConverter.ToUInt64(bytes, offset + 24));
        double[] fields = [contact.Fraction, contact.NormalQX, contact.NormalQY, contact.GapQ,
            contact.NormalImpulse, contact.TangentImpulse, contact.NormalSpeedBefore, contact.TangentSpeedBefore, contact.BallSpeedBefore];
        for (var field = 0; field < fields.Length; field++) Assert.AreEqual(fields[field], BitConverter.ToDouble(bytes, offset + 32 + field * 8));
    }

    private sealed class RawWorld : IDisposable
    {
        internal const int FrameSize = 912;
        private readonly nint library;
        private readonly AddDelegate add;
        private readonly AddBallDelegate addBall;
        private readonly ReadDelegate read;
        private readonly StepDelegate step;
        private readonly DestroyDelegate destroy;
        private nint world;
        internal int RoundBallAbiVersion { get; }
        internal int ReportedFrameSize { get; }

        internal RawWorld()
        {
            library = NativeLibrary.Load(PhysicsTestLibraries.Unified);
            try
            {
                RoundBallAbiVersion = Get<IntegerDelegate>("dango_physics_get_round_ball_abi_version")();
                ReportedFrameSize = Get<IntegerDelegate>("dango_physics_get_round_ball_frame_size")();
                add = Get<AddDelegate>("dango_physics_world_add");
                addBall = Get<AddBallDelegate>("dango_physics_world_add_round_ball");
                read = Get<ReadDelegate>("dango_physics_world_get_round_ball_frame");
                step = Get<StepDelegate>("dango_physics_world_step");
                destroy = Get<DestroyDelegate>("dango_physics_world_destroy");
                var create = Get<CreateDelegate>("dango_physics_world_create");
                // Frozen original config, encoded as two C ints followed by 39
                // floats. This independent caller does not use wrapper structs.
                float[] values = [.68f, .65f, 1.22f, .73f, 1.07f, 5.2f, 20.1f, 1.85f, 1.85f,
                    4.8f, 3.5f, .09f, .3f, 3.5f, .25f, 10f, 1.7f, .5f, 9.8f, .12f, .015f, .42f,
                    -.72f, .045f, .72f, .02f, 28f, 1.8f, 10f, 2.2f, 8f, 2.4f, 3.2f, .35f, 2.2f,
                    .42f, .45f, 12f, .42f];
                var configuration = new byte[164];
                BitConverter.GetBytes(16).CopyTo(configuration, 0);
                BitConverter.GetBytes(4).CopyTo(configuration, 4);
                for (var field = 0; field < values.Length; field++) BitConverter.GetBytes(values[field]).CopyTo(configuration, 8 + field * 4);
                WithBuffer(configuration, pointer => Assert.AreEqual(0, create(pointer, 3, out world)));
                Assert.AreNotEqual(nint.Zero, world);
                Assert.AreEqual(0, Get<BoundsDelegate>("dango_physics_world_set_horizontal_bounds")(world, -4, 4));
            }
            catch
            {
                if (world != 0) Get<DestroyDelegate>("dango_physics_world_destroy")(world);
                NativeLibrary.Free(library);
                throw;
            }
        }

        internal int Add(float x, float y)
        {
            Assert.AreEqual(0, add(world, x, y, out var id));
            return id;
        }

        internal int AddBall(DangoRoundBallDefinition definition)
        {
            double[] values = [definition.CenterQX, definition.CenterQY, definition.RadiusQ, definition.Mass,
                definition.FloorFriction, definition.DangoFriction, definition.FloorRestitution, definition.DangoRestitution];
            var bytes = new byte[64];
            for (var field = 0; field < values.Length; field++) BitConverter.GetBytes(values[field]).CopyTo(bytes, field * 8);
            var id = 0;
            WithBuffer(bytes, pointer => Assert.AreEqual(0, addBall(world, pointer, out id)));
            return id;
        }

        internal void Step(int id, float horizontal)
        {
            var states = new float[126];
            Assert.AreEqual(0, step(world, (float)Dt.TotalSeconds, [id], [horizontal, 0, 0, 0, 0, 0], 1,
                new int[3], states, states.Length, out var count, out _, out var error));
            Assert.AreEqual(0, error);
            Assert.AreEqual(1, count);
        }

        internal byte[] Read()
        {
            var guarded = Enumerable.Repeat((byte)0xA5, FrameSize + 16).ToArray();
            WithBuffer(guarded, pointer =>
            {
                Assert.AreEqual(0, read(world, pointer + 8, FrameSize));
                Marshal.Copy(pointer, guarded, 0, guarded.Length);
            });
            Assert.IsTrue(guarded.Take(8).Concat(guarded.Skip(FrameSize + 8)).All(value => value == 0xA5), "Native read crossed its output buffer.");
            return guarded[8..(FrameSize + 8)];
        }

        internal double ReadGravityScale() => Get<DoubleDelegate>("dango_physics_get_round_ball_gravity_scale")();

        internal void AssertRejectedReadDoesNotWrite(int size)
        {
            var guarded = Enumerable.Repeat((byte)0xA5, FrameSize + 32).ToArray();
            WithBuffer(guarded, pointer =>
            {
                Assert.AreEqual(-410, read(world, pointer + 8, size));
                Marshal.Copy(pointer, guarded, 0, guarded.Length);
            });
            Assert.IsTrue(guarded.All(value => value == 0xA5), "Wrong frame_size wrote output before validation.");
        }

        private T Get<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        private static void WithBuffer(byte[] bytes, Action<nint> action)
        {
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); action(pointer); }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        public void Dispose()
        {
            if (world == 0) return;
            var handle = world;
            world = 0;
            try { Assert.AreEqual(0, destroy(handle)); }
            finally { NativeLibrary.Free(library); }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntegerDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double DoubleDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDelegate(nint config, int capacity, out nint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AddDelegate(nint handle, float x, float y, out int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AddBallDelegate(nint handle, nint definition, out int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadDelegate(nint handle, nint frame, int frameSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BoundsDelegate(nint handle, float left, float right);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DestroyDelegate(nint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int StepDelegate(nint handle, float dt, [In] int[] ids, [In] float[] controls, int controlCount,
            [Out] int[] stateIds, [Out] float[] states, int stateCapacity, out int stateCount, out int contactSteps, out int latchedError);
    }
}
