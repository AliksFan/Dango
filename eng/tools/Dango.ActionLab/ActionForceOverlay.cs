using System.Windows;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Physics;

namespace Dango.ActionLab;

internal sealed record ActionForceArrow(DangoActiveForce Force, Point Start, Point End);

/// <summary>Read-only display of the successful native step's last-substep ledger.</summary>
internal sealed class ActionForceOverlay : FrameworkElement
{
    internal const double DipPerNewton = 90;
    internal DangoActiveForceFrame? Frame { get; private set; }
    internal IReadOnlyList<ActionForceArrow> Arrows { get; private set; } = Array.Empty<ActionForceArrow>();

    internal ActionForceOverlay()
    {
        Width = 520; Height = 340;
        IsHitTestVisible = false;
        Focusable = false;
    }

    internal void Show(DangoActiveForceFrame frame, Func<double, double, LogicalPoint> toHost)
    {
        var arrows = new List<ActionForceArrow>();
        foreach (var force in frame.Forces)
        {
            if (!double.IsFinite(force.X) || !double.IsFinite(force.Y) ||
                !double.IsFinite(force.ForceX) || !double.IsFinite(force.ForceY))
                throw new InvalidOperationException("主动施力数据包含无效数值。");
            var magnitude = Math.Sqrt(force.ForceX * force.ForceX + force.ForceY * force.ForceY);
            if (magnitude == 0) continue;
            var origin = toHost(force.X, force.Y);
            var tip = toHost(force.X + force.ForceX, force.Y + force.ForceY);
            var direction = new Vector(tip.X - origin.X, tip.Y - origin.Y);
            if (!double.IsFinite(direction.Length) || direction.Length == 0)
                throw new InvalidOperationException("主动施力坐标转换无效。");
            direction.Normalize();
            var start = new Point(origin.X, origin.Y);
            // Conversion supplies orientation only. Force magnitude always uses
            // the same linear scale; no frame normalization or visual inference.
            arrows.Add(new(force, start, start + direction * (magnitude * DipPerNewton)));
        }
        Frame = frame;
        Arrows = arrows.AsReadOnly();
        InvalidateVisual();
    }

    internal void Clear()
    {
        Frame = null;
        Arrows = Array.Empty<ActionForceArrow>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var ink = (Brush)FindResource("HomeAccent");
        var pen = new Pen(ink, 1.15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawing.PushOpacity(.82);
        foreach (var arrow in Arrows)
        {
            var vector = arrow.End - arrow.Start;
            var length = vector.Length;
            vector.Normalize();
            var normal = new Vector(-vector.Y, vector.X);
            var head = Math.Min(5, length * .3);
            drawing.DrawLine(pen, arrow.Start, arrow.End);
            drawing.DrawLine(pen, arrow.End, arrow.End - vector * head + normal * head * .5);
            drawing.DrawLine(pen, arrow.End, arrow.End - vector * head - normal * head * .5);
            drawing.DrawEllipse(ink, null, arrow.Start, 1.2, 1.2);
        }
        drawing.Pop();
    }
}
