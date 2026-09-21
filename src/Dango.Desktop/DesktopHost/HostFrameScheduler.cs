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

// Optional wall-clock observation only. All timestamps use the scheduler's timestamp
// provider; RenderingTimeTicks and remainder values instead use TimeSpan ticks.
internal readonly record struct HostFrameStageTiming
{
    public int Version => 1;
    public long Callback { get; init; }
    public long Generation { get; init; }
    public long SchedulerStartedTimestamp { get; init; }
    public long? RenderingTimeTicks { get; init; }
    public long StartedTimestamp { get; init; }
    public long FinishedTimestamp { get; init; }
    public long? PreviousFinishedTimestamp { get; init; }
    public long? PrepareStartedTimestamp { get; init; }
    public long? PrepareFinishedTimestamp { get; init; }
    public long? LoopStartedTimestamp { get; init; }
    public long? LoopFinishedTimestamp { get; init; }
    public long? ApplyStartedTimestamp { get; init; }
    public long? ApplyFinishedTimestamp { get; init; }
    public int RequestedSteps { get; init; }
    public int SuccessfulSteps { get; init; }
    public long RemainderBeforeTicks { get; init; }
    public long? RemainderAfterTicks { get; init; }
    public bool SameGenerationAtExit { get; init; }
    public bool Refresh { get; init; }
    public bool Threw { get; init; }
}

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
    private bool detailedTimingEnabled;
    private long schedulerStartedTimestamp;
    private long? previousDetailedFinish;

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
    internal HostFrameStageTiming? LastStageTiming { get; private set; }
    internal bool DetailedTimingEnabled
    {
        get => detailedTimingEnabled;
        set
        {
            if (detailedTimingEnabled == value) return;
            detailedTimingEnabled = value;
            previousDetailedFinish = null;
            LastStageTiming = null;
        }
    }

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
        schedulerStartedTimestamp = timestamp();
        clock.Start(schedulerStartedTimestamp);
        previousDetailedFinish = null;
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
        var observe = detailedTimingEnabled;
        var timing = observe ? new HostFrameStageTiming
        {
            Callback = counters.Callbacks,
            Generation = generation,
            SchedulerStartedTimestamp = schedulerStartedTimestamp,
            StartedTimestamp = counters.LastStartedTimestamp,
            PreviousFinishedTimestamp = previousDetailedFinish,
            RemainderBeforeTicks = clock.RemainderTicks,
        } : default;
        try
        {
            if (args is not RenderingEventArgs rendering) throw new InvalidOperationException("无效呈现事件。");
            if (observe) timing = timing with { RenderingTimeTicks = rendering.RenderingTime.Ticks };
            if (lastRenderingTime == rendering.RenderingTime)
            {
                counters = counters with { DuplicateCallbacks = counters.DuplicateCallbacks + 1 };
                return;
            }
            lastRenderingTime = rendering.RenderingTime;
            var beforePrepare = runtime.FrameFaultState;
            bool prepared;
            if (observe) timing = timing with { PrepareStartedTimestamp = timestamp() };
            try { prepared = prepare(); }
            finally { if (observe) timing = timing with { PrepareFinishedTimestamp = timestamp() }; }
            if (!prepared || !isCurrent()) return;
            var batch = clock.Advance(counters.LastStartedTimestamp);
            if (observe) timing = timing with { RequestedSteps = batch.Steps };
            if (StackedDangoSceneRuntime.HasNewFrameFault(beforePrepare, runtime.FrameFaultState))
            {
                counters = counters with
                {
                    Faults = counters.Faults + 1,
                    FaultDroppedTicks = counters.FaultDroppedTicks + batch.DroppedTicks + clock.Discard(batch.Steps),
                };
                if (isCurrent() && runtime.LastFrame is { } safe) Refresh(safe, observe, ref timing);
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
                if (isCurrent() && runtime.LastFrame is { } cancelled) Refresh(cancelled, observe, ref timing);
                return;
            }
            if (batch.DroppedTicks > 0)
                counters = counters with { Overloads = counters.Overloads + 1, OverloadDroppedTicks = counters.OverloadDroppedTicks + batch.DroppedTicks };
            if (batch.Steps == 0) return;
            HostFrameResult result;
            // Includes input construction and every requested fixed step, not just native physics.
            if (observe) timing = timing with { LoopStartedTimestamp = timestamp() };
            try { result = loop.Advance(batch.Steps, input(), isCurrent); }
            finally { if (observe) timing = timing with { LoopFinishedTimestamp = timestamp() }; }
            if (observe) timing = timing with { SuccessfulSteps = result.SuccessfulSteps };
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
            if (!result.Healthy) { Refresh(result.Frame, observe, ref timing); return; }
            ApplyObserved(result.Frame, false, observe, ref timing);
            counters = counters with { AppliedFrames = counters.AppliedFrames + 1 };
        }
        catch
        {
            if (observe) timing = timing with { Threw = true };
            throw;
        }
        finally
        {
            counters = counters with { LastFinishedTimestamp = timestamp() };
            if (observe)
            {
                var sameGeneration = timing.Generation == generation;
                LastStageTiming = timing with
                {
                    FinishedTimestamp = counters.LastFinishedTimestamp,
                    SameGenerationAtExit = sameGeneration,
                    RemainderAfterTicks = sameGeneration ? clock.RemainderTicks : null,
                };
                if (sameGeneration && detailedTimingEnabled) previousDetailedFinish = counters.LastFinishedTimestamp;
            }
            inCallback = false;
            CallbackCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Refresh(StackedPhysicsPresentationFrame frame, bool observe, ref HostFrameStageTiming timing)
    {
        ApplyObserved(frame, true, observe, ref timing);
        counters = counters with { RefreshFrames = counters.RefreshFrames + 1 };
    }

    private void ApplyObserved(StackedPhysicsPresentationFrame frame, bool refresh, bool observe, ref HostFrameStageTiming timing)
    {
        if (observe) timing = timing with { ApplyStartedTimestamp = timestamp(), Refresh = refresh };
        try { apply(frame); }
        finally { if (observe) timing = timing with { ApplyFinishedTimestamp = timestamp() }; }
    }
}
