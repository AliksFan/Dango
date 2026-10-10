using System.Net;
using System.Net.Http;
using Dango.Server.Persistence;
using Dango.Server.Pets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1bFaultEvent(long Sequence, string Phase, string Event,
    string? Device = null, int? BackendPid = null, int? StatusCode = null);

internal sealed record AccountSyncB1bFaultSnapshot(string? ActivePhase, bool Failed, bool Disposed,
    int ActiveOperations, int PendingConnectionTasks, int CasArrivals, int CasCompleted,
    IReadOnlyList<int> BackendPids, int AcceptedDropHits, int AcceptedHoldHits,
    int BeforeForwardDropHits, IReadOnlyList<AccountSyncB1bFaultEvent> Events);

// Test-only controls. The fixture still owns request approval, durable reservations,
// response attribution, host generations and resource cleanup. No control refunds an attempt.
internal sealed class AccountSyncB1bFaultControl : IAsyncDisposable
{
    internal const string PrimaryDevice = "primary";
    internal const string SecondaryDevice = "secondary";
    internal const string ConfigurationPath = "/api/v1/me/pet/configuration";
    private const string Cas = "cas";
    private const string DropAccepted = "drop-accepted";
    private const string HoldAccepted = "hold-accepted";
    private const string DropBeforeForward = "drop-before-forward";
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan casTimeout, responseTimeout, drainTimeout;
    private readonly HashSet<string> armed = [];
    private readonly HashSet<Task> connectionTasks = [];
    private readonly List<int> backendPids = [];
    private readonly List<AccountSyncB1bFaultEvent> events = [];
    private readonly HashSet<string> casForwarded = [];
    private readonly TaskCompletionSource casRelease = NewSignal();
    private readonly TaskCompletionSource acceptedPatch = NewSignal();
    private readonly TaskCompletionSource acceptedRelease = NewSignal();
    private TaskCompletionSource drained = CompletedSignal();
    private Task? disposal;
    private string? phase;
    private bool failed, disposed, holdReleased;
    private int activeOperations, casArrivals, casCompleted;
    private int acceptedDropHits, acceptedHoldHits, beforeForwardDropHits;
    private long sequence;

