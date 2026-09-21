namespace Dango.Desktop.DesktopHost;

internal readonly record struct HostFrameBatch(int Steps, long DroppedTicks, bool Reset, bool InvalidClock);

internal sealed class HostFrameClock(long frequency)
{
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);
    internal const int MaximumSteps = 6;
    private readonly long frequency = frequency > 0 ? frequency : throw new ArgumentOutOfRangeException(nameof(frequency));
    private long previous;
    private Int128 fractionalTicks;
    internal long RemainderTicks { get; private set; }
    internal void Start(long timestamp) { previous = timestamp; RemainderTicks = 0; fractionalTicks = 0; }
    internal void Stop() { RemainderTicks = 0; fractionalTicks = 0; }
    internal HostFrameBatch Advance(long timestamp)
    {
        var delta = (Int128)timestamp - previous;
        previous = timestamp;
        if (delta < 0 || timestamp < 0)
        {
            var lost = RemainderTicks;
            Stop();
            return new(0, lost, true, true);
        }
        if (delta == 0) return default;
        var scaled = delta * TimeSpan.TicksPerSecond + fractionalTicks;
        var ticks = (long)(scaled / frequency);
        fractionalTicks = scaled % frequency;
        if (delta >= frequency)
        {
            var lost = ticks + RemainderTicks;
            Stop();
            return new(0, lost, true, false);
        }
        var total = ticks + RemainderTicks;
        var available = total / FixedStep.Ticks;
        var steps = (int)Math.Min(MaximumSteps, available);
        RemainderTicks = total % FixedStep.Ticks;
        return new(steps, (available - steps) * FixedStep.Ticks, false, false);
    }
    internal long Discard(int unexecutedSteps)
    {
        var lost = unexecutedSteps * FixedStep.Ticks + RemainderTicks;
        RemainderTicks = 0;
        fractionalTicks = 0;
        return lost;
    }
}
