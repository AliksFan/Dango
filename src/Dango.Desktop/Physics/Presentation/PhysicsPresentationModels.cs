using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;

namespace Dango.Desktop.Physics.Presentation;

public enum PhysicsPresentationBackend
{
    DeterministicFake,
    Box2D,
    AuthoredFallback,
}

public enum PhysicsPresentationFallbackReason
{
    None,
    Hidden,
    ReducedMotion,
    HostUnavailable,
    NativeUnavailable,
    NativeProbeFailed,
    SimulationUnstable,
}

public enum PhysicsPresentationWorldState
{
    Created,
    Active,
    Suspended,
    Disposed,
}

public enum PhysicsPresentationDiagnosticCode
{
    FakeActive,
    Box2DContractProbe,
    SoftBodyActive,
    Fallback,
}

public readonly record struct SoftBodyConfiguration
{
    public SoftBodyConfiguration(int nodeCount, int subStepCount)
    {
        if (nodeCount is not (12 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount), "软体节点数仅允许 12 或 16。");
        }
        if (subStepCount is not (2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(subStepCount), "Box2D 子步数仅允许 2 或 4。");
        }

        NodeCount = nodeCount;
        SubStepCount = subStepCount;
    }

    public int NodeCount { get; }

    public int SubStepCount { get; }

    public override string ToString() => $"{NodeCount} nodes / {SubStepCount} substeps";
}

public static class SoftBodyStabilityLimits
{
    public const double MinimumSignedArea = 0.45;
    public const double MaximumRadius = 0.98;
    public const double MaximumNodeSpeed = 8;
    public const double MaximumCentroidOffset = 0.25;
    public const double MaximumEnergyPerNode = MaximumNodeSpeed * MaximumNodeSpeed;
    public const double ReplayTolerance = 0.00001;
}

public sealed record SoftBodyMetrics(
    SoftBodyConfiguration Configuration,
    double SignedArea,
    PresentationPoint Centroid,
    double MaximumRadius,
    double MaximumNodeSpeed,
    double EnergyProxy,
    bool IsOrientationValid,
    bool IsSleeping);

public readonly record struct PresentationPoint
{
    public PresentationPoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1 || Math.Abs(y) > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "表现坐标必须是 [-1, 1] 内的有限局部坐标。");
        }

        X = x;
        Y = y;
    }

    // Exact sampled belt contours may extend outside the legacy normalized template.
    // Preserve the public constructor's normalized contract for existing consumers.
    internal static PresentationPoint FromSampledBoundary(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        return new PresentationPoint(x, y, true);
    }

    private PresentationPoint(double x, double y, bool sampledBoundary) { X = x; Y = y; }

    public double X { get; }

    public double Y { get; }
}

