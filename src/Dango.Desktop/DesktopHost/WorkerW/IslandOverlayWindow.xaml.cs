using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Dango.Desktop.DesktopHost.Win32;

namespace Dango.Desktop.DesktopHost.WorkerW;

public partial class IslandOverlayWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private readonly DispatcherTimer fullscreenTimer;
    private nint windowHandle;
    private bool desiredVisible;

    public IslandOverlayWindow()
    {
        InitializeComponent();
        fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        fullscreenTimer.Tick += OnFullscreenTimerTick;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public void SetDesiredVisibility(bool isVisible)
    {
        desiredVisible = isVisible;
        if (!desiredVisible)
        {
            fullscreenTimer.Stop();
            Hide();
            return;
        }

        ApplyFullscreenState();
        fullscreenTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        windowHandle = source.Handle;
        NativeMethods.ApplyToolWindowStyle(windowHandle);
        source.AddHook(WindowMessageHook);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 104;
        Top = SystemParameters.WorkArea.Bottom - Height - 30;
    }

    private nint WindowMessageHook(
        nint sourceWindow,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            handled = true;
            return HtTransparent;
        }

        return nint.Zero;
    }

    private void OnFullscreenTimerTick(object? sender, EventArgs e) => ApplyFullscreenState();

    private void ApplyFullscreenState()
    {
        if (!desiredVisible || NativeMethods.IsForegroundFullscreen())
        {
            Hide();
        }
        else if (!IsVisible)
        {
            Show();
        }
    }
}
