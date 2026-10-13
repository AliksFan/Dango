namespace Dango.Physics;

public enum DangoStaticPlatformChangeKind { Insert = 1, Move = 2, Delete = 3 }

public enum DangoStaticPlatformChangeRejection
{
    None, InvalidDefinition, UnsupportedComposition, StaleScene, InvalidTarget,
    MissingBounds, OutsideRoom, Penetration, UnhealthyWorld
}

public sealed record DangoStaticPlatformProbe(
    ulong WorldGeneration, ulong SceneRevision, ulong PhysicsStep, float MinimumGap,
    DangoStaticPlatformChangeRejection Rejection)
{
    public bool Allowed => Rejection == DangoStaticPlatformChangeRejection.None;
}

/// <summary>Transient, owner-world-bound metadata. Never persist or reuse after a step.</summary>
public readonly record struct DangoPreparedStaticPlatformChange(
    ulong WorldGeneration, ulong TokenId, ulong SceneRevision, ulong PhysicsStep);

public sealed record DangoStaticPlatformPreparation(
    DangoStaticPlatformProbe Probe, DangoPreparedStaticPlatformChange? Token);

public sealed record DangoFurnitureNodeState(ulong BodyId, float X, float Y, float VelocityX, float VelocityY);

public sealed record DangoFurnitureBodyDiagnostics(
    DangoEntityId EntityId, IReadOnlyList<DangoFurnitureNodeState> Nodes,
    float SecondsSinceJump, bool DragActive, bool IsGrounded, DangoStaticPlatformContact PlatformContact);

/// <summary>
/// Furniture and Physics share one native publication. Diagnostics additionally
/// observe the live raw rig; ErrorCode distinguishes a retained safe frame from
/// an unhealthy runtime. Runtime counters are separate from file layout versions.
/// </summary>
public sealed record DangoFurnitureFrame(
    ulong WorldGeneration, ulong SceneRevision, ulong PhysicsStep, ulong PlatformInstanceId,
    ulong LastAppliedToken, DangoStaticPlatformDefinition? Platform,
    DangoPhysicsFrame Physics, IReadOnlyList<DangoFurnitureBodyDiagnostics> BodyDiagnostics);
