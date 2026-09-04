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
            context.BeginFigure(points[0], true, true);
            for (var index = 0; index < points.Length; index++)
            {
                var previous = points[(index - 1 + points.Length) % points.Length];
                var current = points[index];
                var next = points[(index + 1) % points.Length];
                var afterNext = points[(index + 2) % points.Length];
                var firstControl = current + ((next - previous) / 6);
                var secondControl = next - ((afterNext - current) / 6);
                context.BezierTo(firstControl, secondControl, next, true, false);
            }
        }
        geometry.Freeze();
        return new(true, geometry, DangoContourFailure.None);
    }

    private static System.Windows.Point Map(PresentationPoint point, double width, double height) =>
        new(
            (width / 2) + (point.X * width * 0.45),
            (height * 0.55) - (point.Y * height * 0.48333333333333334));
}
