using Dango.Physics.Configuration;

namespace Dango.Physics.Presentation;

public readonly record struct SemanticPoint(double X, double Y);
public sealed record SemanticDrawnContact(double Gap, double NormalX, double NormalY, double MinimumProjectedWidth)
{
    public double PenetrationRatio => Math.Max(0, -Gap) / MinimumProjectedWidth;
}

/// <summary>Measures the actual accepted quadratic display mapping, in native p units and without DPI.</summary>
public static class SemanticDrawnGeometry
{
    public static IReadOnlyList<SemanticPoint> SampleContour(IReadOnlyList<float> contour, double centerX, double centerY)
    {
        if (contour.Count != 32 || contour.Any(value => !float.IsFinite(value)) || !double.IsFinite(centerX) || !double.IsFinite(centerY))
            throw new ArgumentException("Invalid local presentation contour.");
        var scale = Math.Min(1, .98 / contour.Max(value => Math.Abs(value)));
        var result = Enumerable.Range(0, DangoPhysicsDefaults.DisplayContactSampleCount).Select(sample =>
        {
            var node = sample / 8;
            var t = sample % 8 / 8d;
            double Axis(int axis) => .5 * (1 - t) * (1 - t) * contour[((node + 15) % 16) * 2 + axis] +
                (.5 + t - t * t) * contour[node * 2 + axis] + .5 * t * t * contour[((node + 1) % 16) * 2 + axis];
            return new SemanticPoint(centerX + Axis(0) * scale * .9, centerY + Axis(1) * scale * (58d / 60));
        }).ToArray();
        if (result.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("Invalid display mapping.");
        return Array.AsReadOnly(result);
    }

    public static SemanticDrawnContact Contact(IReadOnlyList<SemanticPoint> first, IReadOnlyList<SemanticPoint> second)
    {
        if (first.Count != 128 || second.Count != 128) throw new ArgumentException("Display SAT requires the pinned 128 samples.");
        var best = double.NegativeInfinity;
        var bestX = 0d;
        var bestY = 0d;
        foreach (var polygon in new[] { first, second })
            for (var edge = 0; edge < polygon.Count; edge++)
            {
                var from = polygon[edge];
                var to = polygon[(edge + 1) % polygon.Count];
                var length = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
                if (length < 1e-8) continue;
                var nx = (to.Y - from.Y) / length;
                var ny = -(to.X - from.X) / length;
                var a = first.Select(point => point.X * nx + point.Y * ny).ToArray();
                var b = second.Select(point => point.X * nx + point.Y * ny).ToArray();
                var forward = b.Min() - a.Max();
                var backward = a.Min() - b.Max();
                var gap = Math.Max(forward, backward);
                if (gap <= best) continue;
                best = gap;
                bestX = backward > forward ? -nx : nx;
                bestY = backward > forward ? -ny : ny;
            }
        double Width(IReadOnlyList<SemanticPoint> points)
        {
            var values = points.Select(point => point.X * bestX + point.Y * bestY).ToArray();
            return values.Max() - values.Min();
        }
        var width = Math.Min(Width(first), Width(second));
        if (!double.IsFinite(best) || width <= 0) throw new ArgumentException("Degenerate displayed contour.");
        return new(best, bestX, bestY, width);
    }

    public static bool IsSafeLocalContour(DangoBodyState body)
    {
        var contour = body.Contour;
        if (body.PointCount != 16 || contour.Count != 32 || contour.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(body.SignedArea) || body.SignedArea < DangoPhysicsDefaults.Current.MinimumArea ||
            !float.IsFinite(body.MaximumSpeed) || body.MaximumSpeed > DangoPhysicsDefaults.Current.MaximumSpeed + 1e-5)
            return false;
        var scale = Math.Min(1, .98 / contour.Max(value => Math.Abs(value)));
        var points = Enumerable.Range(0, 16).Select(node => new SemanticPoint(contour[node * 2] * scale, contour[node * 2 + 1] * scale)).ToArray();
        var area = Enumerable.Range(0, 16).Sum(i => Cross(points[i], points[(i + 1) % 16])) / 2;
        if (area <= 0) return false;
        var bottom = points.Min(point => point.Y);
        if (points.Any(point => point.Y <= bottom + .001 && point.Y < -.9 && Math.Abs(point.X) > .35)) return false;
        var edges = Enumerable.Range(0, 16).Select(i => Distance(points[i], points[(i + 1) % 16])).Order().ToArray();
        if (edges[^1] > edges[8] * 1.8) return false;
        for (var a = 0; a < 16; a++)
            for (var b = a + 1; b < 16; b++)
            {
                if ((a + 1) % 16 == b || (b + 1) % 16 == a) continue;
                var p = points[a]; var q = points[(a + 1) % 16]; var r = points[b]; var s = points[(b + 1) % 16];
                if (Cross(Subtract(q, p), Subtract(r, p)) * Cross(Subtract(q, p), Subtract(s, p)) < 0 &&
                    Cross(Subtract(s, r), Subtract(p, r)) * Cross(Subtract(s, r), Subtract(q, r)) < 0) return false;
            }
        return true;
    }

    private static SemanticPoint Subtract(SemanticPoint a, SemanticPoint b) => new(a.X - b.X, a.Y - b.Y);
    private static double Cross(SemanticPoint a, SemanticPoint b) => a.X * b.Y - a.Y * b.X;
    private static double Distance(SemanticPoint a, SemanticPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
