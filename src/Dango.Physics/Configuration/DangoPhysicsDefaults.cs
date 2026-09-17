using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics.Configuration;

public static class DangoPhysicsDefaults
{
    public const int Version = 1;

    public const int MaximumBodyCount = 3;

    public const int NodeCount = 16;

    public const int DisplayContactSampleCount = 128;

    public const double MaximumOverlapRatio = 0.03;

    public static DangoSelfPhysicsParameters Current => DangoSelfPhysicsParameters.CreateDefault();
}
