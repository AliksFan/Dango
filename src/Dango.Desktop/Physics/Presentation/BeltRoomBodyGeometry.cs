using Dango.Core.Coordinates;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

internal readonly record struct BodySupportEnvelope(double Left, double Right, double Height);

// Shared native continuous-curve attachment geometry for room gloves and desktop dragging.
internal static class BeltRoomBodyGeometry
{
    // Bound every harmonic for all theta, including unsampled continuous extrema.
    internal static BodySupportEnvelope? SupportEnvelope(DangoBeltRoomFrame? frame)
    {
        if (frame is not { Fault: 0 } || frame.BodySnapshot.Count != 200 ||
            frame.BodySnapshot[0] != 1 || frame.BodySnapshot[1] != 200) return null;
        var q = frame.BodySnapshot;
        if (q.Skip(2).Take(36).Any(value => !double.IsFinite(value))) return null;
        double rx = 0, ry = 0;
        for (var harmonic = 1; harmonic <= 8; harmonic++)
        {
            var i = 2 + 4 * harmonic - 2;
            rx += Math.Sqrt(q[i] * q[i] + q[i + 2] * q[i + 2]);
            ry += Math.Sqrt(q[i + 1] * q[i + 1] + q[i + 3] * q[i + 3]);
        }
        const double margin = .001;
        var offset = (q[2] - q[36]) * 72;
        var result = new BodySupportEnvelope(offset - rx * 72 - margin, offset + rx * 72 + margin, 2 * ry * 72 + 2 * margin);
        return double.IsFinite(result.Left) && double.IsFinite(result.Right) && double.IsFinite(result.Height) &&
            result.Left < result.Right && result.Height > 0 ? result : null;
    }

    // Stats[12] is the continuous-extremum floor gap. Platform/ball support is excluded.
    internal static bool HasFloorSupport(DangoBeltRoomFrame? frame) =>
        frame is { Fault: 0, Tick: > 0 } && frame.BodySnapshot.Count == 200 && frame.Stats.Count >= 26 &&
        frame.Diagnostics.Count >= 20 && frame.BodySnapshot[82] == 1 && frame.Stats[14] == 1 &&
        frame.Stats[25] > 0 && frame.Diagnostics[18] == 0 && frame.Diagnostics[19] == 0 &&
        double.IsFinite(frame.Stats[12]) && Math.Abs(frame.Stats[12]) <= .003;

    internal static LogicalPoint DragAttachment(DangoBeltRoomFrame frame)
    {
        // Native belt drag attaches to the continuous outer curve, with no legacy node-radius contraction.
        // Read the frozen theta while dragging; otherwise choose the top of the same displayed curve.
        var q = frame.BodySnapshot;
        var theta = q[83] != 0 ? q[80] : Enumerable.Range(0, 256).MaxBy(i => frame.Curve[i * 2 + 1]) * (2 * Math.PI / 256);
        if (q[83] == 0)
        {
            // Refine the sampled maximum onto the continuous stationary point selected by native.
            // Once the drag begins, its frozen native theta is the authority even as the body deforms.
            for (var iteration = 0; iteration < 12; iteration++)
            {
                double derivative = 0, secondDerivative = 0;
                for (var n = 1; n <= 8; n++)
                {
                    var c = q[4 * n + 1]; var s = q[4 * n + 3];
                    derivative += n * (-c * Math.Sin(n * theta) + s * Math.Cos(n * theta));
                    secondDerivative -= n * n * (c * Math.Cos(n * theta) + s * Math.Sin(n * theta));
                }
                if (Math.Abs(secondDerivative) < 1e-12) break;
                var step = Math.Clamp(derivative / secondDerivative, -Math.PI / 128, Math.PI / 128);
                theta -= step;
                if (Math.Abs(step) < 1e-12) break;
            }
        }
        double x = q[2], y = q[3];
        for (var n = 1; n <= 8; n++)
        {
            var i = 4 * n;
            x += q[i] * Math.Cos(n * theta) + q[i + 2] * Math.Sin(n * theta);
            y += q[i + 1] * Math.Cos(n * theta) + q[i + 3] * Math.Sin(n * theta);
        }
        return new(x, y);
    }

}
