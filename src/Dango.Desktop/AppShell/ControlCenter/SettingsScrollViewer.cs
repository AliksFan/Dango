using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using ScrollEventHandler = System.Windows.Controls.Primitives.ScrollEventHandler;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Settings-only wheel interpolation; thumb and keyboard input remain direct.</summary>
public sealed class SettingsScrollViewer : ScrollViewer
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch elapsed = new();
    private double start;
    private double target;
    private int direction;
    private bool reduceMotion;
    public bool ReduceMotion
    {
        get => reduceMotion;
        set { reduceMotion = value; if (value && timer.IsEnabled) { Stop(); ScrollToVerticalOffset(target); } }
    }
    internal void ScrollFromChild(MouseWheelEventArgs e) => OnMouseWheel(e);

    public SettingsScrollViewer()
    {
        timer.Tick += (_, _) => Advance();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); };
        AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((_, _) => Stop()));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        var nextDirection = Math.Sign(e.Delta);
        var origin = timer.IsEnabled && nextDirection == direction ? target : VerticalOffset;
        var step = SystemParameters.WheelScrollLines < 0 ? ViewportHeight : SystemParameters.WheelScrollLines * 16d;
        target = Math.Clamp(origin - e.Delta / 120d * step, 0, ScrollableHeight);
        if (Math.Abs(target - VerticalOffset) < 0.1) return;
        e.Handled = true;
        direction = nextDirection;
        start = VerticalOffset;
        if (ReduceMotion || !SystemParameters.ClientAreaAnimation) { Stop(); ScrollToVerticalOffset(target); return; }
        elapsed.Restart();
        timer.Start();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) { Stop(); base.OnPreviewMouseDown(e); }
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e) { Stop(); base.OnPreviewKeyDown(e); }

    private void Advance()
    {
        if (ReduceMotion || !SystemParameters.ClientAreaAnimation) { Stop(); ScrollToVerticalOffset(target); return; }
        var t = Math.Clamp(elapsed.Elapsed.TotalMilliseconds / 140, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        ScrollToVerticalOffset(Math.Clamp(start + (target - start) * eased, 0, ScrollableHeight));
        if (t >= 1) Stop();
    }

    private void Stop() { timer.Stop(); elapsed.Stop(); direction = 0; }
}
