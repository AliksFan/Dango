namespace Dango.Desktop.AppShell.Rooms;

/// <summary>Native WHEEL_DELTA units only; input eligibility belongs to the host.</summary>
internal sealed class RoomWheelAccumulator
{
    private const int WheelDelta = 120;
    private const long IdleMilliseconds = 250;
    private long? lastInputMs;

    internal int Remainder { get; private set; }

    internal int Add(int delta, long nowMs)
    {
        Expire(nowMs);
        if (delta == 0) return 0;
        var accumulated = (long)Remainder + delta;
        var steps = (int)(accumulated / WheelDelta);
        Remainder = (int)(accumulated % WheelDelta);
        lastInputMs = nowMs;
        return steps;
    }

    internal void Expire(long nowMs)
    {
        if (nowMs < 0) throw new ArgumentOutOfRangeException(nameof(nowMs));
        if (lastInputMs is { } previous && (nowMs < previous || nowMs - previous >= IdleMilliseconds))
            Clear();
    }

    internal void Clear()
    {
        Remainder = 0;
        lastInputMs = null;
    }
}
