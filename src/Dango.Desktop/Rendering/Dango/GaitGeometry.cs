using Dango.Core.Coordinates;
using System.Runtime.CompilerServices;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Rendering.Visuals;

internal static class GaitGeometry
{
    // This per-frame nested geometry loop must start with optimized code rather
    // than waiting for tier promotion while the user is already walking.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static GaitPresentationFrame? Create(PhysicsPresentationFrame frame, DangoEntityId entity,
        LogicalRect bounds, LogicalRect viewport, double strain, bool autonomous, double hatSize,
        bool clipToTemplate = true)
        => CreateBuilder(frame, entity, bounds, viewport, autonomous, hatSize, clipToTemplate).Create(strain);

    /// <summary>A caller-owned, single-frame workspace. Preparation remains lazy for zero strain.</summary>
    internal static Builder CreateBuilder(PhysicsPresentationFrame frame, DangoEntityId entity,
        LogicalRect bounds, LogicalRect viewport, bool autonomous, double hatSize, bool clipToTemplate = true)
        => new(frame, entity, bounds, viewport, autonomous, hatSize, clipToTemplate);

    internal sealed class Builder(PhysicsPresentationFrame frame, DangoEntityId entity,
        LogicalRect bounds, LogicalRect viewport, bool autonomous, double hatSize, bool clipToTemplate)
    {
        private bool preparationAttempted;
        private Prepared? prepared;

        private sealed record Prepared(double BodySizeScale, LogicalPoint[] Body, double Center,
            double Bottom, LogicalPoint[] All, double EyeClearance, double EyeGap,
            AttachmentPresentationFrame Attachment);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private Prepared? Prepare()
        {
            if (!frame.IsVisible || frame.FallbackReason != PhysicsPresentationFallbackReason.None ||
                frame.Attachments is not { SourceShape: { } shape, Hat.Pose: { } pose } attachment) return null;
            // Geometry and the returned transform stay in the fixed 160x120 template.
            // The host applies body size afterwards; every safety gate uses screen DIP.
            var bodySizeScale = bounds.Width / 160;
            if (!double.IsFinite(bodySizeScale) || bodySizeScale <= 0) return null;
            var body = shape.Boundary.Select(Canvas).ToArray();
            var center = (shape.Left + shape.Right) * 80 + 80;
            var bottom = 66 - shape.Bottom * 160;
            var eyes = new[] { attachment.Face.Left, attachment.Face.Right }
                .Select(eye => eye.Visible ? EyeOutline.Create(eye).Select(Canvas).ToArray() : []).ToArray();
            var hat = new List<LogicalPoint>();
            var pad = 23 * HatBodyFollow.DeformationLimit;
            foreach (var x in new[] { 53 - pad, 107 + pad })
                foreach (var y in new[] { -1 - pad, 28 + pad })
                {
                    var dx = (x - 80) * hatSize; var dy = (y - 25) * hatSize;
                    hat.Add(new(80 + pose.Center.X * 160 + dx * Math.Cos(pose.Angle) + dy * Math.Sin(pose.Angle),
                        66 - pose.Center.Y * 160 - dx * Math.Sin(pose.Angle) + dy * Math.Cos(pose.Angle)));
                }
            var all = body.Concat(eyes.SelectMany(e => e)).Concat(hat).ToArray();
            if (eyes.SelectMany(e => e).Any(point => !Contains(body, point))) return null;
            // A diagonal positive scale cannot reduce any point-to-segment distance by more
            // than its smallest scale. Compute the expensive original clearance only once.
            var eyeClearanceSquared = MinimumEyeClearanceSquared(body, eyes);
            // Square root is monotone, so the same minimum needs only one root.
            // Keep the original squared-distance arithmetic and every sample.
            var eyeClearance = Math.Sqrt(eyeClearanceSquared);
            var eyeGap = eyes.All(e => e.Length > 0) ? eyes[1].Min(p => p.X) - eyes[0].Max(p => p.X) : double.PositiveInfinity;
            return new(bodySizeScale, body, center, bottom, all, eyeClearance, eyeGap, attachment);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        internal GaitPresentationFrame? Create(double strain)
        {
            if (strain == 0) return null;
            if (!preparationAttempted)
            {
                prepared = Prepare();
                preparationAttempted = true;
            }
            if (prepared is null) return null;
            var (bodySizeScale, body, center, bottom, all, eyeClearance, eyeGap, attachment) = prepared;
            GaitPresentationFrame Make(double factor) => new(entity, attachment.Generation, attachment.StepIndex,
                center, bottom, 1 - .5 * strain * factor, 1 + strain * factor);
            bool Safe(double factor)
            {
                var transform = Make(factor);
                if (!transform.IsValid) return false;
                foreach (var point in all)
                {
                    var mapped = transform.Map(point);
                    if (Distance(point, mapped) * bodySizeScale > 1.99) return false;
                    var screenX = mapped.X * bodySizeScale;
                    var screenY = mapped.Y * bodySizeScale;
                    // The extra hat overlay remains 16 screen DIP at every body size.
                    // Unclipped laboratory layers can contain a core that has shifted
                    // within the body. Their actual clipping boundary is the viewport.
                    if (clipToTemplate && (screenX < 0 || screenX > bounds.Width || screenY < -16 || screenY > bounds.Height)) return false;
                    if (screenX + bounds.X < viewport.X + (autonomous ? 16 : 0) ||
                        screenX + bounds.X > viewport.Right - (autonomous ? 16 : 0) ||
                        screenY + bounds.Y < viewport.Y || screenY + bounds.Y > viewport.Bottom) return false;
                }
                if (clipToTemplate && body.Any(p => { var q = transform.Map(p); return q.X < 0 || q.X * bodySizeScale > bounds.Width || q.Y < 0 || q.Y * bodySizeScale > bounds.Height; })) return false;
                if (eyeClearance * bodySizeScale * Math.Min(transform.ScaleX, transform.ScaleY) < 1.024 || eyeGap * bodySizeScale * transform.ScaleX < 2) return false;
                return true;
            }
            if (!Safe(0)) return null;
            if (Safe(1)) return Make(1);
            double lo = 0, hi = 1;
            for (var i = 0; i < 12; i++) { var mid = (lo + hi) / 2; if (Safe(mid)) lo = mid; else hi = mid; }
            return lo < 1e-6 ? null : Make(lo);
        }
    }
    internal static LogicalPoint Canvas(AttachmentPoint p) => new(80 + p.X * 160, 66 - p.Y * 160);
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool Contains(LogicalPoint[] polygon, LogicalPoint point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
    private static double Distance(LogicalPoint a, LogicalPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private const double MaximumBoundCoordinate = 1_000_000;
    private readonly record struct EyeBounds(double Left, double Top, double Right, double Bottom, bool Eligible);
    private readonly record struct Segment(LogicalPoint Start, double Dx, double Dy, double LengthSquared, double LowerBoundSquared);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static double MinimumEyeClearanceSquared(LogicalPoint[] body, LogicalPoint[][] eyes)
    {
        var eyeBounds = Bounds(eyes);
        Span<Segment> segments = body.Length <= 128
            ? stackalloc Segment[body.Length]
            : new Segment[body.Length];
        for (var index = 0; index < body.Length; index++)
        {
            var a = body[index]; var b = body[(index + 1) % body.Length];
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var length = dx * dx + dy * dy;
            segments[index] = new(a, dx, dy, length,
                ClearanceLowerBoundSquared(eyeBounds, a, dx, dy, length));
        }
        var best = double.PositiveInfinity;
        foreach (var eye in eyes)
            foreach (var point in eye)
                foreach (ref readonly var segment in segments)
                {
                    // The bound covers every actual eye point, and is deliberately
                    // expanded. Equality still executes the original projection.
                    if (segment.LowerBoundSquared > best) continue;
                    best = Math.Min(best, SegmentDistanceSquared(point, in segment));
                }
        return best;
    }

    internal static double ClearanceLowerBoundSquared(LogicalPoint point, LogicalPoint start,
        double dx, double dy, double lengthSquared) =>
        ClearanceLowerBoundSquared(new EyeBounds(point.X, point.Y, point.X, point.Y,
            InBoundDomain(point.X) && InBoundDomain(point.Y)), start, dx, dy, lengthSquared);

    internal static bool CanSkipClearanceSegment(LogicalPoint point, LogicalPoint start,
        double dx, double dy, double lengthSquared, double bestSquared) =>
        double.IsFinite(bestSquared) && bestSquared >= 0 &&
        ClearanceLowerBoundSquared(point, start, dx, dy, lengthSquared) > bestSquared;

    private static bool InBoundDomain(double value) => double.IsFinite(value) && Math.Abs(value) <= MaximumBoundCoordinate;

    private static EyeBounds Bounds(LogicalPoint[][] eyes)
    {
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (var eye in eyes)
            foreach (var point in eye)
            {
                if (!InBoundDomain(point.X) || !InBoundDomain(point.Y)) return default;
                left = Math.Min(left, point.X); right = Math.Max(right, point.X);
                top = Math.Min(top, point.Y); bottom = Math.Max(bottom, point.Y);
            }
        return new(left, top, right, bottom, left != double.PositiveInfinity);
    }

    private static double ClearanceLowerBoundSquared(EyeBounds eyes, LogicalPoint start,
        double dx, double dy, double lengthSquared)
    {
        // This bounded finite domain prevents the original projection numerator
        // from overflowing to inf - inf. Outside it, execute every original edge.
        if (!eyes.Eligible || !InBoundDomain(start.X) || !InBoundDomain(start.Y) ||
            !InBoundDomain(dx) || !InBoundDomain(dy) || !double.IsFinite(lengthSquared) || lengthSquared < 0)
            return 0;
        // t = 1 evaluates Start + Delta, which can differ from the supplied end.
        // Rounded multiply/add remains inside these actual computed endpoints.
        var endX = start.X + dx; var endY = start.Y + dy;
        var scale = Math.Max(1, Math.Max(Math.Max(Math.Abs(start.X), Math.Abs(start.Y)),
            Math.Max(Math.Max(Math.Abs(endX), Math.Abs(endY)),
                Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)),
                    Math.Max(Math.Max(Math.Abs(eyes.Left), Math.Abs(eyes.Top)),
                        Math.Max(Math.Abs(eyes.Right), Math.Abs(eyes.Bottom)))))));
        var padding = scale * 1e-9;
        var left = Math.BitDecrement(Math.Min(start.X, endX) - padding);
        var right = Math.BitIncrement(Math.Max(start.X, endX) + padding);
        var top = Math.BitDecrement(Math.Min(start.Y, endY) - padding);
        var bottom = Math.BitIncrement(Math.Max(start.Y, endY) + padding);
        var x = Gap(eyes.Left - padding, eyes.Right + padding, left, right);
        var y = Gap(eyes.Top - padding, eyes.Bottom + padding, top, bottom);
        // Keep Pow, including the sign of a separated interval's residual. The
        // geometric padding is far larger than adjacent-double rounding errors.
        var lower = Math.Pow(x, 2) + Math.Pow(y, 2);
        return !double.IsFinite(lower) || lower <= 0 ? 0 : Math.Max(0, Math.BitDecrement(lower));

        static double Gap(double eyeMinimum, double eyeMaximum, double segmentMinimum, double segmentMaximum)
        {
            if (eyeMaximum < segmentMinimum) return Math.BitIncrement(eyeMaximum - segmentMinimum);
            if (eyeMinimum > segmentMaximum) return Math.BitDecrement(eyeMinimum - segmentMaximum);
            return 0;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SegmentDistanceSquared(LogicalPoint p, in Segment segment)
    {
        var a = segment.Start; var dx = segment.Dx; var dy = segment.Dy; var length = segment.LengthSquared;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        var q = new LogicalPoint(a.X + t * dx, a.Y + t * dy);
        return Math.Pow(p.X - q.X, 2) + Math.Pow(p.Y - q.Y, 2);
    }
}
