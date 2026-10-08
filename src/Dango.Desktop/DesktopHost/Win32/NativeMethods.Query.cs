using System.Diagnostics;
using System.Text;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    private static bool TryGetWindowIdentity(nint window, out string className, out uint process)
    {
        className = string.Empty;
        process = 0;
        if (!IsValidWindow(window)) return false;
        var buffer = new StringBuilder(256);
        if (GetClassName(window, buffer, buffer.Capacity) <= 0 ||
            GetWindowThreadProcessId(window, out process) == 0 || process == 0) return false;
        className = buffer.ToString();
        return true;
    }

    // Queries share a cooperative deadline; native cleanup and OS scheduling cannot be preempted.
    private static uint RemainingQueryMilliseconds(long? deadlineTimestamp)
    {
        if (deadlineTimestamp is null) return uint.MaxValue;
        var remaining = deadlineTimestamp.Value - Stopwatch.GetTimestamp();
        return remaining <= 0 ? 0 : (uint)Math.Min(uint.MaxValue, Math.Floor(remaining * 1000d / Stopwatch.Frequency));
    }

    private static bool HasQueryTime(long? deadlineTimestamp) => RemainingQueryMilliseconds(deadlineTimestamp) > 0;
}
