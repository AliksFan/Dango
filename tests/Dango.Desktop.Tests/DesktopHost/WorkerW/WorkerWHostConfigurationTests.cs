using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.DesktopHost.WorkerW;

[TestClass]
public sealed class WorkerWHostConfigurationTests
{
    [TestMethod]
    public void WorkerWUsesNativeSystemLayeredHostWithSameProcessWpfChild()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost",
            "WorkerW",
            "WorkerWWindow.xaml");
        Assert.IsTrue(File.Exists(path), $"缺少 WorkerW 场景：{path}");
        var scene = XDocument.Load(path).Root;
        Assert.IsNotNull(scene);
        Assert.AreEqual("UserControl", scene.Name.LocalName);
        Assert.AreEqual("#FFEAF4FF", Attribute(scene, "Background"), ignoreCase: true);
        Assert.IsNull(scene.Attribute("AllowsTransparency"));

        var codeBehind = File.ReadAllText(path + ".cs");
        StringAssert.Contains(codeBehind, "new WorkerWNativeHost(target)");
        StringAssert.Contains(codeBehind, "ParentWindow = nativeHost.Handle");
        StringAssert.Contains(codeBehind, "UsesPerPixelTransparency = false");
        StringAssert.Contains(codeBehind, "WindowStyle = WsChild | WsVisible");
        Assert.IsFalse(codeBehind.Contains("SetParent(", StringComparison.Ordinal));

        var nativeHost = File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "WorkerWNativeHost.cs"));
        StringAssert.Contains(nativeHost, "WsExLayered");
        StringAssert.Contains(nativeHost, "WsPopup");
        StringAssert.Contains(nativeHost, "Parent = nint.Zero");
        StringAssert.Contains(nativeHost, "CreateHandle");
        StringAssert.Contains(nativeHost, "AttachWorkerWLayeredHost");

        var inputProxy = File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "WorkerWInputProxy.cs"));
        StringAssert.Contains(inputProxy, "AttachWorkerWInputProxy");
        StringAssert.Contains(inputProxy, "WmLeftButtonDown");
        StringAssert.Contains(inputProxy, "SetCapture");
        StringAssert.Contains(inputProxy, "SetEnabled");
        StringAssert.Contains(inputProxy, "inputPriorityTimer");
        StringAssert.Contains(inputProxy, "ShouldShowInputProxy");
        Assert.IsFalse(
            inputProxy.Contains("message.Result = HtTransparent", StringComparison.Ordinal),
            "跨进程 HTTRANSPARENT 不能可靠地把点击交还 Explorer。");
        StringAssert.Contains(codeBehind, "new WorkerWInputProxy");
        StringAssert.Contains(codeBehind, "UpdateInputProxyBounds");
        StringAssert.Contains(codeBehind, "DesktopLayeringSettings.PrototypeCushionId");
        StringAssert.Contains(codeBehind, "DesktopObjectLayer.AboveDesktopIcons");
        StringAssert.Contains(codeBehind, "islandOverlayWindow.SetDesiredVisibility");

        var islandPath = Path.Combine(Path.GetDirectoryName(path)!, "IslandOverlayWindow.xaml");
        var island = XDocument.Load(islandPath).Root;
        Assert.IsNotNull(island);
        Assert.AreEqual("True", Attribute(island, "Topmost"), ignoreCase: true);

        var objectOverlay = File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "DesktopObjectOverlayWindow.cs"));
        StringAssert.Contains(objectOverlay, "Topmost = false");
        StringAssert.Contains(objectOverlay, "interactionEnabled ? HtClient : HtTransparent");
        StringAssert.Contains(objectOverlay, "NativeMethods.SetCapture(windowHandle)");
    }

    [TestMethod]
    public void WorkerWPreservesTheExistingIslandAndCushionSceneContract()
    {
        var root = Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "DesktopHost");
        var window = File.ReadAllText(Path.Combine(root, "Window", "DangoSceneWindow.xaml"));
        var workerW = File.ReadAllText(Path.Combine(root, "WorkerW", "WorkerWWindow.xaml"));

        foreach (var marker in new[] { "悬浮岛", "Width=\"360\"" })
        {
            StringAssert.Contains(window, marker);
            StringAssert.Contains(workerW, marker);
        }

        StringAssert.Contains(window, "DeformableDangoVisual");
        Assert.IsFalse(window.Contains("占位团子", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InputProxyAppliesLayeredAlphaAfterCrossProcessReparenting()
    {
        var nativeMethods = File.ReadAllText(
            Path.Combine(
                TestRepository.Root,
                "src",
                "Dango.Desktop",
                "DesktopHost",
                "Win32",
                "NativeMethods.cs"));
        var methodStart = nativeMethods.IndexOf("public static void AttachWorkerWInputProxy", StringComparison.Ordinal);
        var methodEnd = nativeMethods.IndexOf("public static void PositionWorkerWInputProxy", methodStart, StringComparison.Ordinal);
        Assert.IsTrue(methodStart >= 0 && methodEnd > methodStart);
        var method = nativeMethods[methodStart..methodEnd];

        var reparent = method.IndexOf("SetParent(proxyWindow", StringComparison.Ordinal);
        var alpha = method.IndexOf("ApplyInputProxyTransparency(proxyWindow)", StringComparison.Ordinal);
        Assert.IsTrue(reparent >= 0 && alpha > reparent, "透明属性必须在跨进程 SetParent 完成后应用。");
    }

    [TestMethod]
    public void WorkerWUsesDynamicDangoMotionWithoutOwningBox2DSteps()
    {
        var root = Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "DesktopHost", "WorkerW");
        var xaml = File.ReadAllText(Path.Combine(root, "WorkerWWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "WorkerWWindow.xaml.cs"));

        StringAssert.Contains(xaml, "DeformableDangoVisual");
        Assert.IsFalse(xaml.Contains("占位团子", StringComparison.Ordinal));
        StringAssert.Contains(code, "SingleDangoMotionRuntime");
        StringAssert.Contains(code, "PhysicsPresentationInput.FromSnapshot");
        Assert.IsFalse(code.Contains(".Step(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AuthoritativeMovementKeepsTheInputProxyAlignedWithTheVisibleDango()
    {
        var code = File.ReadAllText(Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "DesktopHost",
            "WorkerW",
            "WorkerWWindow.xaml.cs"));
        var methodStart = code.IndexOf("private void ApplyAuthoritativePosition()", StringComparison.Ordinal);
        var methodEnd = code.IndexOf("private static long NowMilliseconds", methodStart, StringComparison.Ordinal);
        Assert.IsTrue(methodStart >= 0 && methodEnd > methodStart);

        var method = code[methodStart..methodEnd];
        StringAssert.Contains(
            method,
            "UpdateInputProxyBounds();",
            "方向控制更新团子位置时必须同步移动 WorkerW 输入代理，否则可见团子与可拖区域会分离。");
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
