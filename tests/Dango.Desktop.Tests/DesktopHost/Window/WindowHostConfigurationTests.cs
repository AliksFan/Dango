using System.Xml.Linq;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.Window;

[TestClass]
public sealed class WindowHostConfigurationTests
{
    [TestMethod]
    public void SceneWindowIsAStandardTaskbarWindow()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost",
            "Window",
            "DangoSceneWindow.xaml");

        Assert.IsTrue(File.Exists(path), $"缺少普通场景窗口：{path}");
        var window = XDocument.Load(path).Root!;
        Assert.AreEqual("Window", window.Name.LocalName);
        Assert.AreEqual("CanResize", Attribute(window, "ResizeMode"));
        Assert.AreEqual("True", Attribute(window, "ShowInTaskbar"), ignoreCase: true);
        Assert.AreEqual("False", Attribute(window, "Topmost"), ignoreCase: true);
        Assert.AreEqual("800", Attribute(window, "Width"));
        Assert.AreEqual("600", Attribute(window, "Height"));
        Assert.AreEqual("480", Attribute(window, "MinWidth"));
        Assert.AreEqual("360", Attribute(window, "MinHeight"));
        Assert.IsNull(window.Attribute("AllowsTransparency"));
        Assert.IsNull(window.Attribute("WindowStyle"));

        var code = File.ReadAllText(path + ".cs");
        StringAssert.Contains(code, "e.Cancel = true");
        StringAssert.Contains(code, "Hide()");
        StringAssert.Contains(code, "StackedDangoSceneRuntime");
        StringAssert.Contains(code, "PreviewKeyDown");
        StringAssert.Contains(code, "PreviewKeyUp");
    }

    [STATestMethod]
    public void CloseHidesSceneWithoutDisposingTheSharedStackedRuntime()
    {
        using var sessionOwner = new SessionOwner();
        using var runtime = new StackedDangoSceneRuntime();
        var window = new DangoSceneWindow(sessionOwner.Session, runtime);

        window.Show();
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(runtime.IsRunning);

        window.Close();
        Assert.IsFalse(window.IsVisible);
        Assert.IsFalse(runtime.IsRunning);

        window.SetVisibility(true);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(runtime.IsRunning);
        window.SetVisibility(true);
        Assert.IsTrue(runtime.IsRunning);

        window.PrepareForExit();
        window.Close();
    }

    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;

    private sealed class SessionOwner : IDisposable
    {
        public DesktopSceneSession Session { get; } = new();

        public void Dispose() => Session.Exit(Environment.TickCount64);
    }

}
