using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

namespace Dango.Desktop.AppShell.ControlCenter;

public enum HomeEditor { Face, Wardrobe }

public enum ProductSettingsAction
{
    RestoreRoomDisplay, ReinitializeScene, RetrySave, RecoverSettings,
    RefreshDiagnostics, Exit,
}

public sealed record ProductSettingsSceneSnapshot(
    string Name, string Status, string? Error = null,
    bool CanRestoreDisplay = false, bool CanReinitialize = false, string Diagnostics = "", long Revision = 0);

public sealed record ProductSettingsState(
    ProductSettingsSceneSnapshot Scene, bool HasSaveWarning, bool IsWriteProtected, string SaveMessage);

/// <summary>The product shell: the home shows the shared live scene; appearance drafts save only on explicit apply.</summary>
public partial class ProductHomeView : UserControl
{
    private int transitionVersion;
    private (bool Editing, HomeEditor Editor, bool Porch, bool Away)? transitionState;
    private bool reduceMotion;
    private bool highContrast;
    private double minimumStageHeight = 330;
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
    internal bool IsSettingsDiagnosticsOpen => SettingsDiagnosticsText.Visibility == Visibility.Visible;
    public bool IsInPorch { get; private set; }
    public HomeEditor SelectedEditor { get; private set; }
    public Color PreviewColor { get; private set; } = Color.FromRgb(255, 232, 194);
    public bool ReduceMotion
    {
        get => reduceMotion;
        set { reduceMotion = value; SettingsPage.ReduceMotion = value; if (value) UpdateTransition(false); }
    }

    public ProductHomeView()
    {
        AppearanceColorPicker = ShowAppearanceColorPicker;
        InitializeComponent();
        InitializeAccessories();
        InitializePorchNavigation();
        RefreshAppearanceControls();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelAppearancePreview(); RefreshLiveSceneOwnership(); };
        IsKeyboardFocusWithinChanged += (_, e) =>
        {
            // Belt input belongs to the active window, including its chrome controls.
            // Its window focus handlers clear input when leaving that owner or entering text.
            if (e.NewValue is false && beltRuntime is null) ClearLiveDirectionalInput();
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
        if (IsEditing && SelectedEditor == HomeEditor.Face) CancelAppearancePreview();
        if (IsEditing && SelectedEditor == HomeEditor.Wardrobe) CancelAccessoryPreview();
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
        RefreshAppearanceControls();
        EditorTitle.Text = editor == HomeEditor.Face ? "捏脸" : "换装";
        accessoryPanel.Refresh();
        UpdateAccessoryPreviewMode();
        if (IsLoaded && !IsSettingsOpen) Stage.Focus();
        UpdateTransition(true);
    }

    public void CloseEditor() => CloseEditor(focusStage: true);

