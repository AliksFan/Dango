using System.Diagnostics;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.DesktopHost;

internal interface IHostFrameSource
{
    event EventHandler Rendering;
}

internal sealed class WpfHostFrameSource : IHostFrameSource
{
    public event EventHandler Rendering
    {
        add => CompositionTarget.Rendering += value;
        remove => CompositionTarget.Rendering -= value;
    }
}

internal readonly record struct HostFrameDiagnostics(
    long Callbacks, long DuplicateCallbacks, long SuccessfulSteps, long AppliedFrames, long RefreshFrames,
    long Overloads, long LongPauses, long ClockResets, long Faults,
    long OverloadDroppedTicks, long PauseDroppedTicks, long FaultDroppedTicks, long LifecycleDroppedTicks, long RemainderTicks,
    long LastStartedTimestamp, long LastFinishedTimestamp, bool IsRunning);

internal sealed class HostFrameScheduler
{
    private readonly StackedDangoSceneRuntime runtime;
    private readonly HostFrameClock clock;
    private readonly HostFrameLoop loop;
    private readonly IHostFrameSource source;
    private readonly Func<long> timestamp;
    private readonly Func<bool> prepare;
    private readonly Func<PhysicsPresentationInput> input;
    private readonly Action<StackedPhysicsPresentationFrame> apply;
    private EventHandler? handler;
    private TimeSpan? lastRenderingTime;
    private long generation;
    private bool inCallback;
    private HostFrameDiagnostics counters;

    internal HostFrameScheduler(StackedDangoSceneRuntime runtime, Func<bool> prepare,
        Func<PhysicsPresentationInput> input, Action<StackedPhysicsPresentationFrame> apply,
        IHostFrameSource? source = null, Func<long>? timestamp = null, long frequency = 0)
    {
        this.runtime = runtime;
        this.prepare = prepare;
        this.input = input;
        this.apply = apply;
        this.source = source ?? new WpfHostFrameSource();
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        clock = new(frequency == 0 ? Stopwatch.Frequency : frequency);
        loop = new(runtime);
    }

    internal bool IsRunning => handler is not null;
    internal bool OwnsRuntime => ReferenceEquals(runtime.FrameDriver, this);
    internal event EventHandler? CallbackCompleted;
    internal HostFrameDiagnostics Snapshot => counters with { IsRunning = IsRunning, RemainderTicks = clock.RemainderTicks };

    internal void VerifyCanStart()
    {
        if (runtime.FrameDriver is not null && !OwnsRuntime)
            throw new InvalidOperationException("场景已有活动帧驱动。");
    }

    internal void Start()
    {
        if (IsRunning) return;
        VerifyCanStart();
        if (!runtime.IsRunning) throw new InvalidOperationException("场景尚未启动。");
        runtime.FrameDriver = this;
        clock.Start(timestamp());
        lastRenderingTime = null;
        var currentGeneration = ++generation;
        Func<bool> isCurrent = () => IsRunning && OwnsRuntime && currentGeneration == generation && runtime.IsRunning;
        handler = (_, args) => OnRendering(isCurrent, args);
        source.Rendering += handler;
    }

    internal void Stop()
    {
        if (IsRunning)
        {
            var final = clock.Advance(timestamp());
            counters = counters with
            {
                LifecycleDroppedTicks = counters.LifecycleDroppedTicks + final.DroppedTicks + clock.Discard(final.Steps),
            };
        }
        ++generation;
        if (handler is not null) source.Rendering -= handler;
        handler = null;
        clock.Stop();
        lastRenderingTime = null;
        if (OwnsRuntime) runtime.FrameDriver = null;
    }

    private void OnRendering(Func<bool> isCurrent, EventArgs args)
    {
        if (!isCurrent() || inCallback) return;
        inCallback = true;
        counters = counters with { Callbacks = counters.Callbacks + 1, LastStartedTimestamp = timestamp() };
        try
        {
            if (args is not RenderingEventArgs rendering) throw new InvalidOperationException("无效呈现事件。");
            if (lastRenderingTime == rendering.RenderingTime)
            {
                counters = counters with { DuplicateCallbacks = counters.DuplicateCallbacks + 1 };
                return;
            }
            lastRenderingTime = rendering.RenderingTime;
            var beforePrepare = runtime.FrameFaultState;
            if (!prepare() || !isCurrent()) return;
            var batch = clock.Advance(counters.LastStartedTimestamp);
            if (StackedDangoSceneRuntime.HasNewFrameFault(beforePrepare, runtime.FrameFaultState))
            {
                counters = counters with
                {
                    Faults = counters.Faults + 1,
                    FaultDroppedTicks = counters.FaultDroppedTicks + batch.DroppedTicks + clock.Discard(batch.Steps),
                };
                if (isCurrent() && runtime.LastFrame is { } safe) Refresh(safe);
                return;
            }
            if (batch.Reset)
            {
                counters = counters with
                {
                    LongPauses = counters.LongPauses + (batch.InvalidClock ? 0 : 1),
                    ClockResets = counters.ClockResets + (batch.InvalidClock ? 1 : 0),
                    PauseDroppedTicks = counters.PauseDroppedTicks + batch.DroppedTicks,
                };
                var beforeCancel = runtime.FrameFaultState;
                runtime.CancelInput();
                if (StackedDangoSceneRuntime.HasNewFrameFault(beforeCancel, runtime.FrameFaultState))
                    counters = counters with { Faults = counters.Faults + 1 };
                if (isCurrent() && runtime.LastFrame is { } cancelled) Refresh(cancelled);
                return;
            }
            if (batch.DroppedTicks > 0)
                counters = counters with { Overloads = counters.Overloads + 1, OverloadDroppedTicks = counters.OverloadDroppedTicks + batch.DroppedTicks };
            if (batch.Steps == 0) return;
            var result = loop.Advance(batch.Steps, input(), isCurrent);
            counters = counters with
            {
                SuccessfulSteps = counters.SuccessfulSteps + result.SuccessfulSteps,
                Faults = counters.Faults + (result.NewFault ? 1 : 0),
            };
            if (!isCurrent())
            {
                // Stop already accounted for the old remainder and time since entry.
                // Never discard the clock here: a reentrant Start may own its new baseline.
                counters = counters with
                {
                    LifecycleDroppedTicks = counters.LifecycleDroppedTicks + (batch.Steps - result.SuccessfulSteps) * HostFrameClock.FixedStep.Ticks,
                };
                return;
            }
            if (result.NewFault || result.SuccessfulSteps < batch.Steps)
                counters = counters with
                {
                    FaultDroppedTicks = counters.FaultDroppedTicks + clock.Discard(batch.Steps - result.SuccessfulSteps),
                };
            if (result.Frame is null) return;
            if (!result.Healthy) { Refresh(result.Frame); return; }
            apply(result.Frame);
            counters = counters with { AppliedFrames = counters.AppliedFrames + 1 };
        }
        finally
        {
            counters = counters with { LastFinishedTimestamp = timestamp() };
            inCallback = false;
            CallbackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Refresh(StackedPhysicsPresentationFrame frame)
    {
        apply(frame);
        counters = counters with { RefreshFrames = counters.RefreshFrames + 1 };
    }
}
