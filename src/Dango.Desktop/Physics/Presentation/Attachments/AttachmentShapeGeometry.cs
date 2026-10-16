namespace Dango.Desktop.Physics.Presentation.Attachments;

public readonly record struct AttachmentPoint(double X, double Y)
{
    public static AttachmentPoint operator +(AttachmentPoint a, AttachmentPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static AttachmentPoint operator -(AttachmentPoint a, AttachmentPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static AttachmentPoint operator *(AttachmentPoint a, double scale) => new(a.X * scale, a.Y * scale);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(AttachmentPoint other) => X * other.X + Y * other.Y;
}
public sealed record ShapeTilt(double Angle, double Confidence, string Reason);
internal readonly record struct AttachmentBoundaryEdge(AttachmentPoint Start, AttachmentPoint End,
    AttachmentPoint Delta, double LengthSquared);

public sealed class AttachmentShapeGeometry
{
    // Tighter than the 0.0005 gate, leaving room for contact-fit error.
    public const double MaximumChordError = 0.00005;
    private readonly AttachmentPoint[] controls;
    private readonly bool sampledBoundary;
    private readonly AttachmentBoundaryEdge[] boundaryEdges;
    private AttachmentShapeGeometry(AttachmentPoint[] controls, AttachmentPoint[] boundary, bool sampledBoundary = false)
    {
        this.controls = controls;
        this.sampledBoundary = sampledBoundary;
        Boundary = Array.AsReadOnly(boundary);
        boundaryEdges = new AttachmentBoundaryEdge[boundary.Length];
        for (var index = 0; index < boundary.Length; index++)
        {
            var start = boundary[index]; var end = boundary[(index + 1) % boundary.Length];
            var delta = end - start;
            boundaryEdges[index] = new(start, end, delta, delta.Dot(delta));
        }
        Left = boundary.Min(p => p.X); Right = boundary.Max(p => p.X);
        Bottom = boundary.Min(p => p.Y); Top = boundary.Max(p => p.Y);
    }
    public IReadOnlyList<AttachmentPoint> Boundary { get; }
    internal ReadOnlySpan<AttachmentBoundaryEdge> BoundaryEdges => boundaryEdges;
    public double Left { get; }
    public double Right { get; }
    public double Bottom { get; }
    public double Top { get; }
    public double Width => Right - Left;
    public double Height => Top - Bottom;

    public static AttachmentPoint MapToCanvas(PresentationPoint point, double width, double height) =>
        new(width / 2 + point.X * width * 0.45, height * 0.55 - point.Y * width * 0.45);

    public static AttachmentShapeGeometry Create(IReadOnlyList<PresentationPoint> contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (contour.Count is < 8 or > 128) throw new ArgumentException("Attachment contour count is invalid.", nameof(contour));
        var controls = contour.Select(p => new AttachmentPoint(p.X * 72 / 160, p.Y * 72 / 160)).ToArray();
        var boundary = new List<AttachmentPoint>();
        for (var i = 0; i < controls.Length; i++)
        {
            var a = (controls[(i + controls.Length - 1) % controls.Length] + controls[i]) * 0.5;
            var b = controls[i]; var c = (b + controls[(i + 1) % controls.Length]) * 0.5;
            Flatten(a, b, c, boundary);
        }
        var area = boundary.Select((a, i) => { var b = boundary[(i + 1) % boundary.Count]; return a.X * b.Y - a.Y * b.X; }).Sum();
        if (area <= 1e-10 || boundary.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new ArgumentException("Attachment contour is degenerate or reversed.", nameof(contour));
        for (var i = 0; i < boundary.Count; i++)
            for (var j = i + 2; j < boundary.Count; j++)
            {
                if (i == 0 && j == boundary.Count - 1) continue;
                if (Intersects(boundary[i], boundary[(i + 1) % boundary.Count], boundary[j], boundary[(j + 1) % boundary.Count]))
                    throw new ArgumentException("Attachment contour intersects itself.", nameof(contour));
            }
        // Canonical geometric origin: never use a material-node index as identity.
        var start = Enumerable.Range(0, boundary.Count).OrderBy(i => boundary[i].X).ThenBy(i => boundary[i].Y).First();
        return new(controls, boundary.Skip(start).Concat(boundary.Take(start)).ToArray());
    }

    /// <summary>
    /// Accepts an already sampled, counterclockwise closed boundary in reference-width units
    /// (one unit is 160 DIP). No spline fitting, resampling or vertex displacement occurs.
    /// The closing edge is implicit; do not repeat the first point at the end.
    /// </summary>
    public static AttachmentShapeGeometry FromBoundary(IReadOnlyList<AttachmentPoint> boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        if (boundary.Count is < 3 or > 4096)
            throw new ArgumentException("Sampled attachment boundary count is invalid.", nameof(boundary));
        var points = boundary.ToArray();
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1024 || Math.Abs(p.Y) > 1024))
            throw new ArgumentException("Sampled attachment boundary coordinates are invalid.", nameof(boundary));
        var area = 0d;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            if ((b - a).Length <= 1e-12)
                throw new ArgumentException("Sampled attachment boundary has a zero-length edge.", nameof(boundary));
            area += a.X * b.Y - a.Y * b.X;
        }
        if (area <= 1e-10)
            throw new ArgumentException("Sampled attachment boundary is degenerate or reversed.", nameof(boundary));
        for (var i = 0; i < points.Length; i++)
            for (var j = i + 2; j < points.Length; j++)
            {
                if (i == 0 && j == points.Length - 1) continue;
                if (SegmentsTouch(points[i], points[(i + 1) % points.Length], points[j], points[(j + 1) % points.Length]))
                    throw new ArgumentException("Sampled attachment boundary intersects itself.", nameof(boundary));
            }
        return new(points, points, sampledBoundary: true);
    }

    private static bool SegmentsTouch(AttachmentPoint a, AttachmentPoint b, AttachmentPoint c, AttachmentPoint d)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) - 1e-12 || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) - 1e-12 ||
            Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) - 1e-12 || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y) - 1e-12) return false;
        static double Cross(AttachmentPoint p, AttachmentPoint q, AttachmentPoint r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        static bool On(AttachmentPoint p, AttachmentPoint q, AttachmentPoint r) =>
            Math.Abs(Cross(p, q, r)) <= 1e-14 && r.X >= Math.Min(p.X, q.X) - 1e-12 &&
            r.X <= Math.Max(p.X, q.X) + 1e-12 && r.Y >= Math.Min(p.Y, q.Y) - 1e-12 && r.Y <= Math.Max(p.Y, q.Y) + 1e-12;
        return Intersects(a, b, c, d) || On(a, b, c) || On(a, b, d) || On(c, d, a) || On(c, d, b);
    }

    private static void Flatten(AttachmentPoint a, AttachmentPoint b, AttachmentPoint c, List<AttachmentPoint> points)
    {
        if (points.Count >= 1024) throw new ArgumentException("Attachment sampling capacity exceeded.");
        if ((a - b * 2 + c).Length / 4 <= MaximumChordError)
        { points.Add(a); return; }
        var ab = (a + b) * 0.5; var bc = (b + c) * 0.5; var middle = (ab + bc) * 0.5;
        Flatten(a, ab, middle, points); Flatten(middle, bc, c, points);
    }

    public AttachmentPoint Evaluate(int segment, double t)
    {
        if (segment < 0 || segment >= controls.Length || !double.IsFinite(t) || t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(segment));
        if (sampledBoundary) return controls[segment] * (1 - t) + controls[(segment + 1) % controls.Length] * t;
        var b = controls[segment];
        var a = (controls[(segment + controls.Length - 1) % controls.Length] + b) * 0.5;
        var c = (b + controls[(segment + 1) % controls.Length]) * 0.5;
        return a * ((1 - t) * (1 - t)) + b * (2 * (1 - t) * t) + c * (t * t);
    }

    public (double Left, double Right)? CenterInterval(double y)
    {
        var intersections = new List<double>();
        for (var i = 0; i < Boundary.Count; i++)
        {
            var a = Boundary[i]; var b = Boundary[(i + 1) % Boundary.Count];
            if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                intersections.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
        }
        intersections.Sort();
        var center = (Left + Right) / 2;
        for (var i = 0; i + 1 < intersections.Count; i += 2)
            if (intersections[i] <= center && intersections[i + 1] >= center) return (intersections[i], intersections[i + 1]);
        return null;
    }

    public ShapeTilt EstimateTilt()
    {
        List<AttachmentPoint> Centers(double[] heights) => heights.Select(h => Bottom + h * Height)
            .Select(y => (y, interval: CenterInterval(y))).Where(p => p.interval.HasValue)
            .Select(p => new AttachmentPoint((p.interval!.Value.Left + p.interval.Value.Right) / 2, p.y)).ToList();
        var main = Centers([0.45, 0.55, 0.65, 0.75, 0.85]);
        var crown = Centers([0.70, 0.78, 0.86, 0.94]);
        if (main.Count < 3 || crown.Count < 3 || Width <= 1e-8 || Height <= 1e-8) return new(0, 0, "InsufficientSections");
        var (slope, residual) = FitCenters(main);
        var (auxiliary, _) = FitCenters(crown);
        var difference = Math.Abs(Math.Atan(slope) - Math.Atan(auxiliary)) * 180 / Math.PI;
        var agreement = Math.Clamp((15 - difference) / 10, 0, 1);
        var confidence = main.Count / 5d * Math.Clamp(1 - residual / (0.04 * Width), 0, 1) * agreement;
        var deadbandSlope = Math.CopySign(Math.Max(0, Math.Abs(slope) - 0.02), slope);
        return new(confidence * Math.Clamp(Math.Atan(deadbandSlope), -Math.PI / 18, Math.PI / 18), confidence,
            agreement == 0 ? "ConflictingSections" : Math.Abs(slope) <= 0.02 ? "NeutralDeadband" : "ShapeSections");
    }

    private static (double Slope, double Residual) FitCenters(List<AttachmentPoint> points)
    {
        var x = points.Average(p => p.X); var y = points.Average(p => p.Y);
        var yy = points.Sum(p => (p.Y - y) * (p.Y - y));
        var slope = points.Sum(p => (p.X - x) * (p.Y - y)) / yy;
        return (slope, Math.Sqrt(points.Average(p => Math.Pow(p.X - x - slope * (p.Y - y), 2))));
    }

    private static bool Intersects(AttachmentPoint a, AttachmentPoint b, AttachmentPoint c, AttachmentPoint d)
    {
        static double Cross(AttachmentPoint p, AttachmentPoint q, AttachmentPoint r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        return Cross(a, b, c) * Cross(a, b, d) < -1e-16 && Cross(c, d, a) * Cross(c, d, b) < -1e-16;
    }
}
