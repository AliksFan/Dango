using System.Collections.ObjectModel;
using Dango.Physics;

namespace Dango.RoundBallAdmission;

public readonly record struct Point2(double X, double Y);

public readonly record struct ViewParameters(Point2 OriginDip, double Scale, double DpiScale)
{
    public void Validate()
    {
        if (!double.IsFinite(OriginDip.X) || !double.IsFinite(OriginDip.Y) ||
            !double.IsFinite(Scale) || Scale <= 0 || !double.IsFinite(DpiScale) || DpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(ViewParameters), "Finite origin and positive finite scale/DPI are required.");
    }
}

// This is an admission-probe input contract, not a proposed public physics ABI.
public sealed record BallSnapshot(int EntityId, double CenterQX, double CenterQY, double RadiusQ,
    double AngleRadians, double VelocityQX, double VelocityQY, double AngularVelocity, bool IsSleeping);

public sealed record FrameInput(string Name, long SceneRevision, long SuccessfulStep, DangoBodyState Body,
    BallSnapshot Ball, string BallSourceKind = "syntheticGeometry");

public sealed record ProbeInput(int Schema, string SourceKind, string Description,
    string? SourceCommit, string? SourceModuleSha256, FrameInput[] Frames);

// Copies the mutable IReadOnlyList input once. No world, library handle, clock, or Step is held.
public sealed class FrozenSnapshot
{
    public string Name { get; }
    public long SceneRevision { get; }
    public long SuccessfulStep { get; }
    public DangoBodyState Body { get; }
    public BallSnapshot Ball { get; }
    public string BallSourceKind { get; }

    private FrozenSnapshot(FrameInput input, float[] contour)
    {
        Name = input.Name;
        SceneRevision = input.SceneRevision;
        SuccessfulStep = input.SuccessfulStep;
        Body = input.Body with { Contour = new ReadOnlyCollection<float>(contour) };
        Ball = input.Ball;
        BallSourceKind = input.BallSourceKind;
    }

    public static FrozenSnapshot CopyFrom(FrameInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name) || input.SceneRevision < 0 || input.SuccessfulStep < 0)
            throw new InvalidDataException("Named nonnegative scene/step versions are required.");
        var body = input.Body ?? throw new InvalidDataException("A public body frame is required.");
        if (body.EntityId.Value <= 0 || body.PointCount != 16 || body.Contour is null || body.Contour.Count != 32)
            throw new InvalidDataException("A sixteen-node public body frame with a positive ID is required.");
        var contour = body.Contour.ToArray();
        if (contour.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(body.CoreX) || !float.IsFinite(body.CoreY) || !float.IsFinite(body.SignedArea) ||
            !float.IsFinite(body.CentroidX) || !float.IsFinite(body.CentroidY) || !float.IsFinite(body.MaximumSpeed) ||
            body.SignedArea <= 0 || body.MaximumSpeed < 0 || body.GroundContactCount < 0 ||
            body.SelectedDragNode is < -1 or > 15)
            throw new InvalidDataException("The public body frame contains invalid values.");
        var ball = input.Ball ?? throw new InvalidDataException("A ball snapshot is required.");
        if (input.BallSourceKind is not ("syntheticGeometry" or "capturedInternalBallState"))
            throw new InvalidDataException("Ball source must explicitly identify synthetic geometry or captured internal state.");
        if (ball.EntityId <= 0 || !double.IsFinite(ball.CenterQX) || !double.IsFinite(ball.CenterQY) ||
            !double.IsFinite(ball.RadiusQ) || ball.RadiusQ <= 0 || !double.IsFinite(ball.AngleRadians) ||
            !double.IsFinite(ball.VelocityQX) || !double.IsFinite(ball.VelocityQY) || !double.IsFinite(ball.AngularVelocity))
            throw new InvalidDataException("The ball snapshot contains invalid values.");
        return new(input, contour);
    }
}

public sealed record CheckResult(string Name, bool Passed, string Detail);

public sealed record SnapshotEvidence(string Name, long SceneRevision, long SuccessfulStep,
    int BodyEntityId, int BallEntityId, string BallSourceKind, double BallAngleRadians);

public sealed record ProbeReport(int Schema, string Purpose, string SourceKind, string Description,
    string? SourceCommit, string? SourceModuleSha256, string? InputSha256,
    bool BaselineModuleVerified, bool NativeNoBallRegressionVerified, bool RealWindowVerified,
    int Passed, int Failed, IReadOnlyList<CheckResult> Checks, IReadOnlyList<SnapshotEvidence> Snapshots);
