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
    Fallback,
}

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
    PhysicsPresentationDiagnosticCode Code);

public sealed record PhysicsPresentationFrame
{
    public PhysicsPresentationFrame(
        bool isVisible,
        IEnumerable<PresentationPoint> contour,
        PresentationAttachment eyes,
        PresentationAttachment hat,
        PhysicsPresentationFallbackReason fallbackReason,
        PhysicsPresentationDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(contour);
        ArgumentNullException.ThrowIfNull(diagnostics);

        IsVisible = isVisible;
        Contour = Array.AsReadOnly(contour.ToArray());
        Eyes = eyes;
        Hat = hat;
        FallbackReason = fallbackReason;
        Diagnostics = diagnostics;
    }

    public bool IsVisible { get; }

    public IReadOnlyList<PresentationPoint> Contour { get; }

    public PresentationAttachment Eyes { get; }

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
        IEnumerable<string> participantIds)
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

    public static PhysicsPresentationInput FromSnapshot(SceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
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
            []);
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
