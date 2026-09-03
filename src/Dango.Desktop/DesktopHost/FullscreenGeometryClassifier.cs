namespace Dango.Desktop.DesktopHost;

public readonly record struct DesktopPixelRect(int Left, int Top, int Right, int Bottom);

public static class FullscreenGeometryClassifier
{
    private const int EdgeTolerance = 1;

    public static bool IsFullscreen(DesktopPixelRect window, DesktopPixelRect monitor) =>
        window.Left <= monitor.Left + EdgeTolerance &&
        window.Top <= monitor.Top + EdgeTolerance &&
        window.Right >= monitor.Right - EdgeTolerance &&
        window.Bottom >= monitor.Bottom - EdgeTolerance;
}
