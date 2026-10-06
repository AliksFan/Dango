using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Win32;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomWallpaperMenuWindow : Window
{
    private bool closing;
    private bool initialFocusSet;
    internal LocalRoomMenuView Menu { get; } = new();
    internal event Action<bool>? DismissRequested;

    internal LocalRoomWallpaperMenuWindow(LogicalRect screenBounds)
    {
        if (!double.IsFinite(screenBounds.X) || !double.IsFinite(screenBounds.Y) ||
            !double.IsFinite(screenBounds.Right) || !double.IsFinite(screenBounds.Bottom) ||
            screenBounds.Width <= 0 || screenBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenBounds));

        Title = "Dango · 房间菜单";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = screenBounds.Width;
        Height = screenBounds.Height;
        Content = Menu;
        SourceInitialized += (_, _) => NativeMethods.PositionLocalRoomMenu(new WindowInteropHelper(this).Handle, screenBounds);
        Activated += (_, _) => FocusMenu();
        Loaded += (_, _) => { if (IsActive) FocusMenu(); };
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => { if (!closing) DismissRequested?.Invoke(false); };
    }

    internal void BeginClosing() => closing = true;

    private void FocusMenu()
    {
        if (!closing && (!initialFocusSet || !Menu.IsKeyboardFocusWithin)) initialFocusSet = Menu.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        if (!closing && ShouldDismissEscape(ComponentDispatcher.CurrentKeyboardMessage,
            new WindowInteropHelper(this).Handle, e.IsRepeat)) DismissRequested?.Invoke(true);
    }

    internal static bool ShouldDismissEscape(MSG nativeMessage, nint menuHandle, bool wpfRepeat)
    {
        if (menuHandle != 0 && nativeMessage.hwnd == menuHandle &&
            (nativeMessage.message is 0x0100 or 0x0104) && nativeMessage.wParam == (nint)0x1B)
            return (nativeMessage.lParam.ToInt64() & (1L << 30)) == 0;
        return !wpfRepeat;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 asks the owner to perform the same guarded cleanup as deactivation.
        if (!closing)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() => { if (!closing) DismissRequested?.Invoke(false); }));
        }
        base.OnClosing(e);
    }
}
