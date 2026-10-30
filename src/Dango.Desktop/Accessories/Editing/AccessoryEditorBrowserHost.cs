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
    private System.Windows.Size windowControlsCutout;
    private bool cutoutApplied;
    private Rect simulationCutout = Rect.Empty;
    internal Rect SimulationCutout
    {
        get => simulationCutout;
        set { simulationCutout = value; RefreshBounds(); }
    }
    internal System.Windows.Size WindowControlsCutout
    {
        get => windowControlsCutout;
        set { windowControlsCutout = value; RefreshBounds(); }
    }
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
        ApplyWindowControlsCutout(dpi);
        Runtime.SetBounds(new System.Drawing.Rectangle(0, 0,
            Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY))), IsVisible);
    }

    private void ApplyWindowControlsCutout(DpiScale dpi)
    {
        var cutouts = new[] { GetWindowControlsCutoutBounds(RenderSize, dpi), GetSimulationCutoutBounds(RenderSize, dpi) }
            .Where(bounds => bounds.HasValue).Select(bounds => bounds!.Value).ToArray();
        if (cutouts.Length == 0)
        {
            if (cutoutApplied)
            {
                if (SetWindowRgn(handle, 0, true) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                cutoutApplied = false;
            }
            return;
        }
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        var region = CreateRectRgn(0, 0, width, height);
        if (region == 0)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            foreach (var bounds in cutouts)
            {
                var cutout = CreateRectRgn(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                if (cutout == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    if (CombineRgn(region, region, cutout, 4 /* RGN_DIFF */) == 0)
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { DeleteObject(cutout); }
            }
            if (SetWindowRgn(handle, region, true) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            region = 0; // Windows owns a successfully assigned window region.
            cutoutApplied = true;
        }
        finally
        {
            if (region != 0) DeleteObject(region);
        }
    }

    // The same physical-pixel rectangle is used by the native region and unshown DPI/layout checks.
    // All other browser hosts keep the default empty size and therefore retain their full HWND.
    internal System.Drawing.Rectangle? GetWindowControlsCutoutBounds(System.Windows.Size viewport, DpiScale dpi)
    {
        if (WindowControlsCutout.Width <= 0 || WindowControlsCutout.Height <= 0) return null;
        var width = Math.Max(1, (int)Math.Ceiling(viewport.Width * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(viewport.Height * dpi.DpiScaleY));
        var cutoutWidth = Math.Min(width, (int)Math.Ceiling(WindowControlsCutout.Width * dpi.DpiScaleX));
        var cutoutHeight = Math.Min(height, (int)Math.Ceiling(WindowControlsCutout.Height * dpi.DpiScaleY));
        return new System.Drawing.Rectangle(width - cutoutWidth, 0, cutoutWidth, cutoutHeight);
    }

    internal System.Drawing.Rectangle? GetSimulationCutoutBounds(System.Windows.Size viewport, DpiScale dpi)
    {
        var bounds = SimulationCutout;
        if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return null;
        bounds.Intersect(new Rect(new System.Windows.Point(), viewport));
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
        var left = (int)Math.Floor(bounds.Left * dpi.DpiScaleX);
        var top = (int)Math.Floor(bounds.Top * dpi.DpiScaleY);
        return System.Drawing.Rectangle.FromLTRB(left, top, (int)Math.Ceiling(bounds.Right * dpi.DpiScaleX),
            (int)Math.Ceiling(bounds.Bottom * dpi.DpiScaleY));
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
    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
