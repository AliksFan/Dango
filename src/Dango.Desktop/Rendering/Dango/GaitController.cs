namespace Dango.Desktop.Rendering.Visuals;

internal readonly record struct GaitSample(double X, double Y, bool Grounded, double Width, double Height,
    double Area, int Direction, bool Eligible = true, bool Unsafe = false, bool FastRelease = false,
    bool Autonomous = false);

internal sealed class GaitController
{
    private readonly double maximumCompression, maximumWidthDeviation;
    private GaitSample? previous;
    private double support, neutralTime, neutralWidth, neutralHeight, neutralArea;
    private double filteredSpeed, entering, releaseElapsed, releaseDuration;
    private int owner;
    internal double Strain { get; private set; }
    internal double Phase { get; private set; }
    internal string? Error { get; private set; }
    internal (double Width, double Height, double Area) NeutralDimensions => (neutralWidth, neutralHeight, neutralArea);

    internal GaitController(double maximumCompression = .08, double maximumWidthDeviation = .10)
    {
        if (!double.IsFinite(maximumCompression) || maximumCompression is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCompression));
        if (!double.IsFinite(maximumWidthDeviation) || maximumWidthDeviation is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(maximumWidthDeviation));
        this.maximumCompression = maximumCompression;
        this.maximumWidthDeviation = maximumWidthDeviation;
    }

    internal bool OutsideNeutralShape(double width, double height, double area) => neutralHeight > 0 &&
        (height < neutralHeight * (1 - maximumCompression) ||
         Math.Abs(width / neutralWidth - 1) > maximumWidthDeviation || Math.Abs(area / neutralArea - 1) > .10);

    internal double Advance(GaitSample sample, double seconds)
    {
        if (Error is not null) return 0;
        if (!double.IsFinite(seconds) || seconds is <= 0 or > .1 ||
            !double.IsFinite(sample.X) || !double.IsFinite(sample.Y) ||
            !double.IsFinite(sample.Width) || !double.IsFinite(sample.Height) || !double.IsFinite(sample.Area) ||
            sample.Width <= 0 || sample.Height <= 0 || sample.Area <= 0 || Math.Abs(sample.Direction) > 1)
        { Fail(); return 0; }
        var old = previous;
        previous = sample;
        var compressed = OutsideNeutralShape(sample.Width, sample.Height, sample.Area);
        if (sample.Unsafe || !sample.Grounded || compressed)
        { owner = 0; entering = filteredSpeed = support = 0; Strain = 0; releaseDuration = 0; return 0; }
        if (old is null) return Release(seconds, sample.FastRelease ? .12 : .3);
        var dx = sample.X - old.Value.X;
        var vx = dx / seconds;
        var vy = (sample.Y - old.Value.Y) / seconds;
        var supported = sample.Grounded && Math.Abs(vy) <= 12;
        support = supported ? support + seconds : 0;
        if (sample.Eligible && supported && !sample.Unsafe && sample.Direction == 0 &&
            Math.Abs(vx) <= 4 && Math.Abs(vy) <= 4 && Math.Abs(Strain) < 1e-10)
        {
            neutralTime += seconds;
            if (neutralTime + 1e-9 >= .5 && neutralHeight == 0)
            { neutralWidth = sample.Width; neutralHeight = sample.Height; neutralArea = sample.Area; }
        }
        else neutralTime = 0;
        if (!supported)
        { owner = 0; entering = filteredSpeed = support = 0; Strain = 0; releaseDuration = 0; return 0; }
        if (!sample.Eligible || sample.FastRelease || neutralHeight == 0 || support < .1 - 1e-9)
            return Release(seconds, sample.FastRelease ? .12 : .3);
        // A new input owner establishes its own displacement baseline, even when travelling the same way.
        if (sample.Autonomous != old.Value.Autonomous || sample.Direction != old.Value.Direction)
            return Release(seconds, .3);
        var progress = sample.Direction * dx;
        var speed = Math.Max(0, sample.Direction * vx);
        filteredSpeed += (speed - filteredSpeed) * (1 - Math.Exp(-seconds / .08));
        if (sample.Direction == 0 || progress <= 0 || speed < (owner == sample.Direction ? 4 : 8))
            return Release(seconds, .3);
        if (owner != 0 && owner != sample.Direction)
        { previous = null; return Release(seconds, .3); }
        if (owner == 0)
        {
            owner = sample.Direction;
            Phase = 0;
            entering = 0;
        }
        releaseDuration = releaseElapsed = 0;
        entering = Math.Min(.2, entering + seconds);
        Phase = (Phase + Math.Min(progress / 60, seconds * 2)) % 1;
        var target = Wave(Phase) * Math.Clamp(filteredSpeed / 40, 0, 1) * Smooth(entering / .2);
        Strain += Math.Clamp(target - Strain, -.09 * seconds, .09 * seconds);
        return Strain;
    }
    private double Release(double seconds, double duration)
    {
        owner = 0; entering = filteredSpeed = 0;
        if (releaseDuration == 0 || duration < releaseDuration)
        { releaseDuration = duration; releaseElapsed = 0; }
        var before = 1 - Smooth(releaseElapsed / releaseDuration);
        releaseElapsed = Math.Min(releaseDuration, releaseElapsed + seconds);
        var after = 1 - Smooth(releaseElapsed / releaseDuration);
        Strain = before <= 1e-12 ? 0 : Strain * after / before;
        return Strain;
    }
    private static double Smooth(double value) { var t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private static double Wave(double phase) => phase < .2 ? -.02 * Smooth(phase / .2) :
        phase < .55 ? -.02 + .03 * Smooth((phase - .2) / .35) : .01 * (1 - Smooth((phase - .55) / .45));
    internal void InvalidateSampling() { previous = null; owner = 0; support = neutralTime = filteredSpeed = entering = releaseDuration = 0; }
    internal void Reset() { InvalidateSampling(); Strain = Phase = neutralWidth = neutralHeight = neutralArea = 0; Error = null; }
    internal void ClearMotion() { var error = Error; Reset(); Error = error; }
    internal void Fail() { Reset(); Error = "InvalidGaitFrame"; }
    internal void AcceptDisplayed(double strain) => Strain = strain;
}
