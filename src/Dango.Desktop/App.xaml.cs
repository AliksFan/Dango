using System.Windows;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = @"Local\Dango.Phase1.App";
    private SingleInstanceCoordinator? singleInstanceCoordinator;
    private DangoApplicationHost? applicationHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var homeUiReview = e.Args.Contains("--home-ui-review", StringComparer.Ordinal);
        var settingsUiReview = e.Args.Contains("--settings-ui-review", StringComparer.Ordinal);
        if (homeUiReview || settingsUiReview)
        {
            // Exercise the same product control without native physics, tray, user settings or network activity.
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var home = new ProductHomeView();
            home.SettingsButton.ToolTip = "设置界面预览 · 未连接桌面";
            if (settingsUiReview) home.ShowSettingsPage();
            MainWindow = new Window
            {
                Title = settingsUiReview ? "Dango · 设置页视觉预览" : "Dango · 首页交互验收",
                Width = 1024,
                Height = 640,
                MinWidth = 336,
                MinHeight = 560,
                Content = home,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            MainWindow.Show();
            return;
        }

        if (e.Args.Contains("--box2d-native-probe", StringComparer.Ordinal))
        {
            Shutdown(RunBox2DNativeProbe());
            return;
        }

        if (e.Args.Contains("--soft-body-preview", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new SoftBodyPreviewWindow();
            MainWindow.Show();
            return;
        }

        if (e.Args.Contains("--paired-contact-preview", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new PairedContactPreviewWindow();
            MainWindow.Show();
            return;
        }

        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(Shutdown), DispatcherPriority.ApplicationIdle);
            return;
        }

        var physicsReview = e.Args.Contains("--physics-host-review", StringComparer.Ordinal);
        var eyeHatReview = e.Args.Contains("--eye-hat-review", StringComparer.Ordinal);
        var unifiedHomeReview = e.Args.Contains("--unified-live-home-review", StringComparer.Ordinal);
        if ((physicsReview ? 1 : 0) + (eyeHatReview ? 1 : 0) + (unifiedHomeReview ? 1 : 0) > 1)
            throw new ArgumentException("Choose one isolated review mode.");
        var instanceName = eyeHatReview ? SingleInstanceName + ".EyeHatReview"
            : physicsReview ? SingleInstanceName + ".PhysicsHostReview"
            : unifiedHomeReview ? SingleInstanceName + ".UnifiedHomeReview" : SingleInstanceName;
        singleInstanceCoordinator = SingleInstanceCoordinator.TryBecomePrimary(
            instanceName,
            () => Dispatcher.BeginInvoke(
                new Action(() => applicationHost?.ShowControlCenter()),
                DispatcherPriority.Normal));
        if (singleInstanceCoordinator is null)
        {
            var signaled = SingleInstanceCoordinator.TrySignalPrimary(instanceName);
            Shutdown(signaled ? 0 : 2);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        applicationHost = new DangoApplicationHost(physicsReview, eyeHatReview,
            isolatedUnifiedHomeReview: unifiedHomeReview);
        applicationHost.Start();
        if (physicsReview || eyeHatReview) applicationHost.ShowControlCenter();
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
