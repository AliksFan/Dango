using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>The actual home editor owns presentation; the worker alone owns the native body.</summary>
public partial class ProductHomeView
{
    private BeltBodyRuntime? beltRuntime;
    private BeltBodyAppearancePresenter? beltPresenter;
    private BeltBodySnapshot? beltSnapshot, beltPendingDisplay;
    private IReadOnlyList<BeltBodyBoundaryPoint> beltBoundary = [];
    private BeltBodyAppearanceFrame? beltAppearanceFrame;
    private readonly HashSet<Key> beltHeld = [], beltSuppressed = [];
    private long beltPresentationTick, beltRequestedRevision = -1, beltRequestedBodyEpoch = -1;
    private long beltRenderStamp, beltLastPresentedSequence = -1;
    private bool beltVisible, beltPrimed, beltDragging, beltJump, beltCancel, beltFatalError, beltWindowMenuOpen;
    private Point beltDragTarget;
    private string? beltError;
    private string? beltDiagnosticError;
    private string? beltSettingsPath;
    private double BeltFloor => 120 + (PetShadow.Height / 2 - 1) / Math.Max(.1, PetPreview.Width / 160);
    internal BeltBodyRuntime? BoundBeltBody => beltRuntime;
    internal BeltBodyAppearancePresenter? BoundBeltPresenter => beltPresenter;
    internal BeltBodySnapshot? DisplayedBeltSnapshot => beltSnapshot;
    internal event Action<BeltBodySnapshot>? BeltBodyDisplayConfirmed;
    internal event Action<BeltBodySnapshot>? BeltBodyPoseApplied;
    internal long BeltBodyConsumedSnapshots { get; private set; }
    private readonly List<BeltBodySnapshot> beltRenderBatch = new(2);
    internal double BeltBodyPresentationSeconds { get; private set; }

