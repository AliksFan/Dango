using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.AccessoryAssets;
using Dango.AccessoryModeration;
using Dango.Client.Sync.Accessories;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.AccountSync;
using Dango.Protocol.Accessories;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Actual local commits, portable blob I/O and two local repositories. Transport receipts are a test harness.</summary>
[TestClass]
public sealed class AccessoryBackupModuleIntegrationTests
{
    [TestMethod]
    public async Task AppliedOutfitBacksUpAndRestoresIntoSecondLocalRepositoryWithAllParameters()
    {
        var root = Path.Combine(Path.GetTempPath(), "Dango-accessory-module-" + Guid.NewGuid().ToString("N"));
        try
        {
            await OnDispatcherAsync(async () =>
            {
                Directory.CreateDirectory(root);
                var source = Path.Combine(root, "source.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32,
                    null, new byte[] { 0, 0, 0, 0, 22, 40, 60, 255, 77, 90, 100, 128 }, 12)));
                using (var output = File.Create(source)) encoder.Save(output);

                var firstRoot = Path.Combine(root, "first-local");
                var first = new AccessorySettingsSession(firstRoot);
                var firstRepository = new AccessoryAccountLocalRepository(first);
                var transport = new ModuleTransport(Path.Combine(root, "blobs"));
                await using var backup = new AccessoryAccountSync(firstRepository);
                backup.AttachAccount(new("fictional-account", "fictional-pet", 1), transport, 0);
                using var binding = new AccessoryCommittedBackupBinding(first, firstRepository, backup);
                var imported = first.ImportPng(source) with
                {
                    Name = "虚构配饰",
                    Layer = AccessoryLayer.Behind,
                    Order = 7,
                    SizePercent = 233,
                    BaseAngleDegrees = -120,
                    ImagePivotX = .17,
                    ImagePivotY = .9,
                    BodyAnchorX = .6,
                    BodyAnchorY = .75,
                    Softness = 87,
                    AllowFolding = true,
                    MaxDeformationPercent = 175,
                    MaxConnectionTravelPercent = 150,
                    ConnectionStrength = 34,
                    RotationDamping = 29,
                    LockRotation = false,
                    ReturnStrength = 9,
                    MaxSwingDegrees = 81,
                    LimitSwing = false,
                    GravityInfluence = 64,
                };
                var copy = imported with { Id = "second-instance", Name = "独立副本", Layer = AccessoryLayer.Front, Order = 1, Softness = 20 };
                var unequipped = new AccessoryItem("private-library", "本地库", "builtin-top-hat", 64, 64) { IsEquipped = false };
                first.Stage(new([imported, copy, unequipped]));
                Assert.AreEqual(0, transport.Uploads, "Preview must not upload.");
                Assert.IsTrue(first.TrySave());
                File.Move(source, Path.Combine(root, "source-moved.png"));
                await WaitForBackupAsync(backup);
                Assert.AreEqual(1, transport.Uploads, "Independent instances share one image upload.");
                Assert.AreEqual(1, transport.Commits);
                Assert.IsNull(binding.LastCaptureErrorCode);
                Assert.IsNotNull(transport.Stored);
                Assert.HasCount(2, transport.Stored.Outfit.Items);
                Assert.AreNotEqual(imported.AssetId, transport.Stored.Outfit.Items[0].AssetId);
                Assert.HasCount(1, Directory.GetFiles(Path.Combine(root, "blobs"), "*.png", SearchOption.AllDirectories));

                // Actual file-backed image review affects fresh public projections, independently of private restore.
                var reviewRepository = new FileAccessoryReviewRepository(Path.Combine(root, "reviews"));
                var reviewService = new AccessoryImageReviewService(new FileAccessoryBlobStore(Path.Combine(root, "blobs")), reviewRepository);
                var projector = new AccessoryPublicImageProjector(reviewRepository);
                var imageId = transport.Stored.Outfit.Items[0].AssetId;
                Assert.HasCount(0, (await projector.ProjectAsync(transport.Stored.Outfit, _ => true)).Items);
                var pending = await reviewService.RequestReviewAsync(imageId);
                var approved = await reviewService.DecideAsync(imageId, pending.Revision, AccessoryImageReviewDecision.Approve, "Fictional test approval.");
                var changedOutfit = new AccessoryOutfitManifest(1, transport.Stored.Outfit.Items.Select(item => item with { SizePercent = 300, BaseAngleDegrees = 42 }).ToArray());
                Assert.HasCount(2, (await projector.ProjectAsync(changedOutfit, _ => true)).Items);
                Assert.HasCount(0, (await projector.ProjectAsync(changedOutfit, _ => false)).Items, "Approval cannot grant private access or display authorization.");
                Assert.AreEqual(approved.Revision, (await reviewRepository.ReadAsync(imageId))!.Revision);
                await reviewService.DecideAsync(imageId, approved.Revision, AccessoryImageReviewDecision.Revoke, "Fictional test revocation.");
                Assert.HasCount(0, (await projector.ProjectAsync(changedOutfit, _ => true)).Items);

                var secondRoot = Path.Combine(root, "second-local");
                var second = new AccessorySettingsSession(secondRoot);
                second.Stage(new([new AccessoryItem("other-library", "另一素材库", "builtin-bowler", 64, 64) { IsEquipped = false }]));
                Assert.IsTrue(second.TrySave());
                var secondRepository = new AccessoryAccountLocalRepository(second);
                await using var restore = new AccessoryAccountSync(secondRepository);
                restore.AttachAccount(new("fictional-account", "fictional-pet", 2), transport, 1);
                using var secondBinding = new AccessoryCommittedBackupBinding(second, secondRepository, restore);
                Assert.AreEqual(AccessoryRestoreResult.Restored, await restore.RestoreAsync(), "Content revocation must not prevent the owner's private restore.");
                Assert.AreEqual(1, transport.Commits, "Restoring must not trigger a backup loop.");
                var reopened = new AccessorySettingsSession(secondRoot);
                Assert.AreEqual(transport.Stored.Outfit, AccessoryOutfitMapper.FromSavedConfiguration(reopened.SavedConfiguration));
                Assert.IsFalse(reopened.SavedConfiguration.Items.Single(item => item.Id == "other-library").IsEquipped);
                foreach (var item in reopened.SavedConfiguration.Items.Where(item => item.IsEquipped))
                    Assert.IsNotNull(reopened.TryGetImage(item.AssetId));
                Assert.AreEqual(1, transport.Downloads);
            });
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
                !Path.GetFileName(resolved).StartsWith("Dango-accessory-module-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected module test root.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task WaitForBackupAsync(AccessoryAccountSync sync)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (sync.SafeDiagnostics.State == AccessorySyncState.BackedUp) return;
            if (sync.SafeDiagnostics.State is AccessorySyncState.Failed or AccessorySyncState.OutcomeUnknown or AccessorySyncState.RevisionConflict)
                Assert.Fail("Backup did not complete: " + sync.SafeDiagnostics.ErrorCode);
            await Task.Delay(10);
        }
        Assert.Fail("Module backup timed out.");
    }

    private static async Task OnDispatcherAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher? owner = null;
        var thread = new Thread(() =>
        {
            owner = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(owner));
            owner.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { owner.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            if (owner is not null && !owner.HasShutdownStarted) owner.BeginInvokeShutdown(DispatcherPriority.Send);
            if (!thread.Join(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Test dispatcher did not stop.");
        }
    }

    private sealed class ModuleTransport(string root) : IAccessoryAccountTransport
    {
        private readonly FileAccessoryBlobStore blobs = new(root);
        public Uri ServiceOrigin { get; } = new("https://accessory-module.invalid");
        public StoredAccessoryOutfit? Stored { get; private set; }
        public int Uploads { get; private set; }
        public int Commits { get; private set; }
        public int Downloads { get; private set; }

        public async Task<UploadedAccessoryAsset> UploadAssetAsync(AccessoryAccountScope scope, AccessoryImageSnapshot image, CancellationToken cancellationToken)
        {
            Uploads++;
            var result = await blobs.ImportAsync(new MemoryStream(image.PngBytes.ToArray()), image.AssetId, cancellationToken);
            return new(result.SourceDigest, result.BlobDigest, result.Width, result.Height, result.NormalizationVersion);
        }
        public Task<StoredAccessoryOutfit> CommitOutfitAsync(AccessoryAccountScope scope, CommitAccessoryOutfitRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Validate();
            var baselineRevision = Stored?.Revision ?? 0;
            var suppliedRevision = request.ExpectedRevision;
            Assert.AreEqual(baselineRevision, suppliedRevision);
            Commits++;
            Stored = new(request.ExpectedRevision + 1, request.Outfit);
            return Task.FromResult(Stored);
        }
        public Task<StoredAccessoryOutfit?> GetCurrentOutfitAsync(AccessoryAccountScope scope, CancellationToken cancellationToken) => Task.FromResult(Stored);
        public async Task<AccessoryImageSnapshot> DownloadAssetAsync(AccessoryAccountScope scope, string assetId, CancellationToken cancellationToken)
        {
            Downloads++;
            var image = await blobs.ReadAsync(assetId, cancellationToken);
            return new(image.Digest, image.Width, image.Height, image.CopyPngBytes());
        }
        public Task<AccessoryCommitResolution> ResolveCommitAsync(AccessoryAccountScope scope, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(new AccessoryCommitResolution(AccessoryCommitResolutionKind.Unknown));
    }
}
