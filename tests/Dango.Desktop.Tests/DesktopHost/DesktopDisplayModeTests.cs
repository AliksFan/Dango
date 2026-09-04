using Dango.Desktop.DesktopHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class DesktopDisplayModeTests
{
    [TestMethod]
    public void RuntimeModesContainOnlyWallpaperAndWindow()
    {
        CollectionAssert.AreEqual(
            new[] { nameof(DesktopDisplayMode.Wallpaper), nameof(DesktopDisplayMode.Window) },
            Enum.GetNames<DesktopDisplayMode>());
    }

    [TestMethod]
    public void ProductionSourceNoLongerContainsTheOverlayHostNamespace()
    {
        var desktopHostRoot = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost");

        Assert.IsFalse(Directory.Exists(Path.Combine(desktopHostRoot, "Overlay")));
        Assert.IsFalse(Directory.EnumerateFiles(desktopHostRoot, "*.cs", SearchOption.AllDirectories)
            .Any(path => File.ReadAllText(path).Contains("DesktopHost.Overlay", StringComparison.Ordinal)));
    }
}
