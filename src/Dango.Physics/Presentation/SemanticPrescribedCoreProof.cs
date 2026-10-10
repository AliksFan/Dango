namespace Dango.Physics.Presentation;

public sealed record SemanticPrescribedCoreSample(double Fraction, double ActualCoreX, double ActualCoreY,
    double NativeTargetCoreX, double NativeTargetCoreY);

public sealed record SemanticPrescribedBodyProof(DangoEntityId EntityId,
    DangoPresentationTarget Start, DangoPresentationTarget End, double MaximumStageError,
    IReadOnlyList<SemanticPrescribedCoreSample> Samples);

/// <summary>Local native diagnostics; this is never part of an incoming shared semantic snapshot.</summary>
public sealed record SemanticPrescribedCoreProof(ulong ExpectedEpoch, ulong ActualEpoch,
    ulong ExpectedOrdinal, ulong ActualOrdinal, int ErrorCode, int SubstepCount,
    IReadOnlyList<SemanticPrescribedBodyProof> Bodies);
