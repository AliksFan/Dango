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
    [DataRow(480)]
    [DataRow(320)]
    public void HomeKeepsNavigationFixedAndMovesPetAndLaunchersLeftTogether(int width)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        Layout(home, width);
        Assert.AreEqual(Visibility.Hidden, Element<UIElement>(home, "EditorPanel").Visibility);
        Assert.IsFalse(Element<UIElement>(home, "EditorPanel").IsEnabled);
        var petBefore = Bounds(home, "PetPreview");
        var buttonsBefore = Bounds(home, "Launchers");
        var navigationBefore = Bounds(home, "TopNavigation");
        Assert.AreEqual(width / 2d, petBefore.Left + petBefore.Width / 2, 1, "Pet starts centered.");
        SaveImage(home, width, "closed");

        Click(home, "FaceButton");
        Layout(home, width);
        var petAfter = Bounds(home, "PetPreview");
        var buttonsAfter = Bounds(home, "Launchers");
        var panel = Bounds(home, "EditorPanel");
        Assert.IsTrue(petAfter.Left < petBefore.Left - 20, "Opening must actually shift the pet left.");
        Assert.AreEqual(petBefore.Top, petAfter.Top, .1, "Opening must not move the pet vertically.");
        Assert.AreEqual(buttonsBefore.Top, buttonsAfter.Top, .1);
        Assert.AreEqual(petAfter.Left - petBefore.Left, buttonsAfter.Left - buttonsBefore.Left, .1);
        Assert.AreEqual(navigationBefore, Bounds(home, "TopNavigation"));
        Assert.IsTrue(Math.Max(petAfter.Right, buttonsAfter.Right) + 4 < panel.Left, "Two horizontal columns must not overlap.");
        Assert.IsTrue(panel.Right <= width && panel.Left > 0, "Panel stays inside the window.");
        Assert.IsTrue(petAfter.Left >= 0 && buttonsAfter.Left >= 0);
        Assert.IsTrue(Bounds(home, "MoreOptions").Bottom <= Bounds(home, "ApplyButton").Top, "Detail text must not overlap apply.");
        Assert.IsFalse(Element<Button>(home, "ApplyButton").IsEnabled, "No fake save.");
        SaveImage(home, width, "face");

        Element<Expander>(home, "MoreOptions").IsExpanded = true;
        Layout(home, width);
        Assert.AreEqual(petAfter, Bounds(home, "PetPreview"), "Expanding detail must not shift the pet vertically.");
        Assert.IsTrue(Bounds(home, "OptionsBody").Bottom <= Bounds(home, "ApplyButton").Top);
        Assert.IsTrue(Bounds(home, "MoreOptions").Bottom <= Bounds(home, "OptionsBody").Bottom + 1,
            "Expanded detail must remain above the footer.");
        SaveImage(home, width, "more");
        Element<Expander>(home, "MoreOptions").IsExpanded = false;

        Click(home, "PeachButton");
        Click(home, "WardrobeButton");
        Layout(home, width);
        Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
        Assert.AreEqual(petAfter, Bounds(home, "PetPreview"), "Switching editors keeps the pet in place.");
        Assert.IsFalse(Element<Button>(home, "CreateButton").IsEnabled);
        SaveImage(home, width, "wardrobe");
        Click(home, "CloseEditorButton");
        Layout(home, width);
        Assert.AreEqual(petBefore, Bounds(home, "PetPreview"));
        Assert.IsFalse(Element<UIElement>(home, "EditorPanel").IsHitTestVisible);
        Click(home, "FaceButton");
        Assert.AreEqual(Color.FromRgb(242, 182, 168), home.PreviewColor, "Close/reopen preserves only the in-memory preview.");
        Assert.AreEqual(Color.FromRgb(245, 213, 165), new ProductHomeView().PreviewColor, "A new instance must not claim persistence.");
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
                Assert.IsTrue(moving.Left < closed.Left - 1 && moving.Left > open.Left + 1,
                    "With system animation enabled, the 80ms frame must be between start and end, not a static jump.");
            }
            Click(home, "CloseEditorButton");
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
            Assert.IsTrue(Element<Button>(home, "WardrobeButton").IsKeyboardFocused, "Esc restores the originating launcher focus.");
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
            Click(home, "NoticeButton");
            Assert.AreEqual(Visibility.Visible, ((UIElement)window.FindName("DesktopPage")).Visibility);
            Assert.AreSame(window.FindName("RecoveryActions"), ((Border)window.FindName("ProductRecoverySlot")).Child,
                "Real retry/restore actions remain reachable through existing settings.");
            Assert.AreEqual(820, window.MinWidth, "Existing settings keep their supported minimum size.");
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
