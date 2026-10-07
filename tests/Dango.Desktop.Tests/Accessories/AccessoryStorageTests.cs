using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryStorageTests
{
    private string root = null!;

    [TestInitialize]
    public void CreateTemporaryRoot() => root = Path.Combine(Path.GetTempPath(), "Dango-accessory-storage-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void RemoveTemporaryRoot()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void LegacyCompatibilityIsMemoryOnlyAndExplicitEmptySaveStaysEmpty()
    {
        var session = new AccessorySettingsSession(root, "top-hat");
        Assert.IsTrue(session.IsCompatibilityMode);
        Assert.AreEqual("builtin-top-hat", session.SavedConfiguration.Items.Single(item => item.IsEquipped).AssetId);
        Assert.AreEqual(3, session.SavedConfiguration.Items.Count);
        Assert.AreEqual("builtin-top-hat", session.SavedConfiguration.Items[0].AssetId);
        Assert.AreEqual(.9375, session.SavedConfiguration.Items[0].ImagePivotY);
        Assert.IsFalse(Directory.Exists(root));

        session.Stage(AccessoryConfiguration.Empty);
        session.CancelPreview();
        Assert.AreEqual("builtin-top-hat", session.DraftConfiguration.Items.Single(item => item.IsEquipped).AssetId);
        Assert.IsFalse(Directory.Exists(root));
        session.Stage(AccessoryConfiguration.Empty);
        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.IsCompatibilityMode);
        var reopened = new AccessorySettingsSession(root, "bowler");
        Assert.IsFalse(reopened.IsCompatibilityMode);
        Assert.AreEqual(0, reopened.SavedConfiguration.Items.Count);
    }

    [TestMethod]
    public void ImportCancelAndDuplicateKeepSourceAndSavedStateIndependent()
    {
        var source = WriteSource("source.png");
        var sourceBytes = File.ReadAllBytes(source);
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"));
        var imported = session.ImportPng(source);
        var copy = session.Duplicate(imported.Id);
        Assert.AreEqual(imported.AssetId, copy.AssetId);
        Assert.AreNotEqual(imported.Id, copy.Id);
        session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(item =>
            item.Id == copy.Id ? item with { BaseAngleDegrees = 71, Softness = 85, ImagePivotX = .2 } : item)));
        Assert.AreEqual(0, session.DraftConfiguration.Items.Single(item => item.Id == imported.Id).Softness);
        Assert.IsNotNull(session.TryGetImage(imported.AssetId));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "settings")));
        CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(source));
        session.CancelPreview();
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.IsFalse(session.IsDirty);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "settings")));
    }

    [TestMethod]
    public void SavingNormalizesIndependentAssetsAndRoundTripsEveryParameter()
    {
        var source = WriteSource("private-source.png");
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        var imported = session.ImportPng(source);
        var changed = imported with
        {
            Name = "虚构配饰",
            Layer = AccessoryLayer.Behind,
            Order = 4,
            SizePercent = 233,
            BaseAngleDegrees = -120,
            ImagePivotX = .17,
            ImagePivotY = .9,
            BodyAnchorX = .6,
            BodyAnchorY = .75,
            Softness = 87,
            ConnectionStrength = 34,
            RotationDamping = 29,
            LockRotation = false,
            ReturnStrength = 9,
            MaxSwingDegrees = 81,
            LimitSwing = false,
            GravityInfluence = 64,
        };
        session.Stage(new AccessoryConfiguration([changed]));
        Assert.IsTrue(session.TrySave());
        File.Move(source, Path.Combine(root, "moved-source.png"));

        var reopened = new AccessorySettingsSession(settingsRoot, "bowler");
        Assert.AreEqual(session.SavedConfiguration, reopened.SavedConfiguration);
        Assert.AreEqual(changed, reopened.SavedConfiguration.Items.Single());
        var image = reopened.TryGetImage(changed.AssetId);
        Assert.IsNotNull(image);
        Assert.AreEqual(3, image.PixelWidth);
        Assert.AreEqual(2, image.PixelHeight);
        var manifest = File.ReadAllText(Path.Combine(settingsRoot, "accessories.json"));
        Assert.IsFalse(manifest.Contains(source, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("private-source", StringComparison.Ordinal));
        Assert.IsFalse(manifest.Contains("category", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("velocity", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(settingsRoot, "accessory-assets"), "*.png").Length);
    }

    [TestMethod]
    public void ImageWriteFailureKeepsManifestAndDraftUntilRetry()
    {
        var settingsRoot = Path.Combine(root, "settings");
        new AccessorySettingsSession(settingsRoot).TrySave();
        var original = File.ReadAllBytes(Path.Combine(settingsRoot, "accessories.json"));
        var fail = true;
        var store = new AccessorySettingsStore(assetWriter: (path, bytes) =>
        {
            if (fail) throw new IOException("Injected asset write failure.");
            File.WriteAllBytes(path, bytes);
        });
        var session = new AccessorySettingsSession(settingsRoot, store: store);
        var imported = session.ImportPng(WriteSource("image.png"));
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.IsDirty);
        Assert.IsFalse(session.SavedConfiguration.Items.Any(item => item.Id == imported.Id));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(settingsRoot, "accessories.json")));
        Assert.IsNotNull(session.TryGetImage(imported.AssetId));
        fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsSession(settingsRoot).SavedConfiguration);
    }

    [TestMethod]
    public void ManifestWriteFailureLeavesOldConfigurationAndCanCancelOrRetry()
    {
        var settingsRoot = Path.Combine(root, "settings");
        Assert.IsTrue(new AccessorySettingsSession(settingsRoot).TrySave());
        var original = File.ReadAllBytes(Path.Combine(settingsRoot, "accessories.json"));
        var writer = new ToggleWriter { Fail = true };
        var session = new AccessorySettingsSession(settingsRoot, store: new AccessorySettingsStore(writer));
        var imported = session.ImportPng(WriteSource("image.png"));
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.IsDirty);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(settingsRoot, "accessories.json")));
        Assert.IsTrue(File.Exists(Path.Combine(settingsRoot, "accessory-assets", imported.AssetId + ".png")));
        Assert.AreEqual(session.SavedConfiguration, new AccessorySettingsSession(settingsRoot).SavedConfiguration);
        session.CancelPreview();
        Assert.AreEqual(session.SavedConfiguration, session.DraftConfiguration);
        Assert.IsFalse(session.IsDirty);
        Assert.IsTrue(File.Exists(Path.Combine(settingsRoot, "accessory-assets", imported.AssetId + ".png")));
        session.ImportPng(WriteSource("second.png"));
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
    }

    [TestMethod]
    public void CorruptedOrFutureManifestIsProtectedAndNeverFallsBackToLegacyHat()
    {
        Directory.CreateDirectory(root);
        foreach (var original in new[] { "{broken", "{\"accessorySchemaVersion\":999,\"items\":[]}",
            "{\"accessorySchemaVersion\":1,\"items\":[],\"unknown\":true}",
            "{\"accessorySchemaVersion\":1,\"accessorySchemaVersion\":1,\"items\":[]}" })
        {
            File.WriteAllText(Path.Combine(root, "accessories.json"), original);
            var session = new AccessorySettingsSession(root, "bowler");
            Assert.IsTrue(session.IsWriteProtected);
            Assert.IsFalse(session.IsCompatibilityMode);
            Assert.AreEqual(0, session.SavedConfiguration.Items.Count);
            session.Stage(AccessoryConfiguration.FromLegacyHat("top-hat"));
            Assert.IsFalse(session.TrySave());
            Assert.AreEqual(original, File.ReadAllText(Path.Combine(root, "accessories.json")));
        }
    }

    [TestMethod]
    public void DamagedImageIsIsolatedPreservedThroughOtherEditsAndRecoverable()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        var imported = session.ImportPng(WriteSource("image.png"));
        Assert.IsTrue(session.TrySave());
        var assetPath = Path.Combine(settingsRoot, "accessory-assets", imported.AssetId + ".png");
        var originalImage = File.ReadAllBytes(assetPath);
        File.WriteAllText(assetPath, "not an image");
        var reopened = new AccessorySettingsSession(settingsRoot);
        Assert.IsFalse(reopened.IsCompatibilityMode);
        Assert.IsFalse(reopened.IsWriteProtected);
        Assert.IsNull(reopened.TryGetImage(imported.AssetId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(reopened.StatusMessage));
        reopened.Stage(new AccessoryConfiguration(reopened.DraftConfiguration.Items.Select(item =>
            item.AssetId.StartsWith("builtin-", StringComparison.Ordinal) ? item with { Name = "另一个虚构配饰" } : item)));
        Assert.IsTrue(reopened.TrySave());
        Assert.IsTrue(new AccessorySettingsSession(settingsRoot).SavedConfiguration.Items.Any(item => item.Id == imported.Id));
        File.WriteAllBytes(assetPath, originalImage);
        Assert.IsNotNull(reopened.TryGetImage(imported.AssetId));
    }

    [TestMethod]
    public void StrictConfigurationRejectsUnsafeReferencesAndNonFiniteOrOutOfRangeParameters()
    {
        var baseline = AccessoryConfiguration.FromLegacyHat("sprout").Items.Single(item => item.IsEquipped);
        foreach (var invalid in new[]
        {
            baseline with { AssetId = "../source" }, baseline with { ImagePivotX = double.NaN },
            baseline with { BodyAnchorY = 1.01 }, baseline with { SizePercent = 401 },
            baseline with { Softness = -1 }, baseline with { MaxSwingDegrees = 181 },
            baseline with { PixelWidth = 2049 }, baseline with { Order = -1 },
        })
            Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryConfiguration([invalid]));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryConfiguration([baseline, baseline]));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryConfiguration(
            Enumerable.Range(0, 9).Select(index => baseline with { Id = "item-" + index })));
    }

    [TestMethod]
    public void AManifestThatBecomesProtectedAfterStartupCannotBeOverwritten()
    {
        var session = new AccessorySettingsSession(root);
        Directory.CreateDirectory(root);
        const string future = "{\"accessorySchemaVersion\":12,\"items\":[]}";
        File.WriteAllText(Path.Combine(root, "accessories.json"), future);
        session.Stage(AccessoryConfiguration.Empty);
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.IsWriteProtected);
        Assert.AreEqual(future, File.ReadAllText(Path.Combine(root, "accessories.json")));
    }

    [TestMethod]
    public void RealWindowsFileLockRejectsSaveAndUnlockPermitsRetry()
    {
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var path = Path.Combine(root, "accessories.json");
        var original = File.ReadAllBytes(path);
        session.Stage(AccessoryConfiguration.Empty);
        using (var fileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.IsFalse(session.TrySave());
            Assert.IsFalse(session.IsWriteProtected);
            Assert.IsTrue(session.IsDirty);
            Assert.AreEqual(1, session.SavedConfiguration.Items.Count(item => item.IsEquipped));
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(0, new AccessorySettingsSession(root).SavedConfiguration.Items.Count);
    }

    [TestMethod]
    public void SavedImagesAreLazyAndCacheEvictionAllowsDiskRecoveryWithoutDroppingEntries()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        var imported = Enumerable.Range(0, 18).Select(index =>
            session.ImportPng(WriteSource("cache-" + index + ".png", (byte)index))).ToArray();
        Assert.AreEqual(18, session.PendingImageCount);
        Assert.AreEqual(0, session.CachedImageCount, "Pending images must not also occupy the decoded disk cache.");
        session.Stage(new AccessoryConfiguration(session.DraftConfiguration.Items.Select(item => item with { IsEquipped = false })));
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.AreEqual(16, session.CachedImageCount);
        var reopened = new AccessorySettingsSession(settingsRoot);
        Assert.AreEqual(0, reopened.CachedImageCount, "Opening a library must not eagerly decode its images.");
        foreach (var item in imported)
        {
            Assert.IsNotNull(reopened.TryGetImage(item.AssetId));
            Assert.IsTrue(reopened.CachedImageCount <= 16);
        }
        var firstPath = AccessorySettingsStore.GetAssetPath(settingsRoot, imported[0].AssetId);
        var original = File.ReadAllBytes(firstPath);
        File.WriteAllText(firstPath, "damaged after eviction");
        Assert.IsNull(reopened.TryGetImage(imported[0].AssetId), "The evicted first image must be read again, rather than remain retained forever.");
        Assert.AreEqual(21, reopened.DraftConfiguration.Items.Count);
        Assert.AreEqual(reopened.SavedConfiguration, reopened.DraftConfiguration);
        Assert.IsFalse(reopened.IsDirty);
        Assert.IsFalse(reopened.StatusMessage.Contains("未保存", StringComparison.Ordinal));
        Assert.IsTrue(reopened.StatusMessage.Contains("配置仍保留", StringComparison.Ordinal));
        File.WriteAllBytes(firstPath, original);
        Assert.IsNotNull(reopened.TryGetImage(imported[0].AssetId));
        Assert.IsFalse(reopened.StatusMessage.Contains("隔离", StringComparison.Ordinal));
        Assert.AreEqual(16, reopened.CachedImageCount);
    }

    [TestMethod]
    public void LibraryBrowsingDoesNotEvictEquippedImagesFromTheBoundedCache()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        var imported = Enumerable.Range(0, 24).Select(index =>
            session.ImportPng(WriteSource("pinned-" + index + ".png", (byte)index))).ToArray();
        Assert.IsTrue(imported[0].IsEquipped);
        Assert.IsTrue(session.TrySave());
        foreach (var item in imported) Assert.IsNotNull(session.TryGetImage(item.AssetId));
        File.WriteAllText(AccessorySettingsStore.GetAssetPath(settingsRoot, imported[0].AssetId), "broken on disk");
        Assert.IsNotNull(session.TryGetImage(imported[0].AssetId), "Browsing the library must not repeatedly decode the displayed equipped image.");
        Assert.AreEqual(16, session.CachedImageCount);
    }

    [TestMethod]
    public void CumulativePixelBudgetRejectsNewImageKeepsDraftAndDeduplicatesSharedPixels()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot, "sprout", null, 12,
            AccessorySettingsSession.MaximumPendingPngBytes);
        var firstPath = WriteSource("budget-one.png", 1);
        session.ImportPng(firstPath);
        session.ImportPng(WriteSource("budget-two.png", 2));
        var before = session.DraftConfiguration;
        Assert.ThrowsExactly<InvalidDataException>(() => session.ImportPng(WriteSource("budget-three.png", 3)));
        Assert.AreEqual(before, session.DraftConfiguration);
        Assert.AreEqual(12L, session.PendingPixelCount);
        Assert.AreEqual(2, session.PendingImageCount);
        Assert.IsTrue(session.StatusMessage.Contains("先应用或取消", StringComparison.Ordinal));
        session.ImportPng(firstPath);
        Assert.AreEqual(2, session.PendingImageCount, "A shared immutable image consumes the budget once.");
        Assert.AreEqual(12L, session.PendingPixelCount);
        session.CancelPreview();
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.AreEqual(0L, session.PendingPixelCount);
        Assert.AreEqual(0, session.CachedImageCount);
        Assert.IsFalse(Directory.Exists(settingsRoot));
        session.ImportPng(firstPath);
        Assert.AreEqual(6L, session.PendingPixelCount);
    }

    [TestMethod]
    public void CumulativePngBudgetRejectsImportAtomicallyAndSavingReleasesThePendingBytes()
    {
        var first = WriteSource("byte-one.png", 1);
        var second = WriteSource("byte-two.png", 2);
        var firstBytes = AccessoryImageAsset.ImportPng(first).EncodedByteLength;
        var secondBytes = AccessoryImageAsset.ImportPng(second).EncodedByteLength;
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot, "sprout", null,
            AccessorySettingsSession.MaximumPendingPixels, firstBytes + secondBytes - 1);
        session.ImportPng(first);
        var before = session.DraftConfiguration;
        Assert.ThrowsExactly<InvalidDataException>(() => session.ImportPng(second));
        Assert.AreEqual(before, session.DraftConfiguration);
        Assert.AreEqual(firstBytes, session.PendingPngBytes);
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(0L, session.PendingPngBytes);
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.AreEqual(1, session.CachedImageCount);
        session.ImportPng(second);
        Assert.AreEqual(secondBytes, session.PendingPngBytes);
        Assert.AreEqual(1, session.PendingImageCount);
    }

    [TestMethod]
    public void ConfirmedCommitAfterWriterExceptionIsReportedAsSavedAndCancelCannotUndoIt()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot, store: new AccessorySettingsStore(new CommitThenThrowWriter()));
        var imported = session.ImportPng(WriteSource("commit.png"));
        var expected = session.DraftConfiguration;
        Assert.IsTrue(session.TrySave(), "The new manifest already exists and matches the requested snapshot.");
        Assert.AreEqual(expected, session.SavedConfiguration);
        Assert.IsFalse(session.IsDirty);
        Assert.IsFalse(session.IsCompatibilityMode);
        Assert.IsTrue(session.StatusMessage.Contains("已保存", StringComparison.Ordinal));
        Assert.IsTrue(session.SavedConfiguration.Items.Any(item => item.Id == imported.Id));
        session.CancelPreview();
        Assert.AreEqual(expected, session.DraftConfiguration);
        Assert.AreEqual(expected, new AccessorySettingsSession(settingsRoot).SavedConfiguration);
    }

    [TestMethod]
    public void CrossThreadSessionAccessFailsBeforeChangingDraftOrCacheButFrozenPixelsAreReadable()
    {
        var settingsRoot = Path.Combine(root, "settings");
        var source = WriteSource("thread.png");
        var session = new AccessorySettingsSession(settingsRoot);
        var item = session.ImportPng(source);
        var expected = session.DraftConfiguration;
        var bitmap = session.TryGetImage(item.AssetId)!;
        var errors = new List<Exception>();
        var pixels = new byte[24];
        var worker = new Thread(() =>
        {
            foreach (var action in new Action[]
            {
                () => session.Stage(AccessoryConfiguration.Empty), () => session.CancelPreview(),
                () => session.TrySave(), () => session.ImportPng(source),
                () => session.Duplicate(item.Id), () => session.TryGetImage(item.AssetId),
            })
            {
                try { action(); }
                catch (Exception error) { errors.Add(error); }
            }
            try { bitmap.CopyPixels(pixels, 12, 0); }
            catch (Exception error) { errors.Add(error); }
        })
        { IsBackground = true };
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(6, errors.Count);
        Assert.IsTrue(errors.All(error => error is InvalidOperationException));
        Assert.AreEqual(expected, session.DraftConfiguration);
        Assert.AreEqual(1, session.PendingImageCount);
        Assert.AreEqual(0, session.CachedImageCount);
        Assert.AreEqual(255, pixels[7]);
        Assert.IsFalse(Directory.Exists(settingsRoot));
    }

    private string WriteSource(string name, byte marker = 22)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        var pixels = new byte[] { 0, 0, 0, 0, marker, 90, 150, 255, 99, 77, 30, 128,
            40, 60, 70, 255, 0, 0, 0, 0, 1, 2, 3, 64 };
        var bitmap = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private sealed class ToggleWriter : IAtomicTextWriter
    {
        public bool Fail { get; set; }
        public void Write(string path, string content)
        {
            if (Fail) throw new IOException("Injected manifest failure.");
            new AtomicTextFileWriter().Write(path, content);
        }
    }

    private sealed class CommitThenThrowWriter : IAtomicTextWriter
    {
        public void Write(string path, string content)
        {
            new AtomicTextFileWriter().Write(path, content);
            throw new IOException("Injected error after commit.");
        }
    }
}
