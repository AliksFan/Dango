using System.Net;
using System.Net.Http;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1c2")]
public sealed class AccountSyncB1c2LifecycleTests
{
    [TestMethod]
    public void Old_grants_never_reach_the_new_initializer()
    {
        var reads = new List<string>(); var accesses = 0;
        var approved = AccountSyncB1c2TestOwner.IsApprovalGranted(name => { reads.Add(name); return name == "DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED" ? null : "1"; });
        Assert.IsFalse(approved); CollectionAssert.AreEqual(new[] { "DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED" }, reads);
        Assert.ThrowsExactly<AssertInconclusiveException>(() => AccountSyncB1c2TestOwner.RequireWithFactory(approved, () => { accesses++; throw new AssertFailedException(); }));
        Assert.AreEqual(0, accesses);
        Assert.IsTrue(AccountSyncB1c2TestOwner.IsApprovalGranted(name => name == "DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED" ? "1" : null));
    }

    [TestMethod]
    public void Existing_loopback_composition_keeps_observation_off_by_default()
    {
        var origin = new Uri("https://127.0.0.1:61234/");
        using var secrets = new AccountSyncB1c2MemorySecrets(origin);
        var dependencies = new DangoAccountDependencies(origin, secrets, (_, _) => throw new AssertFailedException("Construction must not create a client."));
        Assert.IsNull(dependencies.ObserveSyncRemote); Assert.IsNull(dependencies.SyncCreated);
    }

    [TestMethod]
    public async Task Original_task_observer_waits_for_and_records_actual_success_fault_and_cancel()
    {
        foreach (var status in new[] { "RanToCompletion", "Faulted", "Canceled" })
        {
            var trace = new AccountSyncB1c2Lifecycle(); var original = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            trace.ObserveTask(original.Task, task => trace.Record("RequestTerminal", 1, value: AccountSyncB1c2Lifecycle.Status(task)));
            Assert.IsFalse(trace.ObserversSettled); Assert.AreEqual(0, trace.Events.Length);
            if (status == "RanToCompletion") original.SetResult(); else if (status == "Faulted") original.SetException(new InvalidOperationException()); else original.SetCanceled();
            await trace.DrainAsync(); Assert.IsTrue(trace.ObserversSettled);
            Assert.AreEqual(status, trace.Events.Single().Value);
        }
    }

    [TestMethod]
    public async Task Synchronously_completed_original_runs_terminal_without_holding_the_trace_gate()
    {
        var trace = new AccountSyncB1c2Lifecycle(); var original = Task.CompletedTask;
        trace.ObserveTask(original, actual =>
        {
            var otherThread = Task.Run(() => trace.Record("RequestTerminal", 1, value: AccountSyncB1c2Lifecycle.Status(actual)));
            Assert.IsTrue(otherThread.Wait(TimeSpan.FromSeconds(2)), "A terminal callback must not block a cancellation thread that needs the trace gate.");
        });
        await trace.DrainAsync(); Assert.AreEqual("RanToCompletion", trace.Events.Single().Value);
    }

