using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public enum DangoContourFailure
{
    None,
    TooFewPoints,
    Reversed,
    SelfIntersecting,
    FootLikeSpike,
    SharpSpike,
    InvalidSampledBoundary,
}

public readonly record struct DangoGeometryResult(
    bool Succeeded,
    StreamGeometry? Geometry,
    DangoContourFailure Failure);

public static class DangoContourValidator
{
    public static DangoContourFailure Validate(IReadOnlyList<PresentationPoint> contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (contour.Count < 4)
        {
            return DangoContourFailure.TooFewPoints;
        }
        if (HasSelfIntersection(contour))
        {
            return DangoContourFailure.SelfIntersecting;
        }
        if (SignedArea(contour) <= 0)
        {
            return DangoContourFailure.Reversed;
        }

        var lowest = contour.Min(point => point.Y);
        if (contour.Any(point => point.Y <= lowest + 0.001 && point.Y < -0.9 && Math.Abs(point.X) > 0.35))
        {
            return DangoContourFailure.FootLikeSpike;
        }

        var edges = Enumerable.Range(0, contour.Count)
            .Select(index => Distance(contour[index], contour[(index + 1) % contour.Count]))
            .Order()
            .ToArray();
        var median = edges[edges.Length / 2];
        return edges[^1] > median * 1.8
            ? DangoContourFailure.SharpSpike
            : DangoContourFailure.None;
    }

    private static double SignedArea(IReadOnlyList<PresentationPoint> contour) =>
        Enumerable.Range(0, contour.Count).Sum(index =>
        {
            var current = contour[index];
            var next = contour[(index + 1) % contour.Count];
            return (current.X * next.Y) - (next.X * current.Y);
        }) / 2;

    private static bool HasSelfIntersection(IReadOnlyList<PresentationPoint> contour)
    {
        for (var first = 0; first < contour.Count; first++)
        {
            var firstNext = (first + 1) % contour.Count;
            for (var second = first + 1; second < contour.Count; second++)
            {
                var secondNext = (second + 1) % contour.Count;
                if (first == second || firstNext == second || secondNext == first)
                {
                    continue;
                }
                if (SegmentsIntersect(contour[first], contour[firstNext], contour[second], contour[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        PresentationPoint a,
        PresentationPoint b,
        PresentationPoint c,
        PresentationPoint d)
    {
        static double Cross(PresentationPoint p, PresentationPoint q, PresentationPoint r) =>
            ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));

        var first = Cross(a, b, c);
        var second = Cross(a, b, d);
        var third = Cross(c, d, a);
        var fourth = Cross(c, d, b);
        return first * second < 0 && third * fourth < 0;
    }

    private static double Distance(PresentationPoint first, PresentationPoint second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}

public static class DangoStreamGeometryFactory
{
    // The room's continuous body already passed its native shape guard. Preserve
    // its sampled boundary and the same 72-unit mapping on both axes; the legacy
    // spline's 72x58 template is a different geometry.
    internal static DangoGeometryResult TryCreateBelt(IReadOnlyList<PresentationPoint> contour, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (contour.Count < 8) return new(false, null, DangoContourFailure.TooFewPoints);
        if (contour.Count > 4096 || contour.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            return new(false, null, DangoContourFailure.InvalidSampledBoundary);
        var area = contour.Select((point, index) =>
        {
            var next = contour[(index + 1) % contour.Count];
            return point.X * next.Y - point.Y * next.X;
        }).Sum();
        if (area <= 0) return new(false, null, DangoContourFailure.Reversed);
        var points = contour.Select(point => MapBelt(point, width, height)).ToArray();
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze();
        return new(true, geometry, DangoContourFailure.None);
    }

    internal static System.Windows.Point MapBelt(PresentationPoint point, double width, double height) =>
        new(width / 2 + point.X * width * 72 / 160, height * .55 - point.Y * width * 72 / 160);

    public static DangoGeometryResult TryCreate(
        IReadOnlyList<PresentationPoint> contour,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var failure = contour.Count < 8
            ? DangoContourFailure.TooFewPoints
            : DangoContourValidator.Validate(contour);
        if (failure != DangoContourFailure.None)
        {
            return new(false, null, failure);
        }

        var points = contour.Select(point => Map(point, width, height)).ToArray();
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(Midpoint(points[^1], points[0]), true, true);
            for (var index = 0; index < points.Length; index++)
            {
                var current = points[index];
                var next = points[(index + 1) % points.Length];
                // Closed quadratic B-spline: each arc stays inside its three
                // control points and joins the next with a continuous tangent.
                context.QuadraticBezierTo(current, Midpoint(current, next), true, true);
            }
        }
        geometry.Freeze();
        return new(true, geometry, DangoContourFailure.None);
    }

    private static System.Windows.Point Midpoint(System.Windows.Point first, System.Windows.Point second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    internal static System.Windows.Point Map(PresentationPoint point, double width, double height)
    {
        var mapped = Dango.Desktop.Physics.Presentation.Attachments.AttachmentShapeGeometry.MapToCanvas(point, width, height);
        return new(mapped.X, mapped.Y);
    }
}
