using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;
using Forms = System.Windows.Forms;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ControlCenterWindow : Window
{
    private readonly ApplicationCommandController commandController;
    private readonly VerificationDefinition verificationDefinition;
    private readonly VerificationSessionStore verificationStore;
    private readonly string draftPath;
    private readonly Action requestExit;
    private readonly ApplicationSettingsSession? settingsSession;
    private readonly Action? saveSettings;
    private readonly List<ThemePropertyOverride> highContrastOverrides = [];
    private readonly DispatcherTimer hostStatusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer sizeMenuFocusTimer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(75),
    };
    private DependencyPropertyDescriptor? sizeMenuOpenDescriptor;
    private int sizeMenuFocusAttempts;
    private bool sizeMenuFocusExplicitlyRequested;
    private VerificationSession? verificationSession;
    private IReadOnlyList<VerificationItemViewModel> verificationItems = [];
    private bool allowClose;
    private bool refreshingHostState;
    private bool refreshingPhysicsParameters;
    private bool applyingWindowPreset;
    private ControlCenterWindowPreset selectedWindowPreset = ControlCenterWindowPreset.Landscape1080p;
    private string? currentMonitorName;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRoundCorners = 2;
    private readonly Dictionary<string, (Slider Slider, WpfTextBox TextBox)> physicsControls = [];
    public event Action? OverviewPresented;

    public ControlCenterWindow(
        ApplicationCommandController commandController,
        VerificationDefinition verificationDefinition,
        VerificationSessionStore verificationStore,
        string draftPath,
        Action requestExit,
        ApplicationSettingsSession? settingsSession = null,
        Action? saveSettings = null,
        Action? scheduleSave = null)
    {
        this.commandController = commandController ?? throw new ArgumentNullException(nameof(commandController));
        this.verificationDefinition = verificationDefinition ?? throw new ArgumentNullException(nameof(verificationDefinition));
        this.verificationStore = verificationStore ?? throw new ArgumentNullException(nameof(verificationStore));
        this.draftPath = draftPath ?? throw new ArgumentNullException(nameof(draftPath));
        this.requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        this.settingsSession = settingsSession;
        this.saveSettings = saveSettings;
        refreshingHostState = true;
        InitializeComponent();
        sizeMenuFocusTimer.Tick += (_, _) =>
        {
            sizeMenuFocusTimer.Stop();
            var restoreRequested = sizeMenuFocusExplicitlyRequested || IsOwnProcessInForeground();
            sizeMenuFocusExplicitlyRequested = false;
            if (!IsVisible || !restoreRequested || ProductHome.IsKeyboardFocusWithin) return;
            ProductHome.RestoreKeyboardFocus();
            if (!ProductHome.IsKeyboardFocusWithin && ++sizeMenuFocusAttempts < 3)
            {
                sizeMenuFocusExplicitlyRequested = restoreRequested;
                sizeMenuFocusTimer.Start();
            }
        };
        sizeMenuOpenDescriptor = DependencyPropertyDescriptor.FromProperty(
            System.Windows.Controls.ContextMenu.IsOpenProperty, typeof(System.Windows.Controls.ContextMenu));
        sizeMenuOpenDescriptor?.AddValueChanged(WindowSizeMenu, OnWindowSizeMenuOpenStateChanged);
        ProductHome.SettingsRequested += () => OnDesktopNavigationClick(this, new RoutedEventArgs());
        ProductHome.HomeRequested += ShowOverview;
        ProductHome.NoticeRequested += () =>
        {
            if (settingsSession?.HasWarning == true) ShowDiagnostics();
            else OnDesktopNavigationClick(this, new RoutedEventArgs());
        };
        ProductHome.SettingsActionRequested += OnProductSettingsAction;
        ProductHome.WindowPresetRequested += SelectWindowPreset;
        if (commandController.UiMode == ApplicationUiMode.DeveloperReview) BuildPhysicsTuningPanel();
        ApplyUiMode();
        var initialSettings = settingsSession?.Current ?? ControlCenterWindowSettings.CreateDefault();
        Width = initialSettings.Width;
        Height = initialSettings.Height;
        if (commandController.UiMode == ApplicationUiMode.Product)
        {
            selectedWindowPreset = initialSettings.SelectedPreset;
            ProductHome.SetWindowPreset(selectedWindowPreset);
            ApplyWindowPresetSize(fitWorkingArea: false);
            UpdateWindowSizeMenu();
        }
        refreshingHostState = false;
        SizeChanged += (_, _) => { if (IsLoaded) scheduleSave?.Invoke(); };
        ContentRendered += (_, _) => { if (OverviewPage.Visibility == Visibility.Visible) OverviewPresented?.Invoke(); };
        LocationChanged += (_, _) => OnProductWindowLocationChanged();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += OnClosed;
        hostStatusTimer.Tick += (_, _) => RefreshHostState();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) hostStatusTimer.Start();
            else hostStatusTimer.Stop();
        };
    }

    public void ShowOverview()
    {
        RefreshHostState();
        ShowPage(OverviewPage);
        if (IsLoaded && IsVisible)
            Dispatcher.BeginInvoke(() =>
            {
                if (IsLoaded && IsVisible && OverviewPage.Visibility == Visibility.Visible)
                    OverviewPresented?.Invoke();
            }, DispatcherPriority.ContextIdle);
    }

    public void ShowDiagnostics()
    {
        RefreshDiagnostics();
        ShowPage(DiagnosticsPage);
    }

    public void PrepareForExit() => allowClose = true;

    private void OnMinimizeWindowClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    public ControlCenterWindowPreset SelectedWindowPreset => selectedWindowPreset;

    private void OnWindowSizeButtonClick(object sender, RoutedEventArgs e)
    {
        UpdateWindowSizeMenu();
        WindowSizeMenu.PlacementTarget = WindowSizeButton;
        WindowSizeMenu.Placement = PlacementMode.Bottom;
        WindowSizeMenu.IsOpen = true;
    }

    private void OnLandscape1080pMenuItemClick(object sender, RoutedEventArgs e) =>
        SelectWindowPreset(ControlCenterWindowPreset.Landscape1080p);

    private void OnLandscape1440pMenuItemClick(object sender, RoutedEventArgs e) =>
        SelectWindowPreset(ControlCenterWindowPreset.Landscape1440p);

    private void OnPortraitMenuItemClick(object sender, RoutedEventArgs e) =>
        SelectWindowPreset(ControlCenterWindowPreset.Portrait);

    private void OnWindowSizeMenuClosed(object sender, RoutedEventArgs e) => ScheduleSizeMenuFocusRestore();

    private void OnWindowSizeMenuPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
            sizeMenuFocusExplicitlyRequested = true;
    }

    private void OnWindowSizeMenuOpenStateChanged(object? sender, EventArgs e)
    {
        if (!WindowSizeMenu.IsOpen) ScheduleSizeMenuFocusRestore();
    }

    private void ScheduleSizeMenuFocusRestore(bool explicitSelection = false)
    {
        sizeMenuFocusTimer.Stop();
        sizeMenuFocusAttempts = 0;
        sizeMenuFocusExplicitlyRequested |= explicitSelection;
        sizeMenuFocusTimer.Start();
    }

    private static bool IsOwnProcessInForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private void OnCloseWindowClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateWindowSizeMenu()
    {
        if (commandController.UiMode != ApplicationUiMode.Product) return;
        Landscape1080pMenuItem.IsChecked = selectedWindowPreset == ControlCenterWindowPreset.Landscape1080p;
        Landscape1440pMenuItem.IsChecked = selectedWindowPreset == ControlCenterWindowPreset.Landscape1440p;
        PortraitMenuItem.IsChecked = selectedWindowPreset == ControlCenterWindowPreset.Portrait;
    }

    private void SelectWindowPreset(ControlCenterWindowPreset preset)
    {
        if (commandController.UiMode != ApplicationUiMode.Product || !Enum.IsDefined(preset)) return;
        selectedWindowPreset = preset;
        settingsSession?.Stage(settingsSession.Current with
        {
            SelectedPreset = preset,
        });
        ProductHome.SetWindowPreset(preset);
        UpdateWindowSizeMenu();
        WindowSizeMenu.IsOpen = false;
        ScheduleSizeMenuFocusRestore(explicitSelection: true);
        ApplyWindowPresetSize(fitWorkingArea: new WindowInteropHelper(this).Handle != IntPtr.Zero);
        saveSettings?.Invoke();
    }

    private void ApplyWindowPresetSize(bool fitWorkingArea)
    {
        if (commandController.UiMode != ApplicationUiMode.Product || applyingWindowPreset) return;
        applyingWindowPreset = true;
        try
        {
            var referenceSize = ControlCenterWindowPresets.GetSize(selectedWindowPreset);
            var deviceToLogical = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                ?? Matrix.Identity;
            var logicalTarget = deviceToLogical.Transform(
                new Vector(referenceSize.Width, referenceSize.Height));
            var width = logicalTarget.X;
            var height = logicalTarget.Y;
            if (selectedWindowPreset == ControlCenterWindowPreset.Portrait)
            {
                width = Math.Max(width, ControlCenterWindowPresets.PortraitMinimumLogicalWidth);
                height = Math.Max(height, ControlCenterWindowPresets.PortraitMinimumLogicalHeight);
            }
            Rect? workArea = null;
            if (fitWorkingArea)
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    var screen = Forms.Screen.FromHandle(handle);
                    currentMonitorName = screen.DeviceName;
                    var area = screen.WorkingArea;
                    workArea = new Rect(
                        deviceToLogical.Transform(new System.Windows.Point(area.Left, area.Top)),
                        deviceToLogical.Transform(new System.Windows.Point(area.Right, area.Bottom)));
                    var logicalMargin = deviceToLogical.Transform(new Vector(12, 12));
                    width = Math.Min(width, Math.Max(1, workArea.Value.Width - logicalMargin.X));
                    height = Math.Min(height, Math.Max(1, workArea.Value.Height - logicalMargin.Y));
                    if (selectedWindowPreset == ControlCenterWindowPreset.Portrait)
                        height = Math.Min(height, Math.Max(1, workArea.Value.Height *
                            ControlCenterWindowPresets.PortraitMaximumWorkAreaHeightFraction));
                }
            }

            var previousWidth = ActualWidth > 0 ? ActualWidth : Width;
            var previousHeight = ActualHeight > 0 ? ActualHeight : Height;
            WindowState = WindowState.Normal;
            MinWidth = Math.Min(ControlCenterWindowSettingsValidator.MinimumWidth, width);
            MinHeight = Math.Min(ControlCenterWindowSettingsValidator.MinimumHeight, height);
            Width = width;
            Height = height;
            if (workArea is { } bounds)
            {
                var centerX = double.IsFinite(Left) ? Left + previousWidth / 2 : bounds.Left + bounds.Width / 2;
                var centerY = double.IsFinite(Top) ? Top + previousHeight / 2 : bounds.Top + bounds.Height / 2;
                Left = Math.Clamp(centerX - width / 2, bounds.Left, bounds.Right - width);
                Top = Math.Clamp(centerY - height / 2, bounds.Top, bounds.Bottom - height);
            }
        }
        finally
        {
            applyingWindowPreset = false;
        }
    }

    private void OnProductWindowLocationChanged()
    {
        if (commandController.UiMode != ApplicationUiMode.Product || applyingWindowPreset || !IsLoaded) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var monitorName = Forms.Screen.FromHandle(handle).DeviceName;
        if (string.Equals(monitorName, currentMonitorName, StringComparison.Ordinal)) return;
        Dispatcher.BeginInvoke(() => ApplyWindowPresetSize(fitWorkingArea: true), DispatcherPriority.Loaded);
    }

    private IntPtr OnProductWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmDpiChanged = 0x02E0;
        if (msg == WmDpiChanged)
            Dispatcher.BeginInvoke(() => ApplyWindowPresetSize(fitWorkingArea: true), DispatcherPriority.Loaded);
        return IntPtr.Zero;
    }

    private void RequestRoundedProductCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var preference = DwmRoundCorners;
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (commandController.UiMode == ApplicationUiMode.Product) ApplyWindowPresetSize(fitWorkingArea: true);
        if (commandController.UiMode == ApplicationUiMode.DeveloperReview) LoadOrCreateVerificationSession();
        RefreshHostState();
        if (!string.IsNullOrWhiteSpace(settingsSession?.StartupWarning))
        {
            OverviewCommandStatusText.Text = settingsSession.StartupWarning;
            DesktopCommandStatusText.Text = settingsSession.StartupWarning;
        }
        RefreshDiagnostics();
        ApplyAccessibilityTheme();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            return;
        }

        SaveWindowSettings();
        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        hostStatusTimer.Stop();
        sizeMenuFocusTimer.Stop();
        sizeMenuOpenDescriptor?.RemoveValueChanged(WindowSizeMenu, OnWindowSizeMenuOpenStateChanged);
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        RestoreThemeOverrides();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.WorkArea), StringComparison.Ordinal) &&
            commandController.UiMode == ApplicationUiMode.Product)
            Dispatcher.BeginInvoke(() => ApplyWindowPresetSize(fitWorkingArea: true), DispatcherPriority.Loaded);
        if (!string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.BeginInvoke(ApplyAccessibilityTheme, DispatcherPriority.Background);
    }

    private void OnOverviewNavigationClick(object sender, RoutedEventArgs e)
    {
        RefreshHostState();
        ShowPage(OverviewPage);
    }

    private void OnDesktopNavigationClick(object sender, RoutedEventArgs e)
    {
        RefreshHostState();
        ShowPage(DesktopPage);
    }

    private void OnVerificationNavigationClick(object sender, RoutedEventArgs e)
    {
        if (commandController.UiMode != ApplicationUiMode.DeveloperReview) return;
        RefreshVerificationSummary();
        ShowPage(VerificationPage);
    }

    private void OnDiagnosticsNavigationClick(object sender, RoutedEventArgs e) => ShowDiagnostics();

    private void OnOverviewVisibilityClick(object sender, RoutedEventArgs e)
    {
        var requested = !commandController.GetSnapshot().IsVisible;
        ApplyCommand(commandController.SetVisibility(requested));
    }

    private void OnVisibilityCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (refreshingHostState || VisibilityCheckBox.IsChecked is null)
        {
            return;
        }

        ApplyCommand(commandController.SetVisibility(VisibilityCheckBox.IsChecked.Value));
    }

    private void OnInteractionCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (refreshingHostState || InteractionCheckBox.IsChecked is null)
        {
            return;
        }

        ApplyCommand(commandController.SetInteractionEnabled(InteractionCheckBox.IsChecked.Value));
    }

    private void OnKeyboardControlChanged(object sender, RoutedEventArgs e)
    {
        if (refreshingHostState || KeyboardControlCheckBox.IsChecked is null)
        {
            return;
        }

        var settings = new KeyboardControlSettings(
            KeyboardControlCheckBox.IsChecked.Value,
            KeyboardControlScheme.Wasd);
        ApplyCommand(commandController.ApplyKeyboardControlSettings(settings));
    }

    private void OnDebugSkeletonChanged(object sender, RoutedEventArgs e)
    {
        if (refreshingHostState || DebugSkeletonCheckBox.IsChecked is null)
        {
            return;
        }

        ApplyCommand(commandController.SetDebugSkeletonVisible(DebugSkeletonCheckBox.IsChecked.Value));
    }

    private void OnAddDangoClick(object sender, RoutedEventArgs e) =>
        ApplyCommand(commandController.AddDango());

    private void OnRemoveDangoClick(object sender, RoutedEventArgs e) =>
        ApplyCommand(commandController.RemoveDango());

    private void OnRestoreDefaultPhysicsClick(object sender, RoutedEventArgs e)
    {
        ApplyCommand(commandController.RestoreDefaultPhysicsParameters());
        RefreshPhysicsParameters();
    }

    private void OnRegenerateDangoClick(object sender, RoutedEventArgs e) =>
        ApplyCommand(commandController.RegenerateDango());

    private void OnHostModeChanged(object sender, RoutedEventArgs e)
    {
        if (refreshingHostState)
        {
            return;
        }

        var mode = WindowHostRadioButton.IsChecked == true
            ? DesktopDisplayMode.Window
            : DesktopDisplayMode.Wallpaper;
        ApplyCommand(commandController.SetHostMode(mode));
    }

    private void OnRetryWallpaperClick(object sender, RoutedEventArgs e)
    {
        var result = string.Equals(commandController.GetSnapshot().Mode, "Wallpaper", StringComparison.Ordinal)
            ? commandController.SetVisibility(true)
            : commandController.SetHostMode(DesktopDisplayMode.Wallpaper);
        ApplyCommand(result);
    }

    private void OnObjectLayerChanged(object sender, RoutedEventArgs e)
    {
        if (refreshingHostState ||
            DangoBelowIconsRadioButton.IsChecked is null ||
            CushionBelowIconsRadioButton.IsChecked is null)
        {
            return;
        }

        var settings = new DesktopLayeringSettings(
            DangoAboveIconsRadioButton.IsChecked == true
                ? DesktopObjectLayer.AboveDesktopIcons
                : DesktopObjectLayer.BelowDesktopIcons,
            new Dictionary<string, DesktopObjectLayer>(StringComparer.Ordinal)
            {
                [DesktopLayeringSettings.PrototypeCushionId] =
                    CushionAboveIconsRadioButton.IsChecked == true
                        ? DesktopObjectLayer.AboveDesktopIcons
                        : DesktopObjectLayer.BelowDesktopIcons,
            });
        ApplyCommand(commandController.ApplyLayeringSettings(settings));
    }

    private void OnVerificationOutcomeChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshVerificationSummary();

    private void OnNewVerificationClick(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            "新建验收会清空当前界面中的未保存结果。是否继续？",
            "新建验收",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        BindVerificationSession(CreateVerificationSession());
        DraftStatusText.Text = "已新建验收，尚未保存本地草稿。";
    }

    private void OnSaveDraftClick(object sender, RoutedEventArgs e)
    {
        if (verificationSession is null)
        {
            return;
        }

        try
        {
            verificationStore.Save(draftPath, verificationSession);
            DraftStatusText.Text = "草稿已安全保存在本机 Dango 数据目录。";
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                SafeUserMessage(exception),
                "无法保存验收草稿",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnExportReportClick(object sender, RoutedEventArgs e)
    {
        if (verificationSession is null)
        {
            return;
        }

        try
        {
            var report = VerificationMarkdownExporter.Export(verificationDefinition, verificationSession);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".md",
                FileName = $"dango-{verificationDefinition.Id}-verification-{DateTime.Now:yyyyMMdd-HHmm}.md",
                Filter = "Markdown 文档 (*.md)|*.md",
                Title = "导出 Dango 人工验收报告",
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            new AtomicTextFileWriter().Write(dialog.FileName, report);
            System.Windows.MessageBox.Show(
                $"报告已导出到：\n{dialog.FileName}",
                "导出完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                SafeUserMessage(exception),
                "无法导出验收报告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnRefreshDiagnosticsClick(object sender, RoutedEventArgs e) => RefreshDiagnostics();

    private void OnExitClick(object sender, RoutedEventArgs e) => requestExit();

    private void OnProductSettingsAction(ProductSettingsAction action)
    {
        if (RoomSettingsActionHandler?.Invoke(action) == true) return;
        switch (action)
        {
            case ProductSettingsAction.ToggleVisibility:
                ApplyCommand(commandController.SetVisibility(!commandController.GetSnapshot().IsVisible));
                break;
            case ProductSettingsAction.ToggleInteraction:
                ApplyCommand(commandController.SetInteractionEnabled(!commandController.GetSnapshot().IsInteractionEnabled));
                break;
            case ProductSettingsAction.WallpaperMode:
            case ProductSettingsAction.WindowMode:
                ApplyCommand(commandController.SetHostMode(action == ProductSettingsAction.WallpaperMode
                    ? DesktopDisplayMode.Wallpaper : DesktopDisplayMode.Window));
                break;
            case ProductSettingsAction.BelowIcons:
            case ProductSettingsAction.AboveIcons:
                var currentLayering = commandController.GetLayeringSettings();
                ApplyCommand(commandController.ApplyLayeringSettings(currentLayering with
                {
                    DangoLayer = action == ProductSettingsAction.BelowIcons
                        ? DesktopObjectLayer.BelowDesktopIcons : DesktopObjectLayer.AboveDesktopIcons,
                }));
                break;
            case ProductSettingsAction.ToggleKeyboard:
                var currentKeyboard = commandController.GetKeyboardControlSettings();
                var updatedKeyboard = currentKeyboard with { IsEnabled = !currentKeyboard.IsEnabled };
                ApplyCommand(commandController.ApplyKeyboardControlSettings(updatedKeyboard));
                break;
            case ProductSettingsAction.RetryInitialization:
                OnRetrySceneClick(this, new RoutedEventArgs());
                break;
            case ProductSettingsAction.RestoreDango:
                OnRegenerateDangoClick(this, new RoutedEventArgs());
                break;
            case ProductSettingsAction.RetryWallpaper:
                OnRetryWallpaperClick(this, new RoutedEventArgs());
                break;
            case ProductSettingsAction.RetrySave:
                OnRetrySaveClick(this, new RoutedEventArgs());
                break;
            case ProductSettingsAction.RecoverSettings:
                OnRecoverSettingsClick(this, new RoutedEventArgs());
                break;
            case ProductSettingsAction.RefreshDiagnostics:
                RefreshDiagnostics();
                break;
            case ProductSettingsAction.Exit:
                requestExit();
                break;
        }
    }

    internal Func<ProductSettingsAction, bool>? RoomSettingsActionHandler { get; set; }

    private void OnHatPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingHostState && HatPresetComboBox.SelectedItem is ComboBoxItem { Tag: string id })
            ApplyCommand(commandController.ApplyHatPreset(id));
    }

    private void OnRetrySceneClick(object sender, RoutedEventArgs e) => ApplyCommand(commandController.RetryInitialization());

    private void OnRetrySaveClick(object sender, RoutedEventArgs e)
    {
        settingsSession?.TrySave();
        RefreshHostState();
    }

    private void OnRecoverSettingsClick(object sender, RoutedEventArgs e)
    {
        settingsSession?.TryRecoverAndSave();
        RefreshHostState();
    }

    private void ApplyCommand(HostCommandResult result)
    {
        var status = result.State == CommandApplicationState.Applied
            ? "操作已由桌面宿主确认。"
            : result.Error ?? "操作失败，宿主状态未确认。";
        OverviewCommandStatusText.Text = status;
        DesktopCommandStatusText.Text = status;
        if (ProductHome.IsSettingsOpen)
            ProductHome.ShowSettingsStatus(status, result.State != CommandApplicationState.Applied);
        if (result.State == CommandApplicationState.Applied) SaveWindowSettings();
        RefreshHostState();
    }

    private void RefreshHostState()
    {
        var snapshot = commandController.GetSnapshot();
        refreshingHostState = true;
        try
        {
            var ready = commandController.InitializationState == SceneInitializationState.Ready;
            RetrySceneButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            RestoreDangoButton.IsEnabled = ready;
            VisibilityCheckBox.IsEnabled = ready;
            OverviewVisibilityButton.IsEnabled = ready;
            WallpaperHostRadioButton.IsEnabled = ready;
            WindowHostRadioButton.IsEnabled = ready;
            RetryWallpaperButton.IsEnabled = ready;
            SettingsSaveStatusText.Text = settingsSession?.StatusMessage ?? "";
            ProductHome.SetNotice(settingsSession?.HasWarning == true ? "设置未保存 · 查看" : !ready ? "团子未启动 · 查看" : null,
                settingsSession?.HasWarning == true ? settingsSession.StatusMessage : snapshot.LastError);
            RetrySaveButton.IsEnabled = settingsSession is not null && !settingsSession.IsWriteProtected;
            RecoverSettingsButton.Visibility = settingsSession is { IsWriteProtected: true } or { IsPresetWriteProtected: true }
                ? Visibility.Visible : Visibility.Collapsed;
            HatPresetComboBox.SelectedItem = HatPresetComboBox.Items.OfType<ComboBoxItem>()
                .Single(item => (string)item.Tag == commandController.HatPresetId);
            VisibilityCheckBox.IsChecked = snapshot.IsVisible;
            InteractionCheckBox.IsChecked = snapshot.IsInteractionEnabled;
            var keyboard = commandController.GetKeyboardControlSettings();
            KeyboardControlCheckBox.IsChecked = keyboard.IsEnabled;
            DebugSkeletonCheckBox.IsChecked = commandController.IsDebugSkeletonVisible;
            var dangoCount = commandController.DangoCount;
            DangoCountText.Text = $"当前 {dangoCount} 只（最多 3 只）；拖动选择团子，移除作用于选中团子。";
            AddDangoButton.IsEnabled = snapshot.LastError is null && dangoCount < 3;
            RemoveDangoButton.IsEnabled = snapshot.LastError is null && dangoCount > 1;
            if (commandController.UiMode == ApplicationUiMode.DeveloperReview) RefreshPhysicsParameters();
            var layering = commandController.GetLayeringSettings();
            DangoBelowIconsRadioButton.IsChecked = layering.DangoLayer == DesktopObjectLayer.BelowDesktopIcons;
            DangoAboveIconsRadioButton.IsChecked = layering.DangoLayer == DesktopObjectLayer.AboveDesktopIcons;
            CushionBelowIconsRadioButton.IsChecked =
                layering.GetFurnitureLayer(DesktopLayeringSettings.PrototypeCushionId) ==
                DesktopObjectLayer.BelowDesktopIcons;
            CushionAboveIconsRadioButton.IsChecked =
                layering.GetFurnitureLayer(DesktopLayeringSettings.PrototypeCushionId) ==
                DesktopObjectLayer.AboveDesktopIcons;
            OverviewVisibilityText.Text = snapshot.IsVisible ? "已开启" : "已隐藏";
            OverviewVisibilityButton.Content = snapshot.IsVisible ? "隐藏桌面团子" : "显示桌面团子";
            OverviewInteractionText.Text = snapshot.IsInteractionEnabled ? "已开启" : "已关闭";
            WallpaperHostRadioButton.IsChecked = string.Equals(snapshot.Mode, "Wallpaper", StringComparison.Ordinal);
            WindowHostRadioButton.IsChecked = string.Equals(snapshot.Mode, "Window", StringComparison.Ordinal);
            DesktopLayeringPanel.IsEnabled = string.Equals(snapshot.Mode, "Wallpaper", StringComparison.Ordinal);
            var modeLabel = snapshot.Mode == "Wallpaper" ? "桌面模式" : snapshot.Mode == "Window" ? "窗口模式" : snapshot.Mode;
            OverviewHostStatusText.Text =
                $"{modeLabel} · {(snapshot.IsVisible ? "正在运行" : "已隐藏")} · {snapshot.LastError ?? snapshot.VerificationStatus}";
            ProductHome.UpdateSettings(new ProductSettingsState(
                ready, snapshot.IsVisible, snapshot.IsInteractionEnabled, snapshot.Mode,
                keyboard, layering.DangoLayer,
                snapshot.LastError, settingsSession?.HasWarning == true,
                settingsSession is { IsWriteProtected: true } or { IsPresetWriteProtected: true },
                settingsSession?.StatusMessage ?? string.Empty,
                commandController.CanShowDesktop));
        }
        finally
        {
            refreshingHostState = false;
        }
    }

    private void BuildPhysicsTuningPanel()
    {
        StackPanel? sectionPanel = null;
        var sectionIndex = 0;
        foreach (var descriptor in PhysicsParameterDescriptors)
        {
            if (descriptor.IsSection)
            {
                sectionPanel = new StackPanel
                {
                    Margin = new Thickness(0, 4, 0, 6),
                };
                PhysicsTuningPanel.Children.Add(new Expander
                {
                    Header = new TextBlock
                    {
                        FontWeight = FontWeights.SemiBold,
                        Text = descriptor.Label,
                    },
                    IsExpanded = sectionIndex == 0,
                    Margin = new Thickness(0, sectionIndex == 0 ? 0 : 6, 0, 0),
                    Content = sectionPanel,
                });
                sectionIndex++;
                continue;
            }

            var valueTextBox = new WpfTextBox
            {
                IsReadOnly = true,
                Width = 72,
                Margin = new Thickness(10, 0, 0, 0),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
                Tag = descriptor,
            };
            var slider = new Slider
            {
                IsEnabled = false,
                Minimum = descriptor.Minimum,
                Maximum = descriptor.Maximum,
                TickFrequency = descriptor.Tick,
                IsSnapToTickEnabled = false,
                Tag = descriptor,
            };
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Text = descriptor.Label,
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(valueTextBox, 2);
            row.Children.Add(label);
            row.Children.Add(slider);
            row.Children.Add(valueTextBox);
            (sectionPanel ?? PhysicsTuningPanel).Children.Add(row);
            physicsControls.Add(descriptor.PropertyName, (slider, valueTextBox));
        }
        RefreshPhysicsParameters();
    }

    private void OnPhysicsSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (refreshingPhysicsParameters || sender is not Slider { Tag: PhysicsParameterDescriptor descriptor })
        {
            return;
        }
        ApplyPhysicsValue(descriptor, e.NewValue);
    }

    private void OnPhysicsValueKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }
        OnPhysicsValueCommitted(sender, e);
        e.Handled = true;
    }

    private void OnPhysicsValueCommitted(object sender, RoutedEventArgs e)
    {
        if (refreshingPhysicsParameters || sender is not WpfTextBox { Tag: PhysicsParameterDescriptor descriptor } textBox ||
            !double.TryParse(textBox.Text, out var value))
        {
            RefreshPhysicsParameters();
            return;
        }
        ApplyPhysicsValue(descriptor, value);
    }

    private void ApplyPhysicsValue(PhysicsParameterDescriptor descriptor, double value)
    {
        try
        {
            var parameters = commandController.GetPhysicsParameters().WithNumericValue(descriptor.PropertyName, value);
            var result = commandController.ApplyPhysicsParameters(parameters);
            ApplyCommand(result);
        }
        catch (ArgumentOutOfRangeException)
        {
            DesktopCommandStatusText.Text = $"{descriptor.Label}超出安全范围，未应用。";
            RefreshPhysicsParameters();
        }
    }

    private void RefreshPhysicsParameters()
    {
        if (physicsControls.Count == 0)
        {
            return;
        }
        var parameters = commandController.GetPhysicsParameters();
        refreshingPhysicsParameters = true;
        try
        {
            foreach (var descriptor in PhysicsParameterDescriptors.Where(item => !item.IsSection))
            {
                var value = (double)(typeof(DangoSelfPhysicsParameters).GetProperty(descriptor.PropertyName)!
                    .GetValue(parameters) ?? 0d);
                var controls = physicsControls[descriptor.PropertyName];
                controls.Slider.Value = value;
                controls.TextBox.Text = value.ToString(descriptor.Format, System.Globalization.CultureInfo.CurrentCulture);
            }
        }
        finally
        {
            refreshingPhysicsParameters = false;
        }
    }

    private sealed record PhysicsParameterDescriptor(
        string PropertyName,
        string Label,
        double Minimum,
        double Maximum,
        double Tick,
        string Format,
        bool IsSection = false)
    {
        public static PhysicsParameterDescriptor Section(string label) => new(string.Empty, label, 0, 0, 0, "", true);
    }

    private static readonly IReadOnlyList<PhysicsParameterDescriptor> PhysicsParameterDescriptors =
    [
        PhysicsParameterDescriptor.Section("结构与伸缩"),
        new(nameof(DangoSelfPhysicsParameters.RadialRestLength), "团子大小", 0.45, 0.90, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.TopInwardForce), "顶部内收力", 0, 1, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.RadialMinimumRatio), "径向压缩下限", 0.65, 1, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.RadialMaximumRatio), "径向拉伸上限", 1, 1.35, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.PerimeterMinimumRatio), "外围压缩下限", 0.65, 1, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.PerimeterMaximumRatio), "外围拉伸上限", 1, 1.35, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.RadialElasticity), "径向弹性", 0.5, 20, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.PerimeterElasticity), "外围弹性", 0.5, 25, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.RadialDamping), "径向阻尼", 0, 2, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.PerimeterDamping), "外围阻尼", 0, 2, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.AreaPreservationStrength), "面积保持", 0, 20, 0.1, "F1"),
        PhysicsParameterDescriptor.Section("防交叉"),
        new(nameof(DangoSelfPhysicsParameters.BoneRepulsionDistance), "骨架排斥距离", 0.02, 0.30, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.BoneRepulsionStrength), "骨架排斥力", 0, 10, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.BoneRepulsionMaximumForce), "排斥最大力", 0.1, 20, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.CoreExclusionRatio), "核心排除比例", 0.05, 0.60, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.PerimeterAngleStrength), "90°–180°纠正力", 0, 20, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.LocalAngleBalanceStrength), "局部角度均衡力", 0, 20, 0.1, "F1"),
        PhysicsParameterDescriptor.Section("地面与整体"),
        new(nameof(DangoSelfPhysicsParameters.Gravity), "重力", 0.5, 25, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.DropHeight), "初始下落高度", 0, 1.5, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.GroundFriction), "地面摩擦", 0, 1.5, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.Restitution), "回弹系数", 0, 0.5, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.OverallDamping), "整体阻尼", 0, 8, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.GroundDeformationStrength), "落地形变力", 0, 10, 0.1, "F1"),
        PhysicsParameterDescriptor.Section("鼠标拖动力"),
        new(nameof(DangoSelfPhysicsParameters.DragStiffness), "拖动弹性", 0.1, 40, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.DragDamping), "拖动阻尼", 0, 12, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.DragMaximumForce), "拖动力上限", 0.2, 30, 0.1, "F1"),
        PhysicsParameterDescriptor.Section("WASD 与跳跃冲量"),
        new(nameof(DangoSelfPhysicsParameters.HorizontalForce), "A/D 水平力", 0.1, 12, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.MaximumHorizontalSpeed), "水平速度上限", 0.2, 8, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.AirControlRatio), "空中控制比例", 0, 1, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.DownwardForce), "S 向下力", 0, 12, 0.1, "F1"),
        new(nameof(DangoSelfPhysicsParameters.JumpImpulse), "W 跳跃冲量", 0.05, 2, 0.01, "F2"),
        new(nameof(DangoSelfPhysicsParameters.JumpCooldownMilliseconds), "跳跃冷却 ms", 100, 1500, 10, "F0"),
    ];

    private void RefreshDiagnostics()
    {
        var summary = commandController.CreateDiagnosticSummary();
        DiagnosticsTextBox.Text = summary;
        ProductHome.SetDiagnosticsText(summary);
    }

    private void LoadOrCreateVerificationSession()
    {
        if (verificationSession is not null)
        {
            return;
        }

        if (File.Exists(draftPath))
        {
            try
            {
                BindVerificationSession(verificationStore.Load(draftPath));
                DraftStatusText.Text = "已载入本机验收草稿。";
                return;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                DraftStatusText.Text = "本机草稿无法读取，原文件未被覆盖。";
            }
        }

        BindVerificationSession(CreateVerificationSession());
    }

    private VerificationSession CreateVerificationSession() =>
        VerificationSession.Create(
            verificationDefinition,
            CaptureEnvironment(),
            DateTimeOffset.Now);

    private VerificationEnvironmentSnapshot CaptureEnvironment()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ControlCenterWindow).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "未采集";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? version;
        var dpi = VisualTreeHelper.GetDpi(this);
        var displays = Forms.Screen.AllScreens;
        var displaySummary = $"{displays.Length} 个显示器 / 当前窗口 {dpi.DpiScaleX * 100:F0}% DPI";
        return new VerificationEnvironmentSnapshot(
            version,
            informationalVersion,
            commandController.GetSnapshot().Mode,
            Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture),
            displaySummary);
    }

    private void BindVerificationSession(VerificationSession session)
    {
        verificationSession = session;
        var definitions = verificationDefinition.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        verificationItems = session.Results
            .Select(result => new VerificationItemViewModel(definitions[result.ItemId], result))
            .ToArray();
        VerificationItemsControl.ItemsSource = verificationItems;
        VerificationEnvironmentText.Text =
            $"{session.Environment.HostMode} / Windows Build {session.Environment.OsBuild} / {session.Environment.DisplaySummary}";
        RefreshVerificationSummary();
        if (SystemParameters.HighContrast)
        {
            Dispatcher.BeginInvoke(ApplyAccessibilityTheme, DispatcherPriority.Loaded);
        }
    }

    private void RefreshVerificationSummary()
    {
        if (verificationSession is null)
        {
            VerificationProgressText.Text = "0 / 0 已确认";
            return;
        }

        var confirmed = verificationSession.Results.Count(
            result => result.Outcome != ManualVerificationOutcome.Unverified);
        var failed = verificationSession.Results.Count(
            result => result.Outcome == ManualVerificationOutcome.Failed);
        VerificationProgressText.Text =
            $"{confirmed} / {verificationSession.Results.Count} 已确认 · {failed} 项失败";
    }

    private void ShowPage(UIElement page)
    {
        var product = commandController.UiMode == ApplicationUiMode.Product;
        ProductHome.Visibility = product ? Visibility.Visible : Visibility.Collapsed;
        LegacyShell.Visibility = product ? Visibility.Collapsed : Visibility.Visible;
        MinWidth = product ? Math.Min(ControlCenterWindowSettingsValidator.MinimumWidth, Width) : 820;
        OverviewPage.Visibility = Visibility.Collapsed;
        DesktopPage.Visibility = Visibility.Collapsed;
        VerificationPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        if (!product) return;
        if (ReferenceEquals(page, OverviewPage)) ProductHome.ShowHomePage();
        else ProductHome.ShowSettingsPage(ReferenceEquals(page, DiagnosticsPage));
    }

    private void ApplyUiMode()
    {
        if (commandController.UiMode != ApplicationUiMode.Product) return;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(253, 252, 249));
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            CornerRadius = new CornerRadius(14),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });
        ProductWindowChrome.Visibility = Visibility.Visible;
        SourceInitialized += (_, _) =>
        {
            RequestRoundedProductCorners();
            (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(OnProductWindowMessage);
            ApplyWindowPresetSize(fitWorkingArea: true);
        };
        UpdateWindowSizeMenu();
        foreach (var element in new UIElement[] { VerificationNavigation, ContinueVerificationButton,
            PopulationCard, SkeletonCard, PhysicsCard, CushionTitle, CushionOptions, IslandTitle, IslandDescription })
            element.Visibility = Visibility.Collapsed;
        DesktopNavigation.Content = "桌面与团子设置";
        DiagnosticsNavigation.Content = "帮助与诊断";
        OverviewTitle.Text = "你的桌面团子";
        OverviewIntro.Text = "轻点团子会眨眼；按住 Shift 和鼠标左键，在团子身上横向轻抚会眯眼。不操作时，团子偶尔眨眼、左右看看；单只团子也会偶尔走一小段。直接拖动团子后松手；在设置中开启键盘控制。关闭窗口只是隐藏，可从托盘找回；结束使用请选择退出 Dango。";
        LayeringTitle.Text = "图标层级";
        LayeringDescription.Text = "选择团子显示在桌面图标上方或下方。";
        ((System.Windows.Controls.Panel)RecoveryActions.Parent).Children.Remove(RecoveryActions);
        ProductRecoverySlot.Child = RecoveryActions;
        ProductRecoverySlot.Visibility = Visibility.Visible;
        ShowPage(OverviewPage);
    }

    public (double Width, double Height) CaptureSize()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(0, 0, ActualWidth, ActualHeight) : RestoreBounds;
        return bounds.Width >= MinWidth && bounds.Height >= MinHeight
            ? (bounds.Width, bounds.Height) : (Width, Height);
    }

    private void SaveWindowSettings() => saveSettings?.Invoke();

    private void ApplyAccessibilityTheme()
    {
        if (!SystemParameters.HighContrast)
        {
            RestoreThemeOverrides();
            return;
        }

        ApplyHighContrast(this);
    }

    private void ApplyHighContrast(DependencyObject element)
    {
        // ProductHomeView owns its dynamic high-contrast palette, including selected-button text.
        if (element is ProductHomeView) return;
        switch (element)
        {
            case Window window:
                OverrideThemeProperty(window, BackgroundProperty, System.Windows.SystemColors.WindowBrush);
                OverrideThemeProperty(window, ForegroundProperty, System.Windows.SystemColors.WindowTextBrush);
                break;
            case Border border:
                OverrideThemeProperty(border, Border.BackgroundProperty, System.Windows.SystemColors.WindowBrush);
                OverrideThemeProperty(border, Border.BorderBrushProperty, System.Windows.SystemColors.WindowTextBrush);
                break;
            case TextBlock textBlock:
                OverrideThemeProperty(textBlock, TextBlock.ForegroundProperty, System.Windows.SystemColors.WindowTextBrush);
                break;
            case System.Windows.Controls.Control control:
                OverrideThemeProperty(control, System.Windows.Controls.Control.ForegroundProperty, System.Windows.SystemColors.WindowTextBrush);
                OverrideThemeProperty(control, System.Windows.Controls.Control.BackgroundProperty, System.Windows.SystemColors.WindowBrush);
                OverrideThemeProperty(control, System.Windows.Controls.Control.BorderBrushProperty, System.Windows.SystemColors.WindowTextBrush);
                break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            ApplyHighContrast(VisualTreeHelper.GetChild(element, index));
        }
    }

    private void OverrideThemeProperty(
        DependencyObject target,
        DependencyProperty property,
        object value)
    {
        if (highContrastOverrides.Any(item => item.Target == target && item.Property == property))
        {
            return;
        }

        highContrastOverrides.Add(new ThemePropertyOverride(target, property, target.ReadLocalValue(property)));
        target.SetValue(property, value);
    }

    private void RestoreThemeOverrides()
    {
        for (var index = highContrastOverrides.Count - 1; index >= 0; index--)
        {
            var item = highContrastOverrides[index];
            if (item.OriginalValue == DependencyProperty.UnsetValue)
            {
                item.Target.ClearValue(item.Property);
            }
            else
            {
                item.Target.SetValue(item.Property, item.OriginalValue);
            }
        }

        highContrastOverrides.Clear();
    }

    private static string SafeUserMessage(Exception exception) => exception switch
    {
        InvalidDataException => exception.Message,
        UnauthorizedAccessException => "当前保存位置不可写，请选择其他位置。",
        _ => "本地文件操作失败，原有记录未被覆盖。",
    };

    private sealed record ThemePropertyOverride(
        DependencyObject Target,
        DependencyProperty Property,
        object OriginalValue);
}
