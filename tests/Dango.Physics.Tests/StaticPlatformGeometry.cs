namespace Dango.Physics.Tests;

// Independent measurement of the public render contract. Contour already includes
// the node-radius expansion performed by read_frame; expanding it again is wrong.
internal static class StaticPlatformGeometry
{
    internal readonly record struct Point(double X, double Y);
    internal const double ContactBudget = 1d / 30;
    private const int SamplesPerNode = 8;

    internal static Point[] PaintedContour(DangoBodyState body)
    {
        if (body.PointCount != 16 || body.Contour.Count != 32 || body.Contour.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("A valid sixteen-node public body frame is required.");
        var maximum = body.Contour.Max(v => Math.Abs(v));
        var scale = maximum > .98 ? .98 / maximum : 1;
        return Enumerable.Range(0, 16 * SamplesPerNode).Select(sample =>
        {
            var node = sample / SamplesPerNode;
            var t = (sample % SamplesPerNode) / (double)SamplesPerNode;
            var before = .5 * (1 - t) * (1 - t);
            var middle = .5 + t - t * t;
            var after = .5 * t * t;
            double Coordinate(int axis) => before * body.Contour[((node + 15) % 16) * 2 + axis]
                + middle * body.Contour[node * 2 + axis] + after * body.Contour[((node + 1) % 16) * 2 + axis];
            return new Point(body.CoreX + Coordinate(0) * scale * .9,
                body.CoreY + Coordinate(1) * scale * (58d / 60));
        }).ToArray();
    }

    // Measure along the platform's finite top span, including crossings between
    // sampled curve points. The unrestricted lowest node is not an edge oracle.
    internal static double TopGap(DangoBodyState body, DangoStaticPlatformDefinition platform)
    {
        var contour = PaintedContour(body);
        var points = new List<Point>();
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i]; var b = contour[(i + 1) % contour.Length];
            if (a.X >= platform.Left && a.X <= platform.Right) points.Add(a);
            foreach (var boundary in new[] { platform.Left, platform.Right })
            {
                if ((a.X < boundary && b.X > boundary) || (b.X < boundary && a.X > boundary))
                {
                    var ratio = (boundary - a.X) / (b.X - a.X);
                    points.Add(new(boundary, a.Y + ratio * (b.Y - a.Y)));
                }
            }
        }
        return points.Count == 0 ? double.PositiveInfinity : points.Min(p => p.Y) - platform.Top;
    }

    // SAT over the painted convex boundary and the independent rectangle. This
    // also detects intersecting edges when no sampled vertex lies in the box.
    internal static double Penetration(DangoBodyState body, DangoStaticPlatformDefinition platform)
    {
        var contour = PaintedContour(body);
        Point[] rectangle = [new(platform.Left, platform.Bottom), new(platform.Right, platform.Bottom),
            new(platform.Right, platform.Top), new(platform.Left, platform.Top)];
        var axes = new List<Point> { new(1, 0), new(0, 1) };
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i]; var b = contour[(i + 1) % contour.Length];
            var x = b.X - a.X; var y = b.Y - a.Y;
            var length = Math.Sqrt(x * x + y * y);
            if (length > 1e-12) axes.Add(new(-y / length, x / length));
        }
        var minimum = double.PositiveInfinity;
        foreach (var axis in axes)
        {
            var bodyProjection = contour.Select(p => p.X * axis.X + p.Y * axis.Y).ToArray();
            var boxProjection = rectangle.Select(p => p.X * axis.X + p.Y * axis.Y).ToArray();
            var overlap = Math.Min(bodyProjection.Max() - boxProjection.Min(), boxProjection.Max() - bodyProjection.Min());
            if (overlap <= 0) return 0;
            minimum = Math.Min(minimum, overlap);
        }
        return minimum;
    }
}
