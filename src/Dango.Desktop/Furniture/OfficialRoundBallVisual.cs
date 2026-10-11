using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SystemColors = System.Windows.SystemColors;

namespace Dango.Desktop.Furniture;

/// <summary>The accepted pale-blue ball artwork, shared by the room and warehouse.</summary>
internal static class OfficialRoundBallVisual
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
        const double radius = 66;
        var center = new Point(radius, radius);
        Brush fill = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(167, 218, 239));
        Brush outline = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(60, 133, 162));
        Brush stripe = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(242, 253, 255));
        Brush marker = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 255, 255));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.PushClip(new EllipseGeometry(center, radius, radius));
            context.DrawEllipse(fill, null, center, radius, radius);
            context.DrawEllipse(null, new Pen(outline, 1.2), center, radius - .75, radius - .75);
            var band = Geometry.Parse("M 12,3 C 34,10 34,38 12,45").Clone();
            band.Transform = new ScaleTransform(radius / 24, radius / 24);
            context.DrawGeometry(null, new Pen(stripe, 5 * radius / 24), band);
            context.DrawEllipse(marker, null, new Point(radius * 1.72, radius), 2, 2);
            context.Pop();
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
