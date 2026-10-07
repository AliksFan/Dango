using System.Windows;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories;

/// <summary>Geometric coordinates on the displayed body, independent of material-node indices.</summary>
public static class AccessoryBodyMapper
{
    public static Point Map(PhysicsPresentationFrame body, GaitPresentationFrame? gait, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(x));
        var shape = body.Attachments?.SourceShape ?? AttachmentShapeGeometry.Create(body.Contour);
        var level = shape.Top - y * shape.Height;
        var interval = Section(shape, level);
        var point = new Point(80 + (interval.Left + (interval.Right - interval.Left) * x) * 160, 66 - level * 160);
        return DeformableDangoVisual.CreateGaitTransform(body, gait).Transform(point);
    }

    public static Point Unmap(PhysicsPresentationFrame body, GaitPresentationFrame? gait, Point displayedPoint)
    {
        var transform = DeformableDangoVisual.CreateGaitTransform(body, gait);
        var point = transform.Inverse?.Transform(displayedPoint) ?? displayedPoint;
        var shape = body.Attachments?.SourceShape ?? AttachmentShapeGeometry.Create(body.Contour);
        var level = Math.Clamp((66 - point.Y) / 160, shape.Bottom, shape.Top);
        var interval = Section(shape, level);
        var x = (point.X - 80) / 160;
        return new(interval.Right - interval.Left < 1e-8 ? .5 : Math.Clamp((x - interval.Left) / (interval.Right - interval.Left), 0, 1),
            Math.Clamp((shape.Top - level) / shape.Height, 0, 1));
    }

    private static (double Left, double Right) Section(AttachmentShapeGeometry shape, double level)
    {
        var epsilon = shape.Height * 1e-7;
        if (shape.CenterInterval(Math.Clamp(level, shape.Bottom + epsilon, shape.Top - epsilon)) is { } section)
            return section;
        // A very tilted crown may not intersect the global center. Use geometric intersections,
        // retaining the actual crown rather than substituting a rectangular bounding box.
        var sample = Math.Clamp(level, shape.Bottom + epsilon, shape.Top - epsilon);
        var points = new List<double>();
        for (var i = 0; i < shape.Boundary.Count; i++)
        {
            var a = shape.Boundary[i]; var b = shape.Boundary[(i + 1) % shape.Boundary.Count];
            if ((a.Y <= sample && sample < b.Y) || (b.Y <= sample && sample < a.Y))
                points.Add(a.X + (sample - a.Y) * (b.X - a.X) / (b.Y - a.Y));
        }
        if (points.Count >= 2) return (points.Min(), points.Max());
        var nearest = shape.Boundary.MinBy(p => Math.Abs(p.Y - level));
        return (nearest.X, nearest.X);
    }
}
