using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AccountSyncB1c2JourneyTests
{
    [STATestMethod]
    [TestCategory("AccountSyncB1c2")]
    public void FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccountWithLifecycleEvidence()
    {
        // Independent grant is the first operation; no file, credential, TLS, host or SQL precedes it.
        var owner = AccountSyncB1c2TestOwner.Require();
        var elapsed = Stopwatch.StartNew(); var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        AccountSyncB1c2HttpsFixture? fixture = null; Task<AccountSyncB1c2HttpsFixture>? initialization = null;
        AccountSyncB1c2DesktopRig? active = null; var rigs = new List<AccountSyncB1c2DesktopRig>();
        var cases = new List<AccountSyncB1c2Case>(); var archives = new List<AccountSyncB1c2ArchiveEvidence>();
        var accountTasks = new List<Task>();
        AccountSyncB1c2ActorEvidence[]? actors = null; var businessPassed = false; var fixtureDisposed = false;
        Exception? failure = null;
        try
        {
            initialization = AccountSyncB1c2HttpsFixture.CreateAsync(owner, created => fixture = created); Observe(initialization);
            fixture = Complete(initialization, 120);
            Complete(fixture.RegisterAsync(fixture.A)); Complete(fixture.RegisterAsync(fixture.B));
            for (var index = 1; index <= 2; index++)
            {
                var name = index == 1 ? "late-get" : "late-patch"; fixture.PreviousPump = null;
                fixture.Mark(name + ":start");
                active = new(name, index, owner, fixture); rigs.Add(active);
                cases.Add(Handoff(owner, fixture, active, name, index, archives, accountTasks));
                active.Finish(true); active = null;
            }
            actors = Complete(fixture.ReadFinalActorsAsync()); AccountSyncB1c2EvidenceContract.VerifyActors(actors, owner.RecoveryEpoch);
            AccountSyncB1c2EvidenceContract.VerifyArchives(archives, actors, fixture.Endpoint);
            CollectionAssert.AreEquivalent(AccountSyncB1cAttemptBudget.Limits.Select(p => (p.Key, p.Value)).ToArray(),
                owner.Budget.Snapshot().Operations.Select(p => (p.Key, p.Value)).ToArray());
            Assert.AreEqual(21, owner.Budget.TotalAttempts);
            Assert.AreEqual(2, owner.LogoutIntents);
            businessPassed = true;
        }
        catch (Exception error) { failure = error; owner.Abort(); throw; }
        finally
        {
            try
            {
                var cleanup = Complete(AccountSyncB1bCleanup.RunAsync(
                    ("Desktop", () => { active?.Finish(false); return Task.CompletedTask; }),
                    ("AccountTasks", async () => { try { await Task.WhenAll(accountTasks).WaitAsync(TimeSpan.FromSeconds(20)); } catch when (accountTasks.All(t => t.IsCompleted)) { foreach (var task in accountTasks) _ = task.Exception; } }),
                    ("Initialization", async () => { if (initialization is not null) try { await initialization.WaitAsync(TimeSpan.FromSeconds(20)); } catch when (initialization.IsCompleted) { _ = initialization.Exception; } }),
                    ("Fixture", async () => { if (fixture is not null) { await fixture.DisposeAsync(); fixtureDisposed = true; } })), 90);
                if (businessPassed && failure is null)
                {
                    cleanup.RequireSuccess(); Assert.IsTrue(fixtureDisposed && rigs.Count == 2 && rigs.All(r => r.CleanupPassed));
                    var completedFixture = fixture!; var finalActors = actors!;
                    owner.WriteEvidence("scoped-inputs-evidence.json", new { Version = 1, owner.OwnerId, owner.RecoveryEpoch,
                        Accounts = finalActors.Select(a => new { a.Actor, a.UserId, a.PetId, a.DefaultRoomId, a.SessionIds, a.Nickname, a.ProfileVersion, a.ConfigurationVersion, a.Configuration }).ToArray() });
                    owner.WriteEvidence("configuration-sync-evidence.json", new { Version = 1, owner.OwnerId, Count = archives.Count,
                        ReceiptCount = archives.Count(a => a.ReceiptPresent), ExactOriginalsAndReceipts = true, Archives = archives });
                    owner.WriteEvidence("fault-evidence.json", new { Version = 2, owner.OwnerId, Completed = true, Holds = completedFixture.Holds,
                        Requests = completedFixture.Requests, Phases = completedFixture.Phases, CoordinatorsStopped = true,
                        DispatcherOperationsSettled = rigs.All(r => r.CleanupPassed), OriginalTasksSettled = completedFixture.Tasks.All(t => t.IsCompleted)
                            && completedFixture.OperationTasks.All(t => t.IsCompleted) && accountTasks.All(t => t.IsCompleted),
                        StopwatchFrequency = Stopwatch.Frequency, LifecycleObserversSettled = completedFixture.Trace.ObserversSettled,
                        LifecycleEvents = completedFixture.Trace.Events, LogoutWindows = completedFixture.Trace.Windows });
                    // Preserve the actual trace even when the diagnostic acceptance gate
                    // rejects it. Completed describes business/cleanup, not gate acceptance.
                    AccountSyncB1c2LifecycleContract.Verify(completedFixture.Requests, completedFixture.Phases, completedFixture.Trace.Events,
                        completedFixture.Trace.Windows, Stopwatch.Frequency, completedFixture.Trace.ObserversSettled);
                    owner.WriteEvidence("journey-evidence.json", new { Version = 1, owner.OwnerId, Passed = true, Actors = finalActors, Cases = cases,
                        ConfigurationAttempts = 3, ConfigurationSuccessfulWrites = 3, LoginWrites = 4, ProfileWrites = 2, LogoutWrites = 4,
                        DesktopHosts = 2, HttpsHosts = 1, HttpRequests = owner.Budget.HttpRequests, DurationSeconds = elapsed.Elapsed.TotalSeconds,
                        DesktopTransports = completedFixture.DesktopTransports, completedFixture.ClientsCreated, UsesSystemTrust = false,
                        UsesWindowsCredentials = false, ActualFormalHost = true, ActualApplicationHost = true,
                        HealthyNativeFrames = rigs.All(r => r.HealthyFrameObserved), LocalLogoutIntents = owner.LogoutIntents, CleanupPassed = true });
                }
                else cleanup.RequireSuccess();
            }
            finally { owner.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }

    private static AccountSyncB1c2Case Handoff(AccountSyncB1c2TestOwner owner, AccountSyncB1c2HttpsFixture fixture,
        AccountSyncB1c2DesktopRig rig, string name, int index, List<AccountSyncB1c2ArchiveEvidence> archives, List<Task> accountTasks)
    {
        var patch = index == 2; var aBaseline = fixture.A.Snapshot(1, 103);
        rig.Login(fixture.A);
        if (!patch)
        {
            rig.Wait(() => rig.Controller.IsLoggedIn && rig.PrimaryEnabled && rig.PrimaryName == "保存昵称");
            Assert.IsFalse(rig.Controller.ConfigurationReady);
            rig.Uia(u => { u.Value("AccountNickname", AccountSyncB1c2EvidenceContract.Nickname("a")); u.InvokeId("AccountPrimary"); });
        }
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled); rig.CloseAccount();
        var cleanup = fixture.CopyCleanupClient(index); using var cleanupAccount = cleanup.Account; using var cleanupSecrets = cleanup.Secrets;
        fixture.Arm(name, cleanup.Session);
        byte[]? submittedBytes = null; JsonElement? submittedConfiguration = null;
        if (patch) { rig.SaveEyeSize(111); submittedBytes = rig.ReadDiskBytes(); submittedConfiguration = rig.ReadDiskConfiguration(); }
        var hold = fixture.CurrentHold!; rig.Wait(() => hold.Captured.IsCompleted); hold.Captured.GetAwaiter().GetResult();
        var oldPump = rig.CurrentPump; var oldSync = AccountSyncB1c2DesktopRig.Field<PetConfigurationLiveSyncCoordinator?>(rig.Controller, "configurationSync");
        Assert.IsNotNull(oldSync); Assert.IsFalse(oldPump.IsCompleted); fixture.PreviousPump = oldPump;
        var capturedPhase = fixture.Phases.Single(p => p.Phase == "actual-response-captured" && p.ClientId == cleanup.SourceId);
        Assert.IsNotNull(capturedPhase.RequestSequence);
        var heldSequence = capturedPhase.RequestSequence;
        var heldRequest = fixture.Requests.Single(r => r.Sequence == heldSequence);
        Assert.AreEqual(cleanup.SourceId, heldRequest.ClientId); Assert.AreEqual(patch ? "PATCH" : "GET", heldRequest.Method);
        Assert.AreEqual(patch ? "/api/v1/me/pet/configuration" : "/api/v1/me/pet", heldRequest.Path);
        Assert.AreEqual(patch ? 2L : 1L, heldRequest.ResponseVersion); Assert.IsTrue(patch || heldRequest.PetReadOrdinal >= 3);
        Assert.AreEqual(cleanup.Session, heldRequest.SessionId); Assert.AreEqual(fixture.A.UserId, heldRequest.UserId); Assert.AreEqual(fixture.A.PetId, heldRequest.PetId);
        if (patch) { Assert.AreEqual(1L, heldRequest.ExpectedVersion); Assert.AreEqual(AccountSyncB1c2EvidenceContract.ConfigurationSha256(submittedConfiguration!.Value), heldRequest.ConfigurationSha256); }
        rig.OpenAccount(); owner.RecordLogoutIntent(name, cleanup.SourceId, fixture.A.UserId, fixture.A.PetId, cleanup.Session);
        rig.Uia(u => u.InvokeName("退出登录"));
        rig.Wait(() => !rig.Controller.IsLoggedIn && hold.Snapshot().CancellationObserved); fixture.Retire(cleanup.SourceId);
        Assert.AreEqual(PetLiveSyncStatus.Stopped, oldSync.State.Status); Assert.IsFalse(rig.ConfigurationTimer.IsEnabled); Assert.IsFalse(oldPump.IsCompleted);
        var cancelledLogoutIntent = fixture.Requests.Count(r => r.ClientId == cleanup.SourceId && r.Path == "/api/v1/auth/logout") == 0;
        Assert.IsTrue(cancelledLogoutIntent);
        rig.CloseAccount(); rig.Wait(() => AccountSyncB1c2DesktopRig.Field<SemaphoreSlim>(rig.Controller, "operations").CurrentCount == 1);
        Assert.IsTrue(AccountSyncB1c2DesktopRig.Field<bool>(rig.Controller, "rememberedCleanupNeeded"));
        rig.SaveEyeSize(118); var offlineBytes = rig.ReadDiskBytes(); var offlineConfiguration = rig.ReadDiskConfiguration();
        Assert.IsFalse(oldPump.IsCompleted); Assert.IsTrue(AppearanceContract.Equivalent(offlineConfiguration, AccountSyncB1c2EvidenceContract.Sample("a", 118)));
        rig.OpenAccount(); rig.Uia(u => u.InvokeName("重试清除记住登录"));
        rig.Wait(() => !AccountSyncB1c2DesktopRig.Field<bool>(rig.Controller, "busy") && rig.PrimaryEnabled && rig.PrimaryName == "登录");
        Assert.IsFalse(AccountSyncB1c2DesktopRig.Field<bool>(rig.Controller, "rememberedCleanupNeeded"));
        rig.Login(fixture.B);
        if (!patch)
        {
            rig.Wait(() => rig.Controller.IsLoggedIn && rig.PrimaryEnabled && rig.PrimaryName == "保存昵称");
            rig.Uia(u => { u.Value("AccountNickname", AccountSyncB1c2EvidenceContract.Nickname("b")); u.InvokeId("AccountPrimary"); });
        }
        rig.Wait(() => rig.Controller.IsLoggedIn && rig.Controller.Nickname == AccountSyncB1c2EvidenceContract.Nickname("b")
            && AccountSyncB1c2DesktopRig.Field<bool>(rig.Controller, "busy"));
        var bClient = fixture.DesktopClientId(index, fixture.B);
        Assert.IsFalse(rig.Controller.ConfigurationReady); Assert.IsFalse(oldPump.IsCompleted);
        Assert.AreEqual(1, fixture.Requests.Count(r => r.ClientId == bClient && r.Path == "/api/v1/me/pet"));
        rig.Uia(u => { Assert.IsFalse(u.HasId("AccountCloudChoice")); Assert.AreEqual(AccountSyncB1c2EvidenceContract.Nickname("b"), u.Name("AccountButton")); });
        CollectionAssert.AreEqual(offlineBytes, rig.ReadDiskBytes()); fixture.Mark(name + ":new-identity-awaiting-old-pump", bClient);
        hold.Release(); fixture.Mark(name + ":old-response-released", cleanup.SourceId);
        rig.Wait(() => oldPump.IsCompleted && rig.HasChoice && !AccountSyncB1c2DesktopRig.Field<bool>(rig.Controller, "busy") && !rig.PrimaryEnabled);
        oldPump.GetAwaiter().GetResult(); fixture.Mark(name + ":old-pump-settled", cleanup.SourceId);
        Assert.IsTrue(hold.Snapshot().Settled); Assert.AreEqual(PetLiveSyncStatus.Stopped, oldSync.State.Status);
        rig.Uia(u => { Assert.IsFalse(u.Selected("AccountLocalChoice")); Assert.IsFalse(u.Selected("AccountCloudChoice")); Assert.IsFalse(u.Enabled("AccountPrimary")); });
        CollectionAssert.AreEqual(offlineBytes, rig.ReadDiskBytes());
        var comparison = fixture.Requests.Single(r => r.ClientId == bClient && r.Path == "/api/v1/me/pet" && r.PetReadOrdinal >= 2);
        Assert.AreEqual(true, comparison.PreviousPumpSettled); Assert.AreEqual(patch ? 2L : 1L, comparison.ResponseVersion);
        fixture.Mark(name + ":new-baseline-verified", bClient, comparison.Sequence); rig.Mark("comparison-ready", capture: true);
        var bBaseline = fixture.B.Snapshot(patch ? 2 : 1, patch ? 125 : 103);
        rig.Uia(u => { u.Select("AccountCloudChoice"); Assert.IsTrue(u.Selected("AccountCloudChoice")); Assert.IsFalse(u.Selected("AccountLocalChoice")); Assert.IsTrue(u.Enabled("AccountPrimary")); u.InvokeId("AccountPrimary"); });
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        Assert.AreEqual(fixture.B.PetId, rig.Controller.RoomClient!.PetId); Assert.IsTrue(AppearanceContract.Equivalent(bBaseline.Configuration, rig.ReadDiskConfiguration()));
        var choice = rig.Archives(fixture.B, "configuration-choices").Single();
        rig.AssertArchive(choice, fixture.B, offlineBytes, offlineConfiguration, bBaseline, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, bBaseline.ConfigurationVersion);
        archives.Add(ProjectArchive(rig, choice, fixture.B, name, name + "-choice")); rig.CloseAccount();
        rig.SaveEyeSize(patch ? 130 : 125); var savedBytes = rig.ReadDiskBytes(); var savedConfiguration = rig.ReadDiskConfiguration();
        rig.Wait(() => rig.ReceiptCount(fixture.B, "configuration-sync") == 1); rig.Wait(() => rig.CurrentPump.IsCompleted);
        rig.CurrentPump.GetAwaiter().GetResult(); var reads = fixture.Requests.Count(r => r.ClientId == bClient && r.Path == "/api/v1/me/pet");
        rig.Wait(() => fixture.Requests.Count(r => r.ClientId == bClient && r.Path == "/api/v1/me/pet") >= reads + 2); rig.Wait(() => rig.CurrentPump.IsCompleted);
        var savedPatch = fixture.Requests.Single(r => r.ClientId == bClient && r.Method == "PATCH" && r.Path == "/api/v1/me/pet/configuration");
        var expectedPatchVersion = bBaseline.ConfigurationVersion;
        var expectedResponseVersion = expectedPatchVersion + 1;
        Assert.AreEqual(expectedPatchVersion, savedPatch.ExpectedVersion); Assert.AreEqual(expectedResponseVersion, savedPatch.ResponseVersion);
        Assert.AreEqual(AccountSyncB1c2EvidenceContract.ConfigurationSha256(savedConfiguration), savedPatch.ConfigurationSha256);
        Assert.IsTrue(AppearanceContract.Equivalent(savedConfiguration, AccountSyncB1c2EvidenceContract.Sample("b", patch ? 130 : 125)));
        CollectionAssert.AreEqual(savedBytes, rig.ReadDiskBytes());
        var upload = rig.Archives(fixture.B, "configuration-sync").Single();
        rig.AssertArchive(upload, fixture.B, savedBytes, savedConfiguration, bBaseline, PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, bBaseline.ConfigurationVersion + 1);
        if (patch)
        {
            var original = rig.Archives(fixture.A, "configuration-sync").Single();
            rig.AssertOriginal(original, fixture.A, submittedBytes!, submittedConfiguration!.Value, aBaseline, PetConfigurationChoice.Local);
            Assert.IsNull(rig.LoadReceipt(original)); archives.Insert(2, ProjectArchive(rig, original, fixture.A, name, "late-patch-unacknowledged"));
        }
        else Assert.AreEqual(0, rig.Archives(fixture.A, "configuration-sync").Length);
        archives.Add(ProjectArchive(rig, upload, fixture.B, name, name + "-save")); fixture.Mark(name + ":b-save-converged", bClient, savedPatch.Sequence);
        Assert.AreEqual(0, fixture.Requests.Count(r => r.ClientId == cleanup.SourceId && r.Retired));
        var identityTask = cleanupAccount.ReadIdentityAsync(); accountTasks.Add(identityTask); Observe(identityTask);
        var identity = Complete(identityTask); Assert.AreEqual(fixture.A.UserId, identity.UserId); Assert.AreEqual(fixture.A.PetId, identity.PetId);
        var logoutTask = cleanupAccount.LogoutAsync(); accountTasks.Add(logoutTask); Observe(logoutTask);
        Complete(logoutTask); fixture.Mark(name + ":a-session-cleaned", fixture.ClientId(cleanupAccount));
        rig.Uia(u => Assert.AreEqual(AccountSyncB1c2EvidenceContract.Nickname("b"), u.Name("AccountButton")));
        rig.AssertHealthyFrame(); rig.Mark("handoff-complete", capture: true);
        rig.OpenAccount();
        var logoutWindow = fixture.Trace.OpenWindow(name, bClient, fixture.B.UserId, fixture.B.PetId, savedPatch.SessionId!.Value, savedPatch.Sequence);
        rig.Uia(u =>
        {
            fixture.Trace.Record("UiaLogoutInvoked", window: logoutWindow);
            try { u.InvokeName("退出登录"); }
            finally { fixture.Trace.Record("UiaLogoutReturned", window: logoutWindow); }
        });
        rig.Wait(() => !rig.Controller.IsLoggedIn && !AccountSyncB1c2DesktopRig.Field<bool>(rig.Controller, "busy") && rig.PrimaryEnabled && rig.PrimaryName == "登录");
        Assert.AreEqual(1, fixture.Requests.Count(r => r.ClientId == bClient && r.Path == "/api/v1/auth/logout"));
        rig.Wait(() => fixture.Trace.ObserversSettled);
        var bLogout = fixture.Requests.Single(r => r.ClientId == bClient && r.Path == "/api/v1/auth/logout");
        Assert.AreEqual(200, bLogout.ResponseStatus);
        fixture.Trace.CloseWindow(logoutWindow, bLogout.Sequence, !rig.Controller.IsLoggedIn);
        fixture.Mark(name + ":complete", bClient);
        var snapshot = hold.Snapshot();
        var requestsAfterRetirement = fixture.Requests.Count(r => r.ClientId == cleanup.SourceId && r.Retired);
        Assert.AreEqual(0, requestsAfterRetirement);
        return new(name, true, snapshot.Method, snapshot.ResponseVersion, bBaseline.ConfigurationVersion, snapshot.CancellationObserved,
            snapshot.Released, snapshot.Settled, comparison.PreviousPumpSettled == true, requestsAfterRetirement, cancelledLogoutIntent, false);
    }

    private static AccountSyncB1c2ArchiveEvidence ProjectArchive(AccountSyncB1c2DesktopRig rig, AccountSyncB1c2Archive archive,
        AccountSyncB1c2Actor actor, string name, string step)
    {
        var original = rig.LoadOriginal(archive); var receipt = rig.LoadReceipt(archive);
        var selectedChoice = original.Choice ?? throw new AssertFailedException("Every persisted B1c original must record its explicit source choice.");
        return new(actor.Actor, name, step, archive.Id, original.Origin.AbsoluteUri, actor.UserId, actor.PetId,
            AccountSyncB1c2EvidenceContract.BytesSha256(original.Local.OriginalBytes.Span), AccountSyncB1c2EvidenceContract.ConfigurationSha256(original.Local.NormalizedConfiguration),
            AccountSyncB1c2EvidenceContract.ConfigurationSha256(original.Cloud.Configuration), original.Cloud.ConfigurationVersion,
            (int)selectedChoice, receipt is not null, receipt is null ? null : (int)receipt.Outcome, receipt?.ObservedCloudVersion, receipt?.FailureCode);
    }
    private static T Complete<T>(Task<T> task, int seconds = 30) { Wait(() => task.IsCompleted, seconds); return task.GetAwaiter().GetResult(); }
    private static void Complete(Task task, int seconds = 30) { Wait(() => task.IsCompleted, seconds); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> condition, int seconds)
    {
        if (condition()) return; var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || watch.Elapsed >= TimeSpan.FromSeconds(seconds)) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); } Assert.IsTrue(condition(), "The owned B1c1 task did not settle within its hard bound.");
    }
    private static void Observe(Task task) => _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
