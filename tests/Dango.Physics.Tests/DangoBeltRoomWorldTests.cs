using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DangoBeltRoomWorldTests
{
    private static string Module => RequiredModule("DANGO_BELT_ROOM_MODULE");
    private static string RequiredModule(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value))
            Assert.Inconclusive($"Set {name} to an explicitly built independent native module; native coverage was not executed.");
        return value!;
    }
    private static DangoSelfPhysicsParameters Config => new()
    { RadialElasticity = 1.82, PerimeterElasticity = 15.075, TopInwardForce = 0, GroundDeformationStrength = 0 };

    [TestMethod]
    public void MissingIndependentModuleFailsClosed()
    {
        var absent = Path.Combine(Path.GetTempPath(), $"dango-room-absent-{Guid.NewGuid():N}.dll");
        Assert.ThrowsExactly<DllNotFoundException>(() => new DangoBeltRoomWorld(absent, Config));
    }

    [TestMethod]
    public void IndependentRoomIdentityAndCoherentDetachedFrames()
    {
        using var world = new DangoBeltRoomWorld(Module, Config);
        var before = world.Read();
        Assert.AreNotEqual(0UL, before.Generation);
        Assert.AreEqual(0UL, before.Tick);
        Assert.AreEqual(0UL, before.SceneRevision);
        Assert.AreEqual(1UL, before.BodyId);
        Assert.AreEqual(1UL, before.ConfigurationRevision);
        Assert.IsNull(before.Platform);
        Assert.AreEqual(0, before.Fault);
        world.Step(before.BodyId, new(Horizontal: 1));
        var after = world.Read();
        Assert.AreEqual(before.Generation, after.Generation);
        Assert.AreEqual(1UL, after.Tick);
        Assert.AreEqual(0UL, before.Tick, "Previously returned frame must remain detached.");
        Assert.AreEqual(68, after.Scaffold.Count);
        Assert.AreEqual(512, after.Curve.Count);
        Assert.AreEqual(200, after.BodySnapshot.Count);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<float>)after.Curve)[0] = 10);
    }

    [TestMethod]
    public void FullyDisposedOwnersNeverReuseRetainedFrameIdentity()
    {
        var module = Module;
        DangoBeltRoomFrame retained;
        using (var first = new DangoBeltRoomWorld(module, Config)) retained = first.Read();
        using var second = new DangoBeltRoomWorld(module, Config);
        var fresh = second.Read();
        Assert.AreNotEqual(0UL, retained.OwnerEpoch);
        Assert.AreNotEqual(retained.OwnerEpoch, fresh.OwnerEpoch);
        Assert.AreNotEqual((retained.OwnerEpoch, retained.Generation), (fresh.OwnerEpoch, fresh.Generation));
        second.Step(1);
        Assert.AreEqual(fresh.OwnerEpoch, second.Read().OwnerEpoch);
        Assert.AreEqual(0UL, retained.Tick);
    }

    [TestMethod]
    public void SeparateThreadsReceiveDistinctOwnerEpochs()
    {
        var module = Module;
        var owners = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var world = new DangoBeltRoomWorld(module, Config);
            world.Step(1);
            return world.Read().OwnerEpoch;
        })).ToArray();
        Task.WhenAll(owners).GetAwaiter().GetResult();
        Assert.AreEqual(owners.Length, owners.Select(task => task.Result).Distinct().Count());
    }

    [TestMethod]
    public void NoPlatformExecutionMatchesFrozenSingleBodyChannels()
    {
        using var room = new DangoBeltRoomWorld(Module, Config);
        using var body = new DangoBeltWorld(RequiredModule("DANGO_BELT_MODULE"), Config);
        body.SetBounds(-10, 10);
        for (var tick = 0; tick < 24; tick++)
        {
            var input = new DangoBeltInput(Horizontal: tick < 12 ? 1 : 0);
            body.Step(DangoBeltWorld.FixedDt, input);
            room.Step(1, input);
            var actual = room.Read();
            var expected = body.Read();
            CollectionAssert.AreEqual(expected.Scaffold, actual.Scaffold.ToArray());
            CollectionAssert.AreEqual(expected.Curve, actual.Curve.ToArray());
            CollectionAssert.AreEqual(expected.Stats, actual.Stats.ToArray());
            CollectionAssert.AreEqual(body.ReadDebugState(), actual.BodySnapshot.ToArray());
        }
    }

    [TestMethod]
    public void PlatformTransactionsAreAtomicRevisionedAndDoNotReuseIds()
    {
        using var world = new DangoBeltRoomWorld(Module, Config);
        var before = world.Read();
        var platform = new DangoBeltRoomPlatform(41, 3, 2, 1, .2, .4);
        world.SetPlatform(platform, 0);
        var added = world.Read();
        Assert.AreEqual(platform, added.Platform);
        Assert.AreEqual(1UL, added.SceneRevision);
        Assert.AreEqual(before.Tick, added.Tick);
        CollectionAssert.AreEqual(before.BodySnapshot, added.BodySnapshot);
        Assert.AreEqual(-708, Assert.ThrowsExactly<DangoBeltRoomException>(() => world.SetPlatform(platform with { X = 4 }, 0)).ErrorCode);
        Assert.AreEqual(-708, Assert.ThrowsExactly<DangoBeltRoomException>(() => world.RemovePlatform(42, 1)).ErrorCode);
        AssertStable(added, world.Read());
        Assert.ThrowsExactly<DangoBeltRoomException>(() => world.SetPlatform(platform with { Width = double.NaN }, 1));
        AssertStable(added, world.Read());
        world.SetPlatform(platform with { X = 4 }, 1);
        Assert.AreEqual(2UL, world.Read().SceneRevision);
        world.RemovePlatform(41, 2);
        var removed = world.Read();
        Assert.IsNull(removed.Platform);
        Assert.AreEqual(3UL, removed.SceneRevision);
        Assert.ThrowsExactly<DangoBeltRoomException>(() => world.SetPlatform(platform, 3));
        AssertStable(removed, world.Read());
        world.SetPlatform(platform with { Id = 42 }, 3);
        Assert.AreEqual(4UL, world.Read().SceneRevision);
    }

    [TestMethod]
    public void PhysicalBoundaryRejectionPreservesCompleteAcceptedFrameAndLatchesFault()
    {
        using var world = new DangoBeltRoomWorld(Module, Config, left: -2, right: 2);
        var accepted = world.Read();
        DangoBeltRoomException? rejection = null;
        for (var tick = 0; tick < 480; tick++)
        {
            try { world.Step(1, new(Horizontal: 1)); }
            catch (DangoBeltRoomException error) { rejection = error; break; }
            accepted = world.Read();
        }
        Assert.IsNotNull(rejection, "Walking into the frozen finite boundary must exercise physical refusal, not argument rejection.");
        Assert.IsTrue(rejection.ErrorCode is -705 or -706 or -707);
        var failed = world.Read();
        Assert.AreEqual(rejection.ErrorCode, failed.Fault);
        Assert.AreEqual((double)rejection.ErrorCode, failed.Diagnostics[2]);
        AssertStable(accepted, failed with { Fault = accepted.Fault });
        Assert.AreEqual(-703, Assert.ThrowsExactly<DangoBeltRoomException>(() => world.Step(1)).ErrorCode);
        Assert.AreEqual(-703, Assert.ThrowsExactly<DangoBeltRoomException>(() => world.SetPlatform(new(2, 0, 2, 1, .2, .4), failed.SceneRevision)).ErrorCode);
        AssertStable(failed, world.Read());
    }

    [TestMethod]
    public void InvalidBodyIdentityDoesNotAdvanceOrLatchAndWorldCanContinue()
    {
        using var world = new DangoBeltRoomWorld(Module, Config);
        var before = world.Read();
        Assert.ThrowsExactly<DangoBeltRoomException>(() => world.Step(2));
        AssertStable(before, world.Read());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.Step(1, new(Horizontal: double.NaN)));
        AssertStable(before, world.Read());
        world.Step(1);
        Assert.AreEqual(1UL, world.Read().Tick);
    }

    [TestMethod]
    public void OwnerReadMutationAndDisposeRejectForeignThreadAndUseAfterDispose()
    {
        var world = new DangoBeltRoomWorld(Module, Config);
        try
        {
            Task.Run(() =>
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => world.Read());
                Assert.ThrowsExactly<InvalidOperationException>(() => world.Step(1));
                Assert.ThrowsExactly<InvalidOperationException>(() => world.SetPlatform(new(1, 3, 2, 1, .2, .4), 0));
                Assert.ThrowsExactly<InvalidOperationException>(() => world.RemovePlatform(1, 0));
                Assert.ThrowsExactly<InvalidOperationException>(() => world.Dispose());
            }).GetAwaiter().GetResult();
            Assert.AreEqual(0UL, world.Read().Tick);
        }
        finally { world.Dispose(); }
        world.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.Read());
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.Step(1));
    }

    [TestMethod]
    public void OriginalSingleBodyModuleCannotMasqueradeAsIndependentRoom()
    {
        var singleBodyModule = RequiredModule("DANGO_BELT_MODULE");
        Assert.ThrowsExactly<EntryPointNotFoundException>(() => new DangoBeltRoomWorld(singleBodyModule, Config));
    }

    [TestMethod]
    public void RoomModuleCannotMasqueradeAsOriginalSingleBody()
    {
        var roomModule = Module;
        Assert.ThrowsExactly<EntryPointNotFoundException>(() => new DangoBeltWorld(roomModule, Config));
    }

    [TestMethod]
    [DataRow("DANGO_BELT_ROOM_BAD_ABI_MODULE")]
    [DataRow("DANGO_BELT_ROOM_MISSING_CAPABILITIES_MODULE")]
    [DataRow("DANGO_BELT_ROOM_BAD_FRAME_SIZE_MODULE")]
    public void MalformedNativeIdentityFailsClosed(string variable)
    {
        var module = RequiredModule(variable);
        Assert.ThrowsExactly<NotSupportedException>(() => new DangoBeltRoomWorld(module, Config));
    }

    [TestMethod]
    public void OptionalHorizontalBoundsAreAtomicAndNeverAdvancePhysics()
    {
        using var world = new DangoBeltRoomWorld(Module, Config);
        if (!world.SupportsHorizontalBounds) Assert.Inconclusive("This module does not advertise optional horizontal bounds.");
        world.Step(1,new(Horizontal:1));
        var before=world.Read();
        world.SetHorizontalBounds(-8,8,before.SceneRevision);
        var changed=world.Read();
        Assert.AreEqual(before.Tick,changed.Tick);
        Assert.AreEqual(before.SceneRevision+1,changed.SceneRevision);
        CollectionAssert.AreEqual(before.Curve,changed.Curve);
        CollectionAssert.AreEqual(before.Scaffold,changed.Scaffold);
        CollectionAssert.AreEqual(before.Stats,changed.Stats);
        CollectionAssert.AreEqual(before.Diagnostics,changed.Diagnostics);
        // The snapshot contains native bounds; every other state value is unchanged.
        for(var i=0;i<before.BodySnapshot.Count;i++)
            if(i is not (76 or 77)) Assert.AreEqual(before.BodySnapshot[i],changed.BodySnapshot[i]);
        Assert.AreEqual(-8d,changed.BodySnapshot[76]);
        Assert.AreEqual(8d,changed.BodySnapshot[77]);
        world.SetHorizontalBounds(-8,8,changed.SceneRevision);
        AssertStable(changed,world.Read());
        Assert.ThrowsExactly<DangoBeltRoomException>(()=>world.SetHorizontalBounds(-7,7,before.SceneRevision));
        AssertStable(changed,world.Read());
        Assert.ThrowsExactly<DangoBeltRoomException>(()=>world.SetHorizontalBounds(3,8,changed.SceneRevision));
        AssertStable(changed,world.Read());
        world.SetHorizontalBounds(-256,256,changed.SceneRevision);
        Assert.AreEqual(changed.Tick,world.Read().Tick);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(6)]
    public void StaticActionRegressionsRunThroughTheActualRoomModule(int kind)
    {
        var x = kind == 3 ? -1.3f : kind == 6 ? -2.3f : 0;
        using var world = new DangoBeltRoomWorld(Module, DangoBodyMaterial.Configure(DangoBodyControls.Default),
            x: x, y: kind == 6 ? 0 : 1.5f, left: -20, right: 20);
        world.SetSolidWalls(true, 0);
        var width = kind == 0 ? 3 : 1;
        world.SetPlatform(new(1, 0, .4, width, .2, .72), world.Read().SceneRevision);
        var supported = false;
        var supportedHeight = 0f;
        var departed = false;
        var jumped = false;
        var airborne = false;
        var aboveTop = false;
        var landingTicks = 0;
        var maximumLandingTicks = 0;
        var count = kind == 0 ? 720 : 240;
        for (var tick = 0; tick < count; tick++)
        {
            var horizontal = kind == 0 ? tick >= 120 ? 1 : 0 : kind == 3 || !supported ? 1 : 0;
            world.Step(1, new(Horizontal: horizontal, JumpPressed: kind == 6 && tick == 90));
            var frame = world.Read();
            Assert.AreEqual(0, frame.Fault);
            Assert.IsTrue(frame.SolidWallsEnabled);
            var onPlatform = frame.Diagnostics[18] == 1 && frame.Diagnostics[19] == 0;
            if (onPlatform) { supported = true; supportedHeight = frame.Stats[1]; }
            if (frame.Stats[15] == 1) jumped = true;
            if (jumped && frame.Stats[14] == 0) airborne = true;
            if (airborne && frame.Stats[9] > .5) aboveTop = true;
            landingTicks = aboveTop && onPlatform && frame.Stats[14] == 1 ? landingTicks + 1 : 0;
            maximumLandingTicks = Math.Max(maximumLandingTicks, landingTicks);
            if (supported && tick >= 120 && frame.Stats[8] > width / 2d &&
                !onPlatform && frame.Stats[1] < supportedHeight - .02) departed = true;
        }
        Assert.AreEqual((ulong)count, world.Read().Tick);
        if (kind == 0) Assert.IsTrue(departed, "The complete body must leave the platform and descend after support.");
        if (kind == 6)
        {
            Assert.IsTrue(jumped && airborne && aboveTop);
            Assert.IsGreaterThanOrEqualTo(20, maximumLandingTicks, "Jumping must end in actual sustained platform support.");
        }
    }

    [TestMethod]
    public void OptionalSolidWallsChangeOnlyModeAndRevisionUntilTheNextStep()
    {
        using var world = new DangoBeltRoomWorld(Module, Config, left: -2, right: 2);
        Assert.IsTrue(world.SupportsSolidWalls);
        var before = world.Read();
        Assert.IsFalse(before.SolidWallsEnabled);
        world.SetSolidWalls(true, before.SceneRevision);
        var enabled = world.Read();
        Assert.IsTrue(enabled.SolidWallsEnabled);
        Assert.AreEqual(before.SceneRevision + 1, enabled.SceneRevision);
        Assert.AreEqual(before.Tick, enabled.Tick);
        CollectionAssert.AreEqual(before.BodySnapshot, enabled.BodySnapshot);
        CollectionAssert.AreEqual(before.Curve, enabled.Curve);
        CollectionAssert.AreEqual(before.Stats, enabled.Stats);
        for (var i = 0; i < 23; i++) Assert.AreEqual(before.Diagnostics[i], enabled.Diagnostics[i]);
        world.SetSolidWalls(true, enabled.SceneRevision);
        AssertStable(enabled, world.Read());
        Assert.ThrowsExactly<DangoBeltRoomException>(() => world.SetSolidWalls(false, before.SceneRevision));
        AssertStable(enabled, world.Read());
        Assert.IsTrue(world.Read().SolidWallsEnabled);
        world.Step(1);
        Assert.IsTrue(world.Read().SolidWallsEnabled);
        world.SetSolidWalls(false, world.Read().SceneRevision);
        Assert.IsFalse(world.Read().SolidWallsEnabled);
    }

    [TestMethod]
    public void SolidWallsContainSustainedDriveAndAllowTheBodyToLeave()
    {
        using var world = new DangoBeltRoomWorld(Module, Config, left: -2, right: 2);
        world.SetSolidWalls(true, 0);
        for (var i = 0; i < 360; i++)
        {
            world.Step(1, new(Horizontal: 1, InputActive: true));
            var frame = world.Read();
            Assert.AreEqual(0, frame.Fault);
            Assert.IsTrue(frame.SolidWallsEnabled);
            Assert.IsTrue(frame.Stats[8] >= -2 && frame.Stats[10] <= 2);
        }
        var atWall = world.Read();
        Assert.IsGreaterThan(1.99f, atWall.Stats[10], "The body must actually reach the right wall.");
        for (var i = 0; i < 120; i++) world.Step(1, new(Horizontal: -1, InputActive: true));
        Assert.IsLessThan(atWall.Stats[0] - .1f, world.Read().Stats[0]);
    }

    [TestMethod]
    public void LegacyCapability15ModuleRemainsLoadableButRejectsOptionalBounds()
    {
        var module=RequiredModule("DANGO_BELT_ROOM_LEGACY_MODULE");
        using var world=new DangoBeltRoomWorld(module,Config);
        Assert.IsFalse(world.SupportsHorizontalBounds);
        Assert.IsFalse(world.SupportsSolidWalls);
        var before=world.Read();
        Assert.ThrowsExactly<NotSupportedException>(()=>world.SetHorizontalBounds(-8,8,0));
        Assert.ThrowsExactly<NotSupportedException>(()=>world.SetSolidWalls(true,0));
        AssertStable(before,world.Read());
        world.Step(1);
        Assert.AreEqual(1UL,world.Read().Tick);
    }

    private static void AssertStable(DangoBeltRoomFrame expected, DangoBeltRoomFrame actual)
    {
        Assert.AreEqual(expected.OwnerEpoch, actual.OwnerEpoch);
        Assert.AreEqual(expected.Generation, actual.Generation);
        Assert.AreEqual(expected.Tick, actual.Tick);
        Assert.AreEqual(expected.SceneRevision, actual.SceneRevision);
        Assert.AreEqual(expected.BodyId, actual.BodyId);
        Assert.AreEqual(expected.ConfigurationRevision, actual.ConfigurationRevision);
        Assert.AreEqual(expected.Platform, actual.Platform);
        Assert.AreEqual(expected.Fault, actual.Fault);
        CollectionAssert.AreEqual(expected.BodySnapshot, actual.BodySnapshot);
        CollectionAssert.AreEqual(expected.Scaffold, actual.Scaffold);
        CollectionAssert.AreEqual(expected.Curve, actual.Curve);
        CollectionAssert.AreEqual(expected.Stats, actual.Stats);
    }
}
