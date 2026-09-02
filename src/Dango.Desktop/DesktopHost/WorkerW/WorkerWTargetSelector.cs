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
    bool HasShellView);

public static class WorkerWTargetSelector
{
    public static nint Select(IEnumerable<ShellWindowCandidate> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var candidates = windows.ToArray();
        foreach (var shellHost in candidates.Where(candidate => candidate.HasShellView))
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
                return directChild.Handle;
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
                return legacyTopLevel.Handle;
            }
        }

        return nint.Zero;
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
