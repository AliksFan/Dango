using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Dango.Desktop.Accessories.Editing;
using Size = System.Windows.Size;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryEditorChromeHostTests
{
    [STATestMethod]
    public void HiddenNativeHostAppliesCornerRegionAndReanchorsAfterResizeWithoutStartingBrowser()
    {
        // A real HWND, deliberately lacking WS_VISIBLE. No Show/Activate, browser or input.
        using var source = new HwndSource(new HwndSourceParameters("Dango hidden native chrome region check")
        { Width = 1120, Height = 760, WindowStyle = unchecked((int)0x80000000) });
        using var host = new AccessoryEditorBrowserHost();
        var root = new Grid { Width = 800, Height = 600 };
        root.Children.Add(host); source.RootVisual = root;
        Arrange(800, 600);
        Assert.AreNotEqual(nint.Zero, host.Handle, "The test must exercise the host's real native child HWND.");
        AssertHiddenAndIdle();
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.AreNotEqual(nint.Zero, region);
        try
        {
            Assert.AreEqual(0, GetWindowRgn(host.Handle, region), "Default hosts must not receive a custom native region.");
            host.WindowControlsCutout = new Size(132, 38);
            CheckCutout();
            Arrange(960, 680);
            CheckCutout();
            host.WindowControlsCutout = new Size();
            Assert.AreEqual(0, GetWindowRgn(host.Handle, region), "Disabling the cutout must remove the assigned native region.");
            AssertHiddenAndIdle();
        }
        finally { DeleteObject(region); source.RootVisual = null; }

        void Arrange(int width, int height)
        {
            root.Width = width; root.Height = height;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            Assert.AreEqual((double)width, host.ActualWidth); Assert.AreEqual((double)height, host.ActualHeight);
        }
        void CheckCutout()
        {
            AssertHiddenAndIdle();
            Assert.AreEqual(3, GetWindowRgn(host.Handle, region), "The HWND must have an actual complex region with its upper-right corner removed.");
            Assert.AreEqual(3, GetRgnBox(region, out var bounds));
            var dpi = VisualTreeHelper.GetDpi(host);
            var width = (int)Math.Ceiling(host.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(host.ActualHeight * dpi.DpiScaleY);
            var cutWidth = (int)Math.Ceiling(132 * dpi.DpiScaleX);
            var cutHeight = (int)Math.Ceiling(38 * dpi.DpiScaleY);
            Assert.AreEqual(0, bounds.Left); Assert.AreEqual(0, bounds.Top);
            Assert.AreEqual(width, bounds.Right); Assert.AreEqual(height, bounds.Bottom);
            Assert.IsTrue(PtInRegion(region, width - cutWidth - 1, 0), "The last toolbar pixel before the cutout must remain.");
            Assert.IsFalse(PtInRegion(region, width - cutWidth, 0), "The first cutout pixel must expose WPF chrome.");
            Assert.IsFalse(PtInRegion(region, width - 1, cutHeight - 1));
            Assert.IsTrue(PtInRegion(region, width - 1, cutHeight), "The canvas immediately below the cutout must remain.");
            Assert.IsTrue(PtInRegion(region, width / 2, height / 2));
            Assert.IsTrue(PtInRegion(region, 0, height - 1));
        }
        void AssertHiddenAndIdle()
        {
            Assert.IsFalse(IsWindowVisible(source.Handle), "The parent must stay invisible throughout the native check.");
            Assert.AreEqual(AccessoryEditorRuntimeState.Idle, host.Runtime.State);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")]
    private static extern int GetRgnBox(nint region, out NativeRect bounds);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
