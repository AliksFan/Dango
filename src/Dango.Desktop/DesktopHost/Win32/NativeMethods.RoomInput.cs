using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.WorkerW;

namespace Dango.Desktop.DesktopHost.Win32;

internal enum RoomInputInspectionPhase
{
    Context, ShellTarget, Foreground, BlockingWindows, Cursor, Pointer, Icon,
    CurrentPointer, CurrentIcon, RefreshBlockingWindows, RefreshPointer, RefreshIcon, RefreshForeground, Completed,
}

internal readonly record struct RoomInputInspectionTrace(RoomInputInspectionPhase Phase, bool BudgetExpired,
    bool Exception, double ElapsedMilliseconds, RoomPointerInspectionTrace Pointer = default);

internal enum RoomPointerRole { Unknown, DesktopShell, OwnWallpaperChild, TaskbarOrMenu, OtherApplication, OtherShell }
internal readonly record struct RoomPointerInspectionTrace(RoomPointerRole Role, bool ShellDesktopClass,
    bool SameShellProcess, bool OwnProcessChild, bool TargetRootRelation);

internal readonly record struct RoomPointerQualification(bool Known, bool Background, bool Icon);

internal static partial class NativeMethods
{
    internal delegate nint LowLevelMouseProcedure(int code, nint wordParameter, nint longParameter);

    private static readonly object RoomMouseHooksLock = new();
    private static readonly Dictionary<nint, LowLevelMouseProcedure> RoomMouseHooks = [];
    private static readonly object RoomKeyboardHooksLock = new();
    private static readonly Dictionary<nint, LowLevelKeyboardProcedure> RoomKeyboardHooks = [];

    internal static nint InstallRoomKeyboardHook(LowLevelKeyboardProcedure procedure)
    {
        var installed = InstallLowLevelKeyboardHook(procedure);
        if (installed != 0) lock (RoomKeyboardHooksLock) RoomKeyboardHooks[installed] = procedure;
        return installed;
    }
    internal static bool UninstallRoomKeyboardHook(nint hook)
    {
        if (!UninstallKeyboardHook(hook)) return false;
        lock (RoomKeyboardHooksLock) RoomKeyboardHooks.Remove(hook);
        return true;
    }

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

    internal static WallpaperGameplayMouseData ReadRoomGameplayMouseInput(nint longParameter)
    {
        var data = Marshal.PtrToStructure<LowLevelMouseInput>(longParameter);
        return new(new(data.Point.X, data.Point.Y), data.Flags, IsKeyDown(0x10), HasSystemKeyboardModifier());
    }

    internal static WallpaperWheelInputEnvironment InspectRoomGameplayInput(WallpaperWheelInputContext context,
        WallpaperWheelPoint? point, bool atEvent, bool allowShift) =>
        InspectRoomGameplayInput(context, point, atEvent, allowShift, out _);

    internal static WallpaperWheelInputEnvironment InspectRoomGameplayInput(WallpaperWheelInputContext context,
        WallpaperWheelPoint? point, bool atEvent, bool allowShift, out RoomInputInspectionTrace trace)
    {
        long? deadline = atEvent ? Stopwatch.GetTimestamp() + Stopwatch.Frequency * 20 / 1000 : null;
        return InspectRoomWheelInputCore(context, point, deadline, out trace, allowShift);
    }

    internal static WallpaperWheelInputEnvironment InspectRoomWheelInput(
        WallpaperWheelInputContext context, WallpaperWheelPoint point) => InspectRoomWheelInputCore(context, point, null, out _);

    internal static WallpaperWheelInputEnvironment InspectRoomWheelInputAtEvent(
        WallpaperWheelInputContext context, WallpaperWheelPoint point) => InspectRoomWheelInputCore(context, point,
            Stopwatch.GetTimestamp() + Stopwatch.Frequency * 20 / 1000, out _);

    internal static WallpaperWheelInputEnvironment InspectRoomWheelInputDiagnostic(
        WallpaperWheelInputContext context, WallpaperWheelPoint point, bool atEvent, out RoomInputInspectionTrace trace) =>
        InspectRoomWheelInputCore(context, point, atEvent ? Stopwatch.GetTimestamp() + Stopwatch.Frequency * 20 / 1000 : null, out trace);

