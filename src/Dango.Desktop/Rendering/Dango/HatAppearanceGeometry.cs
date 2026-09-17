using System.Windows.Controls;
using System.Windows.Media;

namespace Dango.Desktop.Rendering.Visuals;

public enum HatAppearance { Sprout, TopHat, Bowler }

/// <summary>Preview alternatives sharing the established root at (80,25).</summary>
public static class HatAppearanceGeometry
{
    public static void Apply(Canvas layer, HatAppearance appearance)
    {
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        (string Path, string Color)[] paths = appearance switch
        {
            HatAppearance.TopHat =>
            [
                ("M66,23 L65,5 Q80,2 95,5 L94,23 Z", "#51697D"),
                ("M66,17 L94,17 L94,22 L66,22 Z", "#E8B76A"),
                ("M57,23 Q80,21 103,23 L103,26 Q80,28 57,26 Z", "#3D5366"),
            ],
            HatAppearance.Bowler =>
            [
                ("M62,23 C62,-1 98,-1 98,23 Z", "#C97D55"),
                ("M62,18 Q80,21 98,18 L98,23 L62,23 Z", "#704C40"),
                ("M56,23 Q80,21 104,23 Q107,28 80,28 Q53,28 56,23 Z", "#A96343"),
            ],
            _ =>
            [
                ("M77,25 C77,20 78,16 80,12 C83,16 84,21 83,26 Z", "#67B84E"),
                ("M80,15 C71,15 64,11 62,4 C70,2 77,5 80,15 Z", "#78C85A"),
                ("M80,15 C89,15 96,11 98,4 C90,2 83,5 80,15 Z", "#78C85A"),
            ],
        };
        HatDeformationGeometry.InvalidateAsset(layer);
        layer.Children.Clear();
        foreach (var (path, color) in paths)
        {
            var geometry = Geometry.Parse(path); geometry.Freeze();
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze();
            layer.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Fill = brush, IsHitTestVisible = false });
        }
    }
}
