using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x00000080;

    public static void ApplyToolWindowStyle(nint windowHandle)
    {
        var currentStyle = GetWindowLongPtr(windowHandle, GwlExStyle);
        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLongPtr(windowHandle, GwlExStyle, currentStyle | WsExToolWindow);
        var error = Marshal.GetLastPInvokeError();
        if (previousStyle == 0 && error != 0)
        {
            throw new Win32Exception(error, "无法为覆盖层应用工具窗口样式。");
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);
}
