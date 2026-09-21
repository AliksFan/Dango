using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments.Reference;

// Frozen from src/Dango.Desktop/Physics/Presentation/Attachments/Hat{Track,Placement}Solver.cs at 75de095.
// Keep this independent test oracle in sync only by intentionally replacing its provenanced baseline.
internal sealed record FrozenHatContactPose(AttachmentPoint Center, double Angle, double S, double ContactWidth,
    double MaximumGap, double MaximumPenetration, IReadOnlyList<AttachmentPoint> ContactSamples)
{
    public IReadOnlyList<AttachmentPoint> FittingSamples { get; init; } = [];
}

internal sealed class FrozenHatTrackSolver
{
    public const double BaseWidth = 6d / 160;
    private readonly AttachmentPoint[] points;
    private readonly double[] lengths;
    private readonly double neutral;
    private readonly bool strictSurface;
    private FrozenHatTrackSolver(AttachmentPoint[] points, bool strictSurface)
    {
        this.points = points; this.strictSurface = strictSurface; lengths = new double[points.Length];
        for (var i = 1; i < points.Length; i++) lengths[i] = lengths[i - 1] + (points[i] - points[i - 1]).Length;
        var mid = (points[0].X + points[^1].X) / 2; var centralWidth = (points[^1].X - points[0].X) * 0.15 / 0.35;
        double total = 0, weightedX = 0;
        for (var i = 1; i < points.Length; i++) { var p = (points[i] + points[i - 1]) * 0.5; if (Math.Abs(p.X - mid) > centralWidth / 2) continue; var weight = lengths[i] - lengths[i - 1]; total += weight; weightedX += p.X * weight; }
        var neutralX = total > 0 ? weightedX / total : mid;
        var index = Enumerable.Range(1, points.Length - 1).First(i => points[i].X >= neutralX); var dx = points[index].X - points[index - 1].X;
        neutral = lengths[index - 1] + (dx > 1e-12 ? (neutralX - points[index - 1].X) / dx : 0) * (lengths[index] - lengths[index - 1]);
        Minimum = Math.Max(-0.08, BaseWidth / 2 - neutral); Maximum = Math.Min(0.08, lengths[^1] - BaseWidth / 2 - neutral);
    }
    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public IReadOnlyList<AttachmentPoint> Track => Array.AsReadOnly(points);
    public static FrozenHatTrackSolver? Create(AttachmentShapeGeometry shape) => Create(shape, true);
    public static FrozenHatTrackSolver? CreateGeometryGuide(AttachmentShapeGeometry shape) => Create(shape, false);
    private static FrozenHatTrackSolver? Create(AttachmentShapeGeometry shape, bool strictSurface)
    {
        ArgumentNullException.ThrowIfNull(shape); var middle = (shape.Left + shape.Right) / 2;
        bool Eligible(AttachmentPoint p) => Math.Abs(p.X - middle) <= shape.Width * 0.35 && p.Y >= shape.Bottom + shape.Height * 0.65;
        var boundary = shape.Boundary; var excluded = Enumerable.Range(0, boundary.Count).FirstOrDefault(i => !Eligible(boundary[i])); var runs = new List<List<AttachmentPoint>>(); List<AttachmentPoint>? run = null;
        for (var j = 1; j <= boundary.Count; j++) { var p = boundary[(excluded + j) % boundary.Count]; if (!Eligible(p)) { run = null; continue; } if (run is null) { run = []; runs.Add(run); } run.Add(p); }
        var crown = runs.Where(r => r.Count > 2 && r.Min(p => p.X) <= middle && r.Max(p => p.X) >= middle).OrderByDescending(r => r.Average(p => p.Y)).FirstOrDefault();
        if (crown is null) return null; if (crown[0].X > crown[^1].X) crown.Reverse(); if (crown.Zip(crown.Skip(1)).Any(pair => pair.Second.X < pair.First.X - 1e-12)) return null;
        var track = new FrozenHatTrackSolver(crown.ToArray(), strictSurface); if (track.Minimum > track.Maximum) return null;
        var legalRuns = new List<(double Min, double Max)>(); double? start = null; double previous = track.Minimum; const int subdivisions = 320;
        for (var i = 0; i <= subdivisions; i++) { var s = track.Minimum + (track.Maximum - track.Minimum) * i / subdivisions; if (track.FitRaw(s) is not null) start ??= s; else if (start.HasValue) { legalRuns.Add((start.Value, previous)); start = null; } previous = s; }
        if (start.HasValue) legalRuns.Add((start.Value, track.Maximum)); if (legalRuns.Count == 0) return null;
        var selected = legalRuns.OrderBy(r => Math.Abs(Math.Clamp(0, r.Min, r.Max))).ThenBy(r => r.Min).First(); track.Minimum = selected.Min; track.Maximum = selected.Max; return track;
    }
    public FrozenHatContactPose? Fit(double s) => !double.IsFinite(s) || s < Minimum || s > Maximum ? null : FitRaw(s);
    private FrozenHatContactPose? FitRaw(double s)
    {
        var arc = neutral + s; if (arc < BaseWidth / 2 || arc > lengths[^1] - BaseWidth / 2) return null;
        var samples = Enumerable.Range(0, 65).Select(i => At(arc + BaseWidth * (i / 64d - 0.5))).ToArray(); var fittingSamples = Enumerable.Range(0, 9).Select(i => samples[i * 8]).ToArray();
        var mean = new AttachmentPoint(0, 0); double weightSum = 0;
        for (var i = 0; i < 9; i++) { var weight = i is 0 or 8 ? 1 : 2; mean += samples[i * 8] * weight; weightSum += weight; } mean *= 1 / weightSum;
        double xx = 0, xy = 0, yy = 0;
        for (var i = 0; i < 9; i++) { var d = samples[i * 8] - mean; var weight = i is 0 or 8 ? 1 : 2; xx += weight * d.X * d.X; xy += weight * d.X * d.Y; yy += weight * d.Y * d.Y; }
        var angle = Math.Atan2(2 * xy, xx - yy) / 2; if (Math.Abs(angle) > 0.35) return null;
        var tangent = new AttachmentPoint(Math.Cos(angle), Math.Sin(angle)); var normal = new AttachmentPoint(-Math.Sin(angle), Math.Cos(angle)); var projected = points.Select(p => (p - mean).Dot(tangent)).ToArray();
        if (projected[0] > -BaseWidth / 2 || projected[^1] < BaseWidth / 2 || projected.Zip(projected.Skip(1)).Any(pair => pair.Second <= pair.First)) return null;
        AttachmentPoint SurfaceAt(double distance) { var index = Array.BinarySearch(projected, distance); if (index >= 0) return points[index]; index = ~index; var t = (distance - projected[index - 1]) / (projected[index] - projected[index - 1]); return points[index - 1] * (1 - t) + points[index] * t; }
        samples = Enumerable.Range(0, 65).Select(i => SurfaceAt(BaseWidth * (i / 64d - 0.5))).Concat(points.Where((p, i) => Math.Abs(projected[i]) < BaseWidth / 2)).OrderBy(p => (p - mean).Dot(tangent)).ToArray();
        var residuals = samples.Select(p => (p - mean).Dot(normal)).ToArray(); var shift = (residuals.Min() + residuals.Max()) / 2; var center = mean + normal * shift; var gap = residuals.Max(r => shift - r) + AttachmentShapeGeometry.MaximumChordError; var penetration = residuals.Max(r => r - shift) + 1d / 160 + AttachmentShapeGeometry.MaximumChordError;
        if (strictSurface && (gap > 0.005 || penetration > 0.007)) return null;
        return new FrozenHatContactPose(center, angle, s, BaseWidth, gap, penetration, Array.AsReadOnly(samples)) { FittingSamples = Array.AsReadOnly(fittingSamples) };
    }
    private AttachmentPoint At(double length) { var index = Array.BinarySearch(lengths, length); if (index >= 0) return points[index]; index = Math.Clamp(~index, 1, points.Length - 1); var fraction = (length - lengths[index - 1]) / (lengths[index] - lengths[index - 1]); return points[index - 1] * (1 - fraction) + points[index] * fraction; }
}

