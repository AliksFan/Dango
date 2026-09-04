using Dango.Core.Coordinates;

namespace Dango.Desktop.DesktopHost.Interaction;

public enum DesktopHitTarget
{
    PassThrough,
    Island,
    Dango,
}

public sealed record DesktopHitRegion
{
    public DesktopHitRegion(DesktopHitTarget target, LogicalRect bounds)
    {
        if (target == DesktopHitTarget.PassThrough)
        {
            throw new ArgumentException("穿透区不应注册为显式命中区域。", nameof(target));
        }

        Target = target;
        Bounds = bounds;
    }

    public DesktopHitTarget Target { get; }

    public LogicalRect Bounds { get; }
}

public static class DesktopHitRegionFactory
{
    public static DesktopHitRegion? TryCreate(
        DesktopHitTarget target,
        double x,
        double y,
        double width,
        double height)
    {
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            !double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return new DesktopHitRegion(target, new LogicalRect(x, y, width, height));
    }
}

public sealed class DesktopHitTestPolicy
{
    private readonly IReadOnlyList<DesktopHitRegion> regions;

    public DesktopHitTestPolicy(IEnumerable<DesktopHitRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        this.regions = Array.AsReadOnly(regions.ToArray());
    }

    public DesktopHitTarget HitTest(LogicalPoint point) =>
        regions
            .Where(region => region.Bounds.Contains(point))
            .OrderByDescending(region => region.Target)
            .Select(region => region.Target)
            .FirstOrDefault();
}
