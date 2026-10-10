using System.Collections.ObjectModel;

namespace Dango.AccountSyncReview.Tests;

// This B1b1 owner never resumes. A reservation is spent before durable append, and
// no response, cancellation or failure refunds it. The gate never covers business execution.
internal sealed class AccountSyncB1bAttemptBudget
{
    internal static readonly IReadOnlyDictionary<AccountSyncOperation, int> Limits =
        new ReadOnlyDictionary<AccountSyncOperation, int>(new Dictionary<AccountSyncOperation, int>
        {
            [AccountSyncOperation.Invite] = 1, [AccountSyncOperation.Start] = 1,
            [AccountSyncOperation.Confirm] = 1, [AccountSyncOperation.Complete] = 1,
            [AccountSyncOperation.Login] = 2, [AccountSyncOperation.Profile] = 1,
            [AccountSyncOperation.Configuration] = 8, [AccountSyncOperation.Logout] = 2
        });
    internal const int MaximumHttpRequests = 128;
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly Guid ownerId;
    private readonly IAccountSyncAttemptAudit audit;
    private readonly Action assertOwnerAlive;
    private readonly TimeProvider clock;
    private readonly long started;
    private readonly Dictionary<AccountSyncOperation, int> used = [];
    private long sequence;
    private int httpRequests;
    private bool aborted;

    internal AccountSyncB1bAttemptBudget(Guid ownerId, IAccountSyncAttemptAudit audit,
        Action assertOwnerAlive, TimeProvider? clock = null)
    {
        if (ownerId == Guid.Empty) throw Reject();
        this.ownerId = ownerId;
        this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
        this.assertOwnerAlive = assertOwnerAlive ?? throw new ArgumentNullException(nameof(assertOwnerAlive));
        this.clock = clock ?? TimeProvider.System;
        started = this.clock.GetTimestamp();
    }

    internal void Reserve(AccountSyncOperation operation)
    {
        lock (gate)
        {
            Check();
            if (!Enum.IsDefined(operation)) { aborted = true; throw Reject(); }
            var count = used.GetValueOrDefault(operation);
            if (count >= Limits[operation])
            { aborted = true; throw new InvalidOperationException("accountsync_attempt_budget_exhausted"); }
            used[operation] = count + 1;
            sequence++;
            try { audit.AppendAndFlush(new(1, ownerId, sequence, operation.ToString(), count + 1, clock.GetUtcNow())); }
            catch { aborted = true; throw new InvalidOperationException("accountsync_attempt_audit_failed"); }
        }
    }

    internal void CountHttpRequest()
    {
        lock (gate)
        {
            Check();
            if (httpRequests >= MaximumHttpRequests)
            { aborted = true; throw new InvalidOperationException("accountsync_http_budget_exhausted"); }
            httpRequests++;
        }
    }

    internal void CheckAlive() { lock (gate) Check(); }
    internal void Abort() { lock (gate) aborted = true; }
    internal int HttpRequests { get { lock (gate) return httpRequests; } }
    internal int TotalAttempts { get { lock (gate) return used.Values.Sum(); } }
    internal AccountSyncBudgetSnapshot Snapshot()
    {
        lock (gate) return new(used.Values.Sum(), httpRequests, new ReadOnlyDictionary<AccountSyncOperation, int>(
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
                throw new InvalidOperationException("accountsync_duration_exhausted");
        }
        catch { aborted = true; throw Reject(); }
    }

    private static InvalidOperationException Reject() => new("accountsync_owner_closed");
}
