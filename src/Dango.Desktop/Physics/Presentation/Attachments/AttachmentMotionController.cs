using Dango.Core.Animation;

namespace Dango.Desktop.Physics.Presentation.Attachments;

/// <summary>Per-entity eye consumer; the owner calls Update only after a successful physics step.</summary>
public sealed class AttachmentMotionController(AttachmentCommandRouter router, AttachmentTarget target, PhysicsPresentationFrame? hatBaseline = null)
{
    private sealed class EyeState
    {
        public SpringState X, Y;
        public SpringState Open = new(1, 0), Soft;
        public SpringState Envelope = new(1, 0);
        public AttachmentChannelIntent? Blink;
        public double BlinkStart;
        public double BlinkInitial = 1;
        public EyeShape Shape;
    }
    private readonly AttachmentStepClock clock = new();
    private readonly EyeState left = new(), right = new();
    private SpringState tilt;
    private double? previousBodyY;
    private readonly HatMotionController? hat = router.HatMotionEnabled ? new(hatBaseline) : null;
    private EyeLayoutParameters eyeLayout = EyeLayoutParameters.Default;
    public AttachmentPresentationFrame? Current { get; private set; }
    /// <summary>Updates only the base request; the existing step owner composes it on the next successful frame.</summary>
    public void SetEyeLayout(EyeLayoutParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        eyeLayout = parameters;
    }
    public bool Paused { get => clock.Paused; set { if (clock.Paused != value) { hat?.ResetSignals(); previousBodyY = null; } clock.Paused = value; } }
    public AttachmentPresentationFrame? Update(PhysicsPresentationFrame body, long stepIndex, double seconds, DangoFacingDirection facing = DangoFacingDirection.Center, HatBodySample? bodyMotion = null, bool hatBodyFollowEnabled = true)
    {
        if (!double.IsFinite(seconds) || seconds is <= 0 or > 0.1 || stepIndex < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Paused || stepIndex <= clock.StepIndex) return Current;
        if (!body.IsVisible || body.FallbackReason != PhysicsPresentationFallbackReason.None) { hat?.ResetSignals(); previousBodyY = null; return Current; }
        if (bodyMotion is { } sample && (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y))) throw new ArgumentOutOfRangeException(nameof(bodyMotion));
        var verticalVelocity = bodyMotion is { } current && previousBodyY is { } previous ? (current.Y - previous) / seconds : 0;
        previousBodyY = bodyMotion?.Y;
        // Physics Y points upward. A small deadzone suppresses resting-body jitter.
        var defaultY = Math.Sign(verticalVelocity) * Math.Clamp((Math.Abs(verticalVelocity) - 0.15) / 3, 0, 1);
        var shape = AttachmentShapeGeometry.Create(body.Contour);
        var signal = shape.EstimateTilt();
        var commands = router.CommitStep(target);
        AttachmentChannelIntent? Command(AttachmentChannel channel) => commands.FirstOrDefault(c => c.Channel == channel);
        hat?.Prepare(shape, body.Hat);
        hat?.Observe(hatBodyFollowEnabled ? bodyMotion : null, shape.Height / shape.Width, seconds);
        if (hat is not null && Command(AttachmentChannel.HatPulse) is { Intent: PulseHat pulse } eventCommand)
        {
            hat.Pulse(pulse.VelocityDelta);
            router.CompleteEvent(target, eventCommand.Token, eventCommand.Sequence, eventCommand.Channel);
        }
        void Synchronize(EyeState eye, AttachmentChannel channel)
        {
            var command = Command(channel);
            if (eye.Blink?.Token == command?.Token && eye.Blink?.Sequence == command?.Sequence) return;
            eye.Blink = command;
            eye.BlinkStart = clock.Elapsed;
            // A cancelled envelope keeps its current position/velocity and springs home.
            if (command is not null) eye.BlinkInitial = eye.Envelope.Position;
        }
        Synchronize(left, AttachmentChannel.LeftBlink); Synchronize(right, AttachmentChannel.RightBlink);
        var boundaries = new List<double>();
        foreach (var eye in new[] { left, right })
            if (eye.Blink?.Intent is Blink blink)
            {
                boundaries.Add(eye.BlinkStart + blink.Close.TotalSeconds);
                boundaries.Add(eye.BlinkStart + blink.Close.TotalSeconds + blink.Hold.TotalSeconds);
                boundaries.Add(eye.BlinkStart + blink.Close.TotalSeconds + blink.Hold.TotalSeconds + blink.Open.TotalSeconds);
            }
        // Blink phase boundaries belong only to the eyes. Keep the body tilt
        // and hat on the same canonical clock slices as a frame with no blink:
        // sampling their changing target at a blink boundary couples channels.
        var motionStart = clock.Elapsed;
        var motionEnd = motionStart + seconds;
        var motionCount = Math.Max(1, (int)Math.Ceiling((motionEnd - motionStart) * 240));
        var previousMotionTime = motionStart;
        for (var i = 1; i <= motionCount; i++)
        {
            var next = i == motionCount ? motionEnd : motionStart + (motionEnd - motionStart) * i / motionCount;
            var motionSeconds = next - previousMotionTime;
            tilt = DampedSpring.AdvanceBounded(tilt, signal.Angle, 4, 0.9, motionSeconds, -Math.PI / 18, Math.PI / 18);
            hat?.Advance(motionSeconds, tilt.Position, (Command(AttachmentChannel.HatSlide)?.Intent as SetHatSlide)?.Offset ?? 0);
            previousMotionTime = next;
        }
        foreach (var slice in clock.Advance(stepIndex, seconds, boundaries))
        {
            AdvanceEye(left, AttachmentChannel.LeftGaze, AttachmentChannel.LeftEyePose, slice);
            AdvanceEye(right, AttachmentChannel.RightGaze, AttachmentChannel.RightEyePose, slice);
        }
        EyeMotionSample Sample(EyeState eye) => new(new(eye.X.Position, eye.Y.Position), eye.Open.Position * eye.Envelope.Position, eye.Shape, eye.Soft.Position);
        var layout = FaceLayoutSolver.Solve(shape, Sample(left), Sample(right), tilt.Position, eyeLayout, horizontalRange: 7);
        if (layout.GazeScale < 1)
        {
            foreach (var eye in new[] { left, right })
            {
                if (eye.X.Position * eye.X.Velocity > 0) eye.X = eye.X with { Velocity = 0 };
                if (eye.Y.Position * eye.Y.Velocity > 0) eye.Y = eye.Y with { Velocity = 0 };
            }
        }
        return Current = new(stepIndex, target.Generation, signal with { Angle = tilt.Position }, layout) { Hat = hat?.Current, SourceShape = shape };

