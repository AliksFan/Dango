using System.Diagnostics;
using System.Reflection;

namespace Dango.ProbeDiagnostics;

// Read-only access to the candidate's explicit diagnostic contract. Missing fields
// fail closed; a broken candidate must never silently fall back to the old timer.
internal sealed class SchedulerObserver : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly object scheduler;
    private readonly EventInfo completedEvent;
    private readonly EventHandler handler;
    private readonly Action<SchedulerSnapshot, SchedulerSnapshot> callback;
    private readonly PropertyInfo? detailedTimingProperty;
    private readonly bool previousDetailedTiming;
    private SchedulerSnapshot previous;
    private bool disposed;

    internal SchedulerObserver(object window, Action<SchedulerSnapshot, SchedulerSnapshot> callback, bool detailedTiming = false)
    {
        scheduler = window.GetType().GetField("motionScheduler", Flags)?.GetValue(window)
            ?? throw new InvalidOperationException("Missing candidate scheduler.");
        this.callback = callback;
        if (detailedTiming)
        {
            detailedTimingProperty = scheduler.GetType().GetProperty("DetailedTimingEnabled", Flags)
                ?? throw new InvalidOperationException("Missing candidate detailed timing contract.");
            previousDetailedTiming = ReadProperty<bool>(scheduler, "DetailedTimingEnabled");
            detailedTimingProperty.SetValue(scheduler, true);
        }
        previous = Read();
        completedEvent = scheduler.GetType().GetEvent("CallbackCompleted", Flags)
            ?? throw new InvalidOperationException("Missing candidate completion event.");
        handler = OnCompleted;
        completedEvent.GetAddMethod(true)!.Invoke(scheduler, [handler]);
    }

    internal static bool IsCandidate(object window) => window.GetType().GetField("motionScheduler", Flags) is not null;
    internal bool IsRunning => ReadProperty<bool>(scheduler, "IsRunning");
    internal SchedulerSnapshot Read()
    {
        var snapshot = SchedulerSnapshot.Read(ReadProperty<object>(scheduler, "Snapshot"));
        if (detailedTimingProperty is null) return snapshot;
        var property = scheduler.GetType().GetProperty("LastStageTiming", Flags)
            ?? throw new InvalidOperationException("Missing candidate detailed timing snapshot.");
        return snapshot with { StageTiming = property.GetValue(scheduler) is { } value ? SchedulerStageTiming.Read(value) : null };
    }

    private void OnCompleted(object? sender, EventArgs args)
    {
        var next = Read();
        var delta = next.Subtract(previous);
        if (delta.Counters["callbacks"] != 1 || delta.Counters["appliedFrames"] > 1 ||
            next.StartedTimestamp <= 0 || next.FinishedTimestamp < next.StartedTimestamp)
            throw new InvalidOperationException("Invalid candidate callback diagnostic delta.");
        if (detailedTimingProperty is not null)
            (next.StageTiming ?? throw new InvalidOperationException("Missing callback stage timing.")).Validate(next);
        previous = next;
        callback(next, delta);
    }

    internal static T ReadProperty<T>(object target, string name) =>
        target.GetType().GetProperty(name, Flags)?.GetValue(target) is T value
            ? value : throw new InvalidOperationException("Invalid diagnostic property: " + name);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        completedEvent.GetRemoveMethod(true)!.Invoke(scheduler, [handler]);
        detailedTimingProperty?.SetValue(scheduler, previousDetailedTiming);
    }
}

