using System.IO;
using System.Windows;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Tray;
using Dango.Desktop.DesktopHost.Overlay;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell;

internal sealed class DangoApplicationHost : IDisposable
{
    private readonly OverlayWindow overlayWindow;
    private readonly ApplicationCommandController commandController;
    private readonly VerificationDefinition verificationDefinition;
    private ControlCenterWindow? controlCenterWindow;
    private DangoTrayIcon? trayIcon;
    private bool disposed;

    public DangoApplicationHost()
    {
        overlayWindow = new OverlayWindow();
        commandController = new ApplicationCommandController(overlayWindow);
        verificationDefinition = VerificationDefinition.CreateP1002Overlay();
    }

    public void Start()
    {
        ThrowIfDisposed();
        overlayWindow.Show();
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
        if (controlCenterWindow is not null)
        {
            controlCenterWindow.PrepareForExit();
            controlCenterWindow.Close();
            controlCenterWindow = null;
        }

        overlayWindow.Close();
    }

    private ControlCenterWindow GetOrCreateControlCenter()
    {
        if (controlCenterWindow is not null)
        {
            return controlCenterWindow;
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var draftPath = Path.Combine(localData, "Dango", "verification", "p1-002-draft.json");
        var settingsPath = Path.Combine(localData, "Dango", "settings.json");
        controlCenterWindow = new ControlCenterWindow(
            commandController,
            verificationDefinition,
            new VerificationSessionStore(verificationDefinition),
            draftPath,
            RequestExit,
            new ControlCenterWindowSettingsStore(),
            settingsPath);
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