    internal AccountSyncB1bFaultControl() : this(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)) { }

    // Shorter bounds exist only for resource-free tests; actual controls cannot widen the fixed bounds.
    internal AccountSyncB1bFaultControl(TimeSpan casTimeout, TimeSpan responseTimeout, TimeSpan drainTimeout)
    {
        RequireBound(casTimeout, TimeSpan.FromSeconds(10));
        RequireBound(responseTimeout, TimeSpan.FromSeconds(20));
        RequireBound(drainTimeout, TimeSpan.FromSeconds(20));
        this.casTimeout = casTimeout; this.responseTimeout = responseTimeout; this.drainTimeout = drainTimeout;
    }

    internal void ArmCas() => Arm(Cas);
    internal void ArmDropAcceptedResponse(bool hold) => Arm(hold ? HoldAccepted : DropAccepted);
    internal void ArmDropBeforeForward() => Arm(DropBeforeForward);

    internal IPetConfigurationCheckpoint CreateCheckpoint(DangoDbContext db) => new ScopedCheckpoint(this, async ct =>
    {
        await db.Database.OpenConnectionAsync(ct);
        if (db.Database.GetDbConnection() is not NpgsqlConnection connection || connection.ProcessID <= 0)
            throw Reject("cas_backend_identity_required");
        // Keep the scoped connection open across the barrier and transaction. A pool checkout
        // performed and closed before the barrier could allow both requests to reuse one backend.
        return connection.ProcessID;
    });

    internal IPetConfigurationCheckpoint CreateOfflineCheckpoint(Func<CancellationToken, Task<int>> open) =>
        new ScopedCheckpoint(this, open);

    internal Task BeforeForwardAsync(string device, string method, string path, CancellationToken ct)
    {
        RequireDevice(device); ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            Check();
            if (!IsConfigurationPatch(method, path)) return Task.CompletedTask;
            if (phase == Cas)
            {
                if (!casForwarded.Add(device)) { Fail("cas_device_forwarded_twice"); throw Reject("cas_device_forwarded_twice"); }
                Record(Cas, "patch-forwarding", device);
            }
            if (phase != DropBeforeForward || device != PrimaryDevice) return Task.CompletedTask;
            beforeForwardDropHits++;
            Record(DropBeforeForward, "reserved-patch-not-forwarded", device);
            phase = null;
        }
        // Call this only after the fixture has durably reserved the attempt.
        throw new HttpRequestException("accountsync_b1b_reserved_request_not_forwarded");
    }

    internal async Task AfterResponseAsync(string device, string method, string path,
        HttpResponseMessage response, CancellationToken ct)
    {
        RequireDevice(device);
        ArgumentNullException.ThrowIfNull(response);
        string selected;
        lock (gate)
        {
            Check();
            if (!IsConfigurationPatch(method, path)) return;
            if (armed.Contains(Cas) && casForwarded.Contains(device) && casCompleted < 2)
                Record(Cas, "patch-response", device, statusCode: (int)response.StatusCode);
            if (device != PrimaryDevice || response.StatusCode != HttpStatusCode.OK
                || phase is not (DropAccepted or HoldAccepted)) return;
            selected = phase!;
            if (selected == DropAccepted)
            {
                if (acceptedDropHits != 0) return;
                acceptedDropHits++;
            }
            else
            {
                if (acceptedHoldHits != 0) return;
                acceptedHoldHits++;
            }
            Enter();
            Record(selected, "accepted-response-captured", device, statusCode: (int)response.StatusCode);
            if (selected == HoldAccepted) acceptedPatch.TrySetResult();
        }
        try
        {
            if (selected == HoldAccepted)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
                await acceptedRelease.Task.WaitAsync(responseTimeout, linked.Token);
            }
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                Check();
                Record(selected, "accepted-response-discarded", device);
                phase = null;
            }
            throw new HttpRequestException("accountsync_b1b_accepted_response_lost");
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            lock (gate) Fail(error is TimeoutException ? "accepted_response_hold_timeout" : "accepted_response_hold_cancelled");
            throw;
        }
        finally
        {
            response.Dispose();
            lock (gate) Exit();
        }
    }

    internal async Task WaitForAcceptedPatchAsync()
    {
        lock (gate)
        {
            Check();
            if (!armed.Contains(HoldAccepted)) throw Reject("accepted_hold_not_armed");
            Enter();
        }
        try { await acceptedPatch.Task.WaitAsync(responseTimeout, lifetime.Token); }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            lock (gate) Fail(error is TimeoutException ? "accepted_patch_wait_timeout" : "accepted_patch_wait_cancelled");
            throw;
        }
        finally { lock (gate) Exit(); }
    }

    internal void ReleaseAcceptedPatch()
    {
        lock (gate)
        {
            Check();
            if (phase != HoldAccepted || acceptedHoldHits != 1 || holdReleased) throw Reject("accepted_release_rejected");
            holdReleased = true;
            Record(HoldAccepted, "accepted-response-released", PrimaryDevice);
            acceptedRelease.TrySetResult();
        }
    }

    internal AccountSyncB1bFaultSnapshot Snapshot()
    {
        lock (gate) return new(phase, failed, disposed, activeOperations, connectionTasks.Count,
            casArrivals, casCompleted, backendPids.ToArray(), acceptedDropHits, acceptedHoldHits,
            beforeForwardDropHits, events.ToArray());
    }

    internal void AssertSettled()
    {
        lock (gate)
        {
            if (failed || phase is not null || activeOperations != 0 || connectionTasks.Count != 0)
                throw Reject("fault_control_not_settled");
            if (armed.Contains(Cas) && (casArrivals != 2 || casCompleted != 2 || backendPids.Distinct().Count() != 2
                || !casForwarded.SetEquals([PrimaryDevice, SecondaryDevice]))) throw Reject("cas_evidence_incomplete");
            if (armed.Contains(DropAccepted) && acceptedDropHits != 1
                || armed.Contains(HoldAccepted) && (acceptedHoldHits != 1 || !holdReleased)
                || armed.Contains(DropBeforeForward) && beforeForwardDropHits != 1) throw Reject("fault_hits_incomplete");
        }
    }

    private void Arm(string requested)
    {
        lock (gate)
        {
            Check();
            if (phase is not null || activeOperations != 0 || !armed.Add(requested)) throw Reject("fault_arm_rejected");
            phase = requested;
            Record(requested, "armed");
        }
    }

    private async Task ReachCasAsync(Func<CancellationToken, Task<int>> open, CancellationToken ct)
    {
        lock (gate)
        {
            Check();
            if (phase != Cas) return;
            Enter(); Record(Cas, "connection-open-started");
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            var opening = open(linked.Token);
            TrackConnection(opening);
            var pid = await opening.WaitAsync(casTimeout, linked.Token);
            lock (gate)
            {
                Check();
                if (pid <= 0 || backendPids.Contains(pid) || casArrivals >= 2)
                { Fail("cas_backend_arrival_rejected"); throw Reject("cas_backend_arrival_rejected"); }
                backendPids.Add(pid); casArrivals++;
                Record(Cas, "before-locks-arrived", backendPid: pid);
                if (casArrivals == 2) casRelease.TrySetResult();
            }
            await casRelease.Task.WaitAsync(casTimeout, linked.Token);
            lock (gate)
            {
                Check(); casCompleted++;
                Record(Cas, "before-locks-released", backendPid: pid);
                if (casCompleted == 2) phase = null;
            }
        }
        catch (Exception error)
        {
            lock (gate) Fail(error switch
            {
                TimeoutException => "cas_wait_timeout",
                OperationCanceledException => "cas_wait_cancelled",
                _ => "cas_checkpoint_failed"
            });
            throw;
        }
        finally { lock (gate) Exit(); }
    }

    private void TrackConnection(Task task)
    {
        lock (gate) { Enter(); connectionTasks.Add(task); }
        _ = task.ContinueWith(completed =>
        {
            // Observe failures even when the bounded callback stopped awaiting an uncooperative open.
            _ = completed.Exception;
            lock (gate) { connectionTasks.Remove(completed); Exit(); }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Task pending;
        lock (gate)
        {
            disposed = true;
            if (phase is not null) Fail("armed_phase_abandoned");
            Record("cleanup", "dispose-started"); pending = drained.Task;
            casRelease.TrySetResult(); acceptedRelease.TrySetResult();
        }
        lifetime.Cancel();
        try
        {
            await pending.WaitAsync(drainTimeout);
            lock (gate) Record("cleanup", "dispose-drained");
            lifetime.Dispose();
        }
        catch (TimeoutException)
        {
            lock (gate) Fail("fault_control_drain_timeout");
            throw Reject("fault_control_drain_timeout");
        }
    }

    private void Check()
    {
        if (disposed || failed) throw Reject("fault_control_closed");
    }
    private void Fail(string code)
    {
        failed = true; Record(phase ?? "cleanup", code);
        casRelease.TrySetResult(); acceptedRelease.TrySetResult();
    }
    private void Enter()
    {
        if (activeOperations++ == 0) drained = NewSignal();
    }
    private void Exit()
    {
        if (--activeOperations == 0) drained.TrySetResult();
    }
    private void Record(string eventPhase, string name, string? device = null, int? backendPid = null, int? statusCode = null) =>
        events.Add(new(++sequence, eventPhase, name, device, backendPid, statusCode));
    private static bool IsConfigurationPatch(string method, string path) => method == "PATCH" && path == ConfigurationPath;
    private static void RequireDevice(string device)
    {
        if (device is not (PrimaryDevice or SecondaryDevice)) throw Reject("device_rejected");
    }
    private static void RequireBound(TimeSpan value, TimeSpan maximum)
    {
        if (value <= TimeSpan.Zero || value > maximum) throw Reject("fault_bound_rejected");
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSignal() { var signal = NewSignal(); signal.SetResult(); return signal; }
    private static InvalidOperationException Reject(string code) => new("accountsync_b1b_" + code);

    private sealed class ScopedCheckpoint(AccountSyncB1bFaultControl owner, Func<CancellationToken, Task<int>> open) : IPetConfigurationCheckpoint
    {
        private int beforeLocksCalled;
        public Task ReachedAsync(string name, CancellationToken ct)
        {
            if (name != "before_locks") return Task.CompletedTask;
            if (Interlocked.Exchange(ref beforeLocksCalled, 1) != 0) throw Reject("checkpoint_repeated");
            return owner.ReachCasAsync(open, ct);
        }
    }
}
