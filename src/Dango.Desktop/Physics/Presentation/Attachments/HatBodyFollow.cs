namespace Dango.Desktop.Physics.Presentation.Attachments;

/// <summary>Read-only world position in physics units, independent of desktop placement.</summary>
public readonly record struct HatBodySample(double X, double Y);
public sealed record HatFollowFrame(double Lift, double Bend, double Stretch, double Slide);

public sealed class HatBodyFollow
{
    public const double LiftLimit = 4d / 160;
    public const double DeformationLimit = 0.06;
    public const double SlideLimit = 0.06;
    private HatBodySample? previous;
    private double vx, vy, previousAspect, ax, ay, shapeRate;
    private bool velocityReady;
    private SpringState lift, bend, stretch, slide;
    private HatFollowFrame target = new(0, 0, 0, 0);
    public HatFollowFrame Current => new(lift.Position, bend.Position, stretch.Position, slide.Position);

    public void Observe(HatBodySample? body, double aspect, double seconds)
    {
        if (!double.IsFinite(aspect) || aspect <= 0 || !double.IsFinite(seconds) || seconds is <= 0 or > 0.1 ||
            body is { } p && (!double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new ArgumentOutOfRangeException(nameof(body));
        if (body is null) { ResetSignals(); return; }
        if (previous is null) { previous = body; previousAspect = aspect; return; }
        var nextVx = (body.Value.X - previous.Value.X) / seconds;
        var nextVy = (body.Value.Y - previous.Value.Y) / seconds;
        var blend = 1 - Math.Exp(-seconds / 0.06);
        static double Deadzone(double value, double threshold) => Math.Sign(value) * Math.Max(0, Math.Abs(value) - threshold);
        var rawX = velocityReady ? Math.Clamp((nextVx - vx) / seconds, -120, 120) : 0;
        var rawY = velocityReady ? Math.Clamp((nextVy - vy) / seconds, -120, 120) : 0;
        // In the body's moving frame a change in body velocity gives the hat
        // an opposite relative velocity. A stop therefore carries it forward.
        // Apply once per observation, independently of spring substep count.
        slide = DampedSpring.Impulse(slide, -rawX * seconds * 0.65, 0.9);
        lift = DampedSpring.Impulse(lift, -rawY * seconds * 0.38, 0.6);
        ax += (Deadzone(rawX, 0.3) - ax) * blend;
        ay += (Deadzone(rawY, 0.3) - ay) * blend;
        shapeRate += (Math.Clamp(Deadzone((aspect - previousAspect) / seconds, 0.02), -3, 3) - shapeRate) * blend;
        vx = nextVx; vy = nextVy; previous = body; previousAspect = aspect; velocityReady = true;
        var b = Math.Clamp(-ax * 0.00225, -DeformationLimit, DeformationLimit);
        var c = Math.Clamp(shapeRate * 0.06 - ay * 0.0009, -DeformationLimit, DeformationLimit);
        var sum = Math.Abs(b) / DeformationLimit + Math.Abs(c) / DeformationLimit;
        var scale = 1 / Math.Max(1, sum);
        target = new(0, b * scale, c * scale, 0);
    }

    public void Advance(double seconds, double availableLift)
    {
        lift = DampedSpring.AdvanceBounded(lift, 0, 2, 0.45, seconds, -LiftLimit, Math.Clamp(availableLift, 0, LiftLimit));
        bend = DampedSpring.AdvanceBounded(bend, target.Bend, 4.5, 0.8, seconds, -DeformationLimit, DeformationLimit);
        stretch = DampedSpring.AdvanceBounded(stretch, target.Stretch, 4.5, 0.8, seconds, -DeformationLimit, DeformationLimit);
        slide = DampedSpring.AdvanceBounded(slide, 0, 2, 0.45, seconds, -SlideLimit, SlideLimit);
    }

    public void ResetSignals()
    {
        previous = null; velocityReady = false; ax = ay = shapeRate = vx = vy = 0;
        target = new(0, 0, 0, 0);
    }
}
