namespace Dango.Desktop.DesktopHost;

public static class DesktopIconPriorityPolicy
{
    public static bool ShouldPassToDesktop(bool querySucceeded, bool iconHit) =>
        !querySucceeded || iconHit;

    public static bool ShouldShowInputProxy(
        bool enabled,
        bool cursorWithinBounds,
        bool querySucceeded,
        bool iconHit) =>
        enabled && (!cursorWithinBounds || (querySucceeded && !iconHit));
}
