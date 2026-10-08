using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dango.Desktop.DesktopHost.Win32;

internal static partial class NativeMethods
{
    internal static void ApplyDesktopPetWindowStyles(nint window)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(window, GwlExStyle,
            GetWindowLongPtr(window, GwlExStyle) | 0x08000000 | 0x80 | 0x20);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法启用桌宠不激活窗口。");
    }

    internal static void SetDesktopPetInteraction(nint window, bool enabled)
    {
        var style = GetWindowLongPtr(window, GwlExStyle);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(window, GwlExStyle, enabled ? style & ~0x20 : style | 0x20);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法更新桌宠输入状态。");
    }

    internal static void PositionDesktopPetWindow(nint window, int left, int top, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (!GetWindowRect(window, out var current))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法读取独立团子层位置。");
        var samePosition = current.Left == left && current.Top == top;
        var sameSize = current.Right - current.Left == width && current.Bottom - current.Top == height;
        if (samePosition && sameSize && (GetWindowLongPtr(window, GwlExStyle) & 0x8) != 0) return;
        const uint noSize = 0x0001, noMove = 0x0002;
        var flags = SwpNoActivate;
        if (sameSize) flags |= noSize;
        if (samePosition) flags |= noMove;
        if (!SetWindowPos(window, new nint(-1), left, top, width, height, flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法定位独立团子层。");
    }

    // A real native region excludes transparent corners even over another process.
    // HTTRANSPARENT alone only searches windows owned by the same thread.
    internal static void SetDesktopPetRegion(nint window, IReadOnlyList<NativePoint[]> polygons)
    {
        var region = DesktopPetCreateRectRgn(0, 0, 0, 0);
        if (region == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            foreach (var polygon in polygons)
            {
                if (polygon.Length < 3) continue;
                var part = DesktopPetCreatePolygonRgn(polygon, polygon.Length, 2);
                if (part == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                try
                {
                    if (DesktopPetCombineRgn(region, region, part, 2) == 0)
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                finally { _ = DesktopPetDeleteObject(part); }
            }
            if (DesktopPetSetWindowRgn(window, region, true) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法更新团子透明轮廓。");
            region = 0; // USER owns the region after success.
        }
        finally { if (region != 0) _ = DesktopPetDeleteObject(region); }
    }

    internal static nint DesktopPetCapturedWindow => DesktopPetGetCapture();
    internal static uint DesktopPetMessageTimestamp => unchecked((uint)DesktopPetGetMessageTime());
    // SendMessage sets IMDT_UNAVAILABLE, including same-thread directed test input.
    // Only a physical pointer WM participates in low-level-hook duplicate suppression.
    internal static bool DesktopPetCurrentMessageIsPointerInput => DesktopPetGetCurrentInputMessageSource(out var source) &&
        (source.DeviceType & (2u | 4u | 8u | 16u)) != 0;

    internal static (NativePoint Point, bool Shift, bool HasForbiddenModifier, uint Timestamp) ReadDesktopPetGestureInput(nint parameter)
    {
        var input = Marshal.PtrToStructure<LowLevelMouseInput>(parameter);
        return (input.Point, IsKeyDown(0x10), HasSystemKeyboardModifier(), input.Time);
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateRectRgn", SetLastError = true)]
    private static extern nint DesktopPetCreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", EntryPoint = "CreatePolygonRgn", SetLastError = true)]
    private static extern nint DesktopPetCreatePolygonRgn([In] NativePoint[] points, int count, int mode);
    [DllImport("gdi32.dll", EntryPoint = "CombineRgn", SetLastError = true)]
    private static extern int DesktopPetCombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DesktopPetDeleteObject(nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowRgn", SetLastError = true)]
    private static extern int DesktopPetSetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll", EntryPoint = "GetCapture")]
    private static extern nint DesktopPetGetCapture();
    [DllImport("user32.dll", EntryPoint = "GetMessageTime")]
    private static extern int DesktopPetGetMessageTime();
    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopPetInputMessageSource
    {
        public uint DeviceType;
        public uint OriginId;
    }
    [DllImport("user32.dll", EntryPoint = "GetCurrentInputMessageSource")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DesktopPetGetCurrentInputMessageSource(out DesktopPetInputMessageSource source);
}
