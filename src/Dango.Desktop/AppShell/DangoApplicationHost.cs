using System.IO;
using System.Windows;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Tray;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell;

internal sealed class DangoApplicationHost : IDisposable
{
    private readonly DesktopSceneSession sceneSession;
    private readonly StackedDangoSceneRuntime stackedRuntime;
    private readonly WorkerWWindow workerWWindow;
    private readonly DangoSceneWindow sceneWindow;
    private readonly DesktopHostCoordinator desktopHost;
    private readonly ApplicationCommandController commandController;
    private readonly VerificationDefinition verificationDefinition;
    private readonly ControlCenterWindowSettingsStore settingsStore;
    private readonly string settingsPath;
    private readonly string? settingsWarning;
    private readonly ControlCenterWindowSettings initialSettings;
    private ControlCenterWindow? controlCenterWindow;
    private DangoTrayIcon? trayIcon;
    private AttachmentReviewPanel? attachmentPanel;
    private bool disposed;

    public DangoApplicationHost(bool isolatedPhysicsReview = false, bool isolatedEyeHatReview = false)
    {
        sceneSession = new DesktopSceneSession();
        stackedRuntime = isolatedEyeHatReview ? StackedDangoSceneRuntime.CreateEyeHatReview() : new StackedDangoSceneRuntime();
        workerWWindow = new WorkerWWindow(sceneSession, stackedRuntime);
        sceneWindow = new DangoSceneWindow(sceneSession, stackedRuntime);
        desktopHost = new DesktopHostCoordinator(workerWWindow, sceneWindow);
        commandController = new ApplicationCommandController(desktopHost);
        verificationDefinition = isolatedEyeHatReview ? VerificationDefinition.CreateEyeHatHosts() : VerificationDefinition.CreatePhysicsModuleHosts();
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        settingsPath = isolatedEyeHatReview
            ? Path.Combine(AppContext.BaseDirectory, "eye-hat-review-state", "settings.json")
            : isolatedPhysicsReview
            ? Path.Combine(AppContext.BaseDirectory, "physics-review-state", "settings.json")
            : Path.Combine(localData, "Dango", "settings.json");
        var firstReviewRun = (isolatedPhysicsReview || isolatedEyeHatReview) && !File.Exists(settingsPath);
        settingsStore = new ControlCenterWindowSettingsStore();
        var loadResult = settingsStore.LoadOrDefault(settingsPath);
        initialSettings = loadResult.Settings;
        settingsWarning = loadResult.Warning;
        sceneWindow.RestorePlacement(loadResult.Settings.SceneWindow);
        desktopHost.ApplyLayeringSettings(loadResult.Settings.DesktopLayeringExperiment);
        desktopHost.ApplyKeyboardControlSettings(firstReviewRun
            ? new KeyboardControlSettings(true, KeyboardControlScheme.Wasd)
            : loadResult.Settings.KeyboardControl);
        desktopHost.SetDebugSkeletonVisible(loadResult.Settings.ShowDebugSkeleton);
        if (firstReviewRun || loadResult.Settings.DisplayMode == DesktopDisplayMode.Window)
        {
            desktopHost.SetMode(DesktopDisplayMode.Window);
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        desktopHost.SetVisibility(true);
        trayIcon = new DangoTrayIcon(
            commandController,
            ShowControlCenter,
            ShowDiagnostics,
            RequestExit);
    }

    public void ShowControlCenter()
    {
        ThrowIfDisposed();
        var window = GetOrCreateControlCenter();
        window.ShowOverview();
        ShowAndActivate(window);
        if (stackedRuntime.EyeHatReviewEnabled)
        {
            attachmentPanel ??= new AttachmentReviewPanel(stackedRuntime);
            attachmentPanel.Show();
        }
    }

    public void ShowDiagnostics()
    {
        ThrowIfDisposed();
        var window = GetOrCreateControlCenter();
        window.ShowDiagnostics();
        ShowAndActivate(window);
    }

    public void RequestExit()
    {
        if (disposed)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            "退出后桌面团子、控制中心和托盘都会关闭。是否退出 Dango？",
            "退出 Dango",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        trayIcon?.Dispose();
        trayIcon = null;
        if (attachmentPanel is not null) { attachmentPanel.AllowClose = true; attachmentPanel.Close(); attachmentPanel = null; }
        if (controlCenterWindow is null)
        {
            SaveSettingsWithoutControlCenter();
        }

        if (controlCenterWindow is not null)
        {
            controlCenterWindow.PrepareForExit();
            controlCenterWindow.Close();
            controlCenterWindow = null;
        }

        sceneSession.Exit(Environment.TickCount64);
        sceneWindow.PrepareForExit();
        sceneWindow.Close();
        workerWWindow.Close();
        stackedRuntime.Dispose();
    }

    private ControlCenterWindow GetOrCreateControlCenter()
    {
        if (controlCenterWindow is not null)
        {
            return controlCenterWindow;
        }

        var draftPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "verification", verificationDefinition.Id + "-draft.json");
        controlCenterWindow = new ControlCenterWindow(
            commandController,
            verificationDefinition,
            new VerificationSessionStore(verificationDefinition),
            draftPath,
            RequestExit,
            settingsStore,
            settingsPath,
            sceneWindow.CapturePlacement,
            settingsWarning);
        return controlCenterWindow;
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void SaveSettingsWithoutControlCenter()
    {
        try
        {
            settingsStore.Save(
                settingsPath,
                new ControlCenterWindowSettings(
                    initialSettings.Width,
                    initialSettings.Height,
                    ControlCenterWindowSettings.CurrentSchemaVersion,
                    desktopHost.GetLayeringSettings(),
                    desktopHost.GetKeyboardControlSettings(),
                    Enum.TryParse<DesktopDisplayMode>(desktopHost.GetSnapshot().Mode, out var mode)
                        ? mode
                        : DesktopDisplayMode.Wallpaper,
                    sceneWindow.CapturePlacement(),
                    desktopHost.IsDebugSkeletonVisible));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to save application settings: {exception.GetType().Name}");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
