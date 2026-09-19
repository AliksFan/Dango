using Dango.Core.Coordinates;

namespace Dango.Core.Scene;

public enum HitShapePurpose
{
    Click,
    Drag,
    Collision,
}

public abstract record HitShape(string Id, HitShapePurpose Purpose, LogicalRect Bounds)
{
    public abstract bool Contains(LogicalPoint point);
}

public sealed record RectangleHitShape(string ShapeId, HitShapePurpose ShapePurpose, LogicalRect ShapeBounds)
    : HitShape(ShapeId, ShapePurpose, ShapeBounds)
{
    public override bool Contains(LogicalPoint point) => Bounds.Contains(point);
}

public sealed record EllipseHitShape(string ShapeId, HitShapePurpose ShapePurpose, LogicalRect ShapeBounds)
    : HitShape(ShapeId, ShapePurpose, ShapeBounds)
{
    public override bool Contains(LogicalPoint point)
    {
        var normalizedX = (point.X - Bounds.Center.X) / (Bounds.Width / 2);
        var normalizedY = (point.Y - Bounds.Center.Y) / (Bounds.Height / 2);
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1;
    }
}
