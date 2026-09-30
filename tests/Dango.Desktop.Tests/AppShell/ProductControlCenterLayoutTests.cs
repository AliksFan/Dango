using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductControlCenterLayoutTests
{
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
            RenderAndAssert(window, content, "OverviewPage", "product-overview", width, height);

            Click(Element<Button>(window, "ProductSettingsNavigation"));
            RenderAndAssert(window, content, "DesktopPage", "product-desktop-settings", width, height);

            Click(Element<Button>(window, "ProductHelpNavigation"));
            RenderAndAssert(window, content, "DiagnosticsPage", "product-diagnostics", width, height);
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

        var page = Element<FrameworkElement>(window, pageName);
        Assert.AreEqual(Visibility.Visible, page.Visibility, pageName);
        AssertInside(content, page, size, pageName);
        AssertInside(content, Element<Grid>(window, "ProductHeader"), size, "product-header");
        AssertInside(content, Element<Button>(window, "ProductExitButton"), size, "exit");

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
