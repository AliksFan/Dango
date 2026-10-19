using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

/// <summary>
/// Supported equilibrium, not a low-COM-speed shortcut. Every generalized q and v, the entire
/// displayed curve, and the core must remain in the frozen 120-tick envelope. Contact projection
/// can leave a constant generalized v residual while accepted positions remain stationary; the
/// separately approved trend contract preserves that residual, including its exact bytes.
/// </summary>
internal sealed class BeltSleepPolicy
{
    internal const int StableTicksRequired = 120;
    internal const double MaximumVelocityTickDelta = .0005;
    internal const double MaximumVelocityWindowDelta = .0005;
    internal const double MaximumTickDelta = .00001;
    internal const double MaximumWindowDelta = .0001;
    private double[]? previous, minimum, maximum;
    private float[]? previousCurve, minimumCurve, maximumCurve;
    private double previousCoreX, previousCoreY, minimumCoreX, minimumCoreY, maximumCoreX, maximumCoreY;
    private int stable;

    internal void Reset()
    {
        previous = minimum = maximum = null;
        previousCurve = minimumCurve = maximumCurve = null; stable = 0;
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
            var windowBound = i < 38 ? MaximumWindowDelta : MaximumVelocityWindowDelta;
            inEnvelope &= Math.Abs(state[i] - previous[i]) <= tickBound &&
                Math.Max(maximum![i], state[i]) - Math.Min(minimum![i], state[i]) <= windowBound;
        }
        for (var i = 0; i < frame.Curve.Length; i++)
            inEnvelope &= Math.Abs(frame.Curve[i] - previousCurve![i]) <= MaximumTickDelta &&
                Math.Max(maximumCurve![i], frame.Curve[i]) - Math.Min(minimumCurve![i], frame.Curve[i]) <= MaximumWindowDelta;
        inEnvelope &= Math.Abs(frame.CoreX - previousCoreX) <= MaximumTickDelta && Math.Abs(frame.CoreY - previousCoreY) <= MaximumTickDelta &&
            Math.Max(maximumCoreX, frame.CoreX) - Math.Min(minimumCoreX, frame.CoreX) <= MaximumWindowDelta &&
            Math.Max(maximumCoreY, frame.CoreY) - Math.Min(minimumCoreY, frame.CoreY) <= MaximumWindowDelta;

        if (!inEnvelope) { stable = 0; CaptureEnvelope(); }
        else
        {
            stable++;
            for (var i = 2; i < 74; i++) { minimum![i] = Math.Min(minimum[i], state[i]); maximum![i] = Math.Max(maximum[i], state[i]); }
            for (var i = 0; i < frame.Curve.Length; i++)
            { minimumCurve![i] = Math.Min(minimumCurve[i], frame.Curve[i]); maximumCurve![i] = Math.Max(maximumCurve[i], frame.Curve[i]); }
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
        }
        void CapturePrevious()
        {
            previous = (double[])state.Clone(); previousCurve = (float[])frame.Curve.Clone();
            previousCoreX = frame.CoreX; previousCoreY = frame.CoreY;
        }
    }

    internal static bool Active(DangoBeltInput input) => input.InputActive || input.Horizontal != 0 ||
        input.Stretch != 0 || input.Downward != 0 || input.JumpPressed || input.DragActive || input.CancelForces;
}
