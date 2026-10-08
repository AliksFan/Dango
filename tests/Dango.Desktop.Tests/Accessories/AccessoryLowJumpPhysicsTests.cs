using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryLowJumpPhysicsTests
{
    private static readonly TimeSpan Step = HostFrameClock.FixedStep;
    private static readonly LogicalRect Bounds = new(400, 350, 160, 120);
    private static readonly PhysicsPresentationInput Input = new("fictional-low-jump-preview", Bounds,
        DangoActionState.Idle, 0, 1, "fictional-ground", new(0, 1), 0, 1, false, true, []);
    private static string CandidatePath => Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
        Path.Combine(TestRepository.Root, "artifacts", "native-spike", "accessory-low-jump-build-20261008", "Release",
            DangoPhysicsWorld.NativeLibraryFileName);
    private static string BaselinePath => Path.Combine(TestRepository.Root, "artifacts", "native-spike",
        "physics-module-unified-build", "Release", DangoPhysicsWorld.NativeLibraryFileName);

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void DefaultCandidatePreservesEveryAcceptedNativeFrameBit(int count)
    {
        using var candidate = DangoPhysicsWorld.Create(CandidatePath);
        using var baseline = DangoPhysicsWorld.Create(BaselinePath);
        var candidateIds = AddBodies(candidate, count);
        var baselineIds = AddBodies(baseline, count);
        var handle = Handle(candidate);
        Assert.AreEqual(1d, candidate.PreviewJumpSpeedScale);
        for (var index = 0; index < 1200; index++)
        {
            if (index is 0 or 325 or 625) candidate.SetPreviewJumpSpeedScale(1);
            if (index == 900) { candidate.Reset(); baseline.Reset(); }
            var commands = Controls(candidateIds, index);
            var actual = candidate.Step(Step, commands);
            var expected = baseline.Step(Step, commands.Select((value, body) => value with { EntityId = baselineIds[body] }).ToArray());
            AssertFramesBitEqual(expected, actual, $"default count={count}, step={index}");
            Assert.IsTrue(actual.Succeeded, $"The accepted baseline must remain healthy: count={count}, step={index}.");
            Assert.AreEqual(handle, Handle(candidate), "Default setters and reset must retain the native world.");
            Assert.AreEqual(1d, candidate.PreviewJumpSpeedScale);
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void OldModuleRunsDefaultAndReportsMissingLowJumpCapabilityWithoutChangingItsTrajectory()
    {
        using var world = DangoPhysicsWorld.Create(BaselinePath);
        using var reference = DangoPhysicsWorld.Create(BaselinePath);
        var id = world.Add(0, 0); var referenceId = reference.Add(0, 0);
        var handle = Handle(world);
        world.SetPreviewJumpSpeedScale(1);
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetPreviewJumpSpeedScale(.5));
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport, error.Kind);
        StringAssert.Contains(error.Message, "dango_physics_world_set_preview_jump_speed_scale");
        Assert.AreEqual(1d, world.PreviewJumpSpeedScale);
        world.SetPreviewJumpSpeedScale(1);
        for (var index = 0; index < 720; index++)
        {
            var command = Controls([id], index).Single();
            var actual = world.Step(Step, [command]);
            var expected = reference.Step(Step, [command with { EntityId = referenceId }]);
            AssertFramesBitEqual(expected, actual, $"missing export step={index}");
            Assert.IsTrue(actual.Succeeded);
            Assert.AreEqual(handle, Handle(world));
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void InvalidScalesAreRejectedAtomicallyByManagedAndNativeBoundaries()
    {
        using var world = DangoPhysicsWorld.Create(CandidatePath);
        using var reference = DangoPhysicsWorld.Create(CandidatePath);
        using var nativeScale = new NativeScaleCall(CandidatePath);
        var id = world.Add(0, 0); var referenceId = reference.Add(0, 0);
        world.SetPreviewJumpSpeedScale(.5); reference.SetPreviewJumpSpeedScale(.5);
        var handle = Handle(world);
        foreach (var invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -.5, 0, .099, 1.001 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => world.SetPreviewJumpSpeedScale(invalid));
            Assert.AreEqual(-410, nativeScale.Invoke(handle, (float)invalid), "The optional native export must reject invalid scale values itself.");
            Assert.AreEqual(.5, world.PreviewJumpSpeedScale);
            AssertFramesBitEqual(reference.Step(Step, [new(referenceId)]), world.Step(Step, [new(id)]), $"invalid={invalid}");
            Assert.AreEqual(handle, Handle(world));
        }
        // A rejected raw export could corrupt a launch-only field without
        // changing idle frames. Compare a grounded jump before any valid setter.
        for (var index = 0; index < 420; index++)
            AssertFramesBitEqual(reference.Step(Step, [new(referenceId, JumpPressed: index == 300)]),
                world.Step(Step, [new(id, JumpPressed: index == 300)]), $"jump after invalid scale step={index}");
        foreach (var valid in new[] { .1, .5, 1 })
        {
            world.SetPreviewJumpSpeedScale(valid); reference.SetPreviewJumpSpeedScale(valid);
            Assert.AreEqual(valid, world.PreviewJumpSpeedScale);
            AssertFramesBitEqual(reference.Step(Step, [new(referenceId)]), world.Step(Step, [new(id)]), $"valid boundary={valid}");
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void MultiBodyLowJumpIsRejectedWithoutChangingDefaultScaleOrAnyNativeBody()
    {
        using var world = DangoPhysicsWorld.Create(CandidatePath);
        using var reference = DangoPhysicsWorld.Create(CandidatePath);
        using var nativeScale = new NativeScaleCall(CandidatePath);
        var ids = AddBodies(world, 2); var referenceIds = AddBodies(reference, 2);
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetPreviewJumpSpeedScale(.5));
        Assert.AreEqual(DangoPhysicsFailureKind.InvalidConfiguration, error.Kind);
        Assert.AreEqual(-410, nativeScale.Invoke(Handle(world), .5f));
        Assert.AreEqual(1d, world.PreviewJumpSpeedScale);
        world.SetPreviewJumpSpeedScale(1);
        for (var index = 0; index < 720; index++)
        {
            var commands = Controls(ids, index);
            AssertFramesBitEqual(reference.Step(Step, commands.Select((value, body) => value with { EntityId = referenceIds[body] }).ToArray()),
                world.Step(Step, commands), $"multibody rejection step={index}");
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ScaleChangesAreReadOnlyForPublishedFramesAndPreserveTheActiveWorldInputAndFrameDriver()
    {
        using var rig = new Rig();
        using var reference = new Rig();
        for (var index = 0; index < 450; index++)
        {
            var command = new DirectionalControlIntent(false, index is >= 300 and < 345,
                index is >= 310 and < 345, false);
            rig.Runtime.SetDirectionalControl(command); reference.Runtime.SetDirectionalControl(command);
            if (index == 315)
            {
                var published = rig.Runtime.LastFrame;
                var physical = rig.Runtime.LastPhysicsFrame;
                var steps = rig.Scheduler.Snapshot.SuccessfulSteps;
                rig.Runtime.SetPreviewJumpSpeedScale(.5);
                Assert.AreEqual(.5, rig.Runtime.PreviewJumpSpeedScale);
                Assert.AreEqual(.5, rig.Scene.PreviewJumpSpeedScale);
                rig.AssertIdentity();
                Assert.AreSame(published, rig.Runtime.LastFrame);
                Assert.AreSame(physical, rig.Runtime.LastPhysicsFrame);
                Assert.AreEqual(steps, rig.Scheduler.Snapshot.SuccessfulSteps);
                rig.Runtime.SetPreviewJumpSpeedScale(1);
                Assert.AreEqual(1d, rig.Runtime.PreviewJumpSpeedScale);
                Assert.AreSame(published, rig.Runtime.LastFrame);
                Assert.AreSame(physical, rig.Runtime.LastPhysicsFrame);
            }
            rig.Advance(); reference.Advance();
            AssertFramesBitEqual(reference.Runtime.LastPhysicsFrame!, rig.Runtime.LastPhysicsFrame!, $"live scale switch step={index}");
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RealPreviewJumpHasTwentyToFortyFivePercentHeightAndRestoresNormalJumpOnTheSameBody()
    {
        using var rig = new Rig();
        rig.Advance(300);
        var ordinary = Jump(rig);
        Assert.IsTrue(ordinary.Height > 1, "The comparison must exercise the real accepted high jump.");
        rig.Advance(180);
        SetScaleWithoutStep(rig, .5);
        var low = Jump(rig);
        var ratio = low.Height / ordinary.Height;
        Assert.IsTrue(ratio is >= .20 and <= .45,
            $"Low jump must change native CoreY height, not only presentation: normal={ordinary.Height:F6}, low={low.Height:F6}, ratio={ratio:F6}.");
        rig.Advance(180);
        SetScaleWithoutStep(rig, 1);
        var restored = Jump(rig);
        Console.WriteLine($"Native CoreY jump height: ordinary={ordinary.Height:F6}, low={low.Height:F6}, ratio={ratio:F6}, restored={restored.Height:F6} world units.");
        Assert.AreEqual(ordinary.Height, restored.Height, ordinary.Height * .02,
            "Exiting preview must restore ordinary native launch height on the existing body.");
        Assert.AreEqual(1d, rig.Runtime.PreviewJumpSpeedScale);
        Assert.AreEqual(1d, rig.Scene.PreviewJumpSpeedScale);
        rig.AssertIdentity();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ExplicitRegenerationKeepsTheDesiredLowJumpScaleAndExistingWorldAndEntity()
    {
        using var rig = new Rig();
        SetScaleWithoutStep(rig, .5);
        rig.Advance(300);
        rig.Runtime.SetDirectionalControl(new(false, false, true, false));
        rig.Advance(18);
        Assert.IsFalse(rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded);
        rig.Runtime.RegenerateDango();
        Assert.AreEqual(.5, rig.Runtime.PreviewJumpSpeedScale);
        Assert.AreEqual(.5, rig.Scene.PreviewJumpSpeedScale);
        Assert.IsNull(rig.Runtime.LastFrame);
        rig.AssertIdentity();
        using var reference = new DesktopPhysicsScene(CandidatePath, Bounds);
        reference.SetPreviewJumpSpeedScale(.5);
        var resetSteps = rig.Scheduler.Snapshot.SuccessfulSteps;
        for (var index = 0; index < 480; index++)
        {
            var command = new DirectionalControlIntent(false, false, index == 300, false);
            rig.Runtime.SetDirectionalControl(command); reference.SetInput(command);
            rig.Advance(assertTotalPresenterSteps: false);
            var expected = reference.Step(Step);
            AssertFramesBitEqual(expected.Physics, rig.Runtime.LastPhysicsFrame!, $"reset low jump step={index}");
            Assert.AreEqual(index + 1, rig.Runtime.LastFrame!.Bodies.Single().Diagnostics.SimulatedStepCount);
            Assert.AreEqual(resetSteps + index + 1, rig.Scheduler.Snapshot.SuccessfulSteps);
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void DefaultScaleCanBeRestoredAfterARealLatchedNativeFaultWithoutResettingIt()
    {
        // A smaller experimental body starts above the area limit, then real
        // squash crosses it with area pressure and ground spreading disabled.
        // All values remain within the public configuration's legal ranges.
        using var world = DangoPhysicsWorld.Create(CandidatePath,
            configuration: DangoPhysicsDefaults.Current with
            {
                RadialRestLength = .62,
                MinimumArea = 1.2,
                AreaPreservationStrength = 0,
                GroundDeformationStrength = 0,
                DownwardForce = 12,
            });
        var id = world.Add(0, 0);
        var handle = Handle(world);
        world.SetPreviewJumpSpeedScale(.5);
        DangoPhysicsFrame? fault = null;
        for (var index = 0; index < 600 && world.LastErrorCode == 0; index++)
            fault = world.Step(Step, [new(id, Downward: 1)]);
        Assert.IsNotNull(fault);
        Assert.IsFalse(fault.Succeeded, "The fixture must produce a real native fault before testing cleanup.");
        Assert.AreEqual(-88, world.LastErrorCode, "The controlled fault must be the configured native area gate.");
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() => world.SetPreviewJumpSpeedScale(.75));
        Assert.AreEqual(DangoPhysicsFailureKind.InvalidConfiguration, error.Kind);
        Assert.AreEqual(.5, world.PreviewJumpSpeedScale);
        world.SetPreviewJumpSpeedScale(1);
        Assert.AreEqual(1d, world.PreviewJumpSpeedScale);
        Assert.AreEqual(-88, world.LastErrorCode, "Restoration must not conceal or reset the physics fault.");
        Assert.AreEqual(handle, Handle(world));
        AssertFramesBitEqual(fault, world.Step(Step, [new(id)]), "restored fault remains frozen");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PublishedRuntimeFaultDoesNotPreventDefaultScaleCleanupOrRecreateItsWorld()
    {
        using var rig = new Rig();
        rig.Advance(300);
        SetScaleWithoutStep(rig, .5);
        var published = rig.Runtime.LastFrame;
        var physical = rig.Runtime.LastPhysicsFrame;
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!
            .SetValue(rig.Runtime, "fictional published physics fault");
        Assert.ThrowsExactly<InvalidOperationException>(() => rig.Runtime.SetPreviewJumpSpeedScale(.75));
        Assert.AreEqual(.5, rig.Runtime.PreviewJumpSpeedScale);
        rig.Runtime.SetPreviewJumpSpeedScale(1);
        Assert.AreEqual(1d, rig.Runtime.PreviewJumpSpeedScale);
        Assert.AreEqual(1d, rig.Scene.PreviewJumpSpeedScale);
        Assert.AreEqual("fictional published physics fault", rig.Runtime.LastError);
        Assert.AreSame(published, rig.Runtime.LastFrame);
        Assert.AreSame(physical, rig.Runtime.LastPhysicsFrame);
        rig.AssertIdentity();
    }

    private static void SetScaleWithoutStep(Rig rig, double scale)
    {
        var published = rig.Runtime.LastFrame;
        var physical = rig.Runtime.LastPhysicsFrame;
        var steps = rig.Scheduler.Snapshot.SuccessfulSteps;
        rig.Runtime.SetPreviewJumpSpeedScale(scale);
        Assert.AreEqual(scale, rig.Runtime.PreviewJumpSpeedScale);
        Assert.AreEqual(scale, rig.Scene.PreviewJumpSpeedScale);
        Assert.AreSame(published, rig.Runtime.LastFrame, "Configuration must not publish an extra presentation frame.");
        Assert.AreSame(physical, rig.Runtime.LastPhysicsFrame, "Configuration must not step the native body.");
        Assert.AreEqual(steps, rig.Scheduler.Snapshot.SuccessfulSteps);
        rig.AssertIdentity();
    }

    private static JumpEvidence Jump(Rig rig)
    {
        var baseline = rig.Runtime.LastPhysicsFrame!.Bodies.Single();
        Assert.IsTrue(baseline.IsGrounded && baseline.GroundContactCount > 0, "A real grounded body is required for every jump.");
        var peak = (double)baseline.CoreY;
        var previous = (double)baseline.CoreY;
        var airborne = 0; var rising = 0; var falling = 0; var landed = false;
        rig.Runtime.SetDirectionalControl(new(false, false, true, false));
        for (var index = 0; index < 180; index++)
        {
            rig.Advance();
            var body = rig.Runtime.LastPhysicsFrame!.Bodies.Single();
            peak = Math.Max(peak, body.CoreY);
            if (!body.IsGrounded)
            {
                airborne++;
                if (body.CoreY > previous + 1e-4) rising++;
                if (body.CoreY < previous - 1e-4) falling++;
            }
            if (airborne > 0 && body.IsGrounded && body.GroundContactCount > 0) landed = true;
            previous = body.CoreY;
            rig.Runtime.SetDirectionalControl(DirectionalControlIntent.None);
        }
        Assert.IsTrue(airborne >= 5 && rising >= 3 && falling >= 3 && landed,
            $"The native arc must leave, rise, fall and contact the floor: airborne={airborne}, rising={rising}, falling={falling}, landed={landed}.");
        return new(peak - baseline.CoreY);
    }

    private static DangoEntityId[] AddBodies(DangoPhysicsWorld world, int count) => Enumerable.Range(0, count)
        .Select(index => world.Add(index == 0 ? 0 : index == 1 ? -2.8 : 2.8, 0)).ToArray();

    private static DangoPhysicsInput[] Controls(DangoEntityId[] ids, int step) => ids.Select((id, index) => new DangoPhysicsInput(id,
        Horizontal: index == 0 && step is >= 300 and < 360 ? 1 : index == 0 && step is >= 360 and < 420 ? -1 : 0,
        Downward: index == 0 && step is >= 480 and < 510 ? 1 : 0,
        JumpPressed: step == 600 && index == 0 || step == 1050 && index == ids.Length - 1,
        DragActive: index == 0 && step is >= 720 and < 780,
        DragTargetX: 0, DragTargetY: 1.4)).ToArray();

    private static void AssertFramesBitEqual(DangoPhysicsFrame expected, DangoPhysicsFrame actual, string context)
    {
        Assert.AreEqual(expected.ErrorCode, actual.ErrorCode, context);
        Assert.AreEqual(expected.ContactStepCount, actual.ContactStepCount, context);
        Assert.AreEqual(expected.Bodies.Count, actual.Bodies.Count, context);
        for (var index = 0; index < expected.Bodies.Count; index++)
        {
            var left = expected.Bodies[index]; var right = actual.Bodies[index];
            Assert.AreEqual(left.EntityId, right.EntityId, context);
            var expectedRaw = Raw(left); var actualRaw = Raw(right);
            for (var field = 0; field < expectedRaw.Length; field++)
                Assert.AreEqual(BitConverter.SingleToInt32Bits(expectedRaw[field]), BitConverter.SingleToInt32Bits(actualRaw[field]),
                    $"{context}; body={index}, field={field}");
        }
    }

    private static float[] Raw(DangoBodyState body) => [.. body.Contour,
        body.CoreX, body.CoreY, body.SignedArea, body.CentroidX, body.CentroidY, body.MaximumSpeed,
        body.GroundContactCount, body.IsGrounded ? 1 : 0, body.SelectedDragNode, body.PointCount];

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static nint Handle(DangoPhysicsWorld world) => Field<nint>(world, "world");
    private sealed record JumpEvidence(double Height);

    private sealed class Source : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long ticks) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [TimeSpan.FromTicks(ticks)], null)!);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Source source = new();
        private readonly DangoPhysicsWorld world;
        private readonly nint handle;
        private readonly DangoEntityId entity;
        private long ticks;
        internal StackedDangoSceneRuntime Runtime { get; } = StackedDangoSceneRuntime.CreateProduct(CandidatePath);
        internal DesktopPhysicsScene Scene { get; }
        internal HostFrameScheduler Scheduler { get; }

        internal Rig()
        {
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            Runtime.Start();
            Runtime.SetUnboundedHorizontalPreview(true);
            Runtime.SetViewport(new(0, 0, 2160, 1280));
            Scene = Field<DesktopPhysicsScene>(Runtime, "scene");
            world = Field<DangoPhysicsWorld>(Scene, "world");
            handle = Handle(world); entity = Scene.SelectedEntity;
            Scheduler = new(Runtime, () => true, () => Input, _ => { }, source, () => ticks, TimeSpan.TicksPerSecond);
            Scheduler.Start();
        }

        internal void Advance(int count = 1, bool assertTotalPresenterSteps = true)
        {
            for (var index = 0; index < count; index++)
            {
                var before = Scheduler.Snapshot.SuccessfulSteps;
                ticks += Step.Ticks; source.Raise(ticks);
                Assert.IsNull(Runtime.LastError);
                Assert.IsTrue(Runtime.LastPhysicsFrame!.Succeeded);
                Assert.AreEqual(before + 1, Scheduler.Snapshot.SuccessfulSteps, "Each rendering event owns exactly one body step.");
                if (assertTotalPresenterSteps)
                    Assert.AreEqual(Scheduler.Snapshot.SuccessfulSteps,
                        (long)Runtime.LastFrame!.Bodies.Single().Diagnostics.SimulatedStepCount,
                        "Changing preview scale must neither add a body step nor reset presentation history.");
                AssertIdentity();
            }
        }

        internal void AssertIdentity()
        {
            Assert.AreSame(Scene, Field<DesktopPhysicsScene>(Runtime, "scene"));
            Assert.AreSame(world, Field<DangoPhysicsWorld>(Scene, "world"));
            Assert.AreEqual(handle, Handle(world));
            Assert.AreEqual(entity, Runtime.SelectedEntity);
            Assert.AreEqual(entity, Scene.SelectedEntity);
            Assert.AreSame(Scheduler, Runtime.FrameDriver);
            Assert.IsTrue(Scheduler.IsRunning && Scheduler.OwnsRuntime);
        }

        public void Dispose() { Scheduler.Stop(); Runtime.Dispose(); }
    }

    private sealed class NativeScaleCall : IDisposable
    {
        private readonly nint library;
        private readonly ScaleDelegate setScale;
        internal NativeScaleCall(string path)
        {
            library = NativeLibrary.Load(Path.GetFullPath(path));
            try
            {
                setScale = Marshal.GetDelegateForFunctionPointer<ScaleDelegate>(
                    NativeLibrary.GetExport(library, "dango_physics_world_set_preview_jump_speed_scale"));
            }
            catch { NativeLibrary.Free(library); throw; }
        }
        internal int Invoke(nint handle, float scale) => setScale(handle, scale);
        public void Dispose() => NativeLibrary.Free(library);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ScaleDelegate(nint handle, float scale);
    }
}
