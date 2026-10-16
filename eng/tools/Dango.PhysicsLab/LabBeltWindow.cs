using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Diagnostics;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.PhysicsLab;

/// <summary>Isolated single-body, flat-ground experiment. No display deformation or filtering.</summary>
internal sealed class LabBeltWindow : Window
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly string halfModule, beltModule, calibratedModule, session;
    private readonly LabInput input = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ComboBox model = new();
    private readonly CheckBox shortBones = new() { Content = "保留短骨架塑形（不碰撞）", IsChecked = true };
    private readonly CheckBox whaleDrive = new() { Content = "鲸形行走（仅改变受力）", IsChecked = true };
    private readonly CheckBox showSkeleton = new() { Content = "骨架与质心", IsChecked = true };
    private readonly CheckBox showForces = new() { Content = "详细施力", IsChecked = true };
    private readonly TextBlock forceDetails = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Focusable = false };
    private readonly Slider forceScale = new() { Minimum = 30, Maximum = 600, Value = 150, TickFrequency = 30, IsSnapToTickEnabled = true };
    private readonly CheckBox showReference = new() { Content = "叠加原版静置轮廓", IsChecked = true };
    private readonly CheckBox showAppearance = new() { Content = "正式眼睛与已佩戴配饰", IsChecked = true };
    private readonly TextBlock appearanceStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 5, 0, 4) };
    private readonly PhysicsLabAppearancePresenter appearance;
    private readonly string appearanceSource;
    private readonly Slider size = new() { Minimum = 80, Maximum = 120, Value = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly Slider softness = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly Slider build = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 21 };
    private readonly Surface surface;
    private readonly ScrollViewer sidebarScroll;
    private readonly Queue<object> trace = new();
    private LabBeltWorld? belt;
    private DangoPhysicsWorld? old;
    private DangoEntityId oldId;
    private LabBeltFrame? beltFrame;
    private DangoBodyState? oldFrame, reference;
    private IReadOnlyList<DangoLabNode> oldNodes = [];
    private DangoSelfPhysicsParameters config = DangoBodyMaterial.Configure(DangoBodyControls.Default);
    private bool fault, paused, initialized, dragging, replaying;
    private int steps, replayFrame, clicks;
    private double previousTime, accumulator, cameraX, dragX, dragY, discardedSeconds;
    private LabInputSnapshot lastInput;
    private LabBeltCalibrationParameters? activeCalibration;
    private string activePreset = "";
    private float[]? guardDiagnostics;
    private double[]? speedLimitDiagnostics, whaleDriveDiagnostics, forceLedger;
    private string? failureReason;
    private double[]? preStepSnapshot, solverFailureDetails;
    private LabBeltInput? actualBeltInput;
    private double? actualBeltDt;
    private int? preStepTick;
    internal Button ApplyButton { get; }
    internal TextBox Notes { get; } = new() { MinHeight = 34, MaxLength = 500, ToolTip = "仅用于验证文字输入隔离；内容不保存" };
    private bool ActiveBelt => belt is not null;
    private float CoreX => beltFrame?.Stats[0] ?? oldFrame?.CoreX ?? 0;
    private float CoreY => beltFrame?.Stats[1] ?? oldFrame?.CoreY ?? 0;

    internal LabBeltWindow(string halfModule, string beltModule, string calibratedModule, string session, string appearanceSettingsPath, string appearanceSource)
    {
        this.halfModule = halfModule; this.beltModule = beltModule; this.calibratedModule = calibratedModule; this.session = session;
        this.appearanceSource = appearanceSource;
        // Open the reported failing corner for review; product material defaults stay unchanged.
        size.Value = 80; softness.Value = 100; build.Value = 0;
        appearance = new PhysicsLabAppearancePresenter(appearanceSettingsPath);
        showSkeleton.IsChecked = false; showReference.IsChecked = false;
        Title = "团子 · 鲸形行走·收尾与施力预览";
        Width = Math.Min(1220, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(850, SystemParameters.WorkArea.Height - 32); MinWidth = 940; MinHeight = 660;
        Background = new SolidColorBrush(Color.FromRgb(253, 252, 249)); Foreground = LabWindow.Ink;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LabWindow.ApplyStyles(Resources);
        InputMethod.SetIsInputMethodEnabled(this, false); InputMethod.SetIsInputMethodEnabled(Notes, true);
        surface = new Surface(this) { Focusable = true, ClipToBounds = true };
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = "连续闭合带实验", FontSize = 21, Margin = new Thickness(0, 0, 0, 6) });
        heading.Children.Add(new TextBlock { Text = "A / D 左右半区   W / S 上半区向上／向下   Space 跳跃   鼠标拖动", Foreground = LabWindow.Accent });
        heading.Children.Add(new TextBlock { Text = "正式眼睛与配饰 · 原始物理显示 · 无周期摆动 · 外观仅用于实验，不写回日常设置", FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(18) }); grid.ColumnDefinitions.Add(new());
        root.Children.Add(grid);
        var sidebar = new DockPanel(); grid.Children.Add(sidebar);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 10, 0) };
        ApplyButton = MakeButton("↻  应用并重置", () => { clicks++; Reset(); }); footer.Children.Add(ApplyButton); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        var tools = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
        sidebarScroll = new ScrollViewer { Content = tools, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        sidebar.Children.Add(sidebarScroll);
        Label(tools, "身体参数对照");
        model.Items.Add("当前身体 · F 基础＋形状保护");
        model.Items.Add("旧对照 · 原外围半侧驱动"); model.Items.Add("旧对照 · 闭合带校正前"); model.SelectedIndex = 0;
        tools.Children.Add(model); tools.Children.Add(shortBones); tools.Children.Add(whaleDrive);
        tools.Children.Add(new TextBlock { Text = "新圈整体承重。取消鲸形行走可对照原推动；更改模型、材料或行走开关后点击应用。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        model.DropDownOpened += (_, _) => Cancel(); model.DropDownClosed += (_, _) => input.ReconcileHeld(HeldKeys());
        Label(tools, "材料"); AddSlider(tools, "大小", size); AddSlider(tools, "柔软度", softness); AddSlider(tools, "落地体态", build);
        Label(tools, "观察"); showAppearance.Content = "正式眼睛与测试配饰"; tools.Children.Add(showAppearance); tools.Children.Add(appearanceStatus); tools.Children.Add(showSkeleton); tools.Children.Add(showForces); tools.Children.Add(showReference);
        AddSlider(tools, "施力箭头 DIP / N（仅显示）", forceScale);
        forceScale.ValueChanged += (_, _) => surface.Refresh();
        showForces.Click += (_, _) => surface.Refresh();
        tools.Children.Add(new TextBlock { Text = "主动施力；末子步积分前位置；不含重力/接触反力。小段合力与64个真实积分力同尺度。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        tools.Children.Add(new Expander { Header = "施力明细 · N", Content = new ScrollViewer { Content = forceDetails, MaxHeight = 220, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        showAppearance.Click += (_, _) => surface.Refresh();
        showSkeleton.Click += (_, _) => surface.Refresh(); showReference.Click += (_, _) => surface.Refresh();
        tools.Children.Add(MakeButton("▷  同输入走停回放", () => { Reset(); replaying = !fault; replayFrame = 0; }));
        tools.Children.Add(MakeButton("Ⅱ  暂停／继续", () => { Cancel(); paused = !paused; appearance.Paused = paused; UpdateForceDetails(); surface.Refresh(); }));
        tools.Children.Add(MakeButton("↓  导出物理记录", Export));
        Label(tools, "文字输入隔离"); tools.Children.Add(Notes);
        Label(tools, "实时诊断"); tools.Children.Add(details);
        Grid.SetColumn(surface, 2); grid.Children.Add(surface); Content = root;
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDownHandler), true);
        AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(KeyUpHandler), true);
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => { if (IsText(Keyboard.FocusedElement as DependencyObject)) Cancel(); }), true);
        Deactivated += (_, _) => { Cancel(); appearance.Paused = true; };
        Activated += (_, _) => { input.ReconcileHeld(HeldKeys()); appearance.Paused = paused || fault; };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { Cancel(); appearance.Paused = true; } };
        Closed += (_, _) => { timer.Stop(); belt?.Dispose(); old?.Dispose(); appearance.Dispose(); };
        Loaded += (_, _) =>
        {
            initialized = true; Reset(); surface.Focus(); previousTime = clock.Elapsed.TotalSeconds;
            timer.Interval = TimeSpan.FromMilliseconds(8); timer.Tick += Tick; timer.Start();
            WriteStarted();
        };
    }

    private static Button MakeButton(string text, Action action)
    {
        var b = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 40 };
        b.Click += (_, _) => action(); return b;
    }
    private static void Label(Panel p, string text) => p.Children.Add(new TextBlock { Text = text, Foreground = LabWindow.Accent, Margin = new Thickness(0, 14, 0, 3) });
    private static void AddSlider(Panel p, string text, Slider slider)
    {
        var label = new TextBlock(); void Refresh() => label.Text = $"{text}  {slider.Value:0}";
        Refresh(); slider.ValueChanged += (_, _) => Refresh(); p.Children.Add(label); p.Children.Add(slider);
    }
    private void WriteStarted() => File.WriteAllText(Path.Combine(session, "started.json"), JsonSerializer.Serialize(new
    { pid = Environment.ProcessId, visible = IsVisible, nativeLoaded = belt is not null || old is not null, model = ActiveBelt ? "continuous-belt" : "old-half", preset = activePreset, calibration = activeCalibration, originalGroundJump = belt?.OriginalGroundJumpEnabled, shapeGuard = GuardState(), speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(), forceLedger = ForceLedgerState(false), failureReason, shortBones = beltFrame?.Stats[40] == 1, displaySmoothing = false, gait = false, formalAppearance = true, equippedCount = appearance.EquippedCount, renderedAccessories = appearance.RenderedAccessoryCount, error = fault }));

    private object WhaleDriveState() => new { available = belt?.WhaleDriveAvailable ?? false, enabled = belt?.WhaleDriveEnabled ?? false,
        requested = model.SelectedIndex == 0 && whaleDrive.IsChecked == true, schema = LabBeltWorld.WhaleDriveSchema, diagnostics = whaleDriveDiagnostics };

    private object ForceLedgerState(bool includeRows = true) => new
    {
        available = belt?.ForceLedgerAvailable ?? false, schema = LabBeltWorld.ForceLedgerSchema,
        tick = forceLedger is { } ledger ? (double?)ledger[2] : null,
        rowCount = forceLedger is { } rows ? (int)rows[4] : 0,
        sourceMask = forceLedger is { } sources ? (int)sources[5] : 0,
        sourceCounts = Enumerable.Range(1, 4).Select(kind => new { kind, count = ForceRowCount(kind) }).ToArray(),
        values = includeRows ? forceLedger : null,
        position = "last substep before integration", scope = "active applied forces; excludes gravity and contact reactions",
        displayEnabled = showForces.IsChecked == true, displayDipPerNewton = forceScale.Value,
    };
    private int ForceRowCount(int kind) => forceLedger is not { } ledger ? 0 :
        Enumerable.Range(0, (int)ledger[4]).Count(i => (int)ledger[8 + 6*i + 4] == kind);
    private static string ForceSource(int kind) => kind switch { 1 => "A/D段", 2 => "W/S段", 3 => "塑形点", 4 => "拖动点", _ => "未知" };
    private void UpdateForceDetails()
    {
        if (forceLedger is not { } ledger) { forceDetails.Text = "当前模块未提供真实施力明细。"; return; }
        var lines = new List<string> { $"帧 {ledger[2]:0} / 末子步 {ledger[3]:0} / {ledger[4]:0} 项", "来源 #  Fx / Fy / |F| (N)" };
        for (var i = 0; i < (int)ledger[4]; i++)
        {
            var r = 8 + 6*i; var fx = ledger[r+2]; var fy = ledger[r+3];
            var material = (int)ledger[r+4] is 1 or 2 ? $"段{ledger[r+5]:0}" : $"θ{ledger[r+5]:0.000}";
            lines.Add(FormattableString.Invariant($"#{i:00} {ForceSource((int)ledger[r+4])} {material}\nFx {fx:0.00000} / Fy {fy:0.00000} / |F| {Math.Sqrt(fx*fx+fy*fy):0.00000}"));
        }
        forceDetails.Text = string.Join("\n", lines);
    }

    private object SpeedLimitState() => new { available = belt?.SpeedSaturationAvailable ?? false, enabled = belt?.SpeedSaturationEnabled ?? false, requested = model.SelectedIndex == 0, maximumSpeed = config.MaximumSpeed, schema = LabBeltWorld.SpeedLimitSchema, diagnostics = speedLimitDiagnostics };

    private object GuardState() => new { available = belt?.ShapeGuardAvailable ?? false, enabled = belt?.ShapeGuardEnabled ?? false, requested = model.SelectedIndex == 0, schema = LabBeltWorld.ShapeGuardSchema, diagnostics = guardDiagnostics };

    private void Reset()
    {
        if (!initialized) return;
        Cancel(); belt?.Dispose(); old?.Dispose(); belt = null; old = null; beltFrame = null; oldFrame = null; reference = null;
        fault = paused = false; failureReason = null; guardDiagnostics = null; speedLimitDiagnostics = whaleDriveDiagnostics = forceLedger = null; UpdateForceDetails(); preStepSnapshot = solverFailureDetails = null; actualBeltInput = null; actualBeltDt = null; preStepTick = null; cameraX = accumulator = 0; steps = 0; trace.Clear();
        appearance.Paused = false; appearance.Reset();
        try
        {
            config = DangoBodyMaterial.Configure(new DangoBodyControls(size.Value, softness.Value, build.Value));
            using (var baseline = DangoPhysicsWorld.Create(halfModule, 1, config))
            {
                baseline.RequireLab(); var id = baseline.Add(0, 0); baseline.SetHorizontalBounds(-256, 256);
                for (var i = 0; i < 300; i++)
                {
                    var f = baseline.StepLab(LabWindow.FixedDt, [new(id)], DangoLabDrive.DirectionRegion);
                    if (!f.Succeeded) throw new InvalidOperationException("原版静置参考失败 " + f.ErrorCode);
                    reference = f.Bodies.Single();
                }
            }
            activePreset = model.SelectedItem?.ToString() ?? "";
            Title = "团子 · " + activePreset + (model.SelectedIndex == 0 ? whaleDrive.IsChecked == true ? " · 鲸形行走·收尾与施力预览" : " · 原推动对照" : " · 身体参数校准");
            activeCalibration = model.SelectedIndex == 0
                ? LabBeltCalibrationParameters.Default with { RadialStiffnessScale = 1.4, AreaScale = 1.4, BeltBendScale = 4, OverallDamping = config.OverallDamping, GroundedBrake = 35, GroundedHorizontalDamping = 60 }
                : null;
            var selectedModule = model.SelectedIndex == 0 ? calibratedModule : model.SelectedIndex == 1 ? halfModule : beltModule;
            if (model.SelectedIndex != 1)
            {
                belt = new LabBeltWorld(selectedModule, config, shortBones.IsChecked == true, activeCalibration, enableShapeGuard: model.SelectedIndex == 0, enableSpeedSaturation: model.SelectedIndex == 0,
                    enableWhaleDrive: model.SelectedIndex == 0 && whaleDrive.IsChecked == true);
                if (activeCalibration is not null) belt.SetOriginalGroundJump();
                belt.SetBounds(-256, 256); beltFrame = belt.Read();
                if (belt.ShapeGuardEnabled) guardDiagnostics = belt.ReadShapeGuard();
                if (belt.SpeedSaturationEnabled) speedLimitDiagnostics = belt.ReadSpeedLimit();
                if (belt.WhaleDriveAvailable) whaleDriveDiagnostics = belt.ReadWhaleDrive();
            }
            else
            {
                old = DangoPhysicsWorld.Create(halfModule, 1, config); old.RequireLab();
                if (old.LabRegionDenominator != 2) throw new InvalidOperationException("原版模块不是半区驱动");
                oldId = old.Add(0, 0); old.SetHorizontalBounds(-256, 256);
            }
            status.Text = activeCalibration is not null ? belt!.WhaleDriveEnabled ? "鲸形行走已启用。A/D观察前后轮廓，松手恢复；无周期摆动。" : "原推动对照，F身体、形状保护与限速保持开启。" :
                ActiveBelt ? "旧对照：校正前闭合带，旧跳跃接线。" : "旧对照：原外围半区驱动。";
            File.AppendAllText(Path.Combine(session, "body-presets.jsonl"), JsonSerializer.Serialize(new
            { preset = activePreset, config, calibration = activeCalibration, originalGroundJump = belt?.OriginalGroundJumpEnabled, shapeGuard = GuardState(), speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(),
                nativeSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(selectedModule))) }) + Environment.NewLine);
            StepOnce(); WriteStarted();
        }
        catch (Exception e) { Fault(e); }
    }
    private void Tick(object? sender, EventArgs args)
    {
        var now = clock.Elapsed.TotalSeconds; var elapsed = now - previousTime; previousTime = now;
        if (paused || fault || !IsActive || !IsVisible || WindowState == WindowState.Minimized) { accumulator = 0; return; }
        appearance.Paused = false;
        accumulator += Math.Min(elapsed, .25); var count = 0;
        while (accumulator >= LabWindow.FixedDt && count++ < 4)
        { StepOnce(); accumulator -= LabWindow.FixedDt; if (fault) break; }
        if (accumulator > 4 * LabWindow.FixedDt) { discardedSeconds += accumulator; accumulator = 0; }
        surface.Refresh();
    }
    private void StepOnce()
    {
        if (fault) return;
        try
        {
            lastInput = input.Consume();
            if (replaying)
            {
                var f = replayFrame++; var h = f is >= 300 and < 900 ? 1 : f is >= 1080 and < 1680 ? -1 : 0;
                lastInput = new(h, 0, 0, h != 0, false, false, false);
                if (f >= 1860) { replaying = false; status.Text = "回放完成。可切换模型重复同输入。"; }
            }
            if (belt is not null)
            {
                var actualInput = new LabBeltInput(Horizontal: lastInput.Horizontal, Stretch: lastInput.Stretch, Downward: lastInput.Downward,
                    JumpPressed: lastInput.JumpPressed, InputActive: lastInput.InputActive, CancelForces: lastInput.CancelForces,
                    DragActive: dragging, DragTargetX: dragX, DragTargetY: dragY);
                actualBeltInput = actualInput; actualBeltDt = LabWindow.FixedDt; preStepTick = steps;
                preStepSnapshot = belt.DebugStateAvailable ? belt.ReadDebugState() : null;
                belt.Step(LabWindow.FixedDt, actualInput);
                beltFrame = belt.Read(); var s = beltFrame.Stats;
                guardDiagnostics = belt.ShapeGuardEnabled ? belt.ReadShapeGuard() : null;
                speedLimitDiagnostics = belt.SpeedSaturationEnabled ? belt.ReadSpeedLimit() : null;
                whaleDriveDiagnostics = belt.WhaleDriveAvailable ? belt.ReadWhaleDrive() : null;
                forceLedger = belt.ForceLedgerAvailable ? belt.ReadForceLedger() : null;
                if (forceLedger is { } forces && (forces.Length != 1024 || forces[0] != 1 || forces[1] != 1024 ||
                    forces[2] != steps + 1 || forces[4] < 0 || forces[4] > 97 || forces[6] != 6 || forces[7] != 64))
                    throw new InvalidOperationException("真实施力明细格式或同帧诊断失败");
                if (belt.WhaleDriveEnabled && (whaleDriveDiagnostics is not { } whale || whale[1] != 1 || whale[2] != steps + 1 ||
                    whale[5] > 1e-9 || whale[6] > 1e-9 || whale[7] > 1e-9))
                    throw new InvalidOperationException("行走受力同帧、净力或力矩诊断失败");
                if (speedLimitDiagnostics is { } cap && (cap[1] != 1 || cap[2] != steps + 1 || cap[6] > config.MaximumSpeed))
                    throw new InvalidOperationException("限速同帧或安全速度上限诊断失败");
                if (guardDiagnostics is { } g && (g[0] != 1 || g[1] != 1 || g[2] != steps + 1 || g[3] != 0 ||
                    g[13] != 1 || g[11] < g[20] || g[12] < g[19]))
                    throw new InvalidOperationException("形状保护同帧、核心在内或硬边界诊断失败");
                if (s[36] != 0 || s[31] > .0001 || s[39] != 0) throw new InvalidOperationException("闭合带本帧安全诊断失败");
                trace.Enqueue(new { tick = steps, model = "belt", stats = s,
                    diagnostics = activeCalibration is null ? null : belt.ReadDiagnostics(), shapeGuard = guardDiagnostics, speedLimit = speedLimitDiagnostics, whaleDrive = whaleDriveDiagnostics });
                if (steps % 6 == 0) details.Text = FormattableString.Invariant($"高 {(s[11] - s[9]) * 72:0.00} DIP / 宽 {(s[10] - s[8]) * 72:0.00} DIP\n真实质心Y {s[5]:0.0000} / 核心Y {s[1]:0.0000}\n接触 {s[25]:0} / 间隙 {s[12] * 72:0.00000} DIP\n合力 ({s[18]:0.000}, {s[19]:0.000})\nKKT残差 {s[30]:0.0e+0} / 36自由度\n独立外围碰撞体 {s[39]:0} / 帧 {steps}") + GuardSummary() + (belt.SpeedSaturationEnabled ? "\n限速：已启用，真实超速后限速继续运行" : "\n限速：旧对照未启用") + (belt.WhaleDriveEnabled ? FormattableString.Invariant($"\n行走受力强度 {whaleDriveDiagnostics?[3]:0.00} · 无周期摆动") : "\n行走受力：原推动");
            }
            else if (old is not null)
            {
                var f = old.StepLab(LabWindow.FixedDt, [new(oldId, Horizontal: lastInput.Horizontal, Stretch: lastInput.Stretch, Downward: lastInput.Downward,
                    JumpPressed: lastInput.JumpPressed, InputActive: lastInput.InputActive, CancelForces: lastInput.CancelForces,
                    DragActive: dragging, DragTargetX: dragX, DragTargetY: dragY)], DangoLabDrive.DirectionRegion);
                if (!f.Succeeded) throw new InvalidOperationException("原版物理失败 " + f.ErrorCode);
                oldFrame = f.Bodies.Single(); oldNodes = old.ReadLabNodes(oldId);
                var ex = LabGroundExperiments.CurveExtent(oldFrame.Contour, 1);
                var mass = oldNodes.Sum(n => n.Mass); var comY = oldNodes.Sum(n => n.Mass * n.Y) / mass;
                trace.Enqueue(new { tick = steps, model = "old-half", coreY = oldFrame.CoreY, comY, height = ex.Maximum - ex.Minimum });
                if (steps % 6 == 0) details.Text = FormattableString.Invariant($"高 {(ex.Maximum - ex.Minimum) * 72:0.00} DIP\n真实质心Y {comY:0.0000} / 核心Y {oldFrame.CoreY:0.0000}\n原外围独立碰撞体 16 / 帧 {steps}");
            }
            if (steps % 6 == 0) UpdateForceDetails();
            UpdateAppearance();
            while (trace.Count > 1200) trace.Dequeue(); steps++;
            if (Math.Abs(CoreX - cameraX) > 1.5) cameraX = CoreX - Math.Sign(CoreX - cameraX) * 1.5;
            surface.Refresh();
            if (lastInput.InteractPressed) status.Text = "闭合带本轮只实验单体平地，平台交互未接入。";
        }
        catch (Exception e) { Fault(e); }
    }
    private string GuardSummary() => guardDiagnostics is not { } g ? "\n形状保护：旧对照未启用" :
        FormattableString.Invariant($"\n形状保护：同帧 {g[2]:0} / 核心绕数 {g[13]:0}\n核心净距 ≥{g[12]:0.0000} / 叉积 ≥{g[11]:0.000000}\n认证工作 {g[4]:0}/{g[21]:0} / 拒绝候选 {g[8]:0}");

    private void UpdateAppearance()
    {
        PhysicsLabBoundaryPoint[] boundary;
        double vx, vy; bool grounded, jump;
        if (beltFrame is { } b)
        {
            boundary = Enumerable.Range(0, b.Curve.Length / 2).Select(i => new PhysicsLabBoundaryPoint(b.Curve[2*i], b.Curve[2*i+1])).ToArray();
            vx = b.Stats[46]; vy = b.Stats[47]; grounded = b.Grounded; jump = b.JumpAccepted;
        }
        else if (oldFrame is { } o)
        {
            boundary = LegacyBoundary(o); vx = oldNodes[0].VelocityX; vy = oldNodes[0].VelocityY; grounded = o.IsGrounded; jump = lastInput.JumpPressed;
        }
        else return;
        appearance.Update(new(steps, LabWindow.FixedDt, boundary, CoreX, CoreY, vx, vy, grounded,
            lastInput.Horizontal, lastInput.Downward > 0, lastInput.Stretch > 0, jump, dragging));
        appearanceStatus.Text = appearanceSource + "\n" + appearance.Status;
    }
    private static PhysicsLabBoundaryPoint[] LegacyBoundary(DangoBodyState b)
    {
        var n = b.PointCount; var points = new PhysicsLabBoundaryPoint[256];
        for (var j = 0; j < points.Length; j++)
        {
            var u = j * n / (double)points.Length; var i = (int)u; var t = u - i; var a = (i+n-1)%n; var c = (i+1)%n;
            double At(int axis) => .5*(1-t)*(1-t)*b.Contour[2*a+axis] + (.5+t-t*t)*b.Contour[2*i+axis] + .5*t*t*b.Contour[2*c+axis];
            points[j] = new(b.CoreX + At(0), b.CoreY + At(1));
        }
        return points;
    }
    private object StepEvidence() => new
    {
        speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(), forceLedger = ForceLedgerState(),
        tick = preStepTick, dt = actualBeltDt, nativeDt = actualBeltDt is { } dt ? (float?)dt : null,
        actualInput = actualBeltInput,
        nativeControls = actualBeltInput is { } nativeInput ? LabBeltWorld.NativeControls(nativeInput) : null,
        nativeDtBits = actualBeltDt is { } nativeDt ? (int?)BitConverter.SingleToInt32Bits((float)nativeDt) : null,
        snapshotSchema = LabBeltWorld.DebugStateSchema, preStepSnapshot,
        solverSchema = LabBeltWorld.SolverDiagnosticsSchema, solverFailureSites = LabBeltWorld.SolverFailureSites, solverFailureDetails,
        debugStateAvailable = belt?.DebugStateAvailable ?? false, solverDiagnosticsAvailable = belt?.SolverDiagnosticsAvailable ?? false,
    };
    private void Fault(Exception e)
    {
        if (fault) return;
        fault = paused = true; failureReason = e.Message; Cancel(); status.Text = "已暂停：" + e.Message + " 点击应用并重置可重新创建物理世界。";
        appearance.Paused = true;
        if (belt?.ShapeGuardAvailable == true)
        {
            try { guardDiagnostics = belt.ReadShapeGuard(); }
            catch (Exception diagnosticError) { failureReason += "；读取保护诊断失败：" + diagnosticError.Message; }
        }
        if (belt?.SpeedSaturationAvailable == true)
        {
            try { speedLimitDiagnostics = belt.ReadSpeedLimit(); }
            catch (Exception diagnosticError) { failureReason += "；读取限速诊断失败：" + diagnosticError.Message; }
        }
        if (belt?.WhaleDriveAvailable == true)
        {
            try { whaleDriveDiagnostics = belt.ReadWhaleDrive(); }
            catch (Exception diagnosticError) { failureReason += "；读取行走受力诊断失败：" + diagnosticError.Message; }
        }
        if (belt?.ForceLedgerAvailable == true)
        {
            try { forceLedger = belt.ReadForceLedger(); UpdateForceDetails(); }
            catch (Exception diagnosticError) { failureReason += "；读取施力明细失败：" + diagnosticError.Message; }
        }
        if (belt?.SolverDiagnosticsAvailable == true)
        {
            try { solverFailureDetails = belt.ReadSolverDiagnostics(); }
            catch (Exception diagnosticError) { failureReason += "；读取求解诊断失败：" + diagnosticError.Message; }
        }
        var specificReason = solverFailureDetails is { Length: >= 16 } diagnostic ? (int)diagnostic[3] switch
        {
            4 => "形变求解未找到有效下降步",
            5 => "形变求解达到迭代上限，未能收敛",
            7 or 8 => "形状计算超过本帧预算",
            6 or 9 or 19 => "本次变形无法通过形状安全检查",
            21 => "本次速度无法安全限制在上限内",
            22 => "行走局部受力计算失败",
            31 or 32 or 33 or 34 or 35 => "身体几何不满足材料计算条件",
            _ => null,
        } : null;
        if (specificReason is not null) status.Text = "已暂停：" + specificReason + "。点击应用并重置可恢复；故障记录已自动保存。";
        WriteStarted();
        var failurePath = Path.Combine(session, "physics-failure.json");
        if (File.Exists(failurePath)) failurePath = Path.Combine(session, $"physics-failure-{DateTime.UtcNow:HHmmss-fff}-{Guid.NewGuid():N}.json");
        using var failureFile = new FileStream(failurePath, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(failureFile, new
        { steps, error = e.ToString(), config, activePreset, activeCalibration, shapeGuard = GuardState(), speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(), stepEvidence = StepEvidence(), lastInput, beltFrame, oldFrame, trace = trace.ToArray(), discardedSeconds }, EvidenceJson);
    }
    private void Cancel()
    {
        input.Cancel(); replaying = false;
        if (dragging) { dragging = false; surface.ReleaseMouseCapture(); }
    }
    private void Export()
    {
        Cancel(); File.WriteAllText(Path.Combine(session, $"belt-trace-{DateTime.UtcNow:HHmmss-fff}.json"), JsonSerializer.Serialize(new
        { model = ActiveBelt ? "continuous-belt" : "old-half", preset = activePreset, calibration = activeCalibration, config, fixedDt = LabWindow.FixedDt, displaySmoothing = false, gait = false, discardedSeconds, shapeGuard = GuardState(), speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(), failureReason, stepEvidence = StepEvidence(), trace = trace.ToArray() }, EvidenceJson));
        status.Text = fault ? "已导出失败记录。" + failureReason + " 点击应用并重置可恢复。" : $"已导出 {trace.Count} 帧物理诊断。";
    }
    private bool Blocked() => IsText(Keyboard.FocusedElement as DependencyObject) || dragging || Mouse.Captured is not null || model.IsDropDownOpen;
    private static bool IsText(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is TextBoxBase or PasswordBox || d is ComboBox { IsEditable: true }) return true;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
    private static bool Map(Key key, out LabKey mapped)
    {
        mapped = key switch { Key.A => LabKey.Left, Key.D => LabKey.Right, Key.W => LabKey.Stretch, Key.S => LabKey.Squash, Key.Space => LabKey.Jump, Key.F => LabKey.Interact, _ => (LabKey)(-1) };
        return (int)mapped >= 0;
    }
    private static IEnumerable<LabKey> HeldKeys()
    {
        foreach (var k in new[] { Key.A, Key.D, Key.W, Key.S, Key.Space, Key.F }) if (Keyboard.IsKeyDown(k) && Map(k, out var m)) yield return m;
    }
    private void KeyDownHandler(object sender, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (Keyboard.Modifiers != ModifierKeys.None) { Cancel(); return; }
        if (!Map(k, out var m)) return;
        var blocked = Blocked(); if (replaying) Cancel(); input.KeyDown(m, e.IsRepeat, blocked: blocked);
        if (!blocked) e.Handled = true;
    }
    private void KeyUpHandler(object sender, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (!Map(k, out var m)) return; input.KeyUp(m); if (!Blocked()) e.Handled = true;
    }
    private void Inject(Key key, bool down, UIElement focus)
    {
        focus.Focus(); var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent }; focus.RaiseEvent(e);
        if (!e.Handled) { e.RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent; focus.RaiseEvent(e); }
    }
    internal async Task Smoke()
    {
        var checks = new List<string>();
        var walkingFrames = new List<object>();
        try
        {
            timer.Stop(); Cancel();
            size.Value = 80; softness.Value = 100; build.Value = 0; model.SelectedIndex = 0; whaleDrive.IsChecked = true; Reset();
            void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
            async Task Capture(string name)
            {
                sidebarScroll.ScrollToTop(); UpdateForceDetails(); surface.Refresh();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(session, name + ".png")); encoder.Save(file);
                walkingFrames.Add(new { name, steps, input = lastInput, frame = beltFrame, whaleDrive = WhaleDriveState(), forceLedger = ForceLedgerState(),
                    appearance = appearance.Status });
                File.WriteAllText(Path.Combine(session, "walking-review.json"), JsonSerializer.Serialize(new { material = new { size = size.Value, softness = softness.Value, build = build.Value },
                    scope = "actual physics frames at one of the eight approved corners; no render deformation or manual acceptance", frames = walkingFrames }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Check(IsLoaded && IsVisible && !fault && ActiveBelt, "真实WPF已显示且连接闭合带DLL");
            Check(belt!.ShapeGuardEnabled && guardDiagnostics is { } g && g[2] == steps && g[13] == 1,
                "80/100/0形状保护启用且核心在内诊断与身体同帧");
            Check(belt.SpeedSaturationEnabled && speedLimitDiagnostics is { } cap && cap[1] == 1 && cap[2] == steps && cap[6] <= config.MaximumSpeed,
                "80/100/0限速显式启用且同帧速度上界合法");
            Check(belt.WhaleDriveEnabled && whaleDriveDiagnostics is { } whale && whale[1] == 1 && whale[2] == steps,
                "鲸形行走受力显式启用且诊断与身体同帧");
            Check(belt.ForceLedgerAvailable && forceLedger is { Length: 1024 } ledger && ledger[0] == 1 && ledger[1] == 1024 && ledger[6] == 6 && ledger[7] == 64 && !string.IsNullOrWhiteSpace(LabBeltWorld.ForceLedgerSchema), "原生施力ledger可读且格式版本明确");
            Check(forceLedger![2] == steps, "施力ledger与成功身体帧同帧");
            for (var i = 0; i < 300; i++) StepOnce(); Check(!fault && beltFrame!.Stats[14] == 1, "闭合带落稳接地");
            Check(appearance.EquippedCount == 2 && appearance.RenderedAccessoryCount == 2, "虚构已佩戴前后配饰使用正式柔性网格");
            Check(!appearance.EyeDiagnostic.Contains("等待", StringComparison.Ordinal), "正式眼睛布局已消费真实身体帧");
            var snapshot = beltFrame; var beforeToggle = steps;
            var forceSnapshot = forceLedger?.ToArray();
            var physicalSnapshot = belt!.ReadDebugState();
            showForces.IsChecked = false; surface.Refresh(); forceScale.Value = 300; surface.Refresh(); forceScale.Value = 150; showForces.IsChecked = true; surface.Refresh();
            Check(steps == beforeToggle && forceSnapshot is not null && belt!.ReadForceLedger().SequenceEqual(forceSnapshot) && belt.ReadDebugState().SequenceEqual(physicalSnapshot), "详细施力显示倍率与只读读取不推进或改变物理");
            Check(showForces.IsChecked == true && showSkeleton.IsChecked == false, "详细施力默认开启且独立于骨架");
            showAppearance.IsChecked = false; surface.Refresh(); showAppearance.IsChecked = true; surface.Refresh();
            Check(ReferenceEquals(snapshot, beltFrame) && steps == beforeToggle, "完整形象开关不推进或重建物理");
            Inject(Key.D, true, ApplyButton); StepOnce(); Check(lastInput.Horizontal == 1 && beltFrame!.Stats[18] > 0, "按钮焦点D驱动真实半区力");
            Inject(Key.D, false, ApplyButton); StepOnce();
            Inject(Key.W, true, size); StepOnce(); Check(lastInput.Stretch == 1 && beltFrame!.Stats[19] > 0, "滑条焦点W仍向上施力");
            Check(ForceRowCount(2) is > 0 and <= 16 && ((int)forceLedger![5] & 2) != 0, "W施力ledger包含真实上半区分段合力");
            Inject(Key.S, true, size); StepOnce(); Check(lastInput.CancelForces && beltFrame!.Stats[19] == 0, "W加S撤销垂直力");
            Inject(Key.W, false, size); Inject(Key.S, false, size); StepOnce();
            Inject(Key.A, true, Notes); StepOnce(); Check(!lastInput.InputActive && beltFrame!.Stats[18] == 0, "文字输入不驱动"); Inject(Key.A, false, Notes);
            Reset(); for (var i = 0; i < 300; i++) StepOnce();
            ApplyButton.Focus(); var c = clicks; Inject(Key.Space, true, ApplyButton); StepOnce();
            Check(lastInput.JumpPressed && beltFrame!.Stats[15] == 1 && beltFrame.Stats[47] > 0, "按钮焦点Space接受原版整身起跳");
            StepOnce(); Check(!lastInput.JumpPressed, "Space不自动重复"); Inject(Key.Space, false, ApplyButton); Check(clicks == c, "Space不误点应用");
            var wholeBodyAirborne = false;
            for (var i = 0; i < 90; i++) { StepOnce(); wholeBodyAirborne |= beltFrame!.Gap > 1e-4; }
            Check(!fault && wholeBodyAirborne, "原版整身起跳规则使真实连续最低点离地");
            Check(ApplyButton.FocusVisualStyle is null && size.FocusVisualStyle is null, "按钮滑条无虚线焦点");
            Inject(Key.D, true, ApplyButton); StepOnce(); Cancel(); StepOnce(); Check(lastInput.CancelForces && lastInput.Horizontal == 0, "取消清除玩法状态");
            Inject(Key.D, true, ApplyButton); StepOnce(); Check(lastInput.Horizontal == 0, "取消后按住的键不补发"); Inject(Key.D, false, ApplyButton);
            model.SelectedIndex = 1; Reset(); for (var i = 0; i < 300; i++) StepOnce(); Check(!fault && !ActiveBelt && oldFrame!.IsGrounded, "原外围半区对照真实落地");
            UpdateForceDetails(); Check(forceLedger is null && forceDetails.Text.Contains("未提供", StringComparison.Ordinal), "旧模块明确显示未提供真实施力");
            model.SelectedIndex = 0;
            Reset(); for (var i = 0; i < 300; i++) StepOnce(); Check(!fault && appearance.RenderedAccessoryCount == 2, "最终落稳和正式配饰恢复无故障");
            var restingHeight = beltFrame!.Height; var restingWidth = beltFrame.Width;
            await Capture("window-rest-before");
            Inject(Key.D, true, ApplyButton); for (var i = 0; i < 90; i++) StepOnce();
            Check(!fault && whaleDriveDiagnostics is { } right && right[3] > .99 && right[4] > 0 && appearance.RenderedAccessoryCount == 2,
                "右行时鲸形受力真实激活并带正式眼睛配饰");
            Check(forceLedger is { } rightForces && rightForces[2] == steps && ForceRowCount(1) is > 0 and <= 16 && ForceRowCount(3) == 64 && ((int)rightForces[5] & 5) == 5,
                "右行ledger包含16材质区间中受力段合力及64个实际塑形积分力");
            await Capture("window-walk-right-forceoverlay");
            await Capture("window-walk-right");
            Inject(Key.D, false, ApplyButton); Inject(Key.A, true, ApplyButton); for (var i = 0; i < 90; i++) StepOnce();
            Check(!fault && whaleDriveDiagnostics is { } left && left[3] < -.99 && left[4] > 0,
                "反向左行时受力方向平滑交换");
            await Capture("window-walk-left");
            Inject(Key.A, false, ApplyButton); for (var i = 0; i < 180; i++) StepOnce();
            Check(!fault && whaleDriveDiagnostics is { } release && Math.Abs(release[3]) < 1e-9 && release[4] == 0 &&
                Math.Abs(beltFrame!.Height / restingHeight - 1) < .02 && Math.Abs(beltFrame.Width / restingWidth - 1) < .02,
                "松手后行走受力撤除且高宽恢复至自身静置2%内");
            await Capture("window-release");
            whaleDrive.IsChecked = false; Reset(); for (var i = 0; i < 300; i++) StepOnce();
            Inject(Key.D, false, ApplyButton);
            Inject(Key.D, true, ApplyButton); var originalDriveApplied = false;
            for (var i = 0; i < 90; i++) { StepOnce(); originalDriveApplied |= beltFrame!.HorizontalForce > 0; }
            Check(!fault && !belt!.WhaleDriveEnabled && whaleDriveDiagnostics is { } disabled && disabled[1] == 0 && disabled[4] == 0 &&
                belt.ShapeGuardEnabled && belt.SpeedSaturationEnabled && originalDriveApplied,
                "关闭鲸形开关后原推动真实运行且保留F和安全保护");
            await Capture("window-original-drive-right");
            Inject(Key.D, false, ApplyButton); whaleDrive.IsChecked = true; Reset(); for (var i = 0; i < 300; i++) StepOnce();
            Check(!fault && belt!.WhaleDriveEnabled && appearance.RenderedAccessoryCount == 2, "恢复鲸形实验开关与外观无故障");
            await Capture("window");
            File.WriteAllText(Path.Combine(session, "walking-review.json"), JsonSerializer.Serialize(new { material = new { size = size.Value, softness = softness.Value, build = build.Value },
                scope = "actual physics frames at one of the eight approved corners; no render deformation or manual acceptance", frames = walkingFrames }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(session, "wpf-smoke.json"), JsonSerializer.Serialize(new { passed = true, material = new { size = size.Value, softness = softness.Value, build = build.Value }, shapeGuard = GuardState(), speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(), checks, method = "shown WPF routed events; not OS keyboard automation", manualAcceptance = false }, new JsonSerializerOptions { WriteIndented = true })); Close();
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(session, "wpf-smoke.json"), JsonSerializer.Serialize(new { passed = false, checks, error = e.ToString(), speedLimit = SpeedLimitState(), whaleDrive = WhaleDriveState(), stepEvidence = StepEvidence() }, new JsonSerializerOptions { WriteIndented = true })); Application.Current.Shutdown(11);
        }
    }

    private sealed class Surface : Canvas
    {
        private readonly LabBeltWindow owner;
        private readonly DebugOverlay overlay;
        internal Surface(LabBeltWindow owner)
        {
            this.owner = owner;
            Children.Add(owner.appearance.View);
            Children.Add(overlay = new DebugOverlay(this) { IsHitTestVisible = false });
            owner.appearance.View.IsHitTestVisible = false;
            owner.appearance.View.RenderTransform = new ScaleTransform(Scale / PhysicsLabAppearancePresenter.WorldToCanvas, Scale / PhysicsLabAppearancePresenter.WorldToCanvas);
            SizeChanged += (_, _) => Refresh();
        }
        internal void Refresh()
        {
            var scale = Scale / PhysicsLabAppearancePresenter.WorldToCanvas;
            var core = Map(owner.CoreX, owner.CoreY);
            SetLeft(owner.appearance.View, core.X - 80*scale); SetTop(owner.appearance.View, core.Y - 66*scale);
            owner.appearance.View.Visibility = owner.showAppearance.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
            overlay.Width = ActualWidth; overlay.Height = ActualHeight;
            InvalidateVisual(); overlay.InvalidateVisual();
        }
        private const double Scale = 120;
        private double Floor => ActualHeight * .78;
        private Point Map(double x, double y) => new(ActualWidth / 2 + (x - owner.cameraX) * Scale, Floor - (y - owner.config.GroundHeight) * Scale);
        private Point Unmap(Point p) => new((p.X - ActualWidth / 2) / Scale + owner.cameraX, owner.config.GroundHeight - (p.Y - Floor) / Scale);
        private StreamGeometry Shape(IReadOnlyList<float> xy, double x = 0, double y = 0, bool spline = false)
        {
            var points = Enumerable.Range(0, xy.Count / 2).Select(i => Map(x + xy[2 * i], y + xy[2 * i + 1])).ToArray();
            var g = new StreamGeometry(); using var c = g.Open();
            if (spline)
            {
                static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                c.BeginFigure(Mid(points[^1], points[0]), true, true);
                for (var i = 0; i < points.Length; i++) c.QuadraticBezierTo(points[i], Mid(points[i], points[(i + 1) % points.Length]), true, false);
            }
            else { c.BeginFigure(points[0], true, true); c.PolyLineTo(points.Skip(1).ToArray(), true, false); }
            return g;
        }
        protected override void OnRender(DrawingContext d)
        {
            d.DrawRectangle(owner.Background, null, new Rect(RenderSize));
            d.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 244, 237)), null, new Rect(0, Floor, ActualWidth, ActualHeight - Floor));
            d.DrawLine(new Pen(LabWindow.Accent, 1), new Point(0, Floor), new Point(ActualWidth, Floor));
            for (var i = (int)Math.Floor(owner.cameraX - ActualWidth / Scale); i < owner.cameraX + ActualWidth / Scale; i++)
            { var p = Map(i, owner.config.GroundHeight); d.DrawLine(new Pen(LabWindow.LineBrush, 1), p, p + new Vector(0, 10)); }
            StreamGeometry? geometry = null;
            if (owner.beltFrame is { } b) geometry = Shape(b.Curve);
            else if (owner.oldFrame is { } o) geometry = Shape(o.Contour, o.CoreX, o.CoreY, true);
            if (geometry is null) return;
            if (owner.showAppearance.IsChecked != true)
                d.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 226, 190)), new Pen(LabWindow.Accent, 1.2), geometry);
        }
        private void DrawOverlay(DrawingContext d)
        {
            if (owner.showReference.IsChecked == true && owner.reference is { } reference)
                d.DrawGeometry(null, new Pen(Brushes.SlateGray, 1), Shape(reference.Contour, owner.CoreX, reference.CoreY, true));
            var core = Map(owner.CoreX, owner.CoreY);
            if (owner.showSkeleton.IsChecked == true)
            {
                if (owner.beltFrame is { } belt)
                {
                    var s = belt.Scaffold; var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, 136, 94, 62)), .7);
                    for (var i = 1; i <= 16; i++)
                    {
                        var p = Map(s[4 * i], s[4 * i + 1]); d.DrawLine(pen, core, p); d.DrawEllipse(LabWindow.Accent, null, p, 2, 2);
                        if (belt.Stats[40] == 1) { var j = i == 16 ? 1 : i + 1; d.DrawLine(pen, p, Map(s[4 * j], s[4 * j + 1])); }
                    }
                    Cross(d, Map(belt.Stats[4], belt.Stats[5]));
                }
                else if (owner.oldNodes.Count > 0)
                {
                    foreach (var n in owner.oldNodes.Skip(1)) { var p = Map(n.X, n.Y); d.DrawLine(new Pen(LabWindow.LineBrush, .8), core, p); d.DrawEllipse(LabWindow.Accent, null, p, 2, 2); }
                    var mass = owner.oldNodes.Sum(n => n.Mass); Cross(d, Map(owner.oldNodes.Sum(n => n.Mass * n.X) / mass, owner.oldNodes.Sum(n => n.Mass * n.Y) / mass));
                }
                d.DrawEllipse(Brushes.DarkOrange, null, core, 4, 4);
            }
            if (owner.showForces.IsChecked == true && owner.forceLedger is { } ledger)
            {
                for (var i = 0; i < (int)ledger[4]; i++)
                {
                    var r = 8 + 6*i; var kind = (int)ledger[r+4];
                    var brush = kind switch { 1 => Brushes.Sienna, 2 => Brushes.SteelBlue, 3 => Brushes.Teal, 4 => Brushes.MediumPurple, _ => LabWindow.Ink };
                    Arrow(d, Map(ledger[r], ledger[r+1]), ledger[r+2], ledger[r+3], brush, owner.forceScale.Value);
                }
            }
            Text(d, "棕红 A/D段合力 · 蓝 W/S段合力 · 青 实际塑形积分力 · 紫 拖动力", 12);
            Text(d, owner.showForces.IsChecked == true ? owner.forceLedger is { } f ? $"主动施力 · 末子步积分前位置 · {owner.forceScale.Value:0} DIP/N · 帧 {f[2]:0}" : "详细施力：当前模块未提供" : "详细施力已隐藏", 34);
            Text(d, "不含重力/接触反力 · 橙 核心 · 蓝十字 真实质心", 56);
            Text(d, owner.fault ? "物理失败，已暂停" : owner.replaying ? "同输入回放中" : owner.paused ? "已暂停" : owner.belt?.WhaleDriveEnabled == true ? "行走受力轮廓 · 无周期摆动" : "原始显示 · 原推动对照", 78);
        }
        private sealed class DebugOverlay(Surface surface) : FrameworkElement
        {
            protected override void OnRender(DrawingContext drawingContext) => surface.DrawOverlay(drawingContext);
        }
        private void Text(DrawingContext d, string text, double y) => d.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(owner.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, LabWindow.Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(12, y));
        private static void Cross(DrawingContext d, Point p) { var pen = new Pen(Brushes.SteelBlue, 1.5); d.DrawLine(pen, p + new Vector(-5, 0), p + new Vector(5, 0)); d.DrawLine(pen, p + new Vector(0, -5), p + new Vector(0, 5)); }
        private static void Arrow(DrawingContext d, Point p, double fx, double fy, Brush brush, double dipPerNewton)
        {
            var v = new Vector(fx * dipPerNewton, -fy * dipPerNewton);
            if (v.Length < 1e-8) return;
            var end = p + v; var unit = v; unit.Normalize(); var side = new Vector(-unit.Y, unit.X);
            var head = Math.Min(5, v.Length * .35); var pen = new Pen(brush, .85);
            d.DrawEllipse(brush, null, p, 1.2, 1.2);
            d.DrawLine(pen, p, end); d.DrawLine(pen, end, end - unit * head + side * head * .5); d.DrawLine(pen, end, end - unit * head - side * head * .5);
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); Focus(); if (owner.fault || owner.paused) return;
            var p = Unmap(e.GetPosition(this)); if (Math.Abs(p.X - owner.CoreX) > 1.3 || Math.Abs(p.Y - owner.CoreY) > 1.3) return;
            owner.Cancel(); owner.dragging = true; owner.dragX = p.X; owner.dragY = p.Y; CaptureMouse(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (!owner.dragging) return; var p = Unmap(e.GetPosition(this)); owner.dragX = p.X; owner.dragY = p.Y; }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); if (owner.dragging) { owner.Cancel(); e.Handled = true; } }
        protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); owner.dragging = false; owner.input.Cancel(); }
    }
}
