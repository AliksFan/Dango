using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint SmtoNormal = 0x0000;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint LwaAlpha = 0x00000002;
    private const nint WsChild = 0x40000000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const nint WsExNoRedirectionBitmap = 0x00200000;
    private const nint WsExToolWindow = 0x00000080;
    private const uint WorkerWSpawnMessage = 0x052C;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint LvmHitTest = 0x1012;
    private const int WhKeyboardLl = 13;
    private static readonly SingleOutstandingQueryGate<DesktopIconQuery, nint> DesktopIconQueries = new(QueryDesktopIcon);

    public static WorkerWDesktopTarget? FindWorkerW()
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

        return WorkerWTargetSelector.Select(CaptureShellCandidates());
    }

    public static bool IsValidWindow(nint windowHandle) =>
        windowHandle != nint.Zero && IsWindow(windowHandle);

    public static bool IsValidDesktopTarget(WorkerWDesktopTarget? target) =>
        target is not null &&
        IsValidWindow(target.ParentHandle) &&
        IsValidWindow(target.WallpaperLayerHandle) &&
        (!target.IsRaisedDesktop || IsValidWindow(target.InsertAfterHandle));

    public static bool IsExplorerDesktopForeground(WorkerWDesktopTarget target) =>
        WorkerWDesktopFocusPolicy.IsAllowed(GetForegroundWindow(), target, IsChild);

    public static bool HasSystemKeyboardModifier() =>
        IsKeyDown(0x11) || IsKeyDown(0x12) || IsKeyDown(0x5B) || IsKeyDown(0x5C);

    public static bool IsEscapePressed() => IsKeyDown(0x1B);

    public static bool IsInputKeyPressed(int virtualKey) => IsKeyDown(virtualKey);

    public static nint InstallLowLevelKeyboardHook(LowLevelKeyboardProcedure procedure) =>
        SetWindowsHookEx(WhKeyboardLl, procedure, GetModuleHandle(null), 0);

    public static bool UninstallKeyboardHook(nint hook) => UnhookWindowsHookEx(hook);

    public static nint CallNextKeyboardHook(nint hook, int code, nint wordParameter, nint longParameter) =>
        CallNextHookEx(hook, code, wordParameter, longParameter);

    public static bool IsForegroundFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (!IsValidWindow(foreground) || !IsWindowVisible(foreground))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        var className = GetWindowClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero ||
            !GetMonitorInfo(monitor, ref monitorInfo) ||
            !GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        return FullscreenGeometryClassifier.IsFullscreen(
            new DesktopPixelRect(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom),
            new DesktopPixelRect(
                monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Top,
                monitorInfo.Monitor.Right,
                monitorInfo.Monitor.Bottom));
    }

    public static bool TryIsDesktopIconAtPoint(
        WorkerWDesktopTarget target,
        int screenX,
        int screenY,
        out bool iconHit,
        uint timeoutMilliseconds = 75,
        long? deadlineTimestamp = null)
    {
        iconHit = false;
        if (timeoutMilliseconds == 0 || !HasQueryTime(deadlineTimestamp)) return false;
        var queryDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * timeoutMilliseconds / 1000;
        if (deadlineTimestamp is { } eventDeadline) queryDeadline = Math.Min(queryDeadline, eventDeadline);
        if (!TryGetWindowIdentity(target.ShellViewHandle, out var shellClass, out var shellProcess) ||
            shellClass != "SHELLDLL_DefView" || !HasQueryTime(queryDeadline))
        {
            return false;
        }

        var listView = FindDescendantWindowByClass(target.ShellViewHandle, "SysListView32", queryDeadline);
        if (!HasQueryTime(queryDeadline) ||
            !TryGetWindowIdentity(listView, out var listClass, out var listProcess) ||
            listClass != "SysListView32" || listProcess != shellProcess)
        {
            return false;
        }

        var query = new DesktopIconQuery(target, listView, GetParent(target.ShellViewHandle),
            GetParent(listView), shellProcess, screenX, screenY, queryDeadline);
        var remaining = RemainingQueryMilliseconds(queryDeadline);
        if (!DesktopIconQueries.TryQuery(query, remaining, out var hitTestResult) ||
            !HasQueryTime(queryDeadline)) return false;
        iconHit = hitTestResult.ToInt64() >= 0;
        return true;
    }

    private static OutstandingQueryResult<nint> QueryDesktopIcon(DesktopIconQuery query)
    {
        if (!HasQueryTime(query.Deadline) || !IsCapturedDesktopIconTarget(query)) return default;
        var point = new NativePoint { X = query.ScreenX, Y = query.ScreenY };
        if (!ScreenToClient(query.ListView, ref point) || !HasQueryTime(query.Deadline)) return default;
        var process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, query.ProcessId);
        if (process == nint.Zero) return default;

        nint remoteBuffer = nint.Zero;
        var result = default(OutstandingQueryResult<nint>);
        var cleanupSucceeded = true;
        try
        {
            var hitTest = new ListViewHitTestInfo { Point = point, ItemIndex = -1 };
            var size = (nuint)Marshal.SizeOf<ListViewHitTestInfo>();
            if (HasQueryTime(query.Deadline))
            {
                remoteBuffer = VirtualAllocEx(process, nint.Zero, size, MemCommit | MemReserve, PageReadWrite);
                if (remoteBuffer != nint.Zero && HasQueryTime(query.Deadline) &&
                    WriteProcessMemory(process, remoteBuffer, ref hitTest, size, out var bytesWritten) &&
                    bytesWritten == size && IsCapturedDesktopIconTarget(query) && HasQueryTime(query.Deadline))
                {
                    // This worker retains the LVM_HITTEST buffer until the synchronous control query returns.
                    // A caller timeout only abandons its result; it never frees resources or reopens the slot.
                    Marshal.SetLastPInvokeError(0);
                    var hitTestResult = SendRoomIconMessage(query.ListView, LvmHitTest, nint.Zero, remoteBuffer);
                    var sendError = Marshal.GetLastPInvokeError();
                    if (sendError == 0 && IsCapturedDesktopIconTarget(query)) result = new(true, hitTestResult);
                }
            }
        }
        finally
        {
            if (remoteBuffer != nint.Zero)
            {
                cleanupSucceeded = VirtualFreeEx(process, remoteBuffer, 0, MemRelease);
            }

            cleanupSucceeded &= CloseHandle(process);
            if (!cleanupSucceeded)
            {
                // Keep the process-wide service closed if cleanup failed; never accumulate remote allocations.
                DesktopIconQueries.Dispose();
            }
        }

        return cleanupSucceeded ? result : default;
    }

    private static bool IsCapturedDesktopIconTarget(DesktopIconQuery query) =>
        IsValidDesktopTarget(query.Target) &&
        TryGetWindowIdentity(query.Target.ParentHandle, out _, out var parentProcess) && parentProcess == query.ProcessId &&
        TryGetWindowIdentity(query.Target.WallpaperLayerHandle, out _, out var layerProcess) && layerProcess == query.ProcessId &&
        TryGetWindowIdentity(query.Target.ShellViewHandle, out var shellClass, out var shellProcess) &&
        shellClass == "SHELLDLL_DefView" && shellProcess == query.ProcessId &&
        GetParent(query.Target.ShellViewHandle) == query.ShellParent && IsValidWindow(query.ShellParent) &&
        TryGetWindowIdentity(query.ListView, out var listClass, out var listProcess) &&
        listClass == "SysListView32" && listProcess == query.ProcessId &&
        GetParent(query.ListView) == query.ListParent && IsChild(query.Target.ShellViewHandle, query.ListView);

    private readonly record struct DesktopIconQuery(WorkerWDesktopTarget Target, nint ListView,
        nint ShellParent, nint ListParent, uint ProcessId, int ScreenX, int ScreenY, long Deadline);

    public static (int Width, int Height) AttachWorkerWLayeredHost(
        nint hostWindow,
        WorkerWDesktopTarget target)
    {
        if (!IsValidWindow(hostWindow) || !IsValidDesktopTarget(target))
        {
            throw new InvalidOperationException("WorkerW 挂载句柄无效。");
        }

        if (!SetLayeredWindowAttributes(hostWindow, 0, byte.MaxValue, LwaAlpha))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法启用系统管理的桌面分层宿主。");
        }

        var style = GetWindowLongPtr(hostWindow, GwlStyle);
        SetWindowStyle(hostWindow, (style & ~WsPopup) | WsChild);
        Marshal.SetLastPInvokeError(0);
        var previousParent = SetParent(hostWindow, target.ParentHandle);
        var parentError = Marshal.GetLastPInvokeError();
        if (previousParent == nint.Zero && parentError != 0)
        {
            throw new Win32Exception(parentError, "无法把原生分层宿主挂到桌面父窗口。");
        }

        if (GetParent(hostWindow) != target.ParentHandle)
        {
            throw new InvalidOperationException("原生分层宿主挂载后父窗口不匹配。");
        }

        if (!GetClientRect(target.ParentHandle, out var bounds))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法读取 WorkerW 客户区。");
        }

        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var positionFlags = SwpNoActivate | SwpFrameChanged;
        if (!target.IsRaisedDesktop)
        {
            positionFlags |= SwpNoZOrder;
        }

        if (!SetWindowPos(
                hostWindow,
                target.InsertAfterHandle,
                0,
                0,
                width,
                height,
                positionFlags))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法设置 WorkerW 子窗口位置。");
        }

        return (width, height);
    }

    public static void AttachWorkerWInputProxy(nint proxyWindow, WorkerWDesktopTarget target)
    {
        if (!IsValidWindow(proxyWindow) || !IsValidDesktopTarget(target))
        {
            throw new InvalidOperationException("WorkerW 输入代理句柄无效。");
        }

        var style = GetWindowLongPtr(proxyWindow, GwlStyle);
        SetWindowStyle(proxyWindow, (style & ~WsPopup) | WsChild);
        Marshal.SetLastPInvokeError(0);
        var previousParent = SetParent(proxyWindow, target.ParentHandle);
        var parentError = Marshal.GetLastPInvokeError();
        if (previousParent == nint.Zero && parentError != 0)
        {
            throw new Win32Exception(parentError, "无法把输入代理挂到桌面父窗口。");
        }

        if (GetParent(proxyWindow) != target.ParentHandle)
        {
            throw new InvalidOperationException("输入代理挂载后父窗口不匹配。");
        }

        ApplyInputProxyTransparency(proxyWindow);
    }

    public static void PositionWorkerWInputProxy(
        nint proxyWindow,
        int x,
        int y,
        int width,
        int height,
        bool showWindow)
    {
        var flags = SwpNoActivate;
        if (showWindow)
        {
            flags |= SwpShowWindow;
        }

        if (!SetWindowPos(
                proxyWindow,
                nint.Zero,
                x,
                y,
                Math.Max(1, width),
                Math.Max(1, height),
                flags))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法定位 WorkerW 输入代理。");
        }

        ApplyInputProxyTransparency(proxyWindow);
    }

    private static void ApplyInputProxyTransparency(nint proxyWindow)
    {
        if (!SetLayeredWindowAttributes(proxyWindow, 0, 1, LwaAlpha))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法启用 WorkerW 透明输入代理。");
        }
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

    public static bool IsPointWithinWindow(nint windowHandle, int screenX, int screenY) =>
        GetWindowRect(windowHandle, out var bounds) &&
        screenX >= bounds.Left &&
        screenX < bounds.Right &&
        screenY >= bounds.Top &&
        screenY < bounds.Bottom;

    private static void SetWindowStyle(nint windowHandle, nint style)
    {
        Marshal.SetLastPInvokeError(0);
        var previousStyle = SetWindowLongPtr(windowHandle, GwlStyle, style);
        var error = Marshal.GetLastPInvokeError();
        if (previousStyle == nint.Zero && error != 0)
        {
            throw new Win32Exception(error, "无法把原生分层宿主转换为桌面子窗口。");
        }
    }

    private static IReadOnlyList<ShellWindowCandidate> CaptureShellCandidates()
    {
        var candidates = new List<ShellWindowCandidate>();
        var zOrder = 0;
        _ = EnumWindows(
            (topLevelWindow, _) =>
            {
                var className = GetWindowClassName(topLevelWindow);
                if (string.Equals(className, "Progman", StringComparison.Ordinal) ||
                    string.Equals(className, "WorkerW", StringComparison.Ordinal))
                {
                    var shellView = FindWindowEx(
                        topLevelWindow,
                        nint.Zero,
                        "SHELLDLL_DefView",
                        null);
                    candidates.Add(
                        CaptureCandidate(
                            topLevelWindow,
                            nint.Zero,
                            zOrder,
                            className,
                            shellView != nint.Zero,
                            shellView,
                            HasExtendedStyle(topLevelWindow, WsExNoRedirectionBitmap)));
                }

                zOrder++;
                return true;
            },
            nint.Zero);

        var shellHosts = candidates.Where(candidate => candidate.HasShellView).ToArray();
        foreach (var shellHost in shellHosts)
        {
            var child = nint.Zero;
            while ((child = FindWindowEx(shellHost.Handle, child, null, null)) != nint.Zero)
            {
                var className = GetWindowClassName(child);
                if (string.Equals(className, "WorkerW", StringComparison.Ordinal))
                {
                    candidates.Add(
                        CaptureCandidate(
                            child,
                            shellHost.Handle,
                            zOrder: 0,
                            className,
                            hasShellView: false,
                            shellViewHandle: nint.Zero,
                            hasNoRedirectionBitmap: false));
                }
            }
        }

        return candidates;
    }

    private static ShellWindowCandidate CaptureCandidate(
        nint windowHandle,
        nint parentHandle,
        int zOrder,
        string className,
        bool hasShellView,
        nint shellViewHandle,
        bool hasNoRedirectionBitmap)
    {
        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        var width = 0;
        var height = 0;
        if (GetWindowRect(windowHandle, out var bounds))
        {
            width = Math.Max(0, bounds.Right - bounds.Left);
            height = Math.Max(0, bounds.Bottom - bounds.Top);
        }

        return new ShellWindowCandidate(
            windowHandle,
            parentHandle,
            unchecked((int)processId),
            zOrder,
            className,
            IsWindowVisible(windowHandle),
            width,
            height,
            hasShellView,
            shellViewHandle,
            hasNoRedirectionBitmap);
    }

    private static bool HasExtendedStyle(nint windowHandle, nint style) =>
        (GetWindowLongPtr(windowHandle, GwlExStyle) & style) == style;

    private static nint FindDescendantWindowByClass(nint parentWindow, string className, long? deadlineTimestamp = null)
    {
        var child = nint.Zero;
        while ((child = FindWindowEx(parentWindow, child, null, null)) != nint.Zero)
        {
            if (!HasQueryTime(deadlineTimestamp)) return nint.Zero;
            if (string.Equals(GetWindowClassName(child), className, StringComparison.Ordinal))
            {
                return child;
            }

            var descendant = FindDescendantWindowByClass(child, className, deadlineTimestamp);
            if (descendant != nint.Zero)
            {
                return descendant;
            }
        }

        return nint.Zero;
    }

    private static string GetWindowClassName(nint windowHandle)
    {
        var className = new StringBuilder(256);
        _ = GetClassName(windowHandle, className, className.Capacity);
        return className.ToString();
    }

    private delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);

    internal delegate nint LowLevelKeyboardProcedure(int code, nint wordParameter, nint longParameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListViewHitTestInfo
    {
        public NativePoint Point;
        public uint Flags;
        public int ItemIndex;
        public int SubItemIndex;
        public int GroupIndex;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parentWindow, nint childWindow);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProcedure procedure,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wordParameter, nint longParameter);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

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

    [DllImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    private static extern nint SendRoomIconMessage(nint windowHandle, uint message, nint wordParameter, nint longParameter);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint childWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint childWindow, nint newParentWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        nint windowHandle,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

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

    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(
        nint process,
        nint address,
        nuint size,
        uint allocationType,
        uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        nint process,
        nint baseAddress,
        ref ListViewHitTestInfo buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
