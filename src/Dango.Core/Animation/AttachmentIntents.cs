namespace Dango.Core.Animation;

public enum EyeSelection { Left = 1, Right = 2, Both = 3 }
public enum EyeShape { Round, Soft, NaturalClosed, Tight, Happy, Sleepy, Deadpan, Angry, Surprised, Puzzled }
public enum HatBounceDirection { Left, Right, Down, Up }
[Flags]
public enum AttachmentChannel
{
    None = 0, LeftGaze = 1, RightGaze = 2, LeftEyePose = 4, RightEyePose = 8,
    LeftBlink = 16, RightBlink = 32, HatSlide = 64, HatPulse = 128, HatBounce = 256,
    All = 511,
}
public enum AttachmentRequestStatus { Accepted, Rejected }
public enum AttachmentRejection { None, InvalidParameter, InvalidTarget, InvalidToken, OldSequence, Busy, Capacity, Unsupported, Stopped }
public enum AttachmentEventStatus { Active, Completed, Cancelled }
public readonly record struct AttachmentToken(Guid Value);
public readonly record struct AttachmentRequestResult(long Sequence, AttachmentRequestStatus Status, AttachmentRejection Reason)
{
    public bool Accepted => Status == AttachmentRequestStatus.Accepted;
}

public abstract record AttachmentIntent
{
    public abstract AttachmentChannel Channels { get; }
    public abstract bool IsValid { get; }
    public virtual bool IsEvent => false;
    protected static bool Unit(double value, double min = 0) => double.IsFinite(value) && value >= min && value <= 1;
    protected static bool ValidEye(EyeSelection eye) => Enum.IsDefined(eye);
    protected static AttachmentChannel Eyes(EyeSelection eye, AttachmentChannel left, AttachmentChannel right) =>
        (eye.HasFlag(EyeSelection.Left) ? left : 0) | (eye.HasFlag(EyeSelection.Right) ? right : 0);
}
public sealed record SetGaze(EyeSelection Eye, double X, double Y) : AttachmentIntent
{
    public override AttachmentChannel Channels => Eyes(Eye, AttachmentChannel.LeftGaze, AttachmentChannel.RightGaze);
    public override bool IsValid => ValidEye(Eye) && Unit(X, -1) && Unit(Y, -1);
}
public sealed record SetEyes(EyeSelection Eye, double Openness, EyeShape Shape = EyeShape.Round, double Strength = 0) : AttachmentIntent
{
    public override AttachmentChannel Channels => Eyes(Eye, AttachmentChannel.LeftEyePose, AttachmentChannel.RightEyePose);
    public override bool IsValid => ValidEye(Eye) && Unit(Openness) && Enum.IsDefined(Shape) && Unit(Strength);
}
public sealed record Blink(EyeSelection Eye, TimeSpan Close, TimeSpan Hold, TimeSpan Open) : AttachmentIntent
{
    public static Blink Default(EyeSelection eye) => new(eye, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(80));
    public override AttachmentChannel Channels => Eyes(Eye, AttachmentChannel.LeftBlink, AttachmentChannel.RightBlink);
    public override bool IsValid => ValidEye(Eye) && new[] { Close, Hold, Open }.All(t => t > TimeSpan.Zero && t <= TimeSpan.FromSeconds(2));
    public override bool IsEvent => true;
}
public sealed record SetHatSlide(double Offset) : AttachmentIntent
{
    public override AttachmentChannel Channels => AttachmentChannel.HatSlide;
    public override bool IsValid => Unit(Offset, -1);
}
public sealed record PulseHat(double VelocityDelta) : AttachmentIntent
{
    public override AttachmentChannel Channels => AttachmentChannel.HatPulse;
    public override bool IsValid => double.IsFinite(VelocityDelta) && Math.Abs(VelocityDelta) <= 0.3;
    public override bool IsEvent => true;
}
public sealed record BounceHat(HatBounceDirection Direction, double Strength) : AttachmentIntent
{
    public override AttachmentChannel Channels => AttachmentChannel.HatBounce;
    public override bool IsValid => Enum.IsDefined(Direction) && Unit(Strength);
    public override bool IsEvent => true;
}
public sealed record Release(AttachmentChannel Mask) : AttachmentIntent
{
    public override AttachmentChannel Channels => Mask;
    public override bool IsValid => Mask != AttachmentChannel.None && (Mask & ~AttachmentChannel.All) == 0;
}
