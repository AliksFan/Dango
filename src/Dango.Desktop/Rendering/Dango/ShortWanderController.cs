namespace Dango.Desktop.Rendering.Visuals;

internal readonly record struct WanderSample(double X, double Y, bool Grounded, double LeftRoom, double RightRoom, double ScreenWidth = 1200);

internal sealed class ShortWanderController
{
    internal Func<int, int, int> Next { get; set; } = Random.Shared.Next;
    private WanderSample? previous;
    private double stable, waiting, remaining, elapsed, startX;
    private double target, maximumDriveSeconds;
    private bool scheduled;
    internal bool Active { get; private set; }
    internal bool Driving { get; private set; }
    internal int Direction { get; private set; }
    internal double Input(double seconds) => Driving ? Direction * .35 * Math.Min(1, (elapsed + seconds) / .2) : 0;

    internal void Advance(WanderSample sample, double seconds, bool eligible, bool eyesBusy)
    {
        if (!double.IsFinite(seconds) || seconds is <= 0 or > .1) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!eligible || !double.IsFinite(sample.X) || !double.IsFinite(sample.Y) ||
            !double.IsFinite(sample.LeftRoom) || !double.IsFinite(sample.RightRoom) ||
            !double.IsFinite(sample.ScreenWidth) || sample.ScreenWidth <= 0 ||
            sample.LeftRoom < 0 || sample.RightRoom < 0 || !sample.Grounded)
        { Cancel(); return; }
        var speed = previous is { } p ? Math.Sqrt(Math.Pow((sample.X - p.X) / seconds, 2) + Math.Pow((sample.Y - p.Y) / seconds, 2)) : double.PositiveInfinity;
        previous = sample;
        if (Driving)
        {
            elapsed += seconds;
            var distance = Direction * (sample.X - startX) * 80;
            if (distance + 1e-6 >= target || elapsed + 1e-9 >= maximumDriveSeconds ||
                (Direction < 0 ? sample.LeftRoom : sample.RightRoom) <= 32 ||
                elapsed + 1e-9 >= .5 && distance < 2)
            { Driving = false; stable = 0; }
            return;
        }
        if (speed > .15)
        { stable = 0; scheduled = false; waiting = 0; return; }
        if (!scheduled)
        {
            stable += seconds;
            if (stable + 1e-9 < .5) return;
            Active = false; Direction = 0;
            scheduled = true; remaining = Next(8000, 15001) / 1000d; waiting = 0;
            return;
        }
        waiting += seconds;
        if (waiting + 1e-9 < remaining) return;
        waiting = 0;
        if (eyesBusy) { remaining = Next(8000, 15001) / 1000d; return; }
        // Draw afresh for each attempt; leave a small physical stopping margin inside the range.
        var fraction = Next(1000, 2001) / 10000d;
        var margin = Math.Min(8, sample.ScreenWidth * .01);
        target = Math.Clamp(sample.ScreenWidth * fraction, sample.ScreenWidth * .1 + margin, sample.ScreenWidth * .2 - margin);
        maximumDriveSeconds = 2 + target / 20;
        var left = sample.LeftRoom >= target + 32;
        var right = sample.RightRoom >= target + 32;
        if (!left && !right) { remaining = Next(8000, 15001) / 1000d; return; }
        Direction = left && right ? (Next(0, 2) == 0 ? -1 : 1) : left ? -1 : 1;
        startX = sample.X; elapsed = stable = 0; scheduled = false;
        Active = Driving = true;
    }

    internal void Cancel()
    {
        previous = null; Active = Driving = scheduled = false;
        stable = waiting = remaining = elapsed = startX = target = maximumDriveSeconds = 0; Direction = 0;
    }
}
