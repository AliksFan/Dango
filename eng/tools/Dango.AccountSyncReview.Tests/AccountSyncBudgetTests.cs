using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
public sealed class AccountSyncBudgetTests
{
    [TestMethod]
    public void Missing_independent_approval_is_inconclusive_before_any_initializer_resource_access()
    {
        var accessed = 0;
        Assert.ThrowsExactly<AssertInconclusiveException>(() => AccountSyncTestOwner.RequireWithFactory(false, () =>
        {
            accessed++;
            throw new AssertFailedException("The unapproved entry reached its resource initializer.");
        }));
        Assert.AreEqual(0, accessed);
    }

    [TestMethod]
    [DataRow(AccountSyncOperation.Invite, 1)]
    [DataRow(AccountSyncOperation.Start, 1)]
    [DataRow(AccountSyncOperation.Confirm, 1)]
    [DataRow(AccountSyncOperation.Complete, 1)]
    [DataRow(AccountSyncOperation.Login, 4)]
    [DataRow(AccountSyncOperation.Profile, 2)]
    [DataRow(AccountSyncOperation.Configuration, 5)]
    [DataRow(AccountSyncOperation.Logout, 4)]
    public void Every_operation_rejects_its_next_attempt_and_permanently_closes_owner(AccountSyncOperation operation, int limit)
    {
        var audit = new Audit(); var budget = Budget(audit);
        for (var index = 0; index < limit; index++) budget.Reserve(operation);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(operation));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.CountHttpRequest());
        Assert.AreEqual(limit, budget.Snapshot().Operations[operation]);
        Assert.AreEqual(limit, audit.Records.Count);
    }

    [TestMethod]
    public void Complete_approved_matrix_is_exactly_nineteen_durable_attempts()
    {
        var audit = new Audit(); var budget = Budget(audit);
        foreach (var item in AccountSyncAttemptBudget.Limits)
            for (var index = 0; index < item.Value; index++) budget.Reserve(item.Key);
        Assert.AreEqual(19, budget.TotalAttempts);
        Assert.AreEqual(19, audit.Records.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 19).Select(value => (long)value).ToArray(), audit.Records.Select(row => row.Sequence).ToArray());
    }

    [TestMethod]
    public void Identical_business_content_and_failed_response_each_still_consume_an_attempt()
    {
        var audit = new Audit(); var budget = Budget(audit);
        for (var index = 0; index < 5; index++)
        {
            budget.Reserve(AccountSyncOperation.Configuration);
            try { throw new OperationCanceledException("simulated unknown response"); }
            catch (OperationCanceledException) { }
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Configuration));
        Assert.AreEqual(5, budget.Snapshot().Operations[AccountSyncOperation.Configuration]);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, audit.Records.Select(row => row.Used).ToArray());
    }

    [TestMethod]
    public void Audit_failure_spends_reservation_but_never_reaches_business_and_closes_owner()
    {
        var audit = new Audit { Fail = true }; var budget = Budget(audit); var forwarded = false;
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            budget.Reserve(AccountSyncOperation.Profile);
            forwarded = true;
        });
        Assert.AreEqual("accountsync_attempt_audit_failed", failure.Message);
        Assert.IsFalse(forwarded);
        Assert.AreEqual(1, budget.Snapshot().Operations[AccountSyncOperation.Profile]);
        audit.Fail = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Profile));
    }

    [TestMethod]
    public void Durable_append_completes_before_reservation_can_forward()
    {
        var audit = new Audit(); var budget = Budget(audit);
        budget.Reserve(AccountSyncOperation.Login);
        Assert.AreEqual(1, audit.Records.Count);
        Assert.AreEqual(1, audit.Flushes);
    }

    [TestMethod]
    public async Task Exactly_one_concurrent_request_can_spend_the_last_login_attempt()
    {
        var audit = new Audit(); var budget = Budget(audit);
        for (var index = 0; index < 3; index++) budget.Reserve(AccountSyncOperation.Login);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            try { budget.Reserve(AccountSyncOperation.Login); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.AreEqual(1, results.Count(passed => passed));
        Assert.AreEqual(4, budget.Snapshot().Operations[AccountSyncOperation.Login]);
        Assert.AreEqual(4, audit.Records.Count);
    }

    [TestMethod]
    public async Task Short_reservation_gate_does_not_serialize_business_execution()
    {
        var audit = new Audit(); var budget = Budget(audit);
        var arrived = 0; var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work()
        {
            budget.Reserve(AccountSyncOperation.Profile);
            if (Interlocked.Increment(ref arrived) == 2) both.SetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await Task.WhenAll(Task.Run(Work), Task.Run(Work));
        Assert.AreEqual(2, audit.Records.Count);
    }

    [TestMethod]
    public void Http_reads_share_one_fixed_counter_and_cannot_refund_or_restart_it()
    {
        var budget = Budget(new Audit());
        for (var index = 0; index < 512; index++) budget.CountHttpRequest();
        Assert.AreEqual(512, budget.HttpRequests);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.CountHttpRequest());
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Login));
        Assert.AreEqual(0, budget.TotalAttempts);
    }

    [TestMethod]
    public void Ten_minute_deadline_closes_business_and_http_at_the_boundary()
    {
        var clock = new Clock(); var budget = Budget(new Audit(), clock);
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        budget.CountHttpRequest();
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Login));
        clock.Advance(TimeSpan.FromMinutes(-10));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.CountHttpRequest());
    }

    [TestMethod]
    public void Owner_loss_rejects_before_durable_append_and_stays_closed_after_owner_returns()
    {
        var alive = true; var audit = new Audit();
        var budget = new AccountSyncAttemptBudget(Guid.NewGuid(), audit, () => { if (!alive) throw new InvalidOperationException(); });
        alive = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Login));
        alive = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Login));
        Assert.AreEqual(0, audit.Records.Count);
    }

    [TestMethod]
    public void Undefined_operation_and_explicit_abort_do_not_allow_later_business()
    {
        var audit = new Audit(); var budget = Budget(audit);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve((AccountSyncOperation)99));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Login));
        var other = Budget(new Audit()); other.Abort();
        Assert.ThrowsExactly<InvalidOperationException>(other.CheckAlive);
    }

    private static AccountSyncAttemptBudget Budget(Audit audit, TimeProvider? clock = null) =>
        new(Guid.NewGuid(), audit, () => { }, clock);

    private sealed class Audit : IAccountSyncAttemptAudit
    {
        internal ConcurrentQueue<AccountSyncAttemptRecord> Records { get; } = new();
        internal bool Fail;
        internal int Flushes;
        public void AppendAndFlush(AccountSyncAttemptRecord record)
        {
            if (Fail) throw new InvalidOperationException("simulated durable write failure");
            Records.Enqueue(record); Flushes++;
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);
        internal void Advance(TimeSpan elapsed) => ticks += elapsed.Ticks;
    }
}
