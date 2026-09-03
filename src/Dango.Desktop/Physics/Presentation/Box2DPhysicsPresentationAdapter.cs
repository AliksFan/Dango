using Dango.Desktop.Physics.Native;

namespace Dango.Desktop.Physics.Presentation;

public sealed class Box2DPhysicsPresentationAdapter : PhysicsPresentationWorldBase
{
    private readonly string _libraryPath;
    private Box2DNativeSession? _session;
    private int _stepCount;

    public Box2DPhysicsPresentationAdapter(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        _libraryPath = libraryPath;
    }

    protected override PhysicsPresentationFrame StepActive(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        if (_session is null && !Box2DNativeLoader.TryLoad(
            _libraryPath,
            Box2DNativeLoader.PinnedRequirements,
            out _session,
            out _))
        {
            return AuthoredPoseSampler.CreateFallback(
                input,
                PhysicsPresentationFallbackReason.NativeUnavailable);
        }

        var probe = _session!.RunLifecycleProbe(1);
        if (!probe.Succeeded || probe.OutstandingBytes != 0)
        {
            ReleaseSession();
            return AuthoredPoseSampler.CreateFallback(
                input,
                PhysicsPresentationFallbackReason.NativeProbeFailed);
        }

        _stepCount++;
        return AuthoredPoseSampler.CreateActive(
            input,
            PhysicsPresentationBackend.Box2D,
            _stepCount,
            PhysicsPresentationDiagnosticCode.Box2DContractProbe);
    }

    protected override void OnSuspended() => ReleaseSession();

    protected override void DisposeCore() => ReleaseSession();

    private void ReleaseSession()
    {
        _session?.Dispose();
        _session = null;
    }
}
