namespace Dango.Desktop.Physics.Presentation.Attachments;

public readonly record struct AttachmentTimeSlice(double Start, double End)
{
    public double Seconds => End - Start;
}

/// <summary>Fixed-step time only. No wall-clock source or rendering callback.</summary>
public sealed class AttachmentStepClock
{
    public double Elapsed { get; private set; }
    public long StepIndex { get; private set; } = -1;
    public bool Paused { get; set; }
    /// <param name="boundaries">Absolute attachment-time command or phase boundaries, in seconds.</param>
    public IReadOnlyList<AttachmentTimeSlice> Advance(long stepIndex, double seconds, IReadOnlyList<double>? boundaries = null)
    {
        if (stepIndex < 0) throw new ArgumentOutOfRangeException(nameof(stepIndex));
        if (!double.IsFinite(seconds) || seconds is <= 0 or > 0.1) throw new ArgumentOutOfRangeException(nameof(seconds));
        var times = boundaries?.ToArray() ?? [];
        if (times.Any(t => !double.IsFinite(t) || t < 0)) throw new ArgumentOutOfRangeException(nameof(boundaries));
        if (Paused || stepIndex <= StepIndex) return Array.Empty<AttachmentTimeSlice>();
        var end = Elapsed + seconds;
        if (!double.IsFinite(end) || end <= Elapsed) throw new ArithmeticException("Attachment clock exhausted numeric precision.");
        var slices = new List<AttachmentTimeSlice>();
        var start = Elapsed;
        foreach (var boundary in times.Where(t => t > Elapsed && t < end).Append(end).Distinct().Order())
        {
            var count = Math.Max(1, (int)Math.Ceiling((boundary - start) * 240));
            var segmentStart = start;
            for (var i = 1; i <= count; i++)
            {
                var next = i == count ? boundary : segmentStart + (boundary - segmentStart) * i / count;
                if (next <= start) throw new ArithmeticException("Attachment slice exhausted numeric precision.");
                slices.Add(new(start, next));
                start = next;
            }
        }
        Elapsed = end;
        StepIndex = stepIndex;
        return slices.AsReadOnly();
    }
    public void Reset() { Elapsed = 0; StepIndex = -1; }
}
