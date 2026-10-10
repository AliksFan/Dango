using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
public sealed class ProfileTestBoundaryTests
{
    [TestMethod]
    public async Task CompetingForLastAttemptAllowsOneBusinessCall()
    {
        var audit = new MemoryAudit();
        var budget = NewBudget(audit); budget.BeginCase("concurrent_profile");
        budget.Reserve("concurrent_profile", ProfileAttemptOperation.DirectProfileUpdate);
        var executed = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Call()
        {
            await start.Task;
            try { budget.Reserve("concurrent_profile", ProfileAttemptOperation.DirectProfileUpdate); }
            catch (InvalidOperationException) { return false; }
            Interlocked.Increment(ref executed); return true;
        }
        var first = Task.Run(Call); var second = Task.Run(Call); start.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.AreEqual(1, outcomes.Count(value => value)); Assert.AreEqual(1, executed);
        Assert.AreEqual(2, audit.Attempts.Count); Assert.AreEqual(2, budget.Snapshot()[ProfileAttemptOperation.DirectProfileUpdate]);
    }

    [TestMethod]
    public void RepeatingSamePayloadStillConsumesEachAttempt()
    {
        var audit = new MemoryAudit(); var budget = NewBudget(audit); budget.BeginCase("concurrent_profile");
        var identical = new Dango.Protocol.AccountProfileUpdate(1, "重复昵称");
        foreach (var update in new[] { identical, identical })
        { budget.Reserve("concurrent_profile", ProfileAttemptOperation.DirectProfileUpdate); Assert.AreEqual(1L, update.ExpectedVersion); }
        Assert.AreEqual(2, audit.Attempts.Count); Assert.AreEqual(2, audit.Attempts[^1].Used);
    }

    [TestMethod]
    public void FailedAuditConsumesAttemptAndPreventsBusinessOrRetry()
    {
        var audit = new MemoryAudit { FailedAttempt = 1 }; var budget = NewBudget(audit); budget.BeginCase("concurrent_profile");
        var executed = 0;
        void Call() { budget.Reserve("concurrent_profile", ProfileAttemptOperation.DirectProfileUpdate); executed++; }
        Assert.ThrowsExactly<IOException>(Call);
        Assert.AreEqual(1, budget.Snapshot()[ProfileAttemptOperation.DirectProfileUpdate]);
        Assert.ThrowsExactly<InvalidOperationException>(Call); Assert.AreEqual(0, executed);
    }

