using Dango.Core.Animation;
using Dango.Physics;

namespace Dango.Desktop.Rendering.Actions;

internal enum DangoActionKind
{
    Shake, Sway, Stretch, Lower, Peek, Lean, Recoil, ChargedHop, SlowBlink, Squint,
    LookAway, LookBack, Approach, Retreat
}

internal readonly record struct ActionObservation(
    double CoreX, double CoreY, double TopX, double TopY, bool Grounded,
    double VelocityX, double VelocityY, double Left, double Right);

internal sealed record ActionOutput(DangoBeltInput Motor, IReadOnlyList<AttachmentIntent> Attachments, string Phase, bool Completed = false);

internal sealed class DangoActionController
{
    internal const double ShakeStrength = .72;
    private const AttachmentChannel EyeChannels = AttachmentChannel.LeftGaze | AttachmentChannel.RightGaze |
        AttachmentChannel.LeftEyePose | AttachmentChannel.RightEyePose | AttachmentChannel.LeftBlink | AttachmentChannel.RightBlink;
    private static readonly ActionOutput Idle = new(default, Array.Empty<AttachmentIntent>(), "idle");
    private DangoActionKind kind;
    private int direction;
    private double elapsed, groundedTime, observeTime, startX, startTopX, shakeStrength;
    private DangoMuscleShakeProfile shakeProfile;
    private bool jumped, leftGround, eyesOwned;

    internal bool Active { get; private set; }
    internal DangoActionKind? Current => Active ? kind : null;
    internal string? Error { get; private set; }
    internal string? RejectionReason { get; private set; }
    internal double Elapsed => elapsed;

    internal bool TryStart(DangoActionKind action, int sign, ActionObservation observation, double shakeStrength = ShakeStrength,
        DangoMuscleShakeProfile shakeProfile = DangoMuscleShakeProfile.Original)
    {
        if (Active) return Reject("当前动作尚未结束。");
        if (!Enum.IsDefined(action)) return Reject("不支持此动作类型。");
        if (sign is not (-1 or 1)) return Reject("动作方向必须是左或右。");
        if (!double.IsFinite(shakeStrength) || shakeStrength is <= 0 or > 2)
            return Reject("抖身驱动力超出支持范围。");
        if (!Enum.IsDefined(shakeProfile)) return Reject("不支持此抖身发力方案。");
        if (action != DangoActionKind.Shake && (shakeStrength != ShakeStrength || shakeProfile != DangoMuscleShakeProfile.Original))
            return Reject("此动作不支持抖身候选参数。");
        if (!Finite(observation) || !observation.Grounded) return Reject("动作需要团子处于稳定落地状态；请稍后重试。");
        if (action is DangoActionKind.Approach or DangoActionKind.Retreat or DangoActionKind.Recoil)
        {
            var moveDirection = action switch
            {
                DangoActionKind.Retreat or DangoActionKind.Recoil => -sign,
                _ => sign
            };
            var travel = action == DangoActionKind.Approach ? .35 : action == DangoActionKind.Retreat ? .25 : .12;
            var room = moveDirection > 0 ? observation.Right - observation.CoreX : observation.CoreX - observation.Left;
            if (room < travel + .7)
                return Reject("移动方向的安全空间不足；请选择另一侧或先调整位置。");
        }
        kind = action;
        direction = sign;
        elapsed = groundedTime = observeTime = 0;
        startX = observation.CoreX;
        startTopX = observation.TopX;
        this.shakeStrength = shakeStrength;
        this.shakeProfile = shakeProfile;
        jumped = leftGround = eyesOwned = false;
        Active = true;
        Error = null;
        RejectionReason = null;
        return true;
    }

    internal ActionOutput Advance(double seconds, ActionObservation observation)
    {
        if (!Active) return Idle;
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > .25 || !Finite(observation))
            return Fail("动作观测或时间步无效；已停止动作。");
        elapsed += seconds;
        if (elapsed >= 6 - 1e-9) return Fail(kind == DangoActionKind.ChargedHop && !leftGround
            ? "蓄力跳跃未能离地；动作已停止。" : "动作超过安全时限；已停止。");

        if (kind == DangoActionKind.ChargedHop) return AdvanceHop(seconds, observation);
        var attachments = new List<AttachmentIntent>();
        var input = default(DangoBeltInput);
        var phase = "enter";
        var doneAt = Duration(kind);
        var settleTail = kind == DangoActionKind.Shake ? .3 : .35;
        var driveEnd = doneAt - settleTail;
        var outRamp = Math.Min(1, Math.Max(0, (driveEnd - elapsed) / .2));
        var envelope = Math.Min(Math.Min(1, elapsed / .25), outRamp);

