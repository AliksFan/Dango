namespace Dango.Desktop.DesktopHost.WorkerW;

public static class WorkerWDesktopFocusPolicy
{
    public static bool IsAllowed(
        nint foregroundWindow,
        WorkerWDesktopTarget target,
        Func<nint, nint, bool> isChild)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isChild);
        if (foregroundWindow == nint.Zero)
        {
            return false;
        }

        return foregroundWindow == target.ParentHandle ||
            foregroundWindow == target.WallpaperLayerHandle ||
            foregroundWindow == target.ShellViewHandle ||
            isChild(target.ParentHandle, foregroundWindow) ||
            isChild(target.ShellViewHandle, foregroundWindow);
    }
}
