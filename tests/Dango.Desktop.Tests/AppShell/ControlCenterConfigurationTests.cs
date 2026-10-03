using System.Xml.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shell;
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
        StringAssert.Contains(text, "壁纸层 · 默认随桌面显示");
        StringAssert.Contains(text, "普通窗口 · 可移动和调整大小");
        Assert.IsFalse(text.Contains("OverlayHostRadioButton", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("WorkerWHostRadioButton", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ArrowKeysRadioButton", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("WasdRadioButton", StringComparison.Ordinal));
        StringAssert.Contains(text, "空格跳；W 交互");
        StringAssert.Contains(text, "对象层级（实验）");
        StringAssert.Contains(text, "团子层级");
        StringAssert.Contains(text, "占位软垫");
        StringAssert.Contains(text, "所有界面之上；全屏时隐藏");
        StringAssert.Contains(text, "添加团子");
        StringAssert.Contains(text, "移除团子");
        StringAssert.Contains(text, "显示骨架");
        StringAssert.Contains(text, "恢复默认参数");
        StringAssert.Contains(text, "重置当前团子");
        StringAssert.Contains(text, "拖动力");
        StringAssert.Contains(text, "落地形变力");
        StringAssert.Contains(text, "跳跃冲量");
        StringAssert.Contains(text, "AutomationProperties");

        var codeBehind = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));
        StringAssert.Contains(codeBehind, "SystemParameters.HighContrast");
        StringAssert.Contains(codeBehind, "SetDebugSkeletonVisible");

        var appHost = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "AppShell", "DangoApplicationHost.cs"));
        Assert.IsFalse(appHost.Contains("CreateP2D007A", StringComparison.Ordinal));
        Assert.IsFalse(appHost.Contains("p2d-007a-draft", StringComparison.OrdinalIgnoreCase));
    }

    [STATestMethod]
    public void ControlCenterCanLoadItsCompiledXamlWithoutShowingAWindow()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var controller = new ApplicationCommandController(new FakeDesktopHost(), ApplicationUiMode.DeveloperReview);
        var store = new VerificationSessionStore(definition);

        var window = new ControlCenterWindow(
            controller,
            definition,
            store,
            Path.Combine(Path.GetTempPath(), $"Dango.Tests.{Guid.NewGuid():N}.json"),
            () => { });

        Assert.AreEqual("Dango 控制中心", window.Title);
        var panel = (System.Windows.Controls.StackPanel)window.FindName("PhysicsTuningPanel");
        var rows = panel.Children.OfType<System.Windows.Controls.Expander>()
            .SelectMany(expander => ((System.Windows.Controls.StackPanel)expander.Content).Children
                .OfType<System.Windows.Controls.Grid>()).ToArray();
        Assert.IsTrue(rows.Length > 0);
        foreach (var row in rows)
        {
            Assert.IsFalse(row.Children.OfType<System.Windows.Controls.Slider>().Single().IsEnabled);
            Assert.IsTrue(row.Children.OfType<System.Windows.Controls.TextBox>().Single().IsReadOnly);
        }
        window.PrepareForExit();
        window.Close();
    }

    [STATestMethod]
    public void ProductWindowHasRoundedBorderlessControlsWhileDeveloperReviewKeepsNativeChrome()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var product = new ControlCenterWindow(
            new ApplicationCommandController(new FakeDesktopHost(), ApplicationUiMode.Product),
            definition, new VerificationSessionStore(definition),
            Path.Combine(Path.GetTempPath(), $"Dango.ProductChrome.{Guid.NewGuid():N}.json"), () => { });
        var review = new ControlCenterWindow(
            new ApplicationCommandController(new FakeDesktopHost(), ApplicationUiMode.DeveloperReview),
            definition, new VerificationSessionStore(definition),
            Path.Combine(Path.GetTempPath(), $"Dango.ReviewChrome.{Guid.NewGuid():N}.json"), () => { });
        try
        {
            Assert.AreEqual(WindowStyle.None, product.WindowStyle);
            Assert.AreEqual(ResizeMode.NoResize, product.ResizeMode,
                "Product dimensions are selected from three presets, not freely resized.");
            Assert.IsFalse(product.AllowsTransparency, "Layered transparency would harm live WPF rendering.");
            var chrome = WindowChrome.GetWindowChrome(product);
            Assert.IsNotNull(chrome);
            Assert.IsTrue(chrome.CornerRadius.TopLeft >= 8);
            Assert.IsTrue(chrome.CaptionHeight <= 40, "The approved title area is deliberately thin.");
            Assert.AreEqual(Visibility.Visible, ((UIElement)product.FindName("ProductWindowChrome")).Visibility);
            foreach (var name in new[] { "MinimizeWindowButton", "WindowSizeButton", "CloseWindowButton" })
            {
                var button = product.FindName(name) as Button;
                Assert.IsNotNull(button, $"Missing custom title control {name}.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.IsTrue(WindowChrome.GetIsHitTestVisibleInChrome(button));
            }
            Assert.IsTrue(WindowChrome.GetIsHitTestVisibleInChrome(
                (Button)((ProductHomeView)product.FindName("ProductHome")).FindName("SettingsButton")),
                "Settings must remain clickable inside the draggable caption region.");
            Assert.AreNotEqual(WindowStyle.None, review.WindowStyle);
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)review.FindName("ProductWindowChrome")).Visibility);

            ((Button)product.FindName("MinimizeWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(WindowState.Minimized, product.WindowState);
            product.WindowState = WindowState.Normal;
            ((Button)product.FindName("WindowSizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(WindowState.Normal, product.WindowState);

            product.Left = product.Top = -10000;
            product.ShowInTaskbar = false;
            product.Show();
            ((Button)product.FindName("CloseWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(product.IsVisible, "The custom close control must retain close-to-tray semantics.");
        }
        finally
        {
            product.PrepareForExit();
            product.Close();
            review.PrepareForExit();
            review.Close();
        }
    }

    [STATestMethod]
    public void FreshDeveloperReviewKeepsItsHistoricalSizeWhileProductUsesDefaultPreset()
    {
        var definition = VerificationDefinition.CreateP1003WorkerW();
        var product = new ControlCenterWindow(
            new ApplicationCommandController(new FakeDesktopHost(), ApplicationUiMode.Product),
            definition, new VerificationSessionStore(definition),
            Path.Combine(Path.GetTempPath(), $"Dango.ProductSize.{Guid.NewGuid():N}.json"), () => { });
        var review = new ControlCenterWindow(
            new ApplicationCommandController(new FakeDesktopHost(), ApplicationUiMode.DeveloperReview),
            definition, new VerificationSessionStore(definition),
            Path.Combine(Path.GetTempPath(), $"Dango.ReviewSize.{Guid.NewGuid():N}.json"), () => { });
        try
        {
            Assert.AreEqual(960d, product.Width);
            Assert.AreEqual(540d, product.Height);
            Assert.AreEqual(540d, product.MinHeight);
            Assert.AreEqual(940d, review.Width, "The preset rollout must not resize fresh developer-review windows.");
            Assert.AreEqual(620d, review.Height);
            Assert.AreEqual(560d, review.MinHeight);
        }
        finally
        {
            product.PrepareForExit();
            product.Close();
            review.PrepareForExit();
            review.Close();
        }
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private sealed class FakeDesktopHost : IDesktopHostControl
    {
        public DesktopHostSnapshot GetSnapshot() =>
            new("Wallpaper", true, true, "尚未完成人工验收", null);

        public void SetVisibility(bool isVisible)
        {
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
        }

        public string CreateDiagnosticSummary() => "safe diagnostics";
    }
}