internal sealed record SchedulerStageTiming(
    int Version, long Callback, long Generation, long SchedulerStartedTimestamp,
    long? RenderingTimeTicks, long StartedTimestamp, long FinishedTimestamp, long? PreviousFinishedTimestamp,
    long? PrepareStartedTimestamp, long? PrepareFinishedTimestamp,
    long? LoopStartedTimestamp, long? LoopFinishedTimestamp,
    long? ApplyStartedTimestamp, long? ApplyFinishedTimestamp,
    int RequestedSteps, int SuccessfulSteps, long RemainderBeforeTicks, long? RemainderAfterTicks,
    bool SameGenerationAtExit, bool Refresh, bool Threw)
{
    internal static SchedulerStageTiming Read(object value)
    {
        T Required<T>(string name) => SchedulerObserver.ReadProperty<T>(value, name);
        long? Optional(string name)
        {
            var property = value.GetType().GetProperty(name) ?? throw new InvalidOperationException("Missing timing property: " + name);
            return property.GetValue(value) switch
            {
                null => null,
                long number => number,
                _ => throw new InvalidOperationException("Invalid timing property: " + name),
            };
        }
        return new(Required<int>("Version"), Required<long>("Callback"), Required<long>("Generation"),
            Required<long>("SchedulerStartedTimestamp"), Optional("RenderingTimeTicks"),
            Required<long>("StartedTimestamp"), Required<long>("FinishedTimestamp"), Optional("PreviousFinishedTimestamp"),
            Optional("PrepareStartedTimestamp"), Optional("PrepareFinishedTimestamp"),
            Optional("LoopStartedTimestamp"), Optional("LoopFinishedTimestamp"),
            Optional("ApplyStartedTimestamp"), Optional("ApplyFinishedTimestamp"),
            Required<int>("RequestedSteps"), Required<int>("SuccessfulSteps"), Required<long>("RemainderBeforeTicks"), Optional("RemainderAfterTicks"),
            Required<bool>("SameGenerationAtExit"), Required<bool>("Refresh"), Required<bool>("Threw"));
    }

    internal void Validate(SchedulerSnapshot snapshot)
    {
        if (Version != 1 || Callback != snapshot.Counters["callbacks"] || Generation < 1 ||
            StartedTimestamp != snapshot.StartedTimestamp || FinishedTimestamp != snapshot.FinishedTimestamp ||
            SchedulerStartedTimestamp > StartedTimestamp || PreviousFinishedTimestamp > StartedTimestamp ||
            RequestedSteps is < 0 or > 6 || SuccessfulSteps < 0 || SuccessfulSteps > RequestedSteps ||
            RemainderBeforeTicks < 0 || RemainderAfterTicks < 0 || SameGenerationAtExit != RemainderAfterTicks.HasValue)
            throw new InvalidOperationException("Invalid callback stage timing.");
        var previous = StartedTimestamp;
        foreach (var (start, finish) in new[] { (PrepareStartedTimestamp, PrepareFinishedTimestamp), (LoopStartedTimestamp, LoopFinishedTimestamp), (ApplyStartedTimestamp, ApplyFinishedTimestamp) })
        {
            if (start.HasValue != finish.HasValue || start < previous || finish < start || finish > FinishedTimestamp)
                throw new InvalidOperationException("Invalid callback stage ordering.");
            if (finish.HasValue) previous = finish.Value;
        }
    }
}

internal sealed record SchedulerSnapshot(Dictionary<string, long> Counters, long RemainderTicks,
    long StartedTimestamp, long FinishedTimestamp, bool IsRunning)
{
    internal SchedulerStageTiming? StageTiming { get; init; }
    internal static readonly string[] Names = ["Callbacks", "DuplicateCallbacks", "SuccessfulSteps", "AppliedFrames",
        "RefreshFrames", "Overloads", "LongPauses", "ClockResets", "Faults", "OverloadDroppedTicks",
        "PauseDroppedTicks", "FaultDroppedTicks", "LifecycleDroppedTicks"];
    internal static SchedulerSnapshot Zero => new(Names.ToDictionary(Camel, _ => 0L), 0, 0, 0, false);
    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    internal static SchedulerSnapshot Read(object source)
    {
        var counters = Names.ToDictionary(Camel, name => SchedulerObserver.ReadProperty<long>(source, name));
        var snapshot = new SchedulerSnapshot(counters, SchedulerObserver.ReadProperty<long>(source, "RemainderTicks"),
            SchedulerObserver.ReadProperty<long>(source, "LastStartedTimestamp"),
            SchedulerObserver.ReadProperty<long>(source, "LastFinishedTimestamp"),
            SchedulerObserver.ReadProperty<bool>(source, "IsRunning"));
        if (counters.Values.Any(value => value < 0) || snapshot.RemainderTicks < 0)
            throw new InvalidOperationException("Negative diagnostic counter.");
        return snapshot;
    }

    internal SchedulerSnapshot Subtract(SchedulerSnapshot earlier)
    {
        var delta = Counters.ToDictionary(pair => pair.Key, pair => checked(pair.Value - earlier.Counters[pair.Key]));
        if (delta.Values.Any(value => value < 0)) throw new InvalidOperationException("Diagnostic counter regressed.");
        return this with { Counters = delta };
    }

    internal static SchedulerSnapshot Sum(IEnumerable<SchedulerSnapshot> snapshots)
    {
        var result = Zero;
        foreach (var snapshot in snapshots)
        {
            foreach (var pair in snapshot.Counters) result.Counters[pair.Key] = checked(result.Counters[pair.Key] + pair.Value);
            result = result with
            {
                RemainderTicks = checked(result.RemainderTicks + snapshot.RemainderTicks),
                IsRunning = result.IsRunning || snapshot.IsRunning
            };
        }
        return result;
    }

    internal double HandlerElapsedMs => (FinishedTimestamp - StartedTimestamp) * 1000d / Stopwatch.Frequency;
}
