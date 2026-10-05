using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    internal delegate nint LowLevelMouseProcedure(int code, nint wordParameter, nint longParameter);

    private static readonly object RoomMouseHooksLock = new();
    private static readonly Dictionary<nint, LowLevelMouseProcedure> RoomMouseHooks = [];

    internal static nint InstallRoomMouseHook(LowLevelMouseProcedure procedure)
    {
        var installed = SetRoomMouseHook(14, procedure, GetModuleHandle(null), 0);
        if (installed != 0) lock (RoomMouseHooksLock) RoomMouseHooks[installed] = procedure;
        return installed;
    }

    internal static bool UninstallRoomMouseHook(nint hook)
    {
        if (!UnhookWindowsHookEx(hook)) return false;
        lock (RoomMouseHooksLock) RoomMouseHooks.Remove(hook);
        return true;
    }

    internal static WallpaperWheelHookData ReadRoomMouseInput(nint longParameter)
    {
        var data = Marshal.PtrToStructure<LowLevelMouseInput>(longParameter);
        return new WallpaperWheelHookData(new WallpaperWheelPoint(data.Point.X, data.Point.Y), data.MouseData,
            data.Flags, HasRoomMouseModifier());
    }

    internal static WallpaperWheelInputEnvironment InspectRoomWheelInput(
        WallpaperWheelInputContext context, WallpaperWheelPoint point) => InspectRoomWheelInputCore(context, point, null);

    internal static WallpaperWheelInputEnvironment InspectRoomWheelInputAtEvent(
        WallpaperWheelInputContext context, WallpaperWheelPoint point) => InspectRoomWheelInputCore(context, point,
            Stopwatch.GetTimestamp() + Stopwatch.Frequency * 20 / 1000);

    private static WallpaperWheelInputEnvironment InspectRoomWheelInputCore(
        WallpaperWheelInputContext context, WallpaperWheelPoint point, long? deadlineTimestamp)
    {
        // Only transient handles, classes, rectangles and hit booleans are queried. No titles or contents are read.
        try
        {
            if (!HasRoomInspectionTime(deadlineTimestamp)) return default;
            if (HasRoomMouseModifier()) return new(true, false, false, false, false, true);
            if (!WallpaperWheelInputPolicy.IsContextEligible(context) || !IsValidDesktopTarget(context.Target) ||
                !TryGetRoomWindowIdentity(context.Target.ShellViewHandle, out var shellClass, out var shellProcess) ||
                shellClass != "SHELLDLL_DefView" || !HasRoomInspectionTime(deadlineTimestamp)) return default;

            var shellRoot = GetRoomWindowAncestor(context.Target.ShellViewHandle, 2);
            if (!IsValidWindow(shellRoot) ||
                !TryIsRoomDesktopForeground(context.Target, shellRoot, shellProcess, out var desktopForeground) ||
                !HasRoomInspectionTime(deadlineTimestamp)) return default;
            if (!desktopForeground) return new(true, false, false, false, false);

            if (!TryHasRoomFullscreenApplicationWithinBudget(shellProcess, out var fullscreen, deadlineTimestamp)) return default;
            if (fullscreen) return new(true, true, true, false, false);

            if (!HasRoomInspectionTime(deadlineTimestamp) || !GetCursorPos(out var cursor)) return default;
            var currentPoint = new WallpaperWheelPoint(cursor.X, cursor.Y);
            if (!WallpaperWheelInputPolicy.ContainsDevicePoint(context.ScreenBounds, point) ||
                !WallpaperWheelInputPolicy.ContainsDevicePoint(context.ScreenBounds, currentPoint))
                return new(true, true, false, false, false);

            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                !TryIsRoomDesktopPoint(context.Target, shellRoot, shellProcess, point, out var pointerAllowed) ||
                !TryIsDesktopIconAtPoint(context.Target, point.X, point.Y, out var iconHit,
                    deadlineTimestamp: deadlineTimestamp)) return default;
            if (currentPoint != point)
            {
                if (!HasRoomInspectionTime(deadlineTimestamp) ||
                    !TryIsRoomDesktopPoint(context.Target, shellRoot, shellProcess, currentPoint, out var currentAllowed) ||
                    !TryIsDesktopIconAtPoint(context.Target, currentPoint.X, currentPoint.Y, out var currentIconHit,
                        deadlineTimestamp: deadlineTimestamp)) return default;
                pointerAllowed &= currentAllowed;
                iconHit |= currentIconHit;
            }

            // Shell hit tests can pump messages or wait. Refresh every eligibility fact they may have outlived.
            if (!TryHasRoomFullscreenApplicationWithinBudget(shellProcess, out fullscreen, deadlineTimestamp) ||
                !GetCursorPos(out cursor)) return default;
            if (fullscreen) return new(true, true, true, false, false);
            if (new WallpaperWheelPoint(cursor.X, cursor.Y) != currentPoint)
                return new(true, true, false, false, false);
            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                !TryIsRoomDesktopPoint(context.Target, shellRoot, shellProcess, currentPoint, out var finalAllowed))
                return default;
            pointerAllowed &= finalAllowed;
            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                !TryIsRoomDesktopForeground(context.Target, shellRoot, shellProcess, out desktopForeground)) return default;
            var modified = HasRoomMouseModifier();
            return HasRoomInspectionTime(deadlineTimestamp)
                ? new(true, desktopForeground, false, pointerAllowed, iconHit, modified) : default;
        }
        catch
        {
            return default;
        }
    }

    private static bool TryIsRoomDesktopForeground(WorkerWDesktopTarget target, nint shellRoot, uint shellProcess,
        out bool allowed)
    {
        allowed = false;
        var foreground = GetForegroundWindow();
        if (!TryGetRoomWindowIdentity(foreground, out var className, out var process)) return false;
        var root = GetRoomWindowAncestor(foreground, 2);
        if (!IsValidWindow(root)) return false;
        allowed = WallpaperWheelInputPolicy.IsDesktopForeground(target, shellRoot, foreground, root,
            className, process == shellProcess);
        return true;
    }

    private static bool TryIsRoomDesktopPoint(WorkerWDesktopTarget target, nint shellRoot, uint shellProcess,
        WallpaperWheelPoint point, out bool allowed)
    {
        allowed = false;
        var window = RoomWindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        if (!TryGetRoomWindowIdentity(window, out var className, out var process)) return false;
        var root = GetRoomWindowAncestor(window, 2);
        if (!IsValidWindow(root)) return false;
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return true;
        allowed = WallpaperWheelInputPolicy.IsDesktopForeground(target, shellRoot, window, root,
            className, process == shellProcess) ||
            (process == Environment.ProcessId &&
             (IsChild(target.ParentHandle, window) || IsChild(target.WallpaperLayerHandle, window)));
        return true;
    }

    private static bool TryGetRoomWindowIdentity(nint window, out string className, out uint process)
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

    private static bool TryHasRoomFullscreenApplication(uint shellProcess, out bool fullscreen) =>
        TryHasRoomFullscreenApplicationWithinBudget(shellProcess, out fullscreen, null);

    private static bool TryHasRoomFullscreenApplicationWithinBudget(uint shellProcess, out bool fullscreen,
        long? deadlineTimestamp)
    {
        fullscreen = false;
        if (!HasRoomInspectionTime(deadlineTimestamp)) return false;
        var monitors = new List<DesktopPixelRect>();
        var known = true;
        if (!EnumRoomDisplayMonitors(0, 0, (nint monitor, nint _, ref NativeRect _, nint _) =>
            {
                if (!HasRoomInspectionTime(deadlineTimestamp)) { known = false; return false; }
                var information = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref information) || !IsRoomRectangleValid(information.Monitor) ||
                    !HasRoomInspectionTime(deadlineTimestamp))
                {
                    known = false;
                    return false;
                }
                monitors.Add(ToRoomPixelRect(information.Monitor));
                return true;
            }, 0) || !known || monitors.Count == 0 || !HasRoomInspectionTime(deadlineTimestamp)) return false;

        var foundFullscreen = false;
        var completed = EnumWindows((window, _) =>
        {
            if (!HasRoomInspectionTime(deadlineTimestamp)) { known = false; return false; }
            if (!IsValidWindow(window)) { known = false; return false; }
            if (!IsWindowVisible(window) || IsRoomWindowMinimized(window)) return true;
            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                !TryGetRoomWindowIdentity(window, out var className, out var process) ||
                !HasRoomInspectionTime(deadlineTimestamp))
            {
                known = false;
                return false;
            }
            if (process == shellProcess && WallpaperWheelInputPolicy.IsShellClass(className)) return true;
            if (GetRoomWindowCloaked(window, 14, out var cloaked, sizeof(int)) != 0 ||
                !HasRoomInspectionTime(deadlineTimestamp))
            {
                known = false;
                return false;
            }
            if (cloaked != 0) return true;
            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                GetRoomWindowFrame(window, 9, out var bounds, Marshal.SizeOf<NativeRect>()) != 0 ||
                !HasRoomInspectionTime(deadlineTimestamp) ||
                bounds.Right < bounds.Left || bounds.Bottom < bounds.Top)
            {
                known = false;
                return false;
            }
            // Some helpers publish an empty DWM frame. Check their native rectangle before
            // declaring a known zero-area window harmless; failed/reversed geometry stays unknown.
            if (bounds.Right == bounds.Left || bounds.Bottom == bounds.Top)
            {
                if (!HasRoomInspectionTime(deadlineTimestamp) || !GetWindowRect(window, out bounds) ||
                    !HasRoomInspectionTime(deadlineTimestamp) || bounds.Right < bounds.Left || bounds.Bottom < bounds.Top)
                { known = false; return false; }
                if (bounds.Right == bounds.Left || bounds.Bottom == bounds.Top) return true;
            }
            var state = new WallpaperWheelWindowState(true, false, false, false, ToRoomPixelRect(bounds));
            if (monitors.Any(monitor => WallpaperWheelInputPolicy.IsBlockingFullscreen(state, monitor)))
            {
                foundFullscreen = true;
                return false;
            }
            return true;
        }, 0);
        fullscreen = foundFullscreen;
        // A deliberate early stop after a blocking window is a known rejection, never a query success for input.
        return known && (completed || foundFullscreen) && HasRoomInspectionTime(deadlineTimestamp);
    }

    // This is a cooperative query deadline, including each enumeration callback and every Shell-message wait.
    // Native cleanup and OS scheduling cannot be preempted, so this does not promise a hard real-time callback bound.
    private static uint RemainingRoomInspectionMilliseconds(long? deadlineTimestamp)
    {
        if (deadlineTimestamp is null) return uint.MaxValue;
        var remaining = deadlineTimestamp.Value - Stopwatch.GetTimestamp();
        return remaining <= 0 ? 0 : (uint)Math.Min(uint.MaxValue, Math.Floor(remaining * 1000d / Stopwatch.Frequency));
    }

    private static bool HasRoomInspectionTime(long? deadlineTimestamp) => RemainingRoomInspectionMilliseconds(deadlineTimestamp) > 0;

    private static bool IsRoomRectangleValid(NativeRect bounds) => bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;

    private static bool HasRoomMouseModifier() => IsKeyDown(0x10) || HasSystemKeyboardModifier();

    private static DesktopPixelRect ToRoomPixelRect(NativeRect bounds) =>
        new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseInput
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetRoomMouseHook(int hookId, LowLevelMouseProcedure procedure, nint module, uint threadId);

    private delegate bool RoomMonitorCallback(nint monitor, nint deviceContext, ref NativeRect rectangle, nint parameter);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayMonitors")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumRoomDisplayMonitors(nint deviceContext, nint clip,
        RoomMonitorCallback callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsRoomWindowMinimized(nint window);

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern nint RoomWindowFromPoint(NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    private static extern nint GetRoomWindowAncestor(nint window, uint flags);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int GetRoomWindowCloaked(nint window, uint attribute, out int cloaked, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int GetRoomWindowFrame(nint window, uint attribute, out NativeRect rectangle, int size);
}
