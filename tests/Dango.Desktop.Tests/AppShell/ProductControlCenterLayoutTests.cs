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
        var expected = Math.Min(610, viewport.ViewportWidth - (width <= 680 ? 32 : 76));
        Assert.AreEqual(expected, column.ActualWidth, 1,
            "Settings must use the available reading width instead of shrinking to its text content.");
        foreach (var name in new[] { "DisplayToggleButton", "WindowModeButton", "AboveIconsButton", "KeyboardToggleButton", "RetryWallpaperSettingsButton" })
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
            Content = home,
            Width = width,
            Height = 620,
            WindowStyle = WindowStyle.None,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
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
            DesktopObjectLayer.BelowDesktopIcons, null, false, false, string.Empty));
        home.ApplyTheme(contrast: true);

        var selected = (Button)home.FindName("WallpaperModeButton");
        Assert.AreEqual(SystemColors.HighlightBrush, selected.Background);
        Assert.AreEqual(SystemColors.HighlightTextBrush, selected.Foreground,
            "Selected text must not use the same highlight color as its background.");
    }

    [STATestMethod]
    [DataRow(960, 540)]
    [DataRow(1280, 720)]
    [DataRow(480, 800)]
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

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(853, 480)]
    [DataRow(360, 640)]
    public void CompactDisplayKeepsHomeEditorsAndSettingsReachable(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();
        AssertInside(home, (FrameworkElement)home.FindName("Launchers"), size, "home actions");

        foreach (var editor in new[] { HomeEditor.Face, HomeEditor.Wardrobe })
        {
            home.ShowEditor(editor);
            home.Measure(size);
            home.Arrange(new Rect(size));
            home.UpdateLayout();
            var stage = Bounds(home, (FrameworkElement)home.FindName("Stage"));
            var panel = Bounds(home, (FrameworkElement)home.FindName("EditorPanel"));
            Assert.IsTrue(panel.Left >= stage.Left - 1 && panel.Right <= stage.Right + 1 &&
                          panel.Top >= stage.Top - 1 && panel.Bottom <= stage.Bottom + 1,
                $"{editor} panel must stay inside the scrollable stage at {width}×{height}; panel {panel}, stage {stage}.");
            var options = Assert.IsInstanceOfType<ScrollViewer>(home.FindName("OptionsBody"),
                "Editor options need their own scroll viewport when the display is short.");
            var actionNames = editor == HomeEditor.Face
                ? new[] { "DefaultAppearanceButton", "CancelAppearanceButton", "ApplyButton" }
                : new[] { "CloseEditorButton" };
            var actionBounds = actionNames.ToDictionary(name => name,
                name => Bounds(home, (FrameworkElement)home.FindName(name)));
            foreach (var (name, action) in actionBounds)
                Assert.IsTrue(action.Top >= stage.Top && action.Bottom <= stage.Bottom + 1 &&
                              action.Left >= stage.Left - 1 && action.Right <= stage.Right + 1,
                    $"The fixed {name} must remain inside its stage while options scroll; action {action}, stage {stage}.");
            if (editor == HomeEditor.Face)
            {
                var faceOptions = (StackPanel)home.FindName("FaceOptions");
                var overflowProbe = new Border { Height = 400, IsHitTestVisible = false };
                faceOptions.Children.Add(overflowProbe);
                home.UpdateLayout();
                Assert.IsTrue(options.ScrollableHeight > 0,
                    $"Oversized face options must scroll at {width}×{height} DIP.");
                options.ScrollToBottom();
                home.UpdateLayout();
                Assert.IsTrue(options.VerticalOffset > 0, "The editor must actually scroll to its lower controls.");
                foreach (var (name, before) in actionBounds)
                    Assert.AreEqual(before, Bounds(home, (FrameworkElement)home.FindName(name)),
                        $"Scrolling options must not displace the fixed {name}.");
                faceOptions.Children.Remove(overflowProbe);
            }
        }

        home.ShowSettingsPage();
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();
        foreach (var name in new[] { "Landscape1080pSettingsButton", "Landscape1440pSettingsButton", "PortraitSettingsButton" })
        {
            var bounds = Bounds(home, (FrameworkElement)home.FindName(name));
            Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= width,
                $"{name} must not overflow horizontally at {width} DIP; actual {bounds}.");
        }
    }

    [STATestMethod]
    public void TwoHundredPercentLandscapeKeepsHorizontalEditorsReachableByScrolling()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(480, 270); // 960×540 physical pixels at 200% system DPI.
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();
        var content = (ScrollViewer)home.FindName("HomeContent");
        Assert.IsTrue(content.ScrollableHeight > 0, "A 270-DIP-tall window needs vertical scrolling.");
        content.ScrollToBottom();
        home.UpdateLayout();
        Assert.IsTrue(Bounds(home, (FrameworkElement)home.FindName("Launchers")).Bottom <=
                      Bounds(home, content).Bottom + 1, "Both home actions must remain reachable by scrolling.");

        foreach (var (editor, column) in new[] { (HomeEditor.Face, 0), (HomeEditor.Wardrobe, 2) })
        {
            home.ShowEditor(editor);
            home.UpdateLayout();
            var panel = (FrameworkElement)home.FindName("EditorPanel");
            Assert.AreEqual(1, Grid.GetColumnSpan(panel),
                "The landscape preset must not turn into the portrait editor solely because DIP width is small.");
            Assert.AreEqual(column, Grid.GetColumn(panel));
            content.ScrollToBottom();
            home.UpdateLayout();
            var fixedAction = editor == HomeEditor.Face ? "ApplyButton" : "CloseEditorButton";
            Assert.IsTrue(Bounds(home, (FrameworkElement)home.FindName(fixedAction)).Bottom <=
                          Bounds(home, content).Bottom + 1, "The editor action must remain reachable by scrolling.");
        }
    }

    [STATestMethod]
    public void CollapsedCompactPortraitFitsWithoutPermanentHomeScrollbar()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.SetWindowPreset(ControlCenterWindowPreset.Portrait);
        var size = new Size(360, 640); // Current-screen 150% portrait content floor.
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();
        var content = (ScrollViewer)home.FindName("HomeContent");
        var stage = (FrameworkElement)home.FindName("Stage");
        Assert.AreEqual(0, content.ScrollableHeight, 1,
            $"Collapsed portrait should not keep a scrollbar; viewport {content.ViewportHeight:F1}, " +
            $"stage {stage.ActualHeight:F1}, minimum {stage.MinHeight:F1}.");

        foreach (var editor in new[] { HomeEditor.Face, HomeEditor.Wardrobe })
        {
            home.ShowEditor(editor);
            home.UpdateLayout();
            var pet = Bounds(home, (FrameworkElement)home.FindName("PetPreview"));
            var panel = Bounds(home, (FrameworkElement)home.FindName("EditorPanel"));
            Assert.IsTrue(panel.Top >= pet.Bottom + 8,
                $"{editor} must still dock below the raised pet; pet {pet}, panel {panel}.");
            Assert.IsTrue(panel.Bottom <= Bounds(home, stage).Bottom + 1,
                $"{editor} must remain inside the scrollable stage; panel {panel}.");
        }
    }

    private static Rect Bounds(FrameworkElement root, FrameworkElement child) =>
        child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));

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
            var settingsButton = (FrameworkElement)page.FindName("SettingsButton");
            AssertInside(content, settingsButton, size, "settings-entry");
            foreach (var name in new[] { "MinimizeWindowButton", "WindowSizeButton", "CloseWindowButton" })
                AssertInside(content, Element<FrameworkElement>(window, name), size, name);
            var settingsBounds = settingsButton.TransformToAncestor(content).TransformBounds(new Rect(settingsButton.RenderSize));
            var minimize = Element<FrameworkElement>(window, "MinimizeWindowButton");
            var minimizeBounds = minimize.TransformToAncestor(content).TransformBounds(new Rect(minimize.RenderSize));
            Assert.IsTrue(Rect.Intersect(settingsBounds, minimizeBounds).IsEmpty,
                $"The product navigation must not collide with custom controls; settings {settingsBounds}, minimize {minimizeBounds}.");
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
