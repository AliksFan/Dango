using Dango.Core.Animation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private AppearanceSettingsSession? appearanceSession;
    private DangoAppearanceConfiguration standaloneDraft = DangoAppearanceConfiguration.Default;
    private bool updatingAppearance;
    private long appearanceGeneration;
    private bool appearanceSleeping;
    private AttachmentChannel appearanceComparisonChannels;
    internal DangoAppearanceConfiguration DraftAppearance => appearanceSession?.DraftAppearance ?? standaloneDraft;
    internal Func<OpaqueSrgbColor, OpaqueSrgbColor?> AppearanceColorPicker { get; set; }
    internal AppearanceSettingsSession? BoundAppearance => appearanceSession;

    internal void BindAppearance(AppearanceSettingsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (appearanceSession is not null) throw new InvalidOperationException("外观会话已绑定。");
        appearanceSession = session;
        BoundRuntime?.SetAppearance(session.DraftAppearance);
        RefreshAppearanceControls();
    }

    internal void CancelAppearancePreview()
    {
        CancelAccessoryPreview();
        appearanceGeneration++;
        ClearAppearanceComparison();
        appearanceSession?.CancelPreview();
        standaloneDraft = BoundRuntime?.EffectiveAppearance ?? DangoAppearanceConfiguration.Default;
        if (appearanceSession is not null) BoundRuntime?.SetAppearance(appearanceSession.SavedAppearance);
        else standaloneDraft = DangoAppearanceConfiguration.Default;
        RestoreBeltBodyDraft();
        RefreshAppearanceControls();
    }

    private void StageAppearance(DangoAppearanceConfiguration draft)
    {
        var previousBody = DraftAppearance.Body;
        if (appearanceSession is not null) appearanceSession.StagePreview(draft);
        else standaloneDraft = draft;
        BoundRuntime?.SetAppearance(draft);
        UpdateBeltAppearance(previousBody != draft.Body);
        RefreshAppearanceControls();
    }

    private void RefreshAppearanceControls()
    {
        if (EyeSpacingSlider is null || AppearanceStatus is null) return;
        updatingAppearance = true;
        try
        {
            var draft = DraftAppearance;
            PreviewColor = Color.FromRgb(draft.BodyColor.R, draft.BodyColor.G, draft.BodyColor.B);
            LiveDangoVisual.BodyFill = new SolidColorBrush(PreviewColor);
            LiveDangoVisual.EyeFill = new SolidColorBrush(Color.FromRgb(draft.EyeColor.R, draft.EyeColor.G, draft.EyeColor.B));
            BodyColorSwatch.Background = LiveDangoVisual.BodyFill;
            EyeColorSwatch.Background = LiveDangoVisual.EyeFill;
            BodyColorHex.Text = draft.BodyColor.ToHex();
            EyeColorHex.Text = draft.EyeColor.ToHex();
            EyeSizeSlider.Value = draft.EyeLayout.SizePercent;
            EyeOvalSlider.Value = draft.EyeLayout.OvalPercent;
            EyeHeightSlider.Value = draft.EyeLayout.HeightControlPercent;
            EyeSpacingSlider.Value = draft.EyeLayout.SpacingControlPercent;
            EyeSizeValue.Text = $"大小 · {draft.EyeLayout.SizePercent:0.#}%";
            var oval = draft.EyeLayout.OvalPercent;
            EyeOvalValue.Text = oval == 0 ? "椭圆度 · 圆形" : $"椭圆度 · {(oval < 0 ? "横" : "竖")} {Math.Abs(oval):0.#}";
            var height = draft.EyeLayout.HeightControlPercent - 51;
            EyeHeightValue.Text = height == 0 ? "高度 · 默认" : $"高度 · {(height < 0 ? "上移" : "下移")} {Math.Abs(height):0.#}";
            var spacing = draft.EyeLayout.SpacingControlPercent - 24.5;
            EyeSpacingValue.Text = spacing == 0 ? "眼距 · 默认" : $"眼距 · {(spacing < 0 ? "靠近" : "分开")} {Math.Abs(spacing):0.#}";
            BodySizeSlider.Value = draft.Body.SizePercent;
            BodySoftnessSlider.Value = draft.Body.Softness;
            BodyLandingBuildSlider.Value = draft.Body.LandingBuild;
            BodySizeValue.Text = $"大小 · {draft.Body.SizePercent:0.#}%";
            BodySoftnessValue.Text = $"柔软度 · {draft.Body.Softness:0.#}";
            BodyLandingBuildValue.Text = $"落地体型 · {draft.Body.LandingBuild:0.#}";
            AppearanceBodyTab.Visibility = beltRuntime is null ? Visibility.Collapsed : Visibility.Visible;
            DefaultAppearanceButton.IsEnabled = IsEditing && SelectedEditor == HomeEditor.Face;
            var canApply = IsEditing && SelectedEditor == HomeEditor.Face && appearanceSession?.HasPendingSave == true &&
                !appearanceSession.IsWriteProtected && !appearanceSession.RequiresBodyPreview && beltError is null;
            ApplyButton.IsEnabled = canApply;
            ApplyButton.Foreground = highContrast && canApply ? System.Windows.SystemColors.HighlightTextBrush
                : (System.Windows.Media.Brush)Resources["HomeInk"];
            AppearanceStatus.Text = beltError ?? (appearanceSession?.RequiresBodyPreview == true ? "身体更新中…" : appearanceSession?.StatusMessage) ?? "应用后保存到本机。";
            RecoverAppearanceButton.Visibility = appearanceSession?.IsWriteProtected == true ? Visibility.Visible : Visibility.Collapsed;
            AppearancePreviewNotice.Text = appearanceSession?.IsDirty == true ? "预览中 · 应用后保存" : "拖动即时预览 · 应用后保存";
            var compare = IsEditing && SelectedEditor == HomeEditor.Face && (beltPresenter is not null && beltError is null || BoundRuntime?.InitializationState == SceneInitializationState.Ready);
            PreviewBlinkButton.IsEnabled = PreviewSleepButton.IsEnabled = compare;
            var sleepAction = appearanceSleeping ? "醒来" : "预览睡眠";
            PreviewSleepButton.ToolTip = sleepAction;
            System.Windows.Automation.AutomationProperties.SetName(PreviewSleepButton, sleepAction);
            SleepPreviewIcon.Visibility = appearanceSleeping ? Visibility.Collapsed : Visibility.Visible;
            WakePreviewIcon.Visibility = appearanceSleeping ? Visibility.Visible : Visibility.Collapsed;
            Select(PreviewSleepButton, appearanceSleeping);
            Select(AppearanceColorsTab, AppearanceColorsPanel.Visibility == Visibility.Visible);
            Select(AppearanceEyesTab, AppearanceEyesPanel.Visibility == Visibility.Visible);
            Select(AppearanceBodyTab, AppearanceBodyPanel.Visibility == Visibility.Visible);
            UpdateColorSelection();
        }
        finally { updatingAppearance = false; }
    }

    private void OnAppearanceTabClick(object sender, RoutedEventArgs e)
    {
        var eyes = ReferenceEquals(sender, AppearanceEyesTab);
        var body = ReferenceEquals(sender, AppearanceBodyTab);
        AppearanceColorsPanel.Visibility = eyes || body ? Visibility.Collapsed : Visibility.Visible;
        AppearanceEyesPanel.Visibility = eyes ? Visibility.Visible : Visibility.Collapsed;
        AppearanceBodyPanel.Visibility = body ? Visibility.Visible : Visibility.Collapsed;
        Select(AppearanceColorsTab, !eyes && !body);
        Select(AppearanceEyesTab, eyes);
        Select(AppearanceBodyTab, body);
        OptionsBody.ScrollToTop();
    }

    private void OnEyeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingAppearance || EyeSpacingSlider is null || !IsEditing || SelectedEditor != HomeEditor.Face) return;
        var eyes = new EyeLayoutParameters(EyeSizeSlider.Value, EyeOvalSlider.Value, EyeHeightSlider.Value, EyeSpacingSlider.Value);
        StageAppearance(DraftAppearance.WithEyeLayout(eyes));
    }

    private void OnBodyColorPickerClick(object sender, RoutedEventArgs e) => PickAppearanceColor(false);
    private void OnEyeColorPickerClick(object sender, RoutedEventArgs e) => PickAppearanceColor(true);
    private void PickAppearanceColor(bool eye)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face) return;
        ClearLiveDirectionalInput();
        var generation = appearanceGeneration;
        var selected = AppearanceColorPicker(eye ? DraftAppearance.EyeColor : DraftAppearance.BodyColor);
        if (generation != appearanceGeneration || !IsEditing || SelectedEditor != HomeEditor.Face || selected is not { } color) return;
        StageAppearance(eye ? DraftAppearance.WithEyeColor(color) : DraftAppearance.WithBodyColor(color));
    }

    private OpaqueSrgbColor? ShowAppearanceColorPicker(OpaqueSrgbColor current)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        var window = Window.GetWindow(this);
        var result = window is null ? dialog.ShowDialog() : dialog.ShowDialog(new ColorDialogOwner(new WindowInteropHelper(window).Handle));
        return result == System.Windows.Forms.DialogResult.OK ? new(dialog.Color.R, dialog.Color.G, dialog.Color.B) : null;
    }
    private sealed record ColorDialogOwner(nint Handle) : System.Windows.Forms.IWin32Window;

    private void OnAppearanceBlinkClick(object sender, RoutedEventArgs e)
    {
        if (TryAppearanceComparison(Blink.Default(EyeSelection.Both)))
            appearanceComparisonChannels |= AttachmentChannel.LeftBlink | AttachmentChannel.RightBlink;
    }
    private void OnAppearanceSleepClick(object sender, RoutedEventArgs e)
    {
        var channels = AttachmentChannel.LeftEyePose | AttachmentChannel.RightEyePose;
        if (TryAppearanceComparison(appearanceSleeping ? new Release(channels) : new SetEyes(EyeSelection.Both, 0, EyeShape.Sleeping, 1)))
        {
            appearanceSleeping = !appearanceSleeping;
            appearanceComparisonChannels = appearanceSleeping ? appearanceComparisonChannels | channels : appearanceComparisonChannels & ~channels;
            RefreshAppearanceControls();
        }
    }
    private bool TryAppearanceComparison(AttachmentIntent intent)
    {
        if (beltPresenter is not null && IsEditing && SelectedEditor == HomeEditor.Face)
        {
            ClearLiveDirectionalInput();
            return beltPresenter.SubmitAttachment(intent);
        }
        if (!IsEditing || SelectedEditor != HomeEditor.Face || BoundRuntime?.InitializationState != SceneInitializationState.Ready) return false;
        ClearLiveDirectionalInput();
        try
        {
            var result = BoundRuntime.SubmitAttachment(intent);
            if (!result.Accepted) AppearanceStatus.Text = $"预览动作暂不可用（{result.Reason}）。";
            return result.Accepted;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            AppearanceStatus.Text = $"预览动作暂不可用（{error.GetType().Name}）。"; return false;
        }
    }
    private void ClearAppearanceComparison()
    {
        beltPresenter?.ClearAttachmentComparison();
        if (appearanceComparisonChannels != AttachmentChannel.None && BoundRuntime?.InitializationState == SceneInitializationState.Ready)
        {
            try { BoundRuntime.SubmitAttachment(new Release(appearanceComparisonChannels)); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { /* Runtime recovery owns this fault. */ }
        }
        appearanceComparisonChannels = AttachmentChannel.None;
        appearanceSleeping = false;
    }

    private void OnApplyAppearanceClick(object sender, RoutedEventArgs e)
    {
        appearanceSession?.TrySave();
        RefreshAppearanceControls();
    }
    private void OnRecoverAppearanceClick(object sender, RoutedEventArgs e)
    {
        appearanceSession?.TryRecoverAndSave();
        RefreshAppearanceControls();
    }
    private void OnDefaultAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (appearanceSession is not null) appearanceSession.RestoreDefault();
        StageAppearance(DangoAppearanceConfiguration.Default);
    }
    private void OnCancelAppearanceClick(object sender, RoutedEventArgs e) => CloseEditor();

    private bool IsAppearanceInputFocused()
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox or PasswordBox) return true;
        return Keyboard.FocusedElement is DependencyObject focused &&
            (focused is Slider || IsEditing && (EditorPanel.IsAncestorOf(focused) || FaceEditorTools.IsAncestorOf(focused) || AccessoryEditorTools.IsAncestorOf(focused)));
    }
}
