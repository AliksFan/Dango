using System.IO;
using System.Windows;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Tray;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell;

internal sealed class DangoApplicationHost : IDisposable
{
    private readonly DesktopSceneSession sceneSession;
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
    private bool disposed;

    public DangoApplicationHost()
    {
        sceneSession = new DesktopSceneSession();
        SingleDangoMotionRuntime CreateRuntime(IPhysicsPresentationSink sink) => new(
            () => Box2DPhysicsPresentationAdapter.CreateTuning(
                Box2DNativeLoader.DefaultLibraryPath,
                new SoftBodyTuningParameters()),
            sink);
        workerWWindow = new WorkerWWindow(sceneSession, CreateRuntime);
        sceneWindow = new DangoSceneWindow(sceneSession, CreateRuntime);
        desktopHost = new DesktopHostCoordinator(workerWWindow, sceneWindow);
        commandController = new ApplicationCommandController(desktopHost);
        verificationDefinition = VerificationDefinition.CreateP1006WindowModes();
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        settingsPath = Path.Combine(localData, "Dango", "settings.json");
        settingsStore = new ControlCenterWindowSettingsStore();
        var loadResult = settingsStore.LoadOrDefault(settingsPath);
        initialSettings = loadResult.Settings;
        settingsWarning = loadResult.Warning;
        sceneWindow.RestorePlacement(loadResult.Settings.SceneWindow);
        desktopHost.ApplyLayeringSettings(loadResult.Settings.DesktopLayeringExperiment);
        desktopHost.ApplyKeyboardControlSettings(loadResult.Settings.KeyboardControl);
        if (loadResult.Settings.DisplayMode == DesktopDisplayMode.Window)
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
    }

    private ControlCenterWindow GetOrCreateControlCenter()
    {
        if (controlCenterWindow is not null)
        {
            return controlCenterWindow;
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var draftPath = Path.Combine(localData, "Dango", "verification", "p1-006-draft.json");
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
                    sceneWindow.CapturePlacement()));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to save application settings: {exception.GetType().Name}");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
