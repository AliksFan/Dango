namespace Dango.Desktop.Physics.Presentation;

public static class DragGrabPointMapper
{
    private const double HorizontalScale = 0.45;
    private const double VerticalOrigin = 0.55;

    public static PresentationPoint FromVisualPoint(double x, double y, double width, double height, double bodySizeScale = 1)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            !double.IsFinite(bodySizeScale) || bodySizeScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        return new PresentationPoint(
            (x - (width / 2)) / (width * HorizontalScale) * bodySizeScale,
            ((height * VerticalOrigin) - y) / (width * HorizontalScale) * bodySizeScale);
    }
}
