using System.Windows;

namespace Dango.MaterialSample;

internal sealed class SceneCoordinates
{
    private double? dragOrigin;
    internal bool IsDragging => dragOrigin.HasValue;
    internal double CameraOrigin(double height, double scaleY, double ground, double coreY) =>
        dragOrigin ?? Math.Max(height - 36 + ground * scaleY, coreY * scaleY + height * .38);

    internal void BeginDrag(double height, double scaleY, double ground, double coreY) =>
        dragOrigin = CameraOrigin(height, scaleY, ground, coreY);

    internal void EndDrag() => dragOrigin = null;

    internal Point WorldTarget(Point pointer, double width, double height, double scaleX, double scaleY, double ground, double coreY) =>
        new((pointer.X - width / 2) / scaleX, (CameraOrigin(height, scaleY, ground, coreY) - pointer.Y) / scaleY);
}
