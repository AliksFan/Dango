using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccessoryAssets.Tests;

[TestClass]
public sealed class AccessoryAssetTests
{
    [TestMethod]
    public void EqualPixelsWithDifferentEncodingAndMetadataHaveOneDigest()
    {
        var normal = PngFixture.Rgba(); var alternative = PngFixture.Rgba(metadata: true, compression: CompressionLevel.NoCompression);
        Assert.AreNotEqual(Hash(normal), Hash(alternative));
        var first = AccessoryPngNormalizer.Normalize(normal); var second = AccessoryPngNormalizer.Normalize(alternative);
        Assert.AreEqual(first.Digest, second.Digest); CollectionAssert.AreEqual(first.CopyPngBytes(), second.CopyPngBytes());
        Assert.AreEqual(Hash(first.CopyPngBytes()), first.Digest);
        Assert.IsFalse(System.Text.Encoding.ASCII.GetString(first.CopyPngBytes()).Contains("fictional-user", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Adam7AndNonInterlacedPixelsNormalizeIdentically() => Assert.AreEqual(
        AccessoryPngNormalizer.Normalize(PngFixture.Rgba()).Digest, AccessoryPngNormalizer.Normalize(PngFixture.Rgba(interlaced: true)).Digest);

    [TestMethod]
    public void InvisibleRgbDoesNotCreateAdditionalBlob()
    {
        var changed = (byte[])PngFixture.Pixels.Clone(); changed[4] = 130; changed[5] = 140; changed[6] = 150;
        Assert.AreEqual(AccessoryPngNormalizer.Normalize(PngFixture.Rgba()).Digest, AccessoryPngNormalizer.Normalize(PngFixture.Rgba(changed)).Digest);
    }

    [TestMethod]
    public void ReturnedBytesCannotMutateNormalizedImage()
    {
        var image = AccessoryPngNormalizer.Normalize(PngFixture.Rgba()); var copy = image.CopyPngBytes(); copy[0] = 0;
        Assert.AreEqual(image.Digest, Hash(image.CopyPngBytes()));
    }

    [TestMethod]
    public void DamagedCrcIsRejected()
    { var png = PngFixture.Rgba(); png[29] ^= 1; Reject(png); }

    [TestMethod]
    public void DeflateBombIsRejectedBeforeCodec()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress(new byte[2_000_000]))); }

