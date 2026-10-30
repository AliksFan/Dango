using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Tray;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.AppShell;

internal sealed partial class DangoApplicationHost : IDisposable
{
    private readonly DesktopSceneSession sceneSession;
    private readonly StackedDangoSceneRuntime stackedRuntime;
    private readonly ProductPhysicsComposition productPhysics;
    private readonly WorkerWWindow workerWWindow;
    private readonly DangoSceneWindow sceneWindow;
    private readonly DesktopHostCoordinator desktopHost;
    private readonly ApplicationCommandController commandController;
    private readonly VerificationDefinition verificationDefinition;
    private readonly ApplicationSettingsSession settings;
    private readonly string settingsPath;
    private AppearanceSettingsSession appearance;
    private AccessorySettingsSession accessories;
    private readonly bool eyeHatReview;
    private readonly ApplicationUiMode uiMode;
    private readonly DispatcherTimer geometrySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private ControlCenterWindow? controlCenterWindow;
    private DangoTrayIcon? trayIcon;
    private AttachmentReviewPanel? attachmentPanel;
    private bool started;
    private bool exitSettingsHandled;
    private bool saveFailureNotified;
    private bool disposed;

    public DangoApplicationHost(bool isolatedPhysicsReview = false, bool isolatedEyeHatReview = false,
        string? settingsFile = null, string? nativeLibraryPath = null, ControlCenterWindowSettingsStore? settingsStore = null,
        string? roomModulePath = null)
    {
        if (isolatedPhysicsReview && isolatedEyeHatReview)
            throw new ArgumentException("Choose one isolated review mode.");
        uiMode = isolatedPhysicsReview || isolatedEyeHatReview ? ApplicationUiMode.DeveloperReview : ApplicationUiMode.Product;
        if (uiMode == ApplicationUiMode.Product && nativeLibraryPath is not null)
            throw new ArgumentException("A legacy native library override requires an explicit diagnostic host.", nameof(nativeLibraryPath));
        if (roomModulePath is not null && (uiMode != ApplicationUiMode.Product || !Path.IsPathFullyQualified(roomModulePath)))
            throw new ArgumentException("A room module override requires the product runtime and an absolute path.", nameof(roomModulePath));
        eyeHatReview = isolatedEyeHatReview;
        productPhysics = ProductPhysicsComposition.FromModules(roomModulePath);
        sceneSession = new DesktopSceneSession();
        stackedRuntime = isolatedEyeHatReview ? StackedDangoSceneRuntime.CreateEyeHatReview(nativeLibraryPath, deferInitialization: true)
            : isolatedPhysicsReview ? new StackedDangoSceneRuntime(nativeLibraryPath)
            : productPhysics.CreateRoom();
        workerWWindow = new WorkerWWindow(sceneSession, stackedRuntime);
        sceneWindow = new DangoSceneWindow(sceneSession, stackedRuntime);
        desktopHost = new DesktopHostCoordinator(workerWWindow, sceneWindow, stackedRuntime);
        if (uiMode == ApplicationUiMode.Product) desktopHost.SetRoomPresentationAllowed(false);
        commandController = new ApplicationCommandController(desktopHost, uiMode);
        verificationDefinition = isolatedEyeHatReview ? VerificationDefinition.CreateEyeHatHosts() : VerificationDefinition.CreatePhysicsModuleHosts();
        settingsPath = settingsFile ?? (isolatedEyeHatReview
            ? Path.Combine(AppContext.BaseDirectory, "eye-hat-review-state", "settings.json")
            : isolatedPhysicsReview
                ? Path.Combine(AppContext.BaseDirectory, "physics-review-state", "settings.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango", "settings.json"));
        var firstReviewRun = uiMode == ApplicationUiMode.DeveloperReview && !File.Exists(settingsPath);
        settings = new ApplicationSettingsSession(settingsPath, settingsStore);
        appearance = new AppearanceSettingsSession(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "appearance.json"));
        stackedRuntime.SetAppearance(appearance.SavedAppearance);
        var initial = settings.Current;
        sceneWindow.RestorePlacement(initial.SceneWindow);
        desktopHost.ApplyLayeringSettings(initial.DesktopLayeringExperiment);
        desktopHost.ApplyKeyboardControlSettings(firstReviewRun
            ? new KeyboardControlSettings(true, KeyboardControlScheme.Wasd) : initial.KeyboardControl);
        desktopHost.SetDebugSkeletonVisible(uiMode == ApplicationUiMode.DeveloperReview && initial.ShowDebugSkeleton);
        desktopHost.SetInteractionEnabled(initial.InteractionEnabled);
        stackedRuntime.TryApplyHatPreset(firstReviewRun && eyeHatReview ? "top-hat" : initial.HatPresetId);
        accessories = new AccessorySettingsSession(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, initial.HatPresetId);
        if (uiMode == ApplicationUiMode.Product) InitializeDangos();
        if (uiMode == ApplicationUiMode.Product) stackedRuntime.BindAccessories(accessories);
        if (firstReviewRun || initial.DisplayMode == DesktopDisplayMode.Window) desktopHost.SetMode(DesktopDisplayMode.Window);
        geometrySaveTimer.Tick += (_, _) => { geometrySaveTimer.Stop(); SaveSettings(); };
        sceneWindow.SizeChanged += (_, _) => ScheduleSave();
        sceneWindow.LocationChanged += (_, _) => ScheduleSave();
        // A control-center command saves immediately; an external hat change still saves after the debounce.
        stackedRuntime.HatPresetChanged += ScheduleSave;
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (started) return;
        started = true;
        // Establish recovery entry points before touching the native module.
        trayIcon = new DangoTrayIcon(commandController, ShowControlCenter, ShowDiagnostics, RequestExit,
            uiMode == ApplicationUiMode.Product ? CreateLocalRoomTrayActions() : null);
        var initialized = uiMode == ApplicationUiMode.DeveloperReview && stackedRuntime.TryInitialize();
        if (uiMode == ApplicationUiMode.Product)
        {
            ShowControlCenter();
            return;
        }
        var shown = initialized && commandController.SetVisibility(true).State == CommandApplicationState.Applied;
        if (!shown) ShowControlCenter();
    }

    public void ShowControlCenter()
    {
        ThrowIfDisposed();
        var window = GetOrCreateControlCenter();
        if (roomHandoff.Pending && roomHandoff.Target == Rooms.LocalRoomPresentation.Porch)
        {
            ShowAndActivate(window);
            return;
        }
        if (localRoomSelected || roomHandoff.Pending)
        {
            roomControlCenterRequested = true;
            window.ProductHome.SetDangoAway(true);
        }
        window.ShowOverview();
        ShowAndActivate(window);
        if (eyeHatReview && stackedRuntime.AttachmentsEnabled)
        {
            attachmentPanel ??= new AttachmentReviewPanel(stackedRuntime);
            attachmentPanel.Show();
        }
    }

    public void ShowDiagnostics()
    {
        ThrowIfDisposed();
        var window = GetOrCreateControlCenter();
        if (roomHandoff.Pending) HideLocalRoom();
        if (localRoomSelected)
        {
            roomControlCenterRequested = true;
            window.ProductHome.SetDangoAway(true);
        }
        window.ShowDiagnostics();
        ShowAndActivate(window);
    }

    public void RequestExit()
    {
        if (disposed) return;
        if (System.Windows.MessageBox.Show("退出后桌面团子、控制中心和托盘都会关闭。是否退出 Dango？",
            "退出 Dango", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        geometrySaveTimer.Stop();
        while (!SaveSettings())
        {
            var answer = System.Windows.MessageBox.Show(settings.StatusMessage + "\n\n是：重试保存；否：不保存退出；取消：继续使用。",
                "设置未保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.No) break;
        }
        exitSettingsHandled = true;
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (disposed) return;
        geometrySaveTimer.Stop();
        if (!exitSettingsHandled) SaveSettings();
        controlCenterWindow?.ProductHome.CancelAppearancePreview();
        controlCenterWindow?.ProductHome.UnbindBeltBody();
        disposed = true;
        DisposeLocalRoom();
        trayIcon?.Dispose();
        trayIcon = null;
        if (attachmentPanel is not null) { attachmentPanel.AllowClose = true; attachmentPanel.Close(); attachmentPanel = null; }
        if (controlCenterWindow is not null)
        {
            controlCenterWindow.ProductHome.UnbindLiveScene();
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
        if (controlCenterWindow is not null) return controlCenterWindow;
        var draftPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "verification", verificationDefinition.Id + "-draft.json");
        controlCenterWindow = new ControlCenterWindow(commandController, verificationDefinition,
            new VerificationSessionStore(verificationDefinition), draftPath, RequestExit,
            settings, () => SaveSettings(), ScheduleSave);
        if (uiMode == ApplicationUiMode.Product)
        {
            controlCenterWindow.ProductHome.BindAppearance(appearance);
            controlCenterWindow.ProductHome.BindAccessories(accessories);
            controlCenterWindow.ProductHome.BindBeltBody(settingsPath, productPhysics);
            BindLocalRoom(controlCenterWindow);
            BindDangoHome(controlCenterWindow);
            controlCenterWindow.BindSettingsScene(ReadProductSettingsScene, RestoreRoomDisplayFromSettings, ReinitializeSceneFromSettings);
            controlCenterWindow.BindDesktopPetDisplaySettings(() => desktopPetDisplaySettings!.Percent,
                ApplyDesktopPetDisplayPercent, () => desktopPetDisplaySettings!.StatusMessage);
        }
        controlCenterWindow.OverviewPresented += () =>
        {
            if (uiMode == ApplicationUiMode.Product && !settings.Current.IntroSeen)
            {
                settings.Stage(settings.Current with { IntroSeen = true });
                SaveSettings();
            }
        };
        return controlCenterWindow;
    }

    private bool SaveSettings()
    {
        if (disposed) return true;
        geometrySaveTimer.Stop();
        var current = settings.Current;
        // Product geometry is stored in the preset sidecar. Keep the main file's legacy
        // geometry readable by the previous app; never persist a DPI/work-area fit here.
        var size = uiMode == ApplicationUiMode.Product
            ? (current.Width, current.Height)
            : controlCenterWindow?.CaptureSize() ?? (current.Width, current.Height);
        var saved = settings.TrySave(current with
        {
            Width = size.Item1,
            Height = size.Item2,
            DesktopLayeringExperiment = desktopHost.GetLayeringSettings(),
            KeyboardControl = desktopHost.GetKeyboardControlSettings(),
            DisplayMode = Enum.TryParse<DesktopDisplayMode>(desktopHost.GetSnapshot().Mode, out var mode) ? mode : current.DisplayMode,
            SceneWindow = sceneWindow.CapturePlacement(),
            // Keep the compatibility value; Product always runs with skeleton off.
            ShowDebugSkeleton = uiMode == ApplicationUiMode.Product ? current.ShowDebugSkeleton : desktopHost.IsDebugSkeletonVisible,
            InteractionEnabled = desktopHost.GetSnapshot().IsInteractionEnabled,
            HatPresetId = stackedRuntime.CurrentHatPresetId,
        });
        if (!saved && !saveFailureNotified && controlCenterWindow?.IsVisible != true)
            trayIcon?.ShowSettingsWarning(settings.StatusMessage);
        saveFailureNotified = !saved;
        return saved;
    }

    private void ScheduleSave()
    {
        if (!started || disposed || exitSettingsHandled) return;
        geometrySaveTimer.Stop();
        geometrySaveTimer.Start();
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