internal sealed class FrozenHatPlacementSolver
{
    private readonly FrozenHatTrackSolver track; private readonly FrozenHatContactPose neutralGuide; private readonly AttachmentPoint referenceCenter; private readonly double referenceAngle;
    private FrozenHatPlacementSolver(FrozenHatTrackSolver track, FrozenHatContactPose neutralGuide, PresentationAttachment reference)
    {
        this.track = track; this.neutralGuide = neutralGuide; var angle = Math.Clamp(reference.RotationRadians, -0.35, 0.35); var rootX = 80 - 10 * Math.Sin(angle) + Math.Clamp(reference.Position.X, -0.2, 0.2) * 72; var rootY = 15 + 10 * Math.Cos(angle) - Math.Clamp(reference.Position.Y - 0.82, -0.2, 0.2) * 58; referenceCenter = new((rootX - 80) / 160, (66 - rootY) / 160); referenceAngle = -angle; Minimum = FindLimit(track.Minimum); Maximum = FindLimit(track.Maximum);
    }
    public static FrozenHatPlacementSolver? Create(AttachmentShapeGeometry shape, PresentationAttachment reference) { if (!double.IsFinite(reference.Position.X) || !double.IsFinite(reference.Position.Y) || !double.IsFinite(reference.RotationRadians)) return null; var track = FrozenHatTrackSolver.CreateGeometryGuide(shape); var neutral = track?.Fit(0); return track is null || neutral is null ? null : new(track, neutral, reference); }
    public double Minimum { get; } public double Maximum { get; } public IReadOnlyList<AttachmentPoint> Guide => track.Track;
    public HatVisualPose? Fit(double s) => !double.IsFinite(s) || s < Minimum || s > Maximum ? null : FitRaw(s);
    public double AvailableLift(double s) { var surface = track.Fit(s); var embedded = Fit(s); return surface is null || embedded is null ? 0 : Math.Max(0, (surface.Center.Y - embedded.Center.Y) * 0.5); }
    private FrozenHatPlacementSolver(FrozenHatTrackSolver next, FrozenHatPlacementSolver calibration) { track = next; neutralGuide = calibration.neutralGuide; referenceCenter = calibration.referenceCenter; referenceAngle = calibration.referenceAngle; Minimum = FindLimit(track.Minimum); Maximum = FindLimit(track.Maximum); }
    public FrozenHatPlacementSolver? Remap(AttachmentShapeGeometry shape) { var next = FrozenHatTrackSolver.CreateGeometryGuide(shape); if (next?.Fit(0) is null) return null; var result = new FrozenHatPlacementSolver(next, this); return result.Fit(0) is null ? null : result; }
    private HatVisualPose? FitRaw(double s) { var pose = track.Fit(s); if (pose is null) return null; var center = referenceCenter + (pose.Center - neutralGuide.Center); var angle = referenceAngle + (pose.Angle - neutralGuide.Angle); if (!double.IsFinite(angle) || !double.IsFinite(center.X) || !double.IsFinite(center.Y) || Math.Abs(angle) > 0.35 || Math.Abs(center.X) > 1 || Math.Abs(center.Y) > 1) return null; return new(center, angle, s); }
    private double FindLimit(double endpoint) { double previous = 0; for (var i = 1; i <= 160; i++) { var s = endpoint * i / 160; if (FitRaw(s) is null) return previous; previous = s; } return endpoint; }
}
