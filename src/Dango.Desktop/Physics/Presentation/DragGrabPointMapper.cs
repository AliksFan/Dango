namespace Dango.Desktop.Physics.Presentation;

public static class DragGrabPointMapper
{
    private const double HorizontalScale = 0.45;
    private const double VerticalOrigin = 0.55;
    private const double VerticalScale = 0.48333333333333334;

    public static PresentationPoint FromVisualPoint(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        return new PresentationPoint(
            Math.Clamp((x - (width / 2)) / (width * HorizontalScale), -1, 1),
            Math.Clamp(((height * VerticalOrigin) - y) / (height * VerticalScale), -1, 1));
    }
}