        switch (kind)
        {
            case DangoActionKind.Shake:
                input = elapsed < driveEnd ? new DangoBeltInput(InputActive: true)
                    { MuscleShakeTime = elapsed, MuscleShakeStrength = shakeStrength, MuscleShakeProfile = shakeProfile } : default;
                phase = elapsed < .1 ? "enter" : elapsed < driveEnd ? "hold" : "settle";
                break;
            case DangoActionKind.Sway:
            case DangoActionKind.Peek:
            case DangoActionKind.Lean:
                var amplitude = kind switch { DangoActionKind.Sway => .10, DangoActionKind.Peek => .10, _ => .12 };
                var offset = kind switch
                {
                    DangoActionKind.Sway => Math.Sin(elapsed * Math.PI * 1.3) * amplitude,
                    _ => direction * amplitude * Smooth(Math.Clamp((elapsed - .25) / .6, 0, 1)) *
                         (elapsed < 1.45 ? 1 : 1 - Smooth(Math.Clamp((elapsed - 1.45) / .6, 0, 1)))
                };
                if (elapsed < driveEnd)
                    input = new(DragActive: true, DragTargetX: startTopX + offset * envelope, DragTargetY: observation.TopY, InputActive: true);
                if (kind == DangoActionKind.Peek)
                {
                    var peekGaze = elapsed < .45 ? .55 * Smooth(elapsed / .45) : elapsed < 1.65 ? .55 : .55 * (1 - Smooth((elapsed - 1.65) / .4));
                    attachments.Add(new SetGaze(EyeSelection.Both, peekGaze * direction, 0));
                    eyesOwned = true;
                }
                phase = elapsed < .25 ? "enter" : elapsed > driveEnd ? "exit" : "hold";
                break;
            case DangoActionKind.Lower:
            case DangoActionKind.Stretch:
                if (elapsed < driveEnd)
                {
                    if (kind == DangoActionKind.Lower)
                    {
                        var down = .28 * (elapsed < .35 ? Smooth(elapsed / .35) : elapsed < .9 ? 1 : 1 - Smooth((elapsed - .9) / .35));
                        input = down > 0 ? new(Downward: down, InputActive: true) : default;
                        phase = elapsed < .35 ? "press" : elapsed < .9 ? "hold" : "release";
                    }
                    else
                    {
                        var down = elapsed < .25 ? .14 * Smooth(elapsed / .25) : elapsed < .5 ? .14 * (1 - Smooth((elapsed - .25) / .25)) : 0;
                        var stretch = elapsed < .5 ? 0 : elapsed < .8 ? .24 * Smooth((elapsed - .5) / .3) : elapsed < 1.1 ? .24 : .24 * (1 - Smooth((elapsed - 1.1) / .35));
                        input = new(Stretch: stretch, Downward: down, InputActive: stretch > 0 || down > 0);
                        phase = elapsed < .25 ? "compress" : elapsed < .5 ? "compress-release" : elapsed < .8 ? "extend" : elapsed < 1.1 ? "hold" : "release";
                    }
                }
                else phase = "settle";
                break;
            case DangoActionKind.Recoil:
                var recoil = Smooth(Math.Clamp(elapsed / .18, 0, 1)) * (1 - Smooth(Math.Clamp((elapsed - .18) / .32, 0, 1)));
                input = new(Stretch: .10 * recoil, Horizontal: -direction * .12 * recoil, InputActive: recoil > 0);
                phase = elapsed < .18 ? "recoil" : elapsed < .5 ? "release" : "observe";
                break;
            case DangoActionKind.Squint:
                var openness = elapsed < .35 ? 1 - .65 * Smooth(elapsed / .35) : elapsed < 1.35 ? .35 : .35 + .65 * Smooth((elapsed - 1.35) / .35);
                attachments.Add(new SetEyes(EyeSelection.Both, openness, EyeShape.NaturalClosed, 1));
                eyesOwned = true;
                phase = elapsed < .35 ? "enter" : elapsed < 1.35 ? "hold" : "exit";
                break;
            case DangoActionKind.SlowBlink:
                if (elapsed <= seconds + 1e-9)
                {
                    attachments.Add(new Blink(EyeSelection.Both, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(260)));
                    eyesOwned = true;
                }
                phase = "blink";
                break;
            case DangoActionKind.LookAway:
            case DangoActionKind.LookBack:
                var gaze = Gaze(elapsed);
                attachments.Add(new SetGaze(EyeSelection.Both, gaze * direction, 0));
                eyesOwned = true;
                phase = elapsed < .55 ? "look-first" : elapsed < 1.35 ? "look-across" : "return";
                break;
            case DangoActionKind.Approach:
            case DangoActionKind.Retreat:
                var moveDir = direction * (kind == DangoActionKind.Retreat ? -1 : 1);
                var targetDistance = kind == DangoActionKind.Approach ? .35 : .25;
                var moved = Math.Max(0, (observation.CoreX - startX) * moveDir);
                var remaining = Math.Max(0, targetDistance - moved);
                var boundaryRoom = moveDir > 0 ? observation.Right - observation.CoreX : observation.CoreX - observation.Left;
                var reached = moved >= targetDistance || remaining < .015;
                if (!reached && boundaryRoom < .7)
                    return Fail("移动已在安全边界前停止；未达到目标距离。");
                var magnitude = Math.Min(.28, Math.Clamp(remaining / .12, 0, .28));
                input = reached ? default : new(Horizontal: moveDir * magnitude, InputActive: true);
                if (!observation.Grounded) return Fail("移动过程中失去接地；已停止。");
                var stable = reached && Math.Abs(observation.VelocityX) < .08 && Math.Abs(observation.VelocityY) < .08;
                observeTime = stable ? observeTime + seconds : 0;
                phase = !reached ? (moved < targetDistance * .7 ? "move" : "brake") : stable ? "observe-stable" : "observe";
                if (observeTime >= .45) return Complete("observed");
                return new(input, Array.Empty<AttachmentIntent>(), phase);
        }

