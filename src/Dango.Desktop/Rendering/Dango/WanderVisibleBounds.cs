using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Rendering.Visuals;

internal static class WanderVisibleBounds
{
    internal static LogicalRect? Get(PhysicsPresentationFrame frame, LogicalRect body, double hatSize)
    {
        if (frame.FallbackReason != PhysicsPresentationFallbackReason.None || !frame.IsVisible ||
            frame.Contour.Count < 8 || frame.Attachments?.Hat?.Pose is not { } pose) return null;
        // The rendered quadratic body curve stays inside its control-point hull.
        var points = frame.Diagnostics.Backend == PhysicsPresentationBackend.ContinuousBelt
            ? frame.Contour.Select(point =>
            {
                var mapped = DangoStreamGeometryFactory.MapBelt(point, body.Width, body.Height);
                return new AttachmentPoint(mapped.X, mapped.Y);
            }).ToList()
            : frame.Contour.Select(p => AttachmentShapeGeometry.MapToCanvas(p, body.Width, body.Height)).ToList();
        // Conservative hull of every supported hat's Bezier control points, expanded
        // for maximum material bend/stretch (23 * .06), before the actual pose transform.
        // Thus no WPF visual or additional geometry allocation is needed on each tick.
        var pad = 23 * HatBodyFollow.DeformationLimit;
        var cosine = Math.Cos(pose.Angle); var sine = Math.Sin(pose.Angle);
        foreach (var x in new[] { 53 - pad, 107 + pad })
            foreach (var y in new[] { -1 - pad, 28 + pad })
            {
                var dx = (x - 80) * hatSize; var dy = (y - 25) * hatSize;
                points.Add(new(80 + pose.Center.X * 160 + dx * cosine + dy * sine,
                    66 - pose.Center.Y * 160 - dx * sine + dy * cosine));
            }
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return null;
        // Body stroke/antialias margin in addition to the convex hull.
        var left = points.Min(p => p.X) - 2; var top = points.Min(p => p.Y) - 2;
        return new(body.X + left, body.Y + top, points.Max(p => p.X) + 2 - left, points.Max(p => p.Y) + 2 - top);
    }
}
