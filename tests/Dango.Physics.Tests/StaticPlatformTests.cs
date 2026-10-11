using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StaticPlatformTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60);
    private static readonly DangoStaticPlatformDefinition LandingPlatform = new(0, 1, 4, .3);
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void NoPlatformCandidateMatchesIndependentAcceptedModuleForNineHundredSteps(int count)
    {
        var candidate = PhysicsTestLibraries.Unified;
        var baseline = PhysicsTestLibraries.StaticPlatformBaseline;
        Assert.AreNotEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidate))), "An independent baseline is required; comparing a candidate to itself is not acceptance.");
        using var expected = DangoPhysicsWorld.Create(baseline);
        using var actual = DangoPhysicsWorld.Create(candidate);
        expected.SetHorizontalBounds(-4, 23); actual.SetHorizontalBounds(-4, 23);
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
            var inputs = expectedIds.Select((id, i) => new DangoPhysicsInput(id,
                Horizontal: i == 0 && step is >= 120 and < 200 ? 1 : i == 0 && step is >= 220 and < 260 ? -1 : 0,
                Downward: i == 0 && step is >= 270 and < 290 ? .5 : 0,
                JumpPressed: i == expectedIds.Count - 1 && step == 300,
                DragActive: i == expectedIds.Count - 1 && step is >= 360 and < 420,
                DragTargetX: .5, DragTargetY: 2)).ToArray();
            var expectedFrame = expected.Step(Dt, inputs);
            var actualFrame = actual.Step(Dt, inputs.Select((input, i) => input with { EntityId = actualIds[i] }).ToArray());
            Assert.AreEqual(JsonSerializer.Serialize(expectedFrame), JsonSerializer.Serialize(actualFrame),
                $"count={count}, step={step}: state, ID, contact or error changed without a platform.");
            Assert.AreEqual(0, actualFrame.ErrorCode);
        }
        TestContext.WriteLine($"Independent accepted baseline: count={count}; exact matching steps=900.");
    }

    [TestMethod]
    public void PlatformLandingSettlesAndStaysSupportedForEighteenHundredSteps()
    {
        using var world = Create(LandingPlatform, 0, 2.5, out var id);
        var body = Settle(world, id);
        var initialY = body.CoreY;
        var maximumError = 0d;
        for (var step = 0; step < 1800; step++)
        {
            body = Advance(world, new(id));
            var contact = world.ReadStaticPlatformContact(id);
            Assert.IsTrue(contact.HasPlatform && contact.IsTouching && contact.IsSupported, $"support lost: step={step}");
            Assert.IsTrue(contact.NormalY >= .7, $"wrong support normal: step={step}");
            Assert.IsTrue(body.IsGrounded);
            Assert.AreEqual(0, body.GroundContactCount, "The elevated platform must not masquerade as contact with the original floor.");
            var gap = StaticPlatformGeometry.TopGap(body, LandingPlatform);
            Assert.IsTrue(Math.Abs(gap) <= StaticPlatformGeometry.ContactBudget, $"painted top gap={gap:R}; step={step}");
            AssertContactBudget(body, LandingPlatform, step);
            maximumError = Math.Max(maximumError, Math.Abs(gap));
        }
        Assert.IsTrue(body.MaximumSpeed < .03, $"not settled: speed={body.MaximumSpeed:R}");
        TestContext.WriteLine($"Support steps=1800; initialY={initialY:R}; finalY={body.CoreY:R}; maxPaintedGap={maximumError:R}; finalSpeed={body.MaximumSpeed:R}.");
    }

    [TestMethod]
    public void PlatformJumpLeavesSupportAndRepeatedAirborneJumpDoesNotAddLaunches()
    {
        using var repeated = Create(LandingPlatform, 0, 2.5, out var a);
        using var single = Create(LandingPlatform, 0, 2.5, out var b);
        var starting = Settle(repeated, a); Settle(single, b);
        var highest = (double)starting.CoreY;
        var cleared = false; var risingFast = 0;
        var previousY = starting.CoreY;
        for (var step = 0; step < 24; step++)
        {
            var actual = Advance(repeated, new(a, JumpPressed: true));
            var expected = Advance(single, new(b, JumpPressed: step == 0));
            Assert.AreEqual(expected.CoreY, actual.CoreY, 1e-5, $"airborne jump added motion: step={step}");
            var contact = repeated.ReadStaticPlatformContact(a);
            var upwardRate = (actual.CoreY - previousY) / Dt.TotalSeconds;
            if (contact.IsSupported)
            {
                Assert.IsTrue(contact.NormalY >= .7 && contact.Gap <= .008,
                    $"An airborne support result must still meet the contact contract: step={step}");
                Assert.IsTrue(Math.Abs(StaticPlatformGeometry.TopGap(actual, LandingPlatform)) <= StaticPlatformGeometry.ContactBudget,
                    $"Platform support was reported without painted contact: step={step}");
            }
            if (upwardRate >= .5)
            {
                risingFast++;
                Assert.IsFalse(contact.IsSupported, $"fast upward motion retained platform support: step={step}; rate={upwardRate:R}");
            }
            if (step <= 1 && !contact.IsSupported) cleared = true;
            previousY = actual.CoreY; highest = Math.Max(highest, actual.CoreY);
            AssertContactBudget(actual, LandingPlatform, step);
        }
        Assert.IsTrue(cleared, "Jump must clear support within two fixed steps.");
        Assert.IsTrue(highest > starting.CoreY + .05, "Space must cause real upward motion, not just change IsGrounded.");
        Assert.IsTrue(risingFast > 0, "The fixture must actually exercise upward motion at or above the 0.5 threshold.");
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void RealAirborneSideContactCannotGrantPlatformSupportOrWallJump(int side)
    {
        var platform = new DangoStaticPlatformDefinition(0, 1.8, 1, 2.4);
        using var jumping = Create(platform, side * 1.5, 2, out var a);
        using var normal = Create(platform, side * 1.5, 2, out var b);
        var contacts = 0;
        for (var step = 0; step < 120; step++)
        {
            var actual = Advance(jumping, new(a, Horizontal: -side, JumpPressed: true));
            var expected = Advance(normal, new(b, Horizontal: -side));
            var contour = StaticPlatformGeometry.PaintedContour(expected);
            if (contour.Min(p => p.Y) <= DangoPhysicsDefaults.Current.GroundHeight + .08) break;
            var contact = jumping.ReadStaticPlatformContact(a);
            if (contact.IsTouching)
            {
                contacts++;
                Assert.IsTrue(Math.Abs(contact.NormalX) >= .7 && contact.NormalY < .7, $"The fixture must touch a side, not the top: step={step}");
                Assert.IsFalse(contact.IsSupported, $"side contact grants support: side={side}, step={step}");
                Assert.IsFalse(actual.IsGrounded, $"airborne side reported grounded: step={step}");
                Assert.AreEqual(0, actual.GroundContactCount);
            }
            Assert.AreEqual(expected.CoreY, actual.CoreY, .02, $"repeated jump launches from the wall: side={side}, step={step}");
            AssertContactBudget(actual, platform, step);
            // SAT measures intersection with the finite rectangle. An unrestricted
            // max-X check incorrectly forbids points passing under its bottom.
        }
        Assert.IsTrue(contacts >= 3, $"Fixture did not make sustained airborne side contact: side={side}, contactFrames={contacts}");
        TestContext.WriteLine($"side={side}; actual airborne contact frames={contacts}.");
    }

    [TestMethod]
    public void RealUndersideImpactBlocksWithoutGrantingSupportOrAdditionalJump()
    {
        var platform = new DangoStaticPlatformDefinition(0, 1.05, 4, .3);
        using (var originalFixture = DangoPhysicsWorld.Create(PhysicsTestLibraries.StaticPlatformBaseline))
        {
            var originalId = originalFixture.Add(0, 0);
            var originalTop = StaticPlatformGeometry.PaintedContour(Advance(originalFixture, new(originalId))).Max(p => p.Y);
            TestContext.WriteLine($"Original spawn anchor 0 first-frame painted top={originalTop:R}; ceiling bottom={platform.Bottom:R}. DropHeight=.42 makes the original fixture intersect, so the approach now starts at anchor -.4.");
            Assert.IsTrue(originalTop > platform.Bottom, "The rejected original fixture must demonstrably intersect the ceiling; do not move a valid fixture to evade a collision bug.");
        }
        using var world = Create(platform, 0, -.4, out var id);
        for (var i = 0; i < 120; i++) Advance(world, new(id));
        var contacts = 0; var airborneContacts = 0;
        for (var step = 0; step < 120; step++)
        {
            var body = Advance(world, new(id, JumpPressed: true));
            var contact = world.ReadStaticPlatformContact(id);
            if (contact.IsTouching)
            {
                contacts++;
                Assert.IsTrue(contact.NormalY <= -.7, $"Must actually contact the underside: step={step}");
                Assert.IsFalse(contact.IsSupported, $"underside impact grants support: step={step}");
                if (body.GroundContactCount == 0)
                {
                    airborneContacts++;
                    Assert.IsFalse(body.IsGrounded, $"underside changed airborne action qualification: step={step}");
                }
            }
            AssertContactBudget(body, platform, step);
            Assert.IsTrue(StaticPlatformGeometry.PaintedContour(body).Max(p => p.Y) <= platform.Bottom + StaticPlatformGeometry.ContactBudget,
                $"body passed through the ceiling: step={step}");
        }
        Assert.IsTrue(contacts > 0 && airborneContacts > 0, $"Fixture must hit the underside while off the floor: contacts={contacts}, airborne={airborneContacts}");
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void WalkingOffFiniteTopLosesSupportAndFallsToOriginalFloor(int direction)
    {
        var platform = new DangoStaticPlatformDefinition(0, 1, 2.5, .3);
        using var world = Create(platform, 0, 2.5, out var id);
        var start = Settle(world, id);
        var passedEdge = false; var offEdgeStep = -1; var lostSupport = false; var fell = false;
        for (var step = 0; step < 480; step++)
        {
            var body = Advance(world, new(id, Horizontal: direction));
            var contour = StaticPlatformGeometry.PaintedContour(body);
            var entirelyOutside = direction > 0 ? contour.Min(p => p.X) > platform.Right : contour.Max(p => p.X) < platform.Left;
            var contact = world.ReadStaticPlatformContact(id);
            if (entirelyOutside && offEdgeStep < 0) { offEdgeStep = step; passedEdge = true; }
            if (offEdgeStep >= 0 && step >= offEdgeStep + 2)
            {
                Assert.IsFalse(contact.IsSupported, $"invisible support persists beyond platform: step={step}");
                lostSupport = true;
            }
            if (body.CoreY < start.CoreY - .5 && body.GroundContactCount > 0) { fell = true; break; }
            AssertContactBudget(body, platform, step);
        }
        Assert.IsTrue(passedEdge && lostSupport && fell, $"Must walk beyond the actual finite edge, lose support and land on the original floor: edge={passedEdge}, lost={lostSupport}, fell={fell}");
    }

    [TestMethod]
    [DataRow(-.85)]
    [DataRow(.85)]
    public void CornerHighSpeedDownwardApproachCannotTunnel(double x)
    {
        var platform = new DangoStaticPlatformDefinition(0, 1, 2, .3);
        using var world = Create(platform, x, 14, out var id);
        var contacts = 0; var maximumSpeed = 0f; var approachSpeed = 0f;
        for (var step = 0; step < 300; step++)
        {
            var body = Advance(world, new(id, Downward: 1));
            maximumSpeed = Math.Max(maximumSpeed, body.MaximumSpeed);
            var contact = world.ReadStaticPlatformContact(id);
            var gap = StaticPlatformGeometry.TopGap(body, platform);
            if (contacts == 0 && (contact.IsTouching || gap is >= 0 and <= .25))
                approachSpeed = Math.Max(approachSpeed, body.MaximumSpeed);
            if (contact.IsTouching) contacts++;
            AssertContactBudget(body, platform, step);
        }
        Assert.IsTrue(contacts > 0, "The high-speed corner fixture must actually contact the platform.");
        Assert.IsTrue(approachSpeed >= 8, $"The near-contact fixture must exercise genuine high-speed motion, rather than an unrelated early peak; approachSpeed={approachSpeed:R}, allFrameMaximum={maximumSpeed:R}");
        TestContext.WriteLine($"cornerX={x:R}; contactFrames={contacts}; nearContactSpeed={approachSpeed:R}; observedMaximumSpeed={maximumSpeed:R} (bounded by 12); real Downward=1, unchanged defaults.");
    }

    [TestMethod]
    [DataRow(-.85)]
    [DataRow(.85)]
    public void RealDragReleaseAtTheCornerRetainsCollisionAndDropsTheGrab(double x)
    {
        var platform = new DangoStaticPlatformDefinition(0, 1, 2, .3);
        using var world = Create(platform, x, 7, out var id);
        var contacts = 0; var grabbed = false; var released = false;
        for (var step = 0; step < 300; step++)
        {
            var dragging = step < 30;
            var body = Advance(world, new(id, DragActive: dragging, DragTargetX: x, DragTargetY: 8));
            if (dragging && body.SelectedDragNode >= 0) grabbed = true;
            if (!dragging)
            {
                Assert.AreEqual(-1, body.SelectedDragNode, $"Grab remained after release: step={step}");
                released = true;
            }
            if (world.ReadStaticPlatformContact(id).IsTouching) contacts++;
            AssertContactBudget(body, platform, step);
        }
        Assert.IsTrue(grabbed && released && contacts > 0, $"Must actually grab, release and contact the corner: grabbed={grabbed}, released={released}, contacts={contacts}");
    }

    [TestMethod]
    public void ClearingSupportCannotLeaveOneLastAirborneJumpQualification()
    {
        using var jumping = Create(LandingPlatform, 0, 2.5, out var a);
        using var normal = Create(LandingPlatform, 0, 2.5, out var b);
        Settle(jumping, a); Settle(normal, b);
        jumping.ClearStaticPlatform(); normal.ClearStaticPlatform();
        for (var step = 0; step < 12; step++)
        {
            var actual = jumping.Step(Dt, [new(a, JumpPressed: true)]);
            var expected = normal.Step(Dt, [new(b)]);
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual),
                $"Cleared platform left stale native action qualification: step={step}");
            Assert.IsFalse(jumping.ReadStaticPlatformContact(a).IsSupported);
        }
    }

    [TestMethod]
    public void RemovingThePlatformReleasesSupportAndResetRetainsTheCurrentDefinition()
    {
        using var world = Create(LandingPlatform, 0, 2.5, out var id);
        var before = Settle(world, id);
        world.Reset();
        Assert.AreEqual(LandingPlatform, world.StaticPlatform);
        Assert.AreEqual(id, Advance(world, new(id)).EntityId);
        Settle(world, id);
        world.ClearStaticPlatform(); world.ClearStaticPlatform();
        Assert.IsNull(world.StaticPlatform);
        var contact = world.ReadStaticPlatformContact(id);
        Assert.IsFalse(contact.HasPlatform || contact.IsTouching || contact.IsSupported);
        DangoBodyState body = before;
        for (var step = 0; step < 240; step++) body = Advance(world, new(id));
        Assert.IsTrue(body.CoreY < before.CoreY - .5 && body.GroundContactCount > 0);
        Assert.IsTrue(body.MaximumSpeed < .03);
        world.Reset();
        Assert.IsNull(world.StaticPlatform);
        Assert.AreEqual(id, Advance(world, new(id)).EntityId);
    }

    [TestMethod]
    public void RepeatedDefinitionAndContactReadsDoNotStepOrMutateTheWorld()
    {
        using var repeated = Create(LandingPlatform, 0, 2.5, out var a);
        using var reference = Create(LandingPlatform, 0, 2.5, out var b);
        for (var step = 0; step < 300; step++)
        {
            repeated.SetStaticPlatform(step % 2 == 0 ? LandingPlatform : new(-0.0, 1, 4, .3));
            var first = repeated.ReadStaticPlatformContact(a);
            Assert.AreEqual(first, repeated.ReadStaticPlatformContact(a));
            repeated.SetHorizontalBounds(-4, 23);
            reference.SetHorizontalBounds(-4, 23);
            var actual = repeated.Step(Dt, [new(a)]);
            var expected = reference.Step(Dt, [new(b)]);
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), $"set/read mutated or advanced: step={step}");
            Assert.AreEqual(LandingPlatform, repeated.StaticPlatform);
        }
    }

    [TestMethod]
    [DataRow(double.NaN, 1d, 4d, .3, .72)]
    [DataRow(0d, double.PositiveInfinity, 4d, .3, .72)]
    [DataRow(double.MaxValue, 1d, 4d, .3, .72)]
    [DataRow(0d, 1d, 0d, .3, .72)]
    [DataRow(0d, 1d, -.1, .3, .72)]
    [DataRow(0d, 1d, .099, .3, .72)]
    [DataRow(0d, 1d, 16.01, .3, .72)]
    [DataRow(0d, 1d, 4d, .099, .72)]
    [DataRow(0d, 1d, 4d, 16.01, .72)]
    [DataRow(1024d, 1d, .1, .3, .72)]
    [DataRow(0d, 1024d, 4d, .1, .72)]
    [DataRow(0d, 1d, 4d, .3, -.001)]
    [DataRow(0d, 1d, 4d, .3, 1.501)]
    [DataRow(0d, 1d, 4d, .3, double.NaN)]
    public void InvalidDefinitionCannotMutateTheExistingWorld(double x, double y, double width, double height, double friction)
    {
        using var actual = Create(LandingPlatform, 0, 2.5, out var a);
        using var reference = Create(LandingPlatform, 0, 2.5, out var b);
        var error = Capture(() => actual.SetStaticPlatform(new(x, y, width, height, friction)));
        Assert.IsInstanceOfType<ArgumentOutOfRangeException>(error);
        Assert.AreEqual(LandingPlatform, actual.StaticPlatform);
        Assert.AreEqual(JsonSerializer.Serialize(reference.Step(Dt, [new(b)])), JsonSerializer.Serialize(actual.Step(Dt, [new(a)])), "A rejected definition changed the next real frame.");
    }

    [TestMethod]
    public void ConflictOverlapSecondBodyAndTranslationAreRejectedWithoutPartialMutation()
    {
        using var actual = Create(LandingPlatform, 0, 2.5, out var a);
        using var reference = Create(LandingPlatform, 0, 2.5, out var b);
        AssertPhysicsRejection(() => actual.SetStaticPlatform(new(5, 1, 4, .3)));
        AssertPhysicsRejection(() => actual.SetStaticPlatform(new(0, 1 + 1e-9, 4, .3)));
        AssertPhysicsRejection(() => actual.Add(5, 0));
        AssertPhysicsRejection(() => actual.TranslateHorizontal(1));
        Assert.AreEqual(LandingPlatform, actual.StaticPlatform);
        Assert.AreEqual(1, actual.Count);
        Assert.AreEqual(JsonSerializer.Serialize(reference.Step(Dt, [new(b)])), JsonSerializer.Serialize(actual.Step(Dt, [new(a)])));
        actual.ClearStaticPlatform(); reference.ClearStaticPlatform();
        actual.TranslateHorizontal(.5);
        var translated = Advance(actual, new(a));
        var untranslated = Advance(reference, new(b));
        Assert.AreEqual(untranslated.CoreX + .5, translated.CoreX, 2e-4, "Clearing the platform must restore the existing translation capability.");
        using var overlap = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var overlapping = overlap.Add(0, 0);
        AssertPhysicsRejection(() => overlap.SetStaticPlatform(new(0, 0, 1, .5)));
        Assert.IsNull(overlap.StaticPlatform);
        Assert.IsTrue(overlap.Step(Dt, [new(overlapping)]).Succeeded);
        using var two = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var first = two.Add(0, 0); var second = two.Add(4, 0);
        AssertPhysicsRejection(() => two.SetStaticPlatform(LandingPlatform));
        Assert.IsNull(two.StaticPlatform);
        Assert.IsTrue(two.Step(Dt, [new(first), new(second)]).Succeeded);
    }

    [TestMethod]
    [DataRow(.1, .1, 0d)]
    [DataRow(16d, 16d, 1.5)]
    public void LegalDimensionAndFrictionEndpointsAreAccepted(double width, double height, double friction)
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var definition = new DangoStaticPlatformDefinition(10, 10, width, height, friction);
        world.SetStaticPlatform(definition);
        Assert.AreEqual(definition, world.StaticPlatform);
        world.ClearStaticPlatform();
        Assert.IsNull(world.StaticPlatform);
    }

    [TestMethod]
    [DataRow(-1023.95, 0d)]
    [DataRow(1023.95, 0d)]
    [DataRow(0d, -1023.95)]
    [DataRow(0d, 1023.95)]
    public void LegalRectangleEndpointAtWorldLimitIsAccepted(double x, double y)
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        var definition = new DangoStaticPlatformDefinition(x, y, .1, .1);
        world.SetStaticPlatform(definition);
        Assert.AreEqual(definition, world.StaticPlatform);
        world.ClearStaticPlatform();
    }

    [TestMethod]
    public void ContactDiagnosticsRejectStaleIdsCrossThreadAndDisposedAccess()
    {
        var world = Create(LandingPlatform, 0, 2.5, out var id);
        try
        {
            AssertPhysicsRejection(() => world.ReadStaticPlatformContact(new(int.MaxValue)));
            var contactError = Task.Run(() => Capture(() => world.ReadStaticPlatformContact(id))).GetAwaiter().GetResult();
            Assert.IsInstanceOfType<InvalidOperationException>(contactError);
            var setError = Task.Run(() => Capture(() => world.SetStaticPlatform(LandingPlatform))).GetAwaiter().GetResult();
            Assert.IsInstanceOfType<InvalidOperationException>(setError);
            var clearError = Task.Run(() => Capture(world.ClearStaticPlatform)).GetAwaiter().GetResult();
            Assert.IsInstanceOfType<InvalidOperationException>(clearError);
            world.Remove(id);
            AssertPhysicsRejection(() => world.ReadStaticPlatformContact(id));
            world.Dispose(); world.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => world.SetStaticPlatform(LandingPlatform));
            Assert.ThrowsExactly<ObjectDisposedException>(world.ClearStaticPlatform);
            Assert.ThrowsExactly<ObjectDisposedException>(() => world.ReadStaticPlatformContact(id));
        }
        finally { world.Dispose(); }
    }

    [TestMethod]
    public void OneHundredWorldPlatformRemoveResetAndDisposeCyclesKeepStableIdsAndNoNativeFailure()
    {
        var library = NativeLibrary.Load(PhysicsTestLibraries.Unified);
        try
        {
            var allocatedBytes = Marshal.GetDelegateForFunctionPointer<AllocatedBytes>(
                NativeLibrary.GetExport(library, "dango_physics_get_allocated_bytes"));
            var initialBytes = allocatedBytes();
            for (var cycle = 0; cycle < 100; cycle++)
            {
                var world = Create(LandingPlatform, 0, 2.5, out var oldId);
                try
                {
                    for (var i = 0; i < 3; i++) Advance(world, new(oldId));
                    world.Remove(oldId);
                    Assert.AreEqual(0, world.Step(Dt, []).Bodies.Count);
                    AssertPhysicsRejection(() => world.ReadStaticPlatformContact(oldId));
                    var newId = world.Add(0, 2.5);
                    Assert.AreNotEqual(oldId, newId);
                    world.Reset();
                    Assert.AreEqual(LandingPlatform, world.StaticPlatform);
                    Assert.AreEqual(newId, Advance(world, new(newId)).EntityId);
                    world.ClearStaticPlatform();
                    world.Remove(newId);
                    Assert.AreEqual(0, world.Count);
                    world.Dispose(); world.Dispose();
                }
                finally { world.Dispose(); }
                Assert.AreEqual(initialBytes, allocatedBytes(), $"Box2D allocation leaked after cycle={cycle}.");
            }
            TestContext.WriteLine($"100 create/remove/re-add/reset/clear/dispose cycles completed; initial/final Box2D bytes={initialBytes}. Process working-set is not a leak oracle.");
        }
        finally { NativeLibrary.Free(library); }
    }

    [TestMethod]
    public void AcceptedOldModuleStillRunsButCannotPretendToProvidePlatformCapability()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.StaticPlatformBaseline);
        var id = world.Add(0, 2.5);
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetStaticPlatform(LandingPlatform));
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport, error.Kind);
        Assert.IsNull(world.StaticPlatform);
        Assert.IsTrue(world.Step(Dt, [new(id)]).Succeeded);
    }

    private static DangoPhysicsWorld Create(DangoStaticPlatformDefinition platform, double x, double y, out DangoEntityId id)
    {
        var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        try { world.SetStaticPlatform(platform); id = world.Add(x, y); return world; }
        catch { world.Dispose(); throw; }
    }

    private static DangoBodyState Settle(DangoPhysicsWorld world, DangoEntityId id)
    {
        DangoBodyState? body = null;
        for (var step = 0; step < 120; step++) body = Advance(world, new(id));
        Assert.IsNotNull(body);
        Assert.IsTrue(world.ReadStaticPlatformContact(id).IsSupported, "Normal landing must acquire real platform support within 120 steps.");
        // The frozen table assigns speed < .03 to the end of the subsequent
        // 1,800 support steps, rather than imposing it on this 120-step entry.
        return body;
    }

    private static DangoBodyState Advance(DangoPhysicsWorld world, DangoPhysicsInput input)
    {
        var frame = world.Step(Dt, [input]);
        Assert.AreEqual(0, frame.ErrorCode, "The native world must succeed; a last-safe fallback frame is not a passing simulation.");
        Assert.AreEqual(1, frame.Bodies.Count);
        var body = frame.Bodies.Single();
        Assert.AreEqual(input.EntityId, body.EntityId);
        Assert.AreEqual(16, body.PointCount);
        Assert.IsTrue(body.Contour.Count == 32 && body.Contour.All(float.IsFinite));
        Assert.IsTrue(float.IsFinite(body.CoreX) && float.IsFinite(body.CoreY) && float.IsFinite(body.SignedArea)
            && float.IsFinite(body.CentroidX) && float.IsFinite(body.CentroidY) && float.IsFinite(body.MaximumSpeed));
        Assert.IsTrue(body.SignedArea >= DangoPhysicsDefaults.Current.MinimumArea, $"area={body.SignedArea:R}");
        Assert.IsTrue(body.MaximumSpeed <= DangoPhysicsDefaults.Current.MaximumSpeed + 1e-5, $"speed={body.MaximumSpeed:R}");
        return body;
    }

    private static void AssertContactBudget(DangoBodyState body, DangoStaticPlatformDefinition platform, int step)
    {
        var penetration = StaticPlatformGeometry.Penetration(body, platform);
        Assert.IsTrue(penetration <= StaticPlatformGeometry.ContactBudget, $"Painted contour penetration={penetration:R}, budget=1/30, step={step}");
    }

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        Assert.Fail("The operation should have been rejected.");
        return new InvalidOperationException("Unreachable assertion path.");
    }

    private static void AssertPhysicsRejection(Action action) => Assert.IsInstanceOfType<DangoPhysicsException>(Capture(action));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AllocatedBytes();
}