        if (elapsed >= doneAt) return Complete("complete");
        return new(input, attachments, phase);
    }

    internal ActionOutput Cancel()
    {
        var attachments = eyesOwned ? new AttachmentIntent[] { new Release(EyeChannels) } : Array.Empty<AttachmentIntent>();
        Reset();
        return new(default, attachments, "cancelled", true);
    }

    internal ActionOutput Fail(string message)
    {
        Error = message;
        var output = Cancel();
        // Cancel preserves the failure explanation while releasing owned channels.
        Error = message;
        return output;
    }

    private ActionOutput AdvanceHop(double seconds, ActionObservation observation)
    {
        if (!jumped && elapsed >= .45)
        {
            jumped = true;
            return new(new DangoBeltInput(JumpPressed: true, InputActive: true), Array.Empty<AttachmentIntent>(), "launch");
        }
        if (!jumped)
        {
            var charge = .35 * Smooth(Math.Clamp(elapsed / .45, 0, 1));
            return new(new DangoBeltInput(Downward: charge, InputActive: charge > 0), Array.Empty<AttachmentIntent>(), "charge");
        }
        if (!observation.Grounded) leftGround = true;
        var settled = observation.Grounded && Math.Abs(observation.VelocityX) < .08 && Math.Abs(observation.VelocityY) < .08;
        if (leftGround && settled) groundedTime += seconds; else groundedTime = 0;
        if (leftGround && groundedTime >= .25) return Complete("landed");
        if (leftGround) return new(default, Array.Empty<AttachmentIntent>(), settled ? "settle" : observation.Grounded ? "settle-fast" : "airborne");
        return new(default, Array.Empty<AttachmentIntent>(), "await-liftoff");
    }

    private double Gaze(double t)
    {
        if (kind == DangoActionKind.LookAway)
            return t < .55 ? .65 : t < 1.25 ? -.8 : t < 1.8 ? -.8 * (1 - Smooth((t - 1.25) / .55)) : 0;
        return t < .55 ? .65 : t < 1.25 ? .65 - 1.45 * Smooth((t - .55) / .7) : t < 1.8 ? -.8 * (1 - Smooth((t - 1.25) / .55)) : 0;
    }

    private ActionOutput Complete(string phase)
    {
        var attachments = eyesOwned ? new AttachmentIntent[] { new Release(EyeChannels) } : Array.Empty<AttachmentIntent>();
        Reset();
        return new(default, attachments, phase, true);
    }

    private bool Reject(string message) { RejectionReason = message; return false; }
    private void Reset() { Active = false; elapsed = groundedTime = observeTime = 0; jumped = leftGround = eyesOwned = false; }
    private static double Duration(DangoActionKind action) => action switch
    {
        DangoActionKind.Shake => 1.05, DangoActionKind.Sway => 2.6,
        DangoActionKind.Peek or DangoActionKind.Lean => 2.4,
        DangoActionKind.Stretch => 1.8,
        DangoActionKind.Lower => 1.6,
        DangoActionKind.Recoil => .9, DangoActionKind.SlowBlink => .62,
        DangoActionKind.Squint => 1.7, DangoActionKind.LookAway or DangoActionKind.LookBack => 2.15,
        _ => 6
    };
    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
    private static bool Finite(ActionObservation o) => double.IsFinite(o.CoreX) && double.IsFinite(o.CoreY) &&
        double.IsFinite(o.TopX) && double.IsFinite(o.TopY) && double.IsFinite(o.VelocityX) && double.IsFinite(o.VelocityY) &&
        double.IsFinite(o.Left) && double.IsFinite(o.Right) && o.Left <= o.Right && o.CoreX >= o.Left && o.CoreX <= o.Right;
}
