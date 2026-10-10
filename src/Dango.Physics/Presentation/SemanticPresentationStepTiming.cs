namespace Dango.Physics.Presentation;

public static class SemanticPresentationStepTiming
{
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