    [TestMethod]
    public void ExcessDecodedScanlinesAreRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress(new byte[10]))); }

    [TestMethod]
    public void InvalidZlibChecksumIsRejected()
    { var compressed = PngFixture.Compress([0, 100, 0, 0, 100]); compressed[^1] ^= 1; Reject(PngFixture.Assemble(PngFixture.Header(1, 1), compressed)); }

    [TestMethod]
    public void InvalidFilterIsRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([5, 100, 0, 0, 100]))); }

    [TestMethod]
    public void AnimationIsRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([0, 100, 0, 0, 100]), [PngFixture.Chunk("acTL", new byte[8])])); }

    [TestMethod]
    public void OversizedDimensionIsRejectedWithoutAllocatingPixels()
    { Reject(PngFixture.Assemble(PngFixture.Header(4097, 1), [])); }

    [TestMethod]
    public void OversizedPixelAreaIsRejectedWithoutAllocatingPixels()
    { Reject(PngFixture.Assemble(PngFixture.Header(4096, 4096), [])); }

    [TestMethod]
    public void AllTransparentImageIsRejected() => Reject(PngFixture.Rgba(new byte[16]));

    [TestMethod]
    public void AllOpaqueImageIsRejected()
    { var pixels = (byte[])PngFixture.Pixels.Clone(); for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; Reject(PngFixture.Rgba(pixels)); }

    [TestMethod]
    public void TruncationAndTrailingBytesAreRejected()
    { var png = PngFixture.Rgba(); Reject(png[..^1]); Reject([.. png, 0]); }

    [TestMethod]
    public void UnknownCriticalChunkIsRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([0, 100, 0, 0, 100]), [PngFixture.Chunk("ABCD", [])])); }

    [TestMethod]
    public void MalformedChunkTypeIsRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([0, 100, 0, 0, 100]), [PngFixture.Chunk("a1CD", [])])); }

    [TestMethod]
    public void RepeatedHeaderIsRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([0, 100, 0, 0, 100]), [PngFixture.Chunk("IHDR", PngFixture.Header(1, 1))])); }

    [TestMethod]
    public void NonconsecutiveIdatIsRejected()
    { Reject(PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([0, 100, 0, 0, 100]), afterData:
        [PngFixture.Chunk("tEXt", "comment\0test"u8), PngFixture.Chunk("IDAT", [])])); }

    [TestMethod]
    public void EmptyAndExcessEncodedBytesAreRejected()
    { Reject([]); Reject(new byte[AccessoryPngNormalizer.MaximumFileBytes + 1]); }

    [TestMethod]
    public void MetadataIsStrippedWithoutDecompressingItsPayload()
    {
        // The compressed-text body is intentionally not a valid zlib stream, yet metadata never reaches the codec.
        var png = PngFixture.Assemble(PngFixture.Header(1, 1), PngFixture.Compress([0, 100, 0, 0, 100]),
            [PngFixture.Chunk("zTXt", [.. "description\0"u8, 0, 0xff, 0xff, 0xff])]);
        Assert.AreEqual(AccessoryPngNormalizer.Normalize(PngFixture.Rgba([100, 0, 0, 100], 1, 1)).Digest,
            AccessoryPngNormalizer.Normalize(png).Digest);
    }

    [TestMethod]
    public void PaletteTransparencyDecodesToEquivalentRgba()
    {
        var indexed = PngFixture.Assemble(PngFixture.Header(2, 1, 8, 3), PngFixture.Compress([0, 0, 1]),
            [PngFixture.Chunk("PLTE", [255, 0, 0, 1, 2, 3]), PngFixture.Chunk("tRNS", [255, 0])]);
        Assert.AreEqual(AccessoryPngNormalizer.Normalize(PngFixture.Rgba([255, 0, 0, 255, 0, 0, 0, 0], 2, 1)).Digest,
            AccessoryPngNormalizer.Normalize(indexed).Digest);
    }

    [TestMethod]
    public async Task ClaimedSourceHashCannotSelectOrOverwriteBlob()
    {
        using var fixture = new BlobDirectory(); var png = PngFixture.Rgba();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Store.ImportAsync(new MemoryStream(png), new string('a', 64)));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
        var original = await fixture.Store.ImportAsync(new MemoryStream(png));
        var altered = (byte[])PngFixture.Pixels.Clone(); altered[0] = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Store.ImportAsync(new MemoryStream(PngFixture.Rgba(altered)), original.SourceDigest));
        Assert.AreEqual(original.BlobDigest, (await fixture.Store.ReadAsync(original.BlobDigest)).Digest);
        Assert.AreEqual(1, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task ImportReturnsVerifiedSourceToCanonicalMapping()
    {
        using var fixture = new BlobDirectory(); var png = PngFixture.Rgba(metadata: true);
        var result = await fixture.Store.ImportAsync(new MemoryStream(png), Hash(png));
        Assert.AreEqual(Hash(png), result.SourceDigest); Assert.AreNotEqual(result.SourceDigest, result.BlobDigest);
        var restored = await fixture.Store.ReadAsync(result.BlobDigest);
        Assert.AreEqual(result.BlobDigest, restored.Digest);
        Assert.AreEqual(1, result.NormalizationVersion); Assert.AreEqual(result.NormalizationVersion, restored.NormalizationVersion);
    }

    [TestMethod]
    public async Task ConcurrentEquivalentImportsPublishOneCompleteFile()
    {
        using var fixture = new BlobDirectory();
        var imports = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => fixture.Store.ImportAsync(
            new MemoryStream(PngFixture.Rgba(metadata: i % 2 == 0, compression: i % 2 == 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal)))));
        Assert.AreEqual(1, imports.Count(i => i.Created)); Assert.AreEqual(1, imports.Select(i => i.BlobDigest).Distinct().Count());
        Assert.AreEqual(1, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
        Assert.AreEqual(imports[0].BlobDigest, (await fixture.Store.ReadAsync(imports[0].BlobDigest)).Digest);
    }

    [TestMethod]
    public async Task PartialWriteFaultDoesNotPublishOrDamageExistingObject()
    {
        using var fixture = new BlobDirectory(); var first = await fixture.Store.ImportAsync(new MemoryStream(PngFixture.Rgba()));
        var changed = (byte[])PngFixture.Pixels.Clone(); changed[0] = 0;
        var failing = new FileAccessoryBlobStore(fixture.Path, stage => { if (stage == BlobWriteStage.AfterPartialWrite) throw new IOException("injected partial write failure"); });
        await Assert.ThrowsExactlyAsync<IOException>(() => failing.ImportAsync(new MemoryStream(PngFixture.Rgba(changed))));
        Assert.AreEqual(first.BlobDigest, (await fixture.Store.ReadAsync(first.BlobDigest)).Digest);
        Assert.AreEqual(1, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task PublishFaultLeavesNoTemporaryFile()
    {
        using var fixture = new BlobDirectory();
        var failing = new FileAccessoryBlobStore(fixture.Path, stage => { if (stage == BlobWriteStage.BeforePublish) throw new IOException("injected publish failure"); });
        await Assert.ThrowsExactlyAsync<IOException>(() => failing.ImportAsync(new MemoryStream(PngFixture.Rgba())));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task CorruptedStoredFileIsRejectedAndNeverOverwritten()
    {
        using var fixture = new BlobDirectory(); var png = PngFixture.Rgba(); var imported = await fixture.Store.ImportAsync(new MemoryStream(png));
        var path = Directory.GetFiles(fixture.Path, "*.png", SearchOption.AllDirectories).Single(); File.WriteAllBytes(path, [0, 1]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Store.ReadAsync(imported.BlobDigest));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Store.ImportAsync(new MemoryStream(png)));
        CollectionAssert.AreEqual(new byte[] { 0, 1 }, File.ReadAllBytes(path));
    }

    [TestMethod]
    public async Task InvalidDigestCannotTraverseStorage()
    {
        using var fixture = new BlobDirectory();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Store.ReadAsync("../../private.png"));
    }

    [TestMethod]
    public async Task CancelledImportDoesNotPublishFiles()
    {
        using var fixture = new BlobDirectory(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Store.ImportAsync(new MemoryStream(PngFixture.Rgba()), cancellationToken: cancelled.Token));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task NonseekableOversizedUploadStopsAtReadBudget()
    {
        using var fixture = new BlobDirectory(); using var endless = new EndlessStream();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Store.ImportAsync(endless));
        Assert.IsTrue(endless.BytesRead <= AccessoryPngNormalizer.MaximumFileBytes + 8192L);
        Assert.AreEqual(0, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task CancellationBeforePublishKeepsPreviouslyCommittedBlob()
    {
        using var fixture = new BlobDirectory(); var original = await fixture.Store.ImportAsync(new MemoryStream(PngFixture.Rgba()));
        using var cancelled = new CancellationTokenSource();
        var store = new FileAccessoryBlobStore(fixture.Path, stage => { if (stage == BlobWriteStage.AfterPartialWrite) cancelled.Cancel(); });
        var changed = (byte[])PngFixture.Pixels.Clone(); changed[0] = 100;
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.ImportAsync(new MemoryStream(PngFixture.Rgba(changed)), cancellationToken: cancelled.Token));
        Assert.AreEqual(original.BlobDigest, (await fixture.Store.ReadAsync(original.BlobDigest)).Digest);
        Assert.AreEqual(1, Directory.GetFiles(fixture.Path, "*", SearchOption.AllDirectories).Length);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Reject(byte[] png) => Assert.ThrowsExactly<InvalidDataException>(() => AccessoryPngNormalizer.Normalize(png));

    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { buffer.AsSpan(offset, count).Clear(); BytesRead += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); buffer.Span.Clear(); BytesRead += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlobDirectory : IDisposable
    {
        private readonly string basePath = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public string Path { get; }
        public FileAccessoryBlobStore Store { get; }
        public BlobDirectory()
        {
            Path = System.IO.Path.Combine(basePath, "Dango.AccessoryAssets.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path); Store = new(Path);
        }
        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (System.IO.Path.GetDirectoryName(resolved) != System.IO.Path.TrimEndingDirectorySeparator(basePath) ||
                !System.IO.Path.GetFileName(resolved).StartsWith("Dango.AccessoryAssets.Tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the test directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
