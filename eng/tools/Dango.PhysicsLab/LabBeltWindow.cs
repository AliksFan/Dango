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
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.PhysicsLab;

/// <summary>Isolated single-body, flat-ground experiment. No display deformation or filtering.</summary>
internal sealed class LabBeltWindow : Window
{
    private readonly string halfModule, beltModule, session;
    private readonly LabInput input = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ComboBox model = new();
    private readonly CheckBox shortBones = new() { Content = "保留短骨架塑形（不碰撞）", IsChecked = true };
    private readonly CheckBox showSkeleton = new() { Content = "骨架、质心与合力位置", IsChecked = true };
    private readonly CheckBox showReference = new() { Content = "叠加原版静置轮廓", IsChecked = true };
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
    private DangoLabRegionStats? oldRegion;
    private DangoSelfPhysicsParameters config = DangoBodyMaterial.Configure(DangoBodyControls.Default);
    private bool fault, paused, initialized, dragging, replaying;
    private int steps, replayFrame, clicks;
    private double previousTime, accumulator, cameraX, dragX, dragY, discardedSeconds;
    private LabInputSnapshot lastInput;
    internal Button ApplyButton { get; }
    internal TextBox Notes { get; } = new() { MinHeight = 34, MaxLength = 500, ToolTip = "仅用于验证文字输入隔离；内容不保存" };
    private bool ActiveBelt => belt is not null;
    private float CoreX => beltFrame?.Stats[0] ?? oldFrame?.CoreX ?? 0;
    private float CoreY => beltFrame?.Stats[1] ?? oldFrame?.CoreY ?? 0;

