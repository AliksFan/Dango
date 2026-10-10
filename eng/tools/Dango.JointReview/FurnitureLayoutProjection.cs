using System.Windows;
using Dango.Core.Furniture;

namespace Dango.JointReview;

/// <summary>The accepted platform adapter uses 80 DIP horizontally and 60 vertically per p-unit.</summary>
internal readonly record struct FurnitureLayoutProjection(double Width, double Height, double PanX, double PanY)
{
    internal const double InitialCameraOffsetX = 2.5;
    internal double VerticalScale => Math.Clamp(Math.Min(60, Math.Min(Height / 5.4, Width / 12)), 30, 60);
    internal double VisualScale => VerticalScale / 60;
    internal double HorizontalScale => 80 * VisualScale;
    internal double OriginX => Width / 2 + PanX;
    internal double FloorY => Math.Max(100, Height - 40) + PanY;
    internal double OriginY => FloorY + LocalFurnitureSceneGeometry.Ground * VerticalScale;
    internal Point ToWorld(Point point) => new((point.X - OriginX) / HorizontalScale, (OriginY - point.Y) / VerticalScale);
    internal Point ToScreen(double x, double y) => new(OriginX + x * HorizontalScale, OriginY - y * VerticalScale);
    internal Rect PlatformRect(FurnitureAnchor anchor)
    {
        var topLeft = ToScreen(anchor.X - LocalFurnitureSceneGeometry.PlatformWidth / 2, anchor.Y);
        return new(topLeft.X, topLeft.Y, LocalFurnitureSceneGeometry.PlatformWidth * HorizontalScale,
            LocalFurnitureSceneGeometry.PlatformHeight * VerticalScale);
    }

    internal static Point ClampGloveTarget(Point point) => new(
        Math.Clamp(point.X, LocalFurnitureSceneGeometry.Left + .8, LocalFurnitureSceneGeometry.Right - .8),
        Math.Clamp(point.Y, LocalFurnitureSceneGeometry.Ground + .8, LocalFurnitureSceneGeometry.Ceiling - .8));

    internal static Point LimitGloveTarget(Point current, Point requested, double elapsedSeconds)
    {
        requested = ClampGloveTarget(requested);
        var offset = requested - current;
        var maximumDistance = 8 * Math.Clamp(elapsedSeconds, 0, .1);
        return offset.Length <= maximumDistance || offset.Length == 0
            ? requested : current + offset * (maximumDistance / offset.Length);
    }
}
