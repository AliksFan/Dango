using Dango.Core.Coordinates;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

// Shared native continuous-curve attachment geometry for room gloves and desktop dragging.
internal static class BeltRoomBodyGeometry
{
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
