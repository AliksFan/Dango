using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;

namespace Dango.Client.Sync.Tests.Pets;

[TestClass]
public sealed class PetConfigurationLiveSyncTests
{
    private static readonly JsonElement White = Configuration("#FFFFFF"), Cream = Configuration("#FFE8C2"), Blue = Configuration("#2244AA");

    [TestMethod]
    public async Task EqualPollsHaveNoWritesArchivesOrEcho()
    {
        var f = new Fixture();
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
        Assert.AreEqual(0, f.Remote.Writes.Count); Assert.AreEqual(0, f.Journal.Archives.Count);
    }

    [TestMethod]
    public async Task ConsecutiveCommittedSavesCoalesceToNewest()
    {
        var f = new Fixture(); f.Save(Cream); f.Save(Blue);
        await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
    }

    [TestMethod]
    public async Task NewSaveDuringOlderWriteIsNotOverwrittenAndFollowsItsVersion()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.BeforeWrite = (_, _) => { f.Remote.BeforeWrite = null; f.Save(Blue); return Task.CompletedTask; };
        await f.Sync.PumpAsync();
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        Assert.IsTrue(f.Sync.State.HasPendingLocalSave);
        await f.Sync.PumpAsync();
        CollectionAssert.AreEqual(new long[] { 1, 2 }, f.Remote.Writes.Select(w => w.Version).ToArray());
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
    }

    [TestMethod]
    public async Task ConflictAdoptsLatestServerWithoutRebasingOldEdit()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.BeforeWrite = (_, _) => { f.Remote.BeforeWrite = null; f.Remote.Commit(Blue); return Task.CompletedTask; };
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        Assert.AreEqual(2L, f.Sync.State.HighestCloudVersion);
    }

    [TestMethod]
    public async Task LostCommittedResponseThenAnotherDeviceWinsNeverReplaysOldEdit()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.AfterWrite = () => { f.Remote.AfterWrite = null; f.Remote.Commit(Blue); throw Unavailable(); };
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
    }

    [TestMethod]
    public async Task UnknownUncommittedWriteOnlyRetriesItsOriginalVersion()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.BeforeWrite = (_, _) => { f.Remote.BeforeWrite = null; throw Unavailable(); };
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Offline, f.Sync.State.Status);
        await f.Sync.PumpAsync();
        CollectionAssert.AreEqual(new long[] { 1, 1 }, f.Remote.Writes.Select(w => w.Version).ToArray());
        Assert.AreEqual(2L, f.Remote.Current.ConfigurationVersion);
    }

    [TestMethod]
    public async Task UnknownOlderWriteSettlesBeforeNewerSaveUsesNextVersion()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.BeforeWrite = (_, _) => { f.Remote.BeforeWrite = null; throw Unavailable(); };
        await f.Sync.PumpAsync(); f.Save(Blue);
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        CollectionAssert.AreEqual(new long[] { 1, 1, 2 }, f.Remote.Writes.Select(w => w.Version).ToArray());
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
    }

    [TestMethod]
    public async Task RemoteApplyPreservesDraftAndDoesNotProduceAnUploadEcho()
    {
        var f = new Fixture(); f.Local.Draft = true; f.Remote.Commit(Cream);
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.PausedForDraft, f.Sync.State.Status);
        Assert.IsTrue(AppearanceContract.Equivalent(White, f.Local.Current));
        f.Local.Draft = false; f.Local.Applied = f.Sync.NotifyLocalSaved;
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Local.Current));
        Assert.AreEqual(0, f.Remote.Writes.Count);
    }

    [TestMethod]
    public async Task DelayedEchoWithEquivalentConfigurationDoesNotWrite()
    {
        var f = new Fixture(); f.Remote.Commit(Cream); await f.Sync.PumpAsync();
        f.Sync.NotifyLocalSaved(); await f.Sync.PumpAsync();
        Assert.AreEqual(0, f.Remote.Writes.Count);
    }

    [TestMethod]
    public async Task ServerVersionRegressionOrSameVersionDifferentContentsCannotOverwriteLocal()
    {
        var f = new Fixture(); f.Remote.Commit(Cream); await f.Sync.PumpAsync();
        f.Remote.ReadOverride = () => f.Remote.Snapshot(1, White);
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.AreEqual(2L, f.Sync.State.HighestCloudVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Local.Current));
        f.Remote.ReadOverride = () => f.Remote.Snapshot(2, Blue); await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Local.Current));
    }

    [TestMethod]
    public async Task StopFencesLateReadThatIgnoresCancellationAndDropsPendingSaves()
    {
        var f = new Fixture(); var read = new TaskCompletionSource<PetConfigurationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Remote.DelayedRead = read.Task;
        var pump = f.Sync.PumpAsync(); f.Sync.Stop();
        read.SetResult(f.Remote.Snapshot(2, Cream)); await pump;
        f.Save(Blue); await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Stopped, f.Sync.State.Status);
        Assert.AreEqual(0, f.Local.ApplyCount); Assert.AreEqual(0, f.Remote.Writes.Count);
        Assert.IsFalse(f.Sync.State.HasPendingLocalSave);
    }

    [TestMethod]
    public async Task StopFencesLateWriteAndCannotReplayItInAnotherSession()
    {
        var f = new Fixture(); f.Save(Cream);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Remote.BeforeWrite = async (_, _) => { entered.SetResult(); await finish.Task; };
        var pump = f.Sync.PumpAsync(); await entered.Task; f.Sync.Stop(); finish.SetResult(); await pump;
        Assert.AreEqual(PetLiveSyncStatus.Stopped, f.Sync.State.Status);
        Assert.AreEqual(1L, f.Sync.State.HighestCloudVersion);
        await f.Sync.PumpAsync(); Assert.AreEqual(1, f.Remote.Writes.Count);
    }

    [TestMethod]
    public async Task ConcurrentPumpsDoNotQueueMultipleRequestsOrWrites()
    {
        var f = new Fixture(); var read = new TaskCompletionSource<PetConfigurationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Remote.DelayedRead = read.Task; f.Save(Cream);
        var first = f.Sync.PumpAsync(); await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Remote.Reads);
        f.Remote.DelayedRead = null; read.SetResult(f.Remote.Current); await first;
        Assert.AreEqual(1, f.Remote.Writes.Count);
    }

    [TestMethod]
    public async Task ArchiveFailurePreventsBothUploadAndCloudApplication()
    {
        var f = new Fixture(); f.Save(Cream); f.Journal.FailArchive = true;
        await f.Sync.PumpAsync(); Assert.AreEqual(0, f.Remote.Writes.Count);
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        var other = new Fixture(); other.Remote.Commit(Cream); other.Journal.FailArchive = true;
        await other.Sync.PumpAsync(); Assert.AreEqual(0, other.Local.ApplyCount);
        Assert.IsTrue(AppearanceContract.Equivalent(White, other.Local.Current));
    }

    [TestMethod]
    public async Task OfflineUnsentSaveIsKeptAndFirstSentUsingFreshServerVersion()
    {
        var f = new Fixture(); f.Save(Cream); f.Remote.ReadOverride = () => throw Unavailable();
        await f.Sync.PumpAsync(); Assert.IsTrue(f.Sync.State.HasPendingLocalSave);
        f.Remote.ReadOverride = null; f.Remote.Commit(Blue); await f.Sync.PumpAsync();
        Assert.AreEqual(2L, f.Remote.Writes.Single().Version);
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Remote.Current.Configuration));
    }

    [TestMethod]
    public async Task AnotherPetsResponseStopsSessionWithoutApplyingOrWriting()
    {
        var f = new Fixture(); f.Remote.ReadOverride = () => f.Remote.Current with { PetId = Guid.NewGuid() };
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Stopped, f.Sync.State.Status);
        Assert.AreEqual("pet_binding_changed", f.Sync.State.FailureCode);
        Assert.AreEqual(0, f.Local.ApplyCount);
    }

    [TestMethod]
    public async Task UnknownWriteHasTwoAttemptLifetimeBudgetAndThenOnlyReads()
    {
        var f = new Fixture(); f.Save(Cream); f.Remote.BeforeWrite = (_, _) => throw Unavailable();
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync(); await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(2, f.Remote.Writes.Count);
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.AreEqual("cloud_outcome_unknown", f.Sync.State.FailureCode);
        Assert.IsTrue(f.Sync.State.HasPendingLocalSave);
        Assert.IsTrue(f.Remote.Reads >= 4);
        f.Remote.Commit(Blue); await f.Sync.PumpAsync();
        Assert.AreEqual(2, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
    }

    [TestMethod]
    public async Task DefiniteWriteRejectionIsNotAutomaticallyResubmitted()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.BeforeWrite = (_, _) => throw new PetConfigurationHttpException(PetHttpFailure.Rejected, "remote_request_rejected");
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(White, f.Local.Current));
    }

    [TestMethod]
    public async Task FailedCompletionReceiptCannotBeReportedAsSynchronizedInSamePump()
    {
        var f = new Fixture(); f.Save(Cream); f.Journal.FailReceipt = true;
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.AreEqual("archive_write_failed", f.Sync.State.FailureCode);
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Remote.Current.Configuration));
        Assert.AreEqual(1, f.Journal.Archives.Count);
    }

    [TestMethod]
    public async Task SaveDuringRemoteApplicationIsKeptInsteadOfSuppressedAsEcho()
    {
        var f = new Fixture(); f.Remote.Commit(Cream);
        f.Local.BeforeApply = () => { f.Local.BeforeApply = null; f.Save(Blue); };
        await f.Sync.PumpAsync();
        Assert.IsTrue(f.Sync.State.HasPendingLocalSave);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        await f.Sync.PumpAsync();
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
    }

    [TestMethod]
    public async Task SaveDuringOriginalArchivingCannotUploadAnObsoleteCapture()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Journal.BeforeArchive = () => { f.Journal.BeforeArchive = null; f.Save(Blue); };
        await f.Sync.PumpAsync(); Assert.AreEqual(0, f.Remote.Writes.Count);
        await f.Sync.PumpAsync(); Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
    }

    [TestMethod]
    public async Task InvalidAcknowledgedVersionKeepsUnknownUntilActualCloudIsRead()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.ResponseOverride = result => result with { ConfigurationVersion = 100 };
        await f.Sync.PumpAsync();
        Assert.AreEqual(1L, f.Sync.State.HighestCloudVersion);
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.IsTrue(f.Sync.State.HasPendingLocalSave);
        await f.Sync.PumpAsync();
        Assert.AreEqual(2L, f.Sync.State.HighestCloudVersion);
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
    }

    [TestMethod]
    public async Task RevokedSessionStopsPollingAndCannotUploadQueuedSave()
    {
        var f = new Fixture(); f.Save(Cream);
        f.Remote.ReadOverride = () => throw new PetConfigurationHttpException(PetHttpFailure.Authentication, "authentication_required");
        await f.Sync.PumpAsync(); var reads = f.Remote.Reads;
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Stopped, f.Sync.State.Status);
        Assert.AreEqual("authentication_required", f.Sync.State.FailureCode);
        Assert.AreEqual(reads, f.Remote.Reads); Assert.AreEqual(0, f.Remote.Writes.Count);
    }

    [TestMethod]
    public async Task TwoIndependentCoordinatorsConvergeToTheLastCommittedServerVersion()
    {
        var first = new Fixture(); var secondLocal = new Local();
        using var second = new PetConfigurationLiveSyncCoordinator(first.Remote, secondLocal, new Journal(), first.Remote.Current);
        first.Save(Cream); secondLocal.Current = Blue; second.NotifyLocalSaved();
        first.Remote.BeforeWrite = async (_, _) => { first.Remote.BeforeWrite = null; await second.PumpAsync(); };
        await first.Sync.PumpAsync(); await second.PumpAsync(); await first.Sync.PumpAsync();
        Assert.AreEqual(2, first.Remote.Writes.Count);
        Assert.AreEqual(2L, first.Remote.Current.ConfigurationVersion, "Only one competing CAS request may commit.");
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, first.Local.Current));
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, secondLocal.Current));
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, first.Sync.State.Status);
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, second.State.Status);
    }

    [TestMethod]
    public async Task NewSaveAfterAnExhaustedUnknownEditBecomesAnIndependentIntent()
    {
        var f = new Fixture(); f.Save(Cream); f.Remote.BeforeWrite = (_, _) => throw Unavailable();
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(2, f.Remote.Writes.Count);
        f.Remote.BeforeWrite = null; f.Save(Blue); await f.Sync.PumpAsync();
        Assert.AreEqual(3, f.Remote.Writes.Count);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Writes[2].Configuration));
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
        Assert.AreEqual(1, f.Journal.Receipts.Count(r => r.Outcome == PetChoiceOutcome.OutcomeUnknown));
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
    }

    [TestMethod]
    public async Task AnotherPollCannotForgetAFailedCompletionReceipt()
    {
        var f = new Fixture(); f.Save(Cream); f.Journal.FailReceipt = true;
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.AreEqual("archive_write_failed", f.Sync.State.FailureCode);
        Assert.AreEqual(1, f.Remote.Writes.Count);
    }

    [TestMethod]
    public async Task CompletingFailedReceiptDoesNotResubmitTheAcknowledgedPatch()
    {
        var f = new Fixture(); f.Save(Cream); f.Journal.FailReceipt = true;
        await f.Sync.PumpAsync(); f.Journal.FailReceipt = false;
        await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Journal.Receipts.Count);
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
    }

    [TestMethod]
    public async Task LateOldUnknownRequestCannotOverwriteAnAcknowledgedNewSave()
    {
        var f = new Fixture(); f.Save(Cream); f.Remote.BeforeWrite = (_, _) => throw Unavailable();
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        f.Remote.BeforeWrite = null; f.Save(Blue); await f.Sync.PumpAsync();
        Assert.AreEqual(PetChoiceOutcome.CloudUpdated, f.Sync.State.LastLocalSaveOutcome);
        Assert.AreEqual(2L, f.Sync.State.LastLocalSaveSerial);
        await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() => f.Remote.UpdateAsync(1, Cream, CancellationToken.None));
        await f.Sync.PumpAsync();
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Remote.Current.Configuration));
        Assert.AreEqual(2L, f.Remote.Current.ConfigurationVersion);
    }

    [TestMethod]
    public async Task OldUnknownWinningBeforeNewSaveIsAnExplicitConflictOutcomeWithBothArchives()
    {
        var f = new Fixture(); f.Save(Cream); f.Remote.BeforeWrite = (_, _) => throw Unavailable();
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync(); f.Save(Blue);
        f.Remote.BeforeWrite = (_, _) => { f.Remote.BeforeWrite = null; f.Remote.Commit(Cream); return Task.CompletedTask; };
        await f.Sync.PumpAsync();
        Assert.AreEqual(PetChoiceOutcome.Conflict, f.Sync.State.LastLocalSaveOutcome);
        Assert.AreEqual(2L, f.Sync.State.LastLocalSaveSerial);
        Assert.AreEqual(1, f.Journal.Receipts.Count(r => r.Outcome == PetChoiceOutcome.OutcomeUnknown));
        Assert.AreEqual(1, f.Journal.Receipts.Count(r => r.Outcome == PetChoiceOutcome.Conflict));
        Assert.IsTrue(f.Journal.Archives.Any(a => AppearanceContract.Equivalent(Blue, a.Local.NormalizedConfiguration)));
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Local.Current));
        Assert.IsTrue(AppearanceContract.Equivalent(Cream, f.Remote.Current.Configuration));
    }

    [TestMethod]
    public async Task ReceiptRetryBudgetIsBoundedAndCannotPermitMoreMutations()
    {
        var f = new Fixture(); f.Save(Cream); f.Journal.FailReceipt = true;
        await f.Sync.PumpAsync(); f.Save(Blue);
        await f.Sync.PumpAsync(); await f.Sync.PumpAsync(); await f.Sync.PumpAsync();
        Assert.AreEqual(2, f.Journal.ReceiptAttempts);
        Assert.AreEqual(1, f.Remote.Writes.Count);
        Assert.AreEqual(PetLiveSyncStatus.Failed, f.Sync.State.Status);
        Assert.IsTrue(f.Sync.State.HasPendingLocalSave);
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
    }

    [TestMethod]
    public async Task CancelledReceiptCanCompleteNextPumpWithoutDuplicatingThePatchOrReceipt()
    {
        var f = new Fixture(); f.Save(Cream); using var cancel = new CancellationTokenSource();
        f.Journal.BeforeReceipt = () => { f.Journal.BeforeReceipt = null; cancel.Cancel(); };
        await f.Sync.PumpAsync(cancel.Token); await f.Sync.PumpAsync();
        Assert.AreEqual(1, f.Remote.Writes.Count); Assert.AreEqual(1, f.Journal.Receipts.Count);
        Assert.AreEqual(PetLiveSyncStatus.Synchronized, f.Sync.State.Status);
    }

    [TestMethod]
    public async Task FailedConflictReceiptBlocksCloudOverwriteUntilTheReceiptIsSaved()
    {
        var f = new Fixture(); f.Save(Cream); f.Journal.FailReceipt = true;
        f.Remote.BeforeWrite = (_, _) => { f.Remote.BeforeWrite = null; f.Remote.Commit(Blue); return Task.CompletedTask; };
        await f.Sync.PumpAsync(); Assert.AreEqual(0, f.Local.ApplyCount);
        f.Journal.FailReceipt = false; await f.Sync.PumpAsync();
        Assert.IsTrue(AppearanceContract.Equivalent(Blue, f.Local.Current));
        Assert.AreEqual(2, f.Journal.Receipts.Count);
        Assert.AreEqual(1, f.Remote.Writes.Count);
    }

    private static PetConfigurationHttpException Unavailable() => new(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
    private static JsonElement Configuration(string color) => JsonSerializer.SerializeToElement(new
    { appearanceSchemaVersion = 3, bodyColorHex = color, eyeColorHex = "#222222", eyeSizePercent = 100, eyeOvalityPercent = 0, eyeHeightControlPercent = 49, eyeSpacingControlPercent = 23 });

    private sealed class Fixture
    {
        public Local Local { get; } = new();
        public Remote Remote { get; } = new();
        public Journal Journal { get; } = new();
        public PetConfigurationLiveSyncCoordinator Sync { get; }
        public Fixture() => Sync = new(Remote, Local, Journal, Remote.Current);
        public void Save(JsonElement configuration) { Local.Current = configuration.Clone(); Sync.NotifyLocalSaved(); }
    }
    private sealed class Local : ILocalPetConfigurationRepository
    {
        public JsonElement Current = White;
        public bool Draft;
        public int ApplyCount;
        public Action? Applied;
        public Action? BeforeApply;
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Draft) throw new PetConfigurationChoiceException("local_unsaved_changes");
            return Task.FromResult(new LocalPetConfigurationSnapshot(true, Encoding.UTF8.GetBytes(Current.GetRawText()), Current));
        }
        public async Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct)
        {
            BeforeApply?.Invoke();
            var current = await CaptureAsync(ct);
            if (!current.SameOriginal(expected)) throw new PetConfigurationChoiceException("local_configuration_conflict");
            Current = configuration.Clone(); ApplyCount++; Applied?.Invoke();
        }
    }
    private sealed class Remote : IRemotePetConfigurationClient
    {
        public Uri Origin { get; } = new("https://sync.dango.invalid/");
        public Guid UserId { get; } = Guid.NewGuid(); public Guid PetId { get; } = Guid.NewGuid();
        public PetConfigurationSnapshot Current { get; private set; }
        public int Reads;
        public List<(long Version, JsonElement Configuration)> Writes { get; } = [];
        public Func<PetConfigurationSnapshot>? ReadOverride;
        public Task<PetConfigurationSnapshot>? DelayedRead;
        public Func<long, JsonElement, Task>? BeforeWrite;
        public Action? AfterWrite;
        public Func<PetConfigurationSnapshot, PetConfigurationSnapshot>? ResponseOverride;
        public Remote() => Current = Snapshot(1, White);
        public PetConfigurationSnapshot Snapshot(long version, JsonElement configuration) => new(UserId, PetId, version, configuration.Clone());
        public void Commit(JsonElement configuration) => Current = Snapshot(Current.ConfigurationVersion + 1, configuration);
        public Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) { Reads++; return DelayedRead ?? Task.FromResult(ReadOverride?.Invoke() ?? Current); }
        public async Task<PetConfigurationSnapshot> UpdateAsync(long expectedVersion, JsonElement configuration, CancellationToken ct)
        {
            Writes.Add((expectedVersion, configuration.Clone()));
            if (BeforeWrite is not null) await BeforeWrite(expectedVersion, configuration);
            if (expectedVersion != Current.ConfigurationVersion) throw new PetConfigurationHttpException(PetHttpFailure.Conflict, "pet_configuration_conflict");
            Commit(configuration); var result = Current; AfterWrite?.Invoke(); return ResponseOverride?.Invoke(result) ?? result;
        }
    }
    private sealed class Journal : IPetConfigurationJournal
    {
        public readonly List<PetConfigurationOriginals> Archives = [];
        public readonly List<PetConfigurationReceipt> Receipts = [];
        public bool FailArchive;
        public bool FailReceipt;
        public Action? BeforeArchive;
        public Action? BeforeReceipt;
        public int ReceiptAttempts;
        public Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); BeforeArchive?.Invoke(); if (FailArchive) throw new IOException(); Archives.Add(originals); return Task.FromResult(Guid.NewGuid()); }
        public Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct) => throw new AssertFailedException("No archived operation is replayed.");
        public Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct)
        { ReceiptAttempts++; BeforeReceipt?.Invoke(); ct.ThrowIfCancellationRequested(); if (FailReceipt) throw new IOException(); Receipts.Add(receipt); return Task.CompletedTask; }
    }
}
