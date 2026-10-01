using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Dango.Desktop.DesktopHost;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum HomeEditor { Face, Wardrobe }

public enum ProductSettingsAction
{
    ToggleVisibility, ToggleInteraction, WallpaperMode, WindowMode, BelowIcons, AboveIcons,
    ToggleKeyboard, ArrowKeys, Wasd, SproutHat, TopHat, BowlerHat,
    RetryInitialization, RestoreDango, RetryWallpaper, RetrySave, RecoverSettings,
    RefreshDiagnostics, Exit,
}

public sealed record ProductSettingsState(
    bool SceneReady, bool IsVisible, bool IsInteractionEnabled, string Mode,
    KeyboardControlSettings Keyboard, DesktopObjectLayer DangoLayer, string HatPresetId,
    string? LastError, bool HasSaveWarning, bool IsWriteProtected, string SaveMessage);

/// <summary>The product shell: home previews stay local; settings requests go through the host.</summary>
public partial class ProductHomeView : UserControl
{
    private int transitionVersion;
    private bool reduceMotion;
    private bool highContrast;
    private ProductSettingsState? settingsState;
    private string? settingsCommandError;
    public event Action? SettingsRequested;
    public event Action? HomeRequested;
    public event Action? NoticeRequested;
    public event Action<ProductSettingsAction>? SettingsActionRequested;
    public bool IsEditing { get; private set; }
    public bool IsSettingsOpen { get; private set; }
    public HomeEditor SelectedEditor { get; private set; }
    public Color PreviewColor { get; private set; } = Color.FromRgb(245, 213, 165);
    public bool ReduceMotion
    {
        get => reduceMotion;
        set { reduceMotion = value; if (value) UpdateTransition(false); }
    }

