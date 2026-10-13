namespace Dango.Physics;

public readonly record struct DangoRoundBallId(int Value);

/// <summary>Birth position and material in q=diag(4/3,1)p. No DIP or initial-velocity input.</summary>
public sealed record DangoRoundBallDefinition(
    double CenterQX, double CenterQY, double RadiusQ = .4, double Mass = .09,
    double FloorFriction = .45, double DangoFriction = .35,
    double FloorRestitution = .25, double DangoRestitution = .15)
{
    public void Validate()
    {
        if (!double.IsFinite(CenterQX) || Math.Abs(CenterQX) > 1024 ||
            !double.IsFinite(CenterQY) || Math.Abs(CenterQY) > 1024 ||
            !double.IsFinite(RadiusQ) || RadiusQ is < .05 or > 4 ||
            !double.IsFinite(Mass) || Mass is < .001 or > 10 ||
            !double.IsFinite(FloorFriction) || FloorFriction is < 0 or > 1.5 ||
            !double.IsFinite(DangoFriction) || DangoFriction is < 0 or > 1.5 ||
            !double.IsFinite(FloorRestitution) || FloorRestitution is < 0 or > 1 ||
            !double.IsFinite(DangoRestitution) || DangoRestitution is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(CenterQX), "圆球的坐标、尺寸、质量或材料超出允许范围。");
    }
}

public sealed record DangoRoundBallState(
    DangoRoundBallId Id, ulong SceneRevision, ulong Step,
    double CenterQX, double CenterQY, double VelocityQX, double VelocityQY,
    double AngleRadians, double AngularVelocity, bool IsSleeping, ulong IntegrationCount);

public sealed record DangoRoundBallContact(
    int ObjectId, bool Touching, int Sample, ulong SceneRevision, ulong Step,
    double Fraction, double NormalQX, double NormalQY, double GapQ,
    double NormalImpulse, double TangentImpulse,
    double NormalSpeedBefore, double TangentSpeedBefore, double BallSpeedBefore);

/// <summary>One deeply read-only published native frame. Original Dango fields remain in p.</summary>
public sealed record DangoRoundBallFrame(
    ulong SceneRevision, ulong Step, DangoPhysicsFrame Physics,
    DangoRoundBallDefinition? Definition, DangoRoundBallState? Ball,
    DangoRoundBallContact DangoContact, DangoRoundBallContact FloorContact)
{
    public bool Succeeded => Physics.Succeeded;
    public int ErrorCode => Physics.ErrorCode;
}
