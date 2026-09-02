using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWHostConfigurationTests
{
    [TestMethod]
    public void WorkerWWindowIsNotTopmostOrShownInTaskbar()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost",
            "WorkerW",
            "WorkerWWindow.xaml");
        Assert.IsTrue(File.Exists(path), $"缺少 WorkerW 窗口：{path}");
        var window = XDocument.Load(path).Root;
        Assert.IsNotNull(window);

        Assert.AreEqual("False", Attribute(window, "Topmost"), ignoreCase: true);
        Assert.AreEqual("False", Attribute(window, "ShowInTaskbar"), ignoreCase: true);
        Assert.AreEqual("None", Attribute(window, "WindowStyle"));
        Assert.AreEqual("Transparent", Attribute(window, "Background"));
    }

    [TestMethod]
    public void WorkerWUsesTheSamePlaceholderSceneContractAsOverlay()
    {
        var root = Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "DesktopHost");
        var overlay = File.ReadAllText(Path.Combine(root, "Overlay", "OverlayWindow.xaml"));
        var workerW = File.ReadAllText(Path.Combine(root, "WorkerW", "WorkerWWindow.xaml"));

        foreach (var marker in new[] { "占位团子", "静态悬浮岛", "Width=\"150\"", "Width=\"360\"" })
        {
            StringAssert.Contains(overlay, marker);
            StringAssert.Contains(workerW, marker);
        }
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
