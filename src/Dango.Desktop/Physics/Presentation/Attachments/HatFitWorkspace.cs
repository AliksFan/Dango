using System.Buffers;

namespace Dango.Desktop.Physics.Presentation.Attachments;

internal readonly record struct HatFitValues(AttachmentPoint Center, double Angle, double S,
    double ContactWidth, double MaximumGap, double MaximumPenetration);

// Owned by a single call (or scan), never stored in a solver or exposed in a pose.
internal readonly struct HatFitWorkspace : IDisposable
{
    private readonly AttachmentPoint[] samples;
    private readonly double[] numbers;
    private readonly int pointCount;

    internal HatFitWorkspace(int pointCount)
    {
        this.pointCount = pointCount;
        samples = ArrayPool<AttachmentPoint>.Shared.Rent(65 + pointCount);
        try { numbers = ArrayPool<double>.Shared.Rent(pointCount + 65 + pointCount); }
        catch { ArrayPool<AttachmentPoint>.Shared.Return(samples); throw; }
    }

    internal Span<AttachmentPoint> Samples => samples.AsSpan(0, 65 + pointCount);
    internal Span<double> Projected => numbers.AsSpan(0, pointCount);
    internal Span<double> KeysAndResiduals => numbers.AsSpan(pointCount, 65 + pointCount);

    public void Dispose()
    {
        ArrayPool<double>.Shared.Return(numbers);
        ArrayPool<AttachmentPoint>.Shared.Return(samples);
    }
}
