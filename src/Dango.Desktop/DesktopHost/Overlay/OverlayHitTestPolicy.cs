using Dango.Core.Coordinates;

namespace Dango.Desktop.DesktopHost.Overlay;

public enum OverlayHitTarget
{
    PassThrough,
    Island,
    Dango,
}

public sealed record OverlayHitRegion
{
    public OverlayHitRegion(OverlayHitTarget target, LogicalRect bounds)
    {
        if (target == OverlayHitTarget.PassThrough)
        {
            throw new ArgumentException("穿透区不应注册为显式命中区域。", nameof(target));
        }

        Target = target;
        Bounds = bounds;
    }

    public OverlayHitTarget Target { get; }

    public LogicalRect Bounds { get; }
}

public sealed class OverlayHitTestPolicy
{
    private readonly IReadOnlyList<OverlayHitRegion> regions;

    public OverlayHitTestPolicy(IEnumerable<OverlayHitRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        this.regions = Array.AsReadOnly(regions.ToArray());
    }

    public OverlayHitTarget HitTest(LogicalPoint point) =>
        regions
            .Where(region => region.Bounds.Contains(point))
            .OrderByDescending(region => region.Target)
            .Select(region => region.Target)
            .FirstOrDefault();
}