    [TestMethod]
    public async Task Failed_terminal_observer_cannot_report_successful_observer_drain()
    {
        var trace = new AccountSyncB1c2Lifecycle(); var original = Task.CompletedTask;
        trace.ObserveTask(original, _ => throw new InvalidOperationException("fictional observer failure"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(trace.DrainAsync);
        Assert.AreEqual(TaskStatus.RanToCompletion, original.Status); Assert.IsFalse(trace.ObserversSettled); Assert.AreEqual(0, trace.Events.Length);
    }

    [TestMethod]
    public async Task Lifo_inner_cancel_finishing_the_original_task_does_not_lose_original_token_observation()
    {
        var trace = new AccountSyncB1c2Lifecycle(); using var remote = new SynchronousCancellationRemote(); var observed = trace.ObserveRemote(remote);
        using var sync = new PetConfigurationLiveSyncCoordinator(observed, null!, null!, Snapshot()); trace.Bind(observed, sync);
        var pump = sync.PumpAsync(); sync.Stop(); await pump; await trace.DrainAsync();
        Assert.AreEqual(1, trace.Events.Count(e => e.Kind == "OperationCallerCancelled"));
        Assert.AreEqual(1, trace.Events.Count(e => e.Kind == "CoordinatorStopped"));
        Assert.AreEqual("Canceled", trace.Events.Single(e => e.Kind == "OperationTerminal").Value);
    }

    [TestMethod]
    [DataRow("missing")][DataRow("Canceled")][DataRow("Faulted")][DataRow("unsettled")]
    public async Task Window_does_not_claim_verified_without_unique_successful_settled_logout(string fault)
    {
        var trace = new AccountSyncB1c2Lifecycle(); var observed = trace.ObserveRemote(new WaitingRemote());
        using var sync = new PetConfigurationLiveSyncCoordinator(observed, null!, null!, Snapshot()); trace.Bind(observed, sync);
        var window = trace.OpenWindow("late-get", 1, Snapshot().UserId, Snapshot().PetId, Guid.NewGuid(), 1);
        trace.Record("UiaLogoutInvoked", window: window); sync.Stop(); trace.Record("UiaLogoutReturned", window: window);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (fault == "unsettled") trace.ObserveTask(pending.Task, task => trace.Record("RequestTerminal", 2, value: AccountSyncB1c2Lifecycle.Status(task)));
        else if (fault != "missing") trace.Record("RequestTerminal", 2, value: fault);
        Assert.ThrowsExactly<InvalidOperationException>(() => trace.CloseWindow(window, 2, true));
        Assert.IsFalse(trace.Windows.Single().Verified); Assert.IsFalse(trace.Events.Any(e => e.Kind == "WindowClosed"));
        pending.SetResult(); await trace.DrainAsync();
    }

    [TestMethod]
    public async Task Real_coordinator_stop_cancels_the_original_remote_token_before_HttpClient_timeout()
    {
        var trace = new AccountSyncB1c2Lifecycle(); var remote = new WaitingRemote(); var observed = trace.ObserveRemote(remote);
        using var sync = new PetConfigurationLiveSyncCoordinator(observed, null!, null!, Snapshot()); trace.Bind(observed, sync);
        var pump = sync.PumpAsync(); Assert.IsFalse(pump.IsCompleted); Assert.IsTrue(remote.Seen.CanBeCanceled);
        sync.Stop(); await pump; await trace.DrainAsync();
        Assert.AreEqual("Canceled", trace.Events.Single(e => e.Kind == "OperationTerminal").Value);
        Assert.AreEqual("StoppedWithoutFailure", trace.Events.Single(e => e.Kind == "CoordinatorStopped").Value);
        Assert.AreEqual(1, trace.Events.Count(e => e.Kind == "OperationCallerCancelled"));
    }

    [TestMethod]
    public async Task Real_HttpClient_timeout_does_not_fabricate_original_caller_or_coordinator_stop()
    {
        var trace = new AccountSyncB1c2Lifecycle(); using var remote = new TimeoutRemote(); var observed = trace.ObserveRemote(remote);
        using var sync = new PetConfigurationLiveSyncCoordinator(observed, null!, null!, Snapshot()); trace.Bind(observed, sync);
        var actual = observed.ReadAsync(CancellationToken.None);
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await actual); await trace.DrainAsync();
        Assert.AreEqual("Canceled", trace.Events.Single(e => e.Kind == "OperationTerminal").Value);
        Assert.AreEqual(0, trace.Events.Count(e => e.Kind is "OperationCallerCancelled" or "CoordinatorStopped"));
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, sync.State.Status);
    }

