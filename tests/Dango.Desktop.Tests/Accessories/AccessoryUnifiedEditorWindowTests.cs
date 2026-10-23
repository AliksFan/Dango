using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryUnifiedEditorWindowTests
{
    [STATestMethod]
    public void UnshownWindowHasEverySharedParameterAndFixedAccessibleActionsWithoutStartingBrowser()
    {
        var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(2, 2, 96, 96,
            PixelFormats.Bgra32, null, new byte[] { 12, 34, 56, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 8));
        var original = new AccessoryItem("fictional-window-item", "虚构配饰", source.AssetId, 2, 2);
        var window = new AccessoryUnifiedEditorWindow(new("fictional-outer", 1, original.Id, 1, original), source, null);
        try
        {
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
            Assert.IsFalse(window.IsVisible); Assert.IsNull(window.Result);
            Assert.AreEqual(original, window.Draft);
            var controls = Descendants(window).ToArray();
            var sliders = controls.OfType<Slider>().ToDictionary(AutomationProperties.GetName);
            Assert.AreEqual(15, sliders.Count);
            Check("尺寸 %", 10, 400); Check("基础角度 °", -180, 180);
            Check("图片根部 X", 0, 1); Check("图片根部 Y", 0, 1);
            Check("身体附着 X", 0, 1); Check("身体附着 Y", 0, 1);
            Check("连接强度", 0, 100); Check("连接最大行程 %", 0, 200);
            Check("整体旋转阻尼", 0, 100); Check("整体回正", 0, 100);
            Check("整体最大摆角 °", 0, 180); Check("重力影响", 0, 100);
            Check("骨架柔软度", 0, 1); Check("骨架阻尼 /s", 0, 30); Check("骨架恢复 /s²", 0, 30);
            Assert.AreEqual(16, controls.OfType<TextBox>().Count());
            var buttons = controls.OfType<Button>().ToDictionary(AutomationProperties.GetName);
            CollectionAssert.AreEquivalent(new[] { "图像", "参数", "重试", "取消", "确认" }, buttons.Keys.ToArray());
            Assert.IsTrue(buttons["取消"].IsCancel);
            Assert.IsFalse(buttons["取消"].Focusable);
            Assert.AreEqual(40d, buttons["确认"].MinHeight);
            Assert.IsFalse(buttons["确认"].IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, buttons["重试"].Visibility);
            Assert.IsNotNull(window.TryFindResource("HomeBackground"));
            Assert.IsNotNull(window.TryFindResource(typeof(ScrollBar)));
            Assert.IsNotNull(window.TryFindResource(typeof(Slider)));
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
            void Check(string label, double min, double max)
            { Assert.AreEqual(min, sliders[label].Minimum, label); Assert.AreEqual(max, sliders[label].Maximum, label); }
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow(560, 440)]
    [DataRow(1120, 760)]
    [DataRow(700, 960)]
    public void UnshownMeasuredLayoutsKeepCanvasOrScrollableParametersAndFooterReachable(int width, int height)
    {
        var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(2, 2, 96, 96,
            PixelFormats.Bgra32, null, new byte[] { 12, 34, 56, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 8));
        var original = new AccessoryItem("fictional-layout-item", "虚构布局", source.AssetId, 2, 2);
        var window = new AccessoryUnifiedEditorWindow(new("fictional-outer", 1, original.Id, 1, original), source, null, _ => { });
        try
        {
            Assert.AreEqual(560d, window.MinWidth); Assert.AreEqual(440d, window.MinHeight);
            var layout = (FrameworkElement)window.Content;
            var controls = Descendants(window).ToArray();
            var buttons = controls.OfType<Button>().ToDictionary(AutomationProperties.GetName);
            var browser = controls.OfType<AccessoryEditorBrowserHost>().Single();
            var parameters = controls.OfType<ScrollViewer>().Single(v => AutomationProperties.GetName(v) == "配饰参数");
            var feedback = controls.OfType<TextBlock>().Single(t => AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite);
            feedback.Text = "需要 Microsoft Edge WebView2 Runtime。草稿仍保留，可重试或取消。重试将恢复最近完整预览或初始工程，未导出的画布修改可能丢失。";
            buttons["重试"].Visibility = Visibility.Visible;
            Arrange();
            var narrow = width < 900;
            Assert.AreEqual(narrow ? Visibility.Visible : Visibility.Collapsed, ((FrameworkElement)buttons["图像"].Parent).Visibility);
            Assert.AreEqual(Visibility.Visible, browser.Visibility);
            Assert.AreEqual(narrow ? Visibility.Collapsed : Visibility.Visible, parameters.Visibility);
            Assert.IsTrue(browser.ActualHeight > 100, "Canvas must retain the full content area at the minimum boundary.");
            CheckFooter();
            if (narrow)
            {
                buttons["参数"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Arrange();
                Assert.AreEqual(Visibility.Collapsed, browser.Visibility);
                Assert.AreEqual(Visibility.Visible, parameters.Visibility);
                Assert.IsTrue(parameters.ViewportHeight > 100);
                Assert.IsTrue(parameters.ScrollableHeight > 0);
                parameters.ScrollToEnd(); Arrange();
                Assert.IsTrue(parameters.VerticalOffset > 0);
                CheckFooter();
                buttons["图像"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Arrange();
                Assert.AreEqual(Visibility.Visible, browser.Visibility);
                Assert.AreEqual(Visibility.Collapsed, parameters.Visibility);
            }
            Assert.AreEqual(original, window.Draft); Assert.IsNull(window.Result);
            Assert.AreEqual(AccessoryEditorRuntimeState.Idle, browser.Runtime.State);
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
            void Arrange()
            {
                layout.Measure(new Size(width, height)); layout.Arrange(new Rect(0, 0, width, height)); layout.UpdateLayout();
                // Visibility changes at the responsive breakpoint invalidate one more measure pass.
                layout.Measure(new Size(width, height)); layout.Arrange(new Rect(0, 0, width, height)); layout.UpdateLayout();
            }
            void CheckFooter()
            {
                foreach (var key in new[] { "重试", "试摆", "取消", "确认" })
                {
                    var button = buttons[key];
                    var bounds = button.TransformToAncestor(layout).TransformBounds(new Rect(new Point(), button.RenderSize));
                    Assert.IsTrue(bounds.Width > 0 && bounds.Height >= 40, key);
                    Assert.IsTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= layout.ActualWidth + .5 &&
                        bounds.Bottom <= layout.ActualHeight + .5, $"{key}: {bounds} outside {layout.RenderSize}");
                }
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