    private static WallpaperWheelInputEnvironment InspectRoomWheelInputCore(
        WallpaperWheelInputContext context, WallpaperWheelPoint? inputPoint, long? deadlineTimestamp,
        out RoomInputInspectionTrace trace, bool allowShift = false)
    {
        // Only transient handles, classes, rectangles and hit booleans are queried. No titles or contents are read.
        var started = Stopwatch.GetTimestamp();
        var phase = RoomInputInspectionPhase.Context;
        var exception = false;
        var pointerTrace = default(RoomPointerInspectionTrace);
        try
        {
            if (!HasRoomInspectionTime(deadlineTimestamp)) return default;
            if (HasSystemKeyboardModifier() || (!allowShift && IsKeyDown(0x10))) return new(true, false, false, false, false, true);
            phase = RoomInputInspectionPhase.ShellTarget;
            if (!WallpaperWheelInputPolicy.IsContextEligible(context) || !IsValidDesktopTarget(context.Target) ||
                !TryGetRoomWindowIdentity(context.Target.ShellViewHandle, out var shellClass, out var shellProcess) ||
                shellClass != "SHELLDLL_DefView" || !HasRoomInspectionTime(deadlineTimestamp)) return default;

            var shellRoot = GetRoomWindowAncestor(context.Target.ShellViewHandle, 2);
            phase = RoomInputInspectionPhase.Foreground;
            if (!IsValidWindow(shellRoot) ||
                !TryIsRoomDesktopForeground(context.Target, shellRoot, shellProcess, out var desktopForeground) ||
                !HasRoomInspectionTime(deadlineTimestamp)) return default;
            if (!desktopForeground) return new(true, false, false, false, false);

            phase = RoomInputInspectionPhase.BlockingWindows;
            if (!TryHasRoomFullscreenApplicationWithinBudget(shellProcess, out var fullscreen, deadlineTimestamp)) return default;
            if (fullscreen) return new(true, true, true, false, false);

            // Gameplay keys depend on Shell/global eligibility, regardless of pointer position or target display.
            if (inputPoint is null)
            {
                phase = RoomInputInspectionPhase.RefreshForeground;
                if (!TryIsRoomDesktopForeground(context.Target, shellRoot, shellProcess, out desktopForeground) ||
                    !HasRoomInspectionTime(deadlineTimestamp)) return default;
                phase = RoomInputInspectionPhase.Completed;
                return new(true, desktopForeground, false, true, false,
                    HasSystemKeyboardModifier() || (!allowShift && IsKeyDown(0x10)));
            }
            var point = inputPoint.Value;

            phase = RoomInputInspectionPhase.Cursor;
            if (!HasRoomInspectionTime(deadlineTimestamp) || !GetCursorPos(out var cursor)) return default;
            var currentPoint = new WallpaperWheelPoint(cursor.X, cursor.Y);
            if (!WallpaperWheelInputPolicy.ContainsDevicePoint(context.ScreenBounds, point) ||
                !WallpaperWheelInputPolicy.ContainsDevicePoint(context.ScreenBounds, currentPoint))
                return new(true, true, false, false, false);

            phase = RoomInputInspectionPhase.Pointer;
            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                !TryIsRoomDesktopPoint(context.Target, shellRoot, shellProcess, point, out var pointerAllowed, out pointerTrace)) return default;
            phase = RoomInputInspectionPhase.Icon;
            if (!TryIsDesktopIconAtPoint(context.Target, point.X, point.Y, out var iconHit,
                    deadlineTimestamp: deadlineTimestamp)) return default;
            if (currentPoint != point)
            {
                phase = RoomInputInspectionPhase.CurrentPointer;
                if (!HasRoomInspectionTime(deadlineTimestamp) ||
                    !TryIsRoomDesktopPoint(context.Target, shellRoot, shellProcess, currentPoint, out var currentAllowed, out pointerTrace)) return default;
                phase = RoomInputInspectionPhase.CurrentIcon;
                if (!TryIsDesktopIconAtPoint(context.Target, currentPoint.X, currentPoint.Y, out var currentIconHit,
                        deadlineTimestamp: deadlineTimestamp)) return default;
                pointerAllowed &= currentAllowed;
                iconHit |= currentIconHit;
            }

            // Shell hit tests can pump messages or wait. Refresh every eligibility fact they may have outlived.
            phase = RoomInputInspectionPhase.RefreshBlockingWindows;
            if (!TryHasRoomFullscreenApplicationWithinBudget(shellProcess, out fullscreen, deadlineTimestamp) ||
                !GetCursorPos(out cursor)) return default;
            if (fullscreen) return new(true, true, true, false, false);
            var refreshedPoint = new WallpaperWheelPoint(cursor.X, cursor.Y);
            var refreshed = RefreshRoomPointerQualification(context.ScreenBounds, refreshedPoint,
                pointerAllowed, iconHit, finalPoint =>
                {
                    phase = RoomInputInspectionPhase.RefreshPointer;
                    if (!HasRoomInspectionTime(deadlineTimestamp) ||
                        !TryIsRoomDesktopPoint(context.Target, shellRoot, shellProcess, finalPoint,
                            out var finalAllowed, out pointerTrace)) return default;
                    phase = RoomInputInspectionPhase.RefreshIcon;
                    if (!TryIsDesktopIconAtPoint(context.Target, finalPoint.X, finalPoint.Y, out var finalIconHit,
                            deadlineTimestamp: deadlineTimestamp)) return default;
                    return new(true, finalAllowed, finalIconHit);
                });
            if (!refreshed.Known) return default;
            pointerAllowed = refreshed.Background;
            iconHit = refreshed.Icon;
            phase = RoomInputInspectionPhase.RefreshForeground;
            if (!HasRoomInspectionTime(deadlineTimestamp) ||
                !TryIsRoomDesktopForeground(context.Target, shellRoot, shellProcess, out desktopForeground)) return default;
            var modified = HasSystemKeyboardModifier() || (!allowShift && IsKeyDown(0x10));
            phase = RoomInputInspectionPhase.Completed;
            return HasRoomInspectionTime(deadlineTimestamp)
                ? new(true, desktopForeground, false, pointerAllowed, iconHit, modified) : default;
        }
        catch
        {
            exception = true;
            return default;
        }
        finally
        {
            trace = new(phase, !HasRoomInspectionTime(deadlineTimestamp), exception,
                (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency, pointerTrace);
        }
    }

