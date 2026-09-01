using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.Verification;
using Forms = System.Windows.Forms;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ControlCenterWindow : Window
{
    private readonly ApplicationCommandController commandController;
    private readonly VerificationDefinition verificationDefinition;
    private readonly VerificationSessionStore verificationStore;
    private readonly string draftPath;
    private readonly Action requestExit;
    private readonly ControlCenterWindowSettingsStore? windowSettingsStore;
    private readonly string? settingsPath;
    private readonly List<ThemePropertyOverride> highContrastOverrides = [];
    private VerificationSession? verificationSession;
    private IReadOnlyList<VerificationItemViewModel> verificationItems = [];
    private bool allowClose;
    private bool refreshingHostState;

    public ControlCenterWindow(
        ApplicationCommandController commandController,
        VerificationDefinition verificationDefinition,
        VerificationSessionStore verificationStore,
        string draftPath,
        Action requestExit,
        ControlCenterWindowSettingsStore? windowSettingsStore = null,
        string? settingsPath = null)
    {
        this.commandController = commandController ?? throw new ArgumentNullException(nameof(commandController));
        this.verificationDefinition = verificationDefinition ?? throw new ArgumentNullException(nameof(verificationDefinition));
        this.verificationStore = verificationStore ?? throw new ArgumentNullException(nameof(verificationStore));
        this.draftPath = draftPath ?? throw new ArgumentNullException(nameof(draftPath));
        this.requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        this.windowSettingsStore = windowSettingsStore;
        this.settingsPath = settingsPath;
        InitializeComponent();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += OnClosed;
    }

    public void ShowOverview() => ShowPage(OverviewPage);

    public void ShowDiagnostics()
    {
        RefreshDiagnostics();
        ShowPage(DiagnosticsPage);
    }

    public void PrepareForExit() => allowClose = true;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreWindowSettings();
        LoadOrCreateVerificationSession();
        RefreshHostState();
        RefreshDiagnostics();
        ShowPage(OverviewPage);
        ApplyAccessibilityTheme();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowSettings();
        if (allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        RestoreThemeOverrides();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
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
                FileName = $"dango-p1-002-verification-{DateTime.Now:yyyyMMdd-HHmm}.md",
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

    private void ApplyCommand(HostCommandResult result)
    {
        var status = result.State == CommandApplicationState.Applied
            ? "操作已由桌面宿主确认。"
            : result.Error ?? "操作失败，宿主状态未确认。";
        OverviewCommandStatusText.Text = status;
        DesktopCommandStatusText.Text = status;
        RefreshHostState();
    }

    private void RefreshHostState()
    {
        var snapshot = commandController.GetSnapshot();
        refreshingHostState = true;
        try
        {
            VisibilityCheckBox.IsChecked = snapshot.IsVisible;
            InteractionCheckBox.IsChecked = snapshot.IsInteractionEnabled;
            OverviewVisibilityText.Text = snapshot.IsVisible ? "已开启" : "已隐藏";
            OverviewVisibilityButton.Content = snapshot.IsVisible ? "隐藏桌面团子" : "显示桌面团子";
            OverviewInteractionText.Text = snapshot.IsInteractionEnabled ? "已开启" : "已关闭";
            OverviewHostStatusText.Text =
                $"透明覆盖层 · {(snapshot.IsVisible ? "正在运行" : "已隐藏")} · {snapshot.VerificationStatus}";
        }
        finally
        {
            refreshingHostState = false;
        }
    }

    private void RefreshDiagnostics() =>
        DiagnosticsTextBox.Text = commandController.CreateDiagnosticSummary();

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
            "Overlay",
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
            $"透明覆盖层 / Windows Build {session.Environment.OsBuild} / {session.Environment.DisplaySummary}";
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
        OverviewPage.Visibility = Visibility.Collapsed;
        DesktopPage.Visibility = Visibility.Collapsed;
        VerificationPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void RestoreWindowSettings()
    {
        if (windowSettingsStore is null ||
            string.IsNullOrWhiteSpace(settingsPath) ||
            !File.Exists(settingsPath))
        {
            return;
        }

        try
        {
            var settings = windowSettingsStore.Load(settingsPath);
            Width = settings.Width;
            Height = settings.Height;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to restore control center size: {exception.GetType().Name}");
        }
    }

    private void SaveWindowSettings()
    {
        if (windowSettingsStore is null || string.IsNullOrWhiteSpace(settingsPath))
        {
            return;
        }

        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            windowSettingsStore.Save(
                settingsPath,
                new ControlCenterWindowSettings(bounds.Width, bounds.Height));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to save control center size: {exception.GetType().Name}");
        }
    }

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
