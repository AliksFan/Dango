using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class PetConfigurationChoiceTests
{
    internal const string AppearanceJson = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#FFFFFF\",\"eyeColorHex\":\"#123abc\",\"eyeSizePercent\":100,\"eyeOvalityPercent\":0,\"eyeHeightControlPercent\":51,\"eyeSpacingControlPercent\":24.5}";

    [TestMethod]
    public async Task BeginOnlyReadsBothConfigurationsAndSnapshotsCloneCallerMemory()
    {
        var raw = Encoding.UTF8.GetBytes("fictional legacy original bytes");
        using var document = JsonDocument.Parse(AppearanceJson);
        var localSnapshot = new LocalPetConfigurationSnapshot(true, raw, document.RootElement);
        raw[0] = 0;
        var events = new List<string>();
        var local = new LocalRepository(localSnapshot, events);
        var remote = new RemoteClient(Snapshot(Configuration("#000000")), events);
        var journal = new Journal(events);
        var coordinator = new PetConfigurationChoiceCoordinator(remote, local, journal);
        var review = await coordinator.BeginAsync();
        CollectionAssert.AreEqual(new[] { "capture", "get" }, events);
        Assert.AreEqual(0, journal.Archives.Count);
        Assert.AreEqual(0, remote.ExpectedVersions.Count);
        Assert.AreEqual((byte)'f', review.Originals.Local.OriginalBytes.Span[0]);
        Assert.IsTrue(AppearanceContract.Equivalent(Configuration(), review.Originals.Local.NormalizedConfiguration));
        var copied = review.Originals.Local.OriginalBytes.ToArray();
        copied[0] = 0;
        Assert.AreEqual((byte)'f', review.Originals.Local.OriginalBytes.Span[0]);
    }

    [TestMethod]
    [DataRow(PetConfigurationChoice.Local)]
    [DataRow(PetConfigurationChoice.Cloud)]
    public async Task ArchiveFailureBlocksEveryMutation(PetConfigurationChoice choice)
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Journal.FailArchive = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => setup.Coordinator.ChooseAsync(review, choice));
        Assert.AreEqual(0, setup.Remote.ExpectedVersions.Count);
        Assert.AreEqual(0, setup.Local.Applies);
    }

    [TestMethod]
    public async Task CloudChoiceArchivesBothOriginalsBeforeByteCasApplication()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Cloud);
        Assert.AreEqual(PetChoiceOutcome.LocalApplied, result.Outcome);
        CollectionAssert.AreEqual(new[] { "capture", "get", "capture", "archive", "get", "apply", "receipt" }, setup.Events);
        Assert.AreEqual(0, setup.Remote.ExpectedVersions.Count);
        Assert.AreEqual(1, setup.Local.Applies);
        Assert.IsTrue(AppearanceContract.Equivalent(setup.Remote.Current.Configuration, setup.Local.Current.NormalizedConfiguration));
        Assert.IsTrue(review.Originals.Local.SameOriginal(setup.Journal.Archives.Single()));
    }

    [TestMethod]
    public async Task LocalExternalEditBeforeOrDuringArchiveBlocksStaleUpload()
    {
        foreach (var duringArchive in new[] { false, true })
        {
            var setup = Setup();
            var review = await setup.Coordinator.BeginAsync();
            void Change() => setup.Local.Current = Local(Configuration("#222222"), "edited original");
            if (duringArchive) setup.Journal.OnArchive = Change;
            else Change();
            if (duringArchive)
            {
                var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
                Assert.AreEqual(PetChoiceOutcome.Failed, result.Outcome);
                Assert.AreEqual("local_configuration_conflict", result.FailureCode);
                Assert.AreEqual(1, setup.Journal.Archives.Count);
            }
            else
            {
                var error = await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local));
                Assert.AreEqual("local_configuration_conflict", error.Code);
                Assert.AreEqual(0, setup.Journal.Archives.Count);
            }
            Assert.AreEqual(0, setup.Remote.ExpectedVersions.Count);
        }
    }

    [TestMethod]
    public async Task LocalChangesDuringCloudReadFailTheFinalByteCas()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.BeforeRead = () => setup.Local.Current = Local(Configuration("#222222"), "external file edit");
        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Cloud);
        Assert.AreEqual(PetChoiceOutcome.Failed, result.Outcome);
        Assert.AreEqual("local_configuration_conflict", result.FailureCode);
        Assert.AreEqual(0, setup.Local.Applies);
        Assert.IsTrue(review.Originals.Local.SameOriginal(setup.Journal.Archives.Single()));
    }

    [TestMethod]
    public async Task UnsavedDraftGuardPreventsArchiveAndMutation()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Local.Dirty = true;
        var error = await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local));
        Assert.AreEqual("local_unsaved_changes", error.Code);
        Assert.AreEqual(0, setup.Journal.Archives.Count);
        Assert.AreEqual(0, setup.Remote.ExpectedVersions.Count);
    }

    [TestMethod]
    public async Task AccountOrPetChangeNeverMutatesEitherConfiguration()
    {
        foreach (var changePet in new[] { false, true })
        {
            var setup = Setup();
            var review = await setup.Coordinator.BeginAsync();
            setup.Remote.Current = changePet ? setup.Remote.Current with { PetId = Guid.NewGuid() } : setup.Remote.Current with { UserId = Guid.NewGuid() };
            var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
            Assert.AreEqual(PetChoiceOutcome.Failed, result.Outcome);
            Assert.AreEqual("pet_binding_changed", result.FailureCode);
            Assert.AreEqual(0, setup.Remote.ExpectedVersions.Count);
            Assert.AreEqual(0, setup.Local.Applies);
        }
    }

    [TestMethod]
    public async Task LostResponseRetriesOriginalVersionAndMatchingConflictMeansOnlyTargetSatisfied()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Update = (version, configuration) =>
        {
            if (setup.Remote.ExpectedVersions.Count == 1)
            {
                setup.Remote.Current = setup.Remote.Current with { ConfigurationVersion = 2, Configuration = configuration };
                throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
            }
            throw new PetConfigurationHttpException(PetHttpFailure.Conflict, "pet_configuration_conflict");
        };
        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
        Assert.AreEqual(PetChoiceOutcome.TargetSatisfied, result.Outcome);
        Assert.AreNotEqual(PetChoiceOutcome.CloudUpdated, result.Outcome);
        CollectionAssert.AreEqual(new long[] { 1, 1 }, setup.Remote.ExpectedVersions);
        Assert.AreEqual(PetChoiceOutcome.TargetSatisfied, setup.Journal.Receipt!.Outcome);
    }

    [TestMethod]
    public async Task LostResponseThenDifferentCloudStatePreservesConflictWithoutVersionUpgrade()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Update = (_, _) =>
        {
            if (setup.Remote.ExpectedVersions.Count == 1)
            {
                setup.Remote.Current = setup.Remote.Current with { ConfigurationVersion = 2, Configuration = Configuration("#333333") };
                throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
            }
            throw new PetConfigurationHttpException(PetHttpFailure.Conflict, "pet_configuration_conflict");
        };
        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
        Assert.AreEqual(PetChoiceOutcome.Conflict, result.Outcome);
        CollectionAssert.AreEqual(new long[] { 1, 1 }, setup.Remote.ExpectedVersions);
        Assert.IsTrue(review.Originals.Local.SameOriginal(setup.Journal.Archives.Single()));
        Assert.AreEqual("#333333", setup.Remote.Current.Configuration.GetProperty("bodyColorHex").GetString());
    }

    [TestMethod]
    [DataRow("local_configuration_changed")]
    [DataRow("local_configuration_unavailable")]
    [DataRow("local_configuration_invalid")]
    [DataRow("local_unsaved_changes")]
    [DataRow("raw_conflict")]
    [DataRow("io")]
    [DataRow("denied")]
    public async Task LostCommittedResponseThenLocalFailureKeepsUnknownAndStopsFurtherUpdates(string failure)
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Update = (_, configuration) =>
        {
            setup.Remote.Current = setup.Remote.Current with { ConfigurationVersion = 2, Configuration = configuration };
            if (failure == "raw_conflict")
                setup.Local.Current = Local(configuration, "externally changed raw bytes");
            else
                setup.Local.CaptureFailure = failure switch
                {
                    "io" => new IOException("Controlled local failure."),
                    "denied" => new UnauthorizedAccessException("Controlled local failure."),
                    _ => new PetConfigurationChoiceException(failure)
                };
            throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
        };

        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);

        Assert.AreEqual(2L, setup.Remote.Current.ConfigurationVersion, "The first request committed before its response was lost.");
        Assert.AreEqual(PetChoiceOutcome.OutcomeUnknown, result.Outcome);
        Assert.AreEqual("cloud_outcome_unknown", result.FailureCode);
        Assert.IsNull(result.ObservedCloud);
        Assert.AreEqual(1, setup.Remote.ExpectedVersions.Count, "A changed or unreadable local original must block another PATCH.");
        Assert.AreEqual(0, setup.Local.Applies);
        Assert.IsTrue(review.Originals.Local.SameOriginal(setup.Journal.Archives.Single()));
        Assert.IsTrue(result.ReceiptSaved);
        Assert.AreEqual(PetChoiceOutcome.OutcomeUnknown, setup.Journal.Receipt!.Outcome);
        Assert.AreEqual("cloud_outcome_unknown", setup.Journal.Receipt.FailureCode);
    }

    [TestMethod]
    [DataRow(PetHttpFailure.Authentication, "authentication_required")]
    [DataRow(PetHttpFailure.Rejected, "remote_request_rejected")]
    public async Task DefiniteRejectionOnlyMeansFailedBeforeAnyUncertainUpdate(PetHttpFailure failure, string code)
    {
        foreach (var responseLost in new[] { false, true })
        {
            var setup = Setup();
            var review = await setup.Coordinator.BeginAsync();
            setup.Remote.Update = (_, configuration) =>
            {
                if (responseLost && setup.Remote.ExpectedVersions.Count == 1)
                {
                    setup.Remote.Current = setup.Remote.Current with { ConfigurationVersion = 2, Configuration = configuration };
                    throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
                }
                throw new PetConfigurationHttpException(failure, code);
            };

            var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);

            var expectedOutcome = responseLost ? PetChoiceOutcome.OutcomeUnknown : PetChoiceOutcome.Failed;
            var expectedCode = responseLost ? "cloud_outcome_unknown" : code;
            Assert.AreEqual(expectedOutcome, result.Outcome);
            Assert.AreEqual(expectedCode, result.FailureCode);
            Assert.AreEqual(expectedOutcome, setup.Journal.Receipt!.Outcome);
            Assert.AreEqual(expectedCode, setup.Journal.Receipt.FailureCode);
            Assert.AreEqual(responseLost ? 2 : 1, setup.Remote.ExpectedVersions.Count);
            Assert.AreEqual(responseLost ? 2L : 1L, setup.Remote.Current.ConfigurationVersion);
        }
    }

    [TestMethod]
    [DataRow(PetHttpFailure.Unavailable, "cloud_outcome_unknown")]
    [DataRow(PetHttpFailure.Authentication, "authentication_required")]
    [DataRow(PetHttpFailure.InvalidResponse, "remote_response_invalid")]
    public async Task LostCommittedResponseThenConflictWithUnreadableCloudKeepsUnknown(PetHttpFailure failure, string code)
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Update = (_, configuration) =>
        {
            if (setup.Remote.ExpectedVersions.Count == 1)
            {
                setup.Remote.Current = setup.Remote.Current with { ConfigurationVersion = 2, Configuration = configuration };
                throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
            }
            setup.Remote.BeforeRead = () => throw new PetConfigurationHttpException(failure, code);
            throw new PetConfigurationHttpException(PetHttpFailure.Conflict, "pet_configuration_conflict");
        };

        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);

        Assert.AreEqual(PetChoiceOutcome.OutcomeUnknown, result.Outcome);
        Assert.AreEqual("cloud_outcome_unknown", result.FailureCode);
        Assert.AreEqual(PetChoiceOutcome.OutcomeUnknown, setup.Journal.Receipt!.Outcome);
        Assert.IsNull(setup.Journal.Receipt.ObservedCloudVersion);
        CollectionAssert.AreEqual(new long[] { 1, 1 }, setup.Remote.ExpectedVersions);
    }

    [TestMethod]
    public async Task FailedResponsesHaveBoundedRetriesAndRecreatedCoordinatorDoesNotAutoPatch()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Update = (_, _) => throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown");
        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
        Assert.AreEqual(PetChoiceOutcome.OutcomeUnknown, result.Outcome);
        Assert.AreEqual(2, setup.Remote.ExpectedVersions.Count);
        var restarted = new PetConfigurationChoiceCoordinator(setup.Remote, setup.Local, setup.Journal);
        await restarted.BeginAsync();
        Assert.AreEqual(2, setup.Remote.ExpectedVersions.Count);
    }

    [TestMethod]
    public async Task InvalidSuccessfulUpdateResponseKeepsOutcomeUnknownAndOriginals()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Update = (_, _) => setup.Remote.Current with { ConfigurationVersion = 2, Configuration = Configuration("#333333") };
        var result = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
        Assert.AreEqual(PetChoiceOutcome.OutcomeUnknown, result.Outcome);
        Assert.AreEqual("remote_response_invalid", result.FailureCode);
        Assert.AreEqual(1, setup.Remote.ExpectedVersions.Count);
        Assert.IsTrue(review.Originals.Local.SameOriginal(setup.Journal.Archives.Single()));
    }

    [TestMethod]
    public async Task OtherDeviceChangeConflictsAndReceiptFailureDoesNotClaimMutationFailed()
    {
        var setup = Setup();
        var review = await setup.Coordinator.BeginAsync();
        setup.Remote.Current = setup.Remote.Current with { ConfigurationVersion = 2, Configuration = Configuration("#333333") };
        var conflict = await setup.Coordinator.ChooseAsync(review, PetConfigurationChoice.Cloud);
        Assert.AreEqual(PetChoiceOutcome.Conflict, conflict.Outcome);
        Assert.AreEqual(0, setup.Local.Applies);
        var fresh = Setup();
        var freshReview = await fresh.Coordinator.BeginAsync();
        fresh.Journal.FailReceipt = true;
        var applied = await fresh.Coordinator.ChooseAsync(freshReview, PetConfigurationChoice.Local);
        Assert.AreEqual(PetChoiceOutcome.CloudUpdated, applied.Outcome);
        Assert.IsFalse(applied.ReceiptSaved);
        Assert.AreEqual(1, fresh.Journal.Archives.Count);
    }

    internal static JsonElement Configuration(string body = "#FFFFFF")
    {
        using var document = JsonDocument.Parse(AppearanceJson.Replace("#FFFFFF", body, StringComparison.Ordinal));
        return document.RootElement.Clone();
    }
    internal static LocalPetConfigurationSnapshot Local(JsonElement configuration, string raw = "fictional local original") => new(true, Encoding.UTF8.GetBytes(raw), configuration);
    private static PetConfigurationSnapshot Snapshot(JsonElement configuration) => new(Guid.NewGuid(), Guid.NewGuid(), 1, configuration);

    private static TestSetup Setup()
    {
        var events = new List<string>();
        var local = new LocalRepository(Local(Configuration()), events);
        var remote = new RemoteClient(Snapshot(Configuration("#000000")), events);
        var journal = new Journal(events);
        return new(events, local, remote, journal, new(remote, local, journal));
    }

    private sealed record TestSetup(List<string> Events, LocalRepository Local, RemoteClient Remote, Journal Journal, PetConfigurationChoiceCoordinator Coordinator);
    private sealed class LocalRepository(LocalPetConfigurationSnapshot current, List<string> events) : ILocalPetConfigurationRepository
    {
        public LocalPetConfigurationSnapshot Current { get; set; } = current;
        public bool Dirty { get; set; }
        public Exception? CaptureFailure { get; set; }
        public int Applies { get; private set; }
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct)
        {
            events.Add("capture");
            if (Dirty) throw new PetConfigurationChoiceException("local_unsaved_changes");
            if (CaptureFailure is not null) throw CaptureFailure;
            return Task.FromResult(Current);
        }
        public Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct)
        {
            events.Add("apply");
            if (!expected.SameOriginal(Current)) throw new PetConfigurationChoiceException("local_configuration_conflict");
            Applies++;
            Current = Local(configuration, "applied configuration");
            return Task.CompletedTask;
        }
    }
    private sealed class RemoteClient(PetConfigurationSnapshot current, List<string> events) : IRemotePetConfigurationClient
    {
        public Uri Origin { get; } = new("https://formal.example.invalid/");
        public Guid UserId { get; } = current.UserId;
        public Guid PetId { get; } = current.PetId;
        public PetConfigurationSnapshot Current { get; set; } = current;
        public Action? BeforeRead { get; set; }
        public Func<long, JsonElement, PetConfigurationSnapshot>? Update { get; set; }
        public List<long> ExpectedVersions { get; } = [];
        public Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) { events.Add("get"); BeforeRead?.Invoke(); return Task.FromResult(Current); }
        public Task<PetConfigurationSnapshot> UpdateAsync(long version, JsonElement configuration, CancellationToken ct)
        {
            events.Add("patch");
            ExpectedVersions.Add(version);
            Current = Update?.Invoke(version, configuration) ?? Current with { ConfigurationVersion = version + 1, Configuration = configuration };
            return Task.FromResult(Current);
        }
    }
    private sealed class Journal(List<string> events) : IPetConfigurationJournal
    {
        public bool FailArchive { get; set; }
        public bool FailReceipt { get; set; }
        public Action? OnArchive { get; set; }
        public List<LocalPetConfigurationSnapshot> Archives { get; } = [];
        public PetConfigurationReceipt? Receipt { get; private set; }
        public Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct)
        {
            events.Add("archive");
            if (FailArchive) throw new IOException("Controlled archive failure.");
            Archives.Add(originals.Local);
            OnArchive?.Invoke();
            return Task.FromResult(Guid.NewGuid());
        }
        public Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct)
        {
            events.Add("receipt");
            if (FailReceipt) throw new IOException("Controlled receipt failure.");
            Receipt = receipt;
            return Task.CompletedTask;
        }
    }
}