    private void CloseEditor(bool focusStage)
    {
        if (!IsEditing) return;
        CancelAppearancePreview();
        if (focusStage) Stage.Focus();
        IsEditing = false;
        UpdateAccessoryPreviewMode();
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
        SettingsShell.Visibility = Visibility.Visible;
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
            if (IsSettingsOpen) SettingsPage.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void ShowHomePage()
    {
        if (IsInPorch) LeavePorch();
        IsSettingsOpen = false;
        SettingsPage.Visibility = Visibility.Collapsed;
        SettingsShell.Visibility = Visibility.Collapsed;
        HomeContent.Visibility = Visibility.Visible;
        HomeFooter.Visibility = Visibility.Visible;
        settingsCommandError = null;
        SettingsFeedbackBar.Visibility = Visibility.Collapsed;
        UpdateNavigationSelection();
        Stage.Focus();
        RefreshLiveSceneOwnership();
        UpdateRoomAwayPresentation();
    }

    public void RestoreKeyboardFocus() => TryRestoreControlFocus();

    internal bool TryRestoreControlFocus()
    {
        if (Window.GetWindow(this) is not { IsActive: true, IsVisible: true, IsEnabled: true,
            WindowState: not WindowState.Minimized }) return false;
        if (!IsVisible || !IsEnabled || IsRoomDialogOpen || beltWindowMenuOpen || Mouse.Captured is not null)
            return false;
        if (IsSettingsOpen)
        {
            if (IsBeltTextEntry(Keyboard.FocusedElement)) return false;
            return SettingsPage.Focus();
        }
        if (Keyboard.FocusedElement is not Button && IsAppearanceInputFocused())
            return false;
        return Stage.Focus();
    }

    public void UpdateSettings(ProductSettingsState state)
    {
        settingsState = state;
        SettingsSceneName.Text = state.Scene.Name;
        SettingRunState.Text = state.Scene.Status;
        SettingsSceneError.Text = state.Scene.Error ?? string.Empty;
        SettingsSceneError.Visibility = string.IsNullOrEmpty(state.Scene.Error) ? Visibility.Collapsed : Visibility.Visible;
        RestoreRoomDisplayButton.IsEnabled = state.Scene.CanRestoreDisplay;
        RestoreRoomDisplayButton.Visibility = state.Scene.CanRestoreDisplay ? Visibility.Visible : Visibility.Collapsed;
        ReinitializeSceneButton.IsEnabled = state.Scene.CanReinitialize;
        ReinitializeSceneButton.Visibility = state.Scene.CanReinitialize ? Visibility.Visible : Visibility.Collapsed;
        SceneRecoveryActions.Visibility = state.Scene.CanRestoreDisplay || state.Scene.CanReinitialize ? Visibility.Visible : Visibility.Collapsed;
        SaveWarningRow.Visibility = state.HasSaveWarning ? Visibility.Visible : Visibility.Collapsed;
        RetrySaveSettingsButton.IsEnabled = !state.IsWriteProtected;
        RecoverSettingsButton.Visibility = state.IsWriteProtected ? Visibility.Visible : Visibility.Collapsed;
        ShowDiagnosticsButton.IsEnabled = SettingsExitButton.IsEnabled = true;
        UpdateSettingsFeedback();
    }

    public void ShowSettingsStatus(string text, bool failed)
    {
        settingsCommandError = failed ? text : null;
        UpdateSettingsFeedback();
    }

    public void SetDiagnosticsText(string text) => SettingsDiagnosticsText.Text = text;
    private void OnSettingsDiagnosticsWheel(object sender, MouseWheelEventArgs e) => SettingsPage.ScrollFromChild(e);

    private void UpdateSettingsFeedback()
    {
        if (!IsSettingsOpen)
        {
            SettingsFeedbackBar.Visibility = Visibility.Collapsed;
            return;
        }

        var warning = settingsState?.HasSaveWarning == true ? settingsState.SaveMessage : null;
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
    private void OnRestoreRoomDisplayClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RestoreRoomDisplay);
    private void OnReinitializeSceneClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.ReinitializeScene);
    private void OnLandscape1080pSettingsClick(object sender, RoutedEventArgs e) => RequestWindowPreset(ControlCenterWindowPreset.Landscape1080p);
    private void OnLandscape1440pSettingsClick(object sender, RoutedEventArgs e) => RequestWindowPreset(ControlCenterWindowPreset.Landscape1440p);
    private void OnPortraitSettingsClick(object sender, RoutedEventArgs e) => RequestWindowPreset(ControlCenterWindowPreset.Portrait);