    internal static RoomPointerQualification RefreshRoomPointerQualification(LogicalRect screenBounds,
        WallpaperWheelPoint refreshedPoint, bool background, bool icon,
        Func<WallpaperWheelPoint, RoomPointerQualification> qualifyPoint)
    {
        if (!WallpaperWheelInputPolicy.ContainsDevicePoint(screenBounds, refreshedPoint)) return new(true, false, icon);
        var final = qualifyPoint(refreshedPoint);
        return final.Known ? new(true, background && final.Background, icon || final.Icon) : default;
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
            className, process == shellProcess, ProvenRoomSystemShellWindow(shellProcess));
        return true;
    }

    private static bool TryIsRoomDesktopPoint(WorkerWDesktopTarget target, nint shellRoot, uint shellProcess,
        WallpaperWheelPoint point, out bool allowed)
        => TryIsRoomDesktopPoint(target, shellRoot, shellProcess, point, out allowed, out _);

    private static bool TryIsRoomDesktopPoint(WorkerWDesktopTarget target, nint shellRoot, uint shellProcess,
        WallpaperWheelPoint point, out bool allowed, out RoomPointerInspectionTrace trace)
    {
        allowed = false;
        trace = default;
        var window = RoomWindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        if (!TryGetRoomWindowIdentity(window, out var className, out var process)) return false;
        var root = GetRoomWindowAncestor(window, 2);
        if (!IsValidWindow(root)) return false;
        var shellClass = WallpaperWheelInputPolicy.IsDesktopShellClass(className);
        var shellProcessMatches = process == shellProcess;
        var ownChild = process == Environment.ProcessId &&
            (IsChild(target.ParentHandle, window) || IsChild(target.WallpaperLayerHandle, window));
        var rootMatches = root == shellRoot || root == target.ParentHandle || root == target.WallpaperLayerHandle;
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768")
        { trace = new(RoomPointerRole.TaskbarOrMenu, shellClass, shellProcessMatches, ownChild, rootMatches); return true; }
        var desktop = WallpaperWheelInputPolicy.IsDesktopForeground(target, shellRoot, window, root, className,
            shellProcessMatches, ProvenRoomSystemShellWindow(shellProcess));
        allowed = desktop || ownChild;
        trace = new(desktop ? RoomPointerRole.DesktopShell : ownChild ? RoomPointerRole.OwnWallpaperChild :
            shellProcessMatches ? RoomPointerRole.OtherShell : RoomPointerRole.OtherApplication,
            shellClass, shellProcessMatches, ownChild, rootMatches);
        return true;
    }

    private static nint ProvenRoomSystemShellWindow(uint shellProcess)
    {
        var shell = GetRoomShellWindow();
        return TryGetRoomWindowIdentity(shell, out var className, out var process) &&
            className == "Progman" && process == shellProcess ? shell : nint.Zero;
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
            var state = new WallpaperWheelWindowState(true, false, false, false, ToRoomPixelRect(bounds), IsRoomWindowMaximized(window));
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

    [DllImport("user32.dll", EntryPoint = "IsZoomed")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsRoomWindowMaximized(nint window);

    [DllImport("user32.dll", EntryPoint = "GetShellWindow")]
    private static extern nint GetRoomShellWindow();

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern nint RoomWindowFromPoint(NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    private static extern nint GetRoomWindowAncestor(nint window, uint flags);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int GetRoomWindowCloaked(nint window, uint attribute, out int cloaked, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int GetRoomWindowFrame(nint window, uint attribute, out NativeRect rectangle, int size);
}
