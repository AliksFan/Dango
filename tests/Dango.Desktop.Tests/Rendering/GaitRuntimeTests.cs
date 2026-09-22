using System.Collections;
using System.Reflection;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class GaitRuntimeTests
{
    private static readonly TimeSpan StepTime = TimeSpan.FromMilliseconds(20);
    internal static readonly PhysicsPresentationInput Input = new("gait-fictional", new(400, 350, 160, 120),
        DangoActionState.Idle, 0, 1, "ground", new(0, 1), 0, 1, false, true, []);
    internal static StackedDangoSceneRuntime Ready()
    {
        var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.Start();
        runtime.SetViewport(new(0, 0, 1600, 900));
        var owner = new object();
        runtime.AttachQuietIdleHost(owner, true);
        runtime.UpdateWanderScreenWidth(owner, 1600);
        runtime.Wander.Next = (minimum, _) => minimum;
        for (var i = 0; i < 150; i++) runtime.Tick(Input, TimeSpan.FromSeconds(.02));
        return runtime;
    }

    [TestMethod]
    public void KeyboardOnEveryStepMustProduceRealGaitInBothDirections()
    {
        foreach (var direction in new[] { -1, 1 })
        {
            using var runtime = Ready();
            var strains = new List<double>();
            for (var i = 0; i < 240; i++)
            {
                runtime.SetDirectionalControl(new(direction < 0, direction > 0, false, false));
                var frame = runtime.Tick(Input, TimeSpan.FromSeconds(.02));
                strains.Add((frame.Gait?.ScaleY ?? 1) - 1);
            }
            Assert.IsTrue(strains.Min() < -.003, "The native Product path must actually show gait.");
            Assert.IsTrue(strains.Max() > .002);
        }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    public void KeyboardGaitLeavesEveryNativeSnapshotAndOriginalPresentationUnchanged(int direction)
    {
        using var pair = new RuntimePair(direction > 0);
        var minimum = 0d;
        var maximum = 0d;
        var start = pair.Runtime.LastPhysicsFrame!.Bodies[0].CoreX;
        for (var i = 0; i < 240; i++)
        {
            pair.Step(Horizontal(direction));
            ObserveStrain(pair.Runtime, ref minimum, ref maximum);
        }
        Assert.IsTrue(direction * (pair.Runtime.LastPhysicsFrame!.Bodies[0].CoreX - start) * 80 > 120,
            "The comparison must exercise actual native displacement, not a stationary fixture.");
        AssertBothPhases(minimum, maximum);
        var previous = Math.Abs(Strain(pair.Runtime));
        var phase = pair.Runtime.Gait.Phase;
        for (var i = 0; i < 18; i++)
        {
            pair.Step(DirectionalControlIntent.None);
            Assert.IsTrue(Math.Abs(Strain(pair.Runtime)) <= previous + 1e-10,
                "Releasing the key must recover the displayed strain instead of playing another half-step.");
            Assert.AreEqual(phase, pair.Runtime.Gait.Phase, 1e-12);
            previous = Math.Abs(Strain(pair.Runtime));
        }
        AssertIdentity(pair.Runtime);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AutonomousGaitPreservesRandomDrawsDriveBoundariesAndCompleteNativeState(bool right)
    {
        using var pair = new RuntimePair(right);
        var started = false;
        var minimum = 0d;
        var maximum = 0d;
        double? start = null;
        for (var i = 0; i < 3300; i++)
        {
            pair.Step(DirectionalControlIntent.None);
            if (pair.Runtime.Wander.Driving && !started)
            {
                started = true;
                start = pair.Runtime.LastPhysicsFrame!.Bodies[0].CoreX;
                AssertIdentity(pair.Runtime);
            }
            ObserveStrain(pair.Runtime, ref minimum, ref maximum);
            if (started && !pair.Runtime.Wander.Active) break;
        }
        Assert.IsTrue(started, "A real autonomous drive must have started.");
        Assert.IsFalse(pair.Runtime.Wander.Active, "The autonomous action must actually finish.");
        AssertBothPhases(minimum, maximum);
        var distance = (right ? 1 : -1) * (pair.Runtime.LastPhysicsFrame!.Bodies[0].CoreX - start!.Value) * 80;
        Assert.IsTrue(distance is >= 160 and <= 320, $"Real autonomous distance was {distance} DIP.");
        AssertIdentity(pair.Runtime);
        Assert.IsTrue(pair.RuntimeDraws.Any(draw => draw.StartsWith("wander:", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NearEdgeAutonomousGaitCannotChangeTheOriginalSafetyDecision(bool right)
    {
        using var pair = new RuntimePair(right);
        var initial = WanderVisibleBounds.Get(pair.Runtime.LastFrame!.Bodies[0],
            DangoHostFrameBinding.Bounds(pair.Runtime.LastFrame, 0), pair.Runtime.DisplayHatSize)!.Value;
        var view = new LogicalRect(right ? initial.Right + 16 + 220 - 1600 : initial.X - 16 - 220,
            0, 1600, 900);
        pair.Runtime.SetViewport(view);
        pair.Reference.SetViewport(view);
        var started = false;
        var observedGait = false;
        for (var i = 0; i < 3300; i++)
        {
            pair.Step(DirectionalControlIntent.None);
            started |= pair.Runtime.Wander.Driving;
            observedGait |= Math.Abs(Strain(pair.Runtime)) > .003;
            var frame = pair.Runtime.LastFrame!;
            var visible = WanderVisibleBounds.Get(frame.Bodies[0], DangoHostFrameBinding.Bounds(frame, 0),
                pair.Runtime.DisplayHatSize)!.Value;
            Assert.IsTrue(visible.X >= view.X + 16 && visible.Right <= view.Right - 16,
                "The unmodified autonomous safety hull must stay inside the accepted reserve.");
            if (started && !pair.Runtime.Wander.Active) break;
        }
        Assert.IsTrue(started);
        Assert.IsTrue(observedGait, "The near-edge comparison must include actual non-identity gait.");
        Assert.IsFalse(pair.Runtime.Wander.Active);
    }

    [TestMethod]
    public void CriticalEdgeDecisionReadsOriginalBoundsEvenWithAVisibleCompressedPose()
    {
        using var pair = new RuntimePair(false);
        for (var i = 0; i < 3300 && !(pair.Runtime.Wander.Driving && Strain(pair.Runtime) < -.005); i++)
            pair.Step(DirectionalControlIntent.None);
        Assert.IsTrue(pair.Runtime.Wander.Driving);
        Assert.IsTrue(Strain(pair.Runtime) < -.005);
        var visible = WanderVisibleBounds.Get(pair.Runtime.LastFrame!.Bodies[0],
            DangoHostFrameBinding.Bounds(pair.Runtime.LastFrame, 0), 1.35)!.Value;
        var view = new LogicalRect(visible.X - 16 - 32.1, 0, 1600, 900);
        // A surgical decision fixture: keep the same already-published native frame and
        // drive ownership, then place the safety threshold .1 DIP outside its original hull.
        // Public resize intentionally cancels ownership, so it cannot exercise this branch.
        foreach (var runtime in new[] { pair.Runtime, pair.Reference })
        {
            typeof(StackedDangoSceneRuntime).GetField("viewport", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(runtime, view);
            typeof(StackedDangoSceneRuntime).GetMethod("AdvanceWander", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(runtime, [.02]);
        }
        Assert.IsTrue(pair.Reference.Wander.Driving, "The unchanged hull is still outside the 32 DIP stop threshold.");
        pair.AssertEquivalent();
    }

    [TestMethod]
    public void ReviewMultipleBodiesAndSleepStayNeutralAndSingleProductCanReenter()
    {
        using var review = StackedDangoSceneRuntime.CreateEyeHatReview();
        review.Start(); review.SetViewport(new(0, 0, 1600, 900));
        for (var i = 0; i < 200; i++) { review.SetDirectionalControl(Horizontal(1)); review.Tick(Input, StepTime); AssertIdentity(review); }
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        pair.Runtime.AddDango(); pair.Reference.AddDango();
        pair.Step(Horizontal(1), 40);
        AssertIdentity(pair.Runtime);
        pair.Runtime.SelectEntity(pair.Runtime.EntityIds[1]); pair.Reference.SelectEntity(pair.Reference.EntityIds[1]);
        pair.Runtime.RemoveDango(); pair.Reference.RemoveDango();
        pair.Step(DirectionalControlIntent.None, 150);
        pair.WalkToVisibleGait();
        pair.Step(DirectionalControlIntent.None, 150);
        foreach (var runtime in new[] { pair.Runtime, pair.Reference })
        {
            runtime.Doze.Reset();
            runtime.Doze.Next = (min, _) => min;
            var physical = runtime.LastPhysicsFrame!.Bodies[0];
            for (var i = 0; i < 3000; i++) runtime.Doze.Advance(new(true, physical.CoreX, physical.CoreY), .1, true, true, _ => true);
            Assert.IsTrue(runtime.Doze.Due);
        }
        pair.Step(DirectionalControlIntent.None, 50);
        Assert.IsTrue(pair.Runtime.Doze.Sleeping);
        Assert.IsNotNull(pair.Runtime.LastFrame!.Bodies[0].DozeSeconds);
        AssertIdentity(pair.Runtime);
        pair.WalkToVisibleGait();
        Assert.IsFalse(pair.Runtime.Doze.Active);
    }

    [TestMethod]
    public void JumpSquashDragAndUnownedThrowSuppressGaitWithoutChangingNativePhysics()
    {
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        for (var i = 0; i < 20; i++)
        {
            pair.Step(new(false, true, true, false));
            AssertIdentity(pair.Runtime);
        }
        Assert.IsFalse(pair.Runtime.LastPhysicsFrame!.Bodies[0].IsGrounded,
            "The jump branch must have exercised real airborne physics.");
        pair.Step(DirectionalControlIntent.None, 150);
        pair.WalkToVisibleGait();
        for (var i = 0; i < 20; i++)
        {
            pair.Step(new(false, true, false, true));
            AssertIdentity(pair.Runtime);
        }
        pair.Step(DirectionalControlIntent.None, 150);
        pair.WalkToVisibleGait();
        var point = DangoHostFrameBinding.Bounds(pair.Runtime.LastFrame!, 0).Center;
        pair.Runtime.BeginPhysicalDrag(point);
        pair.Reference.BeginPhysicalDrag(point);
        for (var i = 0; i < 15; i++)
        {
            var target = new LogicalPoint(point.X + i * 3, point.Y - i * 4);
            pair.Runtime.MovePhysicalDrag(target);
            pair.Reference.MovePhysicalDrag(target);
            pair.Step(DirectionalControlIntent.None);
            AssertIdentity(pair.Runtime);
        }
        Assert.IsTrue(pair.Runtime.IsDragging);
        pair.Runtime.EndPhysicalDrag();
        pair.Reference.EndPhysicalDrag();
        for (var i = 0; i < 40; i++)
        {
            pair.Step(DirectionalControlIntent.None);
            AssertIdentity(pair.Runtime);
        }
    }

    [TestMethod]
    public void PressAndVisibleInteractionDisableReleaseGaitWhileTheWorldKeepsAdvancing()
    {
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        var target = pair.Runtime.GetLightInteractionTarget(pair.Runtime.EntityIds[0])!.Value;
        var referenceTarget = pair.Reference.GetLightInteractionTarget(pair.Reference.EntityIds[0])!.Value;
        var press = pair.Runtime.BeginLightPress(target);
        var referencePress = pair.Reference.BeginLightPress(referenceTarget);
        pair.Step(DirectionalControlIntent.None, 7);
        AssertIdentity(pair.Runtime);
        pair.Runtime.EndLightPress(target, press);
        pair.Reference.EndLightPress(referenceTarget, referencePress);
        pair.Step(DirectionalControlIntent.None, 100);
        pair.WalkToVisibleGait();
        var physicalBefore = pair.Runtime.LastPhysicsFrame;
        pair.Runtime.UpdateQuietIdleHost(pair.RuntimeOwner, false);
        pair.Reference.UpdateQuietIdleHost(pair.ReferenceOwner, false);
        pair.Step(DirectionalControlIntent.None, 18);
        AssertIdentity(pair.Runtime);
        Assert.AreNotSame(physicalBefore, pair.Runtime.LastPhysicsFrame,
            "Disabling interaction may stop gait, but must not pause the native world.");
        Assert.IsTrue(pair.Runtime.IsRunning);
        pair.Runtime.UpdateQuietIdleHost(pair.RuntimeOwner, true);
        pair.Reference.UpdateQuietIdleHost(pair.ReferenceOwner, true);
        pair.Step(DirectionalControlIntent.None, 100);
        pair.WalkToVisibleGait();
    }

    [TestMethod]
    public void StopResumeAndCoordinateChangePreserveThePublishedPoseWithoutHiddenDistance()
    {
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        var saved = pair.Runtime.LastFrame!;
        var phase = pair.Runtime.Gait.Phase;
        pair.Runtime.Stop();
        pair.Reference.Stop();
        Assert.AreSame(saved, pair.Runtime.LastFrame);
        Assert.AreEqual(phase, pair.Runtime.Gait.Phase);
        Assert.IsTrue(saved.Gait!.Matches(saved.Bodies[0], saved.EntityIds[0]),
            "Sampling invalidation must not invalidate the descriptor belonging to a preserved safe frame.");
        pair.ResumeWithNewOwners();
        Assert.AreSame(saved, pair.Runtime.LastFrame);
        pair.Step(DirectionalControlIntent.None);
        Assert.AreEqual(phase, pair.Runtime.Gait.Phase, 1e-12,
            "The first resumed sample may not turn the hidden interval into walked distance.");
        pair.Step(DirectionalControlIntent.None, 18);
        AssertIdentity(pair.Runtime);
        pair.WalkToVisibleGait();
        var beforeMovePhase = pair.Runtime.Gait.Phase;
        var movedInput = new PhysicsPresentationInput(Input.DangoId,
            Input.AuthoritativeBounds.Translate(new(50, 20)), Input.Action,
            Input.AuthoritativeStartTimeMilliseconds, Input.DeterministicSeed, Input.SemanticAnchor,
            Input.ContactNormal, Input.Strength, Input.ParameterVersion, Input.ReducedMotion,
            Input.IsHostAvailable, Input.ParticipantIds, Input.SquashStrength, Input.GroundFrictionCue,
            Input.FacingDirection, Input.DragGrabPoint);
        pair.Step(DirectionalControlIntent.None, input: movedInput);
        Assert.AreEqual(beforeMovePhase, pair.Runtime.Gait.Phase, 1e-12,
            "Host-coordinate translation must invalidate sampling rather than adding gait distance.");
    }

    [TestMethod]
    public void GaitFaultDoesNotInterruptPhysicsAndPopulationChangesCannotClearItsLatch()
    {
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        var beforeFault = pair.Runtime.FrameFaultState;
        pair.Runtime.Gait.Fail();
        Assert.IsFalse(StackedDangoSceneRuntime.HasNewFrameFault(beforeFault, pair.Runtime.FrameFaultState),
            "A gait-only fault must not be added to the native batch fault channel.");
        var result = new HostFrameLoop(pair.Runtime).Advance(4, Input, () => true);
        var referenceResult = new HostFrameLoop(pair.Reference).Advance(4, Input, () => true);
        Assert.AreEqual(4, result.SuccessfulSteps);
        Assert.AreEqual(referenceResult.SuccessfulSteps, result.SuccessfulSteps);
        Assert.IsFalse(result.NewFault);
        AssertIdentity(pair.Runtime);
        pair.AssertEquivalent();
        Assert.IsTrue(pair.Runtime.AddDango());
        Assert.IsTrue(pair.Reference.AddDango());
        pair.Step(DirectionalControlIntent.None, 2);
        pair.Runtime.SelectEntity(pair.Runtime.EntityIds[1]);
        pair.Reference.SelectEntity(pair.Reference.EntityIds[1]);
        Assert.IsTrue(pair.Runtime.RemoveDango());
        Assert.IsTrue(pair.Reference.RemoveDango());
        pair.Step(DirectionalControlIntent.None, 150);
        Assert.IsNotNull(pair.Runtime.GaitError, "Only explicit Regenerate may clear the gait fault latch.");
        for (var i = 0; i < 60; i++)
        {
            pair.Step(Horizontal(1));
            AssertIdentity(pair.Runtime);
        }
        pair.Runtime.RegenerateDango();
        pair.Reference.RegenerateDango();
        pair.Step(DirectionalControlIntent.None, 150);
        Assert.IsNull(pair.Runtime.GaitError);
        pair.WalkToVisibleGait();
    }

    [TestMethod]
    public void AttachmentFaultFreezesAttachmentsButKeepsNativeMovementAndRemovesAllGait()
    {
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        var attachment = pair.Runtime.LastFrame!.Bodies[0].Attachments;
        var start = pair.Runtime.LastPhysicsFrame!.Bodies[0].CoreX;
        SetAttachmentError(pair.Runtime);
        SetAttachmentError(pair.Reference);
        for (var i = 0; i < 20; i++)
        {
            pair.Step(Horizontal(1));
            AssertIdentity(pair.Runtime);
            Assert.AreSame(attachment, pair.Runtime.LastFrame!.Bodies[0].Attachments);
        }
        Assert.IsNull(pair.Runtime.LastError);
        Assert.IsNotNull(pair.Runtime.GaitError);
        Assert.IsTrue(pair.Runtime.LastPhysicsFrame!.Bodies[0].CoreX > start,
            "An attachment fault must not silently stop the original native movement.");
    }

    [TestMethod]
    public void ExistingPhysicalFaultLatchPreservesTheCompleteLastGaitFrame()
    {
        using var pair = new RuntimePair();
        pair.WalkToVisibleGait();
        var saved = pair.Runtime.LastFrame!;
        var physical = pair.Runtime.LastPhysicsFrame;
        foreach (var runtime in new[] { pair.Runtime, pair.Reference })
            typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!
                .SetValue(runtime, "fictional physical fault latch");
        pair.Runtime.CancelInput();
        pair.Reference.CancelInput();
        pair.Step(DirectionalControlIntent.None, 10, expectHealthy: false);
        Assert.AreSame(saved, pair.Runtime.LastFrame);
        Assert.AreSame(physical, pair.Runtime.LastPhysicsFrame);
        Assert.IsTrue(saved.Gait!.Matches(saved.Bodies[0], saved.EntityIds[0]));
    }

    private static void SetAttachmentError(StackedDangoSceneRuntime runtime)
    {
        var controller = typeof(StackedDangoSceneRuntime).GetField("attachments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var entries = (IDictionary)controller.GetType().GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        entries[runtime.EntityIds[0]]!.GetType().GetField("Error")!
            .SetValue(entries[runtime.EntityIds[0]], "fictional attachment fault");
    }

    private static DirectionalControlIntent Horizontal(int direction) => new(direction < 0, direction > 0, false, false);
    private static double Strain(StackedDangoSceneRuntime runtime) => (runtime.LastFrame?.Gait?.ScaleY ?? 1) - 1;
    private static void AssertIdentity(StackedDangoSceneRuntime runtime) =>
        Assert.AreEqual(0, Strain(runtime), 1e-10, "No gait deformation may survive this state.");
    private static void ObserveStrain(StackedDangoSceneRuntime runtime, ref double minimum, ref double maximum)
    {
        minimum = Math.Min(minimum, Strain(runtime));
        maximum = Math.Max(maximum, Strain(runtime));
    }
    private static void AssertBothPhases(double minimum, double maximum)
    {
        Assert.IsTrue(minimum < -.003, $"Actual compression is required; minimum strain={minimum}.");
        Assert.IsTrue(maximum > .002, $"Actual extension is required; maximum strain={maximum}.");
    }

    private sealed class RuntimePair : IDisposable
    {
        internal StackedDangoSceneRuntime Runtime { get; } = StackedDangoSceneRuntime.CreateProduct();
        internal StackedDangoSceneRuntime Reference { get; } = StackedDangoSceneRuntime.CreateProduct();
        internal object RuntimeOwner { get; private set; } = new();
        internal object ReferenceOwner { get; private set; } = new();
        internal List<string> RuntimeDraws { get; } = [];
        private readonly List<string> referenceDraws = [];
        private int step;

        internal RuntimePair(bool right = true)
        {
            Reference.GaitEnabled = false;
            Initialize(Runtime, RuntimeOwner, RuntimeDraws, right);
            Initialize(Reference, ReferenceOwner, referenceDraws, right);
            Step(DirectionalControlIntent.None, 150);
        }

        private static void Initialize(StackedDangoSceneRuntime runtime, object owner, List<string> draws, bool right)
        {
            Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
            runtime.Start();
            runtime.SetViewport(new(0, 0, 1600, 900));
            runtime.AttachQuietIdleHost(owner, true);
            runtime.UpdateWanderScreenWidth(owner, 1600);
            runtime.Wander.Next = (minimum, maximum) =>
            {
                var value = minimum == 0 && right ? 1 : minimum;
                draws.Add($"wander:{minimum}:{maximum}:{value}");
                return value;
            };
            runtime.QuietIdle.Next = (minimum, maximum) =>
            {
                var value = minimum == 15000 ? 25000 : minimum == 4000 ? 6000 : minimum;
                draws.Add($"idle:{minimum}:{maximum}:{value}");
                return value;
            };
        }

        internal void Step(DirectionalControlIntent intent, int count = 1, bool expectHealthy = true,
            PhysicsPresentationInput? input = null)
        {
            for (var i = 0; i < count; i++)
            {
                Runtime.SetDirectionalControl(intent);
                Reference.SetDirectionalControl(intent);
                Assert.AreEqual(Reference.Wander.Input(StepTime.TotalSeconds), Runtime.Wander.Input(StepTime.TotalSeconds), 0,
                    $"Consumed autonomous input diverged before step {step}.");
                Runtime.Tick(input ?? Input, StepTime);
                Reference.Tick(input ?? Input, StepTime);
                if (expectHealthy)
                {
                    Assert.IsNull(Runtime.LastError, $"Native error at step {step}: {Runtime.LastError}");
                    Assert.IsNull(Reference.LastError);
                }
                AssertEquivalent();
                step++;
            }
        }

        internal void AssertEquivalent()
        {
            Assert.AreEqual(JsonSerializer.Serialize(Reference.LastPhysicsFrame), JsonSerializer.Serialize(Runtime.LastPhysicsFrame),
                $"Complete native snapshot diverged at step {step}.");
            Assert.AreEqual(JsonSerializer.Serialize(Reference.LastFrame! with { Gait = null }),
                JsonSerializer.Serialize(Runtime.LastFrame! with { Gait = null }),
                $"Gait changed original body, attachment, positioning or safety data at step {step}.");
            Assert.AreEqual(Reference.Wander.Active, Runtime.Wander.Active, $"Autonomous ownership at step {step}.");
            Assert.AreEqual(Reference.Wander.Driving, Runtime.Wander.Driving, $"Drive start/stop at step {step}.");
            Assert.AreEqual(Reference.Wander.Direction, Runtime.Wander.Direction);
            Assert.AreEqual(Reference.Wander.Input(StepTime.TotalSeconds), Runtime.Wander.Input(StepTime.TotalSeconds), 0);
            CollectionAssert.AreEqual(referenceDraws, RuntimeDraws, $"Random call sequence diverged at step {step}.");
            AssertIdentity(Reference);
        }

        internal void WalkToVisibleGait()
        {
            for (var i = 0; i < 240; i++)
            {
                Step(Horizontal(1));
                if (Math.Abs(Strain(Runtime)) > .003) return;
            }
            Assert.Fail("A real non-identity gait pose is required before testing interruption/failure behavior.");
        }

        internal void ResumeWithNewOwners()
        {
            Runtime.Start();
            Reference.Start();
            RuntimeOwner = new();
            ReferenceOwner = new();
            Runtime.AttachQuietIdleHost(RuntimeOwner, true);
            Reference.AttachQuietIdleHost(ReferenceOwner, true);
            Runtime.UpdateWanderScreenWidth(RuntimeOwner, 1600);
            Reference.UpdateWanderScreenWidth(ReferenceOwner, 1600);
        }

        public void Dispose()
        {
            Runtime.Dispose();
            Reference.Dispose();
        }
    }
}