    internal LabBeltWindow(string halfModule, string beltModule, string session)
    {
        this.halfModule = halfModule; this.beltModule = beltModule; this.session = session;
        Title = "团子 · 连续闭合带实验 · 步态关闭";
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
        heading.Children.Add(new TextBlock { Text = "A / D 左右半区   W / S 上半区向上／向下   Space 跳跃（当前偏弱）   鼠标拖动", Foreground = LabWindow.Accent });
        heading.Children.Add(new TextBlock { Text = "步态关闭 · 原始物理显示 · 单体平地 · 灰线是原版落稳轮廓", FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
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
        Label(tools, "外围模型"); model.Items.Add("连续闭合带（新实验）"); model.Items.Add("原外围（半侧驱动对照）"); model.SelectedIndex = 0;
        tools.Children.Add(model); tools.Children.Add(shortBones);
        tools.Children.Add(new TextBlock { Text = "新圈整体承重；16 个附着点只负责塑形。更改模型或材料后点击应用。", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        model.DropDownOpened += (_, _) => Cancel(); model.DropDownClosed += (_, _) => input.ReconcileHeld(HeldKeys());
        Label(tools, "材料"); AddSlider(tools, "大小", size); AddSlider(tools, "柔软度", softness); AddSlider(tools, "落地体态", build);
        Label(tools, "观察"); tools.Children.Add(showSkeleton); tools.Children.Add(showReference);
        showSkeleton.Click += (_, _) => surface.InvalidateVisual(); showReference.Click += (_, _) => surface.InvalidateVisual();
        tools.Children.Add(MakeButton("▷  同输入走停回放", () => { Reset(); replaying = !fault; replayFrame = 0; }));
        tools.Children.Add(MakeButton("Ⅱ  暂停／继续", () => { Cancel(); paused = !paused; }));
        tools.Children.Add(MakeButton("↓  导出物理记录", Export));
        Label(tools, "文字输入隔离"); tools.Children.Add(Notes);
        Label(tools, "实时诊断"); tools.Children.Add(details);
        Grid.SetColumn(surface, 2); grid.Children.Add(surface); Content = root;
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDownHandler), true);
        AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(KeyUpHandler), true);
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => { if (IsText(Keyboard.FocusedElement as DependencyObject)) Cancel(); }), true);
        Deactivated += (_, _) => Cancel(); Activated += (_, _) => input.ReconcileHeld(HeldKeys());
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Cancel(); };
        Closed += (_, _) => { timer.Stop(); belt?.Dispose(); old?.Dispose(); };
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
    { pid = Environment.ProcessId, visible = IsVisible, nativeLoaded = belt is not null || old is not null, model = ActiveBelt ? "continuous-belt" : "old-half", shortBones = beltFrame?.Stats[40] == 1, displaySmoothing = false, gait = false, error = fault }));

    private void Reset()
    {
        if (!initialized) return;
        Cancel(); belt?.Dispose(); old?.Dispose(); belt = null; old = null; beltFrame = null; oldFrame = null; reference = null;
        fault = paused = false; cameraX = accumulator = 0; steps = 0; trace.Clear();
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
            if (model.SelectedIndex == 0)
            {
                belt = new LabBeltWorld(beltModule, config, shortBones.IsChecked == true); belt.SetBounds(-256, 256); beltFrame = belt.Read();
            }
            else
            {
                old = DangoPhysicsWorld.Create(halfModule, 1, config); old.RequireLab();
                if (old.LabRegionDenominator != 2) throw new InvalidOperationException("原版模块不是半区驱动");
                oldId = old.Add(0, 0); old.SetHorizontalBounds(-256, 256);
            }
            status.Text = ActiveBelt ? "默认材料测试未通过：静置更扁、走速增加，Space未形成正常离地。无显示滤波。" : "已应用原外围半区对照。无显示滤波。";
            StepOnce(); WriteStarted();
        }
        catch (Exception e) { Fault(e); }
    }
    private void Tick(object? sender, EventArgs args)
    {
        var now = clock.Elapsed.TotalSeconds; var elapsed = now - previousTime; previousTime = now;
        if (paused || fault || !IsActive || !IsVisible || WindowState == WindowState.Minimized) { accumulator = 0; return; }
        accumulator += Math.Min(elapsed, .25); var count = 0;
        while (accumulator >= LabWindow.FixedDt && count++ < 4)
        { StepOnce(); accumulator -= LabWindow.FixedDt; if (fault) break; }
        if (accumulator > 4 * LabWindow.FixedDt) { discardedSeconds += accumulator; accumulator = 0; }
        surface.InvalidateVisual();
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
                belt.Step(LabWindow.FixedDt, new LabBeltInput(Horizontal: lastInput.Horizontal, Stretch: lastInput.Stretch, Downward: lastInput.Downward,
                    JumpPressed: lastInput.JumpPressed, InputActive: lastInput.InputActive, CancelForces: lastInput.CancelForces,
                    DragActive: dragging, DragTargetX: dragX, DragTargetY: dragY));
                beltFrame = belt.Read(); var s = beltFrame.Stats;
                if (s[36] != 0 || s[31] > .0001 || s[39] != 0) throw new InvalidOperationException("闭合带本帧安全诊断失败");
                trace.Enqueue(new { tick = steps, model = "belt", stats = s });
                if (steps % 6 == 0) details.Text = FormattableString.Invariant($"高 {(s[11] - s[9]) * 72:0.00} DIP / 宽 {(s[10] - s[8]) * 72:0.00} DIP\n真实质心Y {s[5]:0.0000} / 核心Y {s[1]:0.0000}\n接触 {s[25]:0} / 间隙 {s[12] * 72:0.00000} DIP\n合力 ({s[18]:0.000}, {s[19]:0.000})\nKKT残差 {s[30]:0.0e+0} / 36自由度\n独立外围碰撞体 {s[39]:0} / 帧 {steps}");
            }
            else if (old is not null)
            {
                var f = old.StepLab(LabWindow.FixedDt, [new(oldId, Horizontal: lastInput.Horizontal, Stretch: lastInput.Stretch, Downward: lastInput.Downward,
                    JumpPressed: lastInput.JumpPressed, InputActive: lastInput.InputActive, CancelForces: lastInput.CancelForces,
                    DragActive: dragging, DragTargetX: dragX, DragTargetY: dragY)], DangoLabDrive.DirectionRegion);
                if (!f.Succeeded) throw new InvalidOperationException("原版物理失败 " + f.ErrorCode);
                oldFrame = f.Bodies.Single(); oldNodes = old.ReadLabNodes(oldId); oldRegion = old.ReadLabRegionStats(oldId);
                var ex = LabGroundExperiments.CurveExtent(oldFrame.Contour, 1);
                var mass = oldNodes.Sum(n => n.Mass); var comY = oldNodes.Sum(n => n.Mass * n.Y) / mass;
                trace.Enqueue(new { tick = steps, model = "old-half", coreY = oldFrame.CoreY, comY, height = ex.Maximum - ex.Minimum });
                if (steps % 6 == 0) details.Text = FormattableString.Invariant($"高 {(ex.Maximum - ex.Minimum) * 72:0.00} DIP\n真实质心Y {comY:0.0000} / 核心Y {oldFrame.CoreY:0.0000}\n原外围独立碰撞体 16 / 帧 {steps}");
            }
            while (trace.Count > 1200) trace.Dequeue(); steps++;
            if (Math.Abs(CoreX - cameraX) > 1.5) cameraX = CoreX - Math.Sign(CoreX - cameraX) * 1.5;
            if (lastInput.InteractPressed) status.Text = "闭合带本轮只实验单体平地，平台交互未接入。";
        }
        catch (Exception e) { Fault(e); }
    }
    private void Fault(Exception e)
    {
        fault = paused = true; Cancel(); status.Text = "已暂停：" + e.Message;
        File.WriteAllText(Path.Combine(session, "physics-failure.json"), JsonSerializer.Serialize(new
        { steps, error = e.ToString(), config, lastInput, beltFrame, oldFrame, trace = trace.ToArray(), discardedSeconds }));
    }
    private void Cancel()
    {
        input.Cancel(); replaying = false;
        if (dragging) { dragging = false; surface.ReleaseMouseCapture(); }
    }
    private void Export()
    {
        Cancel(); File.WriteAllText(Path.Combine(session, $"belt-trace-{DateTime.UtcNow:HHmmss-fff}.json"), JsonSerializer.Serialize(new
        { model = ActiveBelt ? "continuous-belt" : "old-half", config, fixedDt = LabWindow.FixedDt, displaySmoothing = false, gait = false, discardedSeconds, trace = trace.ToArray() }));
        status.Text = $"已导出 {trace.Count} 帧物理诊断。";
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
        try
        {
            timer.Stop(); Cancel();
            void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
            Check(IsLoaded && IsVisible && !fault && ActiveBelt, "真实WPF已显示且连接闭合带DLL");
            for (var i = 0; i < 300; i++) StepOnce(); Check(!fault && beltFrame!.Stats[14] == 1, "闭合带落稳接地");
            Inject(Key.D, true, ApplyButton); StepOnce(); Check(lastInput.Horizontal == 1 && beltFrame!.Stats[18] > 0, "按钮焦点D驱动真实半区力");
            Inject(Key.D, false, ApplyButton); StepOnce();
            Inject(Key.W, true, size); StepOnce(); Check(lastInput.Stretch == 1 && beltFrame!.Stats[19] > 0, "滑条焦点W仍向上施力");
            Inject(Key.S, true, size); StepOnce(); Check(lastInput.CancelForces && beltFrame!.Stats[19] == 0, "W加S撤销垂直力");
            Inject(Key.W, false, size); Inject(Key.S, false, size); StepOnce();
            Inject(Key.A, true, Notes); StepOnce(); Check(!lastInput.InputActive && beltFrame!.Stats[18] == 0, "文字输入不驱动"); Inject(Key.A, false, Notes);
            Reset(); for (var i = 0; i < 300; i++) StepOnce();
            ApplyButton.Focus(); var c = clicks; Inject(Key.Space, true, ApplyButton); StepOnce();
            Check(lastInput.JumpPressed && beltFrame!.Stats[15] == 1 && beltFrame.Stats[47] > 0, "按钮焦点Space接受原核心跳跃冲量");
            StepOnce(); Check(!lastInput.JumpPressed, "Space不自动重复"); Inject(Key.Space, false, ApplyButton); Check(clicks == c, "Space不误点应用");
            Check(ApplyButton.FocusVisualStyle is null && size.FocusVisualStyle is null, "按钮滑条无虚线焦点");
            Inject(Key.D, true, ApplyButton); StepOnce(); Cancel(); StepOnce(); Check(lastInput.CancelForces && lastInput.Horizontal == 0, "取消清除玩法状态");
            Inject(Key.D, true, ApplyButton); StepOnce(); Check(lastInput.Horizontal == 0, "取消后按住的键不补发"); Inject(Key.D, false, ApplyButton);
            model.SelectedIndex = 1; Reset(); for (var i = 0; i < 300; i++) StepOnce(); Check(!fault && !ActiveBelt && oldFrame!.IsGrounded, "原外围半区对照真实落地");
            model.SelectedIndex = 0;
            Reset(); for (var i = 0; i < 300; i++) StepOnce(); Check(!fault, "最终落稳无故障"); sidebarScroll.ScrollToTop(); surface.InvalidateVisual();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(session, "window.png"))) encoder.Save(file);
            File.WriteAllText(Path.Combine(session, "wpf-smoke.json"), JsonSerializer.Serialize(new { passed = true, checks, method = "shown WPF routed events; not OS keyboard automation", manualAcceptance = false }, new JsonSerializerOptions { WriteIndented = true })); Close();
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(session, "wpf-smoke.json"), JsonSerializer.Serialize(new { passed = false, checks, error = e.ToString() }, new JsonSerializerOptions { WriteIndented = true })); Application.Current.Shutdown(11);
        }
    }

    private sealed class Surface(LabBeltWindow owner) : FrameworkElement
    {
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
            d.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 226, 190)), new Pen(LabWindow.Accent, 1.2), geometry);
            if (owner.showReference.IsChecked == true && owner.reference is { } reference)
                d.DrawGeometry(null, new Pen(Brushes.SlateGray, 1), Shape(reference.Contour, owner.CoreX, reference.CoreY, true));
            var core = Map(owner.CoreX, owner.CoreY);
            foreach (var side in new[] { -1, 1 })
            { var eye = core + new Vector(side * 20, -4); if (geometry.FillContains(eye)) d.DrawEllipse(LabWindow.Ink, null, eye, 3.4, 5); }
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
                    Arrow(d, Map(belt.Stats[20], belt.Stats[21]), belt.Stats[18], 0);
                    Arrow(d, Map(belt.Stats[22], belt.Stats[23]), 0, belt.Stats[19]);
                }
                else if (owner.oldNodes.Count > 0)
                {
                    foreach (var n in owner.oldNodes.Skip(1)) { var p = Map(n.X, n.Y); d.DrawLine(new Pen(LabWindow.LineBrush, .8), core, p); d.DrawEllipse(LabWindow.Accent, null, p, 2, 2); }
                    var mass = owner.oldNodes.Sum(n => n.Mass); Cross(d, Map(owner.oldNodes.Sum(n => n.Mass * n.X) / mass, owner.oldNodes.Sum(n => n.Mass * n.Y) / mass));
                    if (owner.oldRegion is { } r) { Arrow(d, Map(r.HorizontalPointX, r.HorizontalPointY), r.ActualForceX, 0); Arrow(d, Map(r.VerticalPointX, r.VerticalPointY), 0, r.ActualForceY); }
                }
                d.DrawEllipse(Brushes.DarkOrange, null, core, 4, 4);
            }
            Text(d, "橙：核心／塑形附着点   蓝：真实质量质心   红：区域合力", 12);
            Text(d, owner.fault ? "物理失败，已暂停" : owner.replaying ? "同输入回放中" : owner.paused ? "已暂停" : "原始显示 · 步态关闭", 34);
        }
        private void Text(DrawingContext d, string text, double y) => d.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(owner.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, LabWindow.Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(12, y));
        private static void Cross(DrawingContext d, Point p) { var pen = new Pen(Brushes.SteelBlue, 1.5); d.DrawLine(pen, p + new Vector(-5, 0), p + new Vector(5, 0)); d.DrawLine(pen, p + new Vector(0, -5), p + new Vector(0, 5)); }
        private static void Arrow(DrawingContext d, Point p, double fx, double fy)
        {
            if (Math.Abs(fx) + Math.Abs(fy) < 1e-7) return;
            var v = new Vector(Math.Sign(fx) * 38, -Math.Sign(fy) * 38); var end = p + v; var unit = v; unit.Normalize(); var side = new Vector(-unit.Y, unit.X);
            var pen = new Pen(Brushes.IndianRed, 1.5); d.DrawLine(pen, p, end); d.DrawLine(pen, end, end - unit * 7 + side * 4); d.DrawLine(pen, end, end - unit * 7 - side * 4);
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
