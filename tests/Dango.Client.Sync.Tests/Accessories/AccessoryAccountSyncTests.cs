using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Client.Sync.Accessories;
using Dango.Protocol.Accessories;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests.Accessories;

[TestClass]
public sealed class AccessoryAccountSyncTests
{
    private static AccessoryAccountScope Scope(string account = "fictional-account", long generation = 1) =>
        new(account, "fictional-pet", generation);

    [TestMethod]
    public async Task UploadsRealBytesOnceForTwoInstancesAndMapsEveryParameterBeforeCommit()
    {
        var image = Image(12);
        var serverImage = Image(13);
        var transport = new FakeTransport { MapTo = serverImage };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 6);
        var item = Item(image, "first") with
        {
            Layer = AccessoryOutfitLayer.Behind,
            Order = 4,
            SizePercent = 153,
            BaseAngleDegrees = -21,
            ImagePivotX = .1,
            ImagePivotY = .2,
            BodyAnchorX = .3,
            BodyAnchorY = .4,
            Softness = 54,
            AllowFolding = true,
            MaxDeformationPercent = 129,
            ConnectionStrength = 34,
            MaxConnectionTravelPercent = 145,
            RotationDamping = 23,
            LockRotation = false,
            ReturnStrength = 46,
            MaxSwingDegrees = 165,
            LimitSwing = false,
            GravityInfluence = 88
        };
        sync.QueueLocalCommit(new(new(1, [item, item with { Id = "second", Order = 5 }]), [image], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.BackedUp);
        Assert.AreEqual(1, transport.Uploads.Count);
        CollectionAssert.AreEqual(image.PngBytes.ToArray(), transport.Uploads.Single().Image.PngBytes.ToArray());
        var request = transport.Commits.Single().Request;
        Assert.AreEqual(6L, request.ExpectedRevision);
        Assert.AreEqual(32, request.IdempotencyKey.Length);
        Assert.AreEqual(item with { AssetId = serverImage.AssetId }, request.Outfit.Items[0]);
        Assert.AreEqual("second", request.Outfit.Items[1].Id);
        Assert.AreEqual(serverImage.AssetId, request.Outfit.Items[1].AssetId);
        Assert.AreEqual(7L, sync.SafeDiagnostics.ServerRevision);
        Assert.IsFalse(sync.SafeDiagnostics.HasPendingLocalChanges);
    }

