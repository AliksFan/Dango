using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Resources;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Resource and template behavior only: no window, activation, global input, or native runtime.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SharedThemeTests
{
    [STATestMethod]
    public void SharedOrdinaryButtonsRetainHomeAndStandaloneSelectedHoverDifference()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var standalone = new StackPanel();
        AccessoryEditorTheme.Install(standalone);
        var homeButton = StyledButton(home, typeof(Button));
        var accessoryButton = StyledButton(standalone, typeof(Button));
        foreach (var button in new[] { homeButton, accessoryButton })
        {
            Assert.AreEqual(40d, button.MinHeight);
            Assert.AreEqual(new Thickness(14, 8, 14, 8), button.Padding);
            Assert.AreEqual(new CornerRadius(7), Chrome(button).CornerRadius);
            button.Tag = "Selected";
            SetTemplateState(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
        }
        Assert.AreSame(home.FindResource("HomeSelected"), Chrome(homeButton).Background);
        Assert.AreSame(standalone.FindResource("HomeSoft"), Chrome(accessoryButton).Background);
        foreach (var (owner, button) in new[] { ((FrameworkElement)home, homeButton), (standalone, accessoryButton) })
        {
            button.Tag = "Primary";
            Assert.AreSame(owner.FindResource("HomeSelected"), Chrome(button).Background);
            button.IsEnabled = false;
            Assert.AreSame(owner.FindResource("HomeSoft"), Chrome(button).Background);
            Assert.AreSame(owner.FindResource("HomeMuted"), button.Foreground);
            Assert.AreEqual(Cursors.Arrow, button.Cursor);
        }
    }

    [STATestMethod]
    public void IconStylesRetainDifferentSelectionPressedAndDisabledChrome()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var standalone = new StackPanel();
        AccessoryEditorTheme.Install(standalone);
        var homeIcon = StyledButton(home, "FaceIconButton");
        var accessoryIcon = StyledButton(standalone, "FaceIconButton");
        foreach (var icon in new[] { homeIcon, accessoryIcon })
        {
            Assert.AreEqual(32d, icon.Width);
            Assert.AreEqual(32d, icon.Height);
            Assert.AreEqual(32d, icon.MinHeight);
            Assert.AreEqual(250, ToolTipService.GetInitialShowDelay(icon));
            icon.Tag = "Selected";
        }
        Assert.AreSame(home.FindResource("HomeSelected"), Chrome(homeIcon).Background);
        Assert.AreEqual(Colors.Transparent, ((SolidColorBrush)Chrome(accessoryIcon).Background).Color);
        Assert.AreSame(standalone.FindResource("HomeAccent"), Chrome(accessoryIcon).BorderBrush);
        homeIcon.Tag = null;
        SetTemplateState(homeIcon, typeof(System.Windows.Controls.Primitives.ButtonBase), "IsPressedPropertyKey", true);
        Assert.AreSame(home.FindResource("HomeSelected"), Chrome(homeIcon).Background);
        homeIcon.IsEnabled = false;
        accessoryIcon.IsEnabled = false;
        Assert.AreEqual(.45d, Chrome(homeIcon).Opacity);
        Assert.AreEqual(1d, Chrome(accessoryIcon).Opacity);
        Assert.AreSame(standalone.FindResource("HomeMuted"), accessoryIcon.Foreground);
    }

    [STATestMethod]
    public void AdoptedAccessoryUsesLiveParentPaletteAndKeepsItsOwnNumberAndLibraryStyles()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        var parent = new StackPanel { Resources = home.Resources };
        var child = new StackPanel();
        AccessoryEditorTheme.Install(child);
        parent.Children.Add(child);
        AccessoryEditorTheme.AdoptParent(child);
        Assert.AreSame(home.FindResource("FaceIconButton"), child.FindResource("FaceIconButton"));
        var number = new TextBox();
        number.SetResourceReference(FrameworkElement.StyleProperty, "AccessoryInlineNumber");
        var item = new ListBoxItem { Content = "Fictional accessory", IsSelected = true };
        item.SetResourceReference(FrameworkElement.StyleProperty, "AccessoryLibraryItem");
        child.Children.Add(number);
        child.Children.Add(item);
        Layout(parent);
        Assert.AreEqual(14d, number.FontSize);
        Assert.AreEqual(new Thickness(0, 0, 0, 1), number.BorderThickness);
        home.ApplyTheme(true);
        Assert.AreSame(SystemColors.WindowTextBrush, number.Foreground);
        Assert.AreSame(SystemColors.HighlightBrush, ((Border)item.Template.FindName("Chrome", item)).Background);
        var replacement = new SolidColorBrush(Colors.Magenta);
        parent.Resources["HomeInk"] = replacement;
        Assert.AreSame(replacement, number.Foreground, "A copied fallback palette must not shadow a live parent change.");
        Assert.AreSame(replacement, child.GetValue(TextBlock.ForegroundProperty));
        home.ApplyTheme(false);
        Assert.AreEqual(Color.FromRgb(0x44, 0x3D, 0x34), ((SolidColorBrush)number.Foreground).Color);
    }

    [STATestMethod]
    public void StandalonePaletteReplacementUpdatesRealButtonAndNumberWithoutRebuildingControls()
    {
        var owner = new StackPanel();
        AccessoryEditorTheme.Install(owner);
        var button = AccessoryEditorTheme.IconButton("虚构应用", AccessoryEditorTheme.ApplyIcon, "虚构应用", () => { });
        var number = AccessoryEditorTheme.Number("虚构范围", "虚构范围", -.5, 2.5, .125, _ => { });
        owner.Children.Add(button);
        owner.Children.Add(number.Row);
        Layout(owner);
        Assert.AreEqual("Segoe UI, Microsoft YaHei", button.FontFamily.Source);
        Assert.AreEqual(14d, button.FontSize);
        Assert.AreEqual(-.5, number.Slider.Minimum);
        Assert.AreEqual(2.5, number.Slider.Maximum);
        Assert.AreEqual(.125, number.Slider.Value);
        var fallback = owner.Resources.MergedDictionaries.Single();
        DesktopTheme.ApplyPalette(fallback, true);
        Assert.AreSame(SystemColors.WindowTextBrush, button.Foreground);
        Assert.AreSame(SystemColors.WindowTextBrush, number.Value.Foreground);
        button.IsEnabled = false;
        Assert.AreSame(SystemColors.WindowBrush, Chrome(button).Background);
        DesktopTheme.ApplyPalette(fallback, false);
        Assert.AreEqual(Color.FromRgb(0x80, 0x75, 0x68), ((SolidColorBrush)button.Foreground).Color);
    }

    [STATestMethod]
    public void IndependentEditorCopiesHomeChromeAndCanRefreshItsPaletteWithoutChangingSource()
    {
        var home = new ProductHomeView { ReduceMotion = true };
        home.ApplyTheme(true);
        var editor = new StackPanel();
        AccessoryEditorTheme.Install(editor);
        AccessoryEditorTheme.CopyFrom(home, editor);
        var button = AccessoryEditorTheme.IconButton("虚构确认", AccessoryEditorTheme.ApplyIcon, "虚构确认", () => { });
        editor.Children.Add(button);
        Layout(editor);
        Assert.AreSame(home.FindResource("FaceIconButton"), button.Style);
        Assert.AreSame(SystemColors.WindowTextBrush, button.Foreground);
        Assert.IsNotNull(editor.FindResource("AccessoryInlineNumber"));
        DesktopTheme.ApplyPalette(editor.Resources.MergedDictionaries.Single(), false);
        Assert.AreEqual(Color.FromRgb(0x44, 0x3D, 0x34), ((SolidColorBrush)button.Foreground).Color);
        Assert.AreSame(SystemColors.WindowTextBrush, home.FindResource("HomeInk"));
    }

    private static Button StyledButton(FrameworkElement owner, object key)
    {
        var holder = new StackPanel { Resources = owner.Resources };
        var button = new Button { Content = "Fictional action", Style = (Style)owner.FindResource(key) };
        holder.Children.Add(button);
        Layout(holder);
        return button;
    }

    private static Border Chrome(Button button) => (Border)button.Template.FindName("Chrome", button);

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(800, 600));
        element.Arrange(new Rect(0, 0, 800, 600));
        element.UpdateLayout();
    }

    // Inject read-only WPF state to exercise actual template triggers without owning the user's pointer or keyboard.
    // This verifies template reactions only; it does not claim real input or foreground acceptance.
    private static void SetTemplateState(DependencyObject target, Type declaringType, string field, bool value)
    {
        var key = (DependencyPropertyKey)declaringType.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        target.SetValue(key, value);
    }
}
