using System.Windows;
using System.Windows.Threading;
using Dango.Desktop.DesktopHost.Overlay;

namespace Dango.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\Dango.Phase1.Overlay";
    private SingleInstanceGuard? singleInstanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(Shutdown), DispatcherPriority.ApplicationIdle);
            return;
        }

        singleInstanceGuard = SingleInstanceGuard.TryAcquire(SingleInstanceName);
        if (singleInstanceGuard is null)
        {
            Shutdown(2);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow = new OverlayWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstanceGuard?.Dispose();
        singleInstanceGuard = null;
        base.OnExit(e);
    }
}
