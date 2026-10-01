using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductControlCenterLayoutTests
{
    [STATestMethod]
    [DataRow(320)]
    [DataRow(480)]
    [DataRow(940)]
    public void SettingsColumnUsesAvailableWidthWithoutHorizontalOverflow(int width)
    {
        var home = new ProductHomeView();
        home.ShowSettingsPage();
        var size = new Size(width, 602);
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();

        var inner = (Grid)home.FindName("SettingsInner");
        var column = Assert.IsInstanceOfType<FrameworkElement>(inner.Children[0]);
        var viewport = (ScrollViewer)home.FindName("SettingsPage");
        var expected = Math.Min(610, viewport.ViewportWidth - (width <= 420 ? 24 : width <= 680 ? 32 : 76));
        Assert.AreEqual(expected, column.ActualWidth, 1,
            "Settings must use the available reading width instead of shrinking to its text content.");
        foreach (var name in new[] { "DisplayToggleButton", "WindowModeButton", "AboveIconsButton", "WasdButton", "BowlerHatButton" })
        {
            var button = (FrameworkElement)home.FindName(name);
            var bounds = button.TransformToAncestor(home).TransformBounds(new Rect(button.RenderSize));
            Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= width,
                $"{name} must stay inside {width} DIP; actual {bounds}.");
        }

        var bitmap = new RenderTargetBitmap(width, 602, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(home);
        using var stream = TestEvidence.Create("settings-ui", $"settings-{width}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    [STATestMethod]
    [DataRow(320)]
    [DataRow(940)]
    public void OpeningDiagnosticsMakesItsTextVisibleInTheSettingsViewport(int width)
    {
        var home = new ProductHomeView();
        home.SetDiagnosticsText("fictional safe diagnostic details");
        var window = new Window
        {
            Content = home, Width = width, Height = 620, WindowStyle = WindowStyle.None,
            Left = -10000, Top = -10000, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            home.ShowSettingsPage(showDiagnostics: true);
            Pump(100);
            var viewport = (FrameworkElement)home.FindName("SettingsPage");
            var body = (FrameworkElement)home.FindName("SettingsDiagnosticsText");
            var viewportBounds = viewport.TransformToAncestor(home).TransformBounds(new Rect(viewport.RenderSize));
            var bodyBounds = body.TransformToAncestor(home).TransformBounds(new Rect(body.RenderSize));
            Assert.IsTrue(Rect.Intersect(viewportBounds, bodyBounds).Height >= 40,
                $"Expanded diagnostic text must be visible without an extra manual scroll; body {bodyBounds}, viewport {viewportBounds}.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void SelectedSettingsRemainReadableInHighContrast()
    {
        var home = new ProductHomeView();
        home.UpdateSettings(new ProductSettingsState(
            true, true, true, "Wallpaper", KeyboardControlSettings.CreateDefault(),
            DesktopObjectLayer.BelowDesktopIcons, "sprout", null, false, false, string.Empty));
        home.ApplyTheme(contrast: true);

        var selected = (Button)home.FindName("WallpaperModeButton");
        Assert.AreEqual(SystemColors.HighlightBrush, selected.Background);
        Assert.AreEqual(SystemColors.HighlightTextBrush, selected.Foreground,
            "Selected text must not use the same highlight color as its background.");
    }

    [STATestMethod]
    [DataRow(940, 620)]
    [DataRow(820, 560)]
    public void ProductPagesRenderWithinTheSupportedControlCenterSizes(int width, int height)
    {
        var window = new ControlCenterWindow(
            new ApplicationCommandController(new ReadyProductHost(), ApplicationUiMode.Product),
            VerificationDefinition.CreateP1003WorkerW(),
            new VerificationSessionStore(VerificationDefinition.CreateP1003WorkerW()),
            Path.Combine(TestRepository.Root, "artifacts", "fictional-layout-draft.json"),
            () => { });
        try
        {
            var content = Assert.IsInstanceOfType<FrameworkElement>(window.Content);
            window.ShowOverview();
            RenderAndAssert(window, content, "ProductHome", "product-overview", width, height);

            var home = Element<ProductHomeView>(window, "ProductHome");
            Click((Button)home.FindName("SettingsButton"));
            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("SettingsPage")).Visibility);
            RenderAndAssert(window, content, "ProductHome", "product-desktop-settings", width, height);

            window.ShowDiagnostics();
            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("SettingsDiagnosticsText")).Visibility);
            RenderAndAssert(window, content, "ProductHome", "product-diagnostics", width, height);
        }
        finally
        {
            window.PrepareForExit();
            window.Close();
        }
    }

    private static void RenderAndAssert(ControlCenterWindow window, FrameworkElement content, string pageName,
        string evidenceName, int width, int height)
    {
        var size = new Size(width, height);
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        Pump(100);

        var page = Element<FrameworkElement>(window, pageName);
        Assert.AreEqual(Visibility.Visible, page.Visibility, pageName);
        AssertInside(content, page, size, pageName);
        if (pageName == "ProductHome")
        {
            AssertInside(content, (FrameworkElement)page.FindName("TopNavigation"), size, "home-navigation");
            AssertInside(content, (FrameworkElement)page.FindName("SettingsButton"), size, "settings-entry");
        }
        else AssertInside(content, FindButton(window, "退出 Dango"), size, "exit");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(size));
        bitmap.Render(background);
        bitmap.Render(content);
        using var stream = TestEvidence.Create("desktop-entry-ui", $"{evidenceName}-{width}x{height}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void AssertInside(FrameworkElement root, FrameworkElement element, Size bounds, string name)
    {
        var rect = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        Assert.IsTrue(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= bounds.Width && rect.Bottom <= bounds.Height,
            $"{name} must remain inside {bounds.Width}x{bounds.Height}; actual {rect}.");
    }

    private static T Element<T>(ControlCenterWindow window, string name) where T : class =>
        window.FindName(name) as T ?? throw new AssertFailedException($"Missing compiled control: {name}");

    private static Button FindButton(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Content: string value } button && value == content) return button;
            var found = FindButtonOrNull(child, content);
            if (found is not null) return found;
        }
        throw new AssertFailedException($"Missing button: {content}");
    }

    private static Button? FindButtonOrNull(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Content: string value } button && value == content) return button;
            var found = FindButtonOrNull(child, content);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class ReadyProductHost : IDesktopHostControl, IDangoSceneControl
    {
        public SceneInitializationState InitializationState => SceneInitializationState.Ready;
        public string HatPresetId => "sprout";
        public bool TryApplyHatPreset(string id) => id is "sprout" or "top-hat" or "bowler";
        public bool RetryInitialization() => true;
        public DesktopHostSnapshot GetSnapshot() => new("Wallpaper", true, true, "fictional ready host", null);
        public void SetVisibility(bool isVisible) { }
        public void SetInteractionEnabled(bool isEnabled) { }
        public string CreateDiagnosticSummary() => "fictional safe diagnostics";
    }
}
