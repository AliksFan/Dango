using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

/// <summary>
/// Supported equilibrium, not a low-COM-speed shortcut. Every generalized q and v, the entire
/// displayed curve, and the core must remain in the frozen 120-tick envelope. Contact projection
/// can leave a constant generalized v residual while accepted positions remain stationary; the
/// separately approved trend contract preserves that residual, including its exact bytes.
/// </summary>
internal enum BeltSleepProfile { StrictStateTrendV1, VisibleDeformationV2 }

internal sealed class BeltSleepPolicy(BeltSleepProfile profile = BeltSleepProfile.VisibleDeformationV2)
{
    internal const int StableTicksRequired = 120;
    internal const double MaximumVelocityTickDelta = .0005;
    internal const double MaximumVelocityWindowDelta = .0005;
    internal const double MaximumTickDelta = .00001;
    internal const double MaximumWindowDelta = .0001;
    internal const double MaximumVisibleWindowDisplacement = .0005;
    internal string ContractId => profile == BeltSleepProfile.StrictStateTrendV1 ? "strict-state-trend-v1" : "visible-deformation-v2";
    internal double MaximumCoordinateWindowDelta => profile == BeltSleepProfile.StrictStateTrendV1 ? MaximumWindowDelta : MaximumVisibleWindowDisplacement;
    private double[]? previous, minimum, maximum;
    private double[]? minimumPresentation, maximumPresentation;
    private float[]? previousCurve, minimumCurve, maximumCurve;
    private double previousCoreX, previousCoreY, minimumCoreX, minimumCoreY, maximumCoreX, maximumCoreY;
    private int stable;

    internal void Reset()
    {
        previous = minimum = maximum = null;
        previousCurve = minimumCurve = maximumCurve = null; stable = 0;
        minimumPresentation = maximumPresentation = null;
    }

