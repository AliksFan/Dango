using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>A child HWND owns the browser. It has no animation loop and never activates a window.</summary>
internal sealed class AccessoryEditorBrowserHost : HwndHost
{
    private nint handle;
    private bool released;
    internal AccessoryEditorRuntime Runtime { get; } = new();

    internal AccessoryEditorBrowserHost()
    {
        SizeChanged += (_, _) => RefreshBounds();
        IsVisibleChanged += (_, _) => RefreshBounds();
        Runtime.StateChanged += RefreshBounds;
    }

    internal Task<bool> InitializeAsync(string assets, string profile)
    {
        if (handle == 0 || released) return Task.FromResult(false);
        return Runtime.InitializeAsync(handle, assets, profile);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        handle = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000,
            0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
        if (handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this, handle);
    }

    private void RefreshBounds()
    {
        if (released || handle == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Runtime.SetBounds(new System.Drawing.Rectangle(0, 0,
            Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY))), IsVisible);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { base.OnDpiChanged(oldDpi, newDpi); RefreshBounds(); }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseBrowser(); DestroyWindow(hwnd.Handle); handle = 0;
    }

    internal void ReleaseBrowser()
    {
        if (released) return;
        released = true; Runtime.StateChanged -= RefreshBounds; Runtime.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(int extendedStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}
