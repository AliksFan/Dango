using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Accessories;

/// <summary>Existing authored artwork as immutable transparent pictures, without product categories.</summary>
public static class BuiltinAccessoryImages
{
    public const int TextureEdge = 512;
    private static readonly Dictionary<string, BitmapSource> Cache = [];
    private static readonly object CacheGate = new();

    public static BitmapSource? Get(string assetId)
    {
        lock (CacheGate) return CreateOrGet(assetId);
    }

    private static BitmapSource? CreateOrGet(string assetId)
    {
        if (Cache.TryGetValue(assetId, out var cached)) return cached;
        var appearance = assetId switch
        {
            "builtin-sprout" => HatAppearance.Sprout,
            "builtin-top-hat" => HatAppearance.TopHat,
            "builtin-bowler" => HatAppearance.Bowler,
            _ => (HatAppearance?)null,
        };
        if (appearance is null) return null;
        var artwork = new Canvas { Width = 160, Height = 120 };
        HatAppearanceGeometry.Apply(artwork, appearance.Value);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(TextureEdge / 64d, TextureEdge / 64d));
            // Root (80,25) remains centered near the bottom of a 64-square canvas.
            dc.PushTransform(new TranslateTransform(32 - 80, 60 - 25));
            // Draw the authored geometries directly. A detached VisualBrush can defer
            // its first layout/render and return a completely transparent bitmap.
            foreach (var path in artwork.Children.OfType<System.Windows.Shapes.Path>())
                dc.DrawGeometry(path.Fill, null, path.Data);
            dc.Pop();
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(TextureEdge, TextureEdge, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing); bitmap.Freeze(); Cache[assetId] = bitmap;
        return bitmap;
    }
}
