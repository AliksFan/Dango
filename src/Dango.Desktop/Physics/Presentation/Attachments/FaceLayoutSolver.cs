using System.Buffers;
using Dango.Core.Animation;

namespace Dango.Desktop.Physics.Presentation.Attachments;

public static class FaceLayoutSolver
{
    /// <summary>Saved base layout composed with the existing transient eye motion.</summary>
    public static FaceLayout Solve(AttachmentShapeGeometry shape, EyeMotionSample left, EyeMotionSample right,
        double angle, EyeLayoutParameters parameters, double horizontalRange = 5)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        // Keep the complete old search order and fallback path, including callers
        // that explicitly loaded all four default controls from appearance.json.
        if (parameters.IsDefault) return Solve(shape, left, right, angle, horizontalRange);
        Validate(left); Validate(right);
        if (!double.IsFinite(horizontalRange) || horizontalRange is <= 0 or > 7) throw new ArgumentOutOfRangeException(nameof(horizontalRange));
        if (!double.IsFinite(angle) || Math.Abs(angle) > Math.PI / 18 + 1e-10) throw new ArgumentOutOfRangeException(nameof(angle));

        var legacyY = Math.Clamp(1d / 160, shape.Bottom + .35 * shape.Height, shape.Bottom + .65 * shape.Height);
        var requestedY = legacyY - (parameters.HeightControlPercent - 51) / 100 * shape.Height;
        var requestedDistance = 38d / 160 + (parameters.SpacingControlPercent - 24.5) / 100 * shape.Width;
        var diameter = 12d / 160 * parameters.SizePercent / 100;
        var shortAxis = 1 - Math.Abs(parameters.OvalPercent) / 200;
        var width = diameter * (parameters.OvalPercent > 0 ? shortAxis : 1);
        var height = diameter * (parameters.OvalPercent < 0 ? shortAxis : 1);
        // A distorted contour may make a derived request impossible. Preserve the
        // controls and solve for a positive presentation distance instead.
        var distance = double.IsFinite(requestedDistance) && requestedDistance > 0 ? requestedDistance : width + 2d / 160 + 2 * EyeOutline.ChordError;
        // The requested height is first and is never clamped to the old 35–65%
        // strip. Alternatives are only attempted when the true outline is unsafe.
        var heights = new[] { requestedY, requestedY + .03 * shape.Height, requestedY - .03 * shape.Height,
            requestedY + .06 * shape.Height, requestedY - .06 * shape.Height,
            requestedY + .12 * shape.Height, requestedY - .12 * shape.Height, legacyY };
        var centers = heights.Distinct().Select(y => (y, interval: shape.CenterInterval(y)))
            .Where(value => value.interval.HasValue)
            .Select(value => new AttachmentPoint((value.interval!.Value.Left + value.interval.Value.Right) / 2, value.y)).ToArray();
        if (centers.Length == 0) centers = [new((shape.Left + shape.Right) / 2, legacyY)];

        FaceLayout Candidate(AttachmentPoint center, double gaze, double spacing, double size, bool closed = false)
        {
            EyePresentationFrame Eye(EyeMotionSample input, int side)
            {
                var local = new AttachmentPoint(side * distance / 2 * spacing + input.Gaze.X * horizontalRange / 160 * gaze,
                    input.Gaze.Y * 4d / 160 * gaze);
                var point = center + new AttachmentPoint(local.X * Math.Cos(angle) - local.Y * Math.Sin(angle),
                    local.X * Math.Sin(angle) + local.Y * Math.Cos(angle));
                return new(point, width * size, height * size, angle, closed ? 0 : input.Openness,
                    closed ? EyeShape.NaturalClosed : input.Shape, input.Strength, true, side > 0);
            }
            return new(Eye(left, -1), Eye(right, 1), gaze, spacing, size, "Normal");
        }

        FaceLayout? Try(double gaze, double spacing, double size, bool closed = false)
        {
            foreach (var center in centers)
            {
                var candidate = Candidate(center, gaze, spacing, size, closed);
                if (IsJointlySafe(shape, candidate)) return candidate;
            }
            return null;
        }

