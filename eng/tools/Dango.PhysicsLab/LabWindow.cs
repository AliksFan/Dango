using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.PhysicsLab;

internal sealed class LabWindow : Window
{
    internal static readonly double FixedDt = TimeSpan.FromSeconds(1d / 60).TotalSeconds;
    internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(68, 61, 52));
    internal static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(136, 94, 62));
    internal static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(233, 228, 218));
    internal static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(253, 252, 249));
    private readonly LabModules modules;
    private readonly string session;
    private readonly LabInput input = new();
    private readonly LabMetrics metrics = new(1200, FixedDt);
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ComboBox drive = new(), replay = new(), groundModel = new();
    private readonly Slider bias = new() { Minimum = -1, Maximum = 1, TickFrequency = .25, IsSnapToTickEnabled = true, Value = 0 };
    private readonly Slider size = new() { Minimum = 80, Maximum = 120, Value = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly Slider softness = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly Slider build = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly CheckBox frictionOff = new() { Content = new TextBlock { Text = "基础摩擦设零（保留接地制动）", TextWrapping = TextWrapping.Wrap } };
    private readonly CheckBox skeleton = new() { Content = "节点、弹簧与质心", IsChecked = true };
    private readonly CheckBox polygon = new() { Content = "显示原始节点连线", IsChecked = false };
    private readonly CheckBox displaySmooth = new() { Content = "显示平滑（物理保持原样）" };
    private readonly CheckBox rawOutline = new() { Content = "叠加原始轮廓", IsChecked = false };
    private readonly LabDisplaySmoothing smoothing = new();
    private readonly LabDisplayMotionGate displayMotion = new();
    private readonly Queue<DisplaySample> displayTrace = new();
    private readonly Queue<object> regionTrace = new();
    private readonly TextBlock information = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 22 };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock applied = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly LabSurface surface;
    private readonly LabChart chart;
    private readonly Button platformButton;
    private readonly ScrollViewer sidebarScroll;
    private DangoPhysicsWorld? world;
    private DangoEntityId entity;
    private DangoSelfPhysicsParameters configuration = DangoBodyMaterial.Configure(DangoBodyControls.Default);
    private DangoLabDrive activeDrive;
    private double activeBias, accumulator, previousTime, cameraX, dragX, dragY, discardedSeconds;
    private bool paused, dragging, initialized, replaying;
    private int replayFrame, activeReplay, uiTicks, facing = 1, interactionCount;
    private LabInputSnapshot lastInput;
    private DangoBodyState? body;
    private DangoBodyState? displayBody;
    private IReadOnlyList<DangoLabNode> nodes = [];
    private DangoLabStats? stats;
    private DangoLabGroundStats? groundStats;
    private DangoLabRegionStats? regionStats;
    private DangoLabArcStats? arcStats;
    private readonly Queue<object> arcTrace = new();
    private readonly Queue<object> groundTrace = new();
    private bool activeEnvelope;
    internal Button ApplyButton { get; }
    internal Button PauseButton { get; }
    internal TextBox Notes { get; } = new() { MinHeight = 34, MaxLength = 500, ToolTip = "输入时不控制团子；内容不导出、不保存" };
    internal bool HasFault { get; private set; }
    internal int NativeSteps { get; private set; }
    internal int ButtonClicks { get; private set; }
    internal int InteractionCount => interactionCount;
    internal bool IsContinuousGround => activeEnvelope;
    internal bool IsElasticArcs => world?.LabGroundModel == DangoLabGroundModel.ElasticArcs;
    internal LabInputSnapshot CurrentInput => lastInput;
    internal DangoBodyState? CurrentBody => body;
    internal DangoBodyState? DisplayBody => displayBody;
    internal bool DisplaySmoothingEnabled => displaySmooth.IsChecked == true;
    internal DangoLabDrive CurrentDrive => activeDrive;
    internal DangoLabRegionStats? CurrentRegionStats => regionStats;
    internal IReadOnlyList<LabMetricSample> Samples => metrics.Samples;
    internal Slider TestSlider => bias;

    internal LabWindow(LabModules modules, string session, bool useEnvelope = false, bool useRegions = false, bool useSmoothing = false, bool useArcs = false)
    {
        this.modules = modules; this.session = session;
        displaySmooth.IsChecked = useSmoothing;
        Title = "团子 · 物理实验室 · 步态关闭";
        Width = Math.Min(1220, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 32); MinWidth = 940; MinHeight = 660;
        Background = BackgroundBrush;
        Foreground = Ink; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        InputMethod.SetIsInputMethodEnabled(this, false);
        InputMethod.SetIsInputMethodEnabled(Notes, true);
        SetupStyles();
        surface = new LabSurface(this) { Focusable = true, MinHeight = 260, ClipToBounds = true };
        chart = new LabChart(this) { Height = 130, Margin = new Thickness(0, 8, 0, 0) };
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "物理实验室", FontSize = 21, Margin = new Thickness(0, 0, 0, 6) });
        heading.Children.Add(new TextBlock { Text = "A / D 移动   W 拉伸／上拉（随模式）   S 向下压   Space 原跳跃   F 平台开关（离散接地）   鼠标拖动团子", Foreground = Accent });
        heading.Children.Add(new TextBlock { Text = "原有步态关闭 · 相机纵向固定 · 显示平滑可独立切换，原始物理持续记录", FontSize = 12, Margin = new Thickness(0, 5, 0, 12) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        var tools = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
        var scroll = new ScrollViewer { Content = tools, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        sidebarScroll = scroll;
        var footer = new StackPanel { Margin = new Thickness(0,8,10,0) };
        var sidebar = new DockPanel(); DockPanel.SetDock(footer,Dock.Bottom); sidebar.Children.Add(footer); sidebar.Children.Add(scroll);
        columns.Children.Add(sidebar);
        Label(tools, "显示"); tools.Children.Add(displaySmooth); tools.Children.Add(rawOutline);
        displaySmooth.ToolTip = "60毫秒高度滤波；原始物理持续记录。跳跃、无实际地面接触及拖动时显示原帧。";
        Label(tools, "外围与接地模型");
        groundModel.Items.Add("离散圆节点（原外围）"); groundModel.Items.Add("连续轮廓包络（旧候选）");
        groundModel.Items.Add("弹性弧段（新实验）");
        groundModel.SelectedIndex = useArcs ? 2 : useEnvelope ? 1 : 0;
        groundModel.DropDownOpened += (_, _) => CancelInput();
        groundModel.DropDownClosed += (_, _) => input.ReconcileHeld(HeldKeys());
        groundModel.SelectionChanged += (_, _) => message.Text = "接地模型待应用；点击应用参数或同输入回放。";
        tools.Children.Add(groundModel);
        tools.Children.Add(new TextBlock { Text = "弧段参与弯曲、伸长及多点接地；内部径向骨架保留。仅单体平地，切换后需应用。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Label(tools, "施力方式");
        drive.Items.Add("核心单点（基线）"); drive.Items.Add("全身按实际质量分配"); drive.Items.Add("按高度偏置分配");
        drive.Items.Add("移动合力作用高度（两点分摊）");
        drive.Items.Add("方向外侧 1/2 · 区域共同受力");
        drive.SelectedIndex = useRegions ? 4 : 0;
        drive.DropDownOpened += (_, _) => CancelInput();
        drive.DropDownClosed += (_, _) => input.ReconcileHeld(HeldKeys());
        tools.Children.Add(drive);
        Label(tools, "上／下侧偏置：−1 下侧，+1 上侧"); tools.Children.Add(bias);
        var biasValue = new TextBlock { Text = "当前选值 0.00", FontSize = 12 };
        bias.ValueChanged += (_, _) => biasValue.Text = $"当前选值 {bias.Value:+0.00;-0.00;0.00}"; tools.Children.Add(biasValue);
        tools.Children.Add(new TextBlock { Text = "两点模式把合力移至核心上／下指定比例；分布模式改变全身权重。两者均记录真实作用高度与力矩。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
        tools.Children.Add(new TextBlock { Text = "方向区域按当前真实轮廓范围划分：A 左半、D 右半、W/S 都取上半。区域共同受力，W 向上、S 向下，总力预算保持。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        AddSlider(tools, "大小", size); AddSlider(tools, "柔软度", softness); AddSlider(tools, "落地体态（低值更扁）", build);
        tools.Children.Add(frictionOff);
        ApplyButton = Button("↻  应用参数并重置", () => { ButtonClicks++; ResetWorld(); }); footer.Children.Add(ApplyButton);
        footer.Children.Add(applied);
        Label(tools, "同输入回放");
        replay.Items.Add("走停与左右反向 · 31 秒"); replay.Items.Add("W/S、相反键与组合跳跃 · 22 秒"); replay.Items.Add("短按反向 · 9 秒"); replay.SelectedIndex = 0;
        replay.DropDownOpened += (_, _) => CancelInput();
        replay.DropDownClosed += (_, _) => input.ReconcileHeld(HeldKeys());
        tools.Children.Add(replay);
        tools.Children.Add(Button("▷  从同一起点回放", StartReplay));
        PauseButton = Button("Ⅱ  暂停", TogglePause); tools.Children.Add(PauseButton);
        tools.Children.Add(Button("↓  导出当前物理记录", Export));
        Label(tools, "观察"); tools.Children.Add(skeleton); tools.Children.Add(polygon);
        displaySmooth.Click += (_, _) => SetDisplaySmoothing(displaySmooth.IsChecked == true);
        rawOutline.Click += (_, _) => surface.InvalidateVisual();
        tools.Children.Add(new TextBlock { Text = "平滑仅处理接地时的显示高度；跳跃、离地与拖动立即显示原帧。骨架和蓝色质心始终表示真实物理。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        skeleton.Click += (_, _) => surface.InvalidateVisual(); polygon.Click += (_, _) => surface.InvalidateVisual();
        platformButton = Button("▱  放置／移除平台", TogglePlatform); tools.Children.Add(platformButton);
        tools.Children.Add(new TextBlock { Text = "F 只在左侧地面开关附近操作平台；远处无动作。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Label(tools, "文字输入测试（不保存）"); tools.Children.Add(Notes);
        tools.Children.Add(new TextBlock { Text = "非文字控件上的 WASD / Space 仍控制团子；按钮用 Enter 或鼠标执行。失焦、拖动或输入文字会清按键。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        tools.Children.Add(message);
        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(right, 2); columns.Children.Add(right);
        var border = new Border { BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = surface };
        right.Children.Add(border); Grid.SetRow(chart, 1); right.Children.Add(chart);
        Grid.SetRow(information, 2); information.Margin = new Thickness(0, 8, 0, 0); right.Children.Add(information);
        Content = root;
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HandleKeyDown), true);
        AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(HandleKeyUp), true);
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        { if (IsText(e.NewFocus as DependencyObject)) CancelInput(); }), true);
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        { if (Ancestor<Slider>(e.OriginalSource as DependencyObject) is not null || Ancestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null) CancelInput(); }), true);
        Deactivated += (_, _) => CancelInput();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) CancelInput(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelInput(); };
        Activated += (_, _) => input.ReconcileHeld(HeldKeys());
        Closed += (_, _) => { timer.Stop(); world?.Dispose(); world = null; File.WriteAllText(Path.Combine(session, "exit.json"), "{\"closed\":true}"); };
        Loaded += (_, _) =>
        {
            initialized = true; ResetWorld(); surface.Focus();
            previousTime = clock.Elapsed.TotalSeconds; timer.Interval = TimeSpan.FromMilliseconds(8);
            timer.Tick += Tick; timer.Start();
            File.WriteAllText(Path.Combine(session, "started.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, visible = IsVisible, gait = false, nativeLoaded = world is not null, groundModel = world?.LabGroundModel.ToString(), drive = activeDrive.ToString(), regionDenominator = world?.LabRegionDenominator, displaySmoothing = DisplaySmoothingEnabled, error = HasFault }));
        };
    }

    private void SetupStyles() => ApplyStyles(Resources);

    internal static void ApplyStyles(ResourceDictionary resources)
    {
        foreach (var type in new[] { typeof(Button), typeof(Slider), typeof(ComboBox), typeof(CheckBox), typeof(TextBox) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter(FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 4, 0, 4)));
            if (type == typeof(Button) || type == typeof(ComboBox) || type == typeof(TextBox))
            {
                style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
                style.Setters.Add(new Setter(Control.MinHeightProperty, 38d));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
                style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(243, 239, 232))));
            }
            var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, SystemParameters.HighContrast ? SystemColors.HighlightBrush : Accent));
            focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            style.Triggers.Add(focus); resources.Add(type, style);
        }
        var scrollbar = new Style(typeof(ScrollBar)); scrollbar.Setters.Add(new Setter(WidthProperty, 7d)); resources.Add(typeof(ScrollBar), scrollbar);
    }

    private static void Label(Panel target, string text) => target.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 14, 0, 3), Foreground = Accent });
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action(); return button;
    }
    private static void AddSlider(Panel target, string text, Slider slider)
    {
        var label = new TextBlock(); void Refresh() => label.Text = $"{text}  {slider.Value:0}";
        Refresh(); slider.ValueChanged += (_, _) => Refresh(); target.Children.Add(label); target.Children.Add(slider);
    }

    internal void ResetWorld()
    {
        if (!initialized) return;
        CancelInput(); replaying = false; paused = false; PauseButton.Content = "Ⅱ  暂停";
        world?.Dispose(); world = null; HasFault = false; body = displayBody = null; nodes = []; stats = null; groundStats = null; regionStats = null; arcStats = null;
        groundTrace.Clear(); regionTrace.Clear(); arcTrace.Clear(); displayTrace.Clear(); smoothing.Reset(); displayMotion.Reset();
        try
        {
            configuration = DangoBodyMaterial.Configure(new DangoBodyControls(size.Value, softness.Value, build.Value));
            if (frictionOff.IsChecked == true) configuration = configuration with { GroundFriction = 0 };
            activeDrive = (DangoLabDrive)drive.SelectedIndex; activeBias = bias.Value;
            var regions = (int)activeDrive >= 4;
            var selectedModule = groundModel.SelectedIndex == 2 ? modules.Arcs : groundModel.SelectedIndex == 1
                ? regions ? modules.RegionsEnvelope : modules.Envelope
                : regions ? modules.Regions : modules.Discrete;
            world = DangoPhysicsWorld.Create(selectedModule, 1, configuration); world.RequireBodyMaterialModel(1); world.RequireLab();
            activeEnvelope = world.LabGroundModel != DangoLabGroundModel.DiscreteNodes;
            if ((int)world.LabGroundModel != groundModel.SelectedIndex) throw new InvalidOperationException("接地模型与所选实验模块不一致。");
            if (regions && world.LabRegionDenominator != 2) throw new InvalidOperationException("本轮仅允许半侧区域施力，实验DLL版本不符。");
            entity = world.Add(0, 0); world.SetHorizontalBounds(-256, 256); metrics.Clear(); cameraX = 0;
            accumulator = 0; NativeSteps = 0; replayFrame = 0; facing = 1; interactionCount = 0;
            Title = IsElasticArcs ? "团子 · 弹性弧段实验 · 步态关闭" : regions ? "团子 · 原外围对照 · 半侧共同施力 · 步态关闭" : DisplaySmoothingEnabled ? "团子 · 显示平滑实验 · 原驱动 · 步态关闭" : activeEnvelope ? "团子 · 连续接地候选 · 步态关闭" : "团子 · 离散接地基线 · 步态关闭";
            platformButton.IsEnabled = !activeEnvelope;
            applied.Text = $"当前：{groundModel.SelectedItem}\n{drive.SelectedItem}\n大小 {size.Value:0} / 软 {softness.Value:0} / 体态 {build.Value:0} · 摩擦 {configuration.GroundFriction:0.00}";
            File.AppendAllText(Path.Combine(session, "world-models.jsonl"), JsonSerializer.Serialize(new
            {
                model = world.LabGroundModel.ToString(), regionDenominator = world.LabRegionDenominator,
                nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(selectedModule))),
                activeDrive, activeBias, configuration, fixedDt = FixedDt, displaySmoothing = DisplaySmoothingEnabled,
            }) + Environment.NewLine);
            message.Text = "参数已应用；可直接操作团子。";
            StepOnce(); Paint();
        }
        catch (Exception error) { Fault(error); }
    }

    private void Tick(object? sender, EventArgs args)
    {
        var now = clock.Elapsed.TotalSeconds; var elapsed = now - previousTime; previousTime = now;
        if (paused || HasFault || !IsActive || !IsVisible || WindowState == WindowState.Minimized) { accumulator = 0; return; }
        accumulator += Math.Min(elapsed, .25);
        var steps = 0;
        while (accumulator >= FixedDt && steps++ < 8)
        { StepOnce(); accumulator -= FixedDt; if (HasFault) break; }
        if (accumulator > FixedDt * 8) { discardedSeconds += accumulator; accumulator = 0; }
        Paint();
    }

    internal void StepOnce()
    {
        if (world is null || HasFault) return;
        try
        {
            lastInput = replaying ? ReplayInput() : input.Consume();
            if (lastInput.Horizontal != 0) facing = Math.Sign(lastInput.Horizontal);
            var intent = lastInput.ToPhysicsInput(entity);
            if (dragging) intent = new DangoLabInput(entity, DragActive: true, DragTargetX: dragX, DragTargetY: dragY, CancelForces: true);
            var frame = world.StepLab(FixedDt, [intent], activeDrive, activeBias);
            if (!frame.Succeeded) throw new InvalidOperationException($"物理故障 {frame.ErrorCode}；已暂停，需显式重置。");
            var currentBody = frame.Bodies.Single();
            var currentNodes = world.ReadLabNodes(entity); var currentStats = world.ReadLabStats(entity);
            var currentGround = world.ReadLabGroundStats(entity);
            if (activeEnvelope && (currentGround is null || currentGround.SolverFault || currentGround.MaximumPenetrationAfterSolve > .0001f))
                throw new InvalidOperationException("当前曲面模型未通过本帧几何安全检查，已暂停。");
            body = currentBody; nodes = currentNodes; stats = currentStats; groundStats = currentGround;
            regionStats = world.ReadLabRegionStats(entity);
            arcStats = world.ReadLabArcStats(entity);
            if (IsElasticArcs && (arcStats is null || arcStats.Fault || arcStats.MaximumPenetration > .0001f))
                throw new InvalidOperationException("弹性弧段未通过本帧安全检查，已暂停。");
            if (arcStats is not null)
            {
                arcTrace.Enqueue(new { tick = NativeSteps, diagnostics = arcStats });
                while (arcTrace.Count > 1200) arcTrace.Dequeue();
            }
            if ((int)activeDrive >= 4 && regionStats is null) throw new InvalidOperationException("方向施力模块缺少区域诊断，已暂停。");
            var displayBypass = displayMotion.ShouldBypass(LabDisplayMotionGate.HasFloorContact(currentNodes,
                configuration.GroundHeight, configuration.NodeRadius, configuration.RadialRestLength, currentGround),
                lastInput.JumpPressed, dragging, currentNodes[0].VelocityY, FixedDt, configuration.JumpCooldownMilliseconds/1000);
            displayBody = smoothing.Update(currentBody, FixedDt, DisplaySmoothingEnabled, displayBypass);
            metrics.Record(body, nodes, stats); NativeSteps++;
            var shown = LabGroundExperiments.CurveExtent(displayBody.Contour, 1);
            displayTrace.Enqueue(new(NativeSteps, DisplaySmoothingEnabled, shown.Maximum - shown.Minimum,
                displayBody.CoreY, displayBody.CoreY + shown.Minimum));
            while (displayTrace.Count > 1200) displayTrace.Dequeue();
            if (regionStats is not null)
            {
                regionTrace.Enqueue(new { tick = NativeSteps, diagnostics = regionStats });
                while (regionTrace.Count > 1200) regionTrace.Dequeue();
            }
            if (currentGround is not null)
            {
                groundTrace.Enqueue(new { tick = NativeSteps, diagnostics = currentGround });
                while (groundTrace.Count > 1200) groundTrace.Dequeue();
            }
            if (lastInput.InteractPressed && Math.Abs(currentBody.CoreX + 2.5) < 1.1) TogglePlatform();
            if (Math.Abs(currentBody.CoreX - cameraX) > 1.5) cameraX += currentBody.CoreX - cameraX - Math.Sign(currentBody.CoreX - cameraX) * 1.5;
        }
        catch (Exception error) { Fault(error); }
    }

    private LabInputSnapshot ReplayInput()
    {
        var f = replayFrame++;
        var h = 0d; var down = 0d; var stretch = 0d; var jump = false; var active = false;
        if (activeReplay == 0)
        {
            h = f is >= 300 and < 900 ? 1 : f is >= 1080 and < 1680 ? -1 : 0;
            active = h != 0;
            if (f >= 1860) FinishReplay();
        }
        else if (activeReplay == 2)
        {
            h = f is >= 300 and < 330 ? 1 : f is >= 330 and < 360 ? -1 : 0; active = h != 0;
            if (f >= 540) FinishReplay();
        }
        else
        {
            stretch = f is >= 300 and < 420 or >= 900 and < 960 ? 1 : 0;
            down = f is >= 540 and < 660 or >= 1020 and < 1080 ? 1 : 0;
            active = stretch != 0 || down != 0 || f is >= 780 and < 900;
            jump = f is 900 or 1020 or 1140;
            if (f >= 1320) FinishReplay();
        }
        return new LabInputSnapshot(h, down, stretch, active || jump, jump, false,
            activeReplay == 1 && f is >= 780 and < 840);
    }

    private void FinishReplay()
    {
        replaying = false; CancelInput();
        message.Text = "回放完成；当前数据可导出，再切换施力方式重复。";
    }

    private void StartReplay()
    {
        ResetWorld(); if (HasFault) return;
        activeReplay = replay.SelectedIndex; replaying = true; replayFrame = 0;
        message.Text = "固定输入回放中；按任一玩法键、拖动或失焦可停止。";
    }

    private void TogglePause()
    {
        CancelInput(); paused = !paused; PauseButton.Content = paused ? "▷  继续" : "Ⅱ  暂停";
    }

    private void TogglePlatform()
    {
        if (world is null || HasFault) return;
        CancelInput();
        if (activeEnvelope) { message.Text = "当前曲面模型仅验证单体平地；平台交互请切回离散接地。"; return; }
        try
        {
            if (world.StaticPlatform is null) world.SetStaticPlatform(new DangoStaticPlatformDefinition(2.8, -.30, 2.0, .22, configuration.GroundFriction));
            else world.ClearStaticPlatform();
            interactionCount++; message.Text = world.StaticPlatform is null ? "已移除真实平台。" : "已放置真实平台，向右可跳上去。";
            surface.InvalidateVisual();
        }
        catch (DangoPhysicsException error) { message.Text = "平台操作被拒绝：" + error.Message; }
    }

    private void Export()
    {
        CancelInput();
        var path = Path.Combine(session, $"trace-{DateTime.UtcNow:HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            gait = false, groundModel = world?.LabGroundModel.ToString(), regionDenominator = world?.LabRegionDenominator, mode = activeDrive.ToString(), bias = activeBias, configuration,
            interactionCount, discardedSeconds, metrics = metrics.Export(), groundDiagnostics = groundTrace.ToArray(),
            displaySmoothing = DisplaySmoothingEnabled, displaySmoothingTauSeconds = LabDisplaySmoothing.TimeConstantSeconds,
            display = displayTrace.ToArray(), regionDiagnostics = regionTrace.ToArray(), arcDiagnostics = arcTrace.ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        message.Text = $"已导出 {metrics.Samples.Count} 个紧凑物理样本。";
    }

    internal void CancelInput()
    {
        input.Cancel(); replaying = false;
        smoothing.Reset();
        displayMotion.Reset();
        if (dragging) { dragging = false; surface.ReleaseMouseCapture(); }
    }

    private void Fault(Exception error)
    {
        HasFault = true; paused = true; CancelInput(); message.Text = "已暂停：" + error.Message;
        File.WriteAllText(Path.Combine(session, "physics-failure.json"), JsonSerializer.Serialize(new { nativeSteps = NativeSteps, error = error.ToString() }));
    }

    internal void SetDisplaySmoothing(bool enabled)
    {
        displaySmooth.IsChecked = enabled; smoothing.Reset(); displayBody = body;
        surface.InvalidateVisual();
        File.AppendAllText(Path.Combine(session, "display-settings.jsonl"), JsonSerializer.Serialize(new
        { tick = NativeSteps, enabled, timeConstantSeconds = LabDisplaySmoothing.TimeConstantSeconds }) + Environment.NewLine);
    }

    internal void Paint(bool forceInformation = false)
    {
        surface.InvalidateVisual(); chart.InvalidateVisual();
        if (body is null || stats is null || nodes.Count == 0) return;
        if (++uiTicks % 5 != 0 && NativeSteps > 3 && !forceInformation) return;
        var extent = LabGroundExperiments.CurveExtent(body.Contour, 1);
        var height = extent.Maximum - extent.Minimum;
        var shownExtent = LabGroundExperiments.CurveExtent((displayBody ?? body).Contour, 1);
        var mass = nodes.Sum(n => n.Mass); var comY = nodes.Sum(n => n.Y * n.Mass) / mass;
        information.Text = FormattableString.Invariant(
            $"物理高 {height * 72:0.00} / 显示高 {(shownExtent.Maximum-shownExtent.Minimum) * 72:0.00} DIP   物理质心Y {comY:0.0000}   近地计数 {body.GroundContactCount}   速度 {body.MaximumSpeed:0.000}") + "\n" +
            FormattableString.Invariant($"水平力 {stats.ActualForceX:0.000}   控制力矩 {stats.TorqueAboutCenterOfMass:0.0000}   W力预算 {stats.StretchForceBudgetUsed:0.000}/{stats.MaximumStretchBudget:0.000}   帧 {NativeSteps}") +
            (replaying ? $"   回放 {replayFrame * FixedDt:0.0}s" : "");
        if (groundStats is { } ground)
            information.Text += FormattableString.Invariant($"\n曲面间隙 {ground.Gap * 72:0.00000} DIP   接地位置修正 {ground.MaximumPositionCorrection * 72:0.00000} DIP   骨架修正 {ground.MaximumShapePositionCorrection * 72:0.00000} DIP");
        if (regionStats is { } region)
            information.Text += FormattableString.Invariant($"\n区域合力 ({region.ActualForceX:0.000}, {region.ActualForceY:0.000})   水平节点 {System.Numerics.BitOperations.PopCount((uint)region.HorizontalMask)} / 上部节点 {System.Numerics.BitOperations.PopCount((uint)region.VerticalMask)}");
        if (arcStats is { } arc)
            information.Text += FormattableString.Invariant($"\n弧段伸长能 {arc.StretchEnergy:0.00000} / 弯曲能 {arc.BendingEnergy:0.00000}   有效接触 {arc.ActiveContacts}   阻尼功率 {arc.DampingPower:0.00000}");
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var modifiers = CurrentModifiers();
        if (modifiers != LabModifiers.None) { CancelInput(); return; }
        if (!MapKey(key, out var mapped)) return;
        var blocked = GameplayBlocked();
        if (replaying) { replaying = false; input.Cancel(); }
        input.KeyDown(mapped, e.IsRepeat, modifiers, blocked);
        if (!blocked) e.Handled = true;
    }

    private void HandleKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (!MapKey(key, out var mapped)) return;
        input.KeyUp(mapped);
        if (!GameplayBlocked()) e.Handled = true;
    }

    private bool GameplayBlocked() => IsText(Keyboard.FocusedElement as DependencyObject) || dragging ||
        Mouse.Captured is not null || drive.IsDropDownOpen || replay.IsDropDownOpen || groundModel.IsDropDownOpen;

    private static LabModifiers CurrentModifiers() =>
        ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? LabModifiers.Shift : 0) |
        ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? LabModifiers.Control : 0) |
        ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? LabModifiers.Alt : 0) |
        ((Keyboard.Modifiers & ModifierKeys.Windows) != 0 ? LabModifiers.Windows : 0);

    private static IEnumerable<LabKey> HeldKeys()
    {
        foreach (var key in new[] { Key.A, Key.D, Key.W, Key.S, Key.Space, Key.F })
            if (Keyboard.IsKeyDown(key) && MapKey(key, out var mapped)) yield return mapped;
    }

    private static bool MapKey(Key key, out LabKey mapped)
    {
        mapped = key switch { Key.A => LabKey.Left, Key.D => LabKey.Right, Key.W => LabKey.Stretch,
            Key.S => LabKey.Squash, Key.Space => LabKey.Jump, Key.F => LabKey.Interact, _ => (LabKey)(-1) };
        return (int)mapped >= 0;
    }
    private static bool IsText(DependencyObject? value) => Ancestor<TextBoxBase>(value) is not null || Ancestor<PasswordBox>(value) is not null || Ancestor<ComboBox>(value)?.IsEditable == true;
    private static T? Ancestor<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value is not null)
        {
            if (value is T found) return found;
            value = value is Visual ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
        }
        return null;
    }

    internal void InjectKey(Key key, bool down, UIElement focus)
    {
        focus.Focus();
        var source = PresentationSource.FromVisual(this)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
        focus.RaiseEvent(args);
        if (!args.Handled)
        {
            args.RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent;
            focus.RaiseEvent(args);
        }
    }

    internal void PrepareSmoke() { timer.Stop(); CancelInput(); }
    internal void SelectGroundForSmoke(bool continuous) { groundModel.SelectedIndex = continuous ? 1 : 0; ResetWorld(); }
    internal void SelectArcsForSmoke() { groundModel.SelectedIndex = 2; ResetWorld(); }
    internal void SelectDriveForSmoke(DangoLabDrive mode) { drive.SelectedIndex = (int)mode; ResetWorld(); }
    internal void ShowControlsForSmoke() => sidebarScroll.ScrollToTop();
    private sealed record DisplaySample(int Tick, bool Enabled, double Height, double CoreY, double Bottom);

    private sealed class LabSurface(LabWindow owner) : FrameworkElement
    {
        private const double Scale = 120;
        private double Floor => ActualHeight * .77;
        private Point Map(double x, double y) => new(ActualWidth / 2 + (x - owner.cameraX) * Scale, Floor - (y - owner.configuration.GroundHeight) * Scale);
        internal Point WorldToScreen(double x, double y) => Map(x, y);
        private Point Unmap(Point p) => new((p.X - ActualWidth / 2) / Scale + owner.cameraX, owner.configuration.GroundHeight - (p.Y - Floor) / Scale);
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            drawing.DrawRectangle(owner.Background, null, new Rect(RenderSize));
            var thin = new Pen(LineBrush, 1);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(247, 244, 237)), null, new Rect(0, Floor, ActualWidth, Math.Max(0, ActualHeight - Floor)));
            drawing.DrawLine(new Pen(Accent, 1.3), new Point(0, Floor), new Point(ActualWidth, Floor));
            for (var i = (int)Math.Floor(owner.cameraX - ActualWidth / Scale); i < owner.cameraX + ActualWidth / Scale; i++)
            { var p = Map(i, owner.configuration.GroundHeight); drawing.DrawLine(thin, p, p + new Vector(0, 10)); }
            if (owner.world?.StaticPlatform is { } platform)
            {
                var corner = Map(platform.CenterX - platform.Width / 2, platform.CenterY + platform.Height / 2);
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(208, 184, 151)), new Pen(Accent, 1), new Rect(corner.X, corner.Y, platform.Width * Scale, platform.Height * Scale), 3, 3);
            }
            var lever = Map(-2.5, owner.configuration.GroundHeight);
            drawing.DrawLine(new Pen(Accent, 2), lever, lever + new Vector(0, -30));
            Text(drawing, "F 平台开关", lever + new Vector(-32, 15), 12, Accent);
            if (owner.body is not { } physical) { Text(drawing, "物理世界准备中", new Point(20, 20), 14, Ink); return; }
            var body = owner.displayBody ?? physical;
            var points = Enumerable.Range(0, body.PointCount).Select(i => Map(body.CoreX + body.Contour[2 * i], body.CoreY + body.Contour[2 * i + 1])).ToArray();
            if (points.Length < 3) return;
            var geometry = ContourGeometry(points, owner.polygon.IsChecked == true);
            drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 226, 190)), new Pen(Accent, 1.2), geometry);
            if (owner.rawOutline.IsChecked == true)
            {
                var original = Enumerable.Range(0, physical.PointCount).Select(i => Map(physical.CoreX + physical.Contour[2*i], physical.CoreY + physical.Contour[2*i+1])).ToArray();
                drawing.DrawGeometry(null, new Pen(Brushes.SlateGray, 1), ContourGeometry(original, owner.polygon.IsChecked == true));
            }
            var core = Map(body.CoreX, body.CoreY);
            var physicalCore = Map(physical.CoreX, physical.CoreY);
            var radius = Math.Max(12, (points.Max(p => p.X) - points.Min(p => p.X)) / 2);
            var gazeX = owner.lastInput.Horizontal == 0 ? owner.facing * 3 : owner.lastInput.Horizontal * 5;
            var gazeY = (owner.lastInput.Downward - owner.lastInput.Stretch) * 5;
            foreach (var side in new[] { -1, 1 })
            {
                var eye = core + new Vector(side * radius * .26 + gazeX, -4 + gazeY);
                if (geometry.FillContains(eye)) drawing.DrawEllipse(Ink, null, eye, 3.4, 5);
            }
            if (owner.skeleton.IsChecked == true)
            {
                foreach (var node in owner.nodes.Skip(1))
                {
                    var p = Map(node.X, node.Y); drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 136, 94, 62)), .7), physicalCore, p);
                    drawing.DrawEllipse(node.ContactCount > 0 ? Brushes.SeaGreen : Accent, null, p, 2.4, 2.4);
                    if (owner.regionStats is { } region && (((region.HorizontalMask | region.VerticalMask) >> (node.Index-1)) & 1) != 0)
                        drawing.DrawEllipse(null, new Pen(Brushes.IndianRed, 1.5), p, 5, 5);
                }
                drawing.DrawEllipse(Brushes.DarkOrange, null, physicalCore, 4, 4);
                var mass = owner.nodes.Sum(n => n.Mass);
                if (mass > 0)
                {
                    var com = Map(owner.nodes.Sum(n => n.X * n.Mass) / mass, owner.nodes.Sum(n => n.Y * n.Mass) / mass);
                    drawing.DrawLine(new Pen(Brushes.SteelBlue, 1.5), com + new Vector(-5, 0), com + new Vector(5, 0));
                    drawing.DrawLine(new Pen(Brushes.SteelBlue, 1.5), com + new Vector(0, -5), com + new Vector(0, 5));
                }
                if (owner.stats is { } stats && Math.Abs(stats.ActualForceX) > 1e-6)
                {
                    var start = Map(body.CoreX, stats.ForceWeightedY); var end = start + new Vector(Math.Sign(stats.ActualForceX) * 45, 0);
                    drawing.DrawLine(new Pen(Brushes.IndianRed, 2), start, end);
                    drawing.DrawLine(new Pen(Brushes.IndianRed, 2), end, end + new Vector(-Math.Sign(stats.ActualForceX) * 7, -4));
                    drawing.DrawLine(new Pen(Brushes.IndianRed, 2), end, end + new Vector(-Math.Sign(stats.ActualForceX) * 7, 4));
                }
                if (owner.groundStats is { ContactCount: > 0 } contact)
                {
                    var foot = Map(contact.FootX, contact.FootY);
                    drawing.DrawEllipse(Brushes.SeaGreen, null, foot, 4, 4);
                    drawing.DrawLine(new Pen(Brushes.SeaGreen, 1.5), foot, foot + new Vector(0, -18));
                }
                if (owner.regionStats is { } regionForce && Math.Abs(regionForce.ActualForceY) > 1e-6)
                {
                    var start = Map(regionForce.VerticalPointX, regionForce.VerticalPointY);
                    var end = start + new Vector(0, -Math.Sign(regionForce.ActualForceY)*35);
                    drawing.DrawLine(new Pen(Brushes.IndianRed, 2), start, end);
                    drawing.DrawLine(new Pen(Brushes.IndianRed, 2), end, end + new Vector(-4, Math.Sign(regionForce.ActualForceY)*7));
                    drawing.DrawLine(new Pen(Brushes.IndianRed, 2), end, end + new Vector(4, Math.Sign(regionForce.ActualForceY)*7));
                }
            }
            Text(drawing, "物理诊断 · 橙：核心   蓝：质心   红：施力点" + (owner.activeEnvelope ? "   绿：接地点" : ""), new Point(12, 12), 12, Ink);
            Text(drawing, (owner.replaying ? "固定回放中" : owner.paused ? "已暂停" : "步态关闭") + (owner.DisplaySmoothingEnabled ? " · 显示平滑开" : " · 原始显示"), new Point(12, 34), 12, Accent);
        }
        private static StreamGeometry ContourGeometry(Point[] points, bool polygon)
        {
            var geometry = new StreamGeometry(); using (var context = geometry.Open())
            {
                if (polygon) { context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, false); }
                else
                {
                    static Point Mid(Point a, Point b) => new((a.X+b.X)/2, (a.Y+b.Y)/2);
                    context.BeginFigure(Mid(points[^1], points[0]), true, true);
                    for (var i=0;i<points.Length;i++) context.QuadraticBezierTo(points[i], Mid(points[i],points[(i+1)%points.Length]), true, false);
                }
            }
            geometry.Freeze(); return geometry;
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); Focus();
            if (owner.body is not { } body) return;
            var p = Unmap(e.GetPosition(this));
            if (Math.Abs(p.X - body.CoreX) > 1.1 || Math.Abs(p.Y - body.CoreY) > 1.1) return;
            owner.CancelInput(); owner.dragging = true; owner.dragX = p.X; owner.dragY = p.Y; CaptureMouse(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (!owner.dragging) return; var p = Unmap(e.GetPosition(this)); owner.dragX = p.X; owner.dragY = p.Y; }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        { base.OnMouseLeftButtonUp(e); if (!owner.dragging) return; owner.dragging = false; ReleaseMouseCapture(); owner.input.Cancel(); e.Handled = true; }
        protected override void OnLostMouseCapture(MouseEventArgs e)
        { base.OnLostMouseCapture(e); owner.dragging = false; owner.input.Cancel(); }
    }

    private static void Text(DrawingContext drawing, string text, Point point, double size, Brush brush) => drawing.DrawText(
        new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei"), size, brush, 1), point);

    private sealed class LabChart(LabWindow owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing); drawing.DrawRectangle(owner.Background, new Pen(LineBrush, 1), new Rect(RenderSize));
            Text(drawing, "物理高（棕）／显示高（绿）／物理质心Y（蓝）· 去均值 ±2 DIP", new Point(10, 8), 12, Ink);
            var samples = owner.metrics.Samples; if (samples.Count < 2) return;
            var heights = samples.Select(s => s.ExactCurveHeight).ToArray(); var centers = samples.Select(s => s.CenterOfMassY).ToArray();
            Plot(drawing, heights, Accent); Plot(drawing, centers, Brushes.SteelBlue);
            if (owner.displayTrace.Count == samples.Count) Plot(drawing, owner.displayTrace.Select(s=>s.Height).ToArray(), Brushes.SeaGreen);
        }
        private void Plot(DrawingContext drawing, double[] values, Brush brush)
        {
            var mean = values.Average(); const double span = 2d / 72;
            var geometry = new StreamGeometry(); using (var context = geometry.Open())
            {
                var points = values.Select((v, i) => new Point(10 + i * Math.Max(0, ActualWidth - 20) / (values.Length - 1), 80 - Math.Clamp((v - mean) / span, -1, 1) * 35)).ToArray();
                context.BeginFigure(points[0], false, false); context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            geometry.Freeze(); drawing.DrawGeometry(null, new Pen(brush, 1.2), geometry);
        }
    }
}
