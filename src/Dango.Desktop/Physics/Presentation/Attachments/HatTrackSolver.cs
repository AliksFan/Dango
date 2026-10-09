using System.Runtime.CompilerServices;

namespace Dango.Desktop.Physics.Presentation.Attachments;

public sealed record HatContactPose(AttachmentPoint Center, double Angle, double S, double ContactWidth,
    double MaximumGap, double MaximumPenetration, IReadOnlyList<AttachmentPoint> ContactSamples)
{
    public IReadOnlyList<AttachmentPoint> FittingSamples { get; init; } = [];
}

public sealed class HatTrackSolver
{
    public const double BaseWidth = 6d / 160;
    private readonly AttachmentPoint[] points;
    private readonly double[] lengths;
    private readonly double neutral;
    private readonly bool strictSurface;
    private HatTrackSolver(AttachmentPoint[] points, bool strictSurface)
    {
        this.points = points;
        this.strictSurface = strictSurface;
        lengths = new double[points.Length];
        for (var i = 1; i < points.Length; i++) lengths[i] = lengths[i - 1] + (points[i] - points[i - 1]).Length;
        var mid = (points[0].X + points[^1].X) / 2;
        var centralWidth = (points[^1].X - points[0].X) * 0.15 / 0.35;
        double total = 0, weightedX = 0;
        for (var i = 1; i < points.Length; i++)
        {
            var p = (points[i] + points[i - 1]) * 0.5;
            if (Math.Abs(p.X - mid) > centralWidth / 2) continue;
            var weight = lengths[i] - lengths[i - 1];
            total += weight; weightedX += p.X * weight;
        }
        var neutralX = total > 0 ? weightedX / total : mid;
        var index = Enumerable.Range(1, points.Length - 1).First(i => points[i].X >= neutralX);
        var dx = points[index].X - points[index - 1].X;
        neutral = lengths[index - 1] + (dx > 1e-12 ? (neutralX - points[index - 1].X) / dx : 0) * (lengths[index] - lengths[index - 1]);
        Minimum = Math.Max(-0.08, BaseWidth / 2 - neutral);
        Maximum = Math.Min(0.08, lengths[^1] - BaseWidth / 2 - neutral);
    }

    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public IReadOnlyList<AttachmentPoint> Track => Array.AsReadOnly(points);

    // The original surface fit remains a historical geometry check, not visual placement.
    public static HatTrackSolver? Create(AttachmentShapeGeometry shape) => Create(shape, true);
    public static HatTrackSolver? CreateGeometryGuide(AttachmentShapeGeometry shape) => Create(shape, false);

