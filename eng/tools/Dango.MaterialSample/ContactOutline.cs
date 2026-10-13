using System.Windows;
using Dango.Physics;

namespace Dango.MaterialSample;

// C# mirror of the native pair-contact outline (dango_display_contact.h display_build and
// display_separation): nodes pushed out by the node radius, a quadratic B-spline of 128 samples,
// scaled 0.9 horizontally and 58/60 vertically about the core. Review-only measurement.
// DangoBodyState.Contour is already pushed out by the node radius (dango_self_read_frame).
internal static class ContactOutline
{
    private const int Samples = 128, PerNode = Samples / 16;

    // Node centre relative to the core: the published point minus the radial node radius.
    internal static Vector Node(DangoBodyState s, int n, double radius)
    {
        var p = new Vector(s.Contour[2 * n], s.Contour[2 * n + 1]);
        return p * (1 - radius / p.Length);
    }

    // bodySize = radial rest length / 0.68: the presentation-box limit .98 scales up for larger rigs.
    internal static Point[] Build(DangoBodyState s, double bodySize = 1)
    {
        var expanded = new Vector[16];
        double maximum = 0;
        for (var n = 0; n < 16; n++)
        {
            expanded[n] = new Vector(s.Contour[2 * n], s.Contour[2 * n + 1]);
            maximum = Math.Max(maximum, Math.Max(Math.Abs(expanded[n].X), Math.Abs(expanded[n].Y)));
        }
        var scale = Math.Min(1, .98 * Math.Max(1, bodySize) / maximum);
        var points = new Point[Samples];
        for (var k = 0; k < Samples; k++)
        {
            int n = k / PerNode; double t = (double)(k % PerNode) / PerNode;
            var q = .5 * (1 - t) * (1 - t) * expanded[(n + 15) % 16] + (.5 + t - t * t) * expanded[n] + .5 * t * t * expanded[(n + 1) % 16];
            points[k] = new(s.CoreX + q.X * scale * .9, s.CoreY + q.Y * scale * (58.0 / 60));
        }
        return points;
    }

    // Separating-axis gap (negative = overlap depth), and the smaller projected width along it.
    internal static (double Gap, double SmallerWidth) Separation(Point[] a, Point[] b)
    {
        double best = double.NegativeInfinity, width = 0;
        foreach (var m in new[] { a, b })
            for (var k = 0; k < m.Length; k++)
            {
                var e = m[(k + 1) % m.Length] - m[k];
                if (e.Length < 1e-9) continue;
                var normal = new Vector(e.Y / e.Length, -e.X / e.Length);
                var (amin, amax) = Project(a, normal); var (bmin, bmax) = Project(b, normal);
                var gap = Math.Max(bmin - amax, amin - bmax);
                if (gap > best) { best = gap; width = Math.Min(amax - amin, bmax - bmin); }
            }
        return (best, width);
    }

    // Deepest overlap between node circles of two rigs (positive = overlap).
    internal static double SkeletonOverlap(DangoBodyState a, double ra, DangoBodyState b, double rb)
    {
        double worst = double.NegativeInfinity;
        for (var i = 0; i < 16; i++)
            for (var j = 0; j < 16; j++)
            {
                var d = new Vector(a.CoreX - b.CoreX, a.CoreY - b.CoreY) + Node(a, i, ra) - Node(b, j, rb);
                worst = Math.Max(worst, ra + rb - d.Length);
            }
        return worst;
    }

    private static (double, double) Project(Point[] points, Vector normal)
    {
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (var p in points) { var v = p.X * normal.X + p.Y * normal.Y; low = Math.Min(low, v); high = Math.Max(high, v); }
        return (low, high);
    }
}
