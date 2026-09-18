namespace Dango.Desktop.Rendering.Visuals;

internal enum QuietIdleAction { None, Blink, LookLeft, LookRight }
internal readonly record struct QuietIdleSample(bool Grounded, double X, double Y);

internal sealed class QuietIdleController(Func<int, int, int> next)
{
    private QuietIdleSample? previous;
    private double stableSeconds;
    private double clock;
    private double blinkAt;
    private double lookAt;
    private double activeSeconds;
    private double nextAllowed;
    private bool scheduled;
    internal QuietIdleAction Active { get; private set; }

    internal bool BeforeStep(QuietIdleSample sample, double seconds, bool eligible,
        Func<QuietIdleAction, bool> submit, Func<QuietIdleAction, bool> release)
    {
        if (!double.IsFinite(seconds) || seconds is <= 0 or > .1) throw new ArgumentOutOfRangeException(nameof(seconds));
        var finite = double.IsFinite(sample.X) && double.IsFinite(sample.Y);
        var vx = previous is { } last ? (sample.X - last.X) / seconds : double.PositiveInfinity;
        var vy = previous is { } lastY ? (sample.Y - lastY.Y) / seconds : double.PositiveInfinity;
        previous = finite ? sample : null;
        if (!eligible || !finite || !sample.Grounded || Math.Sqrt(vx * vx + vy * vy) > .15)
            return ResetSchedule(release);

        if (!scheduled)
        {
            stableSeconds += seconds;
            if (stableSeconds + 1e-9 < .5) return true;
            scheduled = true;
            blinkAt = Interval(4000, 8000);
            lookAt = Interval(15000, 25000);
            return true;
        }

        clock += seconds;
        if (Active != QuietIdleAction.None && activeSeconds + 1e-9 >= (Active == QuietIdleAction.Blink ? .13 : 1))
        {
            var finished = Active;
            Active = QuietIdleAction.None;
            nextAllowed = clock + .5;
            if (finished != QuietIdleAction.Blink && !release(finished)) return false;
        }

        var blinkDue = clock + 1e-9 >= blinkAt;
        var lookDue = clock + 1e-9 >= lookAt;
        if (!blinkDue && !lookDue) return true;
        if (blinkDue) blinkAt = clock + Interval(4000, 8000);
        if (lookDue) lookAt = clock + Interval(15000, 25000);
        if (Active != QuietIdleAction.None || clock + 1e-9 < nextAllowed) return true;
        var requested = lookDue ? (next(0, 2) == 0 ? QuietIdleAction.LookLeft : QuietIdleAction.LookRight) : QuietIdleAction.Blink;
        if (submit(requested)) { Active = requested; activeSeconds = 0; }
        return true;
    }

    internal void AfterStep(double seconds)
    {
        if (Active != QuietIdleAction.None) activeSeconds += seconds;
    }

    internal bool Cancel(Func<QuietIdleAction, bool> release)
    {
        previous = null;
        return ResetSchedule(release);
    }

    private bool ResetSchedule(Func<QuietIdleAction, bool> release)
    {
        var cancelled = Active;
        Active = QuietIdleAction.None;
        scheduled = false;
        stableSeconds = clock = blinkAt = lookAt = activeSeconds = nextAllowed = 0;
        return cancelled == QuietIdleAction.None || release(cancelled);
    }

    private double Interval(int min, int max) => next(min, max + 1) / 1000d;
}
