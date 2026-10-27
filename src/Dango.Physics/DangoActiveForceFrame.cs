namespace Dango.Physics;

/// <summary>Actual active point force in native world metres and newtons.</summary>
public readonly record struct DangoActiveForce(double X, double Y, double ForceX, double ForceY,
    int SourceKind, double Material);

/// <summary>Last accepted substep only; excludes gravity, passive elasticity, contact and jump impulses.</summary>
public sealed record DangoActiveForceFrame(ulong Attempt, int Substep, IReadOnlyList<DangoActiveForce> Forces);
