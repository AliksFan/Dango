namespace Dango.Core.Coordinates;

public sealed record DisplayWorkArea
{
    public DisplayWorkArea(
        string id,
        LogicalRect logicalBounds,
        PixelRect physicalBounds,
        DpiScale dpiScale)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("显示器标识不能为空。", nameof(id));
        }

        var expectedPixelWidth = logicalBounds.Width * dpiScale.X;
        var expectedPixelHeight = logicalBounds.Height * dpiScale.Y;
        if (!NearlyEqual(expectedPixelWidth, physicalBounds.Width) ||
            !NearlyEqual(expectedPixelHeight, physicalBounds.Height))
        {
            throw new ArgumentException("逻辑工作区、物理工作区与 DPI 比例不一致。", nameof(physicalBounds));
        }

        Id = id;
        LogicalBounds = logicalBounds;
        PhysicalBounds = physicalBounds;
        DpiScale = dpiScale;
    }

    public string Id { get; }

    public LogicalRect LogicalBounds { get; }

    public PixelRect PhysicalBounds { get; }

    public DpiScale DpiScale { get; }

    public PixelPoint ToPhysical(LogicalPoint logicalPoint) =>
        new(
            PhysicalBounds.X + ((logicalPoint.X - LogicalBounds.X) * DpiScale.X),
            PhysicalBounds.Y + ((logicalPoint.Y - LogicalBounds.Y) * DpiScale.Y));

    public LogicalPoint ToLogical(PixelPoint physicalPoint) =>
        new(
            LogicalBounds.X + ((physicalPoint.X - PhysicalBounds.X) / DpiScale.X),
            LogicalBounds.Y + ((physicalPoint.Y - PhysicalBounds.Y) / DpiScale.Y));

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 0.001;
}

public sealed class DisplayLayout
{
    private readonly IReadOnlyList<DisplayWorkArea> workAreas;

    public DisplayLayout(IEnumerable<DisplayWorkArea> workAreas, string primaryDisplayId)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        var workAreaArray = workAreas.ToArray();
        if (workAreaArray.Length == 0)
        {
            throw new ArgumentException("显示器布局至少需要一个工作区。", nameof(workAreas));
        }

        if (workAreaArray.Select(display => display.Id).Distinct(StringComparer.Ordinal).Count() != workAreaArray.Length)
        {
            throw new ArgumentException("显示器标识必须唯一。", nameof(workAreas));
        }

        Primary = workAreaArray.SingleOrDefault(
            display => string.Equals(display.Id, primaryDisplayId, StringComparison.Ordinal)) ??
            throw new ArgumentException("主显示器必须存在于工作区集合中。", nameof(primaryDisplayId));
        this.workAreas = Array.AsReadOnly(workAreaArray);
    }

    public IReadOnlyList<DisplayWorkArea> WorkAreas => workAreas;

    public DisplayWorkArea Primary { get; }

    public DisplayWorkArea? FindContaining(LogicalPoint point) =>
        workAreas.FirstOrDefault(display => display.LogicalBounds.Contains(point));

    public LogicalRect PlaceWithinAvailableWorkArea(LogicalRect bounds)
    {
        if (workAreas.Any(display => display.LogicalBounds.Contains(bounds)))
        {
            return bounds;
        }

        var target = workAreas
            .Select(display => new
            {
                Display = display,
                Area = display.LogicalBounds.IntersectionArea(bounds),
            })
            .Where(candidate => candidate.Area > 0)
            .OrderByDescending(candidate => candidate.Area)
            .ThenBy(candidate => candidate.Display.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Display)
            .FirstOrDefault() ?? Primary;

        return bounds.ClampInside(target.LogicalBounds);
    }
}
