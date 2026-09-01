using System.Xml.Linq;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ControlCenterConfigurationTests
{
    [TestMethod]
    public void ControlCenterIsARegularTaskbarWindowWithApprovedPages()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "AppShell",
            "ControlCenter",
            "ControlCenterWindow.xaml");
        Assert.IsTrue(File.Exists(path), $"缺少控制中心窗口：{path}");
        var window = XDocument.Load(path).Root;
        Assert.IsNotNull(window);

        Assert.AreEqual("True", Attribute(window, "ShowInTaskbar"), ignoreCase: true);
        Assert.AreEqual("False", Attribute(window, "Topmost"), ignoreCase: true);
        Assert.AreEqual("820", Attribute(window, "MinWidth"));
        Assert.AreEqual("560", Attribute(window, "MinHeight"));

        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "概览");
        StringAssert.Contains(text, "桌面体验");
        StringAssert.Contains(text, "实验与验收");
        StringAssert.Contains(text, "诊断");
        StringAssert.Contains(text, "尚未完成人工验收");
        StringAssert.Contains(text, "WorkerW 尚未实现");
        StringAssert.Contains(text, "AutomationProperties");

        var codeBehind = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));
        StringAssert.Contains(codeBehind, "SystemParameters.HighContrast");
    }

    [STATestMethod]
    public void ControlCenterCanLoadItsCompiledXamlWithoutShowingAWindow()
    {
        var definition = VerificationDefinition.CreateP1002Overlay();
        var controller = new ApplicationCommandController(new FakeDesktopHost());
        var store = new VerificationSessionStore(definition);

        var window = new ControlCenterWindow(
            controller,
            definition,
            store,
            Path.Combine(Path.GetTempPath(), $"Dango.Tests.{Guid.NewGuid():N}.json"),
            () => { });

        Assert.AreEqual("Dango 控制中心", window.Title);
        window.PrepareForExit();
        window.Close();
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private sealed class FakeDesktopHost : IDesktopHostControl
    {
        public DesktopHostSnapshot GetSnapshot() =>
            new("Overlay", true, true, "尚未完成人工验收", null);

        public void SetVisibility(bool isVisible)
        {
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
        }

        public string CreateDiagnosticSummary() => "safe diagnostics";
    }
}
