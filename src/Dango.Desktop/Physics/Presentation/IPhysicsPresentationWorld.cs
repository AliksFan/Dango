namespace Dango.Desktop.Physics.Presentation;

public interface IPhysicsPresentationWorld : IDisposable
{
    PhysicsPresentationWorldState State { get; }

    PhysicsPresentationFrame Step(PhysicsPresentationInput input, TimeSpan fixedStep);
}

public abstract class PhysicsPresentationWorldBase : IPhysicsPresentationWorld
{
    private static readonly TimeSpan MaximumFixedStep = TimeSpan.FromMilliseconds(100);

    public PhysicsPresentationWorldState State { get; private set; } = PhysicsPresentationWorldState.Created;

    public PhysicsPresentationFrame Step(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        ObjectDisposedException.ThrowIf(State == PhysicsPresentationWorldState.Disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        if (fixedStep <= TimeSpan.Zero || fixedStep > MaximumFixedStep)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStep), "固定步长必须大于零且不超过 100 ms。");
        }

        var fallbackReason = GetSuspensionReason(input);
        if (fallbackReason != PhysicsPresentationFallbackReason.None)
        {
            State = PhysicsPresentationWorldState.Suspended;
            OnSuspended();
            return AuthoredPoseSampler.CreateFallback(input, fallbackReason);
        }

        var frame = StepActive(input, fixedStep);
        State = frame.FallbackReason == PhysicsPresentationFallbackReason.None
            ? PhysicsPresentationWorldState.Active
            : PhysicsPresentationWorldState.Suspended;
        return frame;
    }

    public void Dispose()
    {
        if (State == PhysicsPresentationWorldState.Disposed)
        {
            return;
        }

        DisposeCore();
        State = PhysicsPresentationWorldState.Disposed;
        GC.SuppressFinalize(this);
    }

    protected abstract PhysicsPresentationFrame StepActive(PhysicsPresentationInput input, TimeSpan fixedStep);

    protected virtual void OnSuspended()
    {
    }

    protected virtual void DisposeCore()
    {
    }

    private static PhysicsPresentationFallbackReason GetSuspensionReason(PhysicsPresentationInput input)
    {
        if (input.Action == Core.Animation.DangoActionState.Hidden)
        {
            return PhysicsPresentationFallbackReason.Hidden;
        }
        if (input.ReducedMotion)
        {
            return PhysicsPresentationFallbackReason.ReducedMotion;
        }

        return input.IsHostAvailable
            ? PhysicsPresentationFallbackReason.None
            : PhysicsPresentationFallbackReason.HostUnavailable;
    }
}