    [TestMethod]
    public async Task SameVersionDifferentPayloadsReachBusinessConcurrently()
    {
        var budget = NewBudget(new MemoryAudit()); budget.BeginCase("concurrent_profile");
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<string> Call(string payload)
        {
            budget.Reserve("concurrent_profile", ProfileAttemptOperation.DirectProfileUpdate);
            if (Interlocked.Increment(ref arrivals) == 2) allArrived.SetResult();
            await release.Task; return payload;
        }
        var first = Call("甲"); var second = Call("乙");
        try { await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { release.TrySetResult(); }
        CollectionAssert.AreEquivalent(new[] { "甲", "乙" }, await Task.WhenAll(first, second));
    }

    [TestMethod]
    public void OwnerLossCannotBeRecoveredAndCaseCannotBeReplayed()
    {
        var alive = true; var budget = NewBudget(new MemoryAudit(), () => { if (!alive) throw new InvalidOperationException(); });
        budget.BeginCase("concurrent_profile"); alive = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve("concurrent_profile", ProfileAttemptOperation.Invite));
        alive = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve("concurrent_profile", ProfileAttemptOperation.Invite));
        Assert.AreEqual(0, budget.Snapshot()[ProfileAttemptOperation.Invite]);
        var fresh = NewBudget(new MemoryAudit()); fresh.BeginCase("concurrent_profile");
        Assert.ThrowsExactly<InvalidOperationException>(() => fresh.BeginCase("concurrent_profile"));
        Assert.ThrowsExactly<InvalidOperationException>(() => fresh.Reserve("concurrent_profile", ProfileAttemptOperation.Invite));
    }

    [TestMethod]
    public void SevenFixedCasesExactlyConsumeSixtySevenAttempts()
    {
        var audit = new MemoryAudit(); var budget = NewBudget(audit);
        foreach (var name in CaseNames)
        {
            budget.BeginCase(name);
            foreach (var (operation, limit) in ProfileAttemptBudget.CaseLimits(name))
                for (var index = 0; index < limit; index++) budget.Reserve(name, operation);
            if (name == "https") for (var index = 0; index < 22; index++) budget.CountHttpRequest(name);
            budget.EndCase(name, true);
        }
        Assert.AreEqual(67, audit.Attempts.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 67).Select(value => (long)value).ToArray(), audit.Attempts.Select(record => record.Sequence).ToArray());
        foreach (var (operation, limit) in ProfileAttemptBudget.Limits) Assert.AreEqual(limit, budget.Snapshot()[operation]);
    }

    [TestMethod]
    public void IncompleteSuccessfulCaseAndExtraHttpRequestAbortFurtherCalls()
    {
        var budget = NewBudget(new MemoryAudit()); budget.BeginCase("concurrent_profile");
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.EndCase("concurrent_profile", true));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.BeginCase("https"));
        var http = NewBudget(new MemoryAudit()); http.BeginCase("https");
        for (var index = 0; index < 22; index++) http.CountHttpRequest("https");
        Assert.ThrowsExactly<InvalidOperationException>(() => http.CountHttpRequest("https"));
        Assert.ThrowsExactly<InvalidOperationException>(() => http.Reserve("https", ProfileAttemptOperation.Login));
    }

    [TestMethod]
    public void InvalidCaseOrOperationCannotResumeTheSameBudget()
    {
        var invalidCase = NewBudget(new MemoryAudit());
        Assert.ThrowsExactly<InvalidOperationException>(() => invalidCase.BeginCase("unapproved_extra_case"));
        Assert.ThrowsExactly<InvalidOperationException>(() => invalidCase.BeginCase("expiry"));
        var invalidOperation = NewBudget(new MemoryAudit()); invalidOperation.BeginCase("expiry");
        Assert.ThrowsExactly<InvalidOperationException>(() => invalidOperation.Reserve("expiry", (ProfileAttemptOperation)100));
        Assert.ThrowsExactly<InvalidOperationException>(() => invalidOperation.Reserve("expiry", ProfileAttemptOperation.Invite));
    }

    [TestMethod]
    public async Task ForeignRequestUriNeverReservesOrReachesTransport()
    {
        var forwards = 0; var counts = 0; var reserves = 0;
        using var client = new HttpClient(new ProfileTestSupport.ProfileHttpBoundary(new Terminal(_ =>
        { forwards++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }), Endpoint, () => counts++, _ => reserves++));
        foreach (var uri in new[] { "https://foreign.invalid:49123/api/v1/me/profile", "http://127.0.0.1:49123/api/v1/me/profile",
            "https://127.0.0.1:49124/api/v1/me/profile", "https://person@127.0.0.1:49123/api/v1/me/profile",
            "https://127.0.0.1:49123/api/v1/me/profile?extra=1", "https://127.0.0.1:49123/api/v1/me/profile#fragment" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, uri);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.SendAsync(request));
        }
        Assert.AreEqual(0, forwards); Assert.AreEqual(0, counts); Assert.AreEqual(0, reserves);
    }

    [TestMethod]
    public async Task OriginAndCsrfAndMalformedBodyReachTransportUnchanged()
    {
        var observed = new List<(string? Origin, bool Csrf, string Body)>(); var counts = 0; var reserves = 0;
        using var client = new HttpClient(new ProfileTestSupport.ProfileHttpBoundary(new Terminal(async request =>
        {
            observed.Add((request.Headers.TryGetValues("Origin", out var origins) ? origins.Single() : null,
                request.Headers.Contains("X-CSRF-TOKEN"), await request.Content!.ReadAsStringAsync()));
            return new(HttpStatusCode.Forbidden);
        }), Endpoint, () => counts++, operation => { Assert.AreEqual(ProfileAttemptOperation.HttpProfilePatch, operation); reserves++; }));
        const string malformed = "{\"expectedVersion\":1,\"nickname\":null}";
        using var foreign = new HttpRequestMessage(HttpMethod.Patch, new Uri(Endpoint, "api/v1/me/profile")) { Content = new StringContent(malformed) };
        foreign.Headers.Add("Origin", "https://foreign.invalid"); foreign.Headers.Add("X-CSRF-TOKEN", "fictional-test-token");
        using var missing = new HttpRequestMessage(HttpMethod.Patch, new Uri(Endpoint, "api/v1/me/profile")) { Content = new StringContent(malformed) };
        using var first = await client.SendAsync(foreign); using var second = await client.SendAsync(missing);
        Assert.AreEqual(("https://foreign.invalid", true, malformed), observed[0]);
        Assert.AreEqual(((string?)null, false, malformed), observed[1]); Assert.AreEqual(2, counts); Assert.AreEqual(2, reserves);
    }

    [TestMethod]
    public async Task CleanupRunsEvenAfterOwnerLossAndDisposeStartedAuditFailure()
    {
        var audit = new MemoryAudit(); var alive = true; var aborted = false;
        var resources = new ProfileResourceBudget(Guid.NewGuid(), audit, () => { if (!alive || aborted) throw new InvalidOperationException(); }, () => aborted = true);
        var fixture = new FakeResource();
        var scope = await ProfileFixtureScope<FakeResource>.CreateAsync(resources, "concurrent_profile", false, () => Task.FromResult(fixture));
        alive = false; audit.FailedPhase = "dispose_started";
        await Assert.ThrowsExactlyAsync<IOException>(() => scope.DisposeAsync().AsTask());
        Assert.AreEqual(1, fixture.Disposals); Assert.IsTrue(aborted);
        Assert.AreEqual("disposed", audit.Resources[^1].Phase); Assert.IsTrue(audit.Resources[^1].NativeKeyDisposeConfirmed);
        var factories = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ProfileFixtureScope<FakeResource>.CreateAsync(resources, "expiry", false,
            () => { factories++; return Task.FromResult(new FakeResource()); }));
        Assert.AreEqual(0, factories);
    }

    [TestMethod]
    public async Task ReadyAuditFailureDisposesCreatedFixtureAndFailedDisposeNeverClaimsCleanup()
    {
        var audit = new MemoryAudit { FailedPhase = "ready" }; var aborted = false;
        var resources = new ProfileResourceBudget(Guid.NewGuid(), audit, () => { if (aborted) throw new InvalidOperationException(); }, () => aborted = true);
        var fixture = new FakeResource();
        await Assert.ThrowsExactlyAsync<IOException>(() => ProfileFixtureScope<FakeResource>.CreateAsync(resources, "concurrent_profile", false, () => Task.FromResult(fixture)));
        Assert.AreEqual(1, fixture.Disposals); Assert.IsTrue(aborted);
        var failingAudit = new MemoryAudit();
        var failing = await ProfileFixtureScope<FakeResource>.CreateAsync(new(Guid.NewGuid(), failingAudit, () => { }, () => { }),
            "expiry", false, () => Task.FromResult(new FakeResource { FailDispose = true }));
        await Assert.ThrowsExactlyAsync<IOException>(() => failing.DisposeAsync().AsTask());
        Assert.AreEqual("dispose_failed", failingAudit.Resources[^1].Phase);
        Assert.IsFalse(failingAudit.Resources.Any(record => record.NativeKeyDisposeConfirmed));
    }

    [TestMethod]
    public async Task SevenFixturesProduceTwentyEightReceiptsAndExactlyOneHttps()
    {
        var audit = new MemoryAudit(); var resources = new ProfileResourceBudget(Guid.NewGuid(), audit, () => { }, () => { });
        foreach (var name in CaseNames)
        {
            await using var scope = await ProfileFixtureScope<FakeResource>.CreateAsync(resources, name, name == "https", () => Task.FromResult(new FakeResource()));
        }
        Assert.AreEqual(28, audit.Resources.Count); Assert.AreEqual(7, audit.Resources.Select(record => record.FixtureId).Distinct().Count());
        Assert.AreEqual(1, audit.Resources.Count(record => record.Phase == "ready" && record.HttpsStarted));
        Assert.AreEqual(7, audit.Resources.Count(record => record.Phase == "disposed" && record.NativeKeyDisposeConfirmed));
        Assert.IsTrue(audit.Resources.All(record => record.RequestedHttps == (record.CaseId == "https") && record.AtUtc.Offset == TimeSpan.Zero));
        CollectionAssert.AreEqual(Enumerable.Range(1, 28).Select(value => (long)value).ToArray(), audit.Resources.Select(record => record.Sequence).ToArray());
    }

    private static readonly Uri Endpoint = new("https://127.0.0.1:49123/");
    private static readonly string[] CaseNames = ["concurrent_profile", "duplicate_names", "rollback_after_profile", "rollback_before_commit", "cross_session", "expiry", "https"];
    private static ProfileAttemptBudget NewBudget(MemoryAudit audit, Action? alive = null) => new(Guid.NewGuid(), audit, alive ?? (() => { }));
    private sealed class Terminal(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private sealed class FakeResource : IAsyncDisposable
    {
        internal int Disposals; internal bool FailDispose;
        public ValueTask DisposeAsync() { Disposals++; return FailDispose ? ValueTask.FromException(new IOException("fictional_cleanup_failure")) : ValueTask.CompletedTask; }
    }
    private sealed class MemoryAudit : IProfileAttemptAudit, IProfileResourceAudit
    {
        internal readonly List<ProfileAttemptRecord> Attempts = []; internal readonly List<ProfileResourceRecord> Resources = [];
        internal int FailedAttempt; internal string? FailedPhase;
        public void AppendAndFlush(ProfileAttemptRecord record)
        { if (record.Sequence == FailedAttempt) throw new IOException("fictional_audit_failure"); Attempts.Add(record); }
        public void AppendAndFlush(ProfileResourceRecord record)
        { if (record.Phase == FailedPhase) { FailedPhase = null; throw new IOException("fictional_audit_failure"); } Resources.Add(record); }
    }
}
