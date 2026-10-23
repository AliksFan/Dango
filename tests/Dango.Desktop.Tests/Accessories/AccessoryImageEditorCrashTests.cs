using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Import failures exercise the real Create action without opening a window or browser.</summary>
[TestClass]
public sealed class AccessoryImageEditorCrashTests
{
    [STATestMethod]
    public void OversizedPngSelectedFromCreateIsReportedWithoutOpeningTheUnifiedEditorOrChangingTheDraft()
    {
        var run = Path.Combine(TestRepository.Root, "artifacts", "accessory-import-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var source = WritePng(run, 16385, 8);
        var root = Path.Combine(run, "review");
        var session = new AccessorySettingsSession(root);
        var panel = new AccessoryEditorPanel(); panel.Bind(session); panel.ImagePicker = () => source;
        var before = session.DraftConfiguration; var sourceBytes = File.ReadAllBytes(source);
        var opened = false;
        panel.UnifiedEditor = (_, _, _, _) => { opened = true; return null; };
        Assert.IsFalse(typeof(IOException).IsAssignableFrom(typeof(InvalidDataException)),
            "InvalidDataException must be caught explicitly rather than relying on the IOException filter.");

        ((Panel)panel.ActionTools).Children.OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == "创建新配饰").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.AreEqual(before, session.DraftConfiguration);
        Assert.AreEqual(before, session.SavedConfiguration); Assert.IsFalse(session.IsDirty);
        Assert.IsFalse(opened, "Rejected source dimensions must fail before opening the unified editor.");
        StringAssert.Contains(panel.StatusMessage, "操作未完成"); StringAssert.Contains(panel.StatusMessage, "16384");
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
