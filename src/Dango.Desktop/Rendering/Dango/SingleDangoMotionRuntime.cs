using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public delegate SingleDangoMotionRuntime SingleDangoMotionRuntimeFactory(IPhysicsPresentationSink sink);

public sealed class SingleDangoMotionRuntime : IDisposable
{
    private readonly Func<IPhysicsPresentationWorld> worldFactory;
    private readonly IPhysicsPresentationSink sink;
    private PhysicsPresentationController? controller;
    private bool disposed;

    public SingleDangoMotionRuntime(
        Func<IPhysicsPresentationWorld> worldFactory,
        IPhysicsPresentationSink sink)
    {
        ArgumentNullException.ThrowIfNull(worldFactory);
        ArgumentNullException.ThrowIfNull(sink);
        this.worldFactory = worldFactory;
        this.sink = sink;
    }

    public bool IsRunning => controller is not null;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (controller is not null)
        {
            return;
        }

        controller = new PhysicsPresentationController(worldFactory(), sink);
    }

    public PhysicsPresentationFrame Tick(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return (controller ?? throw new InvalidOperationException("单团子表现运行时尚未启动。"))
            .Tick(input, fixedStep);
    }

    public void Stop()
    {
        controller?.Dispose();
        controller = null;
    }

    public void NotifyHostUnavailable() => Stop();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
