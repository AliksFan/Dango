using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Accessories;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductHomeInteractionTests
{
    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(960, 540)]
    [DataRow(960, 640)]
    [DataRow(1280, 720)]
    public void LandscapeFaceEditorFitsViewportAndKeepsActionsReachable(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = new Window { Content = home, Width = width, Height = height, Left = -10000, Top = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
        try
        {
            window.Show(); Arrange(); home.ShowEditor(HomeEditor.Face); Arrange();
            foreach (var tab in new[] { "AppearanceColorsTab", "AppearanceEyesTab" })
            {
                Click(home, tab); Arrange();
                var viewport = Element<ScrollViewer>(home, "HomeContent");
                Assert.IsLessThanOrEqualTo(.5, viewport.ScrollableHeight,
                    $"{width}x{height} {tab}: stage={Element<Grid>(home, "Stage").ActualHeight}, viewport={viewport.ViewportHeight}; the whole editor must fit without page scrolling.");
                foreach (var name in new[] { "ApplyButton", "CancelAppearanceButton", "DefaultAppearanceButton" })
                {
                    var bounds = Bounds(home, name);
                    Assert.IsTrue(bounds.Top >= 0 && bounds.Bottom <= height && bounds.Left >= 0 && bounds.Right <= width, $"{name}: {bounds}");
                    var button = Element<Button>(home, name);
                    var point = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    var hit = home.InputHitTest(point) as DependencyObject;
                    // Apply is intentionally disabled until a valid draft exists; the other actions must be clickable.
                    if (button.IsEnabled) Assert.IsTrue(ReferenceEquals(button, hit) || hit is not null && button.IsAncestorOf(hit), $"{name} must not be clipped by a scroll ancestor.");
                }
            }
        }
        finally { window.Close(); }

        void Arrange()
        {
            window.UpdateLayout(); Pump(20); window.UpdateLayout();
        }
    }

    [STATestMethod]
    [DataRow(1024)]
    [DataRow(736)]
    [DataRow(640)]
    public void HomeKeepsNavigationFixedAndMirrorsFaceAndWardrobePanels(int width)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var accessoryRoot = Path.Combine(TestRepository.Root, "artifacts", "home-interaction", "accessories", Guid.NewGuid().ToString("N"));
        var accessorySession = new AccessorySettingsSession(accessoryRoot); home.BindAccessories(accessorySession);
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
        Assert.IsTrue(petAfter.Left < petBefore.Left - 20, "Face options on the right must shift the pet left.");
        Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(home, "Launchers").Visibility,
            "Face editing gives the preview column to its own tools.");
        Assert.AreEqual(navigationBefore, Bounds(home, "TopNavigation"));
        Assert.IsTrue(petAfter.Right + 4 < panel.Left,
            "Face options must enter from the right without overlapping the pet.");
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
        var wardrobePanel = Bounds(home, "EditorPanel");
        Assert.AreEqual(Visibility.Collapsed, Element<UIElement>(home, "Launchers").Visibility,
            "Wardrobe editing must give the preview area its full space.");
        Assert.IsTrue(wardrobePet.Left > petBefore.Left + 20, "Wardrobe on the left must shift the pet right.");
        Assert.IsTrue(wardrobePanel.Right + 4 < wardrobePet.Left,
            "Wardrobe options must mirror the face composition.");
        Assert.AreEqual(petBefore.Top, wardrobePet.Top, .1);
        var accessoryEditor = Element<StackPanel>(home, "WardrobeOptions").Children.OfType<AccessoryEditorPanel>().Single();
        var accessoryButtons = Descendants<Button>(Element<ContentControl>(home, "AccessoryActionToolsSlot")).ToArray();
        Assert.IsTrue(accessoryButtons.Single(button => AutomationProperties.GetName(button) == "创建新配饰").IsEnabled,
            "Creating a new accessory must expose the real transparent PNG selection action.");
        accessoryEditor.SetBodyAnchor(new Point(.25, .4));
        var editedAccessoryId = accessoryEditor.SelectedItem!.Id;
        Assert.IsTrue(accessorySession.IsDirty);
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(accessoryRoot)), "Editing must only preview before applying.");
        Descendants<Button>(Element<ContentControl>(home, "AccessorySaveActionsSlot"))
            .Single(button => AutomationProperties.GetName(button) == "应用")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(accessorySession.IsDirty);
        var reloadedAccessories = new AccessorySettingsSession(accessoryRoot).SavedConfiguration.Items;
        Assert.HasCount(3, reloadedAccessories, "Applying one edit must preserve all three existing built-in library entries.");
        Assert.AreEqual(.25, reloadedAccessories.Single(item => item.Id == editedAccessoryId).BodyAnchorX);
        Assert.AreEqual(.4, reloadedAccessories.Single(item => item.Id == editedAccessoryId).BodyAnchorY);
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
                Assert.IsTrue(moving.Left < closed.Left - 1 && moving.Left > open.Left + 1,
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
            Assert.IsTrue(Bounds(home, "PetPreview").Left > closed.Left + 20);
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
    [DataRow(HomeEditor.Face, 2, 1, "FaceButton", "FaceCloseEditorButton")]
    [DataRow(HomeEditor.Wardrobe, 0, -1, "WardrobeButton", "CloseEditorButton")]
    public void EditorPanelsExitOnTheirEntrySideAndLauncherActivationReturnsToGameplayFocus(
        HomeEditor editor, int column, int entryDirection, string launcher, string closeButton)
    {
        var home = new ProductHomeView();
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Pump(60); // Let the initial size and layout events settle before measuring an editor transition.
            Element<Button>(home, launcher).Focus();
            Click(home, launcher);
            Assert.AreEqual(editor, home.SelectedEditor);
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused,
                "After opening a panel, Space should act on the Dango instead of clicking the launcher again.");
            Assert.AreEqual(column, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")));
            var translation = Element<TranslateTransform>(home, "PanelTranslation");
            if (SystemParameters.ClientAreaAnimation)
            {
                Pump(20); // Sample after WPF has rendered the first animation frame, not its zero-valued base target.
                var startingOffset = translation.X;
                Assert.IsTrue(startingOffset * entryDirection > 0,
                    $"{editor} must enter from its own side of the window.");
                Pump(80);
                Assert.IsTrue(translation.X * entryDirection > 0 && Math.Abs(translation.X) < Math.Abs(startingOffset),
                    $"{editor} must animate from its entry side toward its resting position.");
            }
            Pump(520);
            Assert.AreEqual(0, translation.X, .001, "An open editor must rest at its assigned column without an extra offset.");

            Click(home, closeButton);
            Assert.AreEqual(column, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")),
                "An exiting panel must not teleport to the opposite side before fading out.");
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused);
            if (SystemParameters.ClientAreaAnimation)
            {
                Pump(80);
                Assert.IsTrue(translation.X * entryDirection > 0,
                    $"{editor} must exit toward the same side it entered from.");
                Assert.AreEqual(column, Grid.GetColumn(Element<UIElement>(home, "EditorPanel")),
                    "The closing animation must retain its original column until it finishes.");
            }
            Pump(520);
            Assert.AreEqual(Visibility.Hidden, Element<UIElement>(home, "EditorPanel").Visibility);
            Assert.IsTrue(translation.X * entryDirection > 0,
                $"The hidden {editor} panel must finish at its own off-screen-side offset.");
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
    public void WardrobeControlKeysDoNotNavigateAndEscapeCancelsWhileOtherHomeShortcutsRemain()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "home-ui", "fictional-keyboard-" + Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        Assert.IsTrue(session.TrySave());
        var saved = session.SavedConfiguration;
        var savedBytes = File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root));
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAccessories(session);
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Pump(50);
            Assert.IsTrue(RaiseKey(home, window, Key.D, Keyboard.PreviewKeyDownEvent).Handled);
            Assert.AreEqual(HomeEditor.Face, home.SelectedEditor, "Closed home retains D as the face shortcut.");
            Assert.IsTrue(RaiseKey(home, window, Key.A, Keyboard.PreviewKeyDownEvent).Handled);
            Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor, "A outside wardrobe retains the wardrobe shortcut.");
            var panel = Element<StackPanel>(home, "WardrobeOptions").Children.OfType<AccessoryEditorPanel>().Single();
            panel.SetBodyAnchor(new Point(.34, .59));
            var draft = session.DraftConfiguration;

            foreach (var key in new[] { Key.A, Key.D, Key.W, Key.S, Key.Space })
            {
                RaiseKey(home, window, key, Keyboard.PreviewKeyDownEvent);
                RaiseKey(home, window, key, Keyboard.PreviewKeyUpEvent);
                Assert.IsTrue(home.IsEditing, key + " must remain within wardrobe control.");
                Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor, key + " must not switch the accessory preview to face.");
                Assert.IsFalse(home.IsInPorch, "W in the wardrobe must not enter the porch.");
                Assert.AreEqual(draft, session.DraftConfiguration, "Gameplay key routing must not reset accessory parameters.");
            }

            Assert.IsTrue(panel.SelectSection(AccessoryEditorSection.Placement));
            Pump(20);
            var name = Descendants<TextBox>(panel).Single(input => AutomationProperties.GetName(input) == "配饰名称");
            Assert.IsTrue(name.Focus(), "The editable field must own actual WPF focus.");
            foreach (var key in new[] { Key.A, Key.D, Key.W, Key.S, Key.Space })
            {
                RaiseKey(home, window, key, Keyboard.PreviewKeyDownEvent);
                RaiseKey(home, window, key, Keyboard.PreviewKeyUpEvent);
                Assert.IsTrue(name.IsKeyboardFocused, "Editor input must preserve field focus rather than route into gameplay or navigation.");
                Assert.IsTrue(home.IsEditing);
                Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
                Assert.IsFalse(home.IsInPorch);
                Assert.AreEqual(draft, session.DraftConfiguration);
            }

            name.Text = "虚构未保存名称";
            Assert.IsTrue(RaiseKey(home, window, Key.Escape, Keyboard.PreviewKeyDownEvent).Handled);
            Assert.IsFalse(home.IsEditing);
            Assert.AreEqual(saved, session.DraftConfiguration, "Esc must cancel all unsaved accessory parameters and buffered field text.");
            CollectionAssert.AreEqual(savedBytes, File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            Assert.IsTrue(Element<FrameworkElement>(home, "Stage").IsKeyboardFocused);
            Assert.IsTrue(RaiseKey(home, window, Key.D, Keyboard.PreviewKeyDownEvent).Handled);
            Assert.AreEqual(HomeEditor.Face, home.SelectedEditor, "Closing wardrobe must restore the home D shortcut.");
            Assert.IsTrue(RaiseKey(home, window, Key.A, Keyboard.PreviewKeyDownEvent).Handled);
            Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
            Assert.IsTrue(panel.SelectSection(AccessoryEditorSection.Placement));
            Assert.AreEqual(saved.Items.Single(item => item.Id == panel.SelectedItem!.Id).Name, name.Text,
                "Reopening the editor must not resurrect canceled buffered text.");
            Assert.AreEqual(saved, session.DraftConfiguration);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void EscapeInFocusedSettingsDiagnosticsDoesNotNavigateHome()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var window = new Window { Content = home, Width = 1024, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            home.SetDiagnosticsText("fictional diagnostic text for keyboard selection");
            home.ShowSettingsPage(showDiagnostics: true);
            Pump(100);
            var diagnostics = Element<TextBox>(home, "SettingsDiagnosticsText");
            diagnostics.Focus();
            Assert.IsTrue(diagnostics.IsKeyboardFocused,
                "This regression requires the diagnostic text box to own keyboard focus.");
            Assert.IsTrue(home.IsSettingsOpen);
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(home, "SettingsPage").Visibility);

            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            home.RaiseEvent(escape);

            Assert.IsFalse(escape.Handled,
                "The home must leave Escape to the focused diagnostic text box instead of intercepting it as navigation.");
            Assert.IsTrue(home.IsSettingsOpen, "Escape in diagnostic text must preserve the current settings page.");
            Assert.AreEqual(Visibility.Visible, Element<UIElement>(home, "SettingsPage").Visibility);
            Assert.IsTrue(diagnostics.IsKeyboardFocused, "Escape must not transfer diagnostic focus to the Dango stage.");
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
            Assert.IsNotNull(home.FindName("ReinitializeSceneButton"), "Recovery stays reachable in the accepted shell.");
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
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants<T>(child)) yield return nested;
    }
    private static void Click(FrameworkElement root, string name) => Element<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static KeyEventArgs RaiseKey(ProductHomeView home, Window window, Key key, RoutedEvent routedEvent)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = routedEvent };
        home.RaiseEvent(input);
        return input;
    }
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
