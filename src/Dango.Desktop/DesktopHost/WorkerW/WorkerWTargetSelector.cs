namespace Dango.Desktop.DesktopHost.WorkerW;

public sealed record ShellWindowCandidate(
    nint Handle,
    nint ParentHandle,
    int ProcessId,
    int ZOrder,
    string ClassName,
    bool IsVisible,
    int Width,
    int Height,
    bool HasShellView,
    nint ShellViewHandle,
    bool HasNoRedirectionBitmap);

public sealed record WorkerWDesktopTarget(
    nint ParentHandle,
    nint WallpaperLayerHandle,
    nint InsertAfterHandle,
    nint ShellViewHandle,
    bool IsRaisedDesktop);

public static class WorkerWTargetSelector
{
    public static WorkerWDesktopTarget? Select(IEnumerable<ShellWindowCandidate> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var candidates = windows.ToArray();
        foreach (var shellHost in candidates.Where(candidate => candidate.HasShellView))
        {
            if (shellHost.HasNoRedirectionBitmap && shellHost.ShellViewHandle != nint.Zero)
            {
                var directChild = candidates
                    .Where(candidate =>
                        IsWorkerW(candidate) &&
                        candidate.ParentHandle == shellHost.Handle &&
                        IsCompatibleDesktopLayer(shellHost, candidate))
                    .OrderByDescending(candidate => (long)candidate.Width * candidate.Height)
                    .FirstOrDefault();
                if (directChild is not null)
                {
                    return new WorkerWDesktopTarget(
                        shellHost.Handle,
                        directChild.Handle,
                        shellHost.ShellViewHandle,
                        shellHost.ShellViewHandle,
                        IsRaisedDesktop: true);
                }
            }

            var legacyTopLevel = candidates
                .Where(candidate =>
                    IsWorkerW(candidate) &&
                    candidate.ParentHandle == nint.Zero &&
                    candidate.ZOrder > shellHost.ZOrder &&
                    IsCompatibleDesktopLayer(shellHost, candidate))
                .OrderBy(candidate => candidate.ZOrder)
                .FirstOrDefault();
            if (legacyTopLevel is not null)
            {
                return new WorkerWDesktopTarget(
                    legacyTopLevel.Handle,
                    legacyTopLevel.Handle,
                    nint.Zero,
                    shellHost.ShellViewHandle,
                    IsRaisedDesktop: false);
            }
        }

        return null;
    }

    private static bool IsWorkerW(ShellWindowCandidate candidate) =>
        candidate.Handle != nint.Zero &&
        string.Equals(candidate.ClassName, "WorkerW", StringComparison.Ordinal);

    private static bool IsCompatibleDesktopLayer(
        ShellWindowCandidate shellHost,
        ShellWindowCandidate candidate)
    {
        if (!candidate.IsVisible || candidate.ProcessId != shellHost.ProcessId)
        {
            return false;
        }

        var minimumWidth = Math.Max(320, shellHost.Width * 3 / 4);
        var minimumHeight = Math.Max(240, shellHost.Height * 3 / 4);
        return candidate.Width >= minimumWidth && candidate.Height >= minimumHeight;
    }
}
