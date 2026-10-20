using Dango.Core.Furniture;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Physics;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation;
using Dango.Core.Animation;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class BeltRoomResumeSafetyTests
{
    private static DangoBeltRoomFrame Frame(double radius = 2)
    {
        var q = new double[200]; q[0] = 1; q[1] = 200;
        q[4] = radius; q[7] = radius; q[82] = 1;
        var stats = new float[64]; stats[14] = 1; stats[25] = 1;
        var curve = Enumerable.Range(0, 256).SelectMany(i => new[] {
            (float)(radius * Math.Cos(2 * Math.PI * i / 256)),
            (float)(radius * Math.Sin(2 * Math.PI * i / 256)) }).ToArray();
        return new(1, 1, 1, 1, 1, 1, null, 0, Array.AsReadOnly(q),
            Array.AsReadOnly(new float[68]), Array.AsReadOnly(curve), Array.AsReadOnly(stats), Array.AsReadOnly(new double[24]));
    }

    [TestMethod]
    public void AppliedLargeBodyCannotReuseTheOldNeutralCheckpointEnvelope()
    {
        var frame = Frame();
        var envelope = RoomResumeSafety.BeltEnvelope(frame)!.Value;
        Assert.IsTrue(RoomResumeSafety.CanRestore(new(120, 0), FurnitureLayout.Empty));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(120, 0), FurnitureLayout.Empty, envelope));
        var platform = new FurnitureItemInstance(Guid.NewGuid(), FurnitureItemKind.StaticPlatform, new(1080, 250));
        var layout = FurnitureLayout.ForItems(1, Guid.NewGuid(), [platform]);
        Assert.IsTrue(RoomResumeSafety.CanRestore(new(1080, 0), layout));
        Assert.IsFalse(RoomResumeSafety.CanRestore(new(1080, 0), layout, envelope));
    }

    [TestMethod]
    public void FourierEnvelopeContainsUnsampledHighHarmonicsAndAsymmetricCoreOffsets()
    {
        var original = Frame(.8); var q = original.BodySnapshot.ToArray();
        q[2] = .25; q[36] = -.2; q[32] = .14; q[34] = -.11; q[33] = -.08; q[35] = .09;
        var frame = original with { BodySnapshot = Array.AsReadOnly(q) };
        var envelope = RoomResumeSafety.BeltEnvelope(frame)!.Value;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (var sample = 0; sample < 8193; sample++)
        {
            var t = 2 * Math.PI * (sample + .37) / 8193;
            double x = q[2] - q[36], y = q[3];
            for (var n = 1; n <= 8; n++)
            {
                var i = 4 * n;
                x += q[i] * Math.Cos(n * t) + q[i + 2] * Math.Sin(n * t);
                y += q[i + 1] * Math.Cos(n * t) + q[i + 3] * Math.Sin(n * t);
            }
            Assert.IsTrue(x * 72 >= envelope.Left && x * 72 <= envelope.Right);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        Assert.IsTrue((maxY - minY) * 72 <= envelope.Height);
        Assert.IsTrue(envelope.Right > -envelope.Left);
        q[4] = double.NaN;
        Assert.IsNull(RoomResumeSafety.BeltEnvelope(frame with { BodySnapshot = Array.AsReadOnly(q) }));
    }

    [TestMethod]
    public void PlatformSupportAndJumpingCannotBeSavedAsFloorSupport()
    {
        var frame = Frame(); Assert.IsTrue(RoomResumeSafety.HasBeltFloorSupport(frame));
        var diag = frame.Diagnostics.ToArray(); diag[18] = 17;
        Assert.IsFalse(RoomResumeSafety.HasBeltFloorSupport(frame with { Diagnostics = Array.AsReadOnly(diag) }));
        var stats = frame.Stats.ToArray(); stats[12] = 2;
        Assert.IsFalse(RoomResumeSafety.HasBeltFloorSupport(frame with { Stats = Array.AsReadOnly(stats) }));
        stats[12] = 0; stats[25] = 0;
        Assert.IsFalse(RoomResumeSafety.HasBeltFloorSupport(frame with { Stats = Array.AsReadOnly(stats) }));
        Assert.IsFalse(RoomResumeSafety.HasBeltFloorSupport(frame with { Tick = 0 }));
        Assert.IsFalse(RoomResumeSafety.HasBeltFloorSupport(frame with { Fault = -1 }));
    }

    [TestMethod]
    public void BeltDragUsesFrozenContinuousAttachmentAndEqualAxisScaleWithoutNodeContraction()
    {
        var frame = Frame(); var q = frame.BodySnapshot.ToArray();
        q[83] = 1; q[80] = Math.PI / 4;
        var anchor = LocalRoomSceneDriver.BeltDragAnchor(frame with { BodySnapshot = Array.AsReadOnly(q) });
        Assert.AreEqual(Math.Sqrt(2) * 72, anchor.X, 1e-9);
        Assert.AreEqual(-Math.Sqrt(2) * 72, anchor.Y, 1e-9);
    }

    [TestMethod]
    public void DesktopPetGestureGuardTracksActualBeltExtremaInsteadOfNeutralControlBounds()
    {
        var frame = Frame(); var stats = frame.Stats.ToArray();
        stats[0] = 3; stats[1] = 4; stats[8] = 1; stats[9] = 2; stats[10] = 5; stats[11] = 7;
        var bounds = DesktopPetDangoWindow.BeltBodyWorldBounds(frame with { Stats = Array.AsReadOnly(stats) }, new LogicalPoint(1000, 300));
        Assert.AreEqual(new LogicalRect(856, 84, 288, 360), bounds);
    }

    [STATestMethod]
    public void BindRoomChecksCurrentAppliedNativeGeometryAndKeepsResumeSchemaAndLayout()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(TestRepository.Root, "artifacts", "belt-room-f7-connection", "native", "build", "Release", "dango_belt_room.dll");
        if (!File.Exists(module)) Assert.Inconclusive("Independent belt room DLL is required.");
        var root = Path.Combine(AppContext.BaseDirectory, "artifacts", "test-runs", "belt-resume", Guid.NewGuid().ToString("N"));
        var catalog = new LocalRoomCatalogStore(root); var room = catalog.Refresh().Catalog!.Rooms.Single();
        var resume = new RoomResumeStore(catalog);
        var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default.WithBody(new DangoBodyControls(120, 100, 0)));
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var envelope = RoomResumeSafety.BeltEnvelope(runtime.LastBeltRoomFrame)!.Value;
        using var driver = new LocalRoomSceneDriver(new DesktopSceneSession(), runtime);
        var safe = new RoomSafePosition(FurnitureLayoutContract.OrdinaryRoom.Left - envelope.Left + .5, 0);
        Assert.IsTrue(RoomResumeSafety.CanRestore(safe, FurnitureLayout.Empty, envelope));
        Assert.IsFalse(RoomResumeSafety.CanRestore(safe, FurnitureLayout.Empty), "The native initial body must differ from the old neutral envelope.");
        resume.Save(room.RoomId, 0, safe, camera);
        driver.BindRoom(catalog, room, resume.Load(room.RoomId).Value);
        Assert.IsNull(driver.ResumeWarning);
        var unsafePoint = safe with { X = safe.X - 1 };
        resume.Save(room.RoomId, 0, unsafePoint, camera);
        var original = resume.Load(room.RoomId).Snapshot.Bytes!;
        driver.BindRoom(catalog, room, resume.Load(room.RoomId).Value);
        Assert.IsNotNull(driver.ResumeWarning);
        CollectionAssert.AreEqual(original, resume.Load(room.RoomId).Snapshot.Bytes!);
        Assert.AreEqual(1, resume.Load(room.RoomId).Value!.SchemaVersion);
        Assert.AreEqual(FurnitureLayout.Empty, catalog.ReadLayout(room.RoomId));
        Assert.ThrowsExactly<InvalidDataException>(() => driver.BindRoom(catalog, room,
            resume.Load(room.RoomId).Value! with { RoomId = Guid.NewGuid() }));
    }

    [STATestMethod]
    public void ReentryUsesLatestSavedBodyWhileDraftAndRunningChangesStayOutsideTheRoom()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(TestRepository.Root, "artifacts", "belt-room-f7-connection", "native", "build", "Release", "dango_belt_room.dll");
        if (!File.Exists(module)) Assert.Inconclusive("Independent belt room DLL is required.");
        var path = Path.Combine(AppContext.BaseDirectory, "artifacts", "test-runs", "belt-appearance", Guid.NewGuid().ToString("N"), "appearance.json");
        var appearance = new AppearanceSettingsSession(path);
        appearance.StagePreview(DangoAppearanceConfiguration.Default.WithBody(new DangoBodyControls(120, 100, 0)));
        // Exercise the storage acknowledgement contract separately from the hidden render test.
        Assert.IsTrue(appearance.MarkBodyApplied(appearance.DraftAppearance.Body, appearance.DraftAppearance.BodyPhysicsModel,
            appearance.BodyConfigurationRevision, appearance.BodyEpoch));
        Assert.IsTrue(appearance.TrySave());
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        runtime.ConfigureBeltRoomAppearance(appearance.SavedAppearance);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var before = runtime.LastBeltRoomFrame!;
        appearance.StagePreview(appearance.SavedAppearance.WithBody(new DangoBodyControls(80, 20, 100)));
        runtime.Start();
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.ConfigureBeltRoomAppearance(appearance.DraftAppearance));
        runtime.Stop();
        runtime.ConfigureBeltRoomAppearance(appearance.SavedAppearance);
        Assert.AreEqual(120, runtime.EffectiveAppearance.Body.SizePercent);
        Assert.AreEqual(before.Generation, runtime.LastBeltRoomFrame!.Generation);
        Assert.IsFalse(appearance.TrySave(), "An unapplied draft must remain unsaved.");
        Assert.IsTrue(appearance.MarkBodyApplied(appearance.DraftAppearance.Body, appearance.DraftAppearance.BodyPhysicsModel,
            appearance.BodyConfigurationRevision, appearance.BodyEpoch));
        Assert.IsTrue(appearance.TrySave());
        runtime.ConfigureBeltRoomAppearance(appearance.SavedAppearance);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        Assert.AreEqual(appearance.SavedAppearance.Body, runtime.EffectiveAppearance.Body);
        // A DLL unload/reload may restart its native generation counter; the owner epoch scopes it.
        Assert.AreNotEqual((before.OwnerEpoch, before.Generation), (runtime.LastBeltRoomFrame!.OwnerEpoch, runtime.LastBeltRoomFrame.Generation));
        Assert.IsTrue(RoomResumeSafety.BeltEnvelope(runtime.LastBeltRoomFrame)!.Value.Height < RoomResumeSafety.BeltEnvelope(before)!.Value.Height);
    }

    [STATestMethod]
    public void ExplicitNewRoomClearsThePreviousRoomsMovingBodyEvenWhenSavedControlsMatch()
    {
        var module = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE") ??
            Path.Combine(TestRepository.Root, "artifacts", "belt-room-f7-connection", "native", "build", "Release", "dango_belt_room.dll");
        if (!File.Exists(module)) Assert.Inconclusive("Independent belt room DLL is required.");
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(module);
        var saved = DangoAppearanceConfiguration.Default;
        runtime.ConfigureBeltRoomAppearance(saved, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var initial = runtime.LastBeltRoomFrame!;
        var session = new DesktopSceneSession(); session.AttachSurface(new(0, 0, 2160, 360), 0);
        var c = session.Controller!;
        var input = PhysicsPresentationInput.FromSnapshot(c.Snapshot, c.SquashAmount, c.GroundFrictionCue, c.FacingDirection, null);
        runtime.Start();
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 30; i++) runtime.Tick(input, HostFrameClock.FixedStep);
        Assert.IsNull(runtime.LastError);
        var moving = runtime.LastBeltRoomFrame!;
        Assert.IsTrue(moving.Tick > 0);
        Assert.IsTrue(moving.BodySnapshot.Skip(38).Take(36).Any(v => Math.Abs(v) > 1e-4), "Fixture must contain actual native velocity.");
        runtime.Stop();
        runtime.ConfigureBeltRoomAppearance(saved, beginRoom: true);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var entered = runtime.LastBeltRoomFrame!;
        Assert.AreNotEqual(moving.OwnerEpoch, entered.OwnerEpoch);
        Assert.AreNotEqual((moving.OwnerEpoch, moving.Generation), (entered.OwnerEpoch, entered.Generation));
        Assert.AreEqual(initial.Tick, entered.Tick);
        CollectionAssert.AreEqual(initial.BodySnapshot, entered.BodySnapshot, "A new room must start its own q/v rather than inherit old motion.");
        Assert.IsFalse(runtime.IsDragging);
    }
}
