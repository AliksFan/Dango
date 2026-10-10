namespace Dango.Physics.Presentation;

public static class SemanticPresentationStepTiming
{
    public static SemanticPoint PrescribedPosition(DangoPresentationTarget start, DangoPresentationTarget end, double fraction)
    {
        if (start.EntityId != end.EntityId || !double.IsFinite(fraction) || fraction is < 0 or > 1)
            throw new ArgumentException("A prescribed substep requires one body and a bounded common-time fraction.");
        var seconds = SemanticPresentationSampler.FixedStep.TotalSeconds;
        var squared = fraction * fraction;
        var cubed = squared * fraction;
        double Axis(double first, double second, double firstVelocity, double secondVelocity) =>
            (2 * cubed - 3 * squared + 1) * first + (cubed - 2 * squared + fraction) * seconds * firstVelocity +
            (-2 * cubed + 3 * squared) * second + (cubed - squared) * seconds * secondVelocity;
        return new(Axis(start.CoreX, end.CoreX, start.CoreVelocityX, end.CoreVelocityX),
            Axis(start.CoreY, end.CoreY, start.CoreVelocityY, end.CoreVelocityY));
    }

    /// <summary>The target is at T-dt; the returned local contour is sampled after stepping to T.</summary>
    public static DangoPresentationTarget TargetAtStart(DangoEntityId localEntity, SemanticBodyState current,
        SemanticBodyState? previous)
    {
        if (previous is not null && previous.EntityId != current.EntityId)
            throw new ArgumentException("A step-start sample must belong to the same body.", nameof(previous));
        var seconds = SemanticPresentationSampler.FixedStep.TotalSeconds;
        return previous is not null
            ? new(localEntity, (float)previous.CoreX, (float)previous.CoreY, (float)previous.CoreVelocityX, (float)previous.CoreVelocityY)
            : new(localEntity, (float)(current.CoreX - current.CoreVelocityX * seconds),
                (float)(current.CoreY - current.CoreVelocityY * seconds), (float)current.CoreVelocityX, (float)current.CoreVelocityY);
    }
}