    [TestMethod]
    public async Task Fixture_lifetime_cancel_alone_does_not_fabricate_original_caller_stop()
    {
        var trace = new AccountSyncB1c2Lifecycle(); using var lifetime = new CancellationTokenSource();
        var observed = trace.ObserveRemote(new FixtureCanceledRemote(lifetime.Token));
        using var sync = new PetConfigurationLiveSyncCoordinator(observed, null!, null!, Snapshot()); trace.Bind(observed, sync);
        var actual = observed.ReadAsync(CancellationToken.None); lifetime.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await actual); await trace.DrainAsync();
        Assert.AreEqual("Canceled", trace.Events.Single(e => e.Kind == "OperationTerminal").Value);
        Assert.AreEqual(0, trace.Events.Count(e => e.Kind == "OperationCallerCancelled"));
    }

    [TestMethod]
    public void Independent_complete_fixture_accepts_only_bounded_proven_logout_cancel()
    {
        var sample = Sample(); Verify(sample);
        Assert.IsNull(sample.Requests[2].ResponseStatus); Assert.IsNull(sample.Requests[2].ResponseVersion);
    }

    [TestMethod]
    public void Independent_sent_body_cancel_preserves_real_headers_and_accepts_no_version()
    {
        var s = Sample(); s.Requests[2] = s.Requests[2] with { ResponseStatus = 200 };
        var position = s.Events.FindIndex(e => e.Kind == "WindowOpened" && e.WindowId == 1);
        s.Events.InsertRange(position, new[]
        {
            new AccountSyncB1c2LifecycleEvent(0, 0, "SendBefore", 3, 1, 1, null, null),
            new AccountSyncB1c2LifecycleEvent(0, 0, "SendAfter", 3, 1, 1, null, null),
            new AccountSyncB1c2LifecycleEvent(0, 0, "HeadersReceived", 3, 1, 1, null, null)
        });
        for (var i = 0; i < s.Events.Count; i++) s.Events[i] = s.Events[i] with { Sequence = i + 1, Tick = (i + 1) * 700 };
        Verify(s); Assert.AreEqual(200, s.Requests[2].ResponseStatus); Assert.IsNull(s.Requests[2].ResponseVersion);
    }

    [TestMethod]
    public void Two_otherwise_valid_cancelled_reads_in_one_window_are_rejected()
    {
        var s = Sample();
        for (var i = 3; i < s.Requests.Count; i++) s.Requests[i] = s.Requests[i] with { Sequence = s.Requests[i].Sequence + 1 };
        for (var i = 0; i < s.Events.Count; i++) if (s.Events[i].RequestSequence is >= 4) s.Events[i] = s.Events[i] with { RequestSequence = s.Events[i].RequestSequence + 1 };
        s.Requests.Insert(3, s.Requests[2] with { Sequence = 4, PetReadOrdinal = 8 });
        s.Windows[0] = s.Windows[0] with { LogoutRequestSequence = 5 };
        s.Windows[1] = s.Windows[1] with { SaveRequestSequence = 7, LogoutRequestSequence = 9 };
        s.Phases[1] = s.Phases[1] with { RequestSequence = 7 };
        var position = s.Events.FindIndex(e => e.Kind == "UiaLogoutReturned" && e.WindowId == 1);
        s.Events.InsertRange(position, new[]
        {
            new AccountSyncB1c2LifecycleEvent(0, 0, "OperationStarted", null, 5, 1, 1, null),
            new AccountSyncB1c2LifecycleEvent(0, 0, "RequestRegistered", 4, 5, 1, 1, null),
            new AccountSyncB1c2LifecycleEvent(0, 0, "CoordinatorStopped", null, 5, 1, 1, "StoppedWithoutFailure"),
            new AccountSyncB1c2LifecycleEvent(0, 0, "OperationCallerCancelled", null, 5, 1, 1, null),
            new AccountSyncB1c2LifecycleEvent(0, 0, "CallerCancelled", 4, 5, 1, 1, null),
            new AccountSyncB1c2LifecycleEvent(0, 0, "RequestCanceledTokenMatched", 4, 5, 1, 1, "true"),
            new AccountSyncB1c2LifecycleEvent(0, 0, "RequestTerminal", 4, 5, 1, 1, "Canceled"),
            new AccountSyncB1c2LifecycleEvent(0, 0, "OperationTerminal", null, 5, 1, 1, "Canceled")
        });
        for (var i = 0; i < s.Events.Count; i++) s.Events[i] = s.Events[i] with { Sequence = i + 1, Tick = (i + 1) * 300 };
        // Keep conservative Send intervals valid while shortening only internal event ticks.
        var offset = 0L;
        for (var i = 0; i < s.Events.Count; i++) { if (s.Events[i].Kind == "SendBefore") offset += 700; s.Events[i] = s.Events[i] with { Tick = s.Events[i].Tick + offset }; }
        Assert.Throws<InvalidOperationException>(() => Verify(s));
    }

    [TestMethod]
    [DataRow("method")][DataRow("identity")][DataRow("cleanup")][DataRow("registration")][DataRow("actor")]
    [DataRow("session")][DataRow("client")][DataRow("early")][DataRow("baseline")][DataRow("held")]
    [DataRow("status500")][DataRow("version")][DataRow("pump")][DataRow("retired")][DataRow("ordinal")]
    [DataRow("caller_missing")][DataRow("original_missing")][DataRow("fixture")][DataRow("deadline")]
    [DataRow("faulted")][DataRow("success_null")][DataRow("wrong_token")][DataRow("timeout_race")]
    [DataRow("outside_window")][DataRow("wrong_coordinator")][DataRow("unverified")][DataRow("logout500")]
    [DataRow("terminal_missing")][DataRow("terminal_duplicate")][DataRow("send_unpaired")][DataRow("pacing")]
    [DataRow("tick_reverse")][DataRow("observers")][DataRow("cross_case")][DataRow("extra_request")]
    [DataRow("success_header_duplicate")][DataRow("success_body_duplicate")][DataRow("success_body_order")]
    [DataRow("presend_headers")][DataRow("presend_status")][DataRow("match_before_caller")][DataRow("stop_before_invoke")]
    [DataRow("save_missing_binding")][DataRow("save_wrong_coordinator")]
    public void Independent_negative_fixtures_fail(string fault)
    {
        var s = Sample(); var r = s.Requests[2]; var caller = s.Events.FindIndex(e => e.Kind == "CallerCancelled" && e.RequestSequence == 3);
        switch (fault)
        {
            case "method": s.Requests[2] = r with { Method = "PATCH" }; break;
            case "identity": s.Requests[2] = r with { Path = "/api/v1/me" }; break;
            case "cleanup": s.Requests[2] = r with { Role = "cleanup" }; break;
            case "registration": s.Requests[2] = r with { Role = "registration" }; break;
            case "actor": s.Requests[2] = r with { Actor = "a" }; break;
            case "session": s.Requests[2] = r with { SessionId = s.Windows[1].SessionId }; break;
            case "client": s.Requests[2] = r with { ClientId = 9 }; break;
            case "early": s.Windows[0] = s.Windows[0] with { SaveRequestSequence = 4 }; break;
            case "baseline": s.Phases.Add(new(3, "late-get:new-baseline-verified", 1, 3)); break;
            case "held": s.Phases.Add(new(3, "actual-response-captured", 1, 3)); break;
            case "status500": s.Requests[2] = r with { ResponseStatus = 500 }; break;
            case "version": s.Requests[2] = r with { ResponseVersion = 2 }; break;
            case "pump": s.Requests[2] = r with { PreviousPumpSettled = false }; break;
            case "retired": s.Requests[2] = r with { Retired = true }; break;
            case "ordinal": s.Requests[2] = r with { PetReadOrdinal = 1 }; break;
            case "caller_missing": s.Events.RemoveAt(caller); break;
            case "original_missing": s.Events.RemoveAll(e => e.Kind == "OperationCallerCancelled" && e.OperationId == 1); break;
            case "fixture": s.Events[caller] = s.Events[caller] with { Kind = "FixtureCancelled" }; break;
            case "deadline": s.Events[caller] = s.Events[caller] with { Kind = "BodyDeadlineCancelled" }; break;
            case "faulted": MutateTerminal(s, "Faulted"); break;
            case "success_null": MutateTerminal(s, "RanToCompletion"); break;
            case "wrong_token": s.Events[s.Events.FindIndex(e => e.Kind == "RequestCanceledTokenMatched" && e.RequestSequence == 3)] = s.Events.Single(e => e.Kind == "RequestCanceledTokenMatched" && e.RequestSequence == 3) with { Value = "false" }; break;
            case "timeout_race": foreach (var i in Enumerable.Range(0, s.Events.Count)) s.Events[i] = s.Events[i] with { Tick = s.Events[i].Tick * 10000 }; break;
            case "outside_window": s.Events[caller] = s.Events[caller] with { WindowId = null, Sequence = 1 }; break;
            case "wrong_coordinator": s.Events[caller] = s.Events[caller] with { CoordinatorId = 2 }; break;
            case "unverified": s.Windows[0] = s.Windows[0] with { Verified = false }; break;
            case "logout500": s.Requests[3] = s.Requests[3] with { ResponseStatus = 500 }; break;
            case "terminal_missing": s.Events.RemoveAll(e => e.Kind == "RequestTerminal" && e.RequestSequence == 3); break;
            case "terminal_duplicate": s.Events.Add(s.Events.Single(e => e.Kind == "RequestTerminal" && e.RequestSequence == 3)); break;
            case "send_unpaired": s.Events.RemoveAll(e => e.Kind == "SendAfter" && e.RequestSequence == 1); break;
            case "pacing": foreach (var i in Enumerable.Range(0, s.Events.Count)) s.Events[i] = s.Events[i] with { Tick = s.Events[i].Tick / 1000 }; break;
            case "tick_reverse": s.Events[2] = s.Events[2] with { Tick = -1 }; break;
            case "observers": s.Settled = false; break;
            case "cross_case": s.Windows[0] = s.Windows[0] with { Case = "late-patch" }; break;
            case "extra_request": s.Requests.Add(r with { Sequence = 9 }); break;
            case "success_header_duplicate": s.Events.Insert(4, s.Events.Single(e => e.Kind == "HeadersReceived" && e.RequestSequence == 1)); break;
            case "success_body_duplicate": s.Events.Insert(5, s.Events.Single(e => e.Kind == "BodyValidated" && e.RequestSequence == 1)); break;
            case "success_body_order": { var index = s.Events.FindIndex(e => e.Kind == "BodyValidated" && e.RequestSequence == 1); var body = s.Events[index]; s.Events.RemoveAt(index); s.Events.Insert(3, body); break; }
            case "presend_headers": s.Requests[2] = r with { ResponseStatus = 200 }; s.Events.Insert(caller, s.Events[caller] with { Kind = "HeadersReceived" }); break;
            case "presend_status": s.Requests[2] = r with { ResponseStatus = 200 }; break;
            case "match_before_caller": { var index = s.Events.FindIndex(e => e.Kind == "RequestCanceledTokenMatched" && e.RequestSequence == 3); var match = s.Events[index]; s.Events.RemoveAt(index); s.Events.Insert(caller, match); break; }
            case "stop_before_invoke": { var index = s.Events.FindIndex(e => e.Kind == "CoordinatorStopped" && e.OperationId == 1); var stop = s.Events[index]; s.Events.RemoveAt(index); s.Events.Insert(s.Events.FindIndex(e => e.Kind == "UiaLogoutInvoked" && e.WindowId == 1), stop); break; }
            case "save_missing_binding":
            {
                // Keep both successful HTTP requests internally consistent while removing
                // only the original save-operation provenance required by this window.
                s.Events.RemoveAll(e => e.OperationId == 3 && e.RequestSequence is null);
                for (var i = 0; i < s.Events.Count; i++)
                    if (s.Events[i].OperationId == 3) s.Events[i] = s.Events[i] with { OperationId = null, CoordinatorId = null };
                break;
            }
            case "save_wrong_coordinator":
            {
                // The entire successful Update (protection GET plus PATCH) is consistently
                // bound to another observed coordinator. Only the window/save link breaks.
                var start = s.Events.FindIndex(e => e.Kind == "OperationStarted" && e.OperationId == 3);
                s.Events.Insert(start, new(0, 0, "CoordinatorBound", null, null, 99, null, null));
                for (var i = 0; i < s.Events.Count; i++)
                    if (s.Events[i].OperationId == 3) s.Events[i] = s.Events[i] with { CoordinatorId = 99 };
                break;
            }
        }
        // Resequence omissions without fixing semantic damage: sequence checks alone are not the oracle.
        for (var i = 0; i < s.Events.Count; i++) s.Events[i] = s.Events[i] with
        {
            Sequence = i + 1,
            Tick = fault is "timeout_race" or "pacing" or "tick_reverse" ? s.Events[i].Tick : (i + 1) * 700
        };
        Assert.Throws<InvalidOperationException>(() => Verify(s));
    }

    private static void MutateTerminal(Fixture s, string value)
    { var index = s.Events.FindIndex(e => e.Kind == "RequestTerminal" && e.RequestSequence == 3); s.Events[index] = s.Events[index] with { Value = value }; }
    private static void Verify(Fixture s) => AccountSyncB1c2LifecycleContract.Verify(s.Requests, s.Phases, s.Events, s.Windows, 1000, s.Settled);
    private sealed class Fixture
    {
        internal List<AccountSyncB1c2Request> Requests = [];
        internal List<AccountSyncB1c2Phase> Phases = [];
        internal List<AccountSyncB1c2LifecycleEvent> Events = [];
        internal List<AccountSyncB1c2LogoutWindow> Windows = [];
        internal bool Settled = true;
    }
    // Handwritten fixed trace independent of recorder/contract construction.
    private static Fixture Sample()
    {
        var s = new Fixture(); var user = Guid.Parse("b1111111-1111-4111-8111-111111111111"); var pet = Guid.Parse("b2222222-2222-4222-8222-222222222222");
        long tick = 0;
        void Event(string kind, int? request = null, int? op = null, int? coordinator = null, int? window = null, string? value = null)
            => s.Events.Add(new(s.Events.Count + 1, tick += 700, kind, request, op, coordinator, window, value));
        for (var i = 0; i < 2; i++)
        {
            var id = i + 1; var save = i * 4 + 2; var updateOperation = i + 3; var session = Guid.Parse(i == 0 ? "b4444444-4444-4444-8444-444444444444" : "b5555555-5555-4555-8555-555555555555"); var name = i == 0 ? "late-get" : "late-patch";
            s.Requests.Add(new(save - 1, id, "desktop", "b", "GET", "/api/v1/auth/request-protection", user, pet, session, null, null, null, true, false, 200, null));
            s.Requests.Add(new(save, id, "desktop", "b", "PATCH", "/api/v1/me/pet/configuration", user, pet, session, null, i + 1, new string('A', 64), true, false, 200, i + 2));
            s.Requests.Add(new(save + 1, id, "desktop", "b", "GET", "/api/v1/me/pet", user, pet, session, 7, null, null, true, false, null, null));
            s.Requests.Add(new(save + 2, id, "desktop", "b", "POST", "/api/v1/auth/logout", user, pet, session, null, null, null, true, false, 200, null));
            s.Windows.Add(new(id, name, id, user, pet, session, id, save, save + 2, true)); s.Phases.Add(new(id, name + ":b-save-converged", id, save));
            Event("CoordinatorBound", coordinator: id);
            Event("OperationStarted", op: updateOperation, coordinator: id);
            Event("RequestRegistered", save - 1, updateOperation, id); Event("SendBefore", save - 1, updateOperation, id); Event("SendAfter", save - 1, updateOperation, id); Event("HeadersReceived", save - 1, updateOperation, id); Event("BodyValidated", save - 1, updateOperation, id); Event("RequestTerminal", save - 1, updateOperation, id, value: "RanToCompletion");
            Event("RequestRegistered", save, updateOperation, id); Event("SendBefore", save, updateOperation, id); Event("SendAfter", save, updateOperation, id); Event("HeadersReceived", save, updateOperation, id); Event("BodyValidated", save, updateOperation, id); Event("RequestTerminal", save, updateOperation, id, value: "RanToCompletion");
            Event("OperationTerminal", op: updateOperation, coordinator: id, value: "RanToCompletion");
            Event("Phase", value: name + ":b-save-converged"); Event("OperationStarted", op: id, coordinator: id);
            Event("RequestRegistered", save + 1, id, id); Event("WindowOpened", window: id); Event("UiaLogoutInvoked", window: id);
            Event("CoordinatorStopped", op: id, coordinator: id, window: id, value: "StoppedWithoutFailure"); Event("OperationCallerCancelled", op: id, coordinator: id, window: id);
            Event("CallerCancelled", save + 1, id, id, id); Event("RequestCanceledTokenMatched", save + 1, id, id, id, "true");
            Event("RequestTerminal", save + 1, id, id, id, "Canceled"); Event("OperationTerminal", op: id, coordinator: id, window: id, value: "Canceled");
            Event("UiaLogoutReturned", window: id);
            Event("RequestRegistered", save + 2); Event("SendBefore", save + 2); Event("SendAfter", save + 2); Event("HeadersReceived", save + 2); Event("BodyValidated", save + 2); Event("RequestTerminal", save + 2, value: "RanToCompletion"); Event("WindowClosed", window: id);
        }
        return s;
    }
    private static PetConfigurationSnapshot Snapshot() => new(Guid.Parse("b1111111-1111-4111-8111-111111111111"), Guid.Parse("b2222222-2222-4222-8222-222222222222"), 1, AccountSyncB1c2EvidenceContract.Sample("b"));
    private class WaitingRemote : IRemotePetConfigurationClient
    {
        internal CancellationToken Seen;
        public Uri Origin => new("https://127.0.0.1:61234/");
        public Guid UserId => Snapshot().UserId;
        public Guid PetId => Snapshot().PetId;
        public virtual async Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) { Seen = ct; await Task.Delay(Timeout.Infinite, ct); return Snapshot(); }
        public Task<PetConfigurationSnapshot> UpdateAsync(long version, JsonElement configuration, CancellationToken ct) => throw new AssertFailedException();
    }
    private sealed class TimeoutRemote : WaitingRemote, IDisposable
    {
        private readonly HttpClient client = new(new WaitingHandler()) { Timeout = TimeSpan.FromMilliseconds(30) };
        public override async Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) { using var response = await client.GetAsync(Origin, ct); return Snapshot(); }
        public void Dispose() => client.Dispose();
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); }
    }
    private sealed class FixtureCanceledRemote(CancellationToken lifetime) : WaitingRemote
    {
        public override async Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct)
        { using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime); await Task.Delay(Timeout.Infinite, linked.Token); return Snapshot(); }
    }
    private sealed class SynchronousCancellationRemote : WaitingRemote, IDisposable
    {
        private CancellationTokenRegistration registration;
        public override Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct)
        {
            // The inner registration is deliberately later than the observer's original
            // caller registration, and completes the original Task inline during Cancel.
            var original = new TaskCompletionSource<PetConfigurationSnapshot>();
            registration = ct.Register(() => original.TrySetCanceled(ct)); return original.Task;
        }
        public void Dispose() => registration.Dispose();
    }
}
