using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation.Attachments;
using Point = System.Windows.Point;

namespace Dango.Desktop.Rendering.Visuals;

/// <summary>Shared material deformation. The root stays fixed in material coordinates.</summary>
public static class HatDeformationGeometry
{
    private sealed record Asset(System.Windows.Shapes.Path[] Paths, Geometry[] Originals, PathGeometry[] Flattened);
    private static readonly ConditionalWeakTable<Canvas, Asset> Assets = new();
    public static void InvalidateAsset(Canvas layer) => Assets.Remove(layer);
    public static Point Deform(Point point, double bend, double stretch)
    {
        if (!double.IsFinite(bend) || !double.IsFinite(stretch) || Math.Abs(bend) > HatBodyFollow.DeformationLimit || Math.Abs(stretch) > HatBodyFollow.DeformationLimit)
            throw new ArgumentOutOfRangeException(nameof(bend));
        var t = Math.Clamp((25 - point.Y) / 23, 0, 1);
        var weight = t * t * (3 - 2 * t);
        return new(point.X + bend * 23 * weight, point.Y - stretch * 23 * weight);
    }

    public static void Apply(Canvas layer, double bend, double stretch)
    {
        _ = Deform(new(80, 25), bend, stretch);
        var asset = Assets.GetValue(layer, canvas =>
        {
            var paths = canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
            var original = paths.Select(p => p.Data).ToArray();
            return new(paths, original, original.Select(g => g.GetFlattenedPathGeometry(0.005, ToleranceType.Absolute)).ToArray());
        });
        for (var index = 0; index < asset.Paths.Length; index++)
        {
            if (bend == 0 && stretch == 0) { asset.Paths[index].Data = asset.Originals[index]; continue; }
            var geometry = new StreamGeometry { FillRule = asset.Flattened[index].FillRule };
            using (var context = geometry.Open())
                foreach (var figure in asset.Flattened[index].Figures)
                {
                    var previous = figure.StartPoint;
                    context.BeginFigure(Deform(previous, bend, stretch), figure.IsFilled, figure.IsClosed);
                    foreach (var segment in figure.Segments)
                    {
                        IEnumerable<Point> points = segment is PolyLineSegment poly ? poly.Points : segment is LineSegment line ? new[] { line.Point } : throw new InvalidOperationException("Expected flattened hat path.");
                        foreach (var point in points) { Append(previous, point, 0); previous = point; }
                    }
                    if (figure.IsClosed) Append(previous, figure.StartPoint, 0);
                    void Append(Point a, Point b, int depth)
                    {
                        var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                        var da = Deform(a, bend, stretch); var db = Deform(b, bend, stretch); var dm = Deform(middle, bend, stretch);
                        // Bound segment length as well as midpoint error, including cubic inflections.
                        if (depth < 16 && ((b - a).Length > 0.5 || (dm - new Point((da.X + db.X) / 2, (da.Y + db.Y) / 2)).Length > 0.005))
                        { Append(a, middle, depth + 1); Append(middle, b, depth + 1); }
                        else context.LineTo(db, true, false);
                    }
                }
            geometry.Freeze(); asset.Paths[index].Data = geometry;
        }
    }
}
