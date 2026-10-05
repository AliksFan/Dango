using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dango.Desktop.AppShell.ControlCenter;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class HomePresentationLayoutTests
{
    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(960, 540)]
    [DataRow(360, 640)]
    public void HomeFaceAndWardrobeHaveTheSamePreviewTop(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        Layout(home, size);
        var closed = Bounds(home, "PetPreview");
        home.ShowEditor(HomeEditor.Face);
        Layout(home, size);
        var face = Bounds(home, "PetPreview");
        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        var wardrobe = Bounds(home, "PetPreview");
        var evidence = $"At {width}x{height}: home {closed}, face {face}, wardrobe {wardrobe}.";
        Assert.AreEqual(closed.Top, face.Top, 1, "Home and face must share one preview baseline. " + evidence);
        Assert.AreEqual(closed.Top, wardrobe.Top, 1, "Home and wardrobe must share one preview baseline. " + evidence);
    }

    [STATestMethod]
    [DataRow(480, 270)]
    [DataRow(640, 360)]
    [DataRow(683, 384)]
    [DataRow(736, 620)]
    [DataRow(853, 480)]
    [DataRow(960, 540)]
    [DataRow(1024, 576)]
    [DataRow(1280, 720)]
    [DataRow(360, 640)]
    [DataRow(480, 800)]
    public void HomeFaceAndWardrobeKeepOnePreviewBaselineAndANoninteractiveGroundShadow(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        Layout(home, size);
        var baseline = Bounds(home, "PetPreview");
        AssertShadowBelowPreview(home, baseline);
        AssertNoBottomShortcutDescription(home);

        foreach (var editor in new[] { HomeEditor.Face, HomeEditor.Wardrobe })
        {
            home.ShowEditor(editor);
            Layout(home, size);
            var preview = Bounds(home, "PetPreview");
            Assert.AreEqual(baseline.Top, preview.Top, 1,
                $"{editor} must keep the same grounded baseline as the closed home at {width}x{height}.");
            Assert.AreEqual(baseline.Size, preview.Size,
                $"{editor} must preserve the normal Dango preview size.");
            AssertShadowBelowPreview(home, preview);
            AssertNoBottomShortcutDescription(home);
            if (height > width * 1.2)
                Assert.IsTrue(Bounds(home, "EditorPanel").Top >= preview.Bottom + 8,
                    $"{editor} portrait options must remain below the preview.");
            if (editor == HomeEditor.Face)
            {
                foreach (var name in new[]
                {
                    "AppearanceColorsTab", "AppearanceEyesTab", "PreviewBlinkButton", "PreviewSleepButton",
                    "AppearanceStatus", "DefaultAppearanceButton", "CancelAppearanceButton", "ApplyButton",
                })
                {
                    AssertNoOverlap(preview, Bounds(home, name), name + " and the Dango preview");
                    AssertNoOverlap(Bounds(home, "PetShadow"), Bounds(home, name), name + " and the ground shadow");
                }
            }
        }
        home.CloseEditor();
        Layout(home, size);
        Assert.AreEqual(baseline, Bounds(home, "PetPreview"),
            "Closing the editor must return to the same home preview geometry.");
    }

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(960, 540)]
    [DataRow(360, 640)]
    public void HomeIconEntriesExposeDAndAAndPlaceTheWorkingWDoorAboveThem(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        Layout(home, size);
        foreach (var (name, key, action) in new[]
        {
            ("FaceButton", "D", "捏脸"), ("WardrobeButton", "A", "换装"), ("EnterRoomButton", "W", "门廊"),
        })
        {
            var button = Element<Button>(home, name);
            Assert.IsTrue(button.IsEnabled && button.IsHitTestVisible, $"{name} must be an available home entry.");
            var letter = Descendants<TextBlock>(button).Single(text => text.Text == key);
            var keycap = Descendants<Border>(button).SingleOrDefault(border => ReferenceEquals(border.Child, letter));
            Assert.IsNotNull(keycap, $"{name} must enclose the {key} letter in a real keycap Border.");
            Assert.AreEqual(22, keycap.Width, .001, "Shortcut keycaps need a consistent 22-DIP width.");
            Assert.AreEqual(22, keycap.Height, .001, "Shortcut keycaps need a consistent 22-DIP height.");
            Assert.AreEqual(new CornerRadius(4), keycap.CornerRadius, "Shortcut keycaps need rounded, recognizable key outlines.");
            Assert.AreEqual(new Thickness(1), keycap.BorderThickness, "The shortcut letter must have a real visible key outline.");
            Assert.IsNotNull(keycap.BorderBrush, "A keycap Border must actually draw its outline.");
            Assert.AreEqual(button.Foreground, letter.Foreground,
                "Shortcut letters must retain the button foreground through their new keycap container.");
            var letterBounds = letter.TransformToAncestor(keycap).TransformBounds(new Rect(letter.RenderSize));
            AssertInside(new Rect(keycap.RenderSize), letterBounds, key + " letter within its keycap");
            var icon = Descendants<Viewbox>(button).Single();
            var capBounds = keycap.TransformToAncestor(button).TransformBounds(new Rect(keycap.RenderSize));
            var iconBounds = icon.TransformToAncestor(button).TransformBounds(new Rect(icon.RenderSize));
            Assert.AreEqual(8, iconBounds.Left - capBounds.Right, 1,
                "Shortcut keycaps need an 8-DIP gap before their action icon.");
            Assert.IsTrue(Descendants<Shape>(button).Any(shape => shape.ActualWidth > 0 && shape.ActualHeight > 0),
                $"{name} must render a vector action icon rather than repeat a text-only label.");
            StringAssert.Contains(button.ToolTip?.ToString() ?? string.Empty, key,
                $"{name} needs a readable shortcut hint.");
            StringAssert.Contains(AutomationProperties.GetName(button), action,
                $"{name} needs an understandable accessible action name.");
            AssertInside(Bounds(home, "Stage"), Bounds(home, name), name);
            AssertNoOverlap(Bounds(home, "PetPreview"), Bounds(home, name), name + " and the Dango preview");
            AssertNoOverlap(Bounds(home, "PetShadow"), Bounds(home, name), name + " and the ground shadow");
        }
        var door = Bounds(home, "EnterRoomButton");
        Assert.IsTrue(door.Bottom + 3 <= Math.Min(Bounds(home, "FaceButton").Top, Bounds(home, "WardrobeButton").Top),
            "The W door must form a separate row above the D/A entries.");
        AssertNoBottomShortcutDescription(home);
        Click(home, "EnterRoomButton");
        Assert.IsTrue(home.IsInPorch, "The W door must enter the existing porch instead of showing a cosmetic response.");
        Assert.AreEqual(Visibility.Visible, Element<FrameworkElement>(home, "PorchContent").Visibility);
    }

    [STATestMethod]
    public void FaceUsesADangoBrandIconAndOtherHomeStatesRestoreTheTextBrand()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(960, 540);
        Layout(home, size);
        var brand = Element<TextBlock>(home, "Brand");
        var icon = Element<FrameworkElement>(home, "FaceBrandIcon");
        Assert.AreEqual(Visibility.Visible, brand.Visibility);
        Assert.AreEqual(Visibility.Collapsed, icon.Visibility);
        home.ShowEditor(HomeEditor.Face);
        Layout(home, size);
        Assert.AreEqual(Visibility.Collapsed, brand.Visibility, "Face mode must release the text brand.");
        Assert.AreEqual(Visibility.Visible, icon.Visibility, "Face mode must display its Dango brand icon.");
        Assert.IsTrue(icon.ActualWidth > 0 && icon.ActualHeight > 0);
        Assert.IsTrue(Descendants<Shape>(icon).Any(), "The face brand must be an actual drawn Dango icon.");
        Assert.IsFalse(icon.IsHitTestVisible, "The decorative brand must not intercept window or navigation input.");
        AssertNoOverlap(Bounds(home, "FaceBrandIcon"), Bounds(home, "ToolsNavigation"), "brand and home navigation");
        home.ShowEditor(HomeEditor.Wardrobe);
        Layout(home, size);
        Assert.AreEqual(Visibility.Visible, brand.Visibility);
        Assert.AreEqual(Visibility.Collapsed, icon.Visibility);
    }

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(360, 640)]
    public void FaceSaveIconsShareARightAlignedRowBelowTheShadowAndRemainReachable(int width, int height)
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(width, height);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Face);
        Layout(home, size);
        var names = new[] { "DefaultAppearanceButton", "CancelAppearanceButton", "ApplyButton" };
        var stage = Bounds(home, "Stage");
        var shadow = Bounds(home, "PetShadow");
        var row = names.Select(name => Bounds(home, name)).ToArray();
        foreach (var (name, index) in names.Select((name, index) => (name, index)))
        {
            var button = Element<Button>(home, name);
            Assert.IsTrue(Descendants<Shape>(button).Any(shape => shape.ActualWidth > 0 && shape.ActualHeight > 0),
                $"{name} must render an actual action icon.");
            Assert.IsFalse(Descendants<TextBlock>(button).Any(text => text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)),
                $"{name} must use an icon without a visible action label.");
            Assert.IsFalse(button.Content is string textLabel && !string.IsNullOrWhiteSpace(textLabel));
            Assert.IsFalse(string.IsNullOrWhiteSpace(button.ToolTip?.ToString()), $"{name} needs a readable tooltip.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), $"{name} needs an accessible action name.");
            Assert.AreEqual(row[0].Top, row[index].Top, 1, "The three save icons must share one row.");
            Assert.IsTrue(row[index].Top >= shadow.Bottom + 3,
                $"{name} must stay below the ground shadow; button {row[index]}, shadow {shadow}.");
            AssertInside(stage, row[index], name);
        }
        Assert.IsTrue(row[0].Right + 3 <= row[1].Left && row[1].Right + 3 <= row[2].Left,
            "Default, cancel and apply must keep separate hit targets in that order.");
        Assert.AreEqual(Bounds(home, "FaceEditorSaveActions").Right, row[2].Right, 1,
            "The apply icon must align to the right edge of the fixed saving area.");
        Element<ScrollViewer>(home, "HomeContent").ScrollToBottom();
        home.UpdateLayout();
        foreach (var name in names)
            AssertInside(Bounds(home, "HomeContent"), Bounds(home, name), name + " after scrolling");
    }

    [STATestMethod]
    public void LauncherShortcutLettersFollowTheSelectedWardrobeForegroundInHighContrast()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var size = new Size(960, 540);
        Layout(home, size);
        home.ShowEditor(HomeEditor.Wardrobe);
        home.ApplyTheme(contrast: true);
        Layout(home, size);
        var wardrobe = Element<Button>(home, "WardrobeButton");
        Assert.AreEqual(SystemColors.HighlightBrush, wardrobe.Background);
        Assert.AreEqual(SystemColors.HighlightTextBrush, wardrobe.Foreground);
        foreach (var (name, key) in new[] { ("FaceButton", "D"), ("WardrobeButton", "A"), ("EnterRoomButton", "W") })
        {
            var button = Element<Button>(home, name);
            var letter = Descendants<TextBlock>(button).Single(text => text.Text == key);
            Assert.AreEqual(button.Foreground, letter.Foreground,
                $"The {key} shortcut must follow its button foreground, including selected high-contrast colors.");
        }
    }

    private static void AssertShadowBelowPreview(ProductHomeView home, Rect preview)
    {
        var shadow = Element<Ellipse>(home, "PetShadow");
        var bounds = Bounds(home, "PetShadow");
        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0, "The ground shadow must have an actual footprint.");
        Assert.IsTrue(bounds.Top >= preview.Bottom - 1,
            $"The ground shadow must sit below the grounded preview; shadow {bounds}, preview {preview}.");
        Assert.AreEqual(preview.Left + preview.Width / 2, bounds.Left + bounds.Width / 2, 1,
            "The shadow must follow the Dango's horizontal presentation anchor.");
        Assert.IsFalse(shadow.IsHitTestVisible, "A ground shadow must not intercept pet or home clicks.");
        Assert.IsFalse(shadow.Focusable, "A decorative shadow must not become a keyboard target.");
        AssertInside(Bounds(home, "Stage"), bounds, "ground shadow");
    }
    private static void AssertNoBottomShortcutDescription(ProductHomeView home)
    {
        if (home.FindName("PreviewStatus") is FrameworkElement legacy)
            Assert.AreEqual(Visibility.Collapsed, legacy.Visibility,
                "The removed bottom shortcut explanation must not reserve visible space.");
    }
    private static T Element<T>(ProductHomeView home, string name) where T : class =>
        home.FindName(name) as T ?? throw new AssertFailedException($"Missing compiled home presentation control: {name}");
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        foreach (var item in Descendants<T>(child)) yield return item;
    }
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
        var element = Element<FrameworkElement>(home, name);
        return element.TransformToAncestor(home).TransformBounds(new Rect(element.RenderSize));
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
}