    private void RequestWindowPreset(ControlCenterWindowPreset preset)
    {
        SetWindowPreset(preset);
        WindowPresetRequested?.Invoke(preset);
    }
    private void OnRetrySaveClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RetrySave);
    private void OnRecoverSettingsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.RecoverSettings);
    private void OnExitSettingsClick(object sender, RoutedEventArgs e) => Request(ProductSettingsAction.Exit);
    private void OnShowDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (SettingsDiagnosticsText.Visibility == Visibility.Visible)
        {
            SettingsDiagnosticsText.Visibility = Visibility.Collapsed;
            ShowDiagnosticsLabel.Text = "查看诊断";
            return;
        }
        OpenDiagnostics();
    }

    private void OpenDiagnostics()
    {
        SettingsDiagnosticsText.Visibility = Visibility.Visible;
        ShowDiagnosticsLabel.Text = "收起诊断";
        Request(ProductSettingsAction.RefreshDiagnostics);
        Dispatcher.BeginInvoke(() =>
        {
            if (IsSettingsOpen && SettingsDiagnosticsText.Visibility == Visibility.Visible)
                SettingsDiagnosticsText.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnFaceClick(object sender, RoutedEventArgs e) => ShowEditor(HomeEditor.Face);
    private void OnWardrobeClick(object sender, RoutedEventArgs e) => ShowEditor(HomeEditor.Wardrobe);
    private void OnEnterRoomClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditing && !IsSettingsOpen) EnterPorch();
    }
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
    private string? noticeText, noticeDetail;
    private readonly Dictionary<Guid, string> roomCheckpointWarnings = [];
    public void SetNotice(string? text, string? detail)
    {
        noticeText = text; noticeDetail = detail;
        UpdateNotice();
    }
    internal void SetRoomCheckpointSaveStatus(Guid roomId, string? warning)
    {
        if (warning is null) roomCheckpointWarnings.Remove(roomId);
        else roomCheckpointWarnings[roomId] = warning;
        UpdateNotice();
    }
    private void UpdateNotice()
    {
        var checkpointDetail = roomCheckpointWarnings.Count == 0 ? null : string.Join("\n", roomCheckpointWarnings.Values);
        var text = string.IsNullOrEmpty(noticeText) && checkpointDetail is not null ? "位置／视角未保存 · 查看" : noticeText;
        NoticeButton.Content = text;
        NoticeButton.ToolTip = checkpointDetail is null ? noticeDetail : string.Join("\n", new[] { noticeDetail, checkpointDetail }.Where(value => !string.IsNullOrEmpty(value)));
        NoticeButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (HandleBeltKey(e, true)) return;
        var key = NavigationKey(e);
        if (IsRoomDialogOpen)
        {
            ClearLiveDirectionalInput();
            if (e.Key == Key.Escape) { if (!e.IsRepeat) CloseRoomDialog(); e.Handled = true; }
            return;
        }
        if (IsAccessoryPreview && IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
        {
            if (!e.IsRepeat) CloseEditor();
            e.Handled = true;
            return;
        }
        if (!IsKeyboardFocusWithin || Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox or PasswordBox)
            return;
        if (IsInPorch && PorchScene.IsCatalogEntryFocused && key is (Key.A or Key.D or Key.S or Key.W or Key.E or Key.F)) Stage.Focus();
        if (IsInPorch && IsAppearanceInputFocused())
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
        if (TryHandleNavigationKey(key, e.IsRepeat))
        {
            e.Handled = true;
            return;
        }
        if (IsAccessoryPreview && !IsSettingsOpen && !IsDangoAway)
        {
            e.Handled = SetLiveDirectionalKey(key, true, e.IsRepeat);
            return;
        }
        if (IsSettingsOpen || IsDangoAway || IsAppearanceInputFocused())
            return;
        if (IsEditing && e.Key is not (Key.S or Key.Space)) return;
        e.Handled = SetLiveDirectionalKey(key, true, e.IsRepeat);
    }

    private static Key NavigationKey(System.Windows.Input.KeyEventArgs e) =>
        e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;

    internal bool TryHandleNavigationKey(Key key, bool repeat)
    {
        if (IsSettingsOpen || IsDangoAway || IsRoomDialogOpen || beltWindowMenuOpen ||
            Keyboard.Modifiers != ModifierKeys.None || IsAppearanceInputFocused()) return false;
        if (IsInPorch)
        {
            if (key is not (Key.F or Key.E)) return false;
            if (!repeat)
            {
                if (key == Key.F) PorchScene.InteractNearestDoor();
                else PorchScene.RequestNearbyFurniture();
            }
            return true;
        }
        if (key is Key.Q or Key.E)
        {
            if (!repeat) ShowEditor(key == Key.Q ? HomeEditor.Wardrobe : HomeEditor.Face);
            return true;
        }
        if (key != Key.F || IsEditing) return false;
        if (!repeat) EnterPorch();
        return true;
    }

    private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (HandleBeltKey(e, false)) return;
        if (IsRoomDialogOpen) { ClearLiveDirectionalInput(); return; }
        if (IsAccessoryPreview)
        {
            e.Handled = SetLiveDirectionalKey(e.Key, false, false);
            return;
        }
        if (IsSettingsOpen || IsDangoAway || IsAppearanceInputFocused()) { ClearLiveDirectionalInput(); return; }
        e.Handled = SetLiveDirectionalKey(NavigationKey(e), false, false);
    }

    private void OnBackFromPorchClick(object sender, RoutedEventArgs e) => LeavePorch();

    private void EnterPorch()
    {
        if (IsInPorch || IsDangoAway) return;
        ClearLiveDirectionalInput();
        if (IsEditing) CloseEditor(focusStage: false);
        CancelAppearancePreview();
        IsInPorch = true;
        if (beltRuntime is not null) EnterBeltPorch();
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
        if (beltRuntime is not null) LeaveBeltPorch();
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
        CancelAppearancePreview();
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
        Dango.Desktop.Resources.DesktopTheme.ApplyPalette(Resources, contrast);
        Background = (System.Windows.Media.Brush)Resources["HomeBackground"];
        Foreground = (System.Windows.Media.Brush)Resources["HomeInk"];
        Resources["SettingsScrollGrip"] = contrast ? Foreground : Brush("#B7A28E");
        PorchScene.InvalidateVisual();
        UpdateLauncherSelection();
        RefreshAppearanceControls();
        UpdatePresetSelection();
        if (settingsState is not null) UpdateSettings(settingsState);
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face || sender is not Button { Tag: string color }) return;
        var value = Dango.Desktop.Rendering.Visuals.OpaqueSrgbColor.Parse(color);
        StageAppearance(DraftAppearance.WithBodyColor(value));
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
        if (beltRuntime is not null && portrait) Stage.Margin = new Thickness(4, 4, 4, 8);
        minimumStageHeight = compactPortrait ? 520 : portrait ? 560 : shortLandscape ? 260 : compactLandscape ? 330 : ActualWidth >= 1150 ? 430 : 340;
        GapColumn.Width = new GridLength(portrait ? 0 : compactLandscape ? 18 : 36);
        var previewWidth = BasePreviewWidth;
        var previewHeight = previewWidth * .75;
        PetPreview.Width = previewWidth;
        PetPreview.Height = previewHeight;
        PetVisualArea.Height = previewHeight + ShadowHeight;
        PetScale.ScaleX = PetScale.ScaleY = 1;
        Launchers.Margin = new Thickness(0);
        WardrobeButton.Margin = new Thickness(0);
        foreach (var button in new[] { FaceButton, WardrobeButton, EnterRoomButton })
        {
            button.MinWidth = button.Width = veryNarrow ? 66 : 76;
            button.MinHeight = 28;
            button.Height = 28;
            button.Padding = new Thickness(veryNarrow ? 6 : 10, 2, veryNarrow ? 6 : 10, 2);
        }
        foreach (var button in new[] { CreamButton, PeachButton, GreenButton })
            button.Margin = new Thickness(0, 0, portrait ? 10 : 8, 0);
        Colors.Margin = new Thickness(0, 0, 0, portrait ? 16 : 24);
        EditorPanel.MinHeight = portrait ? 290 : shortLandscape ? 245 : compactLandscape ? 300 : 340;
        EditorPanel.Margin = portrait ? new Thickness(12, 4, 12, 8)
            : shortLandscape ? new Thickness(4) : new Thickness(4, 8, 4, 8);
        EditorPanel.VerticalAlignment = portrait
            || shortLandscape ? System.Windows.VerticalAlignment.Stretch : System.Windows.VerticalAlignment.Center;
        PorchContent.Margin = new Thickness(0);
        PorchContent.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        OptionsBody.Margin = shortLandscape ? new Thickness(0, 8, 0, 8) : portrait
            ? new Thickness(0, 14, 0, 12) : new Thickness(0, 26, 0, 20);
        HomeFooter.Margin = new Thickness(portrait ? 16 : 22, 0, portrait ? 16 : 22, 0);
        SettingsInner.Margin = portrait ? new Thickness(16, 8, 16, 20)
            : compactLandscape ? new Thickness(18, 8, 18, 20) : new Thickness(38, 10, 38, 24);
        SettingsHeader.Margin = SettingsFooter.Margin = new Thickness(SettingsInner.Margin.Left, 6, SettingsInner.Margin.Right + 16, 8);
        SettingsFeedbackBar.Margin = portrait ? new Thickness(16, 0, 16, 0)
            : compactLandscape ? new Thickness(18, 0, 18, 0) : new Thickness(38, 0, 38, 0);
        UpdateStageSizing();
        UpdateTransition(false);
    }

    private void UpdateStageSizing()
    {
        if (Stage is null || HomeContent is null) return;
        var available = Math.Max(0, HomeContent.ActualHeight - Stage.Margin.Top - Stage.Margin.Bottom);
        var previewFits = available >= BasePreviewWidth * .75 + PreviewBottomSpace + 8;
        if (!IsPortraitLayout && !IsInPorch &&
            (previewFits || IsEditing && SelectedEditor == HomeEditor.Face))
        {
            // Home and landscape editors share the visible stage. Parameters scroll
            // inside their own viewport; preview actions stay inside this fixed height.
            // Keep the existing overflow fallback when a genuinely short home cannot fit.
            Stage.MinHeight = 0;
            if (double.IsNaN(Stage.Height) || Math.Abs(Stage.Height - available) > .5) Stage.Height = available;
            return;
        }
        var commonHeight = CommonPreviewTop + BasePreviewWidth * .75 + HomeLauncherSpace;
        Stage.MinHeight = Math.Max(minimumStageHeight, IsPortraitLayout ? PortraitPreviewHeight : commonHeight);
        if (IsEditing && !IsPortraitLayout)
            Stage.MinHeight = Math.Max(Stage.MinHeight, CommonPreviewTop + BasePreviewWidth * .75 + ShadowHeight +
                EditorSaveActionsHeight + 16);
        var height = Math.Max(Stage.MinHeight, available);
        if (double.IsNaN(Stage.Height) || Math.Abs(Stage.Height - height) > .5) Stage.Height = height;
    }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTransition(false);

    private void OnFacePreviewSlotSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face) return;
        UpdateStageSizing();
        UpdateTransition(true);
    }

    private double BasePreviewWidth => IsPortraitLayout
        ? Math.Min(Math.Clamp(260 + (ActualHeight - 600) * .2, 250, 300), Math.Max(210, ActualWidth * .625))
        : ActualWidth < 800 ? 180 : ActualWidth >= 1150 ? 420 : 320;

    private double ShadowHeight => BasePreviewWidth * .045;
    private double HomeLauncherSpace => ShadowHeight + 4 + 60;
    private double EditorSaveActionsHeight => SelectedEditor == HomeEditor.Face
        ? FaceEditorSaveActions.DesiredSize.Height : AccessorySaveActionsSlot.DesiredSize.Height;
    // Reserve the same space in all three pages so switching the selected editor
    // cannot change the preview baseline when its footer has a different height.
    private double PreviewBottomSpace => Math.Max(HomeLauncherSpace,
        ShadowHeight + Math.Max(FaceEditorSaveActions.DesiredSize.Height,
            AccessorySaveActionsSlot.DesiredSize.Height) + 16);
    private double PortraitPreviewHeight => CommonPreviewTop + BasePreviewWidth * .75 + ShadowHeight +
        (IsEditing ? Math.Max(64, EditorSaveActionsHeight + 20) : 64);
    // Use the visible home area, not the editor's variable content height. The
    // projected native jump can rise 42 canvas units. Keep a further 20 DIP
    // above that arc; the native jump gate also measures the rendered hats.
    private double CommonPreviewTop
    {
        get
        {
            var safeTop = 20 + BasePreviewWidth / 160 * 42;
            var available = HomeContent.ActualHeight - Stage.Margin.Top - Stage.Margin.Bottom;
            // Lower the resting Dango a little further wherever the actual
            // viewport still fits both entry rows; short views keep headroom.
            var lowerBy = Math.Clamp(available - safeTop - BasePreviewWidth * .75 - HomeLauncherSpace, 0, 8);
            var preferred = Math.Max(safeTop + lowerBy, IsPortraitLayout ? 0 :
                (Math.Max(minimumStageHeight, available) - BasePreviewWidth * .75 - HomeLauncherSpace) / 2);
            if (!IsPortraitLayout && !IsInPorch)
                return Math.Max(8, Math.Min(preferred, available - BasePreviewWidth * .75 - PreviewBottomSpace));
            return preferred;
        }
    }

    private double EditorRestingOffset(HomeEditor editor) =>
        (editor == HomeEditor.Face ? 1 : -1) * (IsPortraitLayout ? 120 : 46);

    private void UpdateTransition(bool animate)
    {
        if (EditorPanel is null) return;
        // Hidden tools still measure. Entering/leaving face mode must refresh the
        // stage even when the preview slot itself does not raise SizeChanged.
        UpdateStageSizing();
        var state = (IsEditing, SelectedEditor, IsInPorch, IsDangoAway);
        var sameState = transitionState == state;
        if (!sameState) { transitionState = state; transitionVersion++; }
        var version = transitionVersion;
        var motionEnabled = IsLoaded && !ReduceMotion && SystemParameters.ClientAreaAnimation;
        var preserveAnimation = sameState && motionEnabled;
        var width = Math.Max(0, Stage.ActualWidth);
        var height = Math.Max(0, Stage.ActualHeight);
        var portrait = IsPortraitLayout;
        var compactLandscape = !portrait && ActualWidth < 800;
        var shortLandscape = !portrait && ActualHeight <= 400;
        var faceEditing = IsEditing && SelectedEditor == HomeEditor.Face;
        var accessoryEditing = IsEditing && SelectedEditor == HomeEditor.Wardrobe;
        var panelOnLeft = SelectedEditor == HomeEditor.Wardrobe &&
            (IsEditing || EditorPanel.Visibility == Visibility.Visible && !IsInPorch);
        var panelShare = compactLandscape ? SelectedEditor == HomeEditor.Face && ActualWidth < 600 ? 33 : 50 : 43;
        var petShare = 100 - panelShare;
        LeftColumn.Width = new GridLength(portrait ? 1 : panelOnLeft ? panelShare : petShare, GridUnitType.Star);
        RightColumn.Width = new GridLength(portrait ? 0 : panelOnLeft ? petShare : panelShare, GridUnitType.Star);
        PresenceStageRow.Height = portrait
            ? new GridLength(PortraitPreviewHeight) : new GridLength(1, GridUnitType.Star);
        DetailStageRow.Height = portrait ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(EditorPanel, portrait ? 1 : 0);
        Grid.SetColumn(EditorPanel, portrait ? 0 : panelOnLeft ? 0 : 2);
        Grid.SetColumnSpan(EditorPanel, portrait ? 3 : 1);
        EditorHeading.Visibility = EditorTitle.Visibility = Visibility.Collapsed;
        EditorHeadingRow.Height = new GridLength(0);
        OptionsBody.Margin = new Thickness(0);
        EditorPanel.MinHeight = portrait || faceEditing || accessoryEditing ? 0 : shortLandscape ? 245 : compactLandscape ? 300 : 340;
        EditorPanel.Margin = portrait ? new Thickness(12, 4, 12, 8)
            : shortLandscape ? new Thickness(4) : new Thickness(4, 8, 4, 8);
        Grid.SetColumn(FaceEditorTools, 0);
        Grid.SetColumnSpan(FaceEditorTools, portrait ? 3 : 1);
        Grid.SetRowSpan(FaceEditorTools, portrait ? 1 : 2);
        FaceEditorTools.Margin = portrait ? new Thickness(12, 4, 12, 8) : EditorPanel.Margin;
        if (beltRuntime is not null && portrait) FaceEditorTools.Margin = new Thickness(2, 4, 2, 8);
        FaceEditorTools.VerticalAlignment = portrait ? System.Windows.VerticalAlignment.Top : System.Windows.VerticalAlignment.Center;
        FaceEditorPreviewRegion.Width = BasePreviewWidth + 76;
        FacePreviewRow.Height = new GridLength(1, GridUnitType.Star);
        FaceParameterSpacerRow.Height = new GridLength(0);
        FaceEditorSaveActions.Width = portrait ? BasePreviewWidth : double.NaN;
        FaceEditorSaveActions.HorizontalAlignment = portrait ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch;
        Grid.SetColumn(AccessoryEditorTools, portrait ? 0 : 2);
        Grid.SetColumnSpan(AccessoryEditorTools, portrait ? 3 : 1);
        Grid.SetRowSpan(AccessoryEditorTools, portrait ? 1 : 2);
        AccessoryEditorTools.Margin = FaceEditorTools.Margin;
        AccessoryEditorTools.VerticalAlignment = FaceEditorTools.VerticalAlignment;
        AccessoryEditorPreviewRegion.Width = BasePreviewWidth + 76;
        AccessoryPreviewSlot.Width = BasePreviewWidth;
        AccessorySaveActionsSlot.Width = portrait ? BasePreviewWidth : double.NaN;
        AccessorySaveActionsSlot.HorizontalAlignment = portrait ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch;
        Grid.SetRow(PorchContent, 0);
        Grid.SetColumn(PorchContent, 0);
        Grid.SetColumnSpan(PorchContent, 3);
        var gap = GapColumn.Width.Value;
        var available = Math.Max(0, width - gap);
        var left = available * LeftColumn.Width.Value / (LeftColumn.Width.Value + RightColumn.Width.Value);
        var right = available - left;
        var previewWidth = BasePreviewWidth;
        PetPreview.Width = previewWidth;
        PetPreview.Height = previewWidth * .75;
        // The shadow needs an actual layout slot. Drawing below a fixed-height
        // grid is clipped by WPF even when its ActualSize remains nonzero.
        PetVisualArea.Height = PetPreview.Height + ShadowHeight;
        PetShadow.Width = previewWidth * .62;
        PetShadow.Height = ShadowHeight;
        PetShadow.Margin = new Thickness(0, PetPreview.Height, 0, 0);
        Presence.Width = portrait ? Math.Min(width, Math.Max(250, PetPreview.Width + 16))
            : Math.Min(Math.Max(210, PetPreview.Width + 18), IsEditing ? panelOnLeft ? right : left : width);
        Presence.Margin = new Thickness(0, CommonPreviewTop, 0, 0);
        var offsetX = portrait ? 0 : IsEditing ? panelOnLeft ? (width - right) / 2 : -(width - left) / 2
            : IsInPorch ? -48 : 0;
        const double offsetY = 0;
        animate &= motionEnabled;
        UpdateLauncherSelection();
        var horizontalPanelHeight = Math.Max(EditorPanel.MinHeight,
            IsEditing ? height - EditorPanel.Margin.Top - EditorPanel.Margin.Bottom
                : Math.Min(420, height - EditorPanel.Margin.Top - EditorPanel.Margin.Bottom));
        EditorPanel.Height = portrait ? double.NaN : horizontalPanelHeight;
        FaceEditorTools.Height = portrait ? Math.Max(0, PresenceStageRow.Height.Value -
            FaceEditorTools.Margin.Top - FaceEditorTools.Margin.Bottom) : horizontalPanelHeight;
        AccessoryEditorTools.Height = FaceEditorTools.Height;
        PorchContent.Height = double.NaN;
        Presence.IsHitTestVisible = Presence.IsEnabled = !IsInPorch;
        UpdateHomeEntryPositions(width, previewWidth);
        Launchers.IsHitTestVisible = !IsInPorch && !IsEditing;
        Launchers.Visibility = IsEditing ? Visibility.Collapsed : Visibility.Visible;
        EnterRoomButton.Visibility = IsEditing ? Visibility.Collapsed : Visibility.Visible;
        EnterRoomButton.IsEnabled = !IsEditing && !IsInPorch;
        Brand.Visibility = IsEditing || IsInPorch ? Visibility.Collapsed : Visibility.Visible;
        FaceBrandIcon.Visibility = faceEditing ? Visibility.Visible : Visibility.Collapsed;
        AccessoryBrandIcon.Visibility = accessoryEditing ? Visibility.Visible : Visibility.Collapsed;
        if (BoundRuntime is null) PetShadow.Visibility = IsInPorch ? Visibility.Collapsed : Visibility.Visible;
        PorchBrand.Visibility = IsInPorch ? Visibility.Visible : Visibility.Collapsed;
        TopNavigation.IsHitTestVisible = !IsInPorch;
        HomeFooter.IsHitTestVisible = !IsInPorch;
        Launchers.IsEnabled = !IsInPorch && !IsEditing;
        TopNavigation.IsEnabled = HomeFooter.IsEnabled = !IsInPorch;
        EditorPanel.IsEnabled = EditorPanel.IsHitTestVisible = IsEditing;
        FaceEditorTools.IsEnabled = FaceEditorTools.IsHitTestVisible = faceEditing;
        AccessoryEditorTools.IsEnabled = AccessoryEditorTools.IsHitTestVisible = accessoryEditing;
        if (faceEditing) FaceEditorTools.Visibility = Visibility.Visible;
        if (accessoryEditing) AccessoryEditorTools.Visibility = Visibility.Visible;
        if (IsEditing) EditorPanel.Visibility = Visibility.Visible;
        PorchContent.IsEnabled = IsInPorch;
        PorchContent.IsHitTestVisible = IsInPorch;
        if (IsInPorch) PorchContent.Visibility = Visibility.Visible;
        // Content measurement can raise SizeChanged before the first painted
        // frame. Keep clocks whose target did not change, including completion
        // callbacks for the same semantic state.
        Animate(PresenceTranslation, TranslateTransform.XProperty, offsetX, animate, preserveAnimation);
        Animate(PresenceTranslation, TranslateTransform.YProperty, offsetY, animate, preserveAnimation);
        Animate(Presence, OpacityProperty, IsInPorch ? 0 : 1, animate, preserveAnimation);
        Animate(PanelTranslation, TranslateTransform.XProperty, IsEditing ? 0 : EditorRestingOffset(SelectedEditor), animate, preserveAnimation);
        Animate(PorchTranslation, TranslateTransform.XProperty, IsInPorch ? 0 : portrait ? 120 : 46, animate, preserveAnimation);
        Animate(Launchers, OpacityProperty, IsInPorch ? 0 : 1, animate, preserveAnimation);
        Animate(TopNavigation, OpacityProperty, IsInPorch ? 0 : 1, animate, preserveAnimation);
        Animate(HomeFooter, OpacityProperty, IsInPorch ? 0 : 1, animate, preserveAnimation);
        Animate(EditorPanel, OpacityProperty, IsEditing ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !IsEditing && !IsDangoAway) EditorPanel.Visibility = Visibility.Hidden;
        }, preserveAnimation);
        Animate(FaceEditorTools, OpacityProperty, faceEditing ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !faceEditing && !IsDangoAway) FaceEditorTools.Visibility = Visibility.Hidden;
        }, preserveAnimation);
        Animate(AccessoryEditorTools, OpacityProperty, accessoryEditing ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !accessoryEditing && !IsDangoAway) AccessoryEditorTools.Visibility = Visibility.Hidden;
        }, preserveAnimation);
        Animate(PorchContent, OpacityProperty, IsInPorch ? 1 : 0, animate, () =>
        {
            if (version == transitionVersion && !IsInPorch && !IsDangoAway) PorchContent.Visibility = Visibility.Hidden;
        }, preserveAnimation);
        UpdateRoomAwayPresentation();
    }

    private void UpdateHomeEntryPositions(double width, double previewWidth)
    {
        var center = width / 2;
        Launchers.Height = CommonPreviewTop + previewWidth * .75 + ShadowHeight;
        Canvas.SetLeft(EnterRoomButton, center - EnterRoomButton.Width / 2);
        Canvas.SetTop(EnterRoomButton, 8);
        // Narrow portraits can use the empty sides of the preview rectangle;
        // preserve the Dango size and keep both hit targets inside the stage.
        var side = Math.Min(previewWidth / 2 + 8, Math.Max(0, center - FaceButton.Width));
        var top = CommonPreviewTop + previewWidth * .75 * .72 - FaceButton.Height / 2;
        Canvas.SetLeft(FaceButton, center + side);
        Canvas.SetLeft(WardrobeButton, center - side - WardrobeButton.Width);
        Canvas.SetTop(FaceButton, top);
        Canvas.SetTop(WardrobeButton, top);
    }

    private static void Animate(Animatable target, DependencyProperty property, double destination, bool animate, bool preserveAnimation)
    {
        if (preserveAnimation && target.HasAnimatedProperties &&
            (double)target.GetAnimationBaseValue(property) == destination) return;
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, destination);
        if (animate) target.BeginAnimation(property, CreateAnimation(from, destination), HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(UIElement target, DependencyProperty property, double destination, bool animate, bool preserveAnimation) =>
        Animate(target, property, destination, animate, static () => { }, preserveAnimation);

    private static void Animate(UIElement target, DependencyProperty property, double destination, bool animate, Action completed, bool preserveAnimation)
    {
        if (preserveAnimation && target.HasAnimatedProperties &&
            (double)target.GetAnimationBaseValue(property) == destination) return;
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
