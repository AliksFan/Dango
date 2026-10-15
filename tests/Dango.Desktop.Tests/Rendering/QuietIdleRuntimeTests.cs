using System.Reflection;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class QuietIdleRuntimeTests
{
    [TestMethod]
    public void BlinkPhaseBoundariesCannotChangeTiltOrHatIntegration()
    {
        using var referenceRouter = new AttachmentCommandRouter(true);
        using var blinkRouter = new AttachmentCommandRouter(true);
        var referenceTarget = referenceRouter.Add(new(1));
        var blinkTarget = blinkRouter.Add(new(1));
        var reference = new AttachmentMotionController(referenceRouter, referenceTarget);
        var blinking = new AttachmentMotionController(blinkRouter, blinkTarget);
        var token = blinkRouter.RegisterPreview(blinkTarget);
        Assert.IsTrue(blinkRouter.Submit(blinkTarget, token, 1, Blink.Default(EyeSelection.Both)).Accepted);
        var blinkSeen = false;
        for (var step = 0; step < 16; step++)
        {
            var template = EyeMotionTests.Body();
            var body = new PhysicsPresentationFrame(true,
                AttachmentShapeGeometryTests.Oval(height: .5, shear: step < 5 ? .04 : -.04),
                template.Eyes, template.Hat, template.FallbackReason, template.Diagnostics);
            var expected = reference.Update(body, step, .02)!;
            var actual = blinking.Update(body, step, .02)!;
            Assert.AreEqual(expected.ShapeTilt.Angle, actual.ShapeTilt.Angle);
            Assert.IsNotNull(actual.Hat!.Pose, "The test must exercise a visible hat.");
            Assert.AreEqual(JsonSerializer.Serialize(expected.Hat), JsonSerializer.Serialize(actual.Hat),
                $"Blink boundaries must not change another channel at step {step}.");
            blinkSeen |= actual.Face.Left.Openness < .5;
        }
        Assert.IsTrue(blinkSeen);
    }

    internal static StackedDangoSceneRuntime Create(out object owner)
    {
        var runtime = LightFeedbackRuntimeTests.Create();
        runtime.QuietIdle.Next = (min, _) => min;
        owner = new();
        runtime.AttachQuietIdleHost(owner, true);
        return runtime;
    }

    internal static void WaitFor(StackedDangoSceneRuntime runtime, QuietIdleAction action)
    {
        for (var i = 0; i < 2000; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1);
            if (runtime.QuietIdle.ActiveFor(runtime.EntityIds[0]) == action) return;
        }
        Assert.Fail($"Real resting runtime never started {action}.");
    }

    [TestMethod]
    public void DefaultProductHostBlinksBothEyesWithoutChangingPhysics()
    {
        using var runtime = Create(out _);
        using var reference = LightFeedbackRuntimeTests.Create();
        var blinkSeen = false;
        var lookSeen = false;
        for (var i = 0; i < 950; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1); LightFeedbackRuntimeTests.Steps(reference, 1);
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastPhysicsFrame), JsonSerializer.Serialize(runtime.LastPhysicsFrame));
            Equivalent(JsonSerializer.SerializeToElement(reference.LastFrame!.Bodies[0].Attachments!.Hat),
                JsonSerializer.SerializeToElement(runtime.LastFrame!.Bodies[0].Attachments!.Hat));
            var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
            Assert.AreEqual(face.Left.Openness, face.Right.Openness, 1e-8);
            blinkSeen |= face.Left.Openness < .5;
            lookSeen |= runtime.QuietIdle.ActiveFor(runtime.EntityIds[0]) == QuietIdleAction.LookLeft;
        }
        Assert.IsTrue(blinkSeen);
        Assert.IsTrue(lookSeen);
    }

    [TestMethod]
    public void RealLookReturnsWithoutASecondInputAndDisableCancels()
    {
        using var runtime = Create(out var owner);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        LightFeedbackRuntimeTests.Steps(runtime, 14);
        var left = runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Center.X;
        LightFeedbackRuntimeTests.Steps(runtime, 36 + 15);
        var center = runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Center.X;
        Assert.IsGreaterThan(1.8, (center - left) * 160);
        Assert.IsLessThan(2.2, (center - left) * 160);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        runtime.UpdateQuietIdleHost(owner, false);
        Assert.IsFalse(runtime.HasQuietIdleAction);
        LightFeedbackRuntimeTests.Steps(runtime, 800);
        Assert.IsFalse(runtime.HasQuietIdleAction);
    }

    [TestMethod]
    public void ManualDownOwnsTheWholePressAndDirectClickPreemptsAutoBlink()
    {
        using var runtime = Create(out _);
        WaitFor(runtime, QuietIdleAction.Blink);
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value, false));
        Assert.IsFalse(runtime.HasQuietIdleAction);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        var session = new LightInteractionSession(runtime);
        var point = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0).Center;
        Assert.IsTrue(session.TryBegin(runtime.EntityIds[0], point, 0, true, () => true, () => { }, () => true));
        Assert.IsFalse(runtime.HasQuietIdleAction);
        LightFeedbackRuntimeTests.Steps(runtime, 1600);
        Assert.IsFalse(runtime.HasQuietIdleAction);
        session.Cancel();
        WaitFor(runtime, QuietIdleAction.Blink);
    }

    [TestMethod]
    [DataRow(10, false)]
    [DataRow(20, true)]
    [DataRow(40, false)]
    public void LookTimingAndTwoDipOutputFollowPhysicsSteps(int milliseconds, bool right)
    {
        using var runtime = Create(out _);
        using var reference = LightFeedbackRuntimeTests.Create();
        runtime.QuietIdle.Next = (min, _) => min == 0 && right ? 1 : min;
        var input = new PhysicsPresentationInput("light-fixture", new LogicalRect(400, 500, 160, 120),
            DangoActionState.Idle, 1000, 17, "fixture-ground", new(0, -1), 0, 1, false, true, []);
        void Step()
        {
            runtime.Tick(input, TimeSpan.FromMilliseconds(milliseconds));
            reference.Tick(input, TimeSpan.FromMilliseconds(milliseconds));
        }
        var action = right ? QuietIdleAction.LookRight : QuietIdleAction.LookLeft;
        for (var i = 0; i < 40000 / milliseconds && runtime.QuietIdle.ActiveFor(runtime.EntityIds[0]) != action; i++) Step();
        Assert.AreEqual(action, runtime.QuietIdle.ActiveFor(runtime.EntityIds[0]));
        var elapsed = milliseconds;
        while (elapsed < 300) { Step(); elapsed += milliseconds; }
        var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        var baseline = reference.LastFrame!.Bodies[0].Attachments!.Face;
        foreach (var delta in new[] { face.Left.Center.X - baseline.Left.Center.X, face.Right.Center.X - baseline.Right.Center.X })
            Assert.IsTrue(delta * 160 * (right ? 1 : -1) is > 1.8 and < 2.2);
        while (elapsed < 1000) { Step(); elapsed += milliseconds; }
        Assert.AreEqual(action, runtime.QuietIdle.ActiveFor(runtime.EntityIds[0]));
        Step(); elapsed += milliseconds;
        Assert.IsFalse(runtime.HasQuietIdleAction);
        while (elapsed < 1300) { Step(); elapsed += milliseconds; }
        face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        baseline = reference.LastFrame!.Bodies[0].Attachments!.Face;
        Assert.IsLessThan(.2, Math.Abs(face.Left.Center.X - baseline.Left.Center.X) * 160);
        Assert.IsLessThan(.2, Math.Abs(face.Right.Center.X - baseline.Right.Center.X) * 160);
    }

    [TestMethod]
    public void StopAndOldHostCallbacksCannotResumeAHiddenPlan()
    {
        using var runtime = Create(out var oldHost);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        var oldTarget = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        runtime.Stop(); runtime.Start();
        var newHost = new object();
        runtime.AttachQuietIdleHost(newHost, false);
        runtime.UpdateQuietIdleHost(oldHost, true);
        runtime.AttachQuietIdleHost(oldHost, true);
        LightFeedbackRuntimeTests.Steps(runtime, 800);
        Assert.IsFalse(runtime.HasQuietIdleAction);
        Assert.IsFalse(runtime.TryReact(oldTarget, true));
        runtime.UpdateQuietIdleHost(newHost, true);
        LightFeedbackRuntimeTests.Steps(runtime, 200);
        Assert.IsFalse(runtime.HasQuietIdleAction, "Restoration must wait a full new interval.");
        WaitFor(runtime, QuietIdleAction.Blink);
    }

    [TestMethod]
    public void OldPressReleaseCannotUnlockANewerPressAndFailedCaptureDoesNotStick()
    {
        using var runtime = Create(out _);
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        var old = runtime.BeginLightPress(target);
        var current = runtime.BeginLightPress(target);
        runtime.EndLightPress(target, old);
        LightFeedbackRuntimeTests.Steps(runtime, 300);
        Assert.IsFalse(runtime.HasQuietIdleAction);
        runtime.EndLightPress(target, current);
        WaitFor(runtime, QuietIdleAction.Blink);
        var session = new LightInteractionSession(runtime);
        Assert.IsFalse(session.TryBegin(target.Entity, DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0).Center,
            0, false, () => false, () => { }, () => true));
        Assert.IsFalse(runtime.HasQuietIdleAction);
        WaitFor(runtime, QuietIdleAction.Blink);
    }

    [TestMethod]
    public void QuietReleaseCannotRemoveNewManualGazeOrEyePose()
    {
        using var runtime = Create(out _);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        Assert.IsTrue(runtime.SubmitAttachment(new SetGaze(EyeSelection.Both, .8, 0)).Accepted);
        Assert.IsTrue(runtime.SubmitAttachment(new SetEyes(EyeSelection.Both, .35, EyeShape.Soft, 1)).Accepted);
        runtime.CancelInput();
        LightFeedbackRuntimeTests.Steps(runtime, 15);
        var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        Assert.IsTrue(face.Left.Openness is > .3 and < .4);
        Assert.IsTrue(face.Right.Openness is > .3 and < .4);
        var attachments = runtime.LastFrame!.Bodies[0].Attachments!;
        var neutralMotion = new EyeMotionSample(new(0, 0), .35, EyeShape.Soft, 1);
        var neutral = FaceLayoutSolver.Solve(attachments.SourceShape!, neutralMotion, neutralMotion,
            attachments.ShapeTilt.Angle, EyeLayoutParameters.Default, horizontalRange: 7);
        // Raw physical contours can have a nonzero geometric center. Measure the
        // retained manual gaze from that same frame's neutral layout, not X=0.
        var layoutMidpoint = (face.Left.Center.X + face.Right.Center.X - neutral.Left.Center.X - neutral.Right.Center.X) * 80;
        Assert.IsGreaterThan(3.7, layoutMidpoint);
        Assert.IsLessThan(4.3, layoutMidpoint);
    }

    [TestMethod]
    public void FailedReleaseIsVisibleAndRequiresExplicitReset()
    {
        using var runtime = Create(out _);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        for (var i = 0; i < 64; i++) Assert.IsTrue(runtime.SubmitAttachment(new SetHatSlide(0)).Accepted);
        runtime.Stop();
        Assert.IsNotNull(runtime.QuietIdleError);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.Start());
        runtime.RegenerateDango(); runtime.Start();
        Assert.IsNull(runtime.QuietIdleError);
        runtime.AttachQuietIdleHost(new object(), true);
        WaitFor(runtime, QuietIdleAction.Blink);
    }

    [TestMethod]
    public void JumpImmediatelyReleasesGazeAndReturnsToPhysicalVerticalFollowing()
    {
        using var runtime = Create(out _);
        using var reference = LightFeedbackRuntimeTests.Create();
        var steps = 0;
        while (steps++ < 2000 && runtime.QuietIdle.ActiveFor(runtime.EntityIds[0]) != QuietIdleAction.LookLeft)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1); LightFeedbackRuntimeTests.Steps(reference, 1);
        }
        Assert.IsTrue(runtime.HasQuietIdleAction);
        runtime.SetDirectionalControl(new(false, false, true, false));
        reference.SetDirectionalControl(new(false, false, true, false));
        Assert.IsFalse(runtime.HasQuietIdleAction);
        for (var i = 0; i < 20; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1); LightFeedbackRuntimeTests.Steps(reference, 1);
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastPhysicsFrame), JsonSerializer.Serialize(runtime.LastPhysicsFrame));
            // Releasing gaze preserves its spring state, including residual X.
            // A tilted eye line projects that X into screen Y. Compare the local
            // vertical coordinate to check physical Y ownership on the first step.
            static double LocalY(EyePresentationFrame eye) =>
                eye.Center.Y * Math.Cos(eye.Angle) - eye.Center.X * Math.Sin(eye.Angle);
            Assert.AreEqual(reference.LastFrame!.Bodies[0].Attachments!.Face.Left.Angle,
                runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Angle);
            Assert.AreEqual(LocalY(reference.LastFrame!.Bodies[0].Attachments!.Face.Left),
                LocalY(runtime.LastFrame!.Bodies[0].Attachments!.Face.Left), 1e-7);
        }
    }

    [TestMethod]
    public void PhysicalFaultFreezesWithoutSubmittingCleanupAndResetDropsOldPlan()
    {
        using var runtime = Create(out _);
        WaitFor(runtime, QuietIdleAction.LookLeft);
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!.SetValue(runtime, "fictional world fault");
        var before = runtime.LastFrame;
        runtime.CancelInput();
        LightFeedbackRuntimeTests.Steps(runtime, 20);
        Assert.AreSame(before, runtime.LastFrame);
        Assert.IsFalse(runtime.HasQuietIdleAction);
        Assert.IsNull(runtime.QuietIdleError);
        runtime.RegenerateDango();
        LightFeedbackRuntimeTests.Steps(runtime, 200);
        Assert.IsFalse(runtime.HasQuietIdleAction);
    }

    [TestMethod]
    public void ReviewNeverSchedulesQuietBehavior()
    {
        using var runtime = StackedDangoSceneRuntime.CreateEyeHatReview();
        runtime.Start(); runtime.AttachQuietIdleHost(new object(), true);
        runtime.QuietIdle.Next = (_, _) => throw new AssertFailedException("Review drew an idle deadline");
        LightFeedbackRuntimeTests.Steps(runtime, 300);
        Assert.IsFalse(runtime.HasQuietIdleAction);
    }

    private static void Equivalent(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind == JsonValueKind.Object)
            foreach (var property in expected.EnumerateObject()) Equivalent(property.Value, actual.GetProperty(property.Name));
        else if (expected.ValueKind == JsonValueKind.Number) Assert.AreEqual(expected.GetDouble(), actual.GetDouble(), 1e-7);
        else Assert.AreEqual(expected.GetRawText(), actual.GetRawText());
    }
}