    internal void BindBeltBody(string settingsPath)
    {
        if (beltRuntime is not null || BoundRuntime is not null) throw new InvalidOperationException("主页已绑定实时场景。");
        beltSettingsPath = settingsPath;
        beltRuntime = new BeltBodyRuntime(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll")) { SleepEnabled = true };
        ConfigureBeltLauncher(FaceButton, "捏脸");
        ConfigureBeltLauncher(WardrobeButton, "换装");
        ConfigureBeltLauncher(EnterRoomButton, "房间");
        InputMethod.SetIsInputMethodEnabled(Stage, false);
        OptionsBody.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = Resources["SettingsScrollBar"];
        HomeContent.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = Resources["SettingsScrollBar"];
        // Slider overrides the inheritable IME metadata; disable only the nontext gameplay controls.
        foreach (var slider in new[] { BodySizeSlider, BodySoftnessSlider, BodyLandingBuildSlider,
            EyeSizeSlider, EyeOvalSlider, EyeHeightSlider, EyeSpacingSlider })
            InputMethod.SetIsInputMethodEnabled(slider, false);
        beltPresenter = new BeltBodyAppearancePresenter(DraftAppearance, settingsPath) { GaitEnabled = true };
        LiveEntityLayer.Children.Add(beltPresenter.View);
        LiveDangoVisual.Visibility = LiveDangoHat.Visibility = Visibility.Collapsed;
        PetPreview.ToolTip = "拖动团子 · A/D 移动 · W 拉伸 · S 下压 · Space 跳跃";
        LiveSceneStatusText.Text = "新身体启动中…";
        SetNotice("新身体接入验收", appearanceSession?.StartupWarning ?? "单人首页／捏脸 · 已保存配饰跟随；房间接入为后续批次。");
        PetPreview.PreviewMouseLeftButtonDown += OnBeltPointerDown;
        PetPreview.MouseMove += OnBeltPointerMove;
        PetPreview.MouseLeftButtonUp += OnBeltPointerUp;
        PetPreview.LostMouseCapture += OnBeltLostCapture;
        CompositionTarget.Rendering += OnBeltRendering;
        appearanceSession?.RequestBodyApplication();
        UpdateBeltAppearance(true);
        RefreshAppearanceControls();
        RefreshBeltVisibility();
    }

    internal void UnbindBeltBody()
    {
        if (beltRuntime is null) return;
        CompositionTarget.Rendering -= OnBeltRendering;
        ClearBeltInput();
        PetPreview.PreviewMouseLeftButtonDown -= OnBeltPointerDown;
        PetPreview.MouseMove -= OnBeltPointerMove;
        PetPreview.MouseLeftButtonUp -= OnBeltPointerUp;
        PetPreview.LostMouseCapture -= OnBeltLostCapture;
        // Native calls are finite; disposal is performed by the owner after its in-flight call.
        beltRuntime.CloseAsync().GetAwaiter().GetResult();
        beltRuntime = null;
        if (beltPresenter is not null) LiveEntityLayer.Children.Remove(beltPresenter.View);
        beltPresenter?.Dispose();
        beltPresenter = null;
        beltVisible = false;
        beltSnapshot = beltPendingDisplay = null;
        beltAppearanceFrame = null;
        beltLastPresentedSequence = -1;
    }

    internal void RecoverBeltBody()
    {
        if (beltSettingsPath is not { } path) return;
        UnbindBeltBody();
        beltError = null;
        beltFatalError = false;
        beltDiagnosticError = null;
        beltRequestedRevision = beltRequestedBodyEpoch = -1;
        BindBeltBody(path);
    }

    internal ProductSettingsSceneSnapshot ReadBeltBodySettingsScene()
    {
        var m = beltRuntime?.Metrics;
        return new("新身体 · 单人捏脸", beltError is not null ? "需要恢复" : m?.Sleeping == true ? "身体休眠 · 外观继续显示" : beltVisible ? "正在运行" : "已暂停",
            beltError, CanReinitialize: beltError is not null,
            Diagnostics: $"模型：{DangoBeltWorld.Model}\n标定：{DangoBeltWorld.TuningVersion}\n物理 tick：{beltSnapshot?.Tick}\n配置版本：{beltSnapshot?.ConfigurationRevision}\n身体代际：{beltSnapshot?.BodyEpoch}\n工作线程：{m}\n外观：{beltPresenter?.Status}\n错误：{beltDiagnosticError}");
    }

    private async void ObserveBeltOperation(Task operation)
    {
        var origin = beltRuntime;
        var revision = beltRequestedRevision;
        var epoch = beltRequestedBodyEpoch;
        try { await operation; }
        catch (OperationCanceledException) { /* A newer complete draft superseded this request before its next physics step. */ }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or ObjectDisposedException)
        {
            // Binding can start before a window installs a DispatcherSynchronizationContext.
            // Revalidate ownership on the UI thread, rather than relying on await's context.
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (beltRuntime is null || !ReferenceEquals(origin, beltRuntime) || revision != beltRequestedRevision || epoch != beltRequestedBodyEpoch) return;
                    beltDiagnosticError = error.ToString();
                    beltError = "身体参数暂未生效，请调整重试或在设置中重新初始化。";
                    LiveSceneStatusText.Text = beltError;
                    LiveSceneStatusText.Visibility = Visibility.Visible;
                    ClearBeltInput();
                    RefreshAppearanceControls();
                });
        }
    }

    private void UpdateBeltAppearance(bool bodyChanged)
    {
        if (beltRuntime is null || beltPresenter is null || appearanceSession is null || beltFatalError) return;
        beltPresenter.SetAppearance(DraftAppearance);
        // The session, rather than a color/eye callback, determines whether native work is required.
        if (!bodyChanged && beltRequestedRevision == appearanceSession.BodyConfigurationRevision && beltRequestedBodyEpoch == appearanceSession.BodyEpoch) return;
        if (beltRequestedRevision == appearanceSession.BodyConfigurationRevision && beltRequestedBodyEpoch == appearanceSession.BodyEpoch) return;
        beltRequestedRevision = appearanceSession.BodyConfigurationRevision;
        beltRequestedBodyEpoch = appearanceSession.BodyEpoch;
        ObserveBeltOperation(ConfigureBeltDraftAsync(beltRuntime, DraftAppearance.Body, beltRequestedRevision, beltRequestedBodyEpoch));
    }

    private async Task ConfigureBeltDraftAsync(BeltBodyRuntime runtime, DangoBodyControls body, long revision, long epoch)
    {
        await runtime.ConfigureAsync(body, revision, epoch);
        var resume = await Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(runtime, beltRuntime) || !beltVisible) return Task.CompletedTask;
            if (revision == beltRequestedRevision && epoch == beltRequestedBodyEpoch && runtime.Metrics.Error is null)
                beltError = null;
            return runtime.Metrics.Running ? Task.CompletedTask : runtime.ResumeAsync();
        });
        await resume;
    }

    private void RestoreBeltBodyDraft()
    {
        if (beltRuntime is null) return;
        ClearBeltInput();
        beltPendingDisplay = null;
        beltPrimed = false;
        if (appearanceSession?.RequiresBodyPreview == true)
        {
            LiveSceneStatusText.Text = "正在恢复已保存的身体…";
            LiveSceneStatusText.Visibility = Visibility.Visible;
        }
        UpdateBeltAppearance(true);
    }

    private void RefreshBeltVisibility()
    {
        if (beltRuntime is null) return;
        var visible = IsLoaded && IsVisible && !IsSettingsOpen && !IsDangoAway;
        if (visible == beltVisible) return;
        beltVisible = visible;
        beltRenderStamp = Stopwatch.GetTimestamp();
        beltPrimed = false;
        if (visible)
        {
            if (beltFatalError) return; // Visibility alone cannot recover a latched presentation/native fault.
            beltPresenter!.Paused = false;
            ObserveBeltOperation(beltRuntime.ResumeAsync());
        }
        else PauseBeltBody();
    }

    private void PauseBeltBody()
    {
        if (beltRuntime is null) return;
        beltVisible = false;
        ClearBeltInput();
        beltPendingDisplay = null;
        beltPrimed = false;
        if (beltPresenter is not null) beltPresenter.Paused = true;
        ObserveBeltOperation(beltRuntime.PauseAsync());
    }

    private void OnBeltRendering(object? sender, EventArgs e)
    {
        if (!beltVisible || beltRuntime is null || beltPresenter is null || beltFatalError) return;
        if (beltRuntime.Metrics.Error is { } failure)
        {
            LatchBeltFailure(failure.Message);
            return;
        }
        // A second WPF rendering callback confirms that the successful pose entered the visual tree.
        // This is a presentation acknowledgement, not a GPU-present or human acceptance claim.
        if (beltPendingDisplay is { } displayed && PresentationSource.FromVisual(this) is not null &&
            beltRuntime.MarkDisplayed(displayed))
        {
            BeltBodyDisplayConfirmed?.Invoke(displayed);
            if (appearanceSession?.MarkBodyApplied(displayed.Body, displayed.ModelVersion, displayed.ConfigurationRevision, displayed.BodyEpoch) == true)
                RefreshAppearanceControls();
        }
        beltPendingDisplay = null;
        var depth = beltRuntime.Metrics.QueueDepth;
        beltPrimed |= depth >= 2 || beltRuntime.Metrics.Sleeping;
        var consume = beltPrimed ? depth >= 4 ? 2 : 1 : 0;
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Clamp(Stopwatch.GetElapsedTime(beltRenderStamp, now).TotalSeconds, .001, .05);
        beltRenderStamp = now;
        beltRenderBatch.Clear();
        for (var index = 0; index < consume && beltRuntime.TryDequeue(out var snapshot); index++)
        {
            if (snapshot is null) break;
            if (snapshot.Frame is null || snapshot.Error is not null) continue;
            if (appearanceSession is not null && snapshot.BodyEpoch != appearanceSession.BodyEpoch) continue;
            beltRenderBatch.Add(snapshot);
        }
        PresentBeltBodyBatch(beltRenderBatch, dt);
    }

    internal void PresentBeltBodyBatch(IReadOnlyList<BeltBodySnapshot> batch, double dt)
    {
        // Rendering owns one wall-clock budget, independently of physical tick availability.
        // Every catch-up pose is consumed, but physical dt must not be added to this budget.
        foreach (var snapshot in batch)
        {
            beltSnapshot = snapshot;
            beltBoundary = Enumerable.Range(0, snapshot.Frame!.Curve.Length / 2)
                .Select(i => new BeltBodyBoundaryPoint(snapshot.Frame.Curve[2 * i], snapshot.Frame.Curve[2 * i + 1])).ToArray();
            ApplyBeltPose(snapshot, dt / batch.Count);
            BeltBodyConsumedSnapshots++;
            if (beltFatalError) return;
        }
        if (batch.Count == 0 && beltSnapshot is { } previous && previous.BodyEpoch == appearanceSession?.BodyEpoch)
            ApplyBeltPose(previous, dt);
    }

    private void ApplyBeltPose(BeltBodySnapshot current, double dt)
    {
        if (current.Frame is not { } frame || beltPresenter is null) return;
        var scale = PetPreview.ActualWidth / 160;
        var ground = DangoBodyMaterial.Configure(current.Body).GroundHeight;
        var top = BeltFloor - 66 + (ground - frame.CoreY) * BeltBodyAppearancePresenter.WorldToCanvas;
        Canvas.SetTop(beltPresenter.View, top);
        var input = current.ActualInput;
        var gaitContext = new BeltBodyGaitContext(scale, 0, top * scale, PetPreview.ActualWidth, PetVisualArea.ActualHeight);
        if (beltAppearanceFrame?.PhysicalTick == current.Tick)
            beltAppearanceFrame = beltAppearanceFrame with { Tick = ++beltPresentationTick, Seconds = dt, GaitContext = gaitContext };
        else beltAppearanceFrame = new(++beltPresentationTick, dt, beltBoundary, frame.CoreX, frame.CoreY,
            frame.Scaffold[2], frame.Scaffold[3], frame.Grounded, input.Horizontal, input.Downward > 0,
            input.Stretch > 0, input.JumpPressed, input.DragActive, gaitContext, current.Tick);
        beltPresenter.Update(beltAppearanceFrame);
        BeltBodyPresentationSeconds += beltAppearanceFrame.Seconds;
        if (beltPresenter.RuntimeError is { } presentationError)
        {
            LatchBeltFailure(presentationError);
            return;
        }
        if (beltLastPresentedSequence != current.Sequence)
        {
            BeltBodyPoseApplied?.Invoke(current);
            beltPendingDisplay = current;
            beltLastPresentedSequence = current.Sequence;
        }
        LiveSceneStatusText.Text = beltError ?? string.Empty;
        LiveSceneStatusText.Visibility = beltError is null ? Visibility.Collapsed : Visibility.Visible;
        PetShadow.Visibility = Visibility.Visible;
        var lift = Math.Clamp((frame.MinimumY - DangoBodyMaterial.Configure(current.Body).GroundHeight) / .7, 0, 1);
        PetShadowScale.ScaleX = 1 - .32 * lift;
        PetShadowScale.ScaleY = 1 - .15 * lift;
        PetShadow.Opacity = .17 - .09 * lift;
    }

    private void LatchBeltFailure(string error)
    {
        if (beltFatalError) return;
        beltFatalError = true;
        beltDiagnosticError = error;
        beltError = "身体暂不可用，请在设置中重新初始化。";
        beltPendingDisplay = null;
        ClearBeltInput();
        if (beltPresenter is not null) beltPresenter.Paused = true;
        if (beltRuntime is not null) ObserveBeltOperation(beltRuntime.PauseAsync());
        LiveSceneStatusText.Text = beltError;
        LiveSceneStatusText.Visibility = Visibility.Visible;
        RefreshAppearanceControls();
    }

    private void OnBodySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingAppearance || BodyLandingBuildSlider is null || beltRuntime is null || !IsEditing || SelectedEditor != HomeEditor.Face) return;
        StageAppearance(DraftAppearance.WithBody(new(BodySizeSlider.Value, BodySoftnessSlider.Value, BodyLandingBuildSlider.Value)));
    }

    private bool HandleBeltKey(KeyEventArgs e, bool down)
    {
        if (beltRuntime is null) return false;
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.A or Key.D or Key.W or Key.S or Key.Space)) return false;
        if (!down) SetBeltDirectionalKey(key, false, false);
        // A recognized belt key never falls through to the legacy A/D/W navigation mapping.
        // Text/menu controls still receive unhandled events when gameplay is unavailable.
        if (ownerWindow?.IsActive != true || !beltVisible || IsSettingsOpen || IsDangoAway || IsRoomDialogOpen || beltFatalError || beltWindowMenuOpen ||
            IsBeltTextEntry(Keyboard.FocusedElement) || Keyboard.Modifiers != ModifierKeys.None)
        { ClearBeltInput(); return true; }
        SetBeltDirectionalKey(key, down, e.IsRepeat);
        e.Handled = true;
        return true;
    }

    private void OnBeltOwnerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (beltRuntime is null || e.Handled) return;
        if (Keyboard.Modifiers != ModifierKeys.None) ClearBeltInput();
        HandleBeltKey(e, true);
    }

    private void OnBeltOwnerPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (beltRuntime is not null && !e.Handled) HandleBeltKey(e, false);
    }

    private static bool IsBeltTextEntry(IInputElement? focus)
    {
        for (var node = focus as DependencyObject; node is not null;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox or PasswordBox) return true;
        return false;
    }

    private void OnBeltOwnerKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (beltRuntime is null) return;
        if (beltWindowMenuOpen || e.NewFocus is not DependencyObject target ||
            !ReferenceEquals(Window.GetWindow(target), ownerWindow) || IsBeltTextEntry(e.NewFocus))
        { ClearBeltInput(); return; }
        // Disable IME only on gameplay focus targets; editable fields keep their own policy.
        if (target is UIElement && target is not Window) InputMethod.SetIsInputMethodEnabled(target, false);
        ReconcileBeltKeyboard();
    }

    internal void SetBeltWindowMenuOpen(bool open)
    {
        if (beltWindowMenuOpen == open) return;
        beltWindowMenuOpen = open;
        if (beltRuntime is null) return;
        // A programmatically opened ContextMenu need not move WPF keyboard focus.
        if (open) ClearBeltInput(); else ReconcileBeltKeyboard();
    }

    private bool SetBeltDirectionalKey(Key key, bool down, bool repeat)
    {
        if (beltRuntime is null || key is not (Key.A or Key.D or Key.W or Key.S or Key.Space)) return false;
        if (!down && !beltHeld.Contains(key) && !beltSuppressed.Contains(key)) return true;
        if (!down) { beltHeld.Remove(key); beltSuppressed.Remove(key); }
        else if (!beltSuppressed.Contains(key) && !(repeat && key == Key.Space) && beltHeld.Add(key) && key == Key.Space) beltJump = true;
        PublishBeltInput();
        return true;
    }

    private static void ConfigureBeltLauncher(System.Windows.Controls.Button button, string label)
    {
        // The belt owns WASD in the home too; do not advertise legacy navigation keys.
        if (button.Content is StackPanel panel && panel.Children.Count == 2 && panel.Children[0] is Border)
        {
            panel.Children.RemoveAt(0);
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }

    private void PublishBeltInput()
    {
        if (beltRuntime is null) return;
        var w = beltHeld.Contains(Key.W); var s = beltHeld.Contains(Key.S);
        beltRuntime.PublishInput(new((beltHeld.Contains(Key.D) ? 1 : 0) - (beltHeld.Contains(Key.A) ? 1 : 0),
            w && !s ? 1 : 0, s && !w ? 1 : 0, beltJump, beltDragging,
            beltDragTarget.X, beltDragTarget.Y, beltHeld.Count > 0 || beltDragging, beltCancel || w && s));
        beltJump = beltCancel = false;
    }

    private void ClearBeltInput()
    {
        if (beltRuntime is null) return;
        var alreadyQuiet = beltRuntime.Metrics.Sleeping && beltHeld.Count == 0 && !beltDragging && !beltJump;
        beltSuppressed.UnionWith(beltHeld);
        beltHeld.Clear();
        beltJump = false;
        beltDragging = false;
        if (PetPreview.IsMouseCaptured) PetPreview.ReleaseMouseCapture();
        if (alreadyQuiet) return; // Eye-only preview and focus changes do not require a new body solve.
        beltCancel = true;
        PublishBeltInput();
        beltRuntime.ClearInput();
    }

    private void OnBeltOwnerActivated(object? sender, EventArgs e)
    {
        if (beltRuntime is null) return;
        ReconcileBeltKeyboard();
        if (ownerWindow?.IsActive == true && (Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, ownerWindow)))
            Stage.Focus();
    }

    private void ReconcileBeltKeyboard()
    {
        var before = beltHeld.Count;
        var physical = new[] { Key.A, Key.D, Key.W, Key.S, Key.Space }.Where(Keyboard.IsKeyDown).ToHashSet();
        beltSuppressed.IntersectWith(physical);
        beltSuppressed.UnionWith(physical.Except(beltHeld));
        beltHeld.IntersectWith(physical);
        if (beltHeld.Count != before) PublishBeltInput();
    }

    private void OnBeltPointerDown(object sender, MouseButtonEventArgs e) => TryBeginBeltDrag(e);
    private bool TryBeginBeltDrag(MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left || beltSnapshot is null || beltPresenter is null || IsSettingsOpen) return false;
        var p = e.GetPosition(LiveSceneCanvas);
        var world = BeltPointerToWorld(p);
        var inside = false;
        for (int i = 0, j = beltBoundary.Count - 1; i < beltBoundary.Count; j = i++)
        {
            var a = beltBoundary[i]; var b = beltBoundary[j];
            if ((a.Y > world.Y) != (b.Y > world.Y) && world.X < (b.X - a.X) * (world.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        if (!inside) return false;
        Stage.Focus();
        beltDragging = true;
        beltDragTarget = world;
        PetPreview.CaptureMouse();
        PublishBeltInput();
        e.Handled = true;
        return true;
    }
    private Point BeltPointerToWorld(Point p) => new((beltSnapshot?.Frame?.CoreX ?? 0) + (p.X - 80) / BeltBodyAppearancePresenter.WorldToCanvas,
        DangoBodyMaterial.Configure(beltSnapshot?.Body ?? DraftAppearance.Body).GroundHeight + (BeltFloor - p.Y) / BeltBodyAppearancePresenter.WorldToCanvas);
    private void OnBeltPointerMove(object sender, MouseEventArgs e)
    {
        if (!beltDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ClearBeltInput(); return; }
        beltDragTarget = BeltPointerToWorld(e.GetPosition(LiveSceneCanvas));
        PublishBeltInput();
        e.Handled = true;
    }
    private void OnBeltPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!beltDragging) return;
        beltDragging = false;
        PetPreview.ReleaseMouseCapture();
        PublishBeltInput();
        e.Handled = true;
    }
    private void OnBeltLostCapture(object sender, MouseEventArgs e)
    {
        if (!beltDragging) return;
        beltDragging = false;
        beltCancel = true;
        PublishBeltInput();
    }
}