    private static HatTrackSolver? Create(AttachmentShapeGeometry shape, bool strictSurface)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var middle = (shape.Left + shape.Right) / 2;
        bool Eligible(AttachmentPoint p) => Math.Abs(p.X - middle) <= shape.Width * 0.35 && p.Y >= shape.Bottom + shape.Height * 0.65;
        var boundary = shape.Boundary;
        var excluded = Enumerable.Range(0, boundary.Count).FirstOrDefault(i => !Eligible(boundary[i]));
        var runs = new List<List<AttachmentPoint>>();
        List<AttachmentPoint>? run = null;
        for (var j = 1; j <= boundary.Count; j++)
        {
            var p = boundary[(excluded + j) % boundary.Count];
            if (!Eligible(p)) { run = null; continue; }
            if (run is null) { run = []; runs.Add(run); }
            run.Add(p);
        }
        var crown = runs.Where(r => r.Count > 2 && r.Min(p => p.X) <= middle && r.Max(p => p.X) >= middle)
            .OrderByDescending(r => r.Average(p => p.Y)).FirstOrDefault();
        if (crown is null) return null;
        if (crown[0].X > crown[^1].X) crown.Reverse();
        if (crown.Zip(crown.Skip(1)).Any(pair => pair.Second.X < pair.First.X - 1e-12)) return null;
        var track = new HatTrackSolver(crown.ToArray(), strictSurface);
        if (track.Minimum > track.Maximum) return null;
        // Select a single legal component, closest to the geometric neutral.
        var legalRuns = new List<(double Min, double Max)>();
        double? start = null;
        double previous = track.Minimum;
        const int subdivisions = 320;
        using var workspace = track.RentWorkspace();
        for (var i = 0; i <= subdivisions; i++)
        {
            var s = track.Minimum + (track.Maximum - track.Minimum) * i / subdivisions;
            if (track.Evaluate(s, workspace, true, [], out _, out _)) start ??= s;
            else if (start.HasValue) { legalRuns.Add((start.Value, previous)); start = null; }
            previous = s;
        }
        if (start.HasValue) legalRuns.Add((start.Value, track.Maximum));
        if (legalRuns.Count == 0) return null;
        var selected = legalRuns.OrderBy(r => Math.Abs(Math.Clamp(0, r.Min, r.Max))).ThenBy(r => r.Min).First();
        track.Minimum = selected.Min; track.Maximum = selected.Max;
        return track;
    }

    public HatContactPose? Fit(double s)
    {
        if (!InRange(s)) return null;
        using var workspace = RentWorkspace();
        Span<AttachmentPoint> fitting = stackalloc AttachmentPoint[9];
        if (!Evaluate(s, workspace, false, fitting, out var pose, out var count)) return null;
        return new(pose.Center, pose.Angle, pose.S, pose.ContactWidth, pose.MaximumGap, pose.MaximumPenetration,
            Array.AsReadOnly(workspace.Samples[..count].ToArray()))
        { FittingSamples = Array.AsReadOnly(fitting.ToArray()) };
    }

    internal HatFitWorkspace RentWorkspace() => new(points.Length);
    private bool InRange(double s) => double.IsFinite(s) && s >= Minimum && s <= Maximum;

    internal bool IsLegal(double s)
    {
        if (!InRange(s)) return false;
        using var workspace = RentWorkspace();
        return Evaluate(s, workspace, true, [], out _, out _);
    }

    internal bool TryFitScalars(double s, out HatFitValues pose)
    {
        using var workspace = RentWorkspace();
        return TryFitScalars(s, workspace, out pose);
    }

    internal bool TryFitScalars(double s, in HatFitWorkspace workspace, out HatFitValues pose)
    {
        pose = default;
        return InRange(s) && Evaluate(s, workspace, false, [], out pose, out _);
    }

    // Each native step runs this fitting kernel hundreds of times. Begin with
    // optimized code so cold walking does not wait for tier promotion.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool Evaluate(double s, in HatFitWorkspace workspace, bool legalityOnly,
        Span<AttachmentPoint> fitting, out HatFitValues pose, out int sampleCount)
    {
        pose = default;
        sampleCount = 0;
        var arc = neutral + s;
        if (arc < BaseWidth / 2 || arc > lengths[^1] - BaseWidth / 2) return false;
        var samples = workspace.Samples;
        // Only these nine arc samples participate in the fit. The full 65-point
        // footprint is sampled separately below, before residual evaluation.
        for (var i = 0; i < 65; i += 8) samples[i] = At(arc + BaseWidth * (i / 64d - 0.5));
        if (!fitting.IsEmpty)
            for (var i = 0; i < 9; i++) fitting[i] = samples[i * 8];
        var mean = new AttachmentPoint(0, 0);
        double weightSum = 0;
        for (var i = 0; i < 9; i++)
        {
            var weight = i is 0 or 8 ? 1 : 2;
            mean += samples[i * 8] * weight; weightSum += weight;
        }
        mean *= 1 / weightSum;
        double xx = 0, xy = 0, yy = 0;
        for (var i = 0; i < 9; i++)
        {
            var d = samples[i * 8] - mean; var weight = i is 0 or 8 ? 1 : 2;
            xx += weight * d.X * d.X; xy += weight * d.X * d.Y; yy += weight * d.Y * d.Y;
        }
        var angle = Math.Atan2(2 * xy, xx - yy) / 2;
        if (Math.Abs(angle) > 0.35) return false;
        var tangent = new AttachmentPoint(Math.Cos(angle), Math.Sin(angle));
        var normal = new AttachmentPoint(-Math.Sin(angle), Math.Cos(angle));
        // Evaluate the entire rigid base footprint, including polyline vertices.
        // Arc length alone is insufficient: the rigid chord can extend beyond it.
        var projected = workspace.Projected;
        for (var i = 0; i < points.Length; i++) projected[i] = (points[i] - mean).Dot(tangent);
        if (projected[0] > -BaseWidth / 2 || projected[^1] < BaseWidth / 2) return false;
        for (var i = 1; i < projected.Length; i++)
            if (projected[i] <= projected[i - 1]) return false;
        // Guide evaluation has no later rejection. Strict contact still needs all residual gates.
        if (legalityOnly && !strictSurface) return true;
        for (var i = 0; i < 65; i++) samples[i] = SurfaceAt(projected, BaseWidth * (i / 64d - 0.5));
        sampleCount = 65;
        for (var i = 0; i < points.Length; i++)
            if (Math.Abs(projected[i]) < BaseWidth / 2) samples[sampleCount++] = points[i];
        var residuals = workspace.KeysAndResiduals[..sampleCount];
        for (var i = 0; i < sampleCount; i++) residuals[i] = (samples[i] - mean).Dot(tangent);
        // Stable insertion order matches OrderBy, including equal projected keys.
        for (var i = 1; i < sampleCount; i++)
        {
            var point = samples[i]; var key = residuals[i]; var j = i - 1;
            while (j >= 0 && key.CompareTo(residuals[j]) < 0)
            {
                samples[j + 1] = samples[j]; residuals[j + 1] = residuals[j]; j--;
            }
            samples[j + 1] = point; residuals[j + 1] = key;
        }
        for (var i = 0; i < sampleCount; i++) residuals[i] = (samples[i] - mean).Dot(normal);
        var minimum = residuals[0]; var maximum = residuals[0];
        for (var i = 1; i < sampleCount; i++)
        {
            var value = residuals[i];
            if (value < minimum || double.IsNaN(value)) minimum = value;
            if (value > maximum || double.IsNaN(maximum)) maximum = value;
        }
        var shift = (minimum + maximum) / 2;
        var center = mean + normal * shift;
        // Preserve subtraction and addition order; do not derive these from half the range.
        var gap = shift - residuals[0]; var penetration = residuals[0] - shift;
        for (var i = 1; i < sampleCount; i++)
        {
            var nextGap = shift - residuals[i]; var nextPenetration = residuals[i] - shift;
            if (nextGap > gap || double.IsNaN(gap)) gap = nextGap;
            if (nextPenetration > penetration || double.IsNaN(penetration)) penetration = nextPenetration;
        }
        gap += AttachmentShapeGeometry.MaximumChordError;
        penetration = penetration + 1d / 160 + AttachmentShapeGeometry.MaximumChordError;
        if (strictSurface && (gap > 0.005 || penetration > 0.007)) return false;
        pose = new(center, angle, s, BaseWidth, gap, penetration);
        return true;
    }

    private AttachmentPoint SurfaceAt(ReadOnlySpan<double> projected, double distance)
    {
        var index = projected.BinarySearch(distance);
        if (index >= 0) return points[index];
        index = ~index;
        var t = (distance - projected[index - 1]) / (projected[index] - projected[index - 1]);
        return points[index - 1] * (1 - t) + points[index] * t;
    }

    private AttachmentPoint At(double length)
    {
        var index = Array.BinarySearch(lengths, length);
        if (index >= 0) return points[index];
        index = Math.Clamp(~index, 1, points.Length - 1);
        var fraction = (length - lengths[index - 1]) / (lengths[index] - lengths[index - 1]);
        return points[index - 1] * (1 - fraction) + points[index] * fraction;
    }
}
