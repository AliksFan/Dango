namespace Dango.Desktop.DesktopHost;

public sealed record SceneWindowSettings(
    double Width,
    double Height,
    string MonitorKey,
    double NormalizedCenterX,
    double NormalizedCenterY)
{
    public static SceneWindowSettings CreateDefault() => new(800, 600, string.Empty, 0.5, 0.5);
}