    internal bool Observe(DangoBeltFrame frame, double[] state, DangoBeltInput input, bool healthy)
    {
        if (!healthy || Active(input) || !frame.Grounded || frame.ActiveContacts < 1 || state.Length != 200 ||
            state.Any(x => !double.IsFinite(x)) || state[82] != 1 || state[83] != 0 || state[78] != 0 ||
            // Snapshot79 is elapsed since jump, not time remaining. Snapshot125 is
            // cfg.jump_cooldown_seconds (config scalars start89, cooldown scalar36).
            state[79] < state[125])
        { Reset(); return false; }

        if (previous is null) { CaptureEnvelope(); CapturePrevious(); return false; }
        var inEnvelope = true;
        for (var i = 2; i < 74; i++)
        {
            var tickBound = i < 38 ? MaximumTickDelta : MaximumVelocityTickDelta;
            var windowBound = i < 38 ? MaximumCoordinateWindowDelta : MaximumVelocityWindowDelta;
            inEnvelope &= Math.Abs(state[i] - previous[i]) <= tickBound &&
                Math.Max(maximum![i], state[i]) - Math.Min(minimum![i], state[i]) <= windowBound;
        }
        for (var i = 0; i < frame.Curve.Length; i++)
            inEnvelope &= Math.Abs(frame.Curve[i] - previousCurve![i]) <= MaximumTickDelta;
        inEnvelope &= Math.Abs(frame.CoreX - previousCoreX) <= MaximumTickDelta && Math.Abs(frame.CoreY - previousCoreY) <= MaximumTickDelta;
        var coreRangeX = Math.Max(maximumCoreX, frame.CoreX) - Math.Min(minimumCoreX, frame.CoreX);
        var coreRangeY = Math.Max(maximumCoreY, frame.CoreY) - Math.Min(minimumCoreY, frame.CoreY);
        if (profile == BeltSleepProfile.StrictStateTrendV1)
        {
            inEnvelope &= coreRangeX <= MaximumWindowDelta && coreRangeY <= MaximumWindowDelta;
            for (var i = 0; i < frame.Curve.Length; i++)
                inEnvelope &= Math.Max(maximumCurve![i], frame.Curve[i]) - Math.Min(minimumCurve![i], frame.Curve[i]) <= MaximumWindowDelta;
        }
        else
        {
            inEnvelope &= WithinVisibleEnvelope(coreRangeX, coreRangeY);
            for (var i = 0; i < frame.Curve.Length; i += 2)
            {
                inEnvelope &= WithinVisibleEnvelope(
                    Math.Max(maximumCurve![i], frame.Curve[i]) - Math.Min(minimumCurve![i], frame.Curve[i]),
                    Math.Max(maximumCurve[i + 1], frame.Curve[i + 1]) - Math.Min(minimumCurve[i + 1], frame.Curve[i + 1]));
                // Product framing follows core X, while screen Y uses raw curve Y.
                // Raw curve/core gates alone cannot bound this relative camera coordinate.
                var x = frame.Curve[i] - frame.CoreX; var y = (double)frame.Curve[i + 1];
                inEnvelope &= WithinVisibleEnvelope(
                    Math.Max(maximumPresentation![i], x) - Math.Min(minimumPresentation![i], x),
                    Math.Max(maximumPresentation[i + 1], y) - Math.Min(minimumPresentation[i + 1], y));
            }
        }

        if (!inEnvelope) { stable = 0; CaptureEnvelope(); }
        else
        {
            stable++;
            for (var i = 2; i < 74; i++) { minimum![i] = Math.Min(minimum[i], state[i]); maximum![i] = Math.Max(maximum[i], state[i]); }
            for (var i = 0; i < frame.Curve.Length; i++)
            { minimumCurve![i] = Math.Min(minimumCurve[i], frame.Curve[i]); maximumCurve![i] = Math.Max(maximumCurve[i], frame.Curve[i]); }
            if (profile == BeltSleepProfile.VisibleDeformationV2)
                for (var i = 0; i < frame.Curve.Length; i++)
                {
                    var value = i % 2 == 0 ? frame.Curve[i] - frame.CoreX : frame.Curve[i];
                    minimumPresentation![i] = Math.Min(minimumPresentation[i], value);
                    maximumPresentation![i] = Math.Max(maximumPresentation[i], value);
                }
            minimumCoreX = Math.Min(minimumCoreX, frame.CoreX); maximumCoreX = Math.Max(maximumCoreX, frame.CoreX);
            minimumCoreY = Math.Min(minimumCoreY, frame.CoreY); maximumCoreY = Math.Max(maximumCoreY, frame.CoreY);
        }
        CapturePrevious();
        return stable >= StableTicksRequired;

        void CaptureEnvelope()
        {
            minimum = (double[])state.Clone(); maximum = (double[])state.Clone();
            minimumCurve = (float[])frame.Curve.Clone(); maximumCurve = (float[])frame.Curve.Clone();
            minimumCoreX = maximumCoreX = frame.CoreX; minimumCoreY = maximumCoreY = frame.CoreY;
            if (profile == BeltSleepProfile.VisibleDeformationV2)
            {
                minimumPresentation = new double[frame.Curve.Length]; maximumPresentation = new double[frame.Curve.Length];
                for (var i = 0; i < frame.Curve.Length; i++)
                    minimumPresentation[i] = maximumPresentation[i] = i % 2 == 0 ? frame.Curve[i] - frame.CoreX : frame.Curve[i];
            }
        }
        void CapturePrevious()
        {
            previous = (double[])state.Clone(); previousCurve = (float[])frame.Curve.Clone();
            previousCoreX = frame.CoreX; previousCoreY = frame.CoreY;
        }
    }

    internal static bool WithinVisibleEnvelope(double rangeX, double rangeY) =>
        Math.Sqrt(rangeX * rangeX + rangeY * rangeY) <= MaximumVisibleWindowDisplacement;

    internal static bool Active(DangoBeltInput input) => input.InputActive || input.Horizontal != 0 ||
        input.Stretch != 0 || input.Downward != 0 || input.JumpPressed || input.DragActive || input.CancelForces;
}
