using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.DesktopHost;

internal readonly record struct HostFrameResult(int SuccessfulSteps, bool NewFault, bool Healthy, StackedPhysicsPresentationFrame? Frame);

internal sealed class HostFrameLoop(StackedDangoSceneRuntime runtime)
{
    internal HostFrameResult Advance(int steps, PhysicsPresentationInput input, Func<bool> isCurrent)
    {
        if (!runtime.IsRunning || runtime.LastError is not null) return new(0, false, false, null);
        var completed = 0;
        StackedPhysicsPresentationFrame? frame = null;
        for (var i = 0; i < steps; i++)
        {
            if (!isCurrent()) break;
            var before = runtime.FrameFaultState;
            frame = runtime.Tick(input, HostFrameClock.FixedStep);
            if (runtime.LastError is null && runtime.LastPhysicsFrame?.Succeeded == true) completed++;
            if (StackedDangoSceneRuntime.HasNewFrameFault(before, runtime.FrameFaultState))
                return new(completed, true, false, frame);
            if (!isCurrent()) break;
        }
        return new(completed, false, runtime.AttachmentError is null && runtime.QuietIdleError is null &&
            runtime.LightInteractionError is null, frame);
    }
}
