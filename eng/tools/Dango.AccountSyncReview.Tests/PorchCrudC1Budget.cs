using System.Collections.ObjectModel;

namespace Dango.AccountSyncReview.Tests;

// This C1 owner never resumes. A reservation is spent before durable append, and
// no response, cancellation or failure refunds it. The gate never covers business execution.
internal sealed class PorchCrudC1Budget
{
    internal static readonly IReadOnlyDictionary<PorchCrudC1Operation, int> Limits =
        new ReadOnlyDictionary<PorchCrudC1Operation, int>(new Dictionary<PorchCrudC1Operation, int>
        {
            [PorchCrudC1Operation.Invite] = 2, [PorchCrudC1Operation.Start] = 2,
            [PorchCrudC1Operation.Confirm] = 2, [PorchCrudC1Operation.Complete] = 2,
            [PorchCrudC1Operation.Login] = 2, [PorchCrudC1Operation.Profile] = 2,
            [PorchCrudC1Operation.Logout] = 2,
            [PorchCrudC1Operation.Create] = 1, [PorchCrudC1Operation.Update] = 2,
            [PorchCrudC1Operation.Dissolve] = 2, [PorchCrudC1Operation.Restore] = 2
        });
    internal const int MaximumHttpRequests = 128;
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly Guid ownerId;
    private readonly IAccountSyncAttemptAudit audit;
    private readonly Action assertOwnerAlive;
    private readonly TimeProvider clock;
    private readonly long started;
    private readonly Dictionary<PorchCrudC1Operation, int> used = [];
    private long sequence;
    private int httpRequests;
    private bool aborted;

    internal PorchCrudC1Budget(Guid ownerId, IAccountSyncAttemptAudit audit,
        Action assertOwnerAlive, TimeProvider? clock = null)
    {
        if (ownerId == Guid.Empty) throw Reject();
        this.ownerId = ownerId;
        this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
        this.assertOwnerAlive = assertOwnerAlive ?? throw new ArgumentNullException(nameof(assertOwnerAlive));
        this.clock = clock ?? TimeProvider.System;
        started = this.clock.GetTimestamp();
    }

    internal void Reserve(PorchCrudC1Operation operation)
    {
        lock (gate)
        {
            Check();
            if (!Enum.IsDefined(operation)) { aborted = true; throw Reject(); }
            var count = used.GetValueOrDefault(operation);
            if (count >= Limits[operation])
            { aborted = true; throw new InvalidOperationException("porch_c1_attempt_budget_exhausted"); }
            used[operation] = count + 1;
            sequence++;
            try { audit.AppendAndFlush(new(1, ownerId, sequence, operation.ToString(), count + 1, clock.GetUtcNow())); }
            catch { aborted = true; throw new InvalidOperationException("porch_c1_attempt_audit_failed"); }
        }
    }

    internal void CountHttpRequest()
    {
        lock (gate)
        {
            Check();
            if (httpRequests >= MaximumHttpRequests)
            { aborted = true; throw new InvalidOperationException("porch_c1_http_budget_exhausted"); }
            httpRequests++;
        }
    }

    internal void CheckAlive() { lock (gate) Check(); }
    internal void Abort() { lock (gate) aborted = true; }
    internal int HttpRequests { get { lock (gate) return httpRequests; } }
    internal int TotalAttempts { get { lock (gate) return used.Values.Sum(); } }
    internal PorchCrudC1BudgetSnapshot Snapshot()
    {
        lock (gate) return new(used.Values.Sum(), httpRequests, new ReadOnlyDictionary<PorchCrudC1Operation, int>(
            Limits.Keys.ToDictionary(key => key, key => used.GetValueOrDefault(key))));
    }

    private void Check()
    {
        if (aborted) throw Reject();
        try
        {
            assertOwnerAlive();
            var elapsed = clock.GetElapsedTime(started);
            if (elapsed < TimeSpan.Zero || elapsed >= MaximumDuration)
                throw new InvalidOperationException("porch_c1_duration_exhausted");
        }
        catch { aborted = true; throw Reject(); }
    }

    private static InvalidOperationException Reject() => new("porch_c1_owner_closed");
}

internal enum PorchCrudC1Operation { Invite, Start, Confirm, Complete, Login, Profile, Logout, Create, Update, Dissolve, Restore }
internal sealed record PorchCrudC1BudgetSnapshot(int TotalAttempts, int HttpRequests, IReadOnlyDictionary<PorchCrudC1Operation, int> Operations);
