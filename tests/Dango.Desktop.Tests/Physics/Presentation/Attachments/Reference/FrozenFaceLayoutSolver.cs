using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

// Frozen verbatim from 8f0ca91; only namespace, accessibility, and solver name differ.
namespace Dango.Desktop.Tests.Physics.Presentation.Attachments.Reference;

internal static class FrozenFaceLayoutSolver
{
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
        bool Safe(FaceLayout f) => IsSafe(shape, f.Left) && IsSafe(shape, f.Right) &&
            EyeOutline.Create(f.Right).Min(p => p.X) - EyeOutline.Create(f.Left).Max(p => p.X) >= 2d / 160;
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
    {
        const double margin = 1d / 160 + EyeOutline.ChordError + AttachmentShapeGeometry.MaximumChordError;
        foreach (var point in EyeOutline.Create(eye))
        {
            var inside = false;
            for (var i = 0; i < shape.Boundary.Count; i++)
            {
                var a = shape.Boundary[i]; var b = shape.Boundary[(i + 1) % shape.Boundary.Count];
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                var edge = b - a;
                var lengthSquared = edge.Dot(edge);
                var t = lengthSquared == 0 ? 0 : Math.Clamp((point - a).Dot(edge) / lengthSquared, 0, 1);
                var difference = point - (a + edge * t);
                if (difference.Dot(difference) < margin * margin) return false;
            }
            if (!inside) return false;
        }
        return true;
    }
}
