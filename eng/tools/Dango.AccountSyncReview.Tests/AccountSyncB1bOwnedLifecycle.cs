namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1bLifecycleTask(string Phase, bool Settled, bool Successful, bool FaultObserved);

// A timeout bounds this caller, never the actual operation. The original task remains owned and observed.
internal sealed class AccountSyncB1bOwnedLifecycle
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> operations = new(StringComparer.Ordinal);
    private readonly TimeSpan bound;
    internal AccountSyncB1bOwnedLifecycle() : this(TimeSpan.FromSeconds(20)) { }
    internal AccountSyncB1bOwnedLifecycle(TimeSpan bound)
    {
        if (bound <= TimeSpan.Zero || bound > TimeSpan.FromSeconds(20)) throw Reject();
        this.bound = bound;
    }

    internal Task StartAsync(Func<CancellationToken, Task> start) => AwaitAsync("Start", start);
    internal Task StopAsync(Func<CancellationToken, Task> stop) => AwaitAsync("Stop", stop);
    internal Task DisposeAsync(Func<Task> dispose) => AwaitAsync("Dispose", _ => dispose());

    private async Task AwaitAsync(string phase, Func<CancellationToken, Task> operation)
    {
        Entry entry;
        lock (gate)
        {
            if (!operations.TryGetValue(phase, out entry!))
            {
                var cancellation = new CancellationTokenSource(bound);
                Task actual;
                try { actual = operation(cancellation.Token) ?? Task.FromException(Reject()); }
                catch (Exception error) { actual = Task.FromException(error); }
                entry = new(actual, cancellation); operations.Add(phase, entry);
                _ = actual.ContinueWith(completed =>
                {
                    if (completed.IsFaulted) { _ = completed.Exception; lock (gate) entry.FaultObserved = true; }
                    cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        await entry.Actual.WaitAsync(bound);
    }

    internal async Task DrainAsync()
    {
        Task[] actual;
        lock (gate) actual = operations.Values.Select(entry => entry.Actual).ToArray();
        await Task.WhenAll(actual).WaitAsync(bound);
    }

    internal bool IsSuccessful(string phase)
    {
        lock (gate) return operations.TryGetValue(phase, out var entry) && entry.Actual.IsCompletedSuccessfully;
    }

    internal bool TasksSettledSuccessfully
    {
        get
        {
            lock (gate) return operations.Count == 3 && operations.Keys.All(phase => phase is "Start" or "Stop" or "Dispose")
                && operations.Values.All(entry => entry.Actual.IsCompletedSuccessfully);
        }
    }

    internal IReadOnlyList<AccountSyncB1bLifecycleTask> Snapshot()
    {
        lock (gate) return operations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new AccountSyncB1bLifecycleTask(pair.Key, pair.Value.Actual.IsCompleted,
                pair.Value.Actual.IsCompletedSuccessfully, pair.Value.FaultObserved)).ToArray();
    }

    private sealed class Entry(Task actual, CancellationTokenSource cancellation)
    {
        internal Task Actual { get; } = actual;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal bool FaultObserved;
    }
    private static InvalidOperationException Reject() => new("accountsync_b1b_lifecycle_rejected");
}

internal sealed record AccountSyncB1bCleanupFailure(string Step, string FailureType);
internal sealed record AccountSyncB1bCleanupReport(IReadOnlyList<AccountSyncB1bCleanupFailure> Failures)
{
    internal bool Passed => Failures.Count == 0;
    internal void RequireSuccess()
    {
        if (!Passed) throw new InvalidOperationException("accountsync_b1b_cleanup_failed");
    }
}

internal static class AccountSyncB1bCleanup
{
    // Every independent cleanup is attempted. Exception messages and stacks are never an evidence projection.
    internal static async Task<AccountSyncB1bCleanupReport> RunAsync(params (string Step, Func<Task> Action)[] steps)
    {
        var failures = new List<AccountSyncB1bCleanupFailure>();
        foreach (var step in steps)
        {
            try { await step.Action(); }
            catch (Exception error) { failures.Add(new(step.Step, error.GetType().Name)); }
        }
        return new(failures);
    }
}