public readonly record struct PresentationAttachment
{
    public PresentationAttachment(PresentationPoint position, double rotationRadians)
    {
        if (!double.IsFinite(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }

        Position = position;
        RotationRadians = rotationRadians;
    }

    public PresentationPoint Position { get; }

    public double RotationRadians { get; }
}

public sealed record PhysicsPresentationDiagnostics(
    PhysicsPresentationBackend Backend,
    int SimulatedStepCount,
    PhysicsPresentationDiagnosticCode Code,
    SoftBodyMetrics? SoftBody = null,
    int NativeErrorCode = 0);

public sealed record PhysicsPresentationFrame
{
    public Attachments.AttachmentPresentationFrame? Attachments { get; init; }
    public double? DozeSeconds { get; init; }
    public const double DefaultEyeHalfDistance = 19d / 72d;

    public PhysicsPresentationFrame(
        bool isVisible,
        IEnumerable<PresentationPoint> contour,
        PresentationAttachment eyes,
        PresentationAttachment hat,
        PhysicsPresentationFallbackReason fallbackReason,
        PhysicsPresentationDiagnostics diagnostics,
        PresentationAttachment? leftEye = null,
        PresentationAttachment? rightEye = null)
    {
        ArgumentNullException.ThrowIfNull(contour);
        ArgumentNullException.ThrowIfNull(diagnostics);

        IsVisible = isVisible;
        Contour = Array.AsReadOnly(contour.ToArray());
        Eyes = eyes;
        LeftEye = leftEye ?? new PresentationAttachment(
            new PresentationPoint(
                Math.Clamp(eyes.Position.X - DefaultEyeHalfDistance, -1, 1),
                eyes.Position.Y),
            eyes.RotationRadians);
        RightEye = rightEye ?? new PresentationAttachment(
            new PresentationPoint(
                Math.Clamp(eyes.Position.X + DefaultEyeHalfDistance, -1, 1),
                eyes.Position.Y),
            eyes.RotationRadians);
        Hat = hat;
        FallbackReason = fallbackReason;
        Diagnostics = diagnostics;
    }

    public bool IsVisible { get; }

    public IReadOnlyList<PresentationPoint> Contour { get; }

    public PresentationAttachment Eyes { get; }

    public PresentationAttachment LeftEye { get; }

    public PresentationAttachment RightEye { get; }

    public PresentationAttachment Hat { get; }

    public PhysicsPresentationFallbackReason FallbackReason { get; }

    public PhysicsPresentationDiagnostics Diagnostics { get; }
}

public sealed record PhysicsPresentationInput
{
    public PhysicsPresentationInput(
        string dangoId,
        LogicalRect authoritativeBounds,
        DangoActionState action,
        long authoritativeStartTimeMilliseconds,
        int deterministicSeed,
        string semanticAnchor,
        LogicalVector contactNormal,
        double strength,
        int parameterVersion,
        bool reducedMotion,
        bool isHostAvailable,
        IEnumerable<string> participantIds,
        double squashStrength = 0,
        double groundFrictionCue = 0,
        DangoFacingDirection facingDirection = DangoFacingDirection.Center,
        PresentationPoint? dragGrabPoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dangoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticAnchor);
        ArgumentNullException.ThrowIfNull(participantIds);
        if (!double.IsFinite(contactNormal.X) || !double.IsFinite(contactNormal.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(contactNormal));
        }
        if (!double.IsFinite(strength) || strength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strength));
        }
        if (parameterVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameterVersion));
        }
        if (!double.IsFinite(squashStrength) || squashStrength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(squashStrength));
        }
        if (!double.IsFinite(groundFrictionCue) || groundFrictionCue is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(groundFrictionCue));
        }

        var participants = participantIds.ToArray();
        if (participants.Length is not (0 or 2) ||
            participants.Any(string.IsNullOrWhiteSpace) ||
            participants.Distinct(StringComparer.Ordinal).Count() != participants.Length)
        {
            throw new ArgumentException("表现参与者必须为空，或包含两个不同的团子标识。", nameof(participantIds));
        }

        DangoId = dangoId;
        AuthoritativeBounds = authoritativeBounds;
        Action = action;
        AuthoritativeStartTimeMilliseconds = authoritativeStartTimeMilliseconds;
        DeterministicSeed = deterministicSeed;
        SemanticAnchor = semanticAnchor;
        ContactNormal = contactNormal;
        Strength = strength;
        ParameterVersion = parameterVersion;
        ReducedMotion = reducedMotion;
        IsHostAvailable = isHostAvailable;
        ParticipantIds = Array.AsReadOnly(participants);
        SquashStrength = squashStrength;
        GroundFrictionCue = groundFrictionCue;
        FacingDirection = facingDirection;
        DragGrabPoint = dragGrabPoint;
    }

    public string DangoId { get; }

    public LogicalRect AuthoritativeBounds { get; }

    public DangoActionState Action { get; }

    public long AuthoritativeStartTimeMilliseconds { get; }

    public int DeterministicSeed { get; }

    public string SemanticAnchor { get; }

    public LogicalVector ContactNormal { get; }

    public double Strength { get; }

    public int ParameterVersion { get; }

    public bool ReducedMotion { get; init; }

    public bool IsHostAvailable { get; init; }

    public IReadOnlyList<string> ParticipantIds { get; }

    public double SquashStrength { get; }

    public double GroundFrictionCue { get; }

    public DangoFacingDirection FacingDirection { get; }

    public PresentationPoint? DragGrabPoint { get; }

    public static PhysicsPresentationInput FromSnapshot(
        SceneSnapshot snapshot,
        double squashStrength = 0,
        double groundFrictionCue = 0,
        DangoFacingDirection facingDirection = DangoFacingDirection.Center,
        PresentationPoint? dragGrabPoint = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!double.IsFinite(squashStrength) || squashStrength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(squashStrength));
        }
        return new(
            snapshot.Dango.Id,
            snapshot.Dango.VisualBounds,
            snapshot.Dango.State,
            snapshot.Dango.StateStartedAtMilliseconds,
            snapshot.Dango.VariantSeed,
            snapshot.Dango.IslandGrabAnchorId,
            new LogicalVector(0, -1),
            snapshot.Dango.State == DangoActionState.Landing ? 1 : 0,
            1,
            false,
            true,
            [],
            squashStrength,
            groundFrictionCue,
            facingDirection,
            dragGrabPoint);
    }
}

public sealed record PairedPresentationContactCue
{
    public PairedPresentationContactCue(
        IEnumerable<string> participantIds,
        LogicalVector contactNormal,
        double strength)
    {
        ArgumentNullException.ThrowIfNull(participantIds);
        var participants = participantIds.ToArray();
        if (participants.Length != 2 ||
            participants.Any(string.IsNullOrWhiteSpace) ||
            participants.Distinct(StringComparer.Ordinal).Count() != 2)
        {
            throw new ArgumentException("双体表现接触必须包含两个不同的参与者。", nameof(participantIds));
        }
        if (!double.IsFinite(contactNormal.X) || !double.IsFinite(contactNormal.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(contactNormal));
        }
        if (!double.IsFinite(strength) || strength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strength));
        }

        ParticipantIds = Array.AsReadOnly(participants);
        ContactNormal = contactNormal;
        Strength = strength;
    }

    public IReadOnlyList<string> ParticipantIds { get; }

    public LogicalVector ContactNormal { get; }

    public double Strength { get; }
}
