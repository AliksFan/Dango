using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Dango.ActionLab;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var module = Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll");
        if (args.Length == 2 && args[0] == "--verify") return ActionLabVerification.Run(module, args[1]);
        if (args.Length is 2 or 3 && args[0] == "--verify-shake") return ShakeVerification.Run(module, args[1], args.Length == 3 ? args[2] : null);
        if (args.Length != 0) return 2;
        var app = new Application();
        using var host = new ActionLabHost(module);
        var window = new Window
        {
            Title = "Dango · 动作实验 · 中段抖身候选", Width = 1080, Height = 760, MinWidth = 940, MinHeight = 700,
            Content = host, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var watch = Stopwatch.StartNew();
        var previous = watch.Elapsed.TotalSeconds;
        var accumulated = 0d;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            var now = watch.Elapsed.TotalSeconds;
            var delta = now - previous; previous = now;
            if (window.WindowState == WindowState.Minimized) { accumulated = 0; return; }
            // Keep time tied to executed fixed steps. Long UI stalls are dropped,
            // never replayed as a burst of hidden actions.
            accumulated = Math.Min(accumulated + delta, .05);
            while (accumulated >= ActionLabSession.FixedStep.TotalSeconds)
            { host.Panel?.Step(); accumulated -= ActionLabSession.FixedStep.TotalSeconds; }
        };
        window.StateChanged += (_, _) =>
        {
            host.Panel?.Session.SetPaused(window.WindowState == WindowState.Minimized);
            accumulated = 0; previous = watch.Elapsed.TotalSeconds; host.Panel?.Render();
        };
        window.Loaded += (_, _) => { previous = watch.Elapsed.TotalSeconds; timer.Start(); };
        window.Closed += (_, _) => timer.Stop();
        return app.Run(window);
    }
}
