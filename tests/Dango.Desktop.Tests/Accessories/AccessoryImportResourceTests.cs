using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
[DoNotParallelize]
public sealed class AccessoryImportResourceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DefaultPendingBudgetCanStageMultipleLargeImagesAndDeduplicatesSharedPixels()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "accessory-import-resource-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var session = new AccessorySettingsSession(Path.Combine(root, "settings"));
        Assert.IsGreaterThanOrEqualTo(AccessoryConfiguration.MaximumEquippedItems * 16_000_000L,
            AccessorySettingsSession.MaximumPendingPixels,
            "The cumulative default budget must allow eight legal maximum-sized images.");
        Assert.IsGreaterThanOrEqualTo(AccessoryConfiguration.MaximumEquippedItems * 32L * 1024 * 1024,
            AccessorySettingsSession.MaximumPendingPngBytes);
        var items = Enumerable.Range(0, 3).Select(index => session.ImportPng(WriteImage(root, index))).ToArray();
        Assert.AreEqual(36_000_000L, session.PendingPixelCount,
            "A third useful high-resolution image must not be rejected by the obsolete 32M budget.");
        Assert.AreEqual(3, session.PendingImageCount);
        session.Duplicate(items[0].Id);
        Assert.AreEqual(3, session.PendingImageCount);
        Assert.AreEqual(36_000_000L, session.PendingPixelCount);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "settings")));
        session.CancelPreview();
        Assert.AreEqual(0, session.PendingImageCount);
        Assert.AreEqual(0L, session.PendingPixelCount);
    }

    [TestMethod]
    public void BuiltinHighResolutionTextureKeepsLegacyIdsAndLogicalDimensionsAfterReload()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "accessory-import-resource-evidence", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root, "top-hat");
        var expected = session.SavedConfiguration;
        Assert.IsTrue(session.TrySave());
        var reopened = new AccessorySettingsSession(root, "bowler");
        Assert.AreEqual(expected, reopened.SavedConfiguration);
        Assert.IsTrue(reopened.SavedConfiguration.Items.All(item => item.PixelWidth == 64 && item.PixelHeight == 64));
        Assert.AreEqual(.9375, reopened.SavedConfiguration.Items.Single(item => item.IsEquipped).ImagePivotY);
    }

    [TestMethod]
    public void SessionImportInfoDescribesOnlyItsLatestSuccessAndIsNotPersisted()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "accessory-import-resource-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsRoot = Path.Combine(root, "settings");
        var session = new AccessorySettingsSession(settingsRoot);
        Assert.IsNull(session.LastImportInfo);
        var pixels = new byte[] { 0, 0, 0, 0, 20, 40, 80, 128 };
        var bitmap = BitmapSource.Create(2, 1, 192, 192, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(root, "fictional-small.png");
        using (var stream = File.Create(path)) encoder.Save(stream);
        var imported = session.ImportPng(path);
        var info = session.LastImportInfo;
        Assert.IsNotNull(info);
        Assert.AreEqual(2, info.OriginalWidth); Assert.AreEqual(1, info.OriginalHeight);
        Assert.AreEqual(imported.PixelWidth, info.PixelWidth); Assert.AreEqual(imported.PixelHeight, info.PixelHeight);
        Assert.AreEqual(new FileInfo(path).Length, info.OriginalFileBytes);
        Assert.IsNull(info.Notice);
        Assert.IsTrue(session.TrySave());
        var manifest = File.ReadAllText(AccessorySettingsStore.GetManifestPath(settingsRoot));
        Assert.IsFalse(manifest.Contains("originalWidth", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifest.Contains("originalFileBytes", StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(new AccessorySettingsSession(settingsRoot).LastImportInfo);
        var before = session.DraftConfiguration;
        var invalid = Path.Combine(root, "fictional-invalid.png");
        File.WriteAllText(invalid, "invalid fictional PNG");
        Assert.ThrowsExactly<InvalidDataException>(() => session.ImportPng(invalid));
        Assert.IsNull(session.LastImportInfo, "A failed attempt must not show the previous image's conversion message.");
        Assert.AreEqual(before, session.DraftConfiguration);
    }

    private static string WriteImage(string root, int marker)
    {
        const int width = 4000, height = 3000;
        var pixels = new byte[width * height * 4];
        for (var index = pixels.Length / 2; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)(40 + marker);
            pixels[index + 1] = 140; pixels[index + 2] = 230; pixels[index + 3] = 128;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(root, "fictional-large-" + marker + ".png");
        using var stream = File.Create(path); encoder.Save(stream);
        return path;
    }
}
