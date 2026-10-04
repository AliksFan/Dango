using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Text.Json;
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
        ValidateIsolatedReviewSelection(e.Args);
        var acceptanceSettingsFile = ResolveLocalInstallAcceptanceSettingsFile(e.Args);

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
        var porchReview = e.Args.Contains("--porch-scene-review", StringComparer.Ordinal);
        if ((physicsReview ? 1 : 0) + (eyeHatReview ? 1 : 0) + (unifiedHomeReview ? 1 : 0) + (porchReview ? 1 : 0) > 1)
            throw new ArgumentException("Choose one isolated review mode.");
        var instanceName = eyeHatReview ? SingleInstanceName + ".EyeHatReview"
            : physicsReview ? SingleInstanceName + ".PhysicsHostReview"
            : porchReview ? SingleInstanceName + ".PorchSceneReview"
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
            settingsFile: acceptanceSettingsFile,
            isolatedUnifiedHomeReview: unifiedHomeReview,
            isolatedPorchReview: porchReview);
        applicationHost.Start();
        if (physicsReview || eyeHatReview) applicationHost.ShowControlCenter();
    }

    internal static string? ResolveLocalInstallAcceptanceSettingsFile(string[] args)
    {
        const string flag = "--local-install-acceptance-settings";
        if (!args.Contains(flag, StringComparer.Ordinal)) return null;
        if (args.Length != 2 || args[0] != flag || string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException("Local install acceptance requires only its explicit settings directory.");
        var root = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
        var dailyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango");
        static bool IsWithin(string candidate, string parent) => candidate.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!Path.IsPathFullyQualified(args[1]) || root.Length < 3 || root[1] != ':' ||
            Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) == root ||
            IsWithin(root, dailyRoot) || IsWithin(dailyRoot, root) ||
            IsWithin(root, AppContext.BaseDirectory) || IsWithin(AppContext.BaseDirectory, root))
            throw new InvalidDataException("Acceptance settings must be outside the daily settings and package directories.");
        for (var cursor = root; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Acceptance settings cannot use reparse points.");
        var ownerPath = Path.Combine(root, ".dango-local-install-acceptance.json");
        if ((File.GetAttributes(ownerPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Acceptance owner cannot be a reparse point.");
        using var owner = JsonDocument.Parse(File.ReadAllText(ownerPath));
        if (owner.RootElement.EnumerateObject().Count() != 3 ||
            owner.RootElement.GetProperty("application").GetString() != "Dango" ||
            owner.RootElement.GetProperty("acceptanceFormat").GetInt32() != 1 ||
            !owner.RootElement.GetProperty("fictional").GetBoolean())
            throw new InvalidDataException("The directory is not owned by a Dango local acceptance session.");
        var settingsPath = Path.Combine(root, "settings.json");
        var presetPath = ControlCenterWindowSettingsStore.GetPresetPath(settingsPath);
        // Enumerate entries rather than testing File.Exists: dangling links and
        // directory links at either filename must also fail before settings load.
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            if (entry.Equals(settingsPath, StringComparison.OrdinalIgnoreCase) ||
                entry.Equals(presetPath, StringComparison.OrdinalIgnoreCase))
                if ((File.GetAttributes(entry) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new InvalidDataException("Acceptance settings files cannot be directories or reparse points.");
        return settingsPath;
    }

    internal static void ValidateIsolatedReviewSelection(string[] args)
    {
        string[] flags = ["--home-ui-review", "--settings-ui-review", "--physics-host-review",
            "--eye-hat-review", "--unified-live-home-review", "--porch-scene-review"];
        if (flags.Count(flag => args.Contains(flag, StringComparer.Ordinal)) > 1)
            throw new ArgumentException("Choose one isolated review mode.");
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
