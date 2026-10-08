using System.ComponentModel;
using System.Runtime.InteropServices;
using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    internal static bool IsLocalRoomForeground(nint handle) => handle != 0 && GetForegroundWindow() == handle;
    internal static void PositionLocalRoomHost(nint host, WorkerWDesktopTarget target, int x, int y, int width, int height)
    {
        var origin = new NativePoint();
        if (!LocalRoomClientToScreen(target.ParentHandle, ref origin) ||
            !SetWindowPos(host, target.InsertAfterHandle, x - origin.X, y - origin.Y, width, height,
                SwpNoActivate | SwpShowWindow | (target.IsRaisedDesktop ? 0u : SwpNoZOrder)))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法定位桌面场景宿主。");
    }
    [DllImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LocalRoomClientToScreen(nint window, ref NativePoint point);
}
