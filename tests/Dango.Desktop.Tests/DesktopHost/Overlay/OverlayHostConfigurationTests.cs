using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Overlay;

[TestClass]
public sealed class OverlayHostConfigurationTests
{
    [TestMethod]
    public void OverlayWindowIsTransparentBorderlessAndAbsentFromTheTaskbar()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost",
            "Overlay",
            "OverlayWindow.xaml");
        Assert.IsTrue(File.Exists(path), $"缺少覆盖层窗口：{path}");
        var window = XDocument.Load(path).Root;
        Assert.IsNotNull(window);

        Assert.AreEqual("True", Attribute(window, "AllowsTransparency"), ignoreCase: true);
        Assert.AreEqual("None", Attribute(window, "WindowStyle"));
        Assert.AreEqual("False", Attribute(window, "ShowInTaskbar"), ignoreCase: true);
        Assert.AreEqual("Transparent", Attribute(window, "Background"));
    }

    [TestMethod]
    public void NativeInteropIsPresentAndCentralized()
    {
        var desktopRoot = Path.Combine(TestRepository.Root, "src", "Dango.Desktop");
        var nativeFiles = Directory
            .GetFiles(desktopRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("LibraryImport(", StringComparison.Ordinal) ||
                    source.Contains("DllImport(", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.IsNotEmpty(nativeFiles, "覆盖层宿主必须具有集中封装的最小 Win32 互操作。");
        Assert.IsTrue(
            nativeFiles.All(path => path.Contains(
                Path.Combine("DesktopHost", "Win32"),
                StringComparison.OrdinalIgnoreCase)),
            $"Win32 互操作必须集中在 DesktopHost/Win32：{string.Join(", ", nativeFiles)}");
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
