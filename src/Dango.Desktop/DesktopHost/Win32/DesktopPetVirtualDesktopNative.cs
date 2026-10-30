using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.DesktopHost.Win32;

// Only documented shell/USER APIs are used. A tool/no-activate pet is not a
// reliable application-view anchor. Keep a separate ordinary top-level owner.
// Its zero-alpha pixel and SW_SHOWNOACTIVATE preserve the user's screen/focus;
// A second, never-shown owner suppresses taskbar/Alt+Tab participation without
// removing the anchor's Shell application view (DeleteTab would remove it).
// Real virtual-desktop/Task View behavior still requires Windows acceptance.
internal sealed class DesktopPetVirtualDesktopNative : IDesktopPetVirtualDesktopPlatform
{
    private const int OwnerIndex = -8;
    private readonly uint nativeThread = GetCurrentThreadId();
    private DesktopAnchorWindow? anchor;
    private DesktopAnchorWindow? hiddenTaskbarOwner;
    private IVirtualDesktopManager? desktops;
    private nint originalOwner;
    private nint attachedPet;

    public void ValidateWindow(nint window)
    {
        if (!IsWindow(window) || GetWindowThreadProcessId(window, out var process) != nativeThread ||
            process != (uint)Environment.ProcessId)
            throw new InvalidOperationException("虚拟桌面关联只能处理 Dango 当前界面线程的自有窗口。");
    }

    public nint CreateAnchor()
    {
        if (anchor is not null) throw new InvalidOperationException("虚拟桌面所属窗口已建立。");
        desktops = CreateCom<IVirtualDesktopManager>("AA509086-5CA9-4C25-8F95-589D3C07B48A");
        // HwndTarget removes WS_EX_LAYERED when per-pixel opacity is disabled,
        // including during WM_STYLECHANGING. This owner needs global alpha and
        // no WPF rendering target, so use the existing NativeWindow boundary.
        hiddenTaskbarOwner = new DesktopAnchorWindow(this);
        anchor = new DesktopAnchorWindow(this, hiddenTaskbarOwner.Handle);
        if (!SetLayeredWindowAttributes(anchor.Handle, 0, 0, 2))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法建立透明桌宠所属窗口。");
        _ = ShowWindow(anchor.Handle, 4); // SW_SHOWNOACTIVATE
        WaitForAnchorRegistration();
        return anchor.Handle;
    }

