using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ControlCenterWindow
{
    private readonly DispatcherTimer controlFocusTimer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(75),
    };
    private int controlFocusAttempts;

    private void InitializeControlFocusRules()
    {
        if (commandController.UiMode != ApplicationUiMode.Product) return;
        controlFocusTimer.Tick += OnControlFocusTimerTick;
        Activated += OnControlFocusActivated;
        Deactivated += OnControlFocusDeactivated;
        StateChanged += OnControlFocusStateChanged;
        AddHandler(Keyboard.PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnControlPreviewGotKeyboardFocus), true);
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnControlCommandClick), true);
    }

    private bool CanRestoreControlFocus => IsLoaded && IsVisible && IsActive && IsEnabled &&
        WindowState != WindowState.Minimized && ProductHome.IsVisible && ProductHome.IsEnabled && !WindowSizeMenu.IsOpen;

    private void OnControlPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ProductHome.IsVisible || ProductHome.IsRoomDialogOpen || e.NewFocus is not DependencyObject target ||
            !ReferenceEquals(Window.GetWindow(target), this)) return;
        if (e.NewFocus is Button)
        {
            // One owner-wide rule covers chrome, page and dynamically created buttons.
            // Cancelling keyboard focus does not cancel their mouse Click action.
            e.Handled = true;
            ScheduleControlFocusRestore();
        }
    }

    private void OnControlCommandClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button || !ProductHome.IsVisible) return;
        if (CanRestoreControlFocus) ProductHome.TryRestoreControlFocus();
        ScheduleControlFocusRestore();
    }

    private void OnControlFocusActivated(object? sender, EventArgs e) => ScheduleControlFocusRestore();
    private void OnControlFocusDeactivated(object? sender, EventArgs e) => StopControlFocusRestore();
    private void OnControlFocusStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) StopControlFocusRestore();
        else ScheduleControlFocusRestore();
    }

    private void ScheduleControlFocusRestore()
    {
        if (commandController.UiMode != ApplicationUiMode.Product) return;
        controlFocusTimer.Stop();
        controlFocusAttempts = 0;
        controlFocusTimer.Start();
    }

    private void OnControlFocusTimerTick(object? sender, EventArgs e)
    {
        controlFocusTimer.Stop();
        // Recheck the exact window when the deferred callback runs. A different
        // window in the same process, a modal dialog or a minimized owner cannot qualify.
        if (!CanRestoreControlFocus) return;
        if (!ProductHome.TryRestoreControlFocus() &&
            !ProductHome.IsKeyboardFocusWithin && ++controlFocusAttempts < 3)
        {
            controlFocusTimer.Start();
        }
    }

    private void StopControlFocusRestore()
    {
        controlFocusTimer.Stop();
    }

    private void DisposeControlFocusRules()
    {
        StopControlFocusRestore();
        Activated -= OnControlFocusActivated;
        Deactivated -= OnControlFocusDeactivated;
        StateChanged -= OnControlFocusStateChanged;
        RemoveHandler(Keyboard.PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnControlPreviewGotKeyboardFocus));
        RemoveHandler(Button.ClickEvent, new RoutedEventHandler(OnControlCommandClick));
    }
}
