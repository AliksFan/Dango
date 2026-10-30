using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>Borrowed view of the existing home body; capture never steps physics or reads the desktop.</summary>
internal interface IAccessoryDynamicPreview : IDisposable
{
    byte[]? Capture();
    void Command(string command, double x = 0, double y = 0);
    void SetVisible(bool visible);
}

internal sealed class AccessoryDynamicPreviewInput(Action<DangoBeltInput> publish)
{
    private double horizontal;
    private bool dragging;
    private Point target;

    internal void Clear()
    {
        horizontal = 0; dragging = false; target = default;
        Publish(cancel: true);
        Publish();
    }

    internal void Command(string command, Point world)
    {
        switch (command)
        {
            case "left": horizontal = -1; break;
            case "right": horizontal = 1; break;
            case "stop": horizontal = 0; break;
            case "jump": Publish(jump: true); Publish(); return;
            case "drag-start": dragging = true; target = world; horizontal = 0; break;
            case "drag-move": if (!dragging) return; target = world; break;
            case "drag-end": dragging = false; break;
            default: return;
        }
        Publish();
    }

    private void Publish(bool jump = false, bool cancel = false) => publish(new(horizontal, 0, 0,
        jump, dragging, target.X, target.Y, horizontal != 0 || dragging || jump, cancel));
}

internal static class AccessoryDynamicPreviewImage
{
    internal const int Width = 288, Height = 160;
    internal const double Scale = .8, Floor = 144;

    // This accepts one explicitly supplied application visual, never a screen/window capture API.
    internal static byte[]? Capture(FrameworkElement view, double left, double top)
    {
        if (view.ActualWidth <= 0 || view.ActualHeight <= 0) return null;
        var bounds = VisualTreeHelper.GetDescendantBounds(view);
        bounds.Union(new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        if (bounds.IsEmpty || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return null;
        var brush = new VisualBrush(view)
        {
            AutoLayoutContent = false, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
            Stretch = Stretch.Fill,
        };
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(brush, null, new Rect(left + bounds.X * Scale, top + bounds.Y * Scale,
                bounds.Width * Scale, bounds.Height * Scale));
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
