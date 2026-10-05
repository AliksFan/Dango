using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductHomeInteractionTests
{
    [STATestMethod]
    [DataRow(1024)]
    [DataRow(736)]
    [DataRow(640)]
    public void HomeKeepsNavigationFixedAndMirrorsFaceAndWardrobePanels(int width)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        Layout(home, width);
        Assert.AreEqual(Visibility.Hidden, Element<UIElement>(home, "EditorPanel").Visibility);
        Assert.IsFalse(Element<UIElement>(home, "EditorPanel").IsEnabled);
        var petBefore = Bounds(home, "PetPreview");
        var navigationBefore = Bounds(home, "TopNavigation");
        Assert.AreEqual(width / 2d, petBefore.Left + petBefore.Width / 2, 1, "Pet starts centered.");
        Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(home, "HomeNavigation").Visibility,
            "The redundant top-level home label must stay hidden.");
        SaveImage(home, width, "closed");

        Click(home, "FaceButton");
        Layout(home, width);
        var petAfter = Bounds(home, "PetPreview");
        var panel = Bounds(home, "EditorPanel");
        Assert.IsTrue(petAfter.Left > petBefore.Left + 20, "Face options on the left must shift the pet right.");
        Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(home, "Launchers").Visibility,
            "Face editing gives the preview column to its own tools.");
        Assert.AreEqual(navigationBefore, Bounds(home, "TopNavigation"));
        Assert.IsTrue(panel.Right + 4 < petAfter.Left,
            "Face options must enter from the left without overlapping the pet.");
        Assert.IsTrue(panel.Right <= width && panel.Left > 0, "Panel stays inside the window.");
        Assert.IsTrue(petAfter.Left >= 0);
        Assert.IsTrue(Bounds(home, "AppearancePreviewNotice").Bottom <= Bounds(home, "ApplyButton").Top, "Detail text must not overlap apply.");
        Assert.IsFalse(Element<Button>(home, "ApplyButton").IsEnabled, "No fake save.");
        Assert.AreEqual("拖动即时预览 · 应用后保存", Element<TextBlock>(home, "AppearancePreviewNotice").Text);
        Assert.AreEqual(Visibility.Visible, Element<TextBlock>(home, "AppearancePreviewNotice").Visibility);
        SaveImage(home, width, "face");

        Click(home, "AppearanceEyesTab");
        Layout(home, width);
        Assert.AreEqual(petAfter, Bounds(home, "PetPreview"), "Switching appearance tabs must not shift the pet vertically.");
        Assert.IsTrue(Rect.Intersect(Bounds(home, "OptionsBody"), Bounds(home, "FaceEditorTools")).IsEmpty,
            "Parameters and fixed tools use separate columns.");
        SaveImage(home, width, "eyes");
        Click(home, "AppearanceColorsTab");

        Click(home, "PeachButton");
        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, width);
        Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
        var wardrobePet = Bounds(home, "PetPreview");
        var wardrobeButtons = Bounds(home, "Launchers");
        var wardrobePanel = Bounds(home, "EditorPanel");
        Assert.IsTrue(wardrobePet.Left < petBefore.Left - 20, "Wardrobe on the right must shift the pet left.");
        Assert.IsTrue(Math.Max(wardrobePet.Right, wardrobeButtons.Right) + 4 < wardrobePanel.Left,
            "Wardrobe options must mirror the face composition.");
        Assert.AreEqual(petBefore.Top, wardrobePet.Top, .1);
        Assert.IsFalse(Element<Button>(home, "CreateButton").IsEnabled);
        SaveImage(home, width, "wardrobe");
        Click(home, "CloseEditorButton");
        Layout(home, width);
        Assert.AreEqual(petBefore, Bounds(home, "PetPreview"));
        Assert.IsFalse(Element<UIElement>(home, "EditorPanel").IsHitTestVisible);
        Click(home, "FaceButton");
        Assert.AreEqual(Color.FromRgb(255, 232, 194), home.PreviewColor, "Leaving the editor cancels the whole draft.");
        Assert.AreEqual(Color.FromRgb(255, 232, 194), new ProductHomeView().PreviewColor, "A new instance must not claim persistence.");
    }

    [STATestMethod]
    public void RapidReverseEscAndReducedMotionLeaveOnlyTheLastRequestedState()
    {
        var home = new ProductHomeView();
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Pump(40);
            var closed = Bounds(home, "PetPreview");
            home.ReduceMotion = true;
            Click(home, "FaceButton");
            var open = Bounds(home, "PetPreview");
            home.CloseEditor();
            home.ReduceMotion = false;
            Click(home, "FaceButton");
            Pump(80);
            if (SystemParameters.ClientAreaAnimation)
            {
                var moving = Bounds(home, "PetPreview");
                Assert.IsTrue(moving.Left > closed.Left + 1 && moving.Left < open.Left - 1,
                    "With system animation enabled, the 80ms frame must be between start and end, not a static jump.");
            }
            Click(home, "FaceCloseEditorButton");
            Assert.IsFalse(Element<UIElement>(home, "EditorPanel").IsEnabled, "Closing immediately removes keyboard access.");
            Click(home, "WardrobeButton");
            Pump(520);
            Assert.IsTrue(home.IsEditing);
            Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(home, "EditorPanel").Visibility, "Old completion must not hide a reopened panel.");
            Assert.AreEqual(1, Element<UIElement>(home, "EditorPanel").Opacity, .001);
            Assert.IsTrue(Bounds(home, "PetPreview").Left < closed.Left - 20);
            Element<Button>(home, "CloseEditorButton").Focus();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            home.RaiseEvent(key);
            Assert.IsTrue(key.Handled);
            Assert.IsFalse(home.IsEditing);
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "Esc returns focus to the Dango controls instead of making Space reopen a launcher.");
            home.ReduceMotion = true;
            Assert.AreEqual(Visibility.Hidden, Element<UIElement>(home, "EditorPanel").Visibility);
            Assert.AreEqual(closed.Left, Bounds(home, "PetPreview").Left, .1);
            Click(home, "FaceButton");
            Assert.AreEqual(1, Element<UIElement>(home, "EditorPanel").Opacity, .001, "Reduced motion switches immediately.");
            home.CloseEditor();
            Assert.AreEqual(closed.Left, Bounds(home, "PetPreview").Left, .1);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void FacePanelExitsOnTheLeftAndLauncherActivationReturnsToGameplayFocus()
    {
        var home = new ProductHomeView();
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Element<Button>(home, "FaceButton").Focus();
            Click(home, "FaceButton");
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "After opening a panel, Space should act on the Dango instead of clicking the launcher again.");
            Assert.AreEqual(0, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")));

            Click(home, "FaceCloseEditorButton");
            Assert.AreEqual(0, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")),
                "An exiting face panel must not teleport to the opposite side before fading out.");
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused);
            Pump(520);
            Assert.AreEqual(Visibility.Hidden, Element<UIElement>(home, "EditorPanel").Visibility);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void NewlyShownHomeStartsWithGameplayFocus()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Pump(50);
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "The first Space key should animate the Dango rather than activate Settings or a launcher.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ChoosingFaceColorReturnsFocusToDangoActions()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Click(home, "FaceButton");
            Element<Button>(home, "PeachButton").Focus();
            Assert.IsTrue(Element<Button>(home, "PeachButton").IsKeyboardFocused);
            Click(home, "PeachButton");
            Assert.AreEqual(Color.FromRgb(242, 182, 168), home.PreviewColor);
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "After choosing a color, Space should jump instead of clicking the swatch again.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ProductEntryUsesApprovedHomeInsteadOfLocalSettingsOverview()
    {
        var window = new ControlCenterWindow(
            new ApplicationCommandController(new UnavailableHost(), ApplicationUiMode.Product),
            VerificationDefinition.CreateP1003WorkerW(),
            new VerificationSessionStore(VerificationDefinition.CreateP1003WorkerW()),
            Path.Combine(TestRepository.Root, "artifacts", "fictional-home-draft.json"), () => { });
        try
        {
            window.ShowOverview();
            var home = window.FindName("ProductHome") as FrameworkElement;
            Assert.IsNotNull(home, "Product entry must contain the approved home, not the local host settings overview.");
            Assert.AreEqual(Visibility.Visible, home.Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)window.FindName("LegacyShell")).Visibility);
            Assert.IsNotNull(home.FindName("FaceButton"), "捏脸 must not be deferred out of the approved home.");
            Assert.IsNotNull(home.FindName("WardrobeButton"), "换装 must not be deferred out of the approved home.");
            Assert.IsNotNull(home.FindName("EditorPanel"), "The horizontal detail panel is part of this increment.");
            Assert.AreEqual(336, window.MinWidth);
            Click(home, "SettingsButton");
            Assert.AreEqual(Visibility.Visible, ((UIElement)home.FindName("SettingsPage")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((UIElement)window.FindName("LegacyShell")).Visibility);
            Assert.IsNotNull(home.FindName("RetryInitializationButton"), "Recovery stays reachable in the accepted shell.");
            Assert.AreEqual(336, window.MinWidth);
            window.ShowOverview();
            Assert.AreEqual(Visibility.Visible, home.Visibility);
        }
        finally
        {
            window.PrepareForExit();
            window.Close();
        }
    }

    private static T Element<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new AssertFailedException($"Missing {name}");
    private static void Click(FrameworkElement root, string name) => Element<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Rect Bounds(FrameworkElement root, string name)
    {
        var element = Element<FrameworkElement>(root, name);
        return element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    }
    private static void Layout(FrameworkElement view, int width)
    {
        var size = new Size(width, 602);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void SaveImage(FrameworkElement root, int width, string state)
    {
        var bitmap = new RenderTargetBitmap(width, 602, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        using var stream = TestEvidence.Create("home-ui", $"{state}-{width}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private sealed class UnavailableHost : IDesktopHostControl
    {
        public DesktopHostSnapshot GetSnapshot() => new("Window", false, false, "fictional unavailable host", "未启动");
        public void SetVisibility(bool isVisible) => Assert.Fail("Home preview must not mutate the desktop host.");
        public void SetInteractionEnabled(bool isEnabled) => Assert.Fail("Home preview must not mutate the desktop host.");
        public string CreateDiagnosticSummary() => "fictional diagnostics";
    }
}
