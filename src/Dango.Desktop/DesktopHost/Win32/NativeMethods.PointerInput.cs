using System.Runtime.InteropServices;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    internal delegate nint LowLevelMouseProcedure(int code, nint wordParameter, nint longParameter);

    private static readonly object MouseHooksLock = new();
    private static readonly Dictionary<nint, LowLevelMouseProcedure> MouseHooks = [];

    internal static nint InstallLowLevelMouseHook(LowLevelMouseProcedure procedure)
    {
        var installed = SetMouseHook(14, procedure, GetModuleHandle(null), 0);
        if (installed != 0) lock (MouseHooksLock) MouseHooks[installed] = procedure;
        return installed;
    }

    internal static bool UninstallMouseHook(nint hook)
    {
        if (!UnhookWindowsHookEx(hook)) return false;
        lock (MouseHooksLock) MouseHooks.Remove(hook);
        return true;
    }

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
    private static extern nint SetMouseHook(int hookId, LowLevelMouseProcedure procedure, nint module, uint threadId);
}
