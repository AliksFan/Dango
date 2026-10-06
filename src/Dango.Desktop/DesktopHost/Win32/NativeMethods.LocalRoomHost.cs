using System.ComponentModel;
using System.Runtime.InteropServices;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    internal static bool IsLocalRoomForeground(nint handle) => handle != 0 && GetForegroundWindow() == handle;
    internal static void PositionLocalRoomMenu(nint handle, LogicalRect bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) ||
            !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0 ||
            !SetWindowPos(handle, 0, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), SwpNoZOrder | SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法定位壁纸房间菜单。");
    }

    internal static bool TryRestoreLocalRoomDesktopFocus(WorkerWDesktopTarget target, nint menu)
    {
        if (!IsLocalRoomForeground(menu) || !IsValidDesktopTarget(target) ||
            !TryGetRoomWindowIdentity(target.ShellViewHandle, out var className, out var shellProcess) ||
            className != "SHELLDLL_DefView") return false;
        var shell = ProvenRoomSystemShellWindow(shellProcess);
        var root = GetRoomWindowAncestor(target.ShellViewHandle, 2);
        if (shell == 0 || !IsValidWindow(root) || !IsLocalRoomForeground(menu)) return false;
        _ = SetLocalRoomDesktopForeground(shell);
        return TryIsRoomDesktopForeground(target, root, shellProcess, out var desktopForeground) && desktopForeground;
    }
    internal static NativePoint LocalRoomParentScreenOrigin(WorkerWDesktopTarget target)
    {
        var point = new NativePoint();
        if (!LocalRoomClientToScreen(target.ParentHandle, ref point))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法读取壁纸宿主位置。");
        return point;
    }
    internal static void PositionLocalRoomHost(nint host, WorkerWDesktopTarget target, int x, int y, int width, int height)
    {
        var origin = new NativePoint();
        if (!LocalRoomClientToScreen(target.ParentHandle, ref origin) ||
            !SetWindowPos(host, target.InsertAfterHandle, x - origin.X, y - origin.Y, width, height,
                SwpNoActivate | SwpShowWindow | (target.IsRaisedDesktop ? 0u : SwpNoZOrder)))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法定位普通房壁纸宿主。");
    }
    [DllImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LocalRoomClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLocalRoomDesktopForeground(nint window);
}