        void AdvanceEye(EyeState eye, AttachmentChannel gazeChannel, AttachmentChannel poseChannel, AttachmentTimeSlice slice)
        {
            var gaze = Command(gazeChannel)?.Intent as SetGaze;
            var pose = Command(poseChannel)?.Intent as SetEyes;
            var isLeft = gazeChannel == AttachmentChannel.LeftGaze;
            var defaultX = facing == DangoFacingDirection.Left ? -(isLeft ? 0.75 : 1) :
                facing == DangoFacingDirection.Right ? (isLeft ? 1 : 0.75) : 0;
            // Keep explicit gaze at its existing 5 DIP range within the 7 DIP facing range.
            eye.X = DampedSpring.AdvanceBounded(eye.X, gaze is null ? defaultX : gaze.X * 5d / 7, gaze is null ? 4.5 : 5,
                gaze is null ? 0.75 : 1, slice.Seconds, -1, 1);
            eye.Y = DampedSpring.AdvanceBounded(eye.Y, gaze?.Y ?? defaultY, gaze is null ? 4.5 : 5,
                gaze is null ? 0.75 : 1, slice.Seconds, -1, 1);
            eye.Open = DampedSpring.AdvanceBounded(eye.Open, pose?.Openness ?? 1, 8, 1, slice.Seconds, 0, 1);
            var softTarget = pose?.Shape == EyeShape.Soft ? pose.Strength : 0;
            eye.Soft = DampedSpring.AdvanceBounded(eye.Soft, softTarget, 8, 1, slice.Seconds, 0, 1);
            eye.Shape = pose?.Shape ?? (eye.Soft.Position > 1e-10 ? EyeShape.Soft : EyeShape.Round);
            if (eye.Blink?.Intent is not Blink blink)
            {
                eye.Envelope = DampedSpring.AdvanceBounded(eye.Envelope, 1, 8, 1, slice.Seconds, 0, 1);
                return;
            }
            var elapsed = slice.End - eye.BlinkStart;
            var close = blink.Close.TotalSeconds; var hold = blink.Hold.TotalSeconds; var open = blink.Open.TotalSeconds;
            if (elapsed < close)
            {
                var t = elapsed / close;
                eye.Envelope = new(eye.BlinkInitial * (1 - Smooth(t)), -eye.BlinkInitial * 6 * t * (1 - t) / close);
            }
            else if (elapsed < close + hold) eye.Envelope = new(0, 0);
            else if (elapsed < close + hold + open - 1e-12)
            {
                var t = (elapsed - close - hold) / open;
                eye.Envelope = new(Smooth(t), 6 * t * (1 - t) / open);
            }
            else
            {
                eye.Envelope = new(1, 0);
                router.CompleteEvent(target, eye.Blink.Token, eye.Blink.Sequence, eye.Blink.Channel);
                eye.Blink = null;
            }
        }
    }
    private static double Smooth(double t) => t * t * (3 - 2 * t);
}
