using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using SystemColors = System.Windows.SystemColors;
using Point = System.Windows.Point;

namespace Dango.Desktop.Furniture;

/// <summary>The official wood artwork is shared by the room and its warehouse thumbnail.</summary>
internal static class OfficialWoodPlatformVisual
{
    private static readonly DrawingImage Normal = Create(false);
    private static DrawingImage? highContrast;
    private static (Color Background, Color Ink) highContrastColors;

    internal static ImageSource CurrentImage
    {
        get
        {
            if (!SystemParameters.HighContrast) return Normal;
            var colors = (SystemColors.WindowColor, SystemColors.WindowTextColor);
            if (highContrast is null || highContrastColors != colors)
            { highContrastColors = colors; highContrast = Create(true); }
            return highContrast;
        }
    }

    private static DrawingImage Create(bool contrast)
    {
        Brush wood = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(177, 122, 76));
        Brush top = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(227, 180, 123));
        Brush ink = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(145, 93, 56));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            // This is the accepted official slab artwork. The physics rectangle
            // remains separate from its softened image corners and wood grain.
            context.DrawRoundedRectangle(wood, new Pen(ink, 1), new Rect(0, 0, 340, 35), 4, 4);
            context.DrawRoundedRectangle(top, null, new Rect(1, 1, 338, 7), 3, 3);
            var grain = new Pen(ink, 1.5);
            context.DrawLine(grain, new Point(12, 20), new Point(110, 20));
            context.DrawLine(grain, new Point(125, 17), new Point(220, 17));
            context.DrawLine(grain, new Point(235, 24), new Point(327, 24));
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
