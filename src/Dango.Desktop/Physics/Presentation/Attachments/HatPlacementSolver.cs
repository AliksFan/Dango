namespace Dango.Desktop.Physics.Presentation.Attachments;

public sealed record HatVisualPose(AttachmentPoint Center, double Angle, double S);

/// <summary>Visual placement calibrated to the accepted embedded legacy root.</summary>
public sealed class HatPlacementSolver
{
    private readonly HatTrackSolver track;
    private readonly HatContactPose neutralGuide;
    private readonly AttachmentPoint referenceCenter;
    private readonly double referenceAngle;
    private HatPlacementSolver(HatTrackSolver track, HatContactPose neutralGuide, PresentationAttachment reference)
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
        var neutral = track?.Fit(0);
        return track is null || neutral is null ? null : new(track, neutral, reference);
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public IReadOnlyList<AttachmentPoint> Guide => track.Track;
    public HatVisualPose? Fit(double s) => !double.IsFinite(s) || s < Minimum || s > Maximum ? null : FitRaw(s);

    public double AvailableLift(double s)
    {
        var surface = track.Fit(s); var embedded = Fit(s);
        return surface is null || embedded is null ? 0 : Math.Max(0, (surface.Center.Y - embedded.Center.Y) * 0.5);
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
        if (next?.Fit(0) is null) return null;
        var result = new HatPlacementSolver(next, this);
        return result.Fit(0) is null ? null : result;
    }

    private HatVisualPose? FitRaw(double s)
    {
        var pose = track.Fit(s);
        if (pose is null) return null;
        var center = referenceCenter + (pose.Center - neutralGuide.Center);
        var angle = referenceAngle + (pose.Angle - neutralGuide.Angle);
        if (!double.IsFinite(angle) || !double.IsFinite(center.X) || !double.IsFinite(center.Y) || Math.Abs(angle) > 0.35 || Math.Abs(center.X) > 1 || Math.Abs(center.Y) > 1) return null;
        return new(center, angle, s);
    }

    private double FindLimit(double endpoint)
    {
        double previous = 0;
        for (var i = 1; i <= 160; i++)
        {
            var s = endpoint * i / 160;
            if (FitRaw(s) is null) return previous;
            previous = s;
        }
        return endpoint;
    }
}
