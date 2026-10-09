using System.Buffers.Binary;
using System.IO.Compression;
using Dango.AccessoryAssets;
using Dango.Protocol.Accessories;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccessoryModeration.Tests;

[TestClass]
public sealed class AccessoryModerationTests
{
    [TestMethod]
    public async Task PrivateImportAloneDoesNotCreatePendingReview()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        Assert.AreEqual(0, (await fixture.Reviews.ListAsync()).Count);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ReadPreviewAsync(image.BlobDigest));
    }

    [TestMethod]
    public async Task ExplicitRealImageRequestSurvivesRepositoryReloadAndHasActualPreview()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        var pending = await fixture.Service.RequestReviewAsync(image.BlobDigest);
        Assert.AreEqual(AccessoryImageReviewStatus.Pending, pending.Status); Assert.AreEqual(1, pending.Revision);
        var reloaded = new FileAccessoryReviewRepository(fixture.ReviewRoot);
        var stored = await reloaded.ReadAsync(image.BlobDigest);
        Assert.AreEqual(pending.Digest, stored!.Digest); Assert.AreEqual(1, stored.History.Count);
        CollectionAssert.AreEqual((await fixture.Blobs.ReadAsync(image.BlobDigest)).CopyPngBytes(),
            (await fixture.Service.ReadPreviewAsync(image.BlobDigest)).CopyPngBytes());
    }

    [TestMethod]
    public async Task MissingBlobCannotBeSubmittedOrApproved()
    {
        using var fixture = new ReviewDirectory(); var missing = new string('c', 64);
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => fixture.Service.RequestReviewAsync(missing));
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => fixture.Service.DecideAsync(missing, 1, AccessoryImageReviewDecision.Approve, "reviewed"));
        Assert.AreEqual(0, (await fixture.Reviews.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ApprovalRejectsBlobCorruptedAfterPendingRequest()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        File.WriteAllBytes(fixture.BlobPath(image.BlobDigest), [0, 1]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "reviewed"));
        Assert.AreEqual(AccessoryImageReviewStatus.Pending, (await fixture.Reviews.ReadAsync(image.BlobDigest))!.Status);
    }

    [TestMethod]
    public async Task ConcurrentStaleScreensCannotBothApproveAndReject()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        var second = new AccessoryImageReviewService(fixture.Blobs, new FileAccessoryReviewRepository(fixture.ReviewRoot));
        var decisions = await Task.WhenAll(TryDecision(fixture.Service, AccessoryImageReviewDecision.Approve),
            TryDecision(second, AccessoryImageReviewDecision.Reject));
        Assert.AreEqual(1, decisions.Count(result => result));
        var stored = (await fixture.Reviews.ReadAsync(image.BlobDigest))!;
        Assert.AreEqual(2, stored.Revision); Assert.AreEqual(2, stored.History.Count);
        Assert.AreEqual(stored.Status, stored.History[^1].Status);

        async Task<bool> TryDecision(AccessoryImageReviewService service, AccessoryImageReviewDecision decision)
        {
            try { await service.DecideAsync(image.BlobDigest, 1, decision, "actual manual review"); return true; }
            catch (AccessoryReviewConflictException error) { Assert.AreEqual(2, error.ActualRevision); return false; }
        }
    }

    [TestMethod]
    public async Task RejectedAndRevokedContentRemainReadablePrivately()
    {
        using var fixture = new ReviewDirectory(); var rejected = await fixture.ImportAsync(10); var revoked = await fixture.ImportAsync(20);
        await fixture.Service.RequestReviewAsync(rejected.BlobDigest); await fixture.Service.RequestReviewAsync(revoked.BlobDigest);
        await fixture.Service.DecideAsync(rejected.BlobDigest, 1, AccessoryImageReviewDecision.Reject, "not suitable for display");
        await fixture.Service.DecideAsync(revoked.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        await fixture.Service.DecideAsync(revoked.BlobDigest, 2, AccessoryImageReviewDecision.Revoke, "reported image rechecked");
        Assert.AreEqual(rejected.BlobDigest, (await fixture.Blobs.ReadAsync(rejected.BlobDigest)).Digest);
        Assert.AreEqual(revoked.BlobDigest, (await fixture.Blobs.ReadAsync(revoked.BlobDigest)).Digest);
        Assert.AreEqual(0, (await fixture.ProjectIdsAsync([rejected.BlobDigest, revoked.BlobDigest], _ => true)).Count);
    }

    [TestMethod]
    public async Task WearingParameterChangesReuseApprovalWithoutAnotherRequest()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        var original = new AccessoryOutfitItem("first", "Fictional accessory", image.BlobDigest, 1, 1);
        var changed = original with { SizePercent = 300, BaseAngleDegrees = 90 };
        CollectionAssert.AreEqual(new[] { original with { Name = "配饰" } }, (await fixture.Projector.ProjectAsync(new(1, [original]), _ => true)).Items.ToArray());
        CollectionAssert.AreEqual(new[] { changed with { Name = "配饰" } }, (await fixture.Projector.ProjectAsync(new(1, [changed]), _ => true)).Items.ToArray());
        Assert.AreEqual(2, (await fixture.Service.RequestReviewAsync(image.BlobDigest)).Revision);
        Assert.AreEqual(1, (await fixture.Reviews.ListAsync()).Count);
    }

    [TestMethod]
    public async Task RevokeRemovesEveryReferenceFromFreshMixedOutfitProjections()
    {
        using var fixture = new ReviewDirectory(); var approved = await fixture.ImportAsync(10); var unreviewed = await fixture.ImportAsync(20);
        await fixture.Service.RequestReviewAsync(approved.BlobDigest);
        await fixture.Service.DecideAsync(approved.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        string[] firstOutfit = [approved.BlobDigest, "builtin-sprout", approved.BlobDigest, unreviewed.BlobDigest];
        string[] secondOutfit = [approved.BlobDigest, "builtin-top-hat"];
        Assert.AreEqual(3, (await fixture.ProjectIdsAsync(firstOutfit, _ => true)).Count);
        Assert.AreEqual(2, (await fixture.ProjectIdsAsync(secondOutfit, _ => true)).Count);
        await fixture.Service.DecideAsync(approved.BlobDigest, 2, AccessoryImageReviewDecision.Revoke, "manual complaint recheck");
        CollectionAssert.AreEqual(new[] { "builtin-sprout" }, (await fixture.ProjectIdsAsync(firstOutfit, _ => true)).ToArray());
        CollectionAssert.AreEqual(new[] { "builtin-top-hat" }, (await fixture.ProjectIdsAsync(secondOutfit, _ => true)).ToArray());
    }

    [TestMethod]
    public async Task SharedContentConclusionNeverSuppliesMissingDisplayAccessGrant()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        Assert.AreEqual(1, (await fixture.ProjectIdsAsync([image.BlobDigest], _ => true)).Count);
        Assert.AreEqual(0, (await fixture.ProjectIdsAsync([image.BlobDigest], _ => false)).Count);
    }

    [TestMethod]
    public async Task BuiltinsRequireNeitherRequestsNorPrivateImageGrants()
    {
        using var fixture = new ReviewDirectory(); string[] builtins = ["builtin-sprout", "builtin-top-hat", "builtin-bowler"];
        CollectionAssert.AreEqual(builtins, (await fixture.ProjectIdsAsync(builtins, _ => false)).ToArray());
        Assert.AreEqual(0, (await fixture.Reviews.ListAsync()).Count);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.RequestReviewAsync("builtin-sprout"));
    }

    [TestMethod]
    public async Task DecisionsRequireReasonAndValidTransitionAndPreserveExistingConclusion()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, " "));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Revoke, "cannot revoke pending"));
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Reject, "not suitable for display");
        await Assert.ThrowsExactlyAsync<AccessoryReviewConflictException>(() => fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "stale screen"));
        Assert.AreEqual(AccessoryImageReviewStatus.Rejected, (await fixture.Service.RequestReviewAsync(image.BlobDigest)).Status);
    }

    [TestMethod]
    public async Task CorruptedReviewRecordFailsClosedInsteadOfShowingApprovedImage()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        var path = Directory.GetFiles(fixture.ReviewRoot, "*.review.json").Single();
        var bytes = File.ReadAllText(path); File.WriteAllText(path, bytes.Replace("\"approved\"", "\"unknown-future-state\"", StringComparison.Ordinal));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ProjectIdsAsync([image.BlobDigest], _ => true));
    }

    [TestMethod]
    public async Task ManyConcurrentRequestsShareOneDurablePendingRecord()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        var records = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            new AccessoryImageReviewService(fixture.Blobs, new FileAccessoryReviewRepository(fixture.ReviewRoot)).RequestReviewAsync(image.BlobDigest)));
        Assert.IsTrue(records.All(record => record.Revision == 1 && record.Status == AccessoryImageReviewStatus.Pending));
        Assert.AreEqual(1, Directory.GetFiles(fixture.ReviewRoot, "*.review.json").Length);
        Assert.AreEqual(1, (await fixture.Reviews.ListAsync()).Count);
    }

    [TestMethod]
    public async Task DecisionReasonPersistsButNoPathsOrSourceMetadataAreStored()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "人工查看图片后通过");
        var restored = (await new FileAccessoryReviewRepository(fixture.ReviewRoot).ReadAsync(image.BlobDigest))!;
        Assert.AreEqual("人工查看图片后通过", restored.History[^1].Reason);
        Assert.IsFalse(File.ReadAllText(Directory.GetFiles(fixture.ReviewRoot, "*.review.json").Single()).Contains(fixture.Root, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PartialDecisionWriteFailurePreservesFullOldRecordAndPrivateImage()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        var path = Directory.GetFiles(fixture.ReviewRoot, "*.review.json").Single(); var original = File.ReadAllBytes(path);
        var faulting = new AccessoryImageReviewService(fixture.Blobs, new FileAccessoryReviewRepository(fixture.ReviewRoot,
            stage => { if (stage == ReviewWriteStage.AfterPartialWrite) throw new IOException("injected partial record failure"); }));
        await Assert.ThrowsExactlyAsync<IOException>(() => faulting.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "manual image check"));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.AreEqual(0, Directory.GetFiles(fixture.ReviewRoot, "*.tmp").Length);
        Assert.AreEqual(image.BlobDigest, (await fixture.Blobs.ReadAsync(image.BlobDigest)).Digest);
    }

    [TestMethod]
    public async Task FailedInitialPublishLeavesNoQueueRecordOrTemporaryFile()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        var faulting = new AccessoryImageReviewService(fixture.Blobs, new FileAccessoryReviewRepository(fixture.ReviewRoot,
            stage => { if (stage == ReviewWriteStage.BeforePublish) throw new IOException("injected publication failure"); }));
        await Assert.ThrowsExactlyAsync<IOException>(() => faulting.RequestReviewAsync(image.BlobDigest));
        Assert.AreEqual(0, (await fixture.Reviews.ListAsync()).Count);
        Assert.AreEqual(0, Directory.GetFiles(fixture.ReviewRoot, "*.tmp").Length);
    }

    [TestMethod]
    public async Task UnknownSchemaAndInvalidHistoryDoNotAcquirePublicEligibility()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        var path = Directory.GetFiles(fixture.ReviewRoot, "*.review.json").Single(); var original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ProjectIdsAsync([image.BlobDigest], _ => true));
        File.WriteAllText(path, original.Replace("\"revision\": 2", "\"revision\": 3", StringComparison.Ordinal));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ProjectIdsAsync([image.BlobDigest], _ => true));
        File.WriteAllText(path, original.Insert(original.IndexOf('{') + 1, "\"schemaVersion\":1,"));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Reviews.ReadAsync(image.BlobDigest));
    }

    [TestMethod]
    public async Task ValidImageCannotBeApprovedWithoutExplicitDisplayRequest()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "manual image check"));
        Assert.AreEqual(0, (await fixture.Reviews.ListAsync()).Count);
    }

    [TestMethod]
    public async Task CancelledDecisionDoesNotChangePendingRecord()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Reject,
            "manual image check", cancellation.Token));
        Assert.AreEqual(1, (await fixture.Reviews.ReadAsync(image.BlobDigest))!.Revision);
    }

    [TestMethod]
    public async Task TraversalCannotSelectAnotherReviewRecord()
    {
        using var fixture = new ReviewDirectory();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Reviews.ReadAsync("../../private.json"));
    }

    [TestMethod]
    public async Task CallerCollectionMutationDuringAuthorizationCannotSubstituteUnreviewedAsset()
    {
        using var fixture = new ReviewDirectory(); var approved = await fixture.ImportAsync(10); var unreviewed = await fixture.ImportAsync(20);
        await fixture.Service.RequestReviewAsync(approved.BlobDigest);
        await fixture.Service.DecideAsync(approved.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        var original = new AccessoryOutfitItem("first", "Fictional accessory", approved.BlobDigest, 1, 1);
        AccessoryOutfitItem[] mutableSource = [original]; var captured = new AccessoryOutfitManifest(1, mutableSource);
        var result = await fixture.Projector.ProjectAsync(captured, _ =>
        {
            mutableSource[0] = original with { AssetId = unreviewed.BlobDigest };
            return true;
        });
        Assert.AreEqual(approved.BlobDigest, result.Items.Single().AssetId);
        Assert.AreEqual(unreviewed.BlobDigest, mutableSource.Single().AssetId);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AccessoryOutfitItem>)result.Items)[0] = mutableSource[0]);
    }

    [TestMethod]
    public async Task PublicProjectionRedactsPrivateLibraryLabelsWithoutChangingPrivateOutfit()
    {
        using var fixture = new ReviewDirectory(); var image = await fixture.ImportAsync();
        await fixture.Service.RequestReviewAsync(image.BlobDigest);
        await fixture.Service.DecideAsync(image.BlobDigest, 1, AccessoryImageReviewDecision.Approve, "approved image");
        var item = new AccessoryOutfitItem("first", "fictional-private-label.png", image.BlobDigest, 1, 1)
        { Layer = AccessoryOutfitLayer.Behind, Order = 7, SizePercent = 213, Softness = 67, AllowFolding = true, BodyAnchorX = .21 };
        var original = new AccessoryOutfitManifest(1, [item]); var result = await fixture.Projector.ProjectAsync(original, _ => true);
        Assert.AreEqual(item with { Name = "配饰" }, result.Items.Single());
        Assert.AreEqual(item, original.Items.Single());
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(result).Contains("fictional-private-label", StringComparison.Ordinal));
    }

    private sealed class ReviewDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Dango.AccessoryModeration.Tests-" + Guid.NewGuid().ToString("N"));
        public string BlobRoot => Path.Combine(Root, "blobs");
        public string ReviewRoot => Path.Combine(Root, "reviews");
        public FileAccessoryBlobStore Blobs { get; }
        public FileAccessoryReviewRepository Reviews { get; }
        public AccessoryImageReviewService Service { get; }
        public AccessoryPublicImageProjector Projector { get; }
        public ReviewDirectory()
        {
            Directory.CreateDirectory(Root); Blobs = new(BlobRoot); Reviews = new(ReviewRoot);
            Service = new(Blobs, Reviews); Projector = new(Reviews);
        }
        public string BlobPath(string digest) => Path.Combine(BlobRoot, digest[..2], digest + ".png");
        public Task<AccessoryBlobImport> ImportAsync(byte red = 50) => Blobs.ImportAsync(new MemoryStream(CreatePng(red)));
        public async Task<IReadOnlyList<string>> ProjectIdsAsync(IEnumerable<string> ids, Func<string, bool> authorized)
        {
            var outfit = new AccessoryOutfitManifest(1, ids.Select((id, index) => new AccessoryOutfitItem("instance-" + index, "Fictional accessory", id,
                AccessoryOutfitItem.IsBuiltInAsset(id) ? 64 : 1, AccessoryOutfitItem.IsBuiltInAsset(id) ? 64 : 1)).ToArray());
            return (await Projector.ProjectAsync(outfit, authorized)).Items.Select(item => item.AssetId).ToArray();
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root); var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (Path.GetDirectoryName(resolved) != parent || !Path.GetFileName(resolved).StartsWith("Dango.AccessoryModeration.Tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the test directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static byte[] CreatePng(byte red)
    {
        using var png = new MemoryStream(); png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; header[9] = 6; Chunk("IHDR"u8, header);
        using var compressed = new MemoryStream(); using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zlib.Write([0, red, 0, 0, 128]);
        Chunk("IDAT"u8, compressed.ToArray()); Chunk("IEND"u8, []); return png.ToArray();
        void Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> count = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(count, data.Length); png.Write(count); png.Write(type); png.Write(data);
            uint crc = uint.MaxValue; foreach (var value in type.ToArray().Concat(data.ToArray()))
            { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(count, ~crc); png.Write(count);
        }
    }
}
