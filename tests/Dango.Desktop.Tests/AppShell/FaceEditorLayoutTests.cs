using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class FaceEditorLayoutTests
{
    private static readonly string[] Tools =
    [
        "AppearanceColorsTab", "AppearanceEyesTab", "FaceCloseEditorButton", "PreviewBlinkButton", "PreviewSleepButton",
        "DefaultAppearanceButton", "CancelAppearanceButton", "ApplyButton", "AppearanceStatus",
    ];
    private static readonly string[] Sliders =
        ["EyeSizeSlider", "EyeOvalSlider", "EyeHeightSlider", "EyeSpacingSlider"];

    [STATestMethod]
    [DataRow(640, 360)] // 960x540 at 150% system DPI.
    [DataRow(683, 384)] // 1024x576 at 150% system DPI.
    [DataRow(853, 480)] // 1280x720 at 150% system DPI.
    [DataRow(960, 540)]
    [DataRow(1024, 576)]
    [DataRow(1280, 720)]
    public void LandscapeFaceToolsUseThePreviewColumnAndLeaveAllFourSlidersVisible(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Face);
        Click(home, "AppearanceEyesTab");
        Layout(home, size);

        var stage = Bounds(home, "Stage");
        var parameterColumn = Bounds(home, "EditorPanel");
        var viewport = Bounds(home, "OptionsBody");
        var preview = Bounds(home, "PetPreview");
        var options = Element<ScrollViewer>(home, "OptionsBody");
        Assert.AreEqual(Visibility.Collapsed, Element<TextBlock>(home, "EditorTitle").Visibility,
            "The face title must release its space rather than keep an empty heading slot.");
        Assert.IsTrue(viewport.Top <= parameterColumn.Top + 8,
            $"Face parameters should start at the top of the left column; parameters {parameterColumn}, viewport {viewport}.");
        foreach (var name in Tools)
        {
            var tool = Element<FrameworkElement>(home, name);
            var bounds = Bounds(home, name);
            Assert.IsFalse(options.IsAncestorOf(tool), $"{name} must not scroll with the parameter list.");
            Assert.IsTrue(bounds.Left >= parameterColumn.Right + 4,
                $"{name} must use the right preview column; tool {bounds}, parameters {parameterColumn}.");
            AssertInside(stage, bounds, name);
            AssertNoOverlap(preview, bounds, $"{name} and the Dango preview");
        }
        foreach (var name in Sliders)
        {
            var bounds = Bounds(home, name);
            AssertInside(viewport, bounds, name);
            Assert.IsTrue(bounds.Width >= 160,
                $"{name} needs a usable dragging width at {width}x{height}; actual {bounds.Width:F1} DIP.");
        }
        Assert.IsTrue(Bounds(home, "AppearanceColorsTab").Bottom <= preview.Top,
            "Appearance tabs should sit above the preview.");
        Assert.IsTrue(Bounds(home, "PreviewBlinkButton").Top >= preview.Bottom,
            "Blink and sleep preview actions should sit below the Dango.");
        Assert.IsTrue(Bounds(home, "PreviewSleepButton").Bottom <= Bounds(home, "AppearanceStatus").Top,
            "Preview actions must leave a separate area for saving feedback.");
        Assert.IsTrue(Bounds(home, "AppearanceStatus").Bottom <= Bounds(home, "ApplyButton").Top,
            "Saving feedback must not overlap the fixed action row.");
        AssertDisjointButtons(home);
        SaveImage(home, size, $"face-right-tools-{width}x{height}");
    }

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(1024, 576)]
    [DataRow(360, 640)]
    public void ScrollingTheParameterListCannotMoveTheFaceTools(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Face);
        Click(home, "AppearanceEyesTab");
        Layout(home, size);
        var before = Tools.ToDictionary(name => name, name => Bounds(home, name));
        var faceOptions = Element<StackPanel>(home, "FaceOptions");
        var probe = new Border { Height = 800, IsHitTestVisible = false };
        faceOptions.Children.Add(probe);
        try
        {
            Layout(home, size);
            var options = Element<ScrollViewer>(home, "OptionsBody");
            Assert.IsTrue(options.ScrollableHeight > 0, "The probe must exercise an actual parameter scrollbar.");
            options.ScrollToBottom();
            home.UpdateLayout();
            Assert.IsTrue(options.VerticalOffset > 0);
            foreach (var name in Tools)
                Assert.AreEqual(before[name], Bounds(home, name), $"{name} must remain fixed when parameters scroll.");
        }
        finally { faceOptions.Children.Remove(probe); }
    }

    [STATestMethod]
    [DataRow(360, 640)]
    [DataRow(480, 800)]
    public void PortraitFaceGroupsStayInsideTheStageWithoutCoveringTheDango(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.SetWindowPreset(ControlCenterWindowPreset.Portrait);
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Face);
        Click(home, "AppearanceEyesTab");
        Layout(home, size);
        var stage = Bounds(home, "Stage");
        var pet = Bounds(home, "PetPreview");
        foreach (var name in Tools)
        {
            var tool = Bounds(home, name);
            AssertInside(stage, tool, name);
            AssertNoOverlap(pet, tool, $"{name} and the Dango preview");
        }
        AssertDisjointButtons(home);
        Assert.AreEqual(Visibility.Collapsed, Element<TextBlock>(home, "EditorTitle").Visibility);
        SaveImage(home, size, $"face-right-tools-portrait-{width}x{height}");
    }

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(360, 640)]
    public void ProtectedFileFeedbackAndRecoveryStayInTheFixedToolsWithoutOverlap(int width, int height)
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "face-layout", "fictional", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appearance.json");
        File.WriteAllText(path, "{\"unsupported\":true}");
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(new AppearanceSettingsSession(path));
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Face);
        Layout(home, size);
        var recover = Element<Button>(home, "RecoverAppearanceButton");
        Assert.AreEqual(Visibility.Visible, recover.Visibility, "The test must exercise protected-file recovery.");
        Assert.IsFalse(Element<ScrollViewer>(home, "OptionsBody").IsAncestorOf(recover),
            "Recovery must stay with saving feedback rather than scroll away with parameters.");
        foreach (var name in Tools.Append("RecoverAppearanceButton"))
        {
            AssertInside(Bounds(home, "Stage"), Bounds(home, name), name);
            AssertNoOverlap(Bounds(home, "PetPreview"), Bounds(home, name), name + " and the Dango preview");
        }
        foreach (var name in Tools)
            AssertNoOverlap(Bounds(home, name), Bounds(home, "RecoverAppearanceButton"), name + " and recovery");
        SaveImage(home, size, $"face-right-tools-recovery-{width}x{height}");
    }

    [STATestMethod]
    [DataRow("Close")]
    [DataRow("Wardrobe")]
    public void LeavingLongRecoveryFeedbackRestoresTheHomeStageHeight(string destination)
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "face-layout", "fictional", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appearance.json");
        File.WriteAllText(path, "{");
        var session = new AppearanceSettingsSession(path);
        Assert.IsFalse(session.TrySave(), "The malformed file must remain protected and produce the real recovery warning.");
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(session);
        var size = new Size(640, 360);
        Layout(home, size);
        var initialHeight = Element<FrameworkElement>(home, "Stage").ActualHeight;
        var initialScroll = Element<ScrollViewer>(home, "HomeContent").ScrollableHeight;
        home.ShowEditor(HomeEditor.Face);
        Layout(home, size);
        var faceHeight = Element<FrameworkElement>(home, "Stage").ActualHeight;
        Assert.IsTrue(faceHeight > initialHeight + 10,
            $"The long protected-file feedback must exercise a taller face stage before this regression can pass; initial {initialHeight:F1}, face {faceHeight:F1}.");
        if (destination == "Close") home.CloseEditor();
        else home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        Assert.AreEqual(initialHeight, Element<FrameworkElement>(home, "Stage").ActualHeight, 1,
            $"{destination} must release the extra space reserved for face recovery feedback.");
        Assert.AreEqual(initialScroll, Element<ScrollViewer>(home, "HomeContent").ScrollableHeight, 1,
            "Face recovery must not leave a permanent scrollbar after its tools disappear.");
    }

    [STATestMethod]
    [DataRow("Close")]
    [DataRow("Wardrobe")]
    [DataRow("Settings")]
    [DataRow("Porch")]
    public void LeavingFaceHidesItsToolsAndRemovesKeyboardAccess(string destination)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            home.ShowEditor(HomeEditor.Face);
            switch (destination)
            {
                case "Close": home.CloseEditor(); break;
                case "Wardrobe": home.ShowEditor(HomeEditor.Wardrobe); break;
                case "Settings": home.ShowSettingsPage(); break;
                default:
                    home.CloseEditor();
                    Element<FrameworkElement>(home, "Stage").Focus();
                    RaiseKey(home, window, Key.W);
                    Assert.IsTrue(home.IsInPorch);
                    break;
            }
            home.UpdateLayout();
            foreach (var name in Tools.Where(name => name != "AppearanceStatus"))
            {
                var tool = Element<Button>(home, name);
                Assert.IsFalse(tool.IsVisible, $"{name} must be hidden after leaving face for {destination}.");
                Assert.IsFalse(tool.IsEnabled, $"{name} must reject input immediately after leaving face.");
                Assert.IsFalse(tool.Focus(), $"{name} must not receive Tab or programmatic keyboard focus while inactive.");
            }
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow("AppearanceColorsTab")]
    [DataRow("AppearanceEyesTab")]
    [DataRow("DefaultAppearanceButton")]
    [DataRow("CancelAppearanceButton")]
    [DataRow("ApplyButton")]
    [DataRow("FaceCloseEditorButton")]
    public void FocusedRightSideActionsDoNotTriggerDangoKeyboardShortcuts(string name)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var directory = Path.Combine(TestRepository.Root, "artifacts", "face-layout", "fictional", Guid.NewGuid().ToString("N"));
        home.BindAppearance(new AppearanceSettingsSession(Path.Combine(directory, "appearance.json")));
        var window = ReviewWindow(home);
        try
        {
            window.Show();
            home.ShowEditor(HomeEditor.Face);
            Element<Slider>(home, "EyeSizeSlider").Value = 110;
            Assert.IsTrue(Element<Button>(home, name).Focus(), $"The test must focus the actual right-side {name}.");
            foreach (var key in new[] { Key.A, Key.D, Key.S, Key.Space, Key.W })
            {
                var args = RaiseKey(home, window, key);
                Assert.IsTrue(home.IsEditing, $"{key} must not close or switch the face editor while a tool has focus.");
                Assert.AreEqual(HomeEditor.Face, home.SelectedEditor);
                Assert.IsFalse(home.IsInPorch);
                Assert.IsFalse(args.Handled, $"{key} must remain with the focused face tool rather than enter the game dispatcher.");
            }
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void FaceTabsAndEnabledApplyRemainReadableInHighContrast()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var directory = Path.Combine(TestRepository.Root, "artifacts", "face-layout", "fictional", Guid.NewGuid().ToString("N"));
        home.BindAppearance(new AppearanceSettingsSession(Path.Combine(directory, "appearance.json")));
        home.ShowEditor(HomeEditor.Face);
        home.ApplyTheme(contrast: true);
        Element<Slider>(home, "EyeSizeSlider").Value = 110;
        var tab = Element<Button>(home, "AppearanceColorsTab");
        Assert.AreEqual(SystemColors.HighlightBrush, tab.Background);
        Assert.AreEqual(SystemColors.HighlightTextBrush, tab.Foreground);
        var apply = Element<Button>(home, "ApplyButton");
        Assert.IsTrue(apply.IsEnabled);
        if (Equals(SystemColors.HighlightBrush, apply.Background))
            Assert.AreEqual(SystemColors.HighlightTextBrush, apply.Foreground,
                "A highlighted primary apply button must use the system highlight text color.");
    }

    private static Window ReviewWindow(ProductHomeView home) => new()
    {
        Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000,
        ShowInTaskbar = false, WindowStyle = WindowStyle.None,
    };
    private static T Element<T>(ProductHomeView home, string name) where T : class =>
        home.FindName(name) as T ?? throw new AssertFailedException($"Missing compiled face control: {name}");
    private static void Click(ProductHomeView home, string name) =>
        Element<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Layout(ProductHomeView home, Size size)
    {
        home.Measure(size);
        home.Arrange(new Rect(size));
        home.UpdateLayout();
    }
    private static Rect Bounds(ProductHomeView home, string name)
    {
        var child = Element<FrameworkElement>(home, name);
        return child.TransformToAncestor(home).TransformBounds(new Rect(child.RenderSize));
    }
    private static void AssertInside(Rect area, Rect child, string name) =>
        Assert.IsTrue(child.Left >= area.Left - 1 && child.Top >= area.Top - 1 &&
                      child.Right <= area.Right + 1 && child.Bottom <= area.Bottom + 1,
            $"{name} must remain inside {area}; actual {child}.");
    private static void AssertNoOverlap(Rect a, Rect b, string names)
    {
        var intersection = Rect.Intersect(a, b);
        Assert.IsTrue(intersection.IsEmpty || intersection.Width <= .5 || intersection.Height <= .5,
            $"{names} must not overlap; first {a}, second {b}.");
    }
    private static void AssertDisjointButtons(ProductHomeView home)
    {
        var names = Tools.Where(name => name != "AppearanceStatus").ToArray();
        for (var i = 0; i < names.Length; i++)
        for (var j = i + 1; j < names.Length; j++)
            AssertNoOverlap(Bounds(home, names[i]), Bounds(home, names[j]), $"{names[i]} and {names[j]}");
    }
    private static KeyEventArgs RaiseKey(ProductHomeView home, Window window, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        home.RaiseEvent(args);
        return args;
    }
    private static void SaveImage(ProductHomeView home, Size size, string name)
    {
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(home);
        using var stream = TestEvidence.Create("face-layout", name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
