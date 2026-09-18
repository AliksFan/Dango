using Dango.Core.Coordinates;

namespace Dango.Desktop.DesktopHost.WorkerW;

public static class WorkerWSurfaceCoordinateMapper
{
    internal static LogicalPoint OffsetFromDevice(LogicalPoint origin, double x, double y, DpiScale dpiScale) =>
        new(origin.X + x / dpiScale.X, origin.Y + y / dpiScale.Y);

    public static LogicalRect ToLogicalBounds(int pixelWidth, int pixelHeight, DpiScale dpiScale)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }
        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        return new LogicalRect(0, 0, pixelWidth / dpiScale.X, pixelHeight / dpiScale.Y);
    }
}
