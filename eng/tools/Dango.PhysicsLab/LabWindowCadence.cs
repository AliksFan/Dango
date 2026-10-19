namespace Dango.PhysicsLab;

internal sealed record LabWindowCadenceFrame(double ElapsedMilliseconds, double IntervalMilliseconds, int Step,
    int StepsSinceCallback, bool Active, double DiscardedSeconds);

internal sealed record LabWindowCadenceResult(bool Passed, int WarmupRestSteps, int Samples,
    int MinimumRolling1sCallbacks, int MinimumRolling1sAdvancedPhysicsFrames,
    double? CallbackIntervalP95Milliseconds, double? DiscardedSeconds, string Criterion);

internal static class LabWindowCadence
{
    // Treat floating-point timestamp noise within 0.1 ns as simultaneous. Windows
    // are left-open/right-closed: callbacks at t - 1000 ms have already expired.
    private const double TimestampEpsilonMilliseconds = 1e-7;

    internal static LabWindowCadenceResult Summarize(IReadOnlyList<LabWindowCadenceFrame> samples, int startStep)
    {
        // Keep cold samples in the original report. Sustained walking begins
        // after the explicit first 300 resting steps of the fixed script.
        var frames = samples.Where(f => f.Step - startStep >= 300).ToArray();
        var left = 0; var right = 0; var poses = 0; var minimumFrames = int.MaxValue; var minimumPoses = int.MaxValue;
        if (frames.Length > 0 && frames[^1].ElapsedMilliseconds - frames[0].ElapsedMilliseconds >= 1000 - TimestampEpsilonMilliseconds)
        {
            var firstComplete = frames[0].ElapsedMilliseconds + 1000;
            var last = frames[^1].ElapsedMilliseconds;
            // Counts change on both arrival and expiry. Checking arrivals alone
            // misses the lowest cadence during a gap just before the next callback.
            var boundaries = frames.Select(f => f.ElapsedMilliseconds)
                .Concat(frames.Select(f => f.ElapsedMilliseconds + 1000))
                .Append(firstComplete).Append(last)
                .Where(time => time >= firstComplete - TimestampEpsilonMilliseconds && time <= last + TimestampEpsilonMilliseconds)
                .Order();
            foreach (var time in boundaries)
            {
                while (right < frames.Length && frames[right].ElapsedMilliseconds <= time + TimestampEpsilonMilliseconds)
                {
                    if (frames[right].StepsSinceCallback > 0) poses++;
                    right++;
                }
                while (left < right && frames[left].ElapsedMilliseconds <= time - 1000 + TimestampEpsilonMilliseconds)
                {
                    if (frames[left].StepsSinceCallback > 0) poses--;
                    left++;
                }
                minimumFrames = Math.Min(minimumFrames, right - left);
                minimumPoses = Math.Min(minimumPoses, poses);
            }
        }
        var ordered = frames.Select(f => f.IntervalMilliseconds).Order().ToArray();
        var p95 = ordered.Length == 0 ? double.PositiveInfinity : ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
        var drop = frames.Length < 2 ? double.PositiveInfinity : frames[^1].DiscardedSeconds - frames[0].DiscardedSeconds;
        if (minimumFrames == int.MaxValue) minimumFrames = 0;
        if (minimumPoses == int.MaxValue) minimumPoses = 0;
        var passed = minimumFrames >= 57 && minimumPoses >= 57 && p95 <= 1000d / 57 && drop == 0;
        return new(passed, 300, frames.Length, minimumFrames, minimumPoses,
            double.IsFinite(p95) ? p95 : null, double.IsFinite(drop) ? drop : null,
            "After rest300 warmup: every complete rolling1s (t-1000ms,t] >=57 rendering callbacks AND >=57 frames with a new successful physics tick, checked at arrivals and expiries (timestamp epsilon1e-7ms); callback P95<=1000/57ms; no discarded simulation backlog. Cold samples retained. Not GPU present telemetry.");
    }
}
