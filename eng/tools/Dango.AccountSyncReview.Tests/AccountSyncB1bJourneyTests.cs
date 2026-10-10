using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[DoNotParallelize]
[TestCategory("AccountSyncB1b1")]
public sealed class AccountSyncB1bJourneyTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void RealHttpsCoordinatorsHandleConcurrencyLostResponsesAndHostRecovery()
    {
        // The independent grant is checked before files, credentials, certificates, hosts or SQL.
        var owner = AccountSyncB1bTestOwner.Require();
        var elapsed = Stopwatch.StartNew();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        AccountSyncB1bHttpsFixture? fixture = null;
        Task<AccountSyncB1bHttpsFixture>? initialization = null;
        Exception? journeyFailure = null;
        Device? primary = null, secondary = null;
        var expectations = new List<ArchiveExpectation>();
        var cases = new List<CaseEvidence>();
        using var primarySecrets = new MemorySecrets(); using var secondarySecrets = new MemorySecrets();
        try
        {
            initialization = AccountSyncB1bHttpsFixture.CreateAsync(owner, created => fixture = created);
            ObserveFault(initialization);
            fixture = Complete(initialization, 120);
            primary = new(owner, AccountSyncB1bFaultControl.PrimaryDevice);
            secondary = new(owner, AccountSyncB1bFaultControl.SecondaryDevice);
            var initial = primary.Capture();
            Assert.IsTrue(initial.SameOriginal(secondary.Capture()));
            using var primaryAccount = fixture.CreateClient(primary.Name, primarySecrets);
            using var secondaryAccount = fixture.CreateClient(secondary.Name, secondarySecrets);
            Assert.AreNotSame(primaryAccount.Cookies, secondaryAccount.Cookies);
            var registration = RegistrationAttempt.Create(fixture.Email, fixture.InvitationId, fixture.InvitationProof);
            Complete(primaryAccount.StartRegistrationAsync(registration));
            Complete(fixture.ConfirmAsync());
            var registered = Complete(primaryAccount.CompleteRegistrationAsync(registration, fixture.Password, initial.NormalizedConfiguration));
            Assert.IsNotNull(fixture.Identity);
            Assert.AreEqual(fixture.Identity.UserId, registered.UserId);
            Assert.AreEqual(fixture.Identity.PetId, registered.PetId);
            var first = Complete(primaryAccount.LoginAsync(fixture.Email, fixture.Password, false));
            var second = Complete(secondaryAccount.LoginAsync(fixture.Email, fixture.Password, false));
            Assert.AreEqual(fixture.Identity, first); Assert.AreEqual(first, second);
            var profile = primaryAccount.CreateProfileClient(first);
            var emptyProfile = Complete(profile.ReadAsync(CancellationToken.None));
            Assert.IsNull(emptyProfile.Nickname); Assert.AreEqual(1L, emptyProfile.ProfileVersion);
            var named = Complete(profile.UpdateAsync(emptyProfile.ProfileVersion, "异常同步团子", CancellationToken.None));
            Assert.AreEqual("异常同步团子", named.Nickname); Assert.AreEqual(2L, named.ProfileVersion);
            var primaryRemote = primaryAccount.CreatePetClient(first);
            var secondaryRemote = secondaryAccount.CreatePetClient(second);
            var baseline = Complete(primaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(1L, baseline.ConfigurationVersion); Assert.AreEqual(103d, Size(baseline));
            primary.Attach(primaryRemote, baseline); secondary.Attach(secondaryRemote, baseline);
            var roomFacts = Complete(ReadIdentityAndRoomFactsAsync(fixture));

            // Case 1: the two real requests reach before_locks with different live PostgreSQL backends.
            fixture.Faults.ArmCas();
            var casPrimary = primary.Save(AccountSyncB1bAppearanceSamples.CasPrimary);
            var casSecondary = secondary.Save(AccountSyncB1bAppearanceSamples.CasSecondary);
            Complete(Task.WhenAll(primary.Pump(), secondary.Pump()));
            var raced = Complete(primaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(2L, raced.ConfigurationVersion);
            var casRows = fixture.PatchSnapshot(); Assert.AreEqual(2, casRows.Count);
            Assert.AreEqual(1, casRows.Count(row => row.ResponseStatus == 200));
            Assert.AreEqual(1, casRows.Count(row => row.ResponseStatus == 409));
            Assert.IsTrue(casRows.All(row => row.Forwarded && row.ExpectedVersion == 1));
            var winner = casRows.Single(row => row.ResponseStatus == 200).Device;
            Assert.AreEqual(winner == primary.Name ? 111d : 114d, Size(raced));
            AssertPatch(casRows.Single(row => row.Device == primary.Name), casPrimary);
            AssertPatch(casRows.Single(row => row.Device == secondary.Name), casSecondary);
            Assert.IsTrue(AppearanceContract.Equivalent(raced.Configuration,
                winner == primary.Name ? casPrimary.NormalizedConfiguration : casSecondary.NormalizedConfiguration));
            expectations.Add(new(primary.Name, "cas-primary", casPrimary, baseline, PetConfigurationChoice.Local,
                winner == primary.Name ? PetChoiceOutcome.CloudUpdated : PetChoiceOutcome.Conflict, 2));
            expectations.Add(new(secondary.Name, "cas-secondary", casSecondary, baseline, PetConfigurationChoice.Local,
                winner == secondary.Name ? PetChoiceOutcome.CloudUpdated : PetChoiceOutcome.Conflict, 2));
            expectations.Add(new(winner == primary.Name ? secondary.Name : primary.Name, "cas-loser-follow",
                winner == primary.Name ? casSecondary : casPrimary, raced, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 2));
            Converge(primary, secondary, raced); Assert.AreEqual(2, ConfigAttempts(owner));
            var casFault = fixture.Faults.Snapshot();
            Assert.AreEqual(2, casFault.CasArrivals); Assert.AreEqual(2, casFault.CasCompleted);
            Assert.AreEqual(2, casFault.BackendPids.Distinct().Count());
            fixture.Faults.AssertSettled();
            cases.Add(new("CAS", 2, 1, 2, true));

            // Case 2: the server has committed before the pinned handler loses the success response.
            var secondaryBeforeLoss = secondary.Capture();
            fixture.Faults.ArmDropAcceptedResponse(hold: false);
            var lostOriginal = primary.Save(AccountSyncB1bAppearanceSamples.LostAccepted);
            Complete(primary.Pump());
            Assert.AreEqual(PetChoiceOutcome.TargetSatisfied, primary.Sync.State.LastLocalSaveOutcome);
            Assert.AreEqual(3, ConfigAttempts(owner));
            var recovered = Complete(secondaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(3L, recovered.ConfigurationVersion); Assert.AreEqual(118d, Size(recovered));
            AssertPatch(fixture.PatchSnapshot()[2], lostOriginal);
            Assert.IsTrue(AppearanceContract.Equivalent(recovered.Configuration, lostOriginal.NormalizedConfiguration));
            expectations.Add(new(primary.Name, "lost-accepted-target-satisfied", lostOriginal, raced,
                PetConfigurationChoice.Local, PetChoiceOutcome.TargetSatisfied, 3));
            expectations.Add(new(secondary.Name, "lost-accepted-secondary-follow", secondaryBeforeLoss, recovered,
                PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 3));
            Converge(primary, secondary, recovered); Assert.AreEqual(3, ConfigAttempts(owner));
            fixture.Faults.AssertSettled();
            cases.Add(new("LostAcceptedResponse", 1, 1, 3, true));

            // Case 3: another session commits while the first successful response is held, then lost.
            fixture.Faults.ArmDropAcceptedResponse(hold: true);
            var supersededOriginal = primary.Save(AccountSyncB1bAppearanceSamples.Superseded);
            var heldPump = primary.Pump();
            Complete(fixture.Faults.WaitForAcceptedPatchAsync());
            var heldCommit = Complete(secondaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(4L, heldCommit.ConfigurationVersion); Assert.AreEqual(125d, Size(heldCommit));
            Assert.IsTrue(AppearanceContract.Equivalent(heldCommit.Configuration, supersededOriginal.NormalizedConfiguration));
            var newerOriginal = secondary.Save(AccountSyncB1bAppearanceSamples.Newer);
            Complete(secondary.Pump());
            var newer = Complete(secondaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(5L, newer.ConfigurationVersion); Assert.AreEqual(128d, Size(newer));
            Assert.IsTrue(AppearanceContract.Equivalent(newer.Configuration, newerOriginal.NormalizedConfiguration));
            fixture.Faults.ReleaseAcceptedPatch(); Complete(heldPump);
            Assert.AreEqual(PetChoiceOutcome.Conflict, primary.Sync.State.LastLocalSaveOutcome);
            expectations.Add(new(primary.Name, "lost-accepted-superseded", supersededOriginal, recovered,
                PetConfigurationChoice.Local, PetChoiceOutcome.Conflict, 5));
            expectations.Add(new(secondary.Name, "newer-secondary-save", newerOriginal, heldCommit,
                PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 5));
            expectations.Add(new(primary.Name, "superseded-primary-follow", supersededOriginal, newer,
                PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 5));
            Converge(primary, secondary, newer); Assert.AreEqual(5, ConfigAttempts(owner));
            var afterNewer = fixture.PatchSnapshot();
            Assert.AreEqual(3L, afterNewer[3].ExpectedVersion); Assert.AreEqual(125d, afterNewer[3].EyeSize);
            Assert.AreEqual(4L, afterNewer[4].ExpectedVersion); Assert.AreEqual(128d, afterNewer[4].EyeSize);
            AssertPatch(afterNewer[3], supersededOriginal); AssertPatch(afterNewer[4], newerOriginal);
            fixture.Faults.AssertSettled();
            cases.Add(new("LostResponseThenNewerCommit", 2, 2, 5, true));

            // Case 4: the first reserved PATCH is not forwarded. Its only retry retains content and version.
            var secondaryBeforeRetry = secondary.Capture();
            fixture.Faults.ArmDropBeforeForward();
            var retriedOriginal = primary.Save(AccountSyncB1bAppearanceSamples.Retry);
            Complete(primary.Pump());
            Assert.AreEqual(PetLiveSyncStatus.Offline, primary.Sync.State.Status);
            Assert.AreEqual(6, ConfigAttempts(owner));
            var unchanged = Complete(secondaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(5L, unchanged.ConfigurationVersion); Assert.AreEqual(128d, Size(unchanged));
            Assert.IsFalse(fixture.PatchSnapshot()[5].Forwarded);
            Complete(primary.Pump());
            Assert.AreEqual(PetChoiceOutcome.CloudUpdated, primary.Sync.State.LastLocalSaveOutcome);
            var retried = Complete(secondaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(6L, retried.ConfigurationVersion); Assert.AreEqual(137d, Size(retried));
            Assert.IsTrue(AppearanceContract.Equivalent(retried.Configuration, retriedOriginal.NormalizedConfiguration));
            var retryRows = fixture.PatchSnapshot().Skip(5).ToArray(); Assert.AreEqual(2, retryRows.Length);
            Assert.IsTrue(retryRows.All(row => row.ExpectedVersion == 5 && row.EyeSize == 137));
            Assert.IsFalse(retryRows[0].Forwarded); Assert.IsTrue(retryRows[1].Forwarded);
            AssertPatch(retryRows[0], retriedOriginal); AssertPatch(retryRows[1], retriedOriginal);
            Assert.IsTrue(AppearanceContract.Equivalent(retryRows[0].Configuration, retryRows[1].Configuration));
            Assert.AreEqual(retryRows[0].ConfigurationSha256, retryRows[1].ConfigurationSha256);
            expectations.Add(new(primary.Name, "unforwarded-original-version-retry", retriedOriginal, newer,
                PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 6));
            expectations.Add(new(secondary.Name, "retried-secondary-follow", secondaryBeforeRetry, retried,
                PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 6));
            Converge(primary, secondary, retried); Assert.AreEqual(7, ConfigAttempts(owner));
            fixture.Faults.AssertSettled();
            cases.Add(new("UnforwardedAttempt", 2, 1, 6, true));

            // Case 5: no host accepts either offline save; recovery sends only the newest pending save.
            var secondaryBeforeRecovery = secondary.Capture();
            var sameOrigin = fixture.Endpoint;
            Complete(fixture.StopHostForRecoveryAsync(), 90);
            primary.Save(AccountSyncB1bAppearanceSamples.RecoveryIntermediate); Complete(primary.Pump());
            Assert.AreEqual(PetLiveSyncStatus.Offline, primary.Sync.State.Status);
            Assert.IsTrue(primary.Sync.State.HasPendingLocalSave); Assert.AreEqual(AccountSyncB1bAppearanceSamples.RecoveryIntermediate, primary.DiskSize);
            Assert.AreEqual(7, ConfigAttempts(owner));
            var recoveryOriginal = primary.Save(AccountSyncB1bAppearanceSamples.RecoveryNewest); Complete(primary.Pump());
            Assert.AreEqual(PetLiveSyncStatus.Offline, primary.Sync.State.Status);
            Assert.IsTrue(primary.Sync.State.HasPendingLocalSave); Assert.AreEqual(AccountSyncB1bAppearanceSamples.RecoveryNewest, primary.DiskSize);
            Assert.AreEqual(7, ConfigAttempts(owner)); Assert.AreEqual(7, fixture.PatchSnapshot().Count);
            Complete(fixture.RebuildHostAsync()); Assert.AreEqual(sameOrigin, fixture.Endpoint);
            Complete(primary.Pump());
            var finalCloud = Complete(secondaryRemote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(7L, finalCloud.ConfigurationVersion); Assert.AreEqual(AccountSyncB1bAppearanceSamples.RecoveryNewest, Size(finalCloud));
            var recoveryRow = fixture.PatchSnapshot().Single(row => row.Sequence == 8);
            Assert.AreEqual(6L, recoveryRow.ExpectedVersion); Assert.AreEqual(AccountSyncB1bAppearanceSamples.RecoveryNewest, recoveryRow.EyeSize);
            AssertPatch(recoveryRow, recoveryOriginal);
            Assert.IsTrue(AppearanceContract.Equivalent(finalCloud.Configuration, recoveryOriginal.NormalizedConfiguration));
            expectations.Add(new(primary.Name, "host-recovery-newest-save", recoveryOriginal, retried,
                PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 7));
            expectations.Add(new(secondary.Name, "host-recovery-secondary-follow", secondaryBeforeRecovery, finalCloud,
                PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 7));
            Converge(primary, secondary, finalCloud); Assert.AreEqual(8, ConfigAttempts(owner));
            cases.Add(new("HostRecovery", 1, 1, 7, true));

            Assert.AreEqual(roomFacts, Complete(ReadIdentityAndRoomFactsAsync(fixture)), "Configuration requests changed identity or room facts.");
            VerifyArchives(owner, fixture.Identity, fixture.Endpoint, [primary, secondary], expectations);
            var patches = fixture.PatchSnapshot();
            Assert.AreEqual(8, patches.Count); Assert.AreEqual(6, patches.Count(row => row.ResponseStatus == 200));
            fixture.Faults.AssertSettled();
            var controls = fixture.Faults.Snapshot();
            Assert.AreEqual(1, controls.AcceptedDropHits); Assert.AreEqual(1, controls.AcceptedHoldHits);
            Assert.AreEqual(1, controls.BeforeForwardDropHits);
            primary.StopAndDrain(); secondary.StopAndDrain();
            Complete(primaryAccount.LogoutAsync()); Complete(secondaryAccount.LogoutAsync());
            var final = Complete(fixture.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<DangoDbContext>();
                var user = await db.Users.AsNoTracking().SingleAsync(row => row.Id == fixture.Identity.UserId);
                var pet = await db.Pets.AsNoTracking().SingleAsync(row => row.UserId == user.Id);
                var sessions = await db.AuthSessions.AsNoTracking().Where(row => row.UserId == user.Id).ToListAsync();
                var mail = await (from job in db.EmailJobs.AsNoTracking()
                                  join challenge in db.EmailChallenges.AsNoTracking() on job.ChallengeId equals challenge.Id
                                  where challenge.NormalizedEmail == user.NormalizedEmail select job).SingleAsync();
                return new { UserId = user.Id, PetId = pet.Id, user.Nickname, user.ProfileVersion, pet.ConfigurationVersion,
                    Sessions = sessions.Count, Revoked = sessions.Count(row => row.RevokedAt != null), mail.Attempts,
                    MailState = mail.State.ToString(), DefaultRoomId = fixture.DefaultRoomId, owner.RecoveryEpoch };
            }));
            Assert.AreEqual(fixture.Identity.UserId, final.UserId); Assert.AreEqual(fixture.Identity.PetId, final.PetId);
            Assert.AreEqual("异常同步团子", final.Nickname); Assert.AreEqual(2L, final.ProfileVersion);
            Assert.AreEqual(7L, final.ConfigurationVersion); Assert.AreEqual(2, final.Sessions); Assert.AreEqual(2, final.Revoked);
            Assert.AreEqual(0, final.Attempts); Assert.AreEqual("Pending", final.MailState);
            CollectionAssert.AreEquivalent(AccountSyncB1bAttemptBudget.Limits.Select(pair => (pair.Key, pair.Value)).ToArray(),
                owner.Budget.Snapshot().Operations.Select(pair => (pair.Key, pair.Value)).ToArray());
            owner.Budget.CheckAlive();
            owner.WriteEvidence("fault-evidence.json", new { Version = 1, owner.OwnerId, Completed = true, Controls = controls,
                PatchAttempts = patches, CoordinatorsStopped = primary.Sync.State.Status == PetLiveSyncStatus.Stopped
                    && secondary.Sync.State.Status == PetLiveSyncStatus.Stopped,
                DispatcherOperationsSettled = primary.DispatcherSettled && secondary.DispatcherSettled });
            owner.WriteEvidence("journey-evidence.json", new { Version = 1, owner.OwnerId, Passed = true, Final = final,
                ConfigurationAttempts = 8, ConfigurationSuccessfulWrites = 6, LoginWrites = 2, ProfileWrites = 1, LogoutWrites = 2,
                HttpsHosts = 2, Certificates = 1, HttpRequests = owner.Budget.HttpRequests, DurationSeconds = elapsed.Elapsed.TotalSeconds,
                UsesSystemTrust = false, UsesWindowsCredentials = false, ActualFormalHost = true, ActualApplicationHost = false,
                ActualWpfFileAdapter = true, Exact5Cases = true, Cases = cases });
        }
        catch (Exception error) { journeyFailure = error; owner.Abort(); throw; }
        finally
        {
            try
            {
                // No failed step can skip another device, the real dispatcher barrier, or TLS/host cleanup.
                var cleanup = Complete(AccountSyncB1bCleanup.RunAsync(
                    ("PrimaryStop", () => { primary?.Stop(); return Task.CompletedTask; }),
                    ("SecondaryStop", () => { secondary?.Stop(); return Task.CompletedTask; }),
                    ("FaultControls", () => fixture?.Faults.DisposeAsync().AsTask() ?? Task.CompletedTask),
                    ("PrimaryDrain", () => primary?.DrainAsync() ?? Task.CompletedTask),
                    ("SecondaryDrain", () => secondary?.DrainAsync() ?? Task.CompletedTask),
                    ("Fixture", () => fixture?.DisposeAsync().AsTask() ?? Task.CompletedTask),
                    ("InitializationTask", async () =>
                    {
                        if (initialization is not null) await initialization.WaitAsync(TimeSpan.FromSeconds(20));
                    }),
                    ("PrimaryDispose", () => { primary?.Dispose(); return Task.CompletedTask; }),
                    ("SecondaryDispose", () => { secondary?.Dispose(); return Task.CompletedTask; }),
                    ("OwnerDispose", () => { owner.Dispose(); return Task.CompletedTask; })), 240);
                try
                {
                    TestContext.WriteLine("AccountSyncB1bCleanup=" + JsonSerializer.Serialize(new
                    {
                        cleanup.Passed, cleanup.Failures,
                        InitializationSettled = initialization is null || initialization.IsCompleted,
                        InitializationSucceeded = initialization?.IsCompletedSuccessfully ?? false,
                        HostLifecycleTasks = fixture?.LifecycleSnapshot()
                    }));
                }
                catch when (journeyFailure is not null) { } // Preserve the original failed journey; a normal-path report failure still fails.
                if (journeyFailure is null) cleanup.RequireSuccess();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }

    private sealed record CaseEvidence(string Case, int Attempts, int SuccessfulWrites, long FinalVersion, bool Passed);
    private sealed record ArchiveExpectation(string Device, string Step, LocalPetConfigurationSnapshot Local,
        PetConfigurationSnapshot Cloud, PetConfigurationChoice Choice, PetChoiceOutcome Outcome, long ObservedVersion);
    private sealed record ArchiveEvidence(string Device, string Step, Guid JournalId, string LocalOriginalSha256,
        long ConfigurationVersion, PetConfigurationChoice Choice, PetChoiceOutcome Outcome, long ObservedCloudVersion,
        string? FailureCode, string LocalConfigurationSha256, string CloudConfigurationSha256);

    private static int ConfigAttempts(AccountSyncB1bTestOwner owner) => owner.Budget.Snapshot().Operations[AccountSyncOperation.Configuration];
    private static double Size(PetConfigurationSnapshot snapshot) => snapshot.Configuration.GetProperty("eyeSizePercent").GetDouble();
    private static void ObserveFault(Task task) => _ = task.ContinueWith(completed => { _ = completed.Exception; },
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private static void AssertPatch(AccountSyncB1bPatch patch, LocalPetConfigurationSnapshot saved)
    {
        Assert.IsTrue(AppearanceContract.Equivalent(patch.Configuration, saved.NormalizedConfiguration));
        Assert.AreEqual(AccountSyncB1bEvidenceContract.ConfigurationSha256(saved.NormalizedConfiguration), patch.ConfigurationSha256);
    }

    private static void Converge(Device primary, Device secondary, PetConfigurationSnapshot cloud)
    {
        Complete(Task.WhenAll(primary.Pump(), secondary.Pump()));
        foreach (var device in new[] { primary, secondary })
        {
            Assert.AreEqual(PetLiveSyncStatus.Synchronized, device.Sync.State.Status);
            Assert.AreEqual(cloud.ConfigurationVersion, device.Sync.State.HighestCloudVersion);
            Assert.IsFalse(device.Sync.State.HasPendingLocalSave);
            Assert.AreEqual(Size(cloud), device.DiskSize);
            Assert.IsTrue(AppearanceContract.Equivalent(cloud.Configuration, device.Capture().NormalizedConfiguration));
        }
    }

    private static Task<string> ReadIdentityAndRoomFactsAsync(AccountSyncB1bHttpsFixture fixture) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>(); var identity = fixture.Identity!;
        var pet = await db.Pets.AsNoTracking().SingleAsync(row => row.Id == identity.PetId && row.UserId == identity.UserId);
        var rooms = await db.Rooms.AsNoTracking().Where(row => row.OwnerId == identity.UserId).OrderBy(row => row.Id).ToListAsync();
        var memberships = await db.RoomMemberships.AsNoTracking().Where(row => row.UserId == identity.UserId).OrderBy(row => row.RoomId).ToListAsync();
        var state = await db.PetRoomStates.AsNoTracking().SingleAsync(row => row.PetId == pet.Id);
        Assert.AreEqual(1, rooms.Count); Assert.AreEqual(fixture.DefaultRoomId, rooms.Single().Id);
        return JsonSerializer.Serialize(new { PetId = pet.Id, pet.UserId, pet.CreatedAt, Rooms = rooms, Memberships = memberships, State = state });
    });

    private static void VerifyArchives(AccountSyncB1bTestOwner owner, AccountIdentity identity, Uri origin,
        Device[] devices, List<ArchiveExpectation> expectations)
    {
        Assert.AreEqual(12, expectations.Count);
        var evidence = new List<ArchiveEvidence>();
        foreach (var device in devices)
        {
            var remaining = expectations.Where(expected => expected.Device == device.Name).ToList();
            var originals = Directory.GetFiles(device.JournalDirectory, "*.originals.json");
            Assert.AreEqual(remaining.Count, originals.Length);
            Assert.AreEqual(originals.Length, Directory.GetFiles(device.JournalDirectory, "*.receipt.json").Length);
            Assert.AreEqual(originals.Length * 2, Directory.GetFiles(device.JournalDirectory).Length);
            foreach (var path in originals)
            {
                var id = Guid.ParseExact(Path.GetFileName(path).Split('.')[0], "N");
                var saved = Complete(device.Journal.LoadOriginalsAsync(id, CancellationToken.None));
                var receipt = Complete(device.Journal.LoadReceiptAsync(id)); Assert.IsNotNull(receipt);
                var expected = remaining.Single(candidate => candidate.Cloud.ConfigurationVersion == saved.Cloud.ConfigurationVersion
                    && candidate.Choice == saved.Choice);
                var failure = expected.Outcome == PetChoiceOutcome.Conflict ? "pet_configuration_conflict" : null;
                Assert.AreEqual(origin, saved.Origin); Assert.IsTrue(expected.Local.SameOriginal(saved.Local));
                Assert.AreEqual(identity.UserId, saved.Cloud.UserId); Assert.AreEqual(identity.PetId, saved.Cloud.PetId);
                Assert.AreEqual(expected.Cloud.ConfigurationVersion, saved.Cloud.ConfigurationVersion);
                Assert.IsTrue(AppearanceContract.Equivalent(expected.Cloud.Configuration, saved.Cloud.Configuration));
                Assert.AreEqual(expected.Choice, saved.Choice); Assert.AreEqual(expected.Choice, receipt.Choice);
                var observedVersion = receipt.ObservedCloudVersion
                    ?? throw new AssertFailedException("A completed B1b archive receipt requires its observed cloud version.");
                Assert.AreEqual(expected.Outcome, receipt.Outcome); Assert.AreEqual(expected.ObservedVersion, observedVersion);
                Assert.AreEqual(failure, receipt.FailureCode); Assert.IsTrue(receipt.RecordedAt >= saved.CapturedAt);
                remaining.Remove(expected);
                evidence.Add(new(device.Name, expected.Step, id,
                    Convert.ToHexString(SHA256.HashData(saved.Local.OriginalBytes.Span)), saved.Cloud.ConfigurationVersion,
                    receipt.Choice, receipt.Outcome, observedVersion, receipt.FailureCode,
                    AccountSyncB1bEvidenceContract.ConfigurationSha256(saved.Local.NormalizedConfiguration),
                    AccountSyncB1bEvidenceContract.ConfigurationSha256(saved.Cloud.Configuration)));
            }
            Assert.AreEqual(0, remaining.Count);
        }
        var canonical = AccountSyncB1bEvidenceContract.CanonicalArchives(evidence, item => item.Step, item => item.Device);
        owner.WriteEvidence("configuration-sync-evidence.json", new { Count = canonical.Count, ExactOriginalsAndReceipts = true,
            UserId = identity.UserId, PetId = identity.PetId, Archives = canonical });
    }

    private static T Complete<T>(Task<T> task, int seconds = 45) { Wait(() => task.IsCompleted, seconds); return task.GetAwaiter().GetResult(); }
    private static void Complete(Task task, int seconds = 45) { Wait(() => task.IsCompleted, seconds); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> condition, int seconds)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed >= TimeSpan.FromSeconds(seconds)) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "The bounded B1b1 task or dispatcher operation did not settle.");
    }

    private sealed class Device : IDisposable
    {
        private readonly AppearanceSettingsSession appearance;
        private readonly LocalPetConfigurationRepository local;
        private readonly List<Task> pumps = [];
        private PetConfigurationLiveSyncCoordinator? sync;
        private Task? drain, dispatcherBarrier;
        internal Device(AccountSyncB1bTestOwner owner, string name)
        {
            Name = name;
            var directory = Path.Combine(owner.RunDirectory, "devices", name);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("accountsync_b1b_device_restart_rejected");
            Directory.CreateDirectory(directory);
            AppearancePath = Path.Combine(directory, "appearance.json");
            JournalDirectory = Path.Combine(directory, "configuration-sync");
            appearance = new(AppearancePath);
            Stage(AccountSyncB1bAppearanceSamples.Baseline); Assert.IsTrue(appearance.TrySave());
            local = new(AppearancePath, appearance, Dispatcher.CurrentDispatcher);
            Journal = new(JournalDirectory);
            appearance.SavedToDisk += OnSaved;
        }
        internal string Name { get; }
        internal string AppearancePath { get; }
        internal string JournalDirectory { get; }
        internal PetConfigurationFileJournal Journal { get; }
        internal PetConfigurationLiveSyncCoordinator Sync => sync ?? throw new InvalidOperationException("accountsync_b1b_sync_required");
        internal bool DispatcherSettled => dispatcherBarrier?.IsCompletedSuccessfully == true;
        internal double DiskSize => new AppearanceSettingsStore().LoadOrDefault(AppearancePath).Settings.EyeLayout.SizePercent;
        internal LocalPetConfigurationSnapshot Capture() => Complete(local.CaptureAsync(CancellationToken.None));
        internal void Attach(IRemotePetConfigurationClient remote, PetConfigurationSnapshot baseline)
        {
            Assert.IsNull(sync); Assert.IsTrue(AppearanceContract.Equivalent(Capture().NormalizedConfiguration, baseline.Configuration));
            sync = new(remote, local, Journal, baseline);
        }
        internal LocalPetConfigurationSnapshot Save(double size)
        {
            Stage(size); Assert.IsTrue(appearance.TrySave()); return Capture();
        }
        private void Stage(double size)
        {
            var eyes = appearance.SavedAppearance.EyeLayout;
            appearance.StageEyes(new(size, eyes.OvalPercent, eyes.HeightControlPercent, eyes.SpacingControlPercent));
        }
        private void OnSaved() => sync?.NotifyLocalSaved();
        internal Task<PetLiveSyncState> Pump()
        {
            var task = Sync.PumpAsync(); pumps.Add(task); ObserveFault(task); return task;
        }
        internal void Stop() => sync?.Stop();
        internal void StopAndDrain()
        {
            Stop(); Complete(DrainAsync(), 90);
        }
        internal Task DrainAsync() => drain ??= DrainCoreAsync();
        private async Task DrainCoreAsync()
        {
            var report = await AccountSyncB1bCleanup.RunAsync(
                ("Pumps", () => Task.WhenAll(pumps).WaitAsync(TimeSpan.FromSeconds(20))),
                ("DispatcherBarrier", () =>
                {
                    dispatcherBarrier ??= Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
                    ObserveFault(dispatcherBarrier);
                    return dispatcherBarrier.WaitAsync(TimeSpan.FromSeconds(20));
                }));
            report.RequireSuccess();
            if (!DispatcherSettled || pumps.Any(task => !task.IsCompletedSuccessfully))
                throw new InvalidOperationException("accountsync_b1b_device_not_settled");
        }
        private static void ObserveFault(Task task) => _ = task.ContinueWith(completed => { _ = completed.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        public void Dispose() { appearance.SavedToDisk -= OnSaved; sync?.Dispose(); }
    }

    private sealed class MemorySecrets : IAccountSecretStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(values.TryGetValue(slot, out var bytes) ? bytes.ToArray() : null); }
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Delete(slot); values[slot] = value.ToArray(); return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Delete(slot); return Task.CompletedTask; }
        private void Delete(string slot) { if (values.Remove(slot, out var bytes)) CryptographicOperations.ZeroMemory(bytes); }
        public void Dispose() { foreach (var bytes in values.Values) CryptographicOperations.ZeroMemory(bytes); values.Clear(); }
    }
}
