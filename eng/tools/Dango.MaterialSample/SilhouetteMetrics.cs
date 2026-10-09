using System.Windows;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.MaterialSample;

internal sealed record SilhouetteMetrics(double Aspect, double WidestHeight,
    IReadOnlyList<double> SectionWidths, double BottomCoverage, double MeanReferenceError, double MaxReferenceError)
{
    // Body only, extracted from the user-approved image; leaves, eyes and background excluded.
    internal static readonly double[] Heights = [.10, .25, .50, .65, .80, .90, .97];
    internal static readonly double[] ReferenceWidths = [.5438, .7915, .9728, 1, .9366, .7915, .5619];
    internal const double ReferenceAspect = 331d / 221;
    internal bool MatchesReference => Math.Abs(Aspect - ReferenceAspect) <= .03 &&
        WidestHeight is >= .60 and <= .70 && MeanReferenceError <= .04 && MaxReferenceError <= .08 &&
        BottomCoverage is >= .43 and <= .57;

    internal static SilhouetteMetrics Measure(DangoBodyState state, bool productionMapping = false)
    {
        return Measure(SampleGeometry.Create(state, productionMapping));
    }

    internal static SilhouetteMetrics Measure(IReadOnlyList<PresentationPoint> points, bool productionMapping = false)
    {
        var result = DangoStreamGeometryFactory.TryCreate(points,
            productionMapping ? 160 : 1 / .45, productionMapping ? 120 : 1 / .48333333333333334);
        if (!result.Succeeded || result.Geometry is null)
            throw new InvalidOperationException($"真实身体轮廓无效：{result.Failure}");
        return Measure(result.Geometry);
    }

    private static SilhouetteMetrics Measure(Geometry geometry)
    {
        var bounds = geometry.Bounds;
        var flattened = geometry.GetFlattenedPathGeometry(.0001, ToleranceType.Absolute);
        var vertices = new List<Point>();
        foreach (var figure in flattened.Figures)
        {
            vertices.Add(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment polyline) vertices.AddRange(polyline.Points);
                else if (segment is LineSegment line) vertices.Add(line.Point);
                else throw new InvalidOperationException("轮廓展开包含非线段。");
            }
        }
        double WidthAt(double normalizedHeight)
        {
            var y = bounds.Top + normalizedHeight * bounds.Height;
            var intersections = new List<double>();
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i]; var b = vertices[(i + 1) % vertices.Count];
                if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    intersections.Add(a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y));
            }
            return intersections.Count >= 2 ? (intersections.Max() - intersections.Min()) / bounds.Width : 0;
        }
        var sections = Heights.Select(WidthAt).ToArray();
        var errors = sections.Zip(ReferenceWidths, (actual, expected) => Math.Abs(actual - expected)).ToArray();
        var widestHeight = Enumerable.Range(1, 99).Select(i => (Height: i / 100d, Width: WidthAt(i / 100d)))
            .MaxBy(p => p.Width).Height;
        return new(bounds.Width / bounds.Height, widestHeight, sections, WidthAt(.977), errors.Average(), errors.Max());
    }
}

// Same geometry path for the UI and gates. Normalize only to satisfy the presentation
// coordinate contract, then compensate in the canvas dimensions so size is unchanged.
internal static class SampleGeometry
{
    internal static Geometry Create(DangoBodyState state, bool productionMapping = false, double scale = 1)
    {
        var normalization = Math.Max(1, state.Contour.Max(value => Math.Abs((double)value)));
        var points = Enumerable.Range(0, state.PointCount).Select(i => new PresentationPoint(
            state.Contour[2 * i] / normalization, state.Contour[2 * i + 1] / normalization)).ToArray();
        var width = (productionMapping ? 160 : 1 / .45) * scale * normalization;
        var height = (productionMapping ? 120 : 1 / .48333333333333334) * scale * normalization;
        var result = DangoStreamGeometryFactory.TryCreate(points, width, height);
        if (!result.Succeeded || result.Geometry is null)
            throw new InvalidOperationException($"真实身体轮廓无效：{result.Failure}");
        var geometry = result.Geometry.Clone();
        geometry.Transform = new TranslateTransform(-width / 2, -height * .55);
        geometry.Freeze();
        return geometry;
    }
}