    public ProductHomeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateColorSelection();
        UpdateNavigationSelection();
    }

    public void ShowEditor(HomeEditor editor)
    {
        var wasOpen = IsEditing;
        IsEditing = !IsEditing || SelectedEditor != editor;
        SelectedEditor = editor;
        FaceOptions.Visibility = editor == HomeEditor.Face ? Visibility.Visible : Visibility.Collapsed;
        WardrobeOptions.Visibility = editor == HomeEditor.Wardrobe ? Visibility.Visible : Visibility.Collapsed;
        EditorTitle.Text = editor == HomeEditor.Face ? "捏脸" : "换装";
        if (!IsEditing) FocusLauncher();
        else if (wasOpen && EditorPanel.IsKeyboardFocusWithin) FocusLauncher();
        UpdateTransition(true);
    }

    public void CloseEditor() => CloseEditor(restoreLauncherFocus: true);

    private void CloseEditor(bool restoreLauncherFocus)
    {
        if (!IsEditing) return;
        if (restoreLauncherFocus) FocusLauncher();
        IsEditing = false;
        UpdateTransition(true);
    }

    public void ShowSettingsPage(bool showDiagnostics = false)
    {
        if (IsEditing) CloseEditor(restoreLauncherFocus: false);
        if (!IsSettingsOpen) SettingsPage.ScrollToTop();
        IsSettingsOpen = true;
        HomeContent.Visibility = Visibility.Collapsed;
        HomeFooter.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        UpdateNavigationSelection();
        UpdateSettingsFeedback();
        if (showDiagnostics)
        {
            OpenDiagnostics();
            Dispatcher.BeginInvoke(() =>
            {
                if (IsSettingsOpen && SettingsDiagnosticsText.Visibility == Visibility.Visible)
                    SettingsDiagnosticsText.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else Dispatcher.BeginInvoke(() =>
        {
            if (IsSettingsOpen) BackFromSettingsButton.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void ShowHomePage()
    {
        IsSettingsOpen = false;
        SettingsPage.Visibility = Visibility.Collapsed;
        HomeContent.Visibility = Visibility.Visible;
        HomeFooter.Visibility = Visibility.Visible;
        settingsCommandError = null;
        SettingsFeedbackBar.Visibility = Visibility.Collapsed;
        SettingsCommandStatus.Visibility = Visibility.Collapsed;
        UpdateNavigationSelection();
        HomeNavigation.Focus();
    }

    public void UpdateSettings(ProductSettingsState state)
    {
        settingsState = state;
        var ready = state.SceneReady;
        DisplayToggleButton.Content = state.IsVisible ? "已显示" : "已隐藏";
        InteractionToggleButton.Content = state.IsInteractionEnabled ? "已开启" : "已关闭";
        KeyboardToggleButton.Content = state.Keyboard.IsEnabled ? "已开启" : "已关闭";
        AutomationProperties.SetName(DisplayToggleButton, $"桌面团子，{DisplayToggleButton.Content}，点击切换");
        AutomationProperties.SetHelpText(DisplayToggleButton, "仅本次会话");
        AutomationProperties.SetName(InteractionToggleButton, $"鼠标互动，{InteractionToggleButton.Content}，点击切换");
        AutomationProperties.SetName(KeyboardToggleButton, $"键盘控制，{KeyboardToggleButton.Content}，点击切换");
        foreach (var button in new[] { DisplayToggleButton, InteractionToggleButton, KeyboardToggleButton,
                     WallpaperModeButton, WindowModeButton, SproutHatButton, TopHatButton, BowlerHatButton })
            button.IsEnabled = ready;
        ArrowsButton.IsEnabled = WasdButton.IsEnabled = ready && state.Keyboard.IsEnabled;
        var wallpaper = string.Equals(state.Mode, "Wallpaper", StringComparison.Ordinal);
        BelowIconsButton.IsEnabled = AboveIconsButton.IsEnabled = ready && wallpaper;
        RetryInitializationButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        RetryInitializationButton.IsEnabled = !ready;
        RestoreDangoSettingsButton.IsEnabled = RetryWallpaperSettingsButton.IsEnabled = ready;
        SettingRunState.Text = ready ? state.LastError is null ? "团子正在运行" : state.LastError
            : state.LastError ?? "团子尚未启动";
        SaveWarningRow.Visibility = state.HasSaveWarning ? Visibility.Visible : Visibility.Collapsed;
        SaveWarningText.Text = state.SaveMessage;
        RetrySaveSettingsButton.IsEnabled = !state.IsWriteProtected;
        RecoverSettingsButton.Visibility = state.IsWriteProtected ? Visibility.Visible : Visibility.Collapsed;
        ShowDiagnosticsButton.IsEnabled = SettingsExitButton.IsEnabled = true;
        Select(WallpaperModeButton, wallpaper);
        Select(WindowModeButton, string.Equals(state.Mode, "Window", StringComparison.Ordinal));
        Select(BelowIconsButton, state.DangoLayer == DesktopObjectLayer.BelowDesktopIcons);
        Select(AboveIconsButton, state.DangoLayer == DesktopObjectLayer.AboveDesktopIcons);
        Select(ArrowsButton, state.Keyboard.Scheme == KeyboardControlScheme.ArrowKeys);
        Select(WasdButton, state.Keyboard.Scheme == KeyboardControlScheme.Wasd);
        Select(SproutHatButton, state.HatPresetId == "sprout");
        Select(TopHatButton, state.HatPresetId == "top-hat");
        Select(BowlerHatButton, state.HatPresetId == "bowler");
        UpdateSettingsFeedback();
    }

    public void ShowSettingsStatus(string text, bool failed)
    {
        SettingsCommandStatus.Text = text;
        SettingsCommandStatus.Visibility = Visibility.Visible;
        settingsCommandError = failed ? text : null;
        UpdateSettingsFeedback();
    }

    public void SetDiagnosticsText(string text) => SettingsDiagnosticsText.Text = text;

    private void UpdateSettingsFeedback()
    {
        if (!IsSettingsOpen)
        {
            SettingsFeedbackBar.Visibility = Visibility.Collapsed;
            return;
        }

        var warning = settingsState?.HasSaveWarning == true ? "设置未保存，请在下方重试保存。" : null;
        SettingsFeedbackText.Text = settingsCommandError is null ? warning ?? string.Empty
            : warning is null ? settingsCommandError : $"{settingsCommandError}\n{warning}";
        SettingsFeedbackBar.Visibility = string.IsNullOrEmpty(SettingsFeedbackText.Text)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Select(Button button, bool selected)
    {
        button.Background = selected ? (System.Windows.Media.Brush)Resources["HomeSelected"] : Brushes.Transparent;
        button.Foreground = selected ? highContrast ? System.Windows.SystemColors.HighlightTextBrush
            : (System.Windows.Media.Brush)Resources["HomeAccent"]
            : (System.Windows.Media.Brush)Resources["HomeInk"];
        button.Tag = selected ? "Selected" : null;
        AutomationProperties.SetItemStatus(button, selected ? "已选" : "未选");
    }

    private void Request(ProductSettingsAction action)
    {
        if (settingsState is not null) SettingsActionRequested?.Invoke(action);
    }

    private void OnBackFromSettingsClick(object sender, RoutedEventArgs e)
    {
        HomeRequested?.Invoke();
        if (IsSettingsOpen) ShowHomePage();
    }
    private void OnDisplayToggleClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.ToggleVisibility);
    private void OnInteractionToggleClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.ToggleInteraction);
    private void OnWallpaperModeClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.WallpaperMode);
    private void OnWindowModeClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.WindowMode);
    private void OnBelowIconsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.BelowIcons);
    private void OnAboveIconsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.AboveIcons);
    private void OnKeyboardToggleClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.ToggleKeyboard);
    private void OnArrowsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.ArrowKeys);
    private void OnWasdClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.Wasd);
    private void OnSproutHatClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.SproutHat);
    private void OnTopHatClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.TopHat);
    private void OnBowlerHatClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.BowlerHat);
    private void OnRetryInitializationClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RetryInitialization);
    private void OnRestoreDangoClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RestoreDango);
    private void OnRetryWallpaperClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RetryWallpaper);
    private void OnRetrySaveClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RetrySave);
    private void OnRecoverSettingsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RecoverSettings);
    private void OnExitSettingsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.Exit);
    private void OnShowDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (SettingsDiagnosticsText.Visibility == Visibility.Visible)
        {
            SettingsDiagnosticsText.Visibility = Visibility.Collapsed;
            ShowDiagnosticsButton.Content = "查看诊断";
            return;
        }
        OpenDiagnostics();
    }

    private void OpenDiagnostics()
    {
        SettingsDiagnosticsText.Visibility = Visibility.Visible;
        ShowDiagnosticsButton.Content = "收起诊断";
        Request(ProductSettingsAction.RefreshDiagnostics);
        Dispatcher.BeginInvoke(() =>
        {
            if (IsSettingsOpen && SettingsDiagnosticsText.Visibility == Visibility.Visible)
                SettingsDiagnosticsText.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FocusLauncher() => (SelectedEditor == HomeEditor.Face ? FaceButton : WardrobeButton).Focus();
    private void OnFaceClick(object sender, RoutedEventArgs e) => ShowEditor(HomeEditor.Face);
    private void OnWardrobeClick(object sender, RoutedEventArgs e) => ShowEditor(HomeEditor.Wardrobe);
    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseEditor();
    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        if (IsSettingsOpen)
        {
            HomeRequested?.Invoke();
            if (IsSettingsOpen) ShowHomePage();
        }
        else CloseEditor();
    }
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
        if (!IsSettingsOpen) ShowSettingsPage();
    }
    private void OnNoticeClick(object sender, RoutedEventArgs e) => NoticeRequested?.Invoke();
    public void SetNotice(string? text, string? detail)
    {
        NoticeButton.Content = text;
        NoticeButton.ToolTip = detail;
        NoticeButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (IsSettingsOpen)
        {
            HomeRequested?.Invoke();
            if (IsSettingsOpen) ShowHomePage();
        }
        else if (IsEditing) CloseEditor();
        else return;
        e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        UpdateTheme();
        UpdateTransition(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        UpdateTransition(false);
    }

    private void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) UpdateTheme();
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !SystemParameters.ClientAreaAnimation)
                UpdateTransition(false);
        });
    }

    private void UpdateTheme()
    {
        ApplyTheme(SystemParameters.HighContrast);
    }

    internal void ApplyTheme(bool contrast)
    {
        highContrast = contrast;
        Background = contrast ? System.Windows.SystemColors.WindowBrush : Brush("#FDFCF9");
        Foreground = contrast ? System.Windows.SystemColors.WindowTextBrush : Brush("#443D34");
        Resources["HomeInk"] = Foreground;
        Resources["HomeMuted"] = contrast ? Foreground : Brush("#807568");
        Resources["HomeLine"] = contrast ? Foreground : Brush("#E9E4DA");
        Resources["HomeSoft"] = contrast ? Background : Brush("#F3EFE8");
        Resources["HomeAccent"] = contrast ? System.Windows.SystemColors.HighlightBrush : Brush("#885E3E");
        Resources["HomeSelected"] = contrast ? System.Windows.SystemColors.HighlightBrush : Brush("#EFE3D4");
        UpdateLauncherSelection();
        UpdateColorSelection();
        if (settingsState is not null) UpdateSettings(settingsState);
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face || sender is not Button { Tag: string color }) return;
        PreviewColor = (Color)ColorConverter.ConvertFromString(color);
        PetBody.Fill = new SolidColorBrush(PreviewColor);
        UpdateColorSelection();
    }

    private void UpdateColorSelection()
    {
        foreach (var (button, check) in new[] { (CreamButton, CreamCheck), (PeachButton, PeachCheck), (GreenButton, GreenCheck) })
        {
            var selected = PreviewColor == (Color)ColorConverter.ConvertFromString((string)button.Tag);
            button.BorderBrush = selected ? (System.Windows.Media.Brush)Resources["HomeAccent"] : Brushes.Transparent;
            check.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            AutomationProperties.SetItemStatus(button, selected ? "已选，仅预览" : "未选");
        }
    }

    private void UpdateLauncherSelection()
    {
        foreach (var (button, editor) in new[] { (FaceButton, HomeEditor.Face), (WardrobeButton, HomeEditor.Wardrobe) })
        {
            var selected = IsEditing && SelectedEditor == editor;
            button.Background = selected ? (System.Windows.Media.Brush)Resources["HomeSelected"] : Brushes.Transparent;
            button.Foreground = selected ? highContrast ? System.Windows.SystemColors.HighlightTextBrush
                : (System.Windows.Media.Brush)Resources["HomeAccent"] : (System.Windows.Media.Brush)Resources["HomeInk"];
            AutomationProperties.SetItemStatus(button, selected ? "已展开" : "已收起");
        }
    }

    private void UpdateNavigationSelection()
    {
        foreach (var (button, selected) in new[]
                 {
                     (HomeNavigation, !IsSettingsOpen),
                     (SettingsButton, IsSettingsOpen),
                 })
        {
            button.BorderThickness = selected ? new Thickness(0, 0, 0, 2) : new Thickness(0);
            if (selected) button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "HomeAccent");
            else button.BorderBrush = Brushes.Transparent;
            AutomationProperties.SetItemStatus(button, selected ? "当前页面" : string.Empty);
        }
    }

    private void OnHomeSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Stage is null) return;
        var compact = ActualWidth <= 680;
        var narrow = ActualWidth <= 420;
        TopNavigation.Margin = compact ? new Thickness(16) : new Thickness(30, 22, 30, 22);
        Brand.Margin = new Thickness(0, 0, compact ? 12 : 32, 0);
        Grid.SetRow(ToolsNavigation, compact ? 1 : 0);
        Grid.SetColumn(ToolsNavigation, compact ? 0 : 2);
        Grid.SetColumnSpan(ToolsNavigation, compact ? 3 : 1);
        MainNavigation.HorizontalAlignment = compact ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        foreach (Button button in MainNavigation.Children) button.Margin = new Thickness(compact ? 4 : 10, 0, compact ? 4 : 10, 0);
        Stage.Margin = narrow ? new Thickness(12, 8, 12, 20) : compact ? new Thickness(16, 12, 16, 24) : new Thickness(38, 20, 38, 30);
        Stage.MinHeight = compact ? 380 : 420;
        // Keep a fixed centerline even when optional details make the panel taller.
        Presence.Margin = new Thickness(0, (Stage.MinHeight - 116 - (narrow ? 16 : compact ? 32 : 40) - 40) / 2, 0, 0);
        LeftColumn.Width = new GridLength(narrow ? 44 : compact ? 50 : 57, GridUnitType.Star);
        RightColumn.Width = new GridLength(narrow ? 56 : compact ? 50 : 43, GridUnitType.Star);
        GapColumn.Width = new GridLength(narrow ? 12 : compact ? 22 : 46);
        PetPreview.Width = narrow ? 144 * .74 : 144;
        PetPreview.Height = narrow ? 116 * .74 : 116;
        PetScale.ScaleX = PetScale.ScaleY = narrow ? 1 : 1.15;
        Launchers.Margin = new Thickness(0, narrow ? 16 : compact ? 32 : 40, 0, 0);
        WardrobeButton.Margin = new Thickness(narrow ? 4 : 12, 0, 0, 0);
        foreach (var button in new[] { FaceButton, WardrobeButton })
        {
            button.MinWidth = narrow ? 44 : 72;
            button.Padding = new Thickness(narrow ? 6 : 14, 8, narrow ? 6 : 14, 8);
        }
        foreach (var button in new[] { CreamButton, PeachButton, GreenButton }) button.Margin = new Thickness(0, 0, narrow ? 4 : 8, 0);
        Colors.Margin = new Thickness(0, 0, 0, narrow ? 20 : 24);
        EyePresetButton.Margin = new Thickness(0, 0, 0, narrow ? 20 : 24);
        ApplyButton.MinWidth = narrow ? 72 : 90;
        EditorPanel.Margin = new Thickness(4, compact ? 18 : 22, 4, 24);
        EditorPanel.MinHeight = compact ? 338 : 374;
        HomeFooter.Margin = new Thickness(compact ? 22 : 30, 10, 30, 16);
        SettingsInner.Margin = narrow ? new Thickness(12, 8, 12, 20)
            : compact ? new Thickness(16, 12, 16, 24) : new Thickness(38, 10, 38, 24);
        SettingsFeedbackBar.Margin = narrow ? new Thickness(12, 0, 12, 0)
            : compact ? new Thickness(16, 0, 16, 0) : new Thickness(38, 0, 38, 0);
        UpdateTransition(false);
    }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTransition(false);

    private void UpdateTransition(bool animate)
    {
        if (EditorPanel is null) return;
        var version = ++transitionVersion;
        var width = Math.Max(0, Stage.ActualWidth);
        var gap = GapColumn.Width.Value;
        var available = Math.Max(0, width - gap);
        var left = available * LeftColumn.Width.Value / (LeftColumn.Width.Value + RightColumn.Width.Value);
        Presence.Width = Math.Min(210, left);
        var offset = IsEditing ? -(width - left) / 2 : 0;
        animate &= IsLoaded && !ReduceMotion && SystemParameters.ClientAreaAnimation;
        UpdateLauncherSelection();
        EditorPanel.IsEnabled = EditorPanel.IsHitTestVisible = IsEditing;
        if (IsEditing) EditorPanel.Visibility = Visibility.Visible;
        Animate(PresenceTranslation, TranslateTransform.XProperty, offset, animate);
        Animate(PanelTranslation, TranslateTransform.XProperty, IsEditing ? 0 : 46, animate);
        Animate(EditorPanel, OpacityProperty, IsEditing ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !IsEditing) EditorPanel.Visibility = Visibility.Hidden;
        });
    }

    private static void Animate(Animatable target, DependencyProperty property, double destination, bool animate)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, destination);
        if (animate) target.BeginAnimation(property, CreateAnimation(from, destination), HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(UIElement target, DependencyProperty property, double destination, bool animate, Action completed)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, destination);
        if (!animate) { completed(); return; }
        var animation = CreateAnimation(from, destination);
        animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames CreateAnimation(double from, double destination) => new()
    {
        Duration = TimeSpan.FromMilliseconds(440),
        FillBehavior = FillBehavior.Stop,
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new SplineDoubleKeyFrame(destination, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440)), new KeySpline(.22, 1, .36, 1)),
        },
    };

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