    private void WaitForAnchorRegistration()
    {
        // Shell registers/replaces its application view asynchronously. In
        // particular, rebuilding after a presented WPF window may initially
        // return S_OK with an empty GUID. That is not a verified affiliation.
        // Bound this initial preparation only; runtime reads remain fail-closed.
        // Do not pump the WPF dispatcher here: Attach must not reenter room
        // navigation or consume an unrelated application callback.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var window = anchor?.Handle ?? throw new InvalidOperationException("桌宠所属窗口尚未建立。");
            ValidateWindow(window);
            Check(Manager.GetWindowDesktopId(window, out var desktop), "无法读取桌宠虚拟桌面归属。");
            if (desktop != Guid.Empty) return;
            if (elapsed.ElapsedMilliseconds >= 1500)
                throw new InvalidOperationException("Windows 未在准备时限内确认桌宠虚拟桌面归属。");
            Thread.Sleep(10);
        }
    }

    public void SetOwner(nint pet, nint owner)
    {
        ValidateWindow(pet);
        ValidateWindow(owner);
        originalOwner = GetOwner(pet);
        if (originalOwner != 0) ValidateWindow(originalOwner);
        SetOwnerChecked(pet, owner);
        attachedPet = pet;
    }

    public void ValidateOwner(nint pet, nint owner)
    {
        if (attachedPet != pet || anchor?.Handle != owner || GetOwner(pet) != owner)
            throw new InvalidOperationException("桌宠所属窗口关联已丢失。");
        if (hiddenTaskbarOwner is null) throw new InvalidOperationException("桌宠隐藏所属窗口已丢失。");
        ValidateWindow(hiddenTaskbarOwner.Handle);
        if (GetOwner(owner) != hiddenTaskbarOwner.Handle || IsWindowVisible(hiddenTaskbarOwner.Handle))
            throw new InvalidOperationException("桌宠隐藏所属窗口状态已失效。");
    }

    public void RestoreOwner(nint pet, nint owner)
    {
        // The caller normally closes the pet first. Never touch a recycled HWND
        // or replace ownership installed by a different lifecycle generation.
        if (pet != attachedPet || !IsWindow(pet) ||
            GetWindowThreadProcessId(pet, out var process) != nativeThread ||
            process != (uint)Environment.ProcessId || GetOwner(pet) != owner) return;
        var restore = originalOwner != 0 && IsWindow(originalOwner) &&
            GetWindowThreadProcessId(originalOwner, out var ownerProcess) == nativeThread &&
            ownerProcess == (uint)Environment.ProcessId ? originalOwner : 0;
        SetOwnerChecked(pet, restore);
        attachedPet = 0;
    }

    public Guid GetDesktop(nint window)
    {
        Check(Manager.GetWindowDesktopId(window, out var result), "无法读取桌宠虚拟桌面归属。");
        return result;
    }

    public bool IsCurrent(nint window)
    {
        Check(Manager.IsWindowOnCurrentVirtualDesktop(window, out var result), "无法查询当前虚拟桌面。");
        return result;
    }

    public void Dispose()
    {
        try { anchor?.Dispose(); }
        finally
        {
            try { hiddenTaskbarOwner?.Dispose(); }
            finally
            {
                anchor = hiddenTaskbarOwner = null;
                if (desktops is not null) _ = Marshal.ReleaseComObject(desktops);
                desktops = null;
            }
        }
    }

    private IVirtualDesktopManager Manager => desktops ?? throw new InvalidOperationException("虚拟桌面服务尚未建立。");

    private nint OwnerMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message == 0x0084) { handled = true; return -1; } // WM_NCHITTEST / HTTRANSPARENT
        return 0;
    }

    private static T CreateCom<T>(string classId) => (T)(Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid(classId), throwOnError: true)!) ??
        throw new InvalidOperationException("Windows Shell 服务不可用。"));

    private static void Check(int result, string message)
    {
        if (result != 0) throw new COMException(message, result);
    }

    private static nint GetOwner(nint window) => GetWindow(window, 4); // GW_OWNER
    private static void SetOwnerChecked(nint window, nint owner)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = nint.Size == 8 ? SetWindowLongPtr(window, OwnerIndex, owner) :
            new nint(SetWindowLong(window, OwnerIndex, owner.ToInt32()));
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法设置桌宠所属窗口。");
        if (GetOwner(window) != owner) throw new InvalidOperationException("Windows 未保留桌宠所属窗口关联。");
    }

    private sealed class DesktopAnchorWindow : Forms.NativeWindow, IDisposable
    {
        private readonly DesktopPetVirtualDesktopNative owner;

        internal DesktopAnchorWindow(DesktopPetVirtualDesktopNative owner, nint parent = 0)
        {
            this.owner = owner;
            CreateHandle(new Forms.CreateParams
            {
                Caption = "Dango Desktop Pet Owner",
                Parent = parent,
                Style = unchecked((int)0x80000000), // WS_POPUP, initially hidden
                ExStyle = 0x00080000, // WS_EX_LAYERED; no TOOLWINDOW/NOACTIVATE
                X = 0, Y = 0, Width = 1, Height = 1
            });
        }

        protected override void WndProc(ref Forms.Message message)
        {
            var handled = false;
            var result = owner.OwnerMessage(message.HWnd, message.Msg, message.WParam, message.LParam, ref handled);
            if (handled) message.Result = result;
            else base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle != 0) DestroyHandle();
        }
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
}
