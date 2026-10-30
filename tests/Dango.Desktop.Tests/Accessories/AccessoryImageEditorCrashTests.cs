using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Source import failures preserve the current draft without opening a window or browser.</summary>
[TestClass]
public sealed class AccessoryImageEditorCrashTests
{
    [STATestMethod]
    public void OversizedPngImportIsRejectedWithoutChangingTheDraft()
    {
        var run = Path.Combine(TestRepository.Root, "artifacts", "accessory-import-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var source = WritePng(run, 16385, 8);
        var root = Path.Combine(run, "review");
        var session = new AccessorySettingsSession(root);
        var before = session.DraftConfiguration; var sourceBytes = File.ReadAllBytes(source);
        Assert.IsFalse(typeof(IOException).IsAssignableFrom(typeof(InvalidDataException)),
            "InvalidDataException must be caught explicitly rather than relying on the IOException filter.");

        var error = Assert.ThrowsExactly<InvalidDataException>(() => session.ImportSource(source));

        Assert.AreEqual(before, session.DraftConfiguration);
        Assert.AreEqual(before, session.SavedConfiguration); Assert.IsFalse(session.IsDirty);
        StringAssert.Contains(error.Message, "16384");
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(root)));
        CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(source));
    }

    private static string WritePng(string run, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var index = (y * width + x) * 4;
                pixels[index] = 170;
                pixels[index + 1] = 90;
                pixels[index + 2] = 50;
                pixels[index + 3] = x < width / 3 ? (byte)128 : (byte)255;
            }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var path = Path.Combine(run, $"fictional-transparent-{width}x{height}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

}
