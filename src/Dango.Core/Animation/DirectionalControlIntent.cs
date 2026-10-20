namespace Dango.Core.Animation;

public enum DangoFacingDirection
{
    Center,
    Left,
    Right,
}

public readonly record struct DirectionalControlIntent(
    bool Left,
    bool Right,
    bool Jump,
    bool Squash)
{
    public static DirectionalControlIntent None => default;
    public bool Stretch { get; init; }
}

public sealed record GroundSurfaceMotionProfile
{
    public GroundSurfaceMotionProfile(string surfaceId, double frictionCoefficient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        if (surfaceId.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceId));
        }

        if (!double.IsFinite(frictionCoefficient) || frictionCoefficient is < 0.05 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(frictionCoefficient));
        }

        SurfaceId = surfaceId;
        FrictionCoefficient = frictionCoefficient;
    }

    public string SurfaceId { get; }

    public double FrictionCoefficient { get; }

    public static GroundSurfaceMotionProfile DefaultFloor { get; } = new("default-floor", 1);
}
