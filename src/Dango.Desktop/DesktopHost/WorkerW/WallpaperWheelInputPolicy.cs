using Dango.Core.Coordinates;

namespace Dango.Desktop.DesktopHost.WorkerW;

// ScreenBounds contains Win32 device-pixel coordinates, not WPF DIPs.
internal sealed record WallpaperWheelInputContext(
    WorkerWDesktopTarget Target,
    LogicalRect ScreenBounds,
    long Generation,
    bool IsReady,
    bool IsBlocked);

internal readonly record struct WallpaperWheelPoint(int X, int Y);

internal readonly record struct WallpaperWheelInputEnvironment(
    bool IsKnown,
    bool IsDesktopForeground,
    bool HasFullscreenApplication,
    bool IsPointerOnDesktop,
    bool IsDesktopIcon,
    bool HasModifier = false);

internal readonly record struct WallpaperWheelWindowState(
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsShell,
    DesktopPixelRect Bounds,
    bool IsMaximized = false);

internal static class WallpaperWheelInputPolicy
{
    internal static bool IsContextEligible(WallpaperWheelInputContext? context) =>
        context is { IsReady: true, IsBlocked: false } &&
        context.Target is { ParentHandle: not 0, WallpaperLayerHandle: not 0, ShellViewHandle: not 0 } &&
        context.ScreenBounds.Width > 0 && context.ScreenBounds.Height > 0 &&
        double.IsFinite(context.ScreenBounds.Right) && double.IsFinite(context.ScreenBounds.Bottom);

    internal static bool IsSamePresentation(WallpaperWheelInputContext expected, WallpaperWheelInputContext? current) =>
        IsContextEligible(current) && expected.Generation == current!.Generation &&
        expected.Target == current.Target && expected.ScreenBounds == current.ScreenBounds;

    internal static bool ContainsDevicePoint(LogicalRect bounds, WallpaperWheelPoint point) =>
        point.X >= bounds.X && point.X < bounds.Right && point.Y >= bounds.Y && point.Y < bounds.Bottom;

    internal static bool ShouldApply(WallpaperWheelInputContext expected, WallpaperWheelInputContext? current,
        WallpaperWheelPoint point, WallpaperWheelInputEnvironment environment) =>
        IsContextEligible(expected) && IsSamePresentation(expected, current) &&
        ContainsDevicePoint(expected.ScreenBounds, point) && environment.IsKnown &&
        environment.IsDesktopForeground && !environment.HasFullscreenApplication &&
        environment.IsPointerOnDesktop && !environment.IsDesktopIcon && !environment.HasModifier;

    internal static bool IsBlockingFullscreen(WallpaperWheelWindowState window, DesktopPixelRect monitor) =>
        window.IsVisible && !window.IsMinimized && !window.IsCloaked && !window.IsShell &&
        ((window.IsMaximized && window.Bounds.Left < monitor.Right && window.Bounds.Right > monitor.Left &&
          window.Bounds.Top < monitor.Bottom && window.Bounds.Bottom > monitor.Top) ||
         FullscreenGeometryClassifier.IsFullscreen(window.Bounds, monitor));

    internal static bool IsDesktopForeground(WorkerWDesktopTarget target, nint shellViewRoot,
        nint foreground, nint foregroundRoot, string className, bool isShellProcess, nint systemShellWindow = 0) =>
        foreground != 0 && foregroundRoot != 0 && shellViewRoot != 0 && isShellProcess &&
        IsDesktopShellClass(className) &&
        (foregroundRoot == shellViewRoot || foregroundRoot == target.ParentHandle ||
         foregroundRoot == target.WallpaperLayerHandle ||
         (systemShellWindow != 0 && (foreground == systemShellWindow || foregroundRoot == systemShellWindow)));

    internal static bool IsDesktopShellClass(string className) =>
        className is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32";

    internal static bool IsShellClass(string className) =>
        IsDesktopShellClass(className) || className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    internal static int DecodeRawDelta(uint mouseData) => unchecked((short)(mouseData >> 16));
}
