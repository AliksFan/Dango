using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Dango.Desktop.DesktopHost;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum HomeEditor { Face, Wardrobe }

public enum ProductSettingsAction
{
    ToggleVisibility, ToggleInteraction, WallpaperMode, WindowMode, BelowIcons, AboveIcons,
    ToggleKeyboard,
    RetryInitialization, RestoreDango, RetryWallpaper, RetrySave, RecoverSettings,
    RefreshDiagnostics, Exit,
}

public sealed record ProductSettingsState(
    bool SceneReady, bool IsVisible, bool IsInteractionEnabled, string Mode,
    KeyboardControlSettings Keyboard, DesktopObjectLayer DangoLayer,
    string? LastError, bool HasSaveWarning, bool IsWriteProtected, string SaveMessage,
    bool CanShowDesktop = true);

/// <summary>The product shell: the home shows the shared live scene; appearance edits stay session-only.</summary>
public partial class ProductHomeView : UserControl
{
    private int transitionVersion;
    private bool reduceMotion;
    private bool highContrast;
    private ControlCenterWindowPreset windowPreset = ControlCenterWindowPreset.Landscape1080p;
    private ProductSettingsState? settingsState;
    private string? settingsCommandError;
    public event Action? SettingsRequested;
    public event Action? HomeRequested;
    public event Action? NoticeRequested;
    public event Action<ProductSettingsAction>? SettingsActionRequested;
    public event Action<ControlCenterWindowPreset>? WindowPresetRequested;
    public bool IsEditing { get; private set; }
    public bool IsSettingsOpen { get; private set; }
    public bool IsInPorch { get; private set; }
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
        InitializePorchNavigation();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => RefreshLiveSceneOwnership();
        IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is false) ClearLiveDirectionalInput();
        };
        LiveDangoVisual.BodyFill = new SolidColorBrush(PreviewColor);
        LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
        UpdateColorSelection();
        UpdateNavigationSelection();
        UpdatePresetSelection();
    }

    public void SetWindowPreset(ControlCenterWindowPreset preset)
    {
        windowPreset = preset;
        UpdatePresetSelection();
        UpdateResponsiveLayout();
    }

    public void ShowEditor(HomeEditor editor)
    {
        if (IsInPorch || IsDangoAway) return;
        ClearLiveDirectionalInput();
        var wasOpen = IsEditing;
        var editorChanged = !wasOpen || SelectedEditor != editor;
        if (editorChanged)
        {
            EditorPanel.BeginAnimation(OpacityProperty, null);
            EditorPanel.Opacity = 0;
            PanelTranslation.BeginAnimation(TranslateTransform.XProperty, null);
            PanelTranslation.X = EditorRestingOffset(editor);
        }
        IsEditing = !IsEditing || SelectedEditor != editor;
        SelectedEditor = editor;
        FaceOptions.Visibility = editor == HomeEditor.Face ? Visibility.Visible : Visibility.Collapsed;
        WardrobeOptions.Visibility = editor == HomeEditor.Wardrobe ? Visibility.Visible : Visibility.Collapsed;
        if (editorChanged) OptionsBody.ScrollToTop();
        EditorTitle.Text = editor == HomeEditor.Face ? "捏脸" : "换装";
        if (IsLoaded && !IsSettingsOpen) Stage.Focus();
        UpdateTransition(true);
    }

    public void CloseEditor() => CloseEditor(focusStage: true);

    private void CloseEditor(bool focusStage)
    {
        if (!IsEditing) return;
        if (focusStage) Stage.Focus();
        IsEditing = false;
        UpdateTransition(true);
    }

    public void ShowSettingsPage(bool showDiagnostics = false)
    {
        if (IsInPorch) LeavePorch();
        ClearLiveDirectionalInput();
        if (IsEditing) CloseEditor(focusStage: false);
        if (!IsSettingsOpen) SettingsPage.ScrollToTop();
        IsSettingsOpen = true;
        HomeContent.Visibility = Visibility.Collapsed;
        HomeFooter.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        RefreshLiveSceneOwnership();
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
        if (IsInPorch) LeavePorch();
        IsSettingsOpen = false;
        SettingsPage.Visibility = Visibility.Collapsed;
        HomeContent.Visibility = Visibility.Visible;
        HomeFooter.Visibility = Visibility.Visible;
        settingsCommandError = null;
        SettingsFeedbackBar.Visibility = Visibility.Collapsed;
        SettingsCommandStatus.Visibility = Visibility.Collapsed;
        UpdateNavigationSelection();
        Stage.Focus();
        RefreshLiveSceneOwnership();
        UpdateRoomAwayPresentation();
    }

    public void RestoreKeyboardFocus()
    {
        if (IsSettingsOpen) Keyboard.Focus(BackFromSettingsButton);
        else Keyboard.Focus(Stage);
    }

    public void UpdateSettings(ProductSettingsState state)
    {
        settingsState = state;
        var ready = state.SceneReady;
        DisplayToggleButton.Content = !state.CanShowDesktop ? "进入房间后" : state.IsVisible ? "已显示" : "已隐藏";
        InteractionToggleButton.Content = state.IsInteractionEnabled ? "已开启" : "已关闭";
        KeyboardToggleButton.Content = state.Keyboard.IsEnabled ? "已开启" : "已关闭";
        AutomationProperties.SetName(DisplayToggleButton, $"桌面团子，{DisplayToggleButton.Content}，点击切换");
        AutomationProperties.SetHelpText(DisplayToggleButton, state.CanShowDesktop ? "仅本次会话" : "进入房间后才会显示在桌面");
        AutomationProperties.SetName(InteractionToggleButton, $"鼠标互动，{InteractionToggleButton.Content}，点击切换");
        AutomationProperties.SetName(KeyboardToggleButton, $"键盘控制，{KeyboardToggleButton.Content}，点击切换");
        foreach (var button in new[] { DisplayToggleButton, InteractionToggleButton, KeyboardToggleButton,
                     WallpaperModeButton, WindowModeButton })
            button.IsEnabled = ready;
        DisplayToggleButton.IsEnabled = ready && state.CanShowDesktop;
        var wallpaper = string.Equals(state.Mode, "Wallpaper", StringComparison.Ordinal);
        BelowIconsButton.IsEnabled = AboveIconsButton.IsEnabled = ready && wallpaper;
        RetryInitializationButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        RetryInitializationButton.IsEnabled = !ready;
        RestoreDangoSettingsButton.IsEnabled = RetryWallpaperSettingsButton.IsEnabled = ready;
        SettingRunState.Text = ready ? state.LastError is null ? "场景已就绪" : state.LastError
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
    private void OnLandscape1080pSettingsClick(object sender, RoutedEventArgs e) => RequestWindowPreset(ControlCenterWindowPreset.Landscape1080p);
    private void OnLandscape1440pSettingsClick(object sender, RoutedEventArgs e) => RequestWindowPreset(ControlCenterWindowPreset.Landscape1440p);
    private void OnPortraitSettingsClick(object sender, RoutedEventArgs e) => RequestWindowPreset(ControlCenterWindowPreset.Portrait);

    private void RequestWindowPreset(ControlCenterWindowPreset preset)
    {
        SetWindowPreset(preset);
        WindowPresetRequested?.Invoke(preset);
    }
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
        else if (IsInPorch) LeavePorch();
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
        if (!IsKeyboardFocusWithin || Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox or PasswordBox)
            return;
        if (e.Key == Key.Escape)
        {
            if (e.IsRepeat) { e.Handled = IsSettingsOpen || IsInPorch || IsEditing; return; }
            if (IsSettingsOpen)
            {
                HomeRequested?.Invoke();
                if (IsSettingsOpen) ShowHomePage();
            }
            else if (IsInPorch) LeavePorch();
            else if (IsEditing) CloseEditor();
            else return;
            e.Handled = true;
            return;
        }
        if (IsSettingsOpen || IsDangoAway)
            return;
        if (!IsInPorch && e.Key is (Key.A or Key.D))
        {
            if (!e.IsRepeat) ShowEditor(e.Key == Key.D ? HomeEditor.Face : HomeEditor.Wardrobe);
            e.Handled = true;
            return;
        }
        if (IsEditing && e.Key is not (Key.S or Key.Space)) return;
        if (e.Key == Key.W)
        {
            if (!e.IsRepeat)
            {
                if (IsInPorch) PorchScene.InteractNearestDoor();
                else EnterPorch();
            }
            e.Handled = true;
            return;
        }
        e.Handled = SetLiveDirectionalKey(e.Key, true, e.IsRepeat);
    }

    private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsSettingsOpen || IsDangoAway) return;
        if (!IsInPorch && e.Key is (Key.A or Key.D)) { e.Handled = true; return; }
        e.Handled = SetLiveDirectionalKey(e.Key, false, false);
    }

    private void OnBackFromPorchClick(object sender, RoutedEventArgs e) => LeavePorch();

    private void EnterPorch()
    {
        if (IsInPorch || IsDangoAway) return;
        ClearLiveDirectionalInput();
        IsInPorch = true;
        roomNavigation.ShowPorch();
        ChangeLiveSurface();
        UpdateLiveMovementMode();
        UpdateTransition(true);
        Stage.Focus();
    }

    private void LeavePorch()
    {
        if (!IsInPorch) return;
        porchResumeX = PorchScene.PetWorldX;
        IsInPorch = false;
        roomNavigation.ShowHome();
        ClearLiveDirectionalInput();
        ChangeLiveSurface();
        UpdateLiveMovementMode();
        UpdateTransition(true);
        Stage.Focus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        AttachLiveFocusSafety();
        UpdateTheme();
        UpdateTransition(false);
        RefreshLiveSceneOwnership();
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && IsVisible && !IsSettingsOpen && !IsInPorch) Stage.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        DetachLiveFocusSafety();
        UpdateTransition(false);
        StopLiveScene();
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
        PorchScene.InvalidateVisual();
        UpdateLauncherSelection();
        UpdateColorSelection();
        UpdatePresetSelection();
        if (settingsState is not null) UpdateSettings(settingsState);
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face || sender is not Button { Tag: string color }) return;
        PreviewColor = (Color)ColorConverter.ConvertFromString(color);
        SetLivePreviewColor(PreviewColor);
        UpdateColorSelection();
        Stage.Focus();
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
        SettingsButton.BorderThickness = IsSettingsOpen ? new Thickness(0, 0, 0, 2) : new Thickness(0);
        if (IsSettingsOpen) SettingsButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "HomeAccent");
        else SettingsButton.BorderBrush = Brushes.Transparent;
        AutomationProperties.SetItemStatus(SettingsButton, IsSettingsOpen ? "当前页面" : string.Empty);
    }

    private void UpdatePresetSelection()
    {
        Select(Landscape1080pSettingsButton, windowPreset == ControlCenterWindowPreset.Landscape1080p);
        Select(Landscape1440pSettingsButton, windowPreset == ControlCenterWindowPreset.Landscape1440p);
        Select(PortraitSettingsButton, windowPreset == ControlCenterWindowPreset.Portrait);
    }

    private void OnHomeSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void OnHomeContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStageSizing();
        UpdateTransition(false);
    }

    private bool IsPortraitLayout => windowPreset == ControlCenterWindowPreset.Portrait ||
        ActualWidth > 0 && ActualHeight > ActualWidth * 1.2;

    private void UpdateResponsiveLayout()
    {
        if (Stage is null) return;
        var portrait = IsPortraitLayout;
        var compactPortrait = portrait && ActualWidth <= 420;
        var veryNarrow = ActualWidth < 430;
        var compactLandscape = !portrait && ActualWidth < 800;
        var shortLandscape = !portrait && ActualHeight <= 400;
        TopNavigation.Margin = new Thickness(portrait ? 14 : 22, portrait ? 6 : 8, portrait ? 14 : 22, portrait ? 6 : 8);
        PorchBrand.Margin = TopNavigation.Margin;
        Brand.Margin = new Thickness(0, 0, portrait ? 10 : 20, 0);
        Grid.SetRow(ToolsNavigation, veryNarrow ? 1 : 0);
        Grid.SetColumn(ToolsNavigation, veryNarrow ? 0 : 2);
        Grid.SetColumnSpan(ToolsNavigation, veryNarrow ? 3 : 1);
        ToolsNavigation.Margin = veryNarrow ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 126, 0);
        ToolsNavigation.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        Stage.Margin = portrait ? new Thickness(16, 4, 16, 8)
            : compactLandscape ? new Thickness(18, 8, 18, 8) : new Thickness(24, 10, 24, 10);
        Stage.MinHeight = compactPortrait ? 520 : portrait ? 560 : shortLandscape ? 260 : compactLandscape ? 330 : ActualWidth >= 1150 ? 430 : 340;
        GapColumn.Width = new GridLength(portrait ? 0 : compactLandscape ? 18 : 36);
        var portraitHeightFit = Math.Clamp(260 + (ActualHeight - 600) * .2, 250, 300);
        var previewWidth = portrait ? Math.Min(portraitHeightFit, Math.Max(210, ActualWidth * .625))
            : compactLandscape ? 180 : ActualWidth >= 1150 ? 420 : 320;
        var previewHeight = previewWidth * .75;
        PetPreview.Width = previewWidth;
        PetPreview.Height = PetVisualArea.Height = previewHeight;
        PetScale.ScaleX = PetScale.ScaleY = 1;
        Launchers.Margin = new Thickness(0, portrait ? 18 : compactLandscape ? 18 : 26, 0, 0);
        WardrobeButton.Margin = new Thickness(portrait ? 12 : 14, 0, 0, 0);
        foreach (var button in new[] { FaceButton, WardrobeButton })
        {
            button.MinWidth = compactLandscape ? 66 : 76;
            button.Padding = new Thickness(14, 8, 14, 8);
        }
        foreach (var button in new[] { CreamButton, PeachButton, GreenButton })
            button.Margin = new Thickness(0, 0, portrait ? 10 : 8, 0);
        Colors.Margin = new Thickness(0, 0, 0, portrait ? 16 : 24);
        EyePresetButton.Margin = new Thickness(0, 0, 0, portrait ? 16 : 24);
        ApplyButton.MinWidth = portrait ? 76 : 90;
        EditorPanel.MinHeight = portrait ? 290 : shortLandscape ? 245 : compactLandscape ? 300 : 340;
        EditorPanel.Margin = portrait ? new Thickness(12, 4, 12, 8)
            : shortLandscape ? new Thickness(4) : new Thickness(4, 8, 4, 8);
        EditorPanel.VerticalAlignment = portrait
            || shortLandscape ? System.Windows.VerticalAlignment.Stretch : System.Windows.VerticalAlignment.Center;
        PorchContent.Margin = new Thickness(0);
        PorchContent.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        OptionsBody.Margin = shortLandscape ? new Thickness(0, 8, 0, 8) : portrait
            ? new Thickness(0, 14, 0, 12) : new Thickness(0, 26, 0, 20);
        HomeFooter.Margin = new Thickness(portrait ? 16 : 22, 2, portrait ? 16 : 22, 7);
        PreviewStatus.Text = portrait || compactLandscape ? "A 换装 · D 捏脸 · W 进入"
            : "A 换装 · D 捏脸 · W 进入 · S 轻压 · 空格跳跃";
        SettingsInner.Margin = portrait ? new Thickness(16, 8, 16, 20)
            : compactLandscape ? new Thickness(18, 8, 18, 20) : new Thickness(38, 10, 38, 24);
        SettingsFeedbackBar.Margin = portrait ? new Thickness(16, 0, 16, 0)
            : compactLandscape ? new Thickness(18, 0, 18, 0) : new Thickness(38, 0, 38, 0);
        UpdateStageSizing();
        UpdateTransition(false);
    }

    private void UpdateStageSizing()
    {
        if (Stage is null || HomeContent is null) return;
        var available = HomeContent.ActualHeight - Stage.Margin.Top - Stage.Margin.Bottom;
        var height = Math.Max(Stage.MinHeight, available);
        if (double.IsNaN(Stage.Height) || Math.Abs(Stage.Height - height) > .5) Stage.Height = height;
    }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTransition(false);

    private double EditorRestingOffset(HomeEditor editor) =>
        (editor == HomeEditor.Face ? -1 : 1) * (IsPortraitLayout ? 120 : 46);

    private void UpdateTransition(bool animate)
    {
        if (EditorPanel is null) return;
        var version = ++transitionVersion;
        var width = Math.Max(0, Stage.ActualWidth);
        var height = Math.Max(0, Stage.ActualHeight);
        var portrait = IsPortraitLayout;
        var compactPortrait = portrait && ActualWidth <= 420;
        var compactLandscape = !portrait && ActualWidth < 800;
        var petShare = compactLandscape ? 50 : 57;
        var panelShare = compactLandscape ? 50 : 43;
        var faceOnLeft = SelectedEditor == HomeEditor.Face &&
            (IsEditing || EditorPanel.Visibility == Visibility.Visible && !IsInPorch);
        LeftColumn.Width = new GridLength(portrait ? 1 : faceOnLeft ? panelShare : petShare, GridUnitType.Star);
        RightColumn.Width = new GridLength(portrait ? 0 : faceOnLeft ? petShare : panelShare, GridUnitType.Star);
        PresenceStageRow.Height = portrait
            ? new GridLength(Math.Clamp(height * .40, compactPortrait ? 222 : 248, 292)) : new GridLength(1, GridUnitType.Star);
        DetailStageRow.Height = portrait ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(EditorPanel, portrait ? 1 : 0);
        Grid.SetColumn(EditorPanel, portrait ? 0 : faceOnLeft ? 0 : 2);
        Grid.SetColumnSpan(EditorPanel, portrait ? 3 : 1);
        Grid.SetRow(PorchContent, 0);
        Grid.SetColumn(PorchContent, 0);
        Grid.SetColumnSpan(PorchContent, 3);
        var gap = GapColumn.Width.Value;
        var available = Math.Max(0, width - gap);
        var left = available * LeftColumn.Width.Value / (LeftColumn.Width.Value + RightColumn.Width.Value);
        var right = available - left;
        Presence.Width = portrait ? Math.Min(width, Math.Max(250, PetPreview.Width + 16))
            : Math.Min(Math.Max(210, PetPreview.Width + 18), faceOnLeft ? right : left);
        var presenceHeight = PetVisualArea.Height + Launchers.Margin.Top + 40;
        var restingTop = Math.Max(0, (height - presenceHeight) / 2);
        Presence.Margin = new Thickness(0, restingTop, 0, 0);
        var offsetX = portrait ? 0 : IsEditing ? faceOnLeft ? (width - right) / 2 : -(width - left) / 2
            : IsInPorch ? -48 : 0;
        var offsetY = portrait && IsEditing ? 36 - restingTop : 0;
        animate &= IsLoaded && !ReduceMotion && SystemParameters.ClientAreaAnimation;
        UpdateLauncherSelection();
        var horizontalPanelHeight = Math.Max(EditorPanel.MinHeight,
            Math.Min(420, height - EditorPanel.Margin.Top - EditorPanel.Margin.Bottom));
        EditorPanel.Height = portrait ? double.NaN : horizontalPanelHeight;
        PorchContent.Height = double.NaN;
        Presence.IsHitTestVisible = Presence.IsEnabled = !IsInPorch;
        Launchers.IsHitTestVisible = !IsInPorch;
        PorchBrand.Visibility = IsInPorch ? Visibility.Visible : Visibility.Collapsed;
        TopNavigation.IsHitTestVisible = !IsInPorch;
        HomeFooter.IsHitTestVisible = !IsInPorch;
        Launchers.IsEnabled = TopNavigation.IsEnabled = HomeFooter.IsEnabled = !IsInPorch;
        EditorPanel.IsEnabled = EditorPanel.IsHitTestVisible = IsEditing;
        if (IsEditing) EditorPanel.Visibility = Visibility.Visible;
        PorchContent.IsEnabled = IsInPorch;
        PorchContent.IsHitTestVisible = IsInPorch;
        if (IsInPorch) PorchContent.Visibility = Visibility.Visible;
        Animate(PresenceTranslation, TranslateTransform.XProperty, offsetX, animate);
        Animate(PresenceTranslation, TranslateTransform.YProperty, offsetY, animate);
        Animate(Presence, OpacityProperty, IsInPorch ? 0 : 1, animate);
        Animate(PanelTranslation, TranslateTransform.XProperty, IsEditing ? 0 : EditorRestingOffset(SelectedEditor), animate);
        Animate(PorchTranslation, TranslateTransform.XProperty, IsInPorch ? 0 : portrait ? 120 : 46, animate);
        Animate(Launchers, OpacityProperty, IsInPorch ? 0 : 1, animate);
        Animate(TopNavigation, OpacityProperty, IsInPorch ? 0 : 1, animate);
        Animate(HomeFooter, OpacityProperty, IsInPorch ? 0 : 1, animate);
        Animate(EditorPanel, OpacityProperty, IsEditing ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !IsEditing) EditorPanel.Visibility = Visibility.Hidden;
        });
        Animate(PorchContent, OpacityProperty, IsInPorch ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !IsInPorch) PorchContent.Visibility = Visibility.Hidden;
        });
        UpdateRoomAwayPresentation();
    }

    private static void Animate(Animatable target, DependencyProperty property, double destination, bool animate)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, destination);
        if (animate) target.BeginAnimation(property, CreateAnimation(from, destination), HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(UIElement target, DependencyProperty property, double destination, bool animate) =>
        Animate(target, property, destination, animate, static () => { });

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
