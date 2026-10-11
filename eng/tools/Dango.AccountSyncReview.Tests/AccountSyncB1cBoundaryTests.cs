using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1c")]
public sealed class AccountSyncB1cBoundaryTests
{
    [TestMethod]
    public void Consumed_B1a_B1a2_B1b1_closure_and_profile_grants_cannot_authorize_the_new_owner_or_initializer()
    {
        var oldGrants = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DANGO_BACKEND_ACCOUNTSYNC_B1A_APPROVED"] = "1",
            ["DANGO_BACKEND_ACCOUNTSYNC_B1A2_APPROVED"] = "1",
            ["DANGO_BACKEND_ACCOUNTSYNC_B1A2_CLOSURE_APPROVED"] = "1",
            ["DANGO_BACKEND_ACCOUNTSYNC_B1B1_APPROVED"] = "1",
            ["DANGO_BACKEND_PROFILE_TEST_APPROVED"] = "1"
        };
        var reads = new List<string>(); var initializerAccesses = 0;
        var approved = AccountSyncB1cTestOwner.IsApprovalGranted(name =>
        {
            reads.Add(name); return oldGrants.GetValueOrDefault(name);
        });
        Assert.IsFalse(approved);
        CollectionAssert.AreEqual(new[] { "DANGO_BACKEND_ACCOUNTSYNC_B1C1_APPROVED" }, reads);
        Assert.ThrowsExactly<AssertInconclusiveException>(() => AccountSyncB1cTestOwner.RequireWithFactory(approved, () =>
        {
            initializerAccesses++;
            throw new AssertFailedException("A consumed earlier stage reached the new resource initializer.");
        }));
        Assert.AreEqual(0, initializerAccesses);
    }

    [TestMethod]
    public void Only_the_new_B1c1_exact_grant_is_accepted_by_the_production_approval_reader()
    {
        var reads = new List<string>();
        Assert.IsTrue(AccountSyncB1cTestOwner.IsApprovalGranted(name =>
        {
            reads.Add(name); return name == "DANGO_BACKEND_ACCOUNTSYNC_B1C1_APPROVED" ? "1" : null;
        }));
        CollectionAssert.AreEqual(new[] { "DANGO_BACKEND_ACCOUNTSYNC_B1C1_APPROVED" }, reads);
    }

    [TestMethod]
    public void Missing_zero_and_noncanonical_new_grants_are_rejected_before_initialization()
    {
        foreach (var value in new string?[] { null, "0", "true", "1 " })
        {
            var initializerAccesses = 0;
            var approved = AccountSyncB1cTestOwner.IsApprovalGranted(_ => value);
            Assert.IsFalse(approved);
            Assert.ThrowsExactly<AssertInconclusiveException>(() => AccountSyncB1cTestOwner.RequireWithFactory(approved, () =>
            {
                initializerAccesses++;
                throw new AssertFailedException("An absent or invalid grant reached its resource initializer.");
            }));
            Assert.AreEqual(0, initializerAccesses);
        }
    }

    [TestMethod]
    public void Missing_independent_approval_is_inconclusive_before_any_initializer_resource_access()
    {
        var accessed = 0;
        Assert.ThrowsExactly<AssertInconclusiveException>(() => AccountSyncB1cTestOwner.RequireWithFactory(false, () =>
        {
            accessed++;
            throw new AssertFailedException("The unapproved entry reached its resource initializer.");
        }));
        Assert.AreEqual(0, accessed);
    }

    [TestMethod]
    [DataRow(AccountSyncOperation.Invite, 2)]
    [DataRow(AccountSyncOperation.Start, 2)]
    [DataRow(AccountSyncOperation.Confirm, 2)]
    [DataRow(AccountSyncOperation.Complete, 2)]
    [DataRow(AccountSyncOperation.Login, 4)]
    [DataRow(AccountSyncOperation.Profile, 2)]
    [DataRow(AccountSyncOperation.Configuration, 3)]
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
    public void Complete_approved_matrix_is_exactly_twenty_one_durable_attempts()
    {
        var audit = new Audit(); var budget = Budget(audit);
        foreach (var item in AccountSyncB1cAttemptBudget.Limits)
            for (var index = 0; index < item.Value; index++) budget.Reserve(item.Key);
        Assert.AreEqual(21, budget.TotalAttempts);
        Assert.AreEqual(21, audit.Records.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 21).Select(value => (long)value).ToArray(), audit.Records.Select(row => row.Sequence).ToArray());
    }

    [TestMethod]
    public void Identical_business_content_and_failed_response_each_still_consume_an_attempt()
    {
        var audit = new Audit(); var budget = Budget(audit);
        for (var index = 0; index < 3; index++)
        {
            budget.Reserve(AccountSyncOperation.Configuration);
            try { throw new OperationCanceledException("simulated unknown response"); }
            catch (OperationCanceledException) { }
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(AccountSyncOperation.Configuration));
        Assert.AreEqual(3, budget.Snapshot().Operations[AccountSyncOperation.Configuration]);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, audit.Records.Select(row => row.Used).ToArray());
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
            budget.Reserve(AccountSyncOperation.Configuration);
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
        for (var index = 0; index < 128; index++) budget.CountHttpRequest();
        Assert.AreEqual(128, budget.HttpRequests);
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
        var budget = new AccountSyncB1cAttemptBudget(Guid.NewGuid(), audit, () => { if (!alive) throw new InvalidOperationException(); });
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

    [TestMethod]
    public void Exact_actors_bind_four_canonical_sessions_and_complete_seven_field_configurations()
    {
        var actors = Actors(); AccountSyncB1cEvidenceContract.VerifyActors(actors, Epoch);
        Assert.AreEqual(7, actors[0].Configuration.EnumerateObject().Count());
        Assert.AreEqual(7, actors[1].Configuration.EnumerateObject().Count());
        Assert.AreEqual(4, actors.SelectMany(a => a.SessionIds).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    [DataRow("order")]
    [DataRow("empty-epoch")]
    [DataRow("actor-epoch")]
    [DataRow("empty-user")]
    [DataRow("empty-pet")]
    [DataRow("empty-room")]
    [DataRow("shared-user")]
    [DataRow("shared-pet")]
    [DataRow("shared-room")]
    [DataRow("shared-session")]
    [DataRow("repeated-session")]
    [DataRow("unsorted-sessions")]
    [DataRow("noncanonical-session")]
    [DataRow("empty-session")]
    [DataRow("session-count")]
    [DataRow("nickname")]
    [DataRow("profile-version")]
    [DataRow("configuration-version")]
    [DataRow("configuration-body")]
    [DataRow("configuration-size")]
    [DataRow("sessions")]
    [DataRow("revocations")]
    [DataRow("mail-attempts")]
    [DataRow("mail-state")]
    public void Actor_evidence_rejects_identity_session_and_complete_configuration_substitutions(string fault)
    {
        var actors = Actors(); var epoch = Epoch; var a = actors[0]; var b = actors[1];
        switch (fault)
        {
            case "order": actors = [b, a]; break;
            case "empty-epoch": epoch = Guid.Empty; break;
            case "actor-epoch": actors[0] = a with { RecoveryEpoch = Guid.NewGuid() }; break;
            case "empty-user": actors[0] = a with { UserId = Guid.Empty }; break;
            case "empty-pet": actors[0] = a with { PetId = Guid.Empty }; break;
            case "empty-room": actors[0] = a with { DefaultRoomId = Guid.Empty }; break;
            case "shared-user": actors[1] = b with { UserId = a.UserId }; break;
            case "shared-pet": actors[1] = b with { PetId = a.PetId }; break;
            case "shared-room": actors[1] = b with { DefaultRoomId = a.DefaultRoomId }; break;
            case "shared-session": actors[1] = b with { SessionIds = [a.SessionIds[0], b.SessionIds[1]] }; break;
            case "repeated-session": actors[0] = a with { SessionIds = [a.SessionIds[0], a.SessionIds[0]] }; break;
            case "unsorted-sessions": actors[0] = a with { SessionIds = a.SessionIds.Reverse().ToArray() }; break;
            case "noncanonical-session": actors[0] = a with { SessionIds = [a.SessionIds[0].Replace("-", ""), a.SessionIds[1]] }; break;
            case "empty-session": actors[0] = a with { SessionIds = [Guid.Empty.ToString("D"), a.SessionIds[1]] }; break;
            case "session-count": actors[0] = a with { SessionIds = [a.SessionIds[0]] }; break;
            case "nickname": actors[0] = a with { Nickname = b.Nickname }; break;
            case "profile-version": actors[0] = a with { ProfileVersion = 1 }; break;
            case "configuration-version": actors[1] = b with { ConfigurationVersion = 2 }; break;
            case "configuration-body": actors[0] = a with { Configuration = AccountSyncB1cEvidenceContract.Sample("b", 111) }; break;
            case "configuration-size": actors[0] = a with { Configuration = AccountSyncB1cEvidenceContract.Sample("a", 103) }; break;
            case "sessions": actors[0] = a with { Sessions = 1 }; break;
            case "revocations": actors[0] = a with { Revoked = 1 }; break;
            case "mail-attempts": actors[0] = a with { Attempts = 1 }; break;
            case "mail-state": actors[0] = a with { MailState = "Sent" }; break;
            default: Assert.Fail("Unknown actor fault."); break;
        }
        EvidenceRejected(() => AccountSyncB1cEvidenceContract.VerifyActors(actors, epoch));
    }

    [TestMethod]
    public void Exactly_five_originals_four_receipts_and_one_unacknowledged_A_original_are_accepted()
    {
        var actors = Actors(); var archives = Archives(actors);
        AccountSyncB1cEvidenceContract.VerifyArchives(archives, actors, Origin);
        Assert.AreEqual(4, archives.Count(row => row.ReceiptPresent));
        Assert.IsFalse(archives[2].ReceiptPresent); Assert.IsNull(archives[2].ObservedCloudVersion);
    }

    [TestMethod]
    [DataRow("order")]
    [DataRow("count")]
    [DataRow("journal-reuse")]
    [DataRow("empty-journal")]
    [DataRow("actor")]
    [DataRow("case")]
    [DataRow("user")]
    [DataRow("pet")]
    [DataRow("origin")]
    [DataRow("unsafe-origin")]
    [DataRow("baseline")]
    [DataRow("choice")]
    [DataRow("receipt")]
    [DataRow("late-ack-receipt")]
    [DataRow("outcome")]
    [DataRow("observed-version")]
    [DataRow("failure-code")]
    [DataRow("raw-hash")]
    [DataRow("raw-hash-case")]
    [DataRow("local-content")]
    [DataRow("cloud-content")]
    public void Archive_evidence_rejects_wrong_actor_origin_content_version_or_late_ACK_receipt(string fault)
    {
        var actors = Actors(); var archives = Archives(actors); var origin = Origin; var row = archives[0];
        switch (fault)
        {
            case "order": (archives[0], archives[1]) = (archives[1], archives[0]); break;
            case "count": archives = archives[..4]; break;
            case "journal-reuse": archives[1] = archives[1] with { JournalId = row.JournalId }; break;
            case "empty-journal": archives[0] = row with { JournalId = Guid.Empty }; break;
            case "actor": archives[0] = row with { Actor = "a" }; break;
            case "case": archives[0] = row with { Case = "late-patch" }; break;
            case "user": archives[0] = row with { UserId = actors[0].UserId }; break;
            case "pet": archives[0] = row with { PetId = actors[0].PetId }; break;
            case "origin": archives[0] = row with { Origin = "https://127.0.0.1:65532/" }; break;
            case "unsafe-origin": origin = new("https://external.invalid/"); break;
            case "baseline": archives[0] = row with { ConfigurationVersion = 2 }; break;
            case "choice": archives[0] = row with { Choice = (int)PetConfigurationChoice.Local }; break;
            case "receipt": archives[0] = row with { ReceiptPresent = false }; break;
            case "late-ack-receipt": archives[2] = archives[2] with { ReceiptPresent = true, Outcome = (int)PetChoiceOutcome.CloudUpdated, ObservedCloudVersion = 2 }; break;
            case "outcome": archives[0] = row with { Outcome = (int)PetChoiceOutcome.CloudUpdated }; break;
            case "observed-version": archives[0] = row with { ObservedCloudVersion = 2 }; break;
            case "failure-code": archives[0] = row with { FailureCode = "failure" }; break;
            case "raw-hash": archives[0] = row with { LocalOriginalSha256 = "00" }; break;
            case "raw-hash-case": archives[0] = row with { LocalOriginalSha256 = new string('a', 64) }; break;
            case "local-content": archives[0] = row with { LocalConfigurationSha256 = Hash("b", 118) }; break;
            case "cloud-content": archives[0] = row with { CloudConfigurationSha256 = Hash("a", 103) }; break;
            default: Assert.Fail("Unknown archive fault."); break;
        }
        EvidenceRejected(() => AccountSyncB1cEvidenceContract.VerifyArchives(archives, actors, origin));
    }

    [TestMethod]
    public void Logout_intents_are_durable_before_return_and_do_not_spend_formal_Logout()
    {
        var records = new List<AccountSyncB1cLogoutIntent>(); var audit = new Audit(); var formal = Budget(audit); var aborts = 0;
        var budget = new AccountSyncB1cLogoutIntentBudget(Guid.NewGuid(), formal.CheckAlive, () => { aborts++; formal.Abort(); }, records.Add);
        var user = Guid.NewGuid(); var pet = Guid.NewGuid(); var session = Guid.NewGuid();
        budget.Record("late-get", 1, user, pet, session); budget.Record("late-patch", 2, user, pet, Guid.NewGuid());
        Assert.AreEqual(2, budget.Used); Assert.AreEqual(2, records.Count); Assert.AreEqual(0, formal.TotalAttempts);
        CollectionAssert.AreEqual(new[] { 1, 2 }, records.Select(row => row.Sequence).ToArray());
        Assert.IsTrue(records.All(row => row.Version == 1 && row.Intent == "uia-logout" && !row.Forwarded));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Record("late-patch", 3, user, pet, session));
        Assert.AreEqual(1, aborts); Assert.AreEqual(2, records.Count);
        Assert.ThrowsExactly<InvalidOperationException>(formal.CheckAlive);
    }

    [TestMethod]
    public void Logout_intent_audit_failure_spends_once_and_permanently_closes_without_formal_write()
    {
        var appendAttempts = 0; var aborts = 0; var budget = new AccountSyncB1cLogoutIntentBudget(Guid.NewGuid(), () => { },
            () => aborts++, _ => { appendAttempts++; throw new InvalidOperationException("durable failure"); });
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Record("late-get", 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.AreEqual(1, budget.Used); Assert.AreEqual(1, appendAttempts); Assert.AreEqual(1, aborts);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Record("late-get", 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.AreEqual(1, budget.Used); Assert.AreEqual(1, appendAttempts);
    }

    [TestMethod]
    [DataRow("order")]
    [DataRow("client")]
    [DataRow("user")]
    [DataRow("pet")]
    [DataRow("session")]
    [DataRow("owner-lost")]
    public void Logout_intent_rejection_never_appends_or_reopens_after_owner_returns(string fault)
    {
        var appends = 0; var alive = fault != "owner-lost"; var aborts = 0;
        var budget = new AccountSyncB1cLogoutIntentBudget(Guid.NewGuid(), () => { if (!alive) throw new InvalidOperationException(); },
            () => aborts++, _ => appends++);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Record(fault == "order" ? "late-patch" : "late-get",
            fault == "client" ? 0 : 1, fault == "user" ? Guid.Empty : Guid.NewGuid(),
            fault == "pet" ? Guid.Empty : Guid.NewGuid(), fault == "session" ? Guid.Empty : Guid.NewGuid()));
        alive = true; Assert.ThrowsExactly<InvalidOperationException>(() => budget.Record("late-get", 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.AreEqual(0, budget.Used); Assert.AreEqual(0, appends); Assert.AreEqual(1, aborts);
    }

    [TestMethod]
    public async Task Concurrent_logout_intents_cannot_duplicate_the_final_durable_sequence()
    {
        var records = new ConcurrentQueue<AccountSyncB1cLogoutIntent>(); var budget = new AccountSyncB1cLogoutIntentBudget(Guid.NewGuid(), () => { }, () => { }, records.Enqueue);
        var user = Guid.NewGuid(); var pet = Guid.NewGuid(); budget.Record("late-get", 1, user, pet, Guid.NewGuid());
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            try { budget.Record("late-patch", 2, user, pet, Guid.NewGuid()); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.AreEqual(1, results.Count(value => value)); Assert.AreEqual(2, budget.Used); Assert.AreEqual(2, records.Count);
    }

    [TestMethod]
    public void Precreated_empty_logout_ledger_accepts_two_flushed_appends_without_replacing_the_first_record()
    {
        using var temporary = new TemporaryLogoutLedger(); temporary.ReserveEmpty();
        var records = LogoutLedgerRecords(); var formal = Budget(new Audit()); var aborts = 0;
        var budget = new AccountSyncB1cLogoutIntentBudget(records[0].OwnerId, formal.CheckAlive,
            () => { aborts++; formal.Abort(); }, record => AccountSyncB1cLogoutIntentLedger.AppendExisting(temporary.LedgerPath, record));
        budget.Record("late-get", 1, records[0].UserId, records[0].PetId, records[0].SessionId);
        var firstBytes = File.ReadAllBytes(temporary.LedgerPath);
        Assert.IsTrue(firstBytes.Length > 0 && firstBytes[^1] == 10);
        var first = JsonSerializer.Deserialize<AccountSyncB1cLogoutIntent>(firstBytes.AsSpan(0, firstBytes.Length - 1));
        Assert.IsNotNull(first); Assert.AreEqual(1, first.Sequence); Assert.AreEqual(records[0].SessionId, first.SessionId);
        budget.Record("late-patch", 2, records[1].UserId, records[1].PetId, records[1].SessionId);
        var bothBytes = File.ReadAllBytes(temporary.LedgerPath);
        CollectionAssert.AreEqual(firstBytes, bothBytes[..firstBytes.Length]);
        var lines = File.ReadAllLines(temporary.LedgerPath, Encoding.UTF8);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual(first, JsonSerializer.Deserialize<AccountSyncB1cLogoutIntent>(lines[0]));
        var second = JsonSerializer.Deserialize<AccountSyncB1cLogoutIntent>(lines[1]);
        Assert.IsNotNull(second); Assert.AreEqual(2, second.Sequence); Assert.AreEqual(records[1].SessionId, second.SessionId);
        Assert.AreEqual(2, budget.Used); Assert.AreEqual(0, aborts); Assert.AreEqual(0, formal.TotalAttempts);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("nonempty-first")]
    [DataRow("empty-second")]
    [DataRow("oversized-second")]
    [DataRow("malformed-second")]
    public void Logout_ledger_rejects_invalid_reserved_state_without_creating_or_overwriting_it(string fault)
    {
        using var temporary = new TemporaryLogoutLedger(); var records = LogoutLedgerRecords();
        byte[]? before = null;
        if (fault != "missing")
        {
            temporary.ReserveEmpty();
            if (fault == "nonempty-first") File.WriteAllText(temporary.LedgerPath, "preserved_old_ledger\n", Encoding.UTF8);
            if (fault == "oversized-second") File.WriteAllBytes(temporary.LedgerPath, new byte[4098]);
            if (fault == "malformed-second") File.WriteAllText(temporary.LedgerPath, "malformed_first_record\n", Encoding.UTF8);
            before = File.ReadAllBytes(temporary.LedgerPath);
        }
        var record = records[fault.EndsWith("second", StringComparison.Ordinal) ? 1 : 0];
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => AccountSyncB1cLogoutIntentLedger.AppendExisting(temporary.LedgerPath, record));
        Assert.AreEqual("accountsync_b1c_logout_intent_ledger_rejected", error.Message);
        if (before is null) Assert.IsFalse(File.Exists(temporary.LedgerPath), "A missing reserved ledger must never be recreated.");
        else CollectionAssert.AreEqual(before, File.ReadAllBytes(temporary.LedgerPath), "Rejection cannot replace or truncate any reserved bytes.");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Logout_ledger_rejects_repeated_first_or_second_append_and_preserves_both_records(int repeatedSequence)
    {
        using var temporary = new TemporaryLogoutLedger(); temporary.ReserveEmpty(); var records = LogoutLedgerRecords();
        AccountSyncB1cLogoutIntentLedger.AppendExisting(temporary.LedgerPath, records[0]);
        AccountSyncB1cLogoutIntentLedger.AppendExisting(temporary.LedgerPath, records[1]);
        var before = File.ReadAllBytes(temporary.LedgerPath);
        Assert.ThrowsExactly<InvalidOperationException>(() => AccountSyncB1cLogoutIntentLedger.AppendExisting(temporary.LedgerPath, records[repeatedSequence - 1]));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(temporary.LedgerPath));
        Assert.AreEqual(2, File.ReadAllLines(temporary.LedgerPath).Length);
    }

    private static AccountSyncB1cLogoutIntent[] LogoutLedgerRecords()
    {
        var owner = Guid.NewGuid(); var user = Guid.NewGuid(); var pet = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        return [new(1, owner, 1, "late-get", 1, user, pet, Guid.NewGuid(), "uia-logout", false, at),
            new(1, owner, 2, "late-patch", 2, user, pet, Guid.NewGuid(), "uia-logout", false, at.AddSeconds(1))];
    }

    private sealed class TemporaryLogoutLedger : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "dango-b1c-logout-ledger-" + Guid.NewGuid().ToString("N"));
        internal string LedgerPath => Path.Combine(directory, "logout-intents.jsonl");
        internal TemporaryLogoutLedger()
        {
            Assert.IsFalse(Directory.Exists(directory)); Directory.CreateDirectory(directory);
            Assert.AreEqual((FileAttributes)0, File.GetAttributes(directory) & FileAttributes.ReparsePoint);
        }
        internal void ReserveEmpty()
        {
            using var file = new FileStream(LedgerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Flush(true);
        }
        public void Dispose()
        {
            if (File.Exists(LedgerPath)) File.Delete(LedgerPath);
            Directory.Delete(directory); // Never recursively delete a test directory.
        }
    }

    private static readonly Guid Epoch = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Uri Origin = new("https://127.0.0.1:65531/");
    private static AccountSyncB1cActorEvidence[] Actors() =>
    [
        new("a", Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("11111111-1111-1111-1111-111111111112"),
            Guid.Parse("11111111-1111-1111-1111-111111111113"), AccountSyncB1cEvidenceContract.Nickname("a"), 2, 2,
            AccountSyncB1cEvidenceContract.Sample("a", 111), ["11111111-1111-1111-1111-111111111114", "11111111-1111-1111-1111-111111111115"], 2, 2, 0, "Pending", Epoch),
        new("b", Guid.Parse("22222222-2222-2222-2222-222222222221"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("22222222-2222-2222-2222-222222222223"), AccountSyncB1cEvidenceContract.Nickname("b"), 2, 3,
            AccountSyncB1cEvidenceContract.Sample("b", 130), ["22222222-2222-2222-2222-222222222224", "22222222-2222-2222-2222-222222222225"], 2, 2, 0, "Pending", Epoch)
    ];
    private static AccountSyncB1cArchiveEvidence[] Archives(AccountSyncB1cActorEvidence[] actors)
    {
        string[] steps = ["late-get-choice", "late-get-save", "late-patch-unacknowledged", "late-patch-choice", "late-patch-save"];
        return steps.Select((step, index) =>
        {
            var unacknowledged = index == 2; var choice = index is 0 or 3; var actor = actors[unacknowledged ? 0 : 1];
            return new AccountSyncB1cArchiveEvidence(actor.Actor, index < 2 ? "late-get" : "late-patch", step, Guid.NewGuid(),
                Origin.AbsoluteUri, actor.UserId, actor.PetId, new string('A', 64),
                Hash(index is 0 or 2 or 3 ? "a" : "b", index switch { 0 or 3 => 118, 1 => 125, 2 => 111, _ => 130 }),
                Hash(unacknowledged ? "a" : "b", index is 3 or 4 ? 125 : 103), index is 3 or 4 ? 2 : 1,
                (int)(choice ? PetConfigurationChoice.Cloud : PetConfigurationChoice.Local), !unacknowledged,
                unacknowledged ? null : (int?)(choice ? PetChoiceOutcome.LocalApplied : PetChoiceOutcome.CloudUpdated),
                unacknowledged ? null : choice ? index == 0 ? 1L : 2L : index == 1 ? 2L : 3L, null);
        }).ToArray();
    }
    private static string Hash(string actor, double size) => AccountSyncB1cEvidenceContract.ConfigurationSha256(AccountSyncB1cEvidenceContract.Sample(actor, size));
    private static void EvidenceRejected(Action action) => Assert.AreEqual("accountsync_b1c_evidence_rejected", Assert.ThrowsExactly<InvalidOperationException>(action).Message);

    private static AccountSyncB1cAttemptBudget Budget(Audit audit, TimeProvider? clock = null) =>
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
