using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Dango.Desktop.DesktopHost.Win32;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace Dango.Desktop.DesktopHost.WorkerW;

internal enum PrototypeDesktopObject
{
    Dango,
    Cushion,
}

internal sealed class DesktopObjectOverlayWindow : Window
{
    private const int WmMouseActivate = 0x0021;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmCaptureChanged = 0x0215;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private readonly PrototypeDesktopObject objectKind;
    private readonly Action<double, double>? positionChanged;
    private nint windowHandle;
    private NativeMethods.NativePoint pointerOrigin;
    private double windowLeftOrigin;
    private double windowTopOrigin;
    private bool interactionEnabled;
    private bool dragging;

    public DesktopObjectOverlayWindow(
        PrototypeDesktopObject objectKind,
        Action<double, double>? positionChanged = null)
    {
        this.objectKind = objectKind;
        this.positionChanged = positionChanged;
        Width = objectKind == PrototypeDesktopObject.Dango ? 150 : 150;
        Height = objectKind == PrototypeDesktopObject.Dango ? 135 : 58;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Focusable = objectKind == PrototypeDesktopObject.Dango;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        Content = objectKind == PrototypeDesktopObject.Dango ? CreateDango() : CreateCushion();
        SourceInitialized += OnSourceInitialized;
    }

    public void SetInteractionEnabled(bool isEnabled)
    {
        interactionEnabled = objectKind == PrototypeDesktopObject.Dango && isEnabled;
    }

    public void SetPosition(double left, double top)
    {
        Left = left;
        Top = top;
    }

    private Border CreateDango()
    {
        var face = new TextBlock
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            FontSize = 25,
            Text = "●  ●",
        };
        var label = new TextBlock
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 13,
            Text = "占位团子",
        };
        var stack = new StackPanel
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
        };
        stack.Children.Add(face);
        stack.Children.Add(label);
        var border = CreateObjectBorder(72, WpfColor.FromRgb(0xF2, 0xC9, 0x4C));
        border.Cursor = WpfCursors.Hand;
        border.Child = stack;
        return border;
    }

    private static Border CreateCushion()
    {
        var border = CreateObjectBorder(24, WpfColor.FromRgb(0xB7, 0xD8, 0xF7));
        border.Child = new TextBlock
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x31, 0x58, 0x78)),
            FontSize = 13,
            Text = "占位软垫",
        };
        return border;
    }

    private static Border CreateObjectBorder(double cornerRadius, WpfColor background) =>
        new()
        {
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x6E, 0x5A, 0x3C)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(cornerRadius),
        };

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        windowHandle = source.Handle;
        NativeMethods.ApplyToolWindowStyle(source.Handle);
        source.AddHook(WindowMessageHook);
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            handled = true;
            return interactionEnabled ? HtClient : HtTransparent;
        }

        if (message == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }

        if (message == WmLeftButtonDown && interactionEnabled)
        {
            if (NativeMethods.GetCursorPos(out pointerOrigin))
            {
                dragging = true;
                windowLeftOrigin = Left;
                windowTopOrigin = Top;
                _ = NativeMethods.SetCapture(windowHandle);
            }

            handled = true;
            return nint.Zero;
        }

        if (message == WmMouseMove && dragging && NativeMethods.GetCursorPos(out var pointer))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = windowLeftOrigin + (pointer.X - pointerOrigin.X) / dpi.DpiScaleX;
            Top = windowTopOrigin + (pointer.Y - pointerOrigin.Y) / dpi.DpiScaleY;
            positionChanged?.Invoke(Left, Top);
            handled = true;
            return nint.Zero;
        }

        if (message == WmLeftButtonUp && dragging)
        {
            dragging = false;
            _ = NativeMethods.ReleaseCapture();
            handled = true;
            return nint.Zero;
        }

        if (message == WmCaptureChanged && dragging)
        {
            dragging = false;
        }

        return nint.Zero;
    }
}
