using Dango.Core.Animation;

namespace Dango.Desktop.Rendering.Visuals;

internal sealed class IdleDozeController
{
    internal Func<int, int, int> Next { get; set; } = Random.Shared.Next;
    private QuietIdleSample? previous;
    private double idle, threshold, stable, closing, sleepLength;
    private bool scheduled;
    internal bool Due { get; private set; }
    internal bool Active { get; private set; }
    internal bool Sleeping { get; private set; }
    internal double SleepSeconds { get; private set; }
    internal string? Error { get; private set; }
    private static readonly Release ReleaseEyes = new(AttachmentChannel.LeftEyePose | AttachmentChannel.RightEyePose);

    internal void Advance(QuietIdleSample sample, double seconds, bool eligible, bool busy, Func<AttachmentIntent, bool> submit)
    {
        if (!double.IsFinite(seconds) || seconds is <= 0 or > .1) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!eligible || !double.IsFinite(sample.X) || !double.IsFinite(sample.Y)) { Cancel(submit); return; }
        if (Error is not null) return;
        var settled = sample.Grounded && previous is { } p &&
            Math.Sqrt(Math.Pow((sample.X - p.X) / seconds, 2) + Math.Pow((sample.Y - p.Y) / seconds, 2)) <= .15;
        previous = sample;
        stable = settled ? stable + seconds : 0;
        if (Active && !settled) { Cancel(submit); return; }
        if (!scheduled) { threshold = Next(300000, 600001) / 1000d; scheduled = true; }
        if (Sleeping)
        {
            SleepSeconds += seconds;
            if (SleepSeconds + 1e-9 >= sleepLength) Cancel(submit);
            return;
        }
        if (Active)
        {
            closing += seconds;
            // Finish the target ramp early enough for the existing eye spring to settle.
            if (!submit(new SetEyes(EyeSelection.Both, Math.Max(0, 1 - closing / .6), EyeShape.Sleeping)))
            { Fail(submit); return; }
            if (closing + 1e-9 >= .8)
            {
                Sleeping = true;
                var bucket = Next(0, 100);
                sleepLength = bucket < 5 ? Next(30000, 120001) / 1000d :
                    bucket < 30 ? Next(120000, 300001) / 1000d : Next(300000, 600001) / 1000d;
            }
            return;
        }
        idle += seconds;
        Due = idle + 1e-9 >= threshold;
        if (!Due || busy || stable + 1e-9 < .5) return;
        if (!submit(new SetEyes(EyeSelection.Both, 1, EyeShape.Sleeping))) { Cancel(submit); return; }
        Active = true;
    }

    internal void Cancel(Func<AttachmentIntent, bool> submit)
    {
        if (Active && !submit(ReleaseEyes)) Error = "打盹眼睛释放失败；请使用恢复团子。";
        Active = Sleeping = Due = scheduled = false;
        previous = null;
        idle = threshold = stable = closing = sleepLength = SleepSeconds = 0;
    }

    private void Fail(Func<AttachmentIntent, bool> submit)
    {
        Cancel(submit);
        Error = "打盹眼睛更新失败；请使用恢复团子。";
    }

    internal void Reset() { Cancel(_ => true); Error = null; }
}
