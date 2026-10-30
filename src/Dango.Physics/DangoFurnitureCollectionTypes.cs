namespace Dango.Physics;

public enum DangoFurnitureCoordinateSpace { LegacyPAndQ, BeltWorldMeters }

public enum DangoFurnitureKind { StaticPlatform = 1, RoundBall = 2 }
public enum DangoFurnitureChangeKind { Insert = 1, Move = 2, Delete = 3 }
public enum DangoFurnitureChangeRejection
{
    None, InvalidDefinition, UnsupportedComposition, StaleScene, InvalidTarget,
    MissingBounds, OutsideRoom, Penetration, UnhealthyWorld, CapacityExceeded
}

/// <summary>CoordinateSpace explicitly selects legacy p/q units or room meters, including the ball fields named Q.</summary>
public sealed record DangoFurnitureDefinition(DangoFurnitureKind Kind,
    DangoStaticPlatformDefinition? Platform = null, DangoRoundBallDefinition? Ball = null)
{
    public DangoFurnitureCoordinateSpace CoordinateSpace { get; init; } = DangoFurnitureCoordinateSpace.LegacyPAndQ;

    public void Validate()
    {
        if (!Enum.IsDefined(CoordinateSpace)) throw new ArgumentOutOfRangeException(nameof(CoordinateSpace));
        if (Kind == DangoFurnitureKind.StaticPlatform && Platform is not null && Ball is null) Platform.Validate();
        else if (Kind == DangoFurnitureKind.RoundBall && Ball is not null && Platform is null) Ball.Validate();
        else throw new ArgumentException("家具类型与定义不一致。");
    }
}

public sealed record DangoFurnitureProbe(ulong WorldGeneration, ulong SceneRevision, ulong PhysicsStep,
    double MinimumGap, DangoFurnitureChangeRejection Rejection)
{ public bool Allowed => Rejection == DangoFurnitureChangeRejection.None; }
public readonly record struct DangoPreparedFurnitureChange(
    ulong WorldGeneration, ulong TokenId, ulong SceneRevision, ulong PhysicsStep);
public sealed record DangoFurniturePreparation(DangoFurnitureProbe Probe, DangoPreparedFurnitureChange? Token);
public sealed record DangoFurnitureCollectionItem(ulong InstanceId, DangoFurnitureDefinition Definition,
    DangoRoundBallState? BallState, DangoRoundBallContact? DangoContact, DangoRoundBallContact? SurfaceContact);
/// <summary>One deeply read-only last-safe native publication, including diagnostics.</summary>
public sealed record DangoFurnitureCollectionFrame(ulong WorldGeneration, ulong SceneRevision, ulong PhysicsStep,
    ulong LastAppliedToken, DangoPhysicsFrame Physics,
    IReadOnlyList<DangoFurnitureBodyDiagnostics> BodyDiagnostics, IReadOnlyList<DangoFurnitureCollectionItem> Items)
{
    public DangoFurnitureCoordinateSpace CoordinateSpace { get; init; } = DangoFurnitureCoordinateSpace.LegacyPAndQ;
}
