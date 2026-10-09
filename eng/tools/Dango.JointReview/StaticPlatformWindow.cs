using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;
using Path = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.JointReview;

internal sealed class StaticPlatformWindow : Window
{
    private const double MinimumObservationSeconds = 600;
    private const double MinimumActiveSeconds = 560;
    private readonly string libraryPath;
    private readonly Canvas scene = new() { Background = new SolidColorBrush(Color.FromRgb(250, 246, 239)), ClipToBounds = true, Focusable = true };
    private readonly TextBlock status = new() { Margin = new Thickness(14), TextWrapping = TextWrapping.Wrap, MinHeight = 62 };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, Cursor = Cursors.Hand };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false };
    private readonly Image platformImage = new() { IsHitTestVisible = false, Stretch = Stretch.Fill, Source = CreateOfficialPlatformImage() };
    private readonly Rectangle platformOutline = new() { IsHitTestVisible = false, Stroke = Brushes.DarkSlateGray, StrokeThickness = 1, StrokeDashArray = [4, 3] };
    private readonly Line floor = new() { IsHitTestVisible = false, Stroke = Brushes.SandyBrown, StrokeThickness = 2 };
    private readonly ShapePath bodyOutline = new() { IsHitTestVisible = false, Stroke = Brushes.Teal, StrokeThickness = .8, Fill = null };
    private readonly Button pauseButton = new() { Content = "暂停", Margin = new Thickness(5), Padding = new Thickness(10, 5, 10, 5) };
    private readonly CheckBox outlineToggle = new() { Content = "显示碰撞轮廓", IsChecked = true, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = StaticPlatformSession.FixedStep };
    private readonly DispatcherTimer restoreTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly HashSet<Key> keys = [];
    private readonly DangoPhysicsPresentationAdapter presenter = new(DangoPhysicsDefaults.Current);
    private readonly List<double> stepMilliseconds = [];
    private readonly StreamWriter? observations;
    private readonly string? evidenceDirectory;
    private readonly Stopwatch observationClock = new();
    private readonly Process ownProcess = Process.GetCurrentProcess();
    private readonly string moduleHash;
    private StaticPlatformSession? session;
    private StaticPlatformScenario scenario = StaticPlatformScenario.Top;
    private PhysicsPresentationFrame? presentation;
    private DangoBodyState? nativeBody;
    private Geometry? displayedGeometry;
    private DangoFacingDirection facing;
    private DangoStaticPlatformContact? contact;
    private Point dragTarget;
    private bool dragging, paused, jumpHeld, closing, faulted, restoringPaused;
    private double lastTickSeconds, accumulator, nextStatusSeconds, activeSeconds, droppedSeconds, peakContactError;
    private int schedulerCallbacks, renderedFrames, lifetimeSteps, pauseCycles, hideCycles, observationCycle, observationStage, suspendedStepCount;
    private double nextObservationSample = 1, lastSampleSeconds;
    private TimeSpan lastCpu;
    private long peakWorkingSet;
    private string? currentFailure;
    private string? failure;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private double Scale => Math.Clamp(Math.Min(60, scene.ActualHeight / 5.4), 30, 60);
    private double OriginX => scene.ActualWidth / 2;
    private double FloorY => Math.Max(100, scene.ActualHeight - 40);
    private double OriginY => FloorY + DangoPhysicsDefaults.Current.GroundHeight * Scale;

    internal int ExitCode { get; private set; }

    internal StaticPlatformWindow(string libraryPath, string? evidenceDirectory = null)
    {
        this.libraryPath = Path.GetFullPath(libraryPath);
        using (var input = File.OpenRead(this.libraryPath)) moduleHash = Convert.ToHexString(SHA256.HashData(input));
        if (evidenceDirectory is not null)
        {
            this.evidenceDirectory = Path.GetFullPath(evidenceDirectory);
            if (Directory.Exists(this.evidenceDirectory) || File.Exists(this.evidenceDirectory))
                throw new IOException("观察记录必须使用不存在的新目录。");
            Directory.CreateDirectory(this.evidenceDirectory);
            observations = new(new FileStream(Path.Combine(this.evidenceDirectory, "observation.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
        Title = "Dango 官方静态平台｜独立单人验收";
        Width = 1060; Height = 800; MinWidth = 760; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var surface = new DockPanel { Background = Brushes.White };
        Content = surface;
        var heading = new TextBlock
        {
            Text = "官方矮台 · 单人验收\nA / D 移动，S 下压，Space 跳跃；鼠标拖动团子。平台外观与碰撞矩形独立，构建不代表人工验收通过。",
            FontSize = 17,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(heading, Dock.Top); surface.Children.Add(heading);
        var controls = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
        foreach (var item in new[]
        {
            ("无平台对照", StaticPlatformScenario.WithoutPlatform), ("顶部落地", StaticPlatformScenario.Top),
            ("左侧接近", StaticPlatformScenario.Left), ("右侧接近", StaticPlatformScenario.Right), ("底面顶撞", StaticPlatformScenario.Underside),
        })
        {
            var choice = item.Item2;
            AddButton(controls, item.Item1, () => Reset(choice));
        }
        AddButton(controls, "按当前场景重置", () => Reset(scenario));
        pauseButton.Click += (_, _) => { SetPaused(!paused); FocusScene(); }; controls.Children.Add(pauseButton);
        AddButton(controls, "移除平台", () => { if (session?.Platform is not null && !faulted) session.RemovePlatform(); Draw(); FocusScene(); });
        AddButton(controls, "隐藏两秒后恢复", HideTemporarily);
        outlineToggle.Checked += (_, _) => SafeDraw(); outlineToggle.Unchecked += (_, _) => SafeDraw(); controls.Children.Add(outlineToggle);
        DockPanel.SetDock(controls, Dock.Top); surface.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom); surface.Children.Add(status); surface.Children.Add(scene);
        AutomationProperties.SetName(scene, "平台验收场景");
        AutomationProperties.SetAutomationId(scene, "StaticPlatformScene");
        // This focused surface handles game keys; Chinese IME must not consume A/D/S.
        InputMethod.SetIsInputMethodEnabled(scene, false);
        AutomationProperties.SetName(body, "可拖动团子");
        foreach (var item in new UIElement[] { floor, platformImage, platformOutline, body, hat, bodyOutline }) scene.Children.Add(item);
        Panel.SetZIndex(body, 10); Panel.SetZIndex(hat, 20); Panel.SetZIndex(bodyOutline, 30);
        body.MouseLeftButtonDown += BeginDrag;
        body.MouseMove += MoveDrag;
        body.MouseLeftButtonUp += (_, e) => { ClearInput(); e.Handled = true; };
        body.LostMouseCapture += (_, _) => ClearInput();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (!IsActionKey(e.Key)) return;
            keys.Remove(e.Key);
            if (Keyboard.FocusedElement == scene) e.Handled = true;
        };
        scene.LostKeyboardFocus += (_, _) => ClearInput();
        scene.MouseLeftButtonDown += (_, _) => FocusScene();
        Deactivated += (_, _) => ClearInput();
        IsVisibleChanged += (_, _) => { ClearInput(); ResetClock(); };
        StateChanged += (_, _) => { ClearInput(); ResetClock(); };
        scene.SizeChanged += (_, _) => { ClearInput(); SafeDraw(); };
        Loaded += (_, _) =>
        {
            Reset(scenario);
            observationClock.Start(); lastCpu = ownProcess.TotalProcessorTime;
            timer.Start(); FocusScene();
        };
        timer.Tick += OnTick;
        restoreTimer.Tick += OnRestoreTick;
        Closed += (_, _) => Cleanup();
    }

    private static bool IsActionKey(Key key) => key is Key.A or Key.D or Key.S or Key.Space;

    private void AddButton(Panel controls, string name, Action action)
    {
        var button = new Button { Content = name, Margin = new Thickness(5), Padding = new Thickness(10, 5, 10, 5) };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            try { ClearInput(); action(); }
            catch (Exception error) { StopWithFailure(error); }
        };
        controls.Children.Add(button);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsActionKey(e.Key) || Keyboard.FocusedElement != scene) return;
        e.Handled = true;
        if (paused || faulted || closing || (e.IsRepeat && !keys.Contains(e.Key))) return;
        keys.Add(e.Key);
    }

    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (paused || faulted || displayedGeometry is null || !displayedGeometry.FillContains(e.GetPosition(body))) return;
        FocusScene(); dragTarget = ToWorld(e.GetPosition(scene));
        dragging = body.CaptureMouse(); e.Handled = true;
    }

    private void MoveDrag(object sender, MouseEventArgs e)
    {
        if (!dragging || !body.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        dragTarget = ToWorld(e.GetPosition(scene)); e.Handled = true;
    }

    private void ClearInput()
    {
        keys.Clear(); jumpHeld = false; dragging = false; dragTarget = default;
        if (body.IsMouseCaptured) body.ReleaseMouseCapture();
    }

    private void FocusScene() => Keyboard.Focus(scene);
    private void ResetClock() { accumulator = 0; lastTickSeconds = clock.Elapsed.TotalSeconds; }
    private void SetPaused(bool value) { paused = value; pauseButton.Content = paused ? "继续" : "暂停"; ClearInput(); ResetClock(); UpdateStatus(); }

    private void HideTemporarily()
    {
        if (closing) return;
        restoringPaused = paused; SetPaused(true); Hide(); restoreTimer.Start();
    }

    private void OnRestoreTick(object? sender, EventArgs e)
    {
        restoreTimer.Stop(); Show(); Activate(); SetPaused(restoringPaused); FocusScene();
    }

    private void Reset(StaticPlatformScenario value)
    {
        try
        {
            ClearInput(); restoreTimer.Stop(); session?.Dispose(); session = null;
            presenter.Reset(); presentation = null; nativeBody = null; displayedGeometry = null; contact = null;
            body.Visibility = hat.Visibility = bodyOutline.Visibility = Visibility.Hidden;
            scenario = value; session = new(libraryPath, scenario); facing = DangoFacingDirection.Center;
            currentFailure = null; faulted = false; paused = false; pauseButton.Content = "暂停";
            if (evidenceDirectory is null) { failure = null; ExitCode = 0; }
            ResetClock(); Draw(); FocusScene();
        }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (closing) return;
        schedulerCallbacks++;
        try { Observe(); }
        catch (Exception error) { StopWithFailure(error); }
        if (closing || paused || faulted || !IsVisible || WindowState == WindowState.Minimized || session is null)
        { ResetClock(); return; }
        var now = clock.Elapsed.TotalSeconds;
        var delta = Math.Max(0, now - lastTickSeconds); lastTickSeconds = now;
        activeSeconds += delta;
        if (delta > .25) { droppedSeconds += delta - .25; delta = .25; }
        accumulator += delta;
        var count = 0;
        try
        {
            while (accumulator + 1e-9 >= StaticPlatformSession.FixedStep.TotalSeconds && count < 4)
            {
                var horizontal = (keys.Contains(Key.D) ? 1 : 0) - (keys.Contains(Key.A) ? 1 : 0);
                var jump = keys.Contains(Key.Space) && !jumpHeld; jumpHeld = keys.Contains(Key.Space);
                if (horizontal != 0) facing = horizontal < 0 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
                var started = Stopwatch.GetTimestamp();
                var nextBody = session.Step(horizontal, keys.Contains(Key.S), jump, dragging, dragTarget.X, dragTarget.Y);
                var cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (stepMilliseconds.Count < 60_000) stepMilliseconds.Add(cost);
                if (session.Error != 0) throw new InvalidOperationException($"native 锁存故障 {session.Error}；保留最后安全帧。");
                if (nextBody.SignedArea < .42 || nextBody.MaximumSpeed > 12 + 1e-5)
                    throw new InvalidOperationException("真实物理帧超过面积／速度门槛。");
                var nextPresentation = presenter.Present(nextBody, StaticPlatformSession.FixedStep, facing);
                if (nextPresentation.FallbackReason != PhysicsPresentationFallbackReason.None ||
                    !DangoStreamGeometryFactory.TryCreate(nextPresentation.Contour, 160, 120).Succeeded)
                    throw new InvalidDataException("真实团子绘制轮廓校验失败；保留最后安全画面。");
                var nextContact = session.ReadContact();
                if (nextContact is { HasPlatform: true } item && (item.IsTouching || item.IsSupported))
                {
                    if (!double.IsFinite(item.Gap)) throw new InvalidDataException("平台诊断间隙无效。");
                    var error = item.IsSupported ? Math.Abs(item.Gap) : Math.Max(0, -item.Gap);
                    peakContactError = Math.Max(peakContactError, error);
                }
                nativeBody = nextBody; presentation = nextPresentation; contact = nextContact;
                lifetimeSteps++; count++; accumulator -= StaticPlatformSession.FixedStep.TotalSeconds;
            }
            if (count == 4 && accumulator >= StaticPlatformSession.FixedStep.TotalSeconds)
            { droppedSeconds += accumulator; accumulator = 0; }
            if (count > 0) { Draw(); renderedFrames++; }
            if (now >= nextStatusSeconds) { UpdateStatus(); nextStatusSeconds = now + .15; }
        }
        catch (Exception error) { StopWithFailure(error); }
    }

    private Point ToWorld(Point point) => new((point.X - OriginX) / Scale, (OriginY - point.Y) / Scale);
    private Point ToScreen(double x, double y) => new(OriginX + x * Scale, OriginY - y * Scale);

    private void SafeDraw()
    {
        try { Draw(); }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void Draw()
    {
        floor.X1 = 0; floor.X2 = scene.ActualWidth; floor.Y1 = floor.Y2 = FloorY;
        if (session?.Platform is { } platform)
        {
            var corner = ToScreen(platform.Left, platform.Top);
            platformImage.Width = platform.Width * Scale; platformImage.Height = platform.Height * Scale;
            Canvas.SetLeft(platformImage, corner.X); Canvas.SetTop(platformImage, corner.Y);
            platformOutline.Width = platform.Width * Scale; platformOutline.Height = platform.Height * Scale;
            Canvas.SetLeft(platformOutline, corner.X); Canvas.SetTop(platformOutline, corner.Y);
            platformImage.Visibility = Visibility.Visible;
            platformOutline.Visibility = outlineToggle.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        }
        else platformImage.Visibility = platformOutline.Visibility = Visibility.Hidden;
        if (presentation is null || nativeBody is null) return;
        var result = DangoStreamGeometryFactory.TryCreate(presentation.Contour, 160, 120);
        if (!result.Succeeded || presentation.FallbackReason != PhysicsPresentationFallbackReason.None)
            throw new InvalidDataException("真实团子绘制轮廓校验失败。");
        var core = ToScreen(nativeBody.CoreX, nativeBody.CoreY);
        var transform = new ScaleTransform(Scale / 80, Scale / 60);
        foreach (var visual in new FrameworkElement[] { body, hat })
        {
            visual.RenderTransform = transform;
            Canvas.SetLeft(visual, core.X - Scale); Canvas.SetTop(visual, core.Y - Scale * 1.1);
            visual.Visibility = Visibility.Visible;
        }
        body.ApplyFrame(presentation); hat.ApplyFrame(presentation);
        if (body.LastContourFailure != DangoContourFailure.None)
            throw new InvalidDataException("真实团子绘制轮廓校验失败。");
        displayedGeometry = result.Geometry;
        bodyOutline.Data = displayedGeometry; bodyOutline.RenderTransform = transform;
        Canvas.SetLeft(bodyOutline, core.X - Scale); Canvas.SetTop(bodyOutline, core.Y - Scale * 1.1);
        bodyOutline.Visibility = outlineToggle.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
    }

    private void UpdateStatus()
    {
        var support = contact is { IsSupported: true } ? "平台支撑" : nativeBody?.IsGrounded == true ? "地面支撑" : "空中／未支撑";
        status.Text = currentFailure is not null ? $"已停止：{currentFailure}\n可按场景重置；窗口保留最后安全状态。" :
            $"{(paused ? "已暂停" : "运行中")} · {support} · 真实物理步 {session?.Steps ?? 0}\n" +
            $"最大速度 {nativeBody?.MaximumSpeed ?? 0:F4} · 平台间隙 {contact?.Gap ?? 0:F5} · 原地面接触 {nativeBody?.GroundContactCount ?? 0} · 唯一世界／单团子";
        status.Foreground = faulted ? Brushes.Firebrick : Brushes.DarkSlateGray;
    }

    private void StopWithFailure(Exception error)
    {
        currentFailure = error.Message; failure ??= error.Message; faulted = true; ExitCode = 1;
        ClearInput(); ResetClock(); UpdateStatus();
        if (evidenceDirectory is not null) Dispatcher.BeginInvoke(new Action(Close));
    }

    private void Observe()
    {
        if (evidenceDirectory is null || !observationClock.IsRunning) return;
        var seconds = observationClock.Elapsed.TotalSeconds;
        if (observationCycle < 20)
        {
            var start = (observationCycle + 1) * 15d;
            if (observationStage == 0 && seconds >= start)
            { suspendedStepCount = lifetimeSteps; SetPaused(true); observationStage = 1; pauseCycles++; }
            if (observationStage == 1 && seconds >= start + 1)
            { Hide(); observationStage = 2; hideCycles++; }
            if (observationStage == 2 && seconds >= start + 2)
            {
                if (lifetimeSteps != suspendedStepCount) { StopWithFailure(new InvalidOperationException("暂停／隐藏期间物理仍在推进。")); return; }
                Show(); Activate(); SetPaused(false); FocusScene(); observationCycle++; observationStage = 0;
            }
        }
        if (seconds >= nextObservationSample)
        {
            ownProcess.Refresh(); var cpu = ownProcess.TotalProcessorTime;
            var elapsed = seconds - lastSampleSeconds;
            var cpuPercent = elapsed > 0 ? (cpu - lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100 : 0;
            peakWorkingSet = Math.Max(peakWorkingSet, ownProcess.WorkingSet64);
            observations!.WriteLine(JsonSerializer.Serialize(new
            {
                ElapsedSeconds = seconds,
                ActiveSeconds = activeSeconds,
                PhysicsSteps = lifetimeSteps,
                RenderedFrames = renderedFrames,
                Paused = paused,
                Visible = IsVisible,
                WindowState = WindowState.ToString(),
                Minimized = WindowState == WindowState.Minimized,
                NativeError = session?.Error ?? 0,
                CpuPercent = cpuPercent,
                WorkingSetBytes = ownProcess.WorkingSet64,
                PeakNativeDiagnosticContactErrorPhysical = peakContactError,
                PauseCycles = pauseCycles,
                HideCycles = hideCycles,
                OwnedWorldCount = StaticPlatformSession.OwnedWorldCount,
            }));
            lastCpu = cpu; lastSampleSeconds = seconds; nextObservationSample = seconds + 1;
        }
        if (seconds >= MinimumObservationSeconds && activeSeconds >= MinimumActiveSeconds)
        {
            if (pauseCycles != 20 || hideCycles != 20 || observationCycle != 20 || peakContactError > 1d / 30)
                failure ??= "十分钟观察的循环数或接触预算未通过。";
            if (failure is not null) ExitCode = 1;
            Close();
        }
    }

    private void Cleanup()
    {
        if (closing) return;
        closing = true; timer.Stop(); restoreTimer.Stop(); ClearInput();
        timer.Tick -= OnTick; restoreTimer.Tick -= OnRestoreTick;
        try { session?.Dispose(); session = null; }
        catch (Exception error) { failure ??= error.Message; ExitCode = 1; }
        try
        {
            if (evidenceDirectory is not null)
            {
                if ((observationClock.Elapsed.TotalSeconds < MinimumObservationSeconds ||
                    activeSeconds < MinimumActiveSeconds) && failure is null)
                { failure = "观察不足600秒墙钟或560秒活动时间；不能计为完整观察通过。"; ExitCode = 1; }
                var ordered = stepMilliseconds.Order().ToArray();
                var summary = new
                {
                    NativeModuleSha256 = moduleHash,
                    ElapsedSeconds = observationClock.Elapsed.TotalSeconds,
                    ActiveSeconds = activeSeconds,
                    MinimumObservationSeconds,
                    MinimumActiveSeconds,
                    WindowState = WindowState.ToString(),
                    Minimized = WindowState == WindowState.Minimized,
                    TimeMeasurement = "Elapsed is wall time; active excludes paused, hidden and minimized intervals; scheduler delay while runnable is included, and dropped simulation time is reported separately",
                    SchedulerCallbacks = schedulerCallbacks,
                    PhysicsSteps = lifetimeSteps,
                    NewFrameRenders = renderedFrames,
                    NewFrameDrawsPerSecond = activeSeconds > 0 ? renderedFrames / activeSeconds : 0,
                    FrameMeasurement = "New native-state Draw updates per active second; not display composition or presentation FPS",
                    PhysicsStepsPerSecond = activeSeconds > 0 ? lifetimeSteps / activeSeconds : 0,
                    StepP95Milliseconds = ordered.Length == 0 ? 0 : ordered[(int)Math.Floor((ordered.Length - 1) * .95)],
                    StepMaximumMilliseconds = ordered.LastOrDefault(),
                    PeakWorkingSetBytes = peakWorkingSet,
                    PeakNativeDiagnosticContactErrorPhysical = peakContactError,
                    ContactBudgetPhysical = 1d / 30,
                    ContactMeasurement = "Native contact diagnostic; independent painted-contour oracle is reported by integration gates",
                    DroppedSeconds = droppedSeconds,
                    PauseCycles = pauseCycles,
                    HideCycles = hideCycles,
                    WorldDisposed = session is null,
                    TimerStopped = !timer.IsEnabled && !restoreTimer.IsEnabled,
                    OwnedWorldCount = StaticPlatformSession.OwnedWorldCount,
                    ActiveTimerOwners = (timer.IsEnabled ? 1 : 0) + (restoreTimer.IsEnabled ? 1 : 0),
                    MouseCaptureReleased = !body.IsMouseCaptured,
                    ExitCode,
                    Failure = failure,
                    HardwareInputAndHumanAcceptance = "Not verified by this observation",
                };
                using var output = new FileStream(Path.Combine(evidenceDirectory, "observation-summary.json"), FileMode.CreateNew);
                JsonSerializer.Serialize(output, summary, JsonOptions);
            }
        }
        catch (Exception error) { failure ??= error.Message; ExitCode = 1; Console.Error.WriteLine(error); }
        finally { observations?.Dispose(); ownProcess.Dispose(); }
    }

    private static DrawingImage CreateOfficialPlatformImage()
    {
        var drawing = new DrawingGroup();
        // Original official artwork: a warm wooden slab. Its alpha corner cutouts do not define collision.
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(177, 122, 76)), null, new RectangleGeometry(new Rect(0, 0, 340, 35), 5, 5)));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(227, 180, 123)), null, new RectangleGeometry(new Rect(0, 0, 340, 8), 4, 4)));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(145, 93, 56)), 1.5), Geometry.Parse("M 12,20 L 110,20 M 125,17 L 220,17 M 235,24 L 327,24")));
        drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
