using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

/// <summary>
/// Candidate single-body sleep gate. All 36 generalized velocities and coordinates are checked,
/// together with actual core/curve motion: a quiet COM alone can never put a deforming body to sleep.
/// These freeze solver state; they never zero velocities or alter native contact caches.
/// </summary>
internal sealed class BeltSleepPolicy
{
    internal const int StableTicksRequired = 120;
    internal const double MaximumGeneralizedSpeed = .0005;
    internal const double MaximumTickDelta = .00001;
    internal const double MaximumWindowDelta = .0001;
    private double[]? previous, reference;
    private float[]? previousCurve, referenceCurve;
    private double previousCoreX, previousCoreY, referenceCoreX, referenceCoreY;
    private int stable;

    internal void Reset()
    {
        previous = reference = null; previousCurve = referenceCurve = null; stable = 0;
    }

    internal bool Observe(DangoBeltFrame frame, double[] state, DangoBeltInput input, bool healthy)
    {
        if (!healthy || Active(input) || !frame.Grounded || frame.ActiveContacts < 1 || state.Length != 200 ||
            state.Any(x => !double.IsFinite(x)) || state[82] != 1 || state[83] != 0 ||
            state[78] != 0 || state[79] > 0 ||
            Enumerable.Range(38, 36).Any(i => Math.Abs(state[i]) > MaximumGeneralizedSpeed))
        { Reset(); return false; }

        if (previous is null) { CaptureReference(); CapturePrevious(); return false; }
        var tickStable = Enumerable.Range(2, 36).All(i => Math.Abs(state[i] - previous[i]) <= MaximumTickDelta) &&
            frame.Curve.Zip(previousCurve!).All(x => Math.Abs(x.First - x.Second) <= MaximumTickDelta) &&
            Math.Abs(frame.CoreX - previousCoreX) <= MaximumTickDelta && Math.Abs(frame.CoreY - previousCoreY) <= MaximumTickDelta;
        var windowStable = Enumerable.Range(2, 36).All(i => Math.Abs(state[i] - reference![i]) <= MaximumWindowDelta) &&
            frame.Curve.Zip(referenceCurve!).All(x => Math.Abs(x.First - x.Second) <= MaximumWindowDelta) &&
            Math.Abs(frame.CoreX - referenceCoreX) <= MaximumWindowDelta && Math.Abs(frame.CoreY - referenceCoreY) <= MaximumWindowDelta;
        if (!tickStable || !windowStable) { stable = 0; CaptureReference(); }
        else stable++;
        CapturePrevious();
        return stable >= StableTicksRequired;

        void CaptureReference()
        {
            reference = (double[])state.Clone(); referenceCurve = (float[])frame.Curve.Clone();
            referenceCoreX = frame.CoreX; referenceCoreY = frame.CoreY;
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
