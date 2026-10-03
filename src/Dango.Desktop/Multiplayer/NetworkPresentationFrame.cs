using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Protocol;

namespace Dango.Desktop.Multiplayer;

public sealed record NetworkWaveFrame(string? ActionId, double Progress, long RemainingMilliseconds,
    double ScaleX, double ScaleY)
{
    public bool IsActive => ActionId is not null;
    public static NetworkWaveFrame Idle { get; } = new(null, 1, 0, 1, 1);
}

/// <summary>Immutable drawing data. No native state can be written back through this frame.</summary>
public sealed record NetworkPetFrame(string PetId, RoomPoint AnchorPosition, LogicalRect Bounds,
    PhysicsPresentationFrame Presentation, NetworkWaveFrame Wave, double BottomY,
    double ScaleX, double ScaleY, long FrameNumber)
{
    public bool IsFallback => Presentation.FallbackReason != PhysicsPresentationFallbackReason.None;
}

public static class WavePresentationSampler
{
    public static NetworkWaveFrame Sample(ActionInstance? action, long serverTimeMs, bool reducedMotion = false)
    {
        if (action is null || serverTimeMs >= action.EndsAtServerTimeMs) return NetworkWaveFrame.Idle;
        if (action.ActionKey != "wave" || action.PresentationVersion != 1 || action.FallbackKey != "wave-nod" ||
            action.EndsAtServerTimeMs <= action.StartedAtServerTimeMs)
            throw new ArgumentException("Unsupported network action.", nameof(action));
        var progress = RoomSampling.ActionProgress(action, serverTimeMs);
        // A bounded two-pulse nod; its phase is purely authoritative time, including on late join.
        var pulse = Math.Pow(Math.Sin(Math.PI * progress), 2) *
            (.65 + .35 * Math.Pow(Math.Sin(2 * Math.PI * progress + (action.VariantSeed & 1) * Math.PI / 2), 2));
        var amount = pulse * (reducedMotion ? .003 : .009);
        return new(action.ActionId, progress, Math.Max(0, action.EndsAtServerTimeMs - serverTimeMs),
            1 + amount / 2, 1 - amount);
    }
}