    [TestMethod]
    public async Task EmptyAndBuiltinOnlyOutfitsCommitWithoutUploading()
    {
        var transport = new FakeTransport();
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 1);
        sync.QueueLocalCommit(new(new(1, [new("hat", "Hat", "builtin-top-hat", 64, 64)]), [], 2));
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 2);
        Assert.AreEqual(0, transport.Uploads.Count);
        Assert.AreEqual(2, transport.Commits.Count);
        Assert.AreEqual(1L, transport.Commits.Last().Request.ExpectedRevision);
    }

    [TestMethod]
    public async Task CoalescesNewerCommittedOutfitsBeforeSendingObsoleteCommit()
    {
        var entered = Completion<bool>();
        var release = Completion<UploadedAccessoryAsset>();
        var first = Image(1);
        var last = Image(3);
        var transport = new FakeTransport
        {
            UploadHandler = (_, image, _) =>
            {
                if (image.AssetId != first.AssetId) return Task.FromResult(Mapping(image));
                entered.TrySetResult(true);
                return release.Task;
            }
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 4);
        sync.QueueLocalCommit(Snapshot(first, 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sync.QueueLocalCommit(Snapshot(Image(2), 2));
        sync.QueueLocalCommit(Snapshot(last, 3));
        release.SetResult(Mapping(first));
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 3);
        Assert.AreEqual(1, transport.Commits.Count);
        Assert.AreEqual(last.AssetId, transport.Commits.Single().Request.Outfit.Items.Single().AssetId);
        Assert.IsFalse(sync.QueueLocalCommit(Snapshot(first, 1)));
    }

    [TestMethod]
    public async Task CommitTimeoutKeepsUnknownUntilExplicitReceiptReconciliation()
    {
        var transport = new FakeTransport();
        transport.CommitHandler = (_, request, _) => transport.Commits.Count == 1
            ? throw new TimeoutException("fictional-secret-must-not-leak")
            : Task.FromResult(new StoredAccessoryOutfit(request.ExpectedRevision + 1, request.Outfit));
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 2);
        sync.QueueLocalCommit(Snapshot(Image(1), 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        var attempt = transport.Commits.Single().Request;
        sync.QueueLocalCommit(Snapshot(Image(2), 2));
        Assert.IsFalse(sync.RetryLatestBackup());
        await Task.Delay(40);
        Assert.AreEqual(1, transport.Commits.Count);
        Assert.AreEqual(2L, sync.SafeDiagnostics.ServerRevision);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
        Assert.IsFalse(JsonSerializer.Serialize(sync.SafeDiagnostics).Contains("fictional-secret", StringComparison.Ordinal));
        transport.Resolution = new(AccessoryCommitResolutionKind.Committed, new(3, attempt.Outfit));
        Assert.AreEqual(AccessoryCommitResolutionKind.Committed, await sync.ReconcileUnknownCommitAsync());
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 2);
        Assert.AreEqual(2, transport.Commits.Count);
        Assert.AreEqual(3L, transport.Commits.Last().Request.ExpectedRevision);
        Assert.AreEqual(attempt.IdempotencyKey, transport.ResolvedKeys.Single());
    }

    [TestMethod]
    public async Task MissingReceiptRemainsUnknownAndDefinitiveNotCommittedNeedsExplicitRetry()
    {
        var transport = new FakeTransport
        {
            CommitHandler = (_, _, _) => throw new IOException("lost_response")
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.AreEqual(AccessoryCommitResolutionKind.Unknown, await sync.ReconcileUnknownCommitAsync());
        Assert.AreEqual(AccessorySyncState.OutcomeUnknown, sync.SafeDiagnostics.State);
        transport.Resolution = new(AccessoryCommitResolutionKind.NotCommitted);
        Assert.AreEqual(AccessoryCommitResolutionKind.NotCommitted, await sync.ReconcileUnknownCommitAsync());
        Assert.AreEqual(AccessorySyncState.Failed, sync.SafeDiagnostics.State);
        Assert.AreEqual(1, transport.Commits.Count);
        transport.CommitHandler = null;
        Assert.IsTrue(sync.RetryLatestBackup());
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.BackedUp);
        Assert.AreEqual(2, transport.Commits.Count);
    }

    [TestMethod]
    public async Task RevisionConflictDoesNotOverwriteRemoteOrRetryAutomatically()
    {
        var transport = new FakeTransport
        {
            CommitHandler = (_, _, _) => throw new AccessoryAccountTransportException(AccessoryTransportFailure.RevisionConflict)
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 2);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.RevisionConflict);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 2));
        Assert.IsFalse(sync.RetryLatestBackup());
        await Task.Delay(40);
        Assert.AreEqual(1, transport.Commits.Count);
        Assert.AreEqual(2L, sync.SafeDiagnostics.ServerRevision);
        Assert.AreEqual(AccessoryRestoreResult.Blocked, await sync.RestoreAsync());
    }

    [TestMethod]
    public async Task UploadFailureRetainsLocalSnapshotAndDoesNotClaimAccountSave()
    {
        var repository = new FakeRepository();
        var transport = new FakeTransport { UploadHandler = (_, _, _) => throw new IOException("unsafe_raw_error") };
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope(), transport, 1);
        sync.QueueLocalCommit(Snapshot(Image(4), 7));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.Failed);
        Assert.AreEqual(0, transport.Commits.Count);
        Assert.AreEqual(0, repository.CommitCount);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
        Assert.IsTrue(sync.SafeDiagnostics.HasPendingLocalChanges);
        transport.UploadHandler = null;
        Assert.IsTrue(sync.RetryLatestBackup());
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 7);
    }

    [TestMethod]
    public async Task IncorrectUploadMappingIsRejectedBeforeCommit()
    {
        var other = Image(8);
        var transport = new FakeTransport { UploadHandler = (_, _, _) => Task.FromResult(Mapping(other)) };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 0);
        sync.QueueLocalCommit(Snapshot(Image(7), 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.Failed);
        Assert.AreEqual(0, transport.Commits.Count);
        Assert.AreEqual("invalid_upload_response", sync.SafeDiagnostics.ErrorCode);
    }

    [TestMethod]
    public async Task InvalidCommitReceiptCannotBeReportedAsSuccess()
    {
        var transport = new FakeTransport
        {
            CommitHandler = (_, request, _) => Task.FromResult(new StoredAccessoryOutfit(request.ExpectedRevision, request.Outfit))
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 5);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.AreEqual(5L, sync.SafeDiagnostics.ServerRevision);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
    }

    [TestMethod]
    public async Task SwitchingAccountsFencesLateUploadAndLetsNewSessionProceed()
    {
        var entered = Completion<bool>();
        var release = Completion<UploadedAccessoryAsset>();
        var image = Image(5);
        var oldTransport = new FakeTransport { UploadHandler = (_, _, _) => { entered.SetResult(true); return release.Task; } };
        var newTransport = new FakeTransport { ServiceOrigin = new("https://other-environment.example.test/") };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope("fictional-old", 1), oldTransport, 0);
        sync.QueueLocalCommit(Snapshot(image, 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sync.AttachAccount(Scope("fictional-new", 2), newTransport, 10);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.BackedUp);
        release.SetResult(Mapping(image));
        await Task.Delay(40);
        Assert.AreEqual(0, oldTransport.Commits.Count);
        Assert.AreEqual("fictional-new", newTransport.Commits.Single().Scope.AccountId);
        Assert.AreEqual(11L, sync.SafeDiagnostics.ServerRevision);
        Assert.AreEqual(2L, sync.SafeDiagnostics.AttachmentVersion);
    }

    [TestMethod]
    public async Task CapturedSnapshotCannotCrossAttachmentEvenForSameAccountAndPetInDifferentEnvironment()
    {
        var old = new FakeTransport();
        var next = new FakeTransport { ServiceOrigin = new("https://production-environment.example.test/") };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), old, 0);
        var attachment = sync.SafeDiagnostics.AttachmentVersion;
        var captured = Snapshot(Image(5), 9);
        sync.AttachAccount(Scope(), next, 5);
        Assert.IsFalse(sync.QueueLocalCommit(captured, attachment));
        Assert.AreEqual(0, next.Uploads.Count);
        Assert.IsTrue(sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 10), sync.SafeDiagnostics.AttachmentVersion));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.BackedUp);
        Assert.AreEqual(0, old.Uploads.Count);
        Assert.AreEqual(6L, sync.SafeDiagnostics.ServerRevision);
    }

    [TestMethod]
    public async Task FreshLocalApplyAfterKnownUploadFailureAutomaticallyBacksUpNewestOutfit()
    {
        var transport = new FakeTransport { UploadHandler = (_, _, _) => throw new IOException() };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 0);
        sync.QueueLocalCommit(Snapshot(Image(1), 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.Failed);
        transport.UploadHandler = null;
        var newest = Image(2);
        sync.QueueLocalCommit(Snapshot(newest, 2));
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 2);
        Assert.AreEqual(newest.AssetId, transport.Commits.Single().Request.Outfit.Items.Single().AssetId);
    }

    [TestMethod]
    public async Task NewestQueuedApplyStillRunsWhenAnOlderUploadFails()
    {
        var entered = Completion<bool>();
        var release = Completion<UploadedAccessoryAsset>();
        var first = Image(1);
        var latest = Image(2);
        var transport = new FakeTransport
        {
            UploadHandler = (_, image, _) =>
            {
                if (image.AssetId != first.AssetId) return Task.FromResult(Mapping(image));
                entered.SetResult(true);
                return release.Task;
            }
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 0);
        sync.QueueLocalCommit(Snapshot(first, 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sync.QueueLocalCommit(Snapshot(latest, 2));
        release.SetException(new IOException());
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 2);
        Assert.AreEqual(2, transport.Uploads.Count); // No automatic retry of the failed first snapshot.
        Assert.AreEqual(latest.AssetId, transport.Commits.Single().Request.Outfit.Items.Single().AssetId);
    }

    [TestMethod]
    public async Task NewApplyDuringSentCommitWaitsForItsReceiptAndUsesReturnedRevision()
    {
        var entered = Completion<bool>();
        var release = Completion<StoredAccessoryOutfit>();
        var transport = new FakeTransport
        {
            CommitHandler = (_, request, _) =>
            {
                if (request.ExpectedRevision != 2) return Task.FromResult(new StoredAccessoryOutfit(request.ExpectedRevision + 1, request.Outfit));
                entered.TrySetResult(true);
                return release.Task;
            }
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 2);
        sync.QueueLocalCommit(Snapshot(Image(1), 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sync.QueueLocalCommit(Snapshot(Image(2), 2));
        Assert.AreEqual(1, transport.Commits.Count);
        release.SetResult(new(3, transport.Commits.Single().Request.Outfit));
        await Until(() => sync.SafeDiagnostics.LastBackedUpLocalVersion == 2);
        Assert.AreEqual(2, transport.Commits.Count);
        Assert.AreEqual(3L, transport.Commits.Last().Request.ExpectedRevision);
    }

    [TestMethod]
    public async Task UnknownReconciliationWithWrongOutfitCannotClaimSuccess()
    {
        var transport = new FakeTransport { CommitHandler = (_, _, _) => throw new IOException() };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 3);
        sync.QueueLocalCommit(Snapshot(Image(1), 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        transport.Resolution = new(AccessoryCommitResolutionKind.Committed, new(4, AccessoryOutfitManifest.Empty));
        Assert.AreEqual(AccessoryCommitResolutionKind.Unknown, await sync.ReconcileUnknownCommitAsync());
        Assert.AreEqual(AccessorySyncState.OutcomeUnknown, sync.SafeDiagnostics.State);
        Assert.AreEqual(3L, sync.SafeDiagnostics.ServerRevision);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
    }

    [TestMethod]
    public async Task LateReconciliationForDetachedAccountReturnsUnknownAndCannotReportNewAccountSuccess()
    {
        var entered = Completion<bool>();
        var release = Completion<AccessoryCommitResolution>();
        var old = new FakeTransport
        {
            CommitHandler = (_, _, _) => throw new IOException(),
            ResolutionHandler = (_, _, _) => { entered.SetResult(true); return release.Task; }
        };
        var next = new FakeTransport();
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope("fictional-old"), old, 2);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        var reconcile = sync.ReconcileUnknownCommitAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sync.AttachAccount(Scope("fictional-new", 2), next, 10);
        release.SetResult(new(AccessoryCommitResolutionKind.Committed, new(3, AccessoryOutfitManifest.Empty)));
        Assert.AreEqual(AccessoryCommitResolutionKind.Unknown, await reconcile);
        Assert.AreEqual(10L, sync.SafeDiagnostics.ServerRevision);
        Assert.AreEqual(AccessorySyncState.Idle, sync.SafeDiagnostics.State);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
        Assert.AreEqual(0, next.Commits.Count);
    }

    [TestMethod]
    public async Task BoundedRequestTimeoutStopsIgnoringTransportAndKeepsCommitOutcomeUnknown()
    {
        var never = Completion<StoredAccessoryOutfit>();
        var transport = new FakeTransport { CommitHandler = (_, _, _) => never.Task };
        await using var sync = new AccessoryAccountSync(new FakeRepository(), TimeSpan.FromMilliseconds(25));
        sync.AttachAccount(Scope(), transport, 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.AreEqual(1, transport.Commits.Count);
        Assert.IsFalse(sync.RetryLatestBackup());
        never.SetResult(new(1, AccessoryOutfitManifest.Empty));
        await Task.Delay(40);
        Assert.AreEqual(AccessorySyncState.OutcomeUnknown, sync.SafeDiagnostics.State);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
    }

    [TestMethod]
    public async Task RestoreDownloadsAllUniqueImagesStagesAndCommitsExactlyOnce()
    {
        var first = Image(1);
        var second = Image(2);
        var manifest = new AccessoryOutfitManifest(1, [Item(first, "a"), Item(first, "b"), Item(second, "c")]);
        var transport = new FakeTransport { Current = new(12, manifest) };
        transport.Images[first.AssetId] = first;
        transport.Images[second.AssetId] = second;
        var repository = new FakeRepository { Guard = new(7, false) };
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope(), transport, 0);
        Assert.AreEqual(AccessoryRestoreResult.Restored, await sync.RestoreAsync());
        Assert.AreEqual(2, transport.Downloads.Count);
        Assert.AreEqual(1, repository.CommitCount);
        Assert.AreEqual(manifest, repository.Active!.Outfit);
        Assert.AreEqual(7L, repository.Active.LocalCommitVersion);
        Assert.AreEqual(12L, sync.SafeDiagnostics.ServerRevision);
        Assert.AreEqual(1, repository.DisposedStages);
    }

    [TestMethod]
    public async Task RestoreRejectsOlderThanVerifiedServerRevisionWithoutTouchingLocalState()
    {
        var transport = new FakeTransport { Current = new(4, AccessoryOutfitManifest.Empty) };
        var repository = new FakeRepository();
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope(), transport, 5);
        Assert.AreEqual(AccessoryRestoreResult.Failed, await sync.RestoreAsync());
        Assert.AreEqual(0, repository.StageCount);
        Assert.AreEqual(0, repository.CommitCount);
        Assert.AreEqual(5L, sync.SafeDiagnostics.ServerRevision);
        Assert.AreEqual("stale_account_outfit", sync.SafeDiagnostics.ErrorCode);
    }

    [TestMethod]
    public async Task RestoreDirtyDraftAndChangedLocalVersionBothPreserveLocalOutfit()
    {
        var transport = new FakeTransport { Current = new(2, AccessoryOutfitManifest.Empty) };
        var repository = new FakeRepository { Guard = new(1, true) };
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope(), transport, 0);
        Assert.AreEqual(AccessoryRestoreResult.Blocked, await sync.RestoreAsync());
        Assert.AreEqual(0, transport.GetCurrentCount);
        repository.Guard = new(1, false);
        repository.StageHandler = _ => { repository.Guard = new(2, false); return Task.CompletedTask; };
        Assert.AreEqual(AccessoryRestoreResult.Blocked, await sync.RestoreAsync());
        Assert.AreEqual(0, repository.CommitCount);
        Assert.AreEqual(1, repository.DisposedStages);
    }

    [TestMethod]
    public async Task FailedDownloadOrValidationNeverStagesPartialRestore()
    {
        var image = Image(1);
        var transport = new FakeTransport { Current = new(4, new(1, [Item(image, "a")])) };
        transport.Images[image.AssetId] = Image(2); // Correctly hashed bytes for a different asset are still not authorized data.
        var repository = new FakeRepository();
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope(), transport, 0);
        Assert.AreEqual(AccessoryRestoreResult.Failed, await sync.RestoreAsync());
        Assert.AreEqual(0, repository.StageCount);
        Assert.AreEqual(0, repository.CommitCount);
        Assert.AreEqual(0L, sync.SafeDiagnostics.ServerRevision);
    }

    [TestMethod]
    public async Task AccountSwitchDuringRestorePreventsOldRepositoryCommitEvenIfItIgnoresCancellation()
    {
        var entered = Completion<bool>();
        var release = Completion<bool>();
        var repository = new FakeRepository
        {
            BeforeCommit = async () => { entered.SetResult(true); await release.Task; }
        };
        var old = new FakeTransport { Current = new(7, AccessoryOutfitManifest.Empty) };
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope("fictional-old"), old, 0);
        var restore = sync.RestoreAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sync.AttachAccount(Scope("fictional-new", 2), new FakeTransport(), 3);
        release.SetResult(true);
        Assert.AreEqual(AccessoryRestoreResult.Superseded, await restore);
        Assert.AreEqual(0, repository.CommitCount);
        Assert.AreEqual(3L, sync.SafeDiagnostics.ServerRevision);
        Assert.AreEqual(AccessorySyncState.Idle, sync.SafeDiagnostics.State);
    }

    [TestMethod]
    public async Task RestoreCancellationPreventsAtomicLocalCommitAndDetachDropsQueuedData()
    {
        var entered = Completion<bool>();
        var release = Completion<bool>();
        var repository = new FakeRepository
        {
            BeforeCommit = async () => { entered.SetResult(true); await release.Task; }
        };
        using var cancellation = new CancellationTokenSource();
        await using var sync = new AccessoryAccountSync(repository);
        sync.AttachAccount(Scope(), new FakeTransport { Current = new(5, AccessoryOutfitManifest.Empty) }, 0);
        var restoring = sync.RestoreAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        release.SetResult(true);
        Assert.AreEqual(AccessoryRestoreResult.Cancelled, await restoring);
        Assert.AreEqual(0, repository.CommitCount);
        sync.DetachAccount();
        Assert.IsFalse(sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 2)));
        Assert.AreEqual(AccessorySyncState.Detached, sync.SafeDiagnostics.State);
    }

    [TestMethod]
    public void SnapshotCopiesBytesAndRejectsMissingExtraOrDuplicateImageReferences()
    {
        var original = Image(4);
        var bytes = original.PngBytes.ToArray();
        var copied = new AccessoryImageSnapshot(original.AssetId, 1, 1, bytes);
        bytes[24] = 0;
        var exposed = copied.PngBytes;
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(exposed, out var segment);
        segment.Array![24] = 0;
        CollectionAssert.AreEqual(original.PngBytes.ToArray(), copied.PngBytes.ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryLocalSnapshot(new(1, [Item(original, "a")]), [], 1));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryLocalSnapshot(AccessoryOutfitManifest.Empty, [original], 1));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryLocalSnapshot(new(1, [Item(original, "a")]), [original, original], 1));
        var invalid = copied.PngBytes.ToArray();
        invalid[^1] ^= 1;
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryImageSnapshot(copied.AssetId, 1, 1, invalid));
    }

    [TestMethod]
    public async Task UnsafeOrMutableServiceOriginIsNotUsedForUploads()
    {
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        Assert.ThrowsExactly<ArgumentException>(() => sync.AttachAccount(Scope(),
            new FakeTransport { ServiceOrigin = new("http://public.example.test/") }, 0));
        var transport = new FakeTransport();
        sync.AttachAccount(Scope(), transport, 0);
        transport.ServiceOrigin = new("https://changed.example.test/");
        sync.QueueLocalCommit(Snapshot(Image(2), 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.Failed);
        Assert.AreEqual(0, transport.Uploads.Count);
        Assert.AreEqual("service_origin_changed", sync.SafeDiagnostics.ErrorCode);
    }

    [TestMethod]
    public async Task DefinitiveAuthenticationRejectionDoesNotPretendAnUnknownWriteOccurred()
    {
        var transport = new FakeTransport
        {
            CommitHandler = (_, _, _) => throw new AccessoryAccountTransportException(AccessoryTransportFailure.Authentication)
        };
        await using var sync = new AccessoryAccountSync(new FakeRepository());
        sync.AttachAccount(Scope(), transport, 3);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.Failed);
        Assert.AreEqual("accessory_authentication_required", sync.SafeDiagnostics.ErrorCode);
        Assert.AreEqual(3L, sync.SafeDiagnostics.ServerRevision);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
        Assert.AreEqual(AccessoryCommitResolutionKind.Unknown, await sync.ReconcileUnknownCommitAsync());
        Assert.AreEqual(0, transport.ResolvedKeys.Count);
    }

    private static AccessoryOutfitItem Item(AccessoryImageSnapshot image, string id) => new(id, "Fictional accessory", image.AssetId, 1, 1);
    private static AccessoryLocalSnapshot Snapshot(AccessoryImageSnapshot image, long version) => new(new(1, [Item(image, "a")]), [image], version);
    private static UploadedAccessoryAsset Mapping(AccessoryImageSnapshot image) => new(image.AssetId, image.AssetId, image.PixelWidth, image.PixelHeight, 1);
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private static AccessoryImageSnapshot Image(byte color)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            zlib.Write(new byte[] { 0, color, 12, 25, 128 });
        Chunk(png, "IDAT"u8, compressed.ToArray());
        Chunk(png, "IEND"u8, []);
        var bytes = png.ToArray();
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 1, 1, bytes);
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(count, (uint)data.Length);
        output.Write(count);
        output.Write(type);
        output.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in type.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(count, ~crc);
        output.Write(count);
    }

    private sealed class FakeTransport : IAccessoryAccountTransport
    {
        public Uri ServiceOrigin { get; set; } = new("https://accessory.example.test/");
        public ConcurrentQueue<(AccessoryAccountScope Scope, AccessoryImageSnapshot Image)> Uploads { get; } = [];
        public ConcurrentQueue<(AccessoryAccountScope Scope, CommitAccessoryOutfitRequest Request)> Commits { get; } = [];
        public ConcurrentQueue<string> Downloads { get; } = [];
        public ConcurrentQueue<string> ResolvedKeys { get; } = [];
        public Dictionary<string, AccessoryImageSnapshot> Images { get; } = [];
        public AccessoryImageSnapshot? MapTo { get; init; }
        public StoredAccessoryOutfit? Current { get; init; }
        public AccessoryCommitResolution Resolution { get; set; } = new(AccessoryCommitResolutionKind.Unknown);
        public Func<AccessoryAccountScope, AccessoryImageSnapshot, CancellationToken, Task<UploadedAccessoryAsset>>? UploadHandler { get; set; }
        public Func<AccessoryAccountScope, CommitAccessoryOutfitRequest, CancellationToken, Task<StoredAccessoryOutfit>>? CommitHandler { get; set; }
        public Func<AccessoryAccountScope, string, CancellationToken, Task<AccessoryCommitResolution>>? ResolutionHandler { get; init; }
        public int GetCurrentCount;

        public Task<UploadedAccessoryAsset> UploadAssetAsync(AccessoryAccountScope scope, AccessoryImageSnapshot image, CancellationToken cancellationToken)
        {
            Uploads.Enqueue((scope, image));
            return UploadHandler?.Invoke(scope, image, cancellationToken) ?? Task.FromResult(MapTo is null
                ? Mapping(image) : new UploadedAccessoryAsset(image.AssetId, MapTo.AssetId, MapTo.PixelWidth, MapTo.PixelHeight, 1));
        }

        public Task<StoredAccessoryOutfit> CommitOutfitAsync(AccessoryAccountScope scope, CommitAccessoryOutfitRequest request, CancellationToken cancellationToken)
        {
            Commits.Enqueue((scope, request));
            return CommitHandler?.Invoke(scope, request, cancellationToken) ?? Task.FromResult(new StoredAccessoryOutfit(request.ExpectedRevision + 1, request.Outfit));
        }

        public Task<StoredAccessoryOutfit?> GetCurrentOutfitAsync(AccessoryAccountScope scope, CancellationToken cancellationToken)
        {
            GetCurrentCount++;
            return Task.FromResult(Current);
        }

        public Task<AccessoryImageSnapshot> DownloadAssetAsync(AccessoryAccountScope scope, string assetId, CancellationToken cancellationToken)
        {
            Downloads.Enqueue(assetId);
            return Task.FromResult(Images[assetId]);
        }

        public Task<AccessoryCommitResolution> ResolveCommitAsync(AccessoryAccountScope scope, string idempotencyKey, CancellationToken cancellationToken)
        {
            ResolvedKeys.Enqueue(idempotencyKey);
            return ResolutionHandler?.Invoke(scope, idempotencyKey, cancellationToken) ?? Task.FromResult(Resolution);
        }
    }

    private sealed class FakeRepository : IAccessoryLocalRepository
    {
        public AccessoryRestoreGuard Guard { get; set; } = new(0, false);
        public Func<AccessoryLocalSnapshot, Task>? StageHandler { get; set; }
        public Func<Task>? BeforeCommit { get; init; }
        public AccessoryLocalSnapshot? Active { get; private set; }
        public int StageCount;
        public int CommitCount;
        public int DisposedStages;

        public Task<AccessoryRestoreGuard> CaptureRestoreGuardAsync(CancellationToken cancellationToken) => Task.FromResult(Guard);
        public async Task<IAccessoryRestoreStage> StageRestoreAsync(AccessoryLocalSnapshot snapshot, CancellationToken cancellationToken)
        {
            StageCount++;
            if (StageHandler is not null) await StageHandler(snapshot);
            return new Stage(this, snapshot);
        }
        public async Task<bool> TryCommitRestoreAsync(IAccessoryRestoreStage stage, AccessoryRestoreGuard guard,
            AccessoryRestoreAuthorization authorization, CancellationToken cancellationToken)
        {
            if (BeforeCommit is not null) await BeforeCommit();
            return authorization.TryCommit(() =>
            {
                if (Guard.HasDirtyDraft || Guard.LocalCommitVersion != guard.LocalCommitVersion) return false;
                Active = ((Stage)stage).Snapshot;
                CommitCount++;
                return true;
            });
        }

        private sealed class Stage(FakeRepository repository, AccessoryLocalSnapshot snapshot) : IAccessoryRestoreStage
        {
            internal AccessoryLocalSnapshot Snapshot { get; } = snapshot;
            public ValueTask DisposeAsync() { repository.DisposedStages++; return ValueTask.CompletedTask; }
        }
    }
}
