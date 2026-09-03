using System.Windows;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.Physics.Native;

namespace Dango.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\Dango.Phase1.App";
    private SingleInstanceCoordinator? singleInstanceCoordinator;
    private DangoApplicationHost? applicationHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--box2d-native-probe", StringComparer.Ordinal))
        {
            Shutdown(RunBox2DNativeProbe());
            return;
        }

        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(Shutdown), DispatcherPriority.ApplicationIdle);
            return;
        }

        singleInstanceCoordinator = SingleInstanceCoordinator.TryBecomePrimary(
            SingleInstanceName,
            () => Dispatcher.BeginInvoke(
                new Action(() => applicationHost?.ShowControlCenter()),
                DispatcherPriority.Normal));
        if (singleInstanceCoordinator is null)
        {
            var signaled = SingleInstanceCoordinator.TrySignalPrimary(SingleInstanceName);
            Shutdown(signaled ? 0 : 2);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        applicationHost = new DangoApplicationHost();
        applicationHost.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        applicationHost?.Dispose();
        applicationHost = null;
        singleInstanceCoordinator?.Dispose();
        singleInstanceCoordinator = null;
        base.OnExit(e);
    }

    private static int RunBox2DNativeProbe()
    {
        if (!Box2DNativeLoader.TryLoad(
            Box2DNativeLoader.DefaultLibraryPath,
            Box2DNativeLoader.PinnedRequirements,
            out var session,
            out _))
        {
            return 20;
        }

        var loadedSession = session!;
        using (loadedSession)
        {
            return loadedSession.RunLifecycleProbe(8).Succeeded ? 0 : 21;
        }
    }
}
