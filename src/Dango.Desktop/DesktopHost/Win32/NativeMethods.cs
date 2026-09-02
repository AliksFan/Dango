using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint SmtoNormal = 0x0000;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoActivate = 0x0010;
    private const nint WsChild = 0x40000000;
    private const nint WsExToolWindow = 0x00000080;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const uint WorkerWSpawnMessage = 0x052C;

    public static nint FindWorkerW()
    {
        var programManager = FindWindow("Progman", null);
        if (programManager != nint.Zero)
        {
            _ = SendMessageTimeout(
                programManager,
                WorkerWSpawnMessage,
                (nint)0xD,
                (nint)0x1,
                SmtoNormal,
                1000,
                out _);
        }

        nint workerW = nint.Zero;
        _ = EnumWindows(
            (topLevelWindow, _) =>
            {
                var shellView = FindWindowEx(topLevelWindow, nint.Zero, "SHELLDLL_DefView", null);
                if (shellView == nint.Zero)
                {
                    return true;
                }

                workerW = FindWindowEx(nint.Zero, topLevelWindow, "WorkerW", null);
                return workerW == nint.Zero;
            },
            nint.Zero);
        return workerW;
    }

    public static bool IsValidWindow(nint windowHandle) =>
        windowHandle != nint.Zero && IsWindow(windowHandle);

    public static void AttachToWorkerW(nint childWindow, nint workerW)
    {
        if (!IsValidWindow(childWindow) || !IsValidWindow(workerW))
        {
            throw new InvalidOperationException("WorkerW 挂载句柄无效。");
        }

        var style = GetWindowLongPtr(childWindow, GwlStyle);
        SetWindowStyle(childWindow, (style & ~WsPopup) | WsChild);
        Marshal.SetLastPInvokeError(0);
        var previousParent = SetParent(childWindow, workerW);
        var error = Marshal.GetLastPInvokeError();
        if (previousParent == nint.Zero && error != 0)
        {
            throw new Win32Exception(error, "无法把 Dango 挂载到 WorkerW。");
        }

        if (!GetClientRect(workerW, out var bounds))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法读取 WorkerW 客户区。");
        }

        const int width = 520;
        const int height = 320;
        var x = Math.Max(24, bounds.Right - width - 24);
        var y = Math.Max(24, bounds.Bottom - height - 24);
        if (!SetWindowPos(
                childWindow,
                nint.Zero,
                x,
                y,
                width,
                height,
                SwpNoActivate | SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法设置 WorkerW 子窗口位置。");
        }
    }

    public static void DetachFromWorkerW(nint childWindow)
    {
        if (!IsValidWindow(childWindow))
        {
            return;
        }

        _ = SetParent(childWindow, nint.Zero);
        var style = GetWindowLongPtr(childWindow, GwlStyle);
        SetWindowStyle(childWindow, (style & ~WsChild) | WsPopup);
    }

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

    private static void SetWindowStyle(nint windowHandle, nint style)
    {
        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLongPtr(windowHandle, GwlStyle, style);
        var error = Marshal.GetLastPInvokeError();
        if (previousStyle == nint.Zero && error != 0)
        {
            throw new Win32Exception(error, "无法更新 Dango 窗口样式。");
        }
    }

    private delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowEx(
        nint parentWindow,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter,
        uint flags,
        uint timeoutMilliseconds,
        out nint result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint childWindow, nint newParentWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);
}
