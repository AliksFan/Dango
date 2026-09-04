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
        StringAssert.Contains(code, "SingleDangoMotionRuntime");
        StringAssert.Contains(code, "PreviewKeyDown");
        StringAssert.Contains(code, "PreviewKeyUp");
    }

    [STATestMethod]
    public void CloseHidesSceneAndShowingAgainCreatesOneFreshWorld()
    {
        var worlds = new List<TrackingWorld>();
        using var sessionOwner = new SessionOwner();
        SingleDangoMotionRuntime RuntimeFactory(IPhysicsPresentationSink sink) => new(
            () =>
            {
                var world = new TrackingWorld();
                worlds.Add(world);
                return world;
            },
            sink);
        var window = new DangoSceneWindow(sessionOwner.Session, RuntimeFactory);

        window.Show();
        Assert.IsTrue(window.IsVisible);
        Assert.HasCount(1, worlds);

        window.Close();
        Assert.IsFalse(window.IsVisible);
        Assert.IsTrue(worlds[0].Disposed);

        window.SetVisibility(true);
        Assert.IsTrue(window.IsVisible);
        Assert.HasCount(2, worlds);
        window.SetVisibility(true);
        Assert.HasCount(2, worlds);

        window.PrepareForExit();
        window.Close();
        Assert.IsTrue(worlds[1].Disposed);
    }

    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;

    private sealed class SessionOwner : IDisposable
    {
        public DesktopSceneSession Session { get; } = new();

        public void Dispose() => Session.Exit(Environment.TickCount64);
    }

    private sealed class TrackingWorld : IPhysicsPresentationWorld
    {
        public PhysicsPresentationWorldState State => Disposed
            ? PhysicsPresentationWorldState.Disposed
            : PhysicsPresentationWorldState.Active;

        public bool Disposed { get; private set; }

        public PhysicsPresentationFrame Step(PhysicsPresentationInput input, TimeSpan fixedStep) =>
            throw new InvalidOperationException("The lifecycle assertion closes the window before the first timer tick.");

        public void Dispose() => Disposed = true;
    }
}
