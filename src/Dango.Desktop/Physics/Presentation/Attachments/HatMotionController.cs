using Dango.Core.Animation;

namespace Dango.Desktop.Physics.Presentation.Attachments;

public sealed record HatMotionFrame(HatVisualPose? Pose, double S, double Velocity, double Minimum, double Maximum, string Safety)
{
    public HatFollowFrame Follow { get; init; } = new(0, 0, 0, 0);
}

/// <summary>Sliding only; reads presentation geometry and never owns a physics world.</summary>
public sealed class HatMotionController(PhysicsPresentationFrame? baseline = null)
{
    private HatPlacementSolver? calibration = baseline is null ? null : HatPlacementSolver.Create(AttachmentShapeGeometry.FromFrame(baseline), baseline.Hat);
    private HatPlacementSolver? placement;
    private SpringState state;
    private string safety = "Normal";
    private readonly HatBodyFollow follow = new();
    public HatMotionFrame Current
    {
        get
        {
            var pose = placement?.Fit(state.Position);
            if (pose is not null) pose = pose with { Center = pose.Center + new AttachmentPoint(0, follow.Current.Lift) };
            return new(pose, state.Position, state.Velocity, placement?.Minimum ?? 0, placement?.Maximum ?? 0, safety) { Follow = follow.Current };
        }
    }
    public void ResetSignals() => follow.ResetSignals();
    public void Observe(HatBodySample? body, double aspect, double seconds) => follow.Observe(placement is null ? null : body, aspect, seconds);

    public void Prepare(AttachmentShapeGeometry shape, PresentationAttachment reference)
    {
        calibration ??= HatPlacementSolver.Create(shape, reference);
        var next = calibration?.Remap(shape);
        if (next is null) { placement = null; state = new(0, 0); follow.ResetSignals(); safety = "Unavailable"; return; }
        safety = "Normal";
        if (placement is not null && placement.Fit(state.Position) is { } old)
        {
            using var workspace = next.RentWorkspace();
            // Follow crown motion, then preserve the nearest location along its changing arc.
            var desired = old.Center + next.Fit(0, workspace)!.Center - placement.Fit(0)!.Center;
            double lo = next.Minimum, hi = next.Maximum;
            for (var i = 0; i < 20; i++)
            {
                var a = lo + (hi - lo) / 3; var b = hi - (hi - lo) / 3;
                if ((next.Fit(a, workspace)!.Center - desired).Length <= (next.Fit(b, workspace)!.Center - desired).Length) hi = b;
                else lo = a;
            }
            var mapped = (lo + hi) / 2;
            // Exact unchanged geometry must not accumulate a numerical search bias.
            var unchanged = next.Fit(Math.Clamp(state.Position, next.Minimum, next.Maximum), workspace);
            if (unchanged is not null && (unchanged.Center - desired).Length < 1e-9) mapped = Math.Clamp(state.Position, next.Minimum, next.Maximum);
            state = state with { Position = mapped };
        }
        if (state.Position < next.Minimum || state.Position > next.Maximum)
        { state = state with { Position = Math.Clamp(state.Position, next.Minimum, next.Maximum) }; safety = "Projected"; }
        placement = next;
        Limit();
    }

    public void Pulse(double delta)
    {
        state = DampedSpring.Impulse(state, delta, 0.3);
        Limit();
    }

    public void Advance(double seconds, double tilt, double offset)
    {
        if (placement is null) return;
        follow.Advance(seconds, placement.AvailableLift(state.Position));
        var explicitTarget = offset >= 0 ? offset * placement.Maximum : -offset * placement.Minimum;
        var target = Math.Clamp(explicitTarget + tilt / (Math.PI / 18) * 0.04 + follow.Current.Slide, placement.Minimum, placement.Maximum);
        state = DampedSpring.AdvanceBounded(state, target, 3.5, 0.8, seconds, placement.Minimum, placement.Maximum);
        Limit();
    }

    private void Limit()
    {
        if (placement is null) { state = new(0, 0); return; }
        var v = Math.Clamp(state.Velocity, -0.3, 0.3);
        if ((state.Position <= placement.Minimum + 1e-6 && v < 0) || (state.Position >= placement.Maximum - 1e-6 && v > 0)) v = 0;
        state = state with { Velocity = v };
    }
}
