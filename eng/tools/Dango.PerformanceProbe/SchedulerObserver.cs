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
    private SchedulerSnapshot previous;
    private bool disposed;

    internal SchedulerObserver(object window, Action<SchedulerSnapshot, SchedulerSnapshot> callback)
    {
        scheduler = window.GetType().GetField("motionScheduler", Flags)?.GetValue(window)
            ?? throw new InvalidOperationException("Missing candidate scheduler.");
        this.callback = callback;
        previous = Read();
        completedEvent = scheduler.GetType().GetEvent("CallbackCompleted", Flags)
            ?? throw new InvalidOperationException("Missing candidate completion event.");
        handler = OnCompleted;
        completedEvent.GetAddMethod(true)!.Invoke(scheduler, [handler]);
    }

    internal static bool IsCandidate(object window) => window.GetType().GetField("motionScheduler", Flags) is not null;
    internal bool IsRunning => ReadProperty<bool>(scheduler, "IsRunning");
    internal SchedulerSnapshot Read() => SchedulerSnapshot.Read(ReadProperty<object>(scheduler, "Snapshot"));

    private void OnCompleted(object? sender, EventArgs args)
    {
        var next = Read();
        var delta = next.Subtract(previous);
        if (delta.Counters["callbacks"] != 1 || delta.Counters["appliedFrames"] > 1 ||
            next.StartedTimestamp <= 0 || next.FinishedTimestamp < next.StartedTimestamp)
            throw new InvalidOperationException("Invalid candidate callback diagnostic delta.");
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
    }
}

internal sealed record SchedulerSnapshot(Dictionary<string, long> Counters, long RemainderTicks,
    long StartedTimestamp, long FinishedTimestamp, bool IsRunning)
{
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
