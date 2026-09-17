using System.Xml.Linq;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class StackedPreviewInteractionTests
{
    [TestMethod]
    public void StackedPreviewEntryIsRetiredFromTheFormalApplication()
    {
        var app = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "App.xaml.cs"));
        Assert.IsFalse(app.Contains("--stacked-contact-preview", StringComparison.Ordinal));
        Assert.IsFalse(app.Contains("StackedContactPreviewWindow", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("Window", "DangoSceneWindow.xaml")]
    [DataRow("WorkerW", "WorkerWWindow.xaml")]
    public void AllFormalHatOverlaysAreDeclaredAfterEveryBodyVisual(string host, string file)
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost",
            host,
            file);
        var root = XDocument.Load(path).Root!;
        var scene = root.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "SceneCanvas");
        var children = scene.Elements().ToArray();
        var bodyIndices = children
            .Select((element, index) => (element, index))
            .Where(item => item.element.Name.LocalName == nameof(DeformableDangoVisual))
            .Select(item => item.index)
            .ToArray();
        var hatIndices = children
            .Select((element, index) => (element, index))
            .Where(item => item.element.Name.LocalName == "DangoHatVisual")
            .Select(item => item.index)
            .ToArray();

        Assert.HasCount(3, bodyIndices);
        Assert.HasCount(3, hatIndices);
        Assert.IsGreaterThan(bodyIndices.Max(), hatIndices.Min(), "所有帽子必须在全部身体之后绘制，始终位于最上层。");
    }

    [TestMethod]
    public void FormalHostsUseSharedStackedRuntimeAndNoDirectTwoAxisControl()
    {
        var appHost = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "AppShell", "DangoApplicationHost.cs"));
        StringAssert.Contains(appHost, "StackedDangoSceneRuntime");
        Assert.AreEqual(1, Count(appHost, "new StackedDangoSceneRuntime"), "Wallpaper/Window 必须共享同一个 stacked runtime。");
        Assert.IsFalse(appHost.Contains("SingleDangoMotionRuntime", StringComparison.Ordinal));

        var world = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "Physics", "Presentation", "Box2DStackedContactPresentationWorld.cs"));
        Assert.IsFalse(world.Contains("StackedPresentationControl", StringComparison.Ordinal));
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