        var normal = Try(1, 1, 1);
        if (normal is not null) return normal;
        FaceLayout? Search(double minimum, Func<double, FaceLayout?> build, string reason)
        {
            var best = build(minimum);
            if (best is null) return null;
            var low = minimum; var high = 1d;
            for (var i = 0; i < 12; i++)
            {
                var mid = (low + high) / 2; var candidate = build(mid);
                if (candidate is null) high = mid; else { low = mid; best = candidate; }
            }
            return best with { Safety = reason };
        }
        var reduced = Search(0, gaze => Try(gaze, 1, 1), "GazeReduced") ??
            Search(.8, spacing => Try(0, spacing, 1), "SpacingReduced") ??
            Search(.7, size => Try(0, .8, size), "SizeReduced") ??
            Search(.35, size => Try(0, .6, size), "SizeReduced");
        if (reduced is not null) return reduced;
        var closed = Try(0, .6, .35, true);
        if (closed is not null) return closed with { Safety = "ClosedFallback" };
        var fallback = Candidate(centers[0], 0, .6, .35, true);
        // A per-eye containment result cannot establish the joint clearance.
        // Never emit two individually contained but overlapping fallback eyes.
        return fallback with { Left = fallback.Left with { Visible = false }, Right = fallback.Right with { Visible = false }, Safety = "EyeHidden" };
    }

    private static bool IsJointlySafe(AttachmentShapeGeometry shape, FaceLayout face)
    {
        var left = EyeOutline.Create(face.Left);
        if (!IsSafe(shape, left)) return false;
        var right = EyeOutline.Create(face.Right);
        // Exact open ellipses use curved WPF geometry, whereas the safety outline
        // uses chords. Reserve both outline errors in the final pair clearance.
        return IsSafe(shape, right) && right.Min(point => point.X) - left.Max(point => point.X) >= 2d / 160 + 2 * EyeOutline.ChordError;
    }

    public static FaceLayout Solve(AttachmentShapeGeometry shape, EyeMotionSample left, EyeMotionSample right, double angle, double horizontalRange = 5)
    {
        Validate(left); Validate(right);
        if (!double.IsFinite(horizontalRange) || horizontalRange is <= 0 or > 7) throw new ArgumentOutOfRangeException(nameof(horizontalRange));
        if (!double.IsFinite(angle) || Math.Abs(angle) > Math.PI / 18 + 1e-10) throw new ArgumentOutOfRangeException(nameof(angle));
        var y = Math.Clamp(1d / 160, shape.Bottom + 0.35 * shape.Height, shape.Bottom + 0.65 * shape.Height);
        var centers = new[] { y, Math.Min(y + shape.Height * 0.03, shape.Bottom + shape.Height * 0.65),
            Math.Max(y - shape.Height * 0.03, shape.Bottom + shape.Height * 0.35) }
            .Select(height => (height, interval: shape.CenterInterval(height)))
            .Where(v => v.interval.HasValue)
            .Select(v => new AttachmentPoint((v.interval!.Value.Left + v.interval.Value.Right) / 2, v.height)).ToArray();
        if (centers.Length == 0) centers = [new((shape.Left + shape.Right) / 2, y)];

        FaceLayout Candidate(AttachmentPoint center, double gaze, double spacing, double size, bool closed = false)
        {
            EyePresentationFrame Eye(EyeMotionSample input, int side)
            {
                var local = new AttachmentPoint(side * 19d / 160 * spacing + input.Gaze.X * horizontalRange / 160 * gaze, input.Gaze.Y * 4d / 160 * gaze);
                var point = center + new AttachmentPoint(local.X * Math.Cos(angle) - local.Y * Math.Sin(angle), local.X * Math.Sin(angle) + local.Y * Math.Cos(angle));
                var openness = closed ? 0 : input.Openness;
                return new(point, 12d / 160 * size, 12d / 160 * size, angle, openness,
                    closed ? EyeShape.NaturalClosed : input.Shape, input.Strength, true, side > 0);
            }
            return new(Eye(left, -1), Eye(right, 1), gaze, spacing, size, "Normal");
        }
        bool Safe(FaceLayout f)
        {
            var leftOutline = EyeOutline.Create(f.Left);
            if (!IsSafe(shape, leftOutline)) return false;
            var rightOutline = EyeOutline.Create(f.Right);
            return IsSafe(shape, rightOutline) && rightOutline.Min(p => p.X) - leftOutline.Max(p => p.X) >= 2d / 160;
        }
        FaceLayout? Try(double gaze, double spacing, double size)
        {
            foreach (var center in centers)
            {
                var candidate = Candidate(center, gaze, spacing, size);
                if (Safe(candidate)) return candidate;
            }
            return null;
        }
        var normal = Try(1, 1, 1);
        if (normal is not null) return normal;
        // Preserve targets. Search each reduction stage before reducing the next quantity.
        FaceLayout? Search(double minimum, Func<double, FaceLayout?> build, string reason)
        {
            var best = build(minimum);
            if (best is null) return null;
            var lo = minimum; var hi = 1d;
            for (var i = 0; i < 12; i++)
            {
                var mid = (lo + hi) / 2; var candidate = build(mid);
                if (candidate is null) hi = mid; else { lo = mid; best = candidate; }
            }
            return best with { Safety = reason };
        }
        var reduced = Search(0, g => Try(g, 1, 1), "GazeReduced") ??
            Search(0.8, s => Try(0, s, 1), "SpacingReduced") ??
            Search(0.7, s => Try(0, 0.8, s), "SizeReduced");
        if (reduced is not null) return reduced;
        foreach (var center in centers)
        {
            var closed = Candidate(center, 0, 0.8, 0.7, true);
            if (Safe(closed)) return closed with { Safety = "ClosedFallback" };
        }
        var fallback = Candidate(centers[0], 0, 0.8, 0.7, true);
        return fallback with
        {
            Left = fallback.Left with { Visible = IsSafe(shape, fallback.Left) },
            Right = fallback.Right with { Visible = IsSafe(shape, fallback.Right) },
            Safety = "EyeHidden"
        };
    }

    private static void Validate(EyeMotionSample value)
    {
        if (!double.IsFinite(value.Gaze.X) || !double.IsFinite(value.Gaze.Y) || Math.Abs(value.Gaze.X) > 1 || Math.Abs(value.Gaze.Y) > 1 ||
            !double.IsFinite(value.Openness) || value.Openness is < 0 or > 1 || !double.IsFinite(value.Strength) || value.Strength is < 0 or > 1 || !Enum.IsDefined(value.Shape))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    public static bool IsSafe(AttachmentShapeGeometry shape, EyePresentationFrame eye)
        => IsSafe(shape, EyeOutline.Create(eye));

    private static bool IsSafe(AttachmentShapeGeometry shape, IReadOnlyList<AttachmentPoint> outline)
    {
        const double margin = 1d / 160 + EyeOutline.ChordError + AttachmentShapeGeometry.MaximumChordError;
        var edges = shape.BoundaryEdges;
        int[]? rented = null;
        Span<int> selected = edges.Length <= 512 ? stackalloc int[edges.Length] :
            (rented = ArrayPool<int>.Shared.Rent(edges.Length));
        try
        {
            var count = SelectSafetyEdges(shape, outline, selected);
            foreach (var point in outline)
            {
                var inside = false;
                for (var i = 0; i < count; i++)
                {
                    ref readonly var segment = ref edges[selected[i]];
                    var a = segment.Start; var b = segment.End;
                    if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                    var edge = segment.Delta;
                    var lengthSquared = segment.LengthSquared;
                    var t = lengthSquared == 0 ? 0 : Math.Clamp((point - a).Dot(edge) / lengthSquared, 0, 1);
                    var difference = point - (a + edge * t);
                    if (difference.Dot(difference) < margin * margin) return false;
                }
                if (!inside) return false;
            }
            return true;
        }
        finally
        {
            if (rented is not null) ArrayPool<int>.Shared.Return(rented);
        }
    }

    internal static int SelectSafetyEdges(AttachmentShapeGeometry shape, IReadOnlyList<AttachmentPoint> outline, Span<int> selected)
    {
        const double margin = 1d / 160 + EyeOutline.ChordError + AttachmentShapeGeometry.MaximumChordError;
        var edges = shape.BoundaryEdges;
        var eligible = InDomain(shape.Left) && InDomain(shape.Right) && InDomain(shape.Bottom) && InDomain(shape.Top);
        var minY = double.PositiveInfinity; var maxY = double.NegativeInfinity;
        if (eligible)
            foreach (var point in outline)
            {
                if (!InDomain(point.X) || !InDomain(point.Y)) { eligible = false; break; }
                minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
            }
        if (!eligible || outline.Count == 0)
        {
            for (var i = 0; i < edges.Length; i++) selected[i] = i;
            return edges.Length;
        }

        // In this finite +/-16 domain, Delta and the projection numerator cannot
        // overflow, and the original clamped t is in [0,1] (or zero for a zero
        // squared length). Include the actual rounded Start + Delta endpoint.
        // 1e-9 is far above the <1e-14 Y multiply/add/residual roundoff here;
        // it only expands candidates and never changes the strict margin test.
        var lower = Math.BitDecrement(minY - margin - 1e-9);
        var upper = Math.BitIncrement(maxY + margin + 1e-9);
        var count = 0;
        for (var i = 0; i < edges.Length; i++)
        {
            ref readonly var edge = ref edges[i];
            var projectedEndY = edge.Start.Y + edge.Delta.Y;
            var bottom = Math.Min(edge.Start.Y, Math.Min(edge.End.Y, projectedEndY));
            var top = Math.Max(edge.Start.Y, Math.Max(edge.End.Y, projectedEndY));
            // Omitted edges have both original endpoints on one side of every
            // outline point (no ray crossing), and every projected Y is farther
            // than the unchanged clearance margin. Retain original edge order.
            if (top >= lower && bottom <= upper) selected[count++] = i;
        }
        return count;

        static bool InDomain(double coordinate) => double.IsFinite(coordinate) && Math.Abs(coordinate) <= 16;
    }
}
