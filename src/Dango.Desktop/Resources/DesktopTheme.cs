using System.Windows;
using SystemColors = System.Windows.SystemColors;

namespace Dango.Desktop.Resources;

/// <summary>Shared palette and the one intentional ordinary-button difference between Home and accessories.</summary>
internal static class DesktopTheme
{
    public static readonly DependencyProperty PreserveSelectedHoverProperty = DependencyProperty.RegisterAttached(
        "PreserveSelectedHover", typeof(bool), typeof(DesktopTheme), new PropertyMetadata(false));

    public static bool GetPreserveSelectedHover(DependencyObject target) =>
        (bool)target.GetValue(PreserveSelectedHoverProperty);

    public static void SetPreserveSelectedHover(DependencyObject target, bool value) =>
        target.SetValue(PreserveSelectedHoverProperty, value);

    internal static void ApplyPalette(ResourceDictionary target, bool highContrast)
    {
        var palette = CreatePalette(highContrast);
        // Update the owning dictionary so existing DynamicResource expressions invalidate immediately.
        foreach (var key in palette.Keys) target[key] = palette[key];
    }

    internal static ResourceDictionary CreatePalette(bool highContrast = false)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri("/Dango.Desktop;component/Resources/DesktopPalette.xaml", UriKind.Relative),
        };
        if (highContrast)
        {
            palette["HomeBackground"] = SystemColors.WindowBrush;
            palette["HomeInk"] = SystemColors.WindowTextBrush;
            palette["HomePrimaryInk"] = SystemColors.HighlightTextBrush;
            palette["HomeMuted"] = SystemColors.WindowTextBrush;
            palette["HomeLine"] = SystemColors.WindowTextBrush;
            palette["HomeSoft"] = SystemColors.WindowBrush;
            palette["HomeAccent"] = SystemColors.HighlightBrush;
            palette["HomeSelected"] = SystemColors.HighlightBrush;
        }
        return palette;
    }
}
