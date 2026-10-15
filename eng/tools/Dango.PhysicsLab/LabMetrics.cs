using Dango.Physics;

namespace Dango.PhysicsLab;

public sealed record LabMetricSample(long Tick, double TimeSeconds, int EntityId,
    double CoreX, double CoreY, double CenterOfMassX, double CenterOfMassY,
    double ContourHeight, double MaximumSpeed, int GroundContactCount,
    int ConsecutiveNoContactFrames, DangoLabDrive Mode, double Bias, double StretchRamp,
    double RequestedForceX, double ActualForceX, double ActualForceY, double TorqueAboutCenterOfMass)
{
    // ContourHeight retains the legacy control-point span for comparison with earlier exports.
    public double ExactCurveHeight { get; init; }
}

public sealed record LabMetricExport(double FixedDt, int Capacity, long TotalSamples,
    IReadOnlyList<LabMetricSample> Samples);

/// <summary>Bounded, fixed-tick physical measurements; independent of drawing and wall-clock FPS.</summary>
public sealed class LabMetrics
{
    private readonly Queue<LabMetricSample> samples = new();
    private readonly Dictionary<int, int> noContactFrames = [];
    public LabMetrics(int capacity = 1200, double fixedDt = 1d / 60)
    {
        if (capacity is < 1 or > 1200) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!double.IsFinite(fixedDt) || fixedDt <= 0 || fixedDt > .1)
            throw new ArgumentOutOfRangeException(nameof(fixedDt));
        Capacity = capacity;
        FixedDt = fixedDt;
    }

    public int Capacity { get; }
    public double FixedDt { get; }
    public long TotalSamples { get; private set; }
    public IReadOnlyList<LabMetricSample> Samples => Array.AsReadOnly(samples.ToArray());

    public LabMetricSample Record(DangoBodyState body, IReadOnlyList<DangoLabNode> nodes, DangoLabStats stats)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(stats);
        if (nodes.Count == 0 || body.Contour.Count < 2 || body.Contour.Count % 2 != 0)
            throw new ArgumentException("Metrics require real nodes and paired contour coordinates.");
        double mass = 0, weightedX = 0, weightedY = 0;
        foreach (var node in nodes)
        {
            if (!float.IsFinite(node.Mass) || node.Mass <= 0 || !float.IsFinite(node.X) || !float.IsFinite(node.Y))
                throw new ArgumentException("Metrics require finite positions and positive actual masses.");
            mass += node.Mass;
            weightedX += node.X * (double)node.Mass;
            weightedY += node.Y * (double)node.Mass;
        }
        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;
        for (var i = 1; i < body.Contour.Count; i += 2)
        {
            var y = body.Contour[i];
            if (!float.IsFinite(y)) throw new ArgumentException("Contour height must be finite.");
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        var id = body.EntityId.Value;
        var noContact = body.GroundContactCount == 0 ? noContactFrames.GetValueOrDefault(id) + 1 : 0;
        noContactFrames[id] = noContact;
        var tick = ++TotalSamples;
        var sample = new LabMetricSample(tick, tick * FixedDt, id, body.CoreX, body.CoreY,
            weightedX / mass, weightedY / mass, maxY - minY, body.MaximumSpeed,
            body.GroundContactCount, noContact, stats.Mode, stats.Bias, stats.StretchRamp,
            stats.RequestedForceX, stats.ActualForceX, stats.ActualForceY, stats.TorqueAboutCenterOfMass)
        {
            ExactCurveHeight = CurveHeight(body.Contour),
        };
        if (samples.Count == Capacity) samples.Dequeue();
        samples.Enqueue(sample);
        return sample;
    }

    public void Clear()
    {
        samples.Clear();
        noContactFrames.Clear();
        TotalSamples = 0;
    }

    public LabMetricExport Export() => new(FixedDt, Capacity, TotalSamples, Samples);

    private static double CurveHeight(IReadOnlyList<float> contour)
    {
        var extent = LabGroundExperiments.CurveExtent(contour, 1);
        return extent.Maximum - extent.Minimum;
    }
}
