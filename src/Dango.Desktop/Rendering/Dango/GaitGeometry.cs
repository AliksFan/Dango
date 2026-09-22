using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Rendering.Visuals;

internal static class GaitGeometry
{
    internal static GaitPresentationFrame? Create(PhysicsPresentationFrame frame, DangoEntityId entity,
        LogicalRect bounds, LogicalRect viewport, double strain, bool autonomous, double hatSize)
    {
        if (strain == 0 || !frame.IsVisible || frame.FallbackReason != PhysicsPresentationFallbackReason.None ||
            frame.Attachments is not { SourceShape: { } shape, Hat.Pose: { } pose } attachment) return null;
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
        var eyeClearance = double.PositiveInfinity;
        foreach (var eye in eyes)
            foreach (var point in eye)
                for (var i = 0; i < body.Length; i++)
                    eyeClearance = Math.Min(eyeClearance, SegmentDistance(point, body[i], body[(i + 1) % body.Length]));
        var eyeGap = eyes.All(e => e.Length > 0) ? eyes[1].Min(p => p.X) - eyes[0].Max(p => p.X) : double.PositiveInfinity;
        GaitPresentationFrame Make(double factor) => new(entity, attachment.Generation, attachment.StepIndex,
            center, bottom, 1 - .5 * strain * factor, 1 + strain * factor);
        bool Safe(double factor)
        {
            var transform = Make(factor);
            if (!transform.IsValid) return false;
            foreach (var point in all)
            {
                var mapped = transform.Map(point);
                if (Distance(point, mapped) > 1.99) return false;
                // All supported hats fit inside the pre-existing overlay; never resize a host to fit gait.
                if (mapped.X < 0 || mapped.X > bounds.Width || mapped.Y < -16 || mapped.Y > bounds.Height) return false;
                if (mapped.X + bounds.X < viewport.X + (autonomous ? 16 : 0) ||
                    mapped.X + bounds.X > viewport.Right - (autonomous ? 16 : 0) ||
                    mapped.Y + bounds.Y < viewport.Y || mapped.Y + bounds.Y > viewport.Bottom) return false;
            }
            if (body.Any(p => { var q = transform.Map(p); return q.X < 0 || q.X > bounds.Width || q.Y < 0 || q.Y > bounds.Height; })) return false;
            if (eyeClearance * Math.Min(transform.ScaleX, transform.ScaleY) < 1.024 || eyeGap * transform.ScaleX < 2) return false;
            return true;
        }
        if (!Safe(0)) return null;
        if (Safe(1)) return Make(1);
        double lo = 0, hi = 1;
        for (var i = 0; i < 12; i++) { var mid = (lo + hi) / 2; if (Safe(mid)) lo = mid; else hi = mid; }
        return lo < 1e-6 ? null : Make(lo);
    }
    internal static LogicalPoint Canvas(AttachmentPoint p) => new(80 + p.X * 160, 66 - p.Y * 160);
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
    private static double SegmentDistance(LogicalPoint p, LogicalPoint a, LogicalPoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Distance(p, new(a.X + t * dx, a.Y + t * dy));
    }
}
