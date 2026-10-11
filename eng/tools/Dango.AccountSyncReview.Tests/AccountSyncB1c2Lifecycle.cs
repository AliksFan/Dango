using System.Diagnostics;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1c2LifecycleEvent(int Sequence, long Tick, string Kind,
    int? RequestSequence, int? OperationId, int? CoordinatorId, int? WindowId, string? Value);
internal sealed record AccountSyncB1c2LogoutWindow(int WindowId, string Case, int ClientId,
    Guid UserId, Guid PetId, Guid SessionId, int CoordinatorId, int SaveRequestSequence,
    int LogoutRequestSequence, bool Verified);
internal sealed record AccountSyncB1c2Operation(int Id, int CoordinatorId);

// One lock gives all phase, cancellation and terminal observations a comparable order.
// No exception message, request body, cookie or token is exported.
internal sealed class AccountSyncB1c2Lifecycle
{
    private readonly object gate = new();
    private readonly List<AccountSyncB1c2LifecycleEvent> events = [];
    private readonly List<AccountSyncB1c2LogoutWindow> windows = [];
    private readonly List<Task> observers = [];
    private readonly List<ObservedRemote> remotes = [];
    private readonly AsyncLocal<AccountSyncB1c2Operation?> current = new();
    private int nextOperation;
    internal AccountSyncB1c2Operation? Current => current.Value;
    internal AccountSyncB1c2LifecycleEvent[] Events { get { lock (gate) return events.ToArray(); } }
    internal AccountSyncB1c2LogoutWindow[] Windows { get { lock (gate) return windows.ToArray(); } }
    internal bool ObserversSettled { get { lock (gate) return observers.All(t => t.IsCompletedSuccessfully); } }
    internal void Record(string kind, int? request = null, AccountSyncB1c2Operation? operation = null,
        int? window = null, string? value = null, int? coordinator = null)
    {
        lock (gate)
        {
            if (events.Count >= 4096) throw Reject();
            coordinator ??= operation?.CoordinatorId;
            window ??= coordinator is null ? null : windows.LastOrDefault(w => w.CoordinatorId == coordinator && !w.Verified)?.WindowId;
            events.Add(new(events.Count + 1, Stopwatch.GetTimestamp(), kind, request, operation?.Id, coordinator, window, value));
        }
    }
    internal IRemotePetConfigurationClient ObserveRemote(IRemotePetConfigurationClient remote)
    {
        lock (gate)
        {
            if (remotes.Count >= 4) throw Reject();
            var observed = new ObservedRemote(this, remote, remotes.Count + 1); remotes.Add(observed); return observed;
        }
    }
    internal void Bind(IRemotePetConfigurationClient remote, PetConfigurationLiveSyncCoordinator coordinator)
    {
        if (remote is not ObservedRemote observed || observed.Owner != this || observed.Coordinator is not null) throw Reject();
        observed.Coordinator = coordinator;
        Record("CoordinatorBound", coordinator: observed.Id);
    }
    internal int OpenWindow(string name, int client, Guid user, Guid pet, Guid session, int saved)
    {
        lock (gate)
        {
            var remote = remotes.Last(r => r.UserId == user && r.PetId == pet && r.Coordinator?.State.Status != PetLiveSyncStatus.Stopped);
            if (windows.Count >= 2 || windows.Any(w => !w.Verified) || remote.Coordinator is null || remote.Coordinator.State.FailureCode is not null) throw Reject();
            var id = windows.Count + 1;
            windows.Add(new(id, name, client, user, pet, session, remote.Id, saved, 0, false));
            Record("WindowOpened", window: id); return id;
        }
    }
    internal void CloseWindow(int id, int logout, bool uiLoggedOut)
    {
        lock (gate)
        {
            var row = windows.Single(w => w.WindowId == id);
            var remote = remotes.Single(r => r.Id == row.CoordinatorId);
            var terminals = events.Where(e => e.Kind == "RequestTerminal" && e.RequestSequence == logout).ToArray();
            if (!ObserversSettled || terminals.Length != 1 || terminals[0].Value != "RanToCompletion"
                || !uiLoggedOut || row.Verified || remote.Coordinator?.State.Status != PetLiveSyncStatus.Stopped
                || remote.Coordinator.State.FailureCode is not null) throw Reject();
            // A no-in-flight logout still records the observed stopped coordinator.
            if (!events.Any(e => e.Kind == "CoordinatorStopped" && e.CoordinatorId == remote.Id && e.WindowId == id))
                Record("CoordinatorStopped", coordinator: remote.Id, window: id, value: "StoppedWithoutFailure");
            windows[id - 1] = row with { LogoutRequestSequence = logout, Verified = true };
            Record("WindowClosed", window: id);
        }
    }
    internal void ObserveTask(Task original, Action<Task> terminal)
    {
        // This task reads the actual original Task's terminal status after awaiting it.
        // It is itself drained before evidence can be exported.
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (observers.Count >= 512) throw Reject();
            observers.Add(settled.Task);
        }
        async Task Observe()
        {
            try
            {
                try { await original.ConfigureAwait(false); } catch { _ = original.Exception; }
                terminal(original); settled.TrySetResult();
            }
            catch (Exception error) { settled.TrySetException(error); }
        }
        // A synchronously completed original must not run terminal/registration.Dispose
        // while holding gate: another cancellation callback can need that same gate.
        _ = Observe();
    }
    internal async Task DrainAsync()
    {
        Task[] snapshot; lock (gate) snapshot = observers.ToArray();
        await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        if (!ObserversSettled) throw Reject();
    }
    internal static string Status(Task task) => task.Status switch
    {
        TaskStatus.RanToCompletion => "RanToCompletion", TaskStatus.Canceled => "Canceled", TaskStatus.Faulted => "Faulted", _ => throw Reject()
    };
    private sealed class ObservedRemote(AccountSyncB1c2Lifecycle owner, IRemotePetConfigurationClient inner, int id) : IRemotePetConfigurationClient
    {
        internal AccountSyncB1c2Lifecycle Owner => owner;
        internal int Id => id;
        internal PetConfigurationLiveSyncCoordinator? Coordinator;
        public Uri Origin => inner.Origin;
        public Guid UserId => inner.UserId;
        public Guid PetId => inner.PetId;
        public Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) => Run(() => inner.ReadAsync(ct), ct);
        public Task<PetConfigurationSnapshot> UpdateAsync(long version, JsonElement configuration, CancellationToken ct)
            => Run(() => inner.UpdateAsync(version, configuration, ct), ct);
        private Task<PetConfigurationSnapshot> Run(Func<Task<PetConfigurationSnapshot>> create, CancellationToken ct)
        {
            AccountSyncB1c2Operation operation;
            lock (owner.gate) { if (owner.nextOperation >= 128 || Coordinator is null) throw Reject(); operation = new(++owner.nextOperation, id); }
            owner.Record("OperationStarted", operation: operation);
            var cancellationRecorded = 0;
            void RecordCancellation()
            {
                if (Interlocked.Exchange(ref cancellationRecorded, 1) != 0) return;
                // This is the token supplied to the remote BEFORE HttpClient's timeout token.
                var state = Coordinator!.State;
                owner.Record("CoordinatorStopped", operation: operation, value:
                    state.Status == PetLiveSyncStatus.Stopped && state.FailureCode is null ? "StoppedWithoutFailure" : "StoppedWithFailure");
                owner.Record("OperationCallerCancelled", operation: operation);
            }
            var registration = ct.Register(RecordCancellation);
            var previous = owner.current.Value; owner.current.Value = operation;
            Task<PetConfigurationSnapshot> actual;
            try { actual = create(); }
            catch { registration.Dispose(); throw; }
            finally { owner.current.Value = previous; }
            owner.ObserveTask(actual, terminal =>
            {
                // CTS invokes callbacks in reverse registration order. Inner HttpClient
                // cancellation may finish the Task before this registration is invoked.
                // Read the real original token and share the callback's once-only collector.
                if (ct.IsCancellationRequested) RecordCancellation();
                registration.Dispose(); owner.Record("OperationTerminal", operation: operation, value: Status(terminal));
            });
            return actual;
        }
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c2_lifecycle_rejected");
}
