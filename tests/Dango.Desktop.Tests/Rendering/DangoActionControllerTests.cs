using Dango.Core.Animation;
using Dango.Desktop.Rendering.Actions;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DangoActionControllerTests
{
    private static ActionObservation Sample(double x = 0, bool grounded = true, double left = -2, double right = 2, double vx = 0, double vy = 0) =>
        new(x, .3, x, .8, grounded, vx, vy, left, right);

    [TestMethod]
    public void BusyAndInvalidDirectionAreRejectedWithoutReplacingAction()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.Sway, 1, Sample()));
        Assert.IsFalse(c.TryStart(DangoActionKind.Shake, -1, Sample()));
        Assert.AreEqual(DangoActionKind.Sway, c.Current);
        Assert.IsNotNull(c.RejectionReason);
        Assert.IsNull(c.Error);
        Assert.IsFalse(c.Advance(.02, Sample()).Completed);
        Assert.IsTrue(c.Active);
        Assert.IsFalse(c.TryStart(DangoActionKind.Shake, 0, Sample()));
        Assert.IsFalse(c.TryStart(DangoActionKind.Shake, 1, Sample(grounded: false)));
        Assert.IsFalse(c.TryStart((DangoActionKind)999, 1, Sample()));
    }

    [TestMethod]
    public void ShakeAndSwayHaveDifferentDurationsAndMeasuredDragTargets()
    {
        var shake = new DangoActionController();
        Assert.IsTrue(shake.TryStart(DangoActionKind.Shake, 1, Sample()));
        var first = shake.Advance(.1, Sample());
        Assert.IsTrue(first.Motor.DragActive);
        Assert.AreEqual(.9, RunToCompletion(shake, Sample()), .001);

        var sway = new DangoActionController();
        Assert.IsTrue(sway.TryStart(DangoActionKind.Sway, 1, Sample()));
        Assert.AreEqual(2.6, RunToCompletion(sway, Sample()), .001);
    }

    [TestMethod]
    public void ShakeUsesDistinctShortCycleAndEndsWithPointTwoSecondsOfFreeSettling()
    {
        var shake = new DangoActionController();
        var sway = new DangoActionController();
        Assert.IsTrue(shake.TryStart(DangoActionKind.Shake, 1, Sample()));
        Assert.IsTrue(sway.TryStart(DangoActionKind.Sway, 1, Sample()));
        var peakTarget = 0d;
        var firstShakeReversal = 0d;
        var firstSwayReversal = 0d;
        var lastShakeDrag = 0d;
        var priorShake = 0d;
        var priorSway = 0d;
        for (var i = 1; i <= 130; i++)
        {
            var t = i * .02;
            if (shake.Active)
            {
                var output = shake.Advance(.02, Sample());
                if (output.Motor.DragActive)
                {
                    var offset = output.Motor.DragTargetX;
                    peakTarget = Math.Max(peakTarget, Math.Abs(offset));
                    lastShakeDrag = t;
                    if (firstShakeReversal == 0 && priorShake > 0 && offset < 0) firstShakeReversal = t;
                    priorShake = offset;
                }
                else Assert.IsTrue(t >= .7, $"Shake released before its 2.4-cycle drive window at {t:F2}s");
            }
            if (sway.Active)
            {
                var output = sway.Advance(.02, Sample());
                if (output.Motor.DragActive)
                {
                    var offset = output.Motor.DragTargetX;
                    if (firstSwayReversal == 0 && priorSway > 0 && offset < 0) firstSwayReversal = t;
                    priorSway = offset;
                }
            }
        }
        Assert.IsTrue(peakTarget > .12 && peakTarget <= .18, $"Expected a larger target excursion, got {peakTarget:F3}m");
        Assert.IsTrue(firstShakeReversal is > 0 and < .2, $"Shake reversal was {firstShakeReversal:F2}s");
        Assert.IsTrue(firstSwayReversal > .6, $"Sway reversal was {firstSwayReversal:F2}s");
        Assert.AreEqual(.7, lastShakeDrag, .02);
        Assert.IsFalse(shake.Active);
    }

    [TestMethod]
    public void SlowBlinkEmitsOneEventAndReleasesOnlyEyeChannels()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.SlowBlink, 1, Sample()));
        var events = 0;
        ActionOutput last = new(default, [], "");
        for (var i = 0; i < 40 && c.Active; i++)
        {
            last = c.Advance(.02, Sample());
            events += last.Attachments.Count(x => x is Blink);
        }
        Assert.AreEqual(1, events);
        var release = (Release)last.Attachments.Single();
        Assert.AreEqual(AttachmentChannel.LeftGaze | AttachmentChannel.RightGaze |
            AttachmentChannel.LeftEyePose | AttachmentChannel.RightEyePose |
            AttachmentChannel.LeftBlink | AttachmentChannel.RightBlink, release.Mask);
        Assert.IsFalse((release.Mask & AttachmentChannel.HatSlide) != 0);
        Assert.IsFalse(last.Motor.DragActive);
    }

    [TestMethod]
    public void ChargedHopSendsOneJumpAndWaitsForObservedLanding()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.ChargedHop, 1, Sample()));
        var jumps = 0;
        for (var i = 0; i < 30; i++) jumps += c.Advance(.02, Sample()).Motor.JumpPressed ? 1 : 0;
        Assert.AreEqual(1, jumps);
        var air = Sample(grounded: false);
        Assert.IsFalse(c.Advance(.02, air).Completed);
        Assert.IsFalse(c.Advance(.02, Sample()).Completed);
        for (var i = 0; i < 11; i++) Assert.IsFalse(c.Advance(.02, Sample()).Completed);
        Assert.IsTrue(c.Advance(.02, Sample()).Completed);
        Assert.IsFalse(c.Active);
    }

    [TestMethod]
    public void ApproachUsesObservedDisplacementAndRejectsInsufficientBoundaryRoom()
    {
        var c = new DangoActionController();
        Assert.IsFalse(c.TryStart(DangoActionKind.Approach, 1, Sample(x: .5, right: 1.1)));
        Assert.IsTrue(c.TryStart(DangoActionKind.Approach, 1, Sample()));
        var output = c.Advance(.02, Sample());
        Assert.IsTrue(output.Motor.Horizontal > 0 && output.Motor.Horizontal <= .28);
        var braking = c.Advance(.02, Sample(x: .25, vx: .1));
        Assert.IsTrue(braking.Motor.Horizontal >= 0 && braking.Motor.Horizontal <= .28);
        var stopped = c.Advance(.02, Sample(x: .35));
        Assert.AreEqual(0, stopped.Motor.Horizontal);
        Assert.AreEqual("observe-stable", stopped.Phase);
        Assert.IsFalse(stopped.Completed);
        ActionOutput end = stopped;
        for (var i = 0; i < 22 && !end.Completed; i++) end = c.Advance(.02, Sample(x: .35));
        Assert.IsTrue(end.Completed);
        Assert.AreEqual(default, end.Motor);
    }

    [TestMethod]
    public void CancelReleasesEyeOwnershipAndClearsMotorWithoutReleasingHat()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.Squint, 1, Sample()));
        c.Advance(.1, Sample());
        var output = c.Cancel();
        Assert.AreEqual(default, output.Motor);
        var release = (Release)output.Attachments.Single();
        Assert.IsFalse((release.Mask & (AttachmentChannel.HatSlide | AttachmentChannel.HatPulse | AttachmentChannel.HatBounce)) != 0);
        Assert.IsFalse(c.Active);
        var idle = c.Advance(.02, Sample());
        Assert.AreEqual(0, idle.Motor.Horizontal);
        Assert.AreEqual(0, idle.Motor.Stretch);
        Assert.AreEqual(0, idle.Motor.Downward);
        Assert.IsFalse(idle.Motor.DragActive);
    }

    [TestMethod]
    public void RetreatAndRecoilCheckSpaceOnTheirActualReverseSide()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.Retreat, 1, Sample(right: 3, left: -1)));
        c.Cancel();
        Assert.IsFalse(c.TryStart(DangoActionKind.Retreat, 1, Sample(right: 3, left: -.9)));
        Assert.IsTrue(c.TryStart(DangoActionKind.Recoil, 1, Sample(right: 3, left: -.9)));
        c.Cancel();
        Assert.IsFalse(c.TryStart(DangoActionKind.Recoil, 1, Sample(right: 3, left: -.8)));
    }

    [TestMethod]
    public void PeekLooksAlongDirectionThenReturnsAndReleasesEyes()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.Peek, -1, Sample()));
        var first = c.Advance(.2, Sample());
        var look = (SetGaze)first.Attachments.Single();
        Assert.IsTrue(look.X < 0);
        var end = new ActionOutput(default, [], "");
        for (var i = 0; i < 120 && c.Active; i++) end = c.Advance(.02, Sample());
        Assert.IsTrue(end.Completed);
        Assert.IsInstanceOfType<Release>(end.Attachments.Single());
        Assert.AreEqual(default, end.Motor);
    }

    [TestMethod]
    public void LowerHoldsAndReleasesWhileStretchCompressesBeforeExtending()
    {
        var lower = new DangoActionController();
        Assert.IsTrue(lower.TryStart(DangoActionKind.Lower, 1, Sample()));
        lower.Advance(.1, Sample());
        var hold = new ActionOutput(default, [], "");
        for (var i = 0; i < 20; i++) hold = lower.Advance(.02, Sample());
        Assert.AreEqual("hold", hold.Phase);
        Assert.AreEqual(.28, hold.Motor.Downward, .01);
        var settle = new ActionOutput(default, [], "");
        for (var i = 0; i < 60 && lower.Active; i++) settle = lower.Advance(.02, Sample());
        Assert.IsTrue(settle.Completed);
        Assert.AreEqual(default, settle.Motor);

        var stretch = new DangoActionController();
        Assert.IsTrue(stretch.TryStart(DangoActionKind.Stretch, 1, Sample()));
        var compress = stretch.Advance(.1, Sample());
        Assert.IsTrue(compress.Motor.Downward > 0);
        Assert.AreEqual(0, compress.Motor.Stretch);
        var extend = new ActionOutput(default, [], "");
        for (var i = 0; i < 30; i++)
        {
            extend = stretch.Advance(.02, Sample());
            Assert.IsFalse(extend.Motor.Downward > 0 && extend.Motor.Stretch > 0);
        }
        Assert.IsTrue(extend.Motor.Stretch > 0);
    }

    [TestMethod]
    public void EveryNonHopActionEmitsValidAttachmentsAndFiniteNativeInputsForItsFullDuration()
    {
        foreach (var kind in Enum.GetValues<DangoActionKind>().Where(x => x != DangoActionKind.ChargedHop))
        {
            var c = new DangoActionController();
            var x = 0d;
            Assert.IsTrue(c.TryStart(kind, 1, Sample(x, left: -5, right: 5)), $"Could not start {kind}: {c.RejectionReason}");
            for (var i = 0; i < 400 && c.Active; i++)
            {
                var output = c.Advance(.02, Sample(x, left: -5, right: 5));
                foreach (var intent in output.Attachments)
                    Assert.IsTrue(intent.IsValid, $"{kind} emitted invalid attachment at step {i}: {intent}");
                Assert.IsTrue(double.IsFinite(output.Motor.Horizontal) && Math.Abs(output.Motor.Horizontal) <= 1, $"{kind} horizontal input out of range");
                Assert.IsTrue(double.IsFinite(output.Motor.Downward) && Math.Abs(output.Motor.Downward) <= 1, $"{kind} downward input out of range");
                Assert.IsTrue(double.IsFinite(output.Motor.Stretch) && Math.Abs(output.Motor.Stretch) <= 1, $"{kind} stretch input out of range");
                Assert.IsTrue(double.IsFinite(output.Motor.DragTargetX) && double.IsFinite(output.Motor.DragTargetY), $"{kind} emitted non-finite drag target");
                if (kind is DangoActionKind.Approach or DangoActionKind.Retreat)
                    x += output.Motor.Horizontal * .03;
            }
            Assert.IsFalse(c.Active, $"{kind} did not finish its full timeline");
            Assert.IsNull(c.Error, $"{kind} failed during its timeline: {c.Error}");
        }
    }

    [TestMethod]
    public void HopRequiresSlowStableLandingAndCancelCompletesWithDefaultMotor()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.ChargedHop, 1, Sample()));
        var jumps = 0;
        for (var i = 0; i < 30; i++) jumps += c.Advance(.02, Sample()).Motor.JumpPressed ? 1 : 0;
        Assert.AreEqual(1, jumps);
        Assert.IsFalse(c.Advance(.02, Sample(grounded: false)).Completed);
        Assert.IsFalse(c.Advance(.02, Sample(vx: .4)).Completed);
        for (var i = 0; i < 12; i++) Assert.IsFalse(c.Advance(.02, Sample()).Completed);
        Assert.IsTrue(c.Advance(.02, Sample()).Completed);
        Assert.AreEqual(default, c.Cancel().Motor);
    }

    [TestMethod]
    public void MovementThatRunsOutOfSafeRoomFailsInsteadOfPushingAgainstBoundary()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.Approach, 1, Sample(right: 1.1)));
        var output = c.Advance(.02, Sample(right: .65));
        Assert.IsTrue(output.Completed);
        Assert.IsNotNull(c.Error);
        Assert.IsTrue(c.Error!.Contains("安全边界"));
        Assert.AreEqual(default, output.Motor);
    }

    [TestMethod]
    public void HopCannotTimeCompleteWithoutLiftoffAndThenTimesOut()
    {
        var c = new DangoActionController();
        Assert.IsTrue(c.TryStart(DangoActionKind.ChargedHop, -1, Sample()));
        ActionOutput result = new(default, [], "");
        for (var i = 0; i < 300 && c.Active; i++) result = c.Advance(.02, Sample());
        Assert.IsTrue(result.Completed);
        Assert.IsNotNull(c.Error);
        Assert.IsTrue(c.Error!.Contains("未能离地"));
    }

    private static double RunToCompletion(DangoActionController c, ActionObservation observation)
    {
        var elapsed = c.Elapsed;
        while (c.Active && elapsed < 4)
        {
            var output = c.Advance(.02, observation);
            elapsed += .02;
            if (output.Completed) break;
        }
        return elapsed;
    }
}
