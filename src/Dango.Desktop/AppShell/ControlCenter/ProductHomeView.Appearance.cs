using Dango.Core.Animation;
using Dango.Desktop.Physics.Tuning;
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
    private bool bodyRestorePending;
    private string? bodyPreviewMessage;
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

    internal bool CancelAppearancePreview()
    {
        CancelAccessoryPreview();
        appearanceGeneration++;
        ClearLiveDirectionalInput();
        ClearAppearanceComparison();
        if (bodyRestorePending) return false;
        var saved = appearanceSession?.SavedAppearance ?? DangoAppearanceConfiguration.Default;
        if (BoundRuntime is { } runtime && (runtime.PhysicsBody != saved.Body || runtime.BodyPreviewError is not null) && !runtime.TryApplyBody(saved.Body))
        {
            bodyRestorePending = true;
            bodyPreviewMessage = $"已保存外观尚未恢复：{runtime.BodyPreviewError} 请点“恢复已保存外观”重试。";
            RefreshAppearanceControls();
            return false;
        }
        bodyRestorePending = false;
        bodyPreviewMessage = null;
        appearanceSession?.CancelPreview();
        standaloneDraft = saved;
        BoundRuntime?.SetAppearance(saved);
        ResetBodyPresentation();
        RefreshAppearanceControls();
        return true;
    }

    private void ResetBodyPresentation()
    {
        visualAnchor = null;
        shadowGroundCenterY = null;
        groundedVisualOffset = null;
        ResetAccessoryPresentation();
    }

    private void OnRestoreSavedBodyClick(object sender, RoutedEventArgs e)
    {
        ClearLiveDirectionalInput();
        try
        {
            if (BoundRuntime is { LastError: not null } runtime)
            {
                runtime.RegenerateDango();
                if (runtime.LastError is not null)
                    throw new InvalidOperationException(runtime.LastError);
            }
            bodyRestorePending = false;
            CancelAppearancePreview();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            bodyRestorePending = true;
            bodyPreviewMessage = $"已保存外观尚未恢复：{error.Message} 请点“恢复已保存外观”重试。";
            RefreshAppearanceControls();
        }
    }

    private void OnBodySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingAppearance || BodyLandingBuildSlider is null || !IsEditing || SelectedEditor != HomeEditor.Face) return;
        var body = new DangoBodyControls(BodySizeSlider.Value, BodySoftnessSlider.Value, BodyLandingBuildSlider.Value);
        // Body sliders stage controls only. Colors and eyes continue to preview against the active world.
        if (appearanceSession is not null) appearanceSession.StageBody(body);
        else standaloneDraft = standaloneDraft.WithBody(body);
        if (!bodyRestorePending && BoundRuntime?.BodyPreviewError is null) bodyPreviewMessage = null;
        RefreshAppearanceControls();
    }

    private void OnPreviewBodyClick(object sender, RoutedEventArgs e)
    {
        if (!IsEditing || SelectedEditor != HomeEditor.Face || bodyRestorePending) return;
        ClearLiveDirectionalInput();
        ClearAppearanceComparison();
        var body = DraftAppearance.Body;
        if (BoundRuntime is not { } runtime || !runtime.TryApplyBody(body))
        {
            bodyPreviewMessage = BoundRuntime?.BodyPreviewError ?? "实时物理场景尚未就绪，身体未预览。";
            RefreshAppearanceControls();
            return;
        }
        appearanceSession?.MarkBodyPreviewed(body, DangoBodyMaterial.ModelVersion);
        runtime.SetAppearance(DraftAppearance);
        bodyPreviewMessage = $"已预览 · 大小 {body.SizePercent:0}% · 柔软 {body.Softness:0} · 体型 {body.LandingBuild:0}";
        ResetBodyPresentation();
        RefreshAppearanceControls();
    }

    private void StageAppearance(DangoAppearanceConfiguration draft)
    {
        if (appearanceSession is not null) appearanceSession.StagePreview(draft);
        else standaloneDraft = draft;
        if (BoundRuntime is { } runtime) runtime.SetAppearance(draft.WithBody(runtime.PhysicsBody));
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
            BodySizeValue.Text = $"大小 · {draft.Body.SizePercent:0}%";
            BodySoftnessValue.Text = $"柔软度 · {draft.Body.Softness:0}";
            BodyLandingBuildValue.Text = $"落地体型 · {draft.Body.LandingBuild:0}";
            var needsBodyPreview = appearanceSession?.RequiresBodyPreview == true || BoundRuntime is { } active && active.PhysicsBody != draft.Body;
            BodyPreviewStatus.Text = bodyRestorePending || BoundRuntime?.BodyPreviewError is not null ? bodyPreviewMessage
                : needsBodyPreview ? "身体预览尚未更新" : bodyPreviewMessage ?? "滑块只修改草稿；点击后预览身体并自然下落。";
            PreviewBodyButton.IsEnabled = IsEditing && SelectedEditor == HomeEditor.Face && !bodyRestorePending;
            RestoreSavedBodyButton.Visibility = bodyPreviewMessage is not null && (bodyRestorePending || BoundRuntime?.BodyPreviewError is not null) ? Visibility.Visible : Visibility.Collapsed;
            BodyRestoreFeedback.Visibility = bodyRestorePending ? Visibility.Visible : Visibility.Collapsed;
            BodyRestoreFeedbackText.Text = bodyRestorePending ? bodyPreviewMessage : string.Empty;
            DefaultAppearanceButton.IsEnabled = IsEditing && SelectedEditor == HomeEditor.Face;
            var canApply = IsEditing && SelectedEditor == HomeEditor.Face && appearanceSession?.HasPendingSave == true && !appearanceSession.IsWriteProtected && !needsBodyPreview && !bodyRestorePending && BoundRuntime?.BodyPreviewError is null;
            ApplyButton.IsEnabled = canApply;
            ApplyButton.Foreground = highContrast && canApply ? System.Windows.SystemColors.HighlightTextBrush
                : (System.Windows.Media.Brush)Resources["HomeInk"];
            AppearanceStatus.Text = bodyRestorePending || BoundRuntime?.BodyPreviewError is not null ? bodyPreviewMessage
                : needsBodyPreview ? "身体预览尚未更新，请预览后再保存。" : bodyPreviewMessage ?? appearanceSession?.StatusMessage ?? "应用后保存到本机。";
            RecoverAppearanceButton.Visibility = appearanceSession?.IsWriteProtected == true ? Visibility.Visible : Visibility.Collapsed;
            AppearancePreviewNotice.Text = needsBodyPreview ? "身体预览尚未更新" : appearanceSession?.IsDirty == true ? "预览中 · 应用后保存" : "拖动即时预览 · 应用后保存";
            var compare = IsEditing && SelectedEditor == HomeEditor.Face && BoundRuntime?.InitializationState == SceneInitializationState.Ready;
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
        AppearanceBodyPanel.Visibility = body ? Visibility.Visible : Visibility.Collapsed;
        AppearanceColorsPanel.Visibility = eyes || body ? Visibility.Collapsed : Visibility.Visible;
        AppearanceEyesPanel.Visibility = eyes ? Visibility.Visible : Visibility.Collapsed;
        Select(AppearanceColorsTab, !eyes && !body);
        Select(AppearanceBodyTab, body);
        Select(AppearanceEyesTab, eyes);
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
        if (bodyRestorePending || BoundRuntime?.BodyPreviewError is not null) return;
        bodyPreviewMessage = null;
        if (appearanceSession?.TrySave() == true) bodyPreviewMessage = null;
        RefreshAppearanceControls();
    }
    private void OnRecoverAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (bodyRestorePending || appearanceSession?.RequiresBodyPreview == true || BoundRuntime?.BodyPreviewError is not null) return;
        if (appearanceSession?.TryRecoverAndSave() == true) bodyPreviewMessage = null;
        RefreshAppearanceControls();
    }
    private void OnDefaultAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (!bodyRestorePending && BoundRuntime?.BodyPreviewError is null) bodyPreviewMessage = null;
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
