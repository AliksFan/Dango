using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryUnifiedEditorWindowTests
{
    [STATestMethod]
    [DataRow(800d, 640d, 1d, 668, 132, 38)]
    [DataRow(1120d, 760d, 1.5d, 1482, 198, 57)]
    [DataRow(800d, 640d, 1.25d, 835, 165, 48)]
    [DataRow(80d, 20d, 1d, 0, 80, 20)]
    public void UnshownNativeCutoutTracksPhysicalDpiAndResizedViewportOnlyWhenExplicitlyEnabled(
        double width, double height, double scale, int left, int cutoutWidth, int cutoutHeight)
    {
        using var host = new AccessoryEditorBrowserHost();
        var viewport = new Size(width, height); var dpi = new DpiScale(scale, scale);
        Assert.IsNull(host.GetWindowControlsCutoutBounds(viewport, dpi), "Other browser hosts must retain the full native viewport.");
        host.WindowControlsCutout = new Size(132, 38);
        Assert.AreEqual(new System.Drawing.Rectangle(left, 0, cutoutWidth, cutoutHeight), host.GetWindowControlsCutoutBounds(viewport, dpi));
        var wider = host.GetWindowControlsCutoutBounds(new Size(width + 100, height), dpi)!.Value;
        Assert.AreEqual((int)Math.Ceiling((width + 100) * scale), wider.Right,
            "The cutout stays anchored to the physical right edge after resizing.");
        host.WindowControlsCutout = new Size();
        Assert.IsNull(host.GetWindowControlsCutoutBounds(viewport, dpi));
        Assert.AreEqual(AccessoryEditorRuntimeState.Idle, host.Runtime.State);
    }

    [STATestMethod]
    public void UnshownWindowContainsOnlyFullBrowserAndFixedAccessibleActionsWithoutStartingBrowser()
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
            Assert.AreEqual(0, controls.OfType<Slider>().Count());
            Assert.AreEqual(0, controls.OfType<TextBox>().Count());
            Assert.AreEqual(0, controls.OfType<ComboBox>().Count());
            Assert.AreEqual(0, controls.OfType<ScrollViewer>().Count());
            var buttons = controls.OfType<Button>().ToDictionary(AutomationProperties.GetName);
            CollectionAssert.AreEquivalent(new[] { "重试", "取消", "确认", "最小化窗口", "最大化窗口", "关闭窗口" }, buttons.Keys.ToArray());
            Assert.IsFalse(buttons["确认"].IsDefault, "Enter must remain owned by the browser tool or its text input.");
            Assert.IsTrue(buttons.Values.All(button => !button.Focusable), "Footer clicks must not take editor keyboard focus.");
            Assert.IsTrue(buttons["取消"].IsCancel);
            Assert.IsFalse(buttons["取消"].Focusable);
            foreach (var name in new[] { "重试", "取消", "确认" })
            {
                var button = buttons[name];
                Assert.AreEqual(32d, button.Width, name);
                Assert.AreEqual(name, button.ToolTip, name);
                Assert.IsFalse(button.Content is string or TextBlock, $"{name} should render as an icon only.");
            }
            Assert.AreEqual(1120d, window.Width); Assert.AreEqual(760d, window.Height);
            Assert.AreEqual(800d, window.MinWidth); Assert.AreEqual(640d, window.MinHeight);
            Assert.AreEqual(WindowStyle.None, window.WindowStyle);
            Assert.AreEqual(ResizeMode.CanResize, window.ResizeMode);
            var chrome = WindowChrome.GetWindowChrome(window);
            Assert.IsNotNull(chrome); Assert.AreEqual(0d, chrome.CaptionHeight);
            Assert.AreEqual(new Thickness(7), chrome.ResizeBorderThickness);
            Assert.IsFalse(chrome.UseAeroCaptionButtons);
            Assert.IsFalse(controls.OfType<TextBlock>().Any(text => text.Text == "编辑配饰"));
            Assert.AreEqual(new Size(132, 38), controls.OfType<AccessoryEditorBrowserHost>().Single().WindowControlsCutout);
            foreach (var name in new[] { "最小化窗口", "最大化窗口", "关闭窗口" })
            {
                var button = buttons[name];
                Assert.AreSame(window.FindResource("ProductWindowButton"), button.Style);
                Assert.AreEqual(34d, button.Width); Assert.AreEqual(30d, button.Height);
                Assert.IsTrue(WindowChrome.GetIsHitTestVisibleInChrome(button));
                Assert.IsFalse(button.IsDefault); Assert.IsFalse(button.IsCancel);
                Assert.IsInstanceOfType<System.Windows.Shapes.Path>(button.Content);
            }
            Assert.IsFalse(buttons["确认"].IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, buttons["重试"].Visibility);
            Assert.IsNotNull(window.TryFindResource("HomeBackground"));
            Assert.IsNotNull(window.TryFindResource(typeof(ScrollBar)));
            Assert.IsNotNull(window.TryFindResource(typeof(Slider)));
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow(800, 640)]
    [DataRow(1120, 760)]
    [DataRow(960, 900)]
    public void UnshownMeasuredLayoutsKeepFullBrowserAndFailureFooterReachable(int width, int height)
    {
        var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(2, 2, 96, 96,
            PixelFormats.Bgra32, null, new byte[] { 12, 34, 56, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 8));
        var original = new AccessoryItem("fictional-layout-item", "虚构布局", source.AssetId, 2, 2);
        var window = new AccessoryUnifiedEditorWindow(new("fictional-outer", 1, original.Id, 1, original), source, null, _ => { });
        try
        {
            Assert.AreEqual(800d, window.MinWidth); Assert.AreEqual(640d, window.MinHeight);
            var layout = (FrameworkElement)window.Content;
            var controls = Descendants(window).ToArray();
            var buttons = controls.OfType<Button>().ToDictionary(AutomationProperties.GetName);
            var browser = controls.OfType<AccessoryEditorBrowserHost>().Single();
            var feedback = controls.OfType<TextBlock>().Single(t => AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite);
            var footer = controls.OfType<Grid>().Single(g => g.RowDefinitions.Count == 2 && g.MinHeight == 32);
            Assert.AreEqual(6d, footer.Margin.Top); Assert.AreEqual(32d, footer.MinHeight);
            Assert.AreEqual(TextWrapping.Wrap, feedback.TextWrapping);
            feedback.Text = "需要 Microsoft Edge WebView2 Runtime。草稿仍保留，可重试或取消。重试将恢复最近完整预览或初始工程，未导出的画布修改可能丢失。";
            buttons["重试"].Visibility = Visibility.Visible;
            Arrange();
            Assert.AreEqual(Visibility.Visible, browser.Visibility);
            Assert.IsTrue(browser.ActualHeight > 100, "Browser must retain the main content area at the minimum boundary.");
            Assert.AreEqual(layout.ActualWidth, browser.ActualWidth, .5, "Native parameter columns must not reserve browser width.");
            Assert.AreEqual(0, controls.OfType<ScrollViewer>().Count());
            var feedbackBounds = feedback.TransformToAncestor(layout).TransformBounds(new Rect(new Point(), feedback.RenderSize));
            Assert.IsTrue(feedbackBounds.Width > 0 && feedbackBounds.Height > 0, "Status and long error details must remain reachable.");
            if (width < 868)
                Assert.IsTrue(feedbackBounds.Height >= 32, "Long errors should wrap in the stacked narrow footer.");
            CheckFooter();
            var close = buttons["关闭窗口"];
            var closeBounds = close.TransformToAncestor(layout).TransformBounds(new Rect(new Point(), close.RenderSize));
            Assert.IsTrue(closeBounds.Right <= layout.ActualWidth && closeBounds.Top == 0);
            Assert.IsTrue(closeBounds.Bottom <= 38, "Window controls must occupy the reserved browser corner, not an extra title row.");
            Assert.AreEqual(0, browser.TransformToAncestor(layout).Transform(new Point()).Y,
                "The browser starts at the top without a native title row.");
            Assert.AreEqual(original, window.Draft); Assert.IsNull(window.Result);
            Assert.AreEqual(AccessoryEditorRuntimeState.Idle, browser.Runtime.State);
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
            void Arrange()
            {
                layout.Measure(new Size(width, height)); layout.Arrange(new Rect(0, 0, width, height)); layout.UpdateLayout();
                // Footer stacking at the responsive breakpoint invalidates one more measure pass.
                layout.Measure(new Size(width, height)); layout.Arrange(new Rect(0, 0, width, height)); layout.UpdateLayout();
            }
            void CheckFooter()
            {
                Assert.IsFalse(buttons.ContainsKey("试摆"), "Dynamic preview is now in the browser's upper inspector.");
                foreach (var key in new[] { "重试", "取消", "确认" })
                {
                    var button = buttons[key];
                    var bounds = button.TransformToAncestor(layout).TransformBounds(new Rect(new Point(), button.RenderSize));
                    Assert.AreEqual(32d, bounds.Width, .5, key);
                    Assert.AreEqual(32d, bounds.Height, .5, key);
                    Assert.IsTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= layout.ActualWidth + .5 &&
                        bounds.Bottom <= layout.ActualHeight + .5, $"{key}: {bounds} outside {layout.RenderSize}");
                }
            }
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RuntimeFailureKeepsWholeDraftAndAccessibleRetryWithoutStartingAnUnshownBrowser()
    {
        var source = AccessoryEditorSourceAsset.FromBitmap(BitmapSource.Create(2, 2, 96, 96,
            PixelFormats.Bgra32, null, new byte[] { 12, 34, 56, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 8));
        var original = new AccessoryItem("fictional-failure-item", "虚构故障", source.AssetId, 2, 2)
            { GravityInfluence = 17, SizePercent = 130, BodyAnchorY = .8 };
        var window = new AccessoryUnifiedEditorWindow(new("fictional-outer", 1, original.Id, 1, original), source, null, _ => { });
        try
        {
            var session = (AccessoryUnifiedEditorSession)typeof(AccessoryUnifiedEditorWindow)
                .GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            session.Fail("虚构的编辑器进程故障");
            var controls = Descendants(window).ToArray();
            var buttons = controls.OfType<Button>().ToDictionary(AutomationProperties.GetName);
            Assert.AreEqual(Visibility.Visible, buttons["重试"].Visibility);
            Assert.IsTrue(buttons["重试"].IsEnabled);
            Assert.IsFalse(buttons["确认"].IsEnabled);
            Assert.IsFalse(buttons.ContainsKey("试摆"), "The retired snapshot-only footer action must not return.");
            Assert.IsTrue(buttons["取消"].IsEnabled);
            StringAssert.Contains(window.StatusMessage, "虚构的编辑器进程故障");
            StringAssert.Contains(window.StatusMessage, "未导出");
            Assert.AreEqual(original, window.Draft); Assert.IsNull(window.Result);
            Assert.AreEqual(AccessoryEditorRuntimeState.Idle, controls.OfType<AccessoryEditorBrowserHost>().Single().Runtime.State);
            Assert.AreEqual(nint.Zero, new WindowInteropHelper(window).Handle);
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
