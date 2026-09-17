using Dango.Core.Coordinates;

namespace Dango.Desktop.Rendering.Visuals;

internal static class DangoHostLayout
{
    public static LogicalPoint FindSpawn(LogicalRect viewport, IReadOnlyList<LogicalRect> occupied)
    {
        viewport = ContentArea(viewport);
        const double width = 160;
        const double height = 120;
        var candidates = new List<LogicalRect>();
        foreach (var body in occupied)
        {
            candidates.Add(new(body.Right, body.Y, width, height));
            candidates.Add(new(body.X - width, body.Y, width, height));
            candidates.Add(new(body.X, body.Y - height - 12, width, height));
        }
        for (var y = viewport.Bottom - height; y >= viewport.Y; y -= height)
            for (var x = viewport.X; x + width <= viewport.Right; x += width)
                candidates.Add(new(x, y, width, height));
        var available = candidates.Where(candidate => viewport.Contains(candidate) &&
            occupied.All(body => body.IntersectionArea(candidate) < 1e-8)).ToArray();
        if (available.Length == 0)
            throw new InvalidOperationException("当前可见区域没有安全出生位置；请放大窗口或调整团子位置后重试。");
        return available[0].Center;
    }

    public static LogicalVector FitTranslation(LogicalRect viewport, IReadOnlyList<LogicalRect> bodies)
    {
        if (bodies.Count == 0) return default;
        viewport = ContentArea(viewport);
        var left = bodies.Min(b => b.X); var right = bodies.Max(b => b.Right);
        var top = bodies.Min(b => b.Y); var bottom = bodies.Max(b => b.Bottom);
        // Translation only: no node teleportation, scaling, or physics boundary change.
        var x = right - left <= viewport.Width
            ? Math.Clamp(0, viewport.X - left, viewport.Right - right)
            : viewport.Center.X - (left + right) / 2;
        var y = bottom - top <= viewport.Height
            ? Math.Clamp(0, viewport.Y - top, viewport.Bottom - bottom)
            : viewport.Center.Y - (top + bottom) / 2;
        return new(x, y);
    }

    private static LogicalRect ContentArea(LogicalRect viewport)
    {
        var margin = Math.Min(8, Math.Min(viewport.Width, viewport.Height) / 4);
        return new(viewport.X + margin, viewport.Y + margin, viewport.Width - margin * 2, viewport.Height - margin * 2);
    }
}
