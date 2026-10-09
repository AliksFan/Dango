using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Client.Sync.Accessories;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.AccountSync;
using Dango.Protocol.Accessories;

namespace Dango.Desktop.Tests.Accessories.AccountSync;

[TestClass]
public sealed class AccessoryAccountLocalRepositoryTests
{
    private string root = null!;
    private string settingsRoot = null!;

    [TestInitialize]
    public void CreateRoot()
    {
        root = Path.Combine(Path.GetTempPath(), "Dango-account-accessory-local", Guid.NewGuid().ToString("N"));
        settingsRoot = Path.Combine(root, "settings");
        Directory.CreateDirectory(root);
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void SnapshotReadsCommittedEquippedImagesOnceAndExcludesDraftLibraryAndPaths()
    {
        var session = new AccessorySettingsSession(settingsRoot);
        var first = session.ImportPng(WritePng("fictional-first.png", 19));
        var duplicate = session.Duplicate(first.Id);
        var unequipped = session.ImportPng(WritePng("fictional-library.png", 42));
        session.Stage(new(session.DraftConfiguration.Items.Select(item => item.Id == unequipped.Id
            ? item with { IsEquipped = false } : item)));
        Assert.IsTrue(session.TrySave());
        var repo = new AccessoryAccountLocalRepository(session);
        var draft = session.ImportPng(WritePng("fictional-draft.png", 88));
        var snapshot = repo.CaptureCommittedAsync().GetAwaiter().GetResult();
        Assert.AreEqual(3, snapshot.Outfit.Items.Count);
        Assert.IsTrue(snapshot.Outfit.Items.Any(item => item.Id == first.Id));
        Assert.IsTrue(snapshot.Outfit.Items.Any(item => item.Id == duplicate.Id));
        Assert.IsFalse(snapshot.Outfit.Items.Any(item => item.Id == unequipped.Id || item.Id == draft.Id));
        Assert.AreEqual(1, snapshot.Images.Count, "Two instances share one image upload.");
        var bytes = snapshot.Images[0].PngBytes.ToArray();
        Assert.AreEqual(first.AssetId, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        CollectionAssert.AreEqual(File.ReadAllBytes(AccessorySettingsStore.GetAssetPath(settingsRoot, first.AssetId)), bytes);
        Assert.AreEqual(64, snapshot.Outfit.Items.Single(item => AccessoryItem.IsBuiltInAsset(item.AssetId)).PixelWidth);
        var json = JsonSerializer.Serialize(snapshot.Outfit);
        Assert.IsFalse(json.Contains(root, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("fictional-draft", StringComparison.Ordinal));
        Assert.IsTrue(session.IsDirty);
    }

    [TestMethod]
    public void CaptureRejectsCompatibilityUnsavedExternalChangesAndCorruptionHiddenByPresentationCache()
    {
        var session = new AccessorySettingsSession(settingsRoot);
        var repo = new AccessoryAccountLocalRepository(session);
        Assert.ThrowsExactly<InvalidDataException>(() => repo.CaptureCommittedAsync().GetAwaiter().GetResult());
        var item = session.ImportPng(WritePng("fictional.png", 19));
        Assert.IsTrue(session.TrySave());
        Assert.IsNotNull(session.TryGetImage(item.AssetId));
        File.WriteAllText(AccessorySettingsStore.GetAssetPath(settingsRoot, item.AssetId), "corrupted committed blob");
        Assert.IsNotNull(session.TryGetImage(item.AssetId), "The UI can still have its frozen cached bitmap.");
        Assert.ThrowsExactly<InvalidDataException>(() => repo.CaptureCommittedAsync().GetAwaiter().GetResult());
        new AccessorySettingsStore().Save(settingsRoot, AccessoryConfiguration.Empty);
        Assert.ThrowsExactly<InvalidDataException>(() => repo.CaptureCommittedAsync().GetAwaiter().GetResult());
    }

    [TestMethod]
    public void MappingRoundTripPreservesEveryWearingParameterAndOldUnequippedLibrary()
    {
        var item = new AccessoryItem("fictional-equipped", "虚构配饰", "builtin-top-hat", 64, 64)
        {
            Layer = AccessoryLayer.Behind, Order = 32, SizePercent = 371, BaseAngleDegrees = -159,
            ImagePivotX = .17, ImagePivotY = .61, BodyAnchorX = .78, BodyAnchorY = .23,
            Softness = 93, AllowFolding = true, MaxDeformationPercent = 187, ConnectionStrength = 27,
            MaxConnectionTravelPercent = 183, RotationDamping = 81, LockRotation = false,
            ReturnStrength = 7, MaxSwingDegrees = 167, LimitSwing = false, GravityInfluence = 73,
        };
        var old = new AccessoryItem("fictional-previous", "之前佩戴", "builtin-sprout", 64, 64);
        var library = new AccessoryItem("fictional-library", "未佩戴库", "builtin-bowler", 64, 64) { IsEquipped = false };
        var outfit = AccessoryOutfitMapper.FromSavedConfiguration(new([item, library]));
        Assert.AreEqual(1, outfit.Items.Count);
        var restored = AccessoryOutfitMapper.MergeRestoredOutfit(new([old, library]), outfit);
        Assert.AreEqual(item, restored.Items.Single(entry => entry.Id == item.Id));
        Assert.AreEqual(library, restored.Items.Single(entry => entry.Id == library.Id));
        Assert.AreEqual(old with { IsEquipped = false }, restored.Items.Single(entry => entry.Id == old.Id));
    }

    [TestMethod]
    public void MappingRejectsRemoteIdCollisionWithUnequippedLocalContentWithoutChangingIt()
    {
        var item = new AccessoryItem("fictional-conflict", "本地库", "builtin-sprout", 64, 64) { IsEquipped = false };
        var saved = new AccessoryConfiguration([item]);
        var outfit = new AccessoryOutfitManifest(1, [new(item.Id, "远端配饰", "builtin-top-hat", 64, 64)]);
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryOutfitMapper.MergeRestoredOutfit(saved, outfit));
        Assert.AreEqual(item, saved.Items.Single());
    }

    [TestMethod]
    public void StageRejectsPngWithCorrectHashAndHeaderButInvalidCrcBeforeAnyActiveWrites()
    {
        var session = new AccessorySettingsSession(settingsRoot);
        Assert.IsTrue(session.TrySave());
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot));
        var normalized = AccessoryImageAsset.ImportPng(WritePng("fictional-invalid.png", 19)).CopyPngBytes();
        normalized[^1] ^= 1;
        var id = Convert.ToHexStringLower(SHA256.HashData(normalized));
        var image = new AccessoryImageSnapshot(id, 3, 2, normalized);
        var snapshot = new AccessoryLocalSnapshot(new(1, [new("fictional-remote", "远端", id, 3, 2)]), [image], 0);
        var repo = new AccessoryAccountLocalRepository(session);
        var before = Directory.GetDirectories(Path.GetTempPath(), "Dango-accessory-restore-*").ToHashSet(StringComparer.Ordinal);
        Assert.ThrowsExactly<InvalidDataException>(() => repo.StageRestoreAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot)));
        Assert.IsFalse(Directory.Exists(Path.Combine(settingsRoot, "accessory-assets")));
        CollectionAssert.AreEquivalent(before.ToArray(), Directory.GetDirectories(Path.GetTempPath(), "Dango-accessory-restore-*"));
    }

    [TestMethod]
    public void StageValidationAndDisposalPreserveActiveManifestAndNeverCreateActiveAssets()
    {
        var session = new AccessorySettingsSession(settingsRoot);
        Assert.IsTrue(session.TrySave());
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot));
        var asset = AccessoryImageAsset.ImportPng(WritePng("fictional-stage.png", 27));
        var image = new AccessoryImageSnapshot(asset.AssetId, asset.PixelWidth, asset.PixelHeight, asset.CopyPngBytes());
        var snapshot = new AccessoryLocalSnapshot(new(1, [new("fictional-remote", "远端", asset.AssetId, 3, 2)]), [image], 0);
        var repo = new AccessoryAccountLocalRepository(session);
        var stage = repo.StageRestoreAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult();
        CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot)));
        Assert.IsFalse(Directory.Exists(Path.Combine(settingsRoot, "accessory-assets")));
        stage.DisposeAsync().GetAwaiter().GetResult();
        stage.DisposeAsync().GetAwaiter().GetResult();
        Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsStore().Load(settingsRoot).Configuration);
    }

    [TestMethod]
    public void ExpectedLoadGuardDetectsExternalCommitDuringAssetWritesAndPreservesIt()
    {
        var normal = new AccessorySettingsStore();
        var baseline = AccessoryConfiguration.FromLegacyHat("sprout");
        normal.Save(settingsRoot, baseline);
        var expected = normal.Load(settingsRoot);
        var external = AccessoryConfiguration.Empty;
        var asset = AccessoryImageAsset.ImportPng(WritePng("fictional-race.png", 55));
        var item = new AccessoryItem("fictional-remote", "远端", asset.AssetId, asset.PixelWidth, asset.PixelHeight);
        var injected = new AccessorySettingsStore(assetWriter: (path, bytes) =>
        {
            normal.Save(settingsRoot, external);
            File.WriteAllBytes(path, bytes);
        });
        Assert.ThrowsExactly<InvalidDataException>(() => injected.Save(settingsRoot, new([item]),
            new Dictionary<string, AccessoryImageAsset> { [asset.AssetId] = asset }, expectedLoad: expected));
        Assert.AreEqual(external, normal.Load(settingsRoot).Configuration);
    }

    [TestMethod]
    public void PartialStagingWriteFailureRemovesOnlyItsFileAndPreservesTheOriginalErrorAndManifest()
    {
        var session = new AccessorySettingsSession(settingsRoot);
        Assert.IsTrue(session.TrySave());
        var original = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot));
        var asset = AccessoryImageAsset.ImportPng(WritePng("fictional-partial.png", 67));
        var image = new AccessoryImageSnapshot(asset.AssetId, 3, 2, asset.CopyPngBytes());
        var snapshot = new AccessoryLocalSnapshot(new(1, [new("fictional-remote", "远端", asset.AssetId, 3, 2)]), [image], 0);
        string? partial = null;
        var repo = new AccessoryAccountLocalRepository(session, null, path =>
        {
            partial = path;
            return new PartialFailureStream(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));
        });
        var error = Assert.ThrowsExactly<IOException>(() => repo.StageRestoreAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult());
        Assert.AreEqual("Injected partial staging write.", error.Message);
        Assert.IsNotNull(partial);
        Assert.IsFalse(File.Exists(partial));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(partial)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(settingsRoot)));
        Assert.IsFalse(Directory.Exists(Path.Combine(settingsRoot, "accessory-assets")));
    }

    private sealed class PartialFailureStream(Stream output) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => output.Length;
        public override long Position { get => output.Position; set => throw new NotSupportedException(); }
        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            output.Write(buffer[..Math.Max(1, buffer.Length / 2)]);
            throw new IOException("Injected partial staging write.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) output.Dispose();
            base.Dispose(disposing);
        }
    }

    private string WritePng(string name, byte marker)
    {
        var path = Path.Combine(root, name);
        var pixels = new byte[] { 0, 0, 0, 0, marker, 90, 150, 255, 99, 77, 30, 128,
            40, 60, 70, 255, 0, 0, 0, 0, 1, 2, 3, 64 };
        var bitmap = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }
}
