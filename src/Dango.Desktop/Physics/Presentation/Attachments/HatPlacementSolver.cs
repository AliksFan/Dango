namespace Dango.Desktop.Physics.Presentation.Attachments;

public sealed record HatVisualPose(AttachmentPoint Center, double Angle, double S);

/// <summary>Visual placement calibrated to the accepted embedded legacy root.</summary>
public sealed class HatPlacementSolver
{
    private readonly HatTrackSolver track;
    private readonly HatFitValues neutralGuide;
    private readonly AttachmentPoint referenceCenter;
    private readonly double referenceAngle;
    private HatPlacementSolver(HatTrackSolver track, HatFitValues neutralGuide, PresentationAttachment reference)
    {
        this.track = track;
        this.neutralGuide = neutralGuide;
        // Calibrate the original Canvas transform at the material root (80,25).
        // Its legacy transform origin is (80,15); preserve the resulting pose,
        // not that pivot as a constraint on subsequent sliding.
        var angle = Math.Clamp(reference.RotationRadians, -0.35, 0.35);
        var rootX = 80 - 10 * Math.Sin(angle) + Math.Clamp(reference.Position.X, -0.2, 0.2) * 72;
        var rootY = 15 + 10 * Math.Cos(angle) - Math.Clamp(reference.Position.Y - 0.82, -0.2, 0.2) * 58;
        referenceCenter = new((rootX - 80) / 160, (66 - rootY) / 160);
        referenceAngle = -angle;
        Minimum = FindLimit(track.Minimum);
        Maximum = FindLimit(track.Maximum);
    }

    public static HatPlacementSolver? Create(AttachmentShapeGeometry shape, PresentationAttachment reference)
    {
        if (!double.IsFinite(reference.Position.X) || !double.IsFinite(reference.Position.Y) || !double.IsFinite(reference.RotationRadians)) return null;
        var track = HatTrackSolver.CreateGeometryGuide(shape);
        return track is null || !track.TryFitScalars(0, out var neutral) ? null : new(track, neutral, reference);
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public IReadOnlyList<AttachmentPoint> Guide => track.Track;
    public HatVisualPose? Fit(double s)
    {
        if (!InRange(s)) return null;
        using var workspace = track.RentWorkspace();
        return TryFitRaw(s, workspace, out var center, out var angle) ? new(center, angle, s) : null;
    }

    private bool InRange(double s) => double.IsFinite(s) && s >= Minimum && s <= Maximum;

    public double AvailableLift(double s)
    {
        using var workspace = track.RentWorkspace();
        return track.TryFitScalars(s, workspace, out var surface) && InRange(s) &&
            TryCalibrate(surface, out var center, out _) ? Math.Max(0, (surface.Center.Y - center.Y) * 0.5) : 0;
    }

    private HatPlacementSolver(HatTrackSolver next, HatPlacementSolver calibration)
    {
        track = next;
        neutralGuide = calibration.neutralGuide;
        referenceCenter = calibration.referenceCenter;
        referenceAngle = calibration.referenceAngle;
        Minimum = FindLimit(track.Minimum);
        Maximum = FindLimit(track.Maximum);
    }

    // Rebuild geometry while retaining the accepted initial embedded-root calibration.
    public HatPlacementSolver? Remap(AttachmentShapeGeometry shape)
    {
        var next = HatTrackSolver.CreateGeometryGuide(shape);
        if (next is null || !next.TryFitScalars(0, out _)) return null;
        var result = new HatPlacementSolver(next, this);
        using var workspace = next.RentWorkspace();
        return result.InRange(0) && result.TryFitRaw(0, workspace, out _, out _) ? result : null;
    }

    private bool TryFitRaw(double s, in HatFitWorkspace workspace, out AttachmentPoint center, out double angle)
    {
        center = default; angle = 0;
        if (!track.TryFitScalars(s, workspace, out var pose)) return false;
        return TryCalibrate(pose, out center, out angle);
    }

    private bool TryCalibrate(HatFitValues pose, out AttachmentPoint center, out double angle)
    {
        center = referenceCenter + (pose.Center - neutralGuide.Center);
        angle = referenceAngle + (pose.Angle - neutralGuide.Angle);
        return double.IsFinite(angle) && double.IsFinite(center.X) && double.IsFinite(center.Y) &&
            Math.Abs(angle) <= 0.35 && Math.Abs(center.X) <= 1 && Math.Abs(center.Y) <= 1;
    }

    private double FindLimit(double endpoint)
    {
        double previous = 0;
        using var workspace = track.RentWorkspace();
        for (var i = 1; i <= 160; i++)
        {
            var s = endpoint * i / 160;
            if (!TryFitRaw(s, workspace, out _, out _)) return previous;
            previous = s;
        }
        return endpoint;
    }
}
