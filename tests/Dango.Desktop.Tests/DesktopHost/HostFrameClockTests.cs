using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class HostFrameClockTests
{
    [TestMethod]
    [DataRow(30, 10)]
    [DataRow(60, 10)]
    [DataRow(120, 10)]
    [DataRow(30, 600)]
    [DataRow(60, 600)]
    [DataRow(120, 600)]
    public void CallbackRateDoesNotChangeElapsedTime(int rate, int seconds)
    {
        const long frequency = 10_000_003;
        var clock = new HostFrameClock(frequency);
        clock.Start(0);
        long steps = 0;
        for (var i = 1; i <= rate * seconds; i++)
        {
            var batch = clock.Advance((long)i * frequency / rate);
            steps += batch.Steps;
            Assert.AreEqual(0L, batch.DroppedTicks);
            Assert.IsFalse(batch.Reset);
        }
        Assert.AreEqual(TimeSpan.FromSeconds(seconds).Ticks,
            steps * HostFrameClock.FixedStep.Ticks + clock.RemainderTicks);
        Assert.IsTrue(clock.RemainderTicks < HostFrameClock.FixedStep.Ticks);
    }

    [TestMethod]
    [DataRow(99)]
    [DataRow(100)]
    [DataRow(101)]
    [DataRow(999)]
    [DataRow(1000)]
    [DataRow(10000)]
    public void BudgetAndLongPauseAreDifferentBoundaries(int milliseconds)
    {
        var clock = new HostFrameClock(TimeSpan.TicksPerSecond);
        clock.Start(0);
        clock.Advance(123);
        var elapsed = TimeSpan.FromMilliseconds(milliseconds).Ticks;
        var batch = clock.Advance(123 + elapsed);
        Assert.IsTrue(batch.Steps <= 6);
        Assert.AreEqual(milliseconds >= 1000, batch.Reset);
        Assert.AreEqual(elapsed + 123, batch.Steps * HostFrameClock.FixedStep.Ticks + batch.DroppedTicks + clock.RemainderTicks);
        if (batch.Reset) Assert.AreEqual(0, batch.Steps);
        else Assert.AreEqual(Math.Min(6, (int)((elapsed + 123) / HostFrameClock.FixedStep.Ticks)), batch.Steps);
    }

    [TestMethod]
    public void DuplicateBackwardsAndRestartCannotReplayTime()
    {
        var clock = new HostFrameClock(TimeSpan.TicksPerSecond);
        clock.Start(0);
        clock.Advance(100);
        Assert.AreEqual(0, clock.Advance(100).Steps);
        Assert.AreEqual(100L, clock.RemainderTicks);
        var bad = clock.Advance(99);
        Assert.IsTrue(bad.Reset && bad.InvalidClock);
        Assert.AreEqual(100L, bad.DroppedTicks);
        Assert.AreEqual(0L, clock.RemainderTicks);
        clock.Stop(); clock.Start(900_000_000);
        Assert.AreEqual(1, clock.Advance(900_000_000 + HostFrameClock.FixedStep.Ticks).Steps);
    }

    [TestMethod]
    public void JitterAndFaultDiscardConserveTime()
    {
        var clock = new HostFrameClock(TimeSpan.TicksPerSecond);
        clock.Start(0);
        long now = 0, simulated = 0, dropped = 0;
        for (var i = 0; i < 500; i++)
        {
            now += new[] { 1, 8, 17, 34, 66, 180 }[i % 6] * TimeSpan.TicksPerMillisecond;
            var batch = clock.Advance(now);
            dropped += batch.DroppedTicks;
            var completed = i == 200 ? 0 : batch.Steps;
            simulated += completed * HostFrameClock.FixedStep.Ticks;
            if (completed != batch.Steps) dropped += clock.Discard(batch.Steps - completed);
            Assert.AreEqual(now, simulated + dropped + clock.RemainderTicks);
        }
    }

    [TestMethod]
    [DataRow(30, false, 0)]
    [DataRow(60, false, 0)]
    [DataRow(30, true, 5)]
    [DataRow(60, true, 5)]
    [DataRow(30, true, 30)]
    [DataRow(60, true, 30)]
    public void RealDozeControllerUsesScheduledTime(int rate, bool maximum, int bucket)
    {
        var doze = new IdleDozeController { Next = (min, max) => min == 0 ? bucket : maximum ? max - 1 : min };
        var clock = new HostFrameClock(TimeSpan.TicksPerSecond);
        clock.Start(0);
        double? closing = null, sleeping = null, waking = null;
        for (var i = 1; i <= rate * 1250 && waking is null; i++)
        {
            var batch = clock.Advance((long)i * TimeSpan.TicksPerSecond / rate);
            for (var step = 0; step < batch.Steps; step++)
            {
                doze.Advance(new(true, 0, 0), HostFrameClock.FixedStep.TotalSeconds, true, false, _ => true);
                if (doze.Active) closing ??= (double)i / rate;
                if (doze.Sleeping) sleeping ??= (double)i / rate;
                if (sleeping is not null && !doze.Active) waking = (double)i / rate;
            }
        }
        var tolerance = 1d / rate + HostFrameClock.FixedStep.TotalSeconds;
        Assert.IsNotNull(closing); Assert.IsNotNull(sleeping); Assert.IsNotNull(waking);
        Assert.AreEqual(maximum ? 600d : 300d, closing.Value, tolerance);
        Assert.AreEqual(.8, sleeping.Value - closing.Value, tolerance);
        var duration = bucket < 5 ? (maximum ? 120 : 30) : bucket < 30 ? (maximum ? 300 : 120) : (maximum ? 600 : 300);
        Assert.AreEqual((double)duration, waking.Value - sleeping.Value, tolerance);
    }
}
