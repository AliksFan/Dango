using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

internal sealed class RoundBallWindow : Window
{
    private const double OriginalHorizontalScale = 80;
    private const double OriginalVerticalScale = 60;
    private const double VisualCenterX = 80;
    private const double VisualCenterY = 66;
    private readonly string libraryPath;
    private readonly string moduleHash;
    private double ballGravityScale;
    private readonly Canvas scene = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(246, 249, 250)),
        ClipToBounds = true, Focusable = true, MinHeight = 200,
    };
    private readonly TextBlock status = new() { Margin = new Thickness(14), TextWrapping = TextWrapping.Wrap, MinHeight = 72 };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, Cursor = Cursors.Hand };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false };
    private readonly Canvas ballVisual = new() { IsHitTestVisible = false, Width = 0, Height = 0 };
    private readonly Ellipse ballCircle = new() { IsHitTestVisible = false, Fill = new SolidColorBrush(Color.FromRgb(167, 218, 239)) };
    private readonly Ellipse ballInnerBorder = new() { IsHitTestVisible = false, Stroke = new SolidColorBrush(Color.FromRgb(60, 133, 162)), StrokeThickness = 1.2 };
    private readonly ShapePath ballBand = new()
    {
        IsHitTestVisible = false, Stroke = new SolidColorBrush(Color.FromRgb(242, 253, 255)), StrokeThickness = 5,
        Data = Geometry.Parse("M 12,3 C 34,10 34,38 12,45"),
    };
    private readonly Ellipse ballMarker = new() { IsHitTestVisible = false, Fill = Brushes.White, Width = 4, Height = 4 };
    private readonly Ellipse ballOutline = new() { IsHitTestVisible = false, Stroke = Brushes.Teal, StrokeThickness = .8, StrokeDashArray = [4, 3] };
    private readonly Line floor = new() { IsHitTestVisible = false, Stroke = Brushes.SandyBrown, StrokeThickness = 2 };
    private readonly Line leftWall = new() { IsHitTestVisible = false, Stroke = Brushes.LightSlateGray, StrokeThickness = 2 };
    private readonly Line rightWall = new() { IsHitTestVisible = false, Stroke = Brushes.LightSlateGray, StrokeThickness = 2 };
    private readonly ShapePath bodyOutline = new() { IsHitTestVisible = false, Stroke = Brushes.Teal, StrokeThickness = .8, Fill = null };
    private readonly Button pauseButton = new() { Content = "暂停", Margin = new Thickness(5), Padding = new Thickness(10, 5, 10, 5) };
    private readonly CheckBox outlineToggle = new() { Content = "显示碰撞轮廓", IsChecked = false, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox diagnosticToggle = new() { Content = "显示诊断", IsChecked = false, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = RoundBallSession.FixedStep };
    private readonly DispatcherTimer restoreTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly RoundBallInputState input = new();
    private readonly DangoPhysicsPresentationAdapter presenter = new(DangoPhysicsDefaults.Current);
    private readonly List<double> stepMilliseconds = [];
    private readonly RoundBallWindowDiagnostics? diagnostics;
    private RoundBallSession? session;
    private RoundBallScenario scenario = RoundBallScenario.PushFromLeft;
    private DangoRoundBallFrame? currentFrame;
    private PhysicsPresentationFrame? presentation;
    private Geometry? displayedGeometry;
    private DangoFacingDirection facing;
    private Point dragTarget;
    private bool dragging, paused, closing, faulted, restoringPaused;
    private double lastTickSeconds, accumulator, nextStatusSeconds, activeSeconds, droppedSeconds;
    private double peakNativeContactErrorQ, peakPaintedContactErrorQ, latestPaintedGapQ;
    private int schedulerCallbacks, lifetimeStepCalls, successfulSteps, drawCalls, newFrameDraws, presenterAdvances;
    private int jumpRequestsConsumed;
    private bool lastStepJumpRequested;
    private int pauseCycles, hideCycles, observationCycle, observationStage, suspendedStepCalls, inputClearCount;
    private string? currentFailure, failure;

    private double VerticalScale => Math.Clamp(Math.Min(OriginalVerticalScale, scene.ActualHeight / 5.4), OriginalVerticalScale / 2, OriginalVerticalScale);
    private double VisualScale => VerticalScale / OriginalVerticalScale;
    private double HorizontalScale => OriginalHorizontalScale * VisualScale;
    private double OriginX => scene.ActualWidth / 2;
    private double FloorY => Math.Max(100, scene.ActualHeight - 40);
    private double OriginY => FloorY + DangoPhysicsDefaults.Current.GroundHeight * VerticalScale;
    internal int ExitCode { get; private set; }

    internal RoundBallWindow(string libraryPath, string? evidenceDirectory = null, bool automaticObservation = true)
    {
        this.libraryPath = Path.GetFullPath(libraryPath);
        using (var input = File.OpenRead(this.libraryPath)) moduleHash = Convert.ToHexString(SHA256.HashData(input));
        if (evidenceDirectory is not null) diagnostics = new(evidenceDirectory, moduleHash, automaticObservation);
        Title = "Dango 官方圆球｜独立单人验收";
        Width = 1060; Height = 800; MinWidth = 760; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var surface = new DockPanel { Background = Brushes.White };
        Content = surface;
        var heading = new TextBlock
        {
            Text = "团子和小圆球\nA / D 移动，S 下压，Space 从地面起跳；拖动团子也能推动球。点击场景按钮重新开始。",
            FontSize = 17, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(heading, Dock.Top); surface.Children.Add(heading);
        var controls = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
        foreach (var item in new[]
        {
            ("无球对照", RoundBallScenario.WithoutBall), ("从左推球", RoundBallScenario.PushFromLeft),
            ("从右推球", RoundBallScenario.PushFromRight), ("球落向团子", RoundBallScenario.BallHitsDango),
            ("球落地", RoundBallScenario.BallFalls),
        })
        {
            var choice = item.Item2;
            AddButton(controls, item.Item1, () => ResetScenario(choice));
        }
        AddButton(controls, "重置当前状态", ResetCurrent);
        pauseButton.Click += (_, _) => { SetPaused(!paused); FocusScene(); }; controls.Children.Add(pauseButton);
        AddButton(controls, "移除球", () =>
        {
            if (session is not null && !faulted) AcceptFrame(session.RemoveBall());
            Draw(); UpdateStatus(); FocusScene();
        });
        AddButton(controls, "隐藏两秒后恢复", HideTemporarily);
        outlineToggle.Checked += (_, _) => { ClearInput(); SafeDraw(); };
        outlineToggle.Unchecked += (_, _) => { ClearInput(); SafeDraw(); };
        diagnosticToggle.Checked += (_, _) => { ClearInput(); UpdateStatus(); };
        diagnosticToggle.Unchecked += (_, _) => { ClearInput(); UpdateStatus(); };
        controls.Children.Add(outlineToggle); controls.Children.Add(diagnosticToggle);
        DockPanel.SetDock(controls, Dock.Top); surface.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom); surface.Children.Add(status); surface.Children.Add(scene);
        AutomationProperties.SetName(scene, "圆球验收场景");
        AutomationProperties.SetAutomationId(scene, "RoundBallScene");
        InputMethod.SetIsInputMethodEnabled(scene, false);
        AutomationProperties.SetName(body, "可拖动团子");
        AutomationProperties.SetAutomationId(body, "RoundBallDango");
        AutomationProperties.SetName(ballVisual, "官方小圆球");
        AutomationProperties.SetAutomationId(ballVisual, "RoundBallVisual");
        foreach (var item in new UIElement[] { ballCircle, ballInnerBorder, ballBand, ballMarker }) ballVisual.Children.Add(item);
        foreach (var item in new UIElement[] { floor, leftWall, rightWall, ballVisual, body, hat, bodyOutline, ballOutline }) scene.Children.Add(item);
        Panel.SetZIndex(ballVisual, 5); Panel.SetZIndex(body, 10); Panel.SetZIndex(hat, 20);
        Panel.SetZIndex(bodyOutline, 30); Panel.SetZIndex(ballOutline, 30);
        body.MouseLeftButtonDown += BeginDrag;
        body.MouseMove += MoveDrag;
        body.MouseLeftButtonUp += (_, e) => { ClearInput(); e.Handled = true; };
        body.LostMouseCapture += (_, _) => ClearInput();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (!IsActionKey(e.Key)) return;
            input.Release(e.Key);
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
            ResetScenario(scenario);
            diagnostics?.Start();
            timer.Start(); FocusScene();
        };
        timer.Tick += OnTick; restoreTimer.Tick += OnRestoreTick;
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
        if (paused || faulted || closing) return;
        input.Press(e.Key, e.IsRepeat);
    }

    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (paused || faulted || closing || displayedGeometry is null || !displayedGeometry.FillContains(e.GetPosition(body))) return;
        FocusScene(); dragTarget = ToDangoWorld(e.GetPosition(scene));
        dragging = body.CaptureMouse(); e.Handled = true;
    }

    private void MoveDrag(object sender, MouseEventArgs e)
    {
        if (!dragging || !body.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        dragTarget = ToDangoWorld(e.GetPosition(scene)); e.Handled = true;
    }

    private void ClearInput()
    {
        inputClearCount++; input.Clear(); dragging = false; dragTarget = default;
        if (body.IsMouseCaptured) body.ReleaseMouseCapture();
    }

    private void FocusScene() => Keyboard.Focus(scene);
    private void ResetClock() { accumulator = 0; lastTickSeconds = clock.Elapsed.TotalSeconds; }
    private void SetPaused(bool value)
    {
        paused = value; pauseButton.Content = paused ? "继续" : "暂停";
        ClearInput(); ResetClock(); UpdateStatus();
    }

    private void HideTemporarily()
    {
        if (closing) return;
        restoringPaused = paused; SetPaused(true); Hide(); restoreTimer.Start();
    }

    private void OnRestoreTick(object? sender, EventArgs e)
    {
        restoreTimer.Stop(); Show(); Activate(); SetPaused(restoringPaused); FocusScene();
    }

    private void ResetScenario(RoundBallScenario value)
    {
        try
        {
            ClearInput(); restoreTimer.Stop(); session?.Dispose(); session = null;
            ClearPresentation();
            scenario = value; session = new(libraryPath, scenario);
            ballGravityScale = session.BallGravityScale;
            AcceptFrame(session.LastFrame); CompleteReset();
        }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void ResetCurrent()
    {
        if (session is null) return;
        ClearInput(); restoreTimer.Stop();
        var next = session.Reset();
        ClearPresentation(); AcceptFrame(next); CompleteReset();
    }

    private void ClearPresentation()
    {
        presenter.Reset(); presentation = null; currentFrame = null; displayedGeometry = null;
        facing = DangoFacingDirection.Center;
        body.Visibility = hat.Visibility = bodyOutline.Visibility = ballVisual.Visibility = ballOutline.Visibility = Visibility.Hidden;
    }

    private void CompleteReset()
    {
        currentFailure = null; faulted = false; paused = false; pauseButton.Content = "暂停";
        if (diagnostics is null) { failure = null; ExitCode = 0; }
        ResetClock(); Draw(); UpdateStatus(); FocusScene();
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
            while (accumulator + 1e-9 >= RoundBallSession.FixedStep.TotalSeconds && count < 4)
            {
                var horizontal = (input.Contains(Key.D) ? 1 : 0) - (input.Contains(Key.A) ? 1 : 0);
                var jump = input.ConsumeJump();
                lastStepJumpRequested = jump;
                if (jump) jumpRequestsConsumed++;
                if (horizontal != 0) facing = horizontal < 0 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
                var started = Stopwatch.GetTimestamp(); lifetimeStepCalls++;
                var next = session.Step(horizontal, input.Contains(Key.S), jump, dragging, dragTarget.X, dragTarget.Y);
                var cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (stepMilliseconds.Count < 60_000) stepMilliseconds.Add(cost);
                AcceptFrame(next);
                successfulSteps++; count++; accumulator -= RoundBallSession.FixedStep.TotalSeconds;
            }
            if (count == 4 && accumulator >= RoundBallSession.FixedStep.TotalSeconds)
            { droppedSeconds += accumulator; accumulator = 0; }
            if (count > 0) { Draw(); newFrameDraws++; }
            if (now >= nextStatusSeconds) { UpdateStatus(); nextStatusSeconds = now + .15; }
        }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void AcceptFrame(DangoRoundBallFrame frame)
    {
        if (!frame.Physics.Succeeded)
            throw new InvalidOperationException($"native 锁存故障 {frame.Physics.ErrorCode}；保留最后安全画面。");
        var nextBody = frame.Physics.Bodies.Single();
        if (nextBody.SignedArea < .42 || nextBody.MaximumSpeed > 12 + 1e-5)
            throw new InvalidDataException("真实团子物理帧超过面积／速度门槛。");
        var nextPresentation = presenter.Present(nextBody, RoundBallSession.FixedStep, facing);
        presenterAdvances++;
        var result = DangoStreamGeometryFactory.TryCreate(nextPresentation.Contour, 160, 120);
        if (!result.Succeeded || nextPresentation.FallbackReason != PhysicsPresentationFallbackReason.None)
            throw new InvalidDataException("真实团子绘制轮廓校验失败；保留最后安全画面。");
        if (frame.Ball is { } ball)
        {
            var definition = frame.Definition ?? throw new InvalidDataException("球快照缺少同帧定义。");
            if (!double.IsFinite(ball.AngleRadians) || !double.IsFinite(ball.AngularVelocity) ||
                !double.IsFinite(ball.CenterQX) || !double.IsFinite(ball.CenterQY) ||
                Math.Abs(ball.AngularVelocity) > 40 + 1e-5 ||
                Math.Sqrt(ball.VelocityQX * ball.VelocityQX + ball.VelocityQY * ball.VelocityQY) > 12 + 1e-5)
                throw new InvalidDataException("真实球状态超过角度／速度门槛。");
            latestPaintedGapQ = RoundBallWindowDiagnostics.PaintedGapQ(nextBody, ball.CenterQX, ball.CenterQY, definition.RadiusQ);
            var nativeError = 0d;
            foreach (var contact in new[] { frame.DangoContact, frame.FloorContact })
            {
                if (!contact.Touching) continue;
                if (!double.IsFinite(contact.GapQ)) throw new InvalidDataException("球接触诊断间隙无效。");
                nativeError = Math.Max(nativeError, Math.Abs(contact.GapQ));
            }
            var paintedError = Math.Max(0, -latestPaintedGapQ);
            if (frame.DangoContact.Touching) paintedError = Math.Max(paintedError, Math.Abs(latestPaintedGapQ));
            var floorGap = ball.CenterQY - definition.RadiusQ - DangoPhysicsDefaults.Current.GroundHeight;
            nativeError = Math.Max(nativeError, Math.Max(0, -floorGap));
            if (frame.FloorContact.Touching || ball.IsSleeping) nativeError = Math.Max(nativeError, Math.Abs(floorGap));
            peakNativeContactErrorQ = Math.Max(peakNativeContactErrorQ, nativeError);
            peakPaintedContactErrorQ = Math.Max(peakPaintedContactErrorQ, paintedError);
            if (nativeError > RoundBallWindowDiagnostics.ContactBudgetQ || paintedError > RoundBallWindowDiagnostics.ContactBudgetQ)
                throw new InvalidDataException("圆球接触超出已冻结的 q 间隙预算；保留最后安全画面。");
        }
        else latestPaintedGapQ = 0;
        currentFrame = frame; presentation = nextPresentation; displayedGeometry = result.Geometry;
    }

    private Point ToDangoWorld(Point point) => new((point.X - OriginX) / HorizontalScale, (OriginY - point.Y) / VerticalScale);
    private Point DangoToScreen(double x, double y) => new(OriginX + x * HorizontalScale, OriginY - y * VerticalScale);
    private Point QToScreen(double x, double y) => new(OriginX + x * VerticalScale, OriginY - y * VerticalScale);

    private void SafeDraw()
    {
        try { Draw(); }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void Draw()
    {
        drawCalls++;
        var left = DangoToScreen(RoundBallSession.LeftBound, DangoPhysicsDefaults.Current.GroundHeight);
        var right = DangoToScreen(RoundBallSession.RightBound, DangoPhysicsDefaults.Current.GroundHeight);
        floor.X1 = left.X; floor.X2 = right.X; floor.Y1 = floor.Y2 = FloorY;
        foreach (var item in new[] { (Wall: leftWall, X: left.X), (Wall: rightWall, X: right.X) })
        {
            item.Wall.X1 = item.Wall.X2 = item.X; item.Wall.Y1 = 12; item.Wall.Y2 = FloorY;
        }
        if (currentFrame is null || presentation is null || displayedGeometry is null) return;
        var nativeBody = currentFrame.Physics.Bodies.Single();
        var core = DangoToScreen(nativeBody.CoreX, nativeBody.CoreY);
        var transform = new ScaleTransform(VisualScale, VisualScale);
        foreach (var visual in new FrameworkElement[] { body, hat, bodyOutline })
        {
            visual.RenderTransform = transform;
            Canvas.SetLeft(visual, core.X - VisualCenterX * VisualScale);
            Canvas.SetTop(visual, core.Y - VisualCenterY * VisualScale);
        }
        body.ApplyFrame(presentation); hat.ApplyFrame(presentation);
        if (body.LastContourFailure != DangoContourFailure.None)
            throw new InvalidDataException("真实团子绘制轮廓校验失败。");
        body.Visibility = hat.Visibility = Visibility.Visible;
        bodyOutline.Data = displayedGeometry;
        bodyOutline.Visibility = outlineToggle.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        if (currentFrame.Ball is { } ball && currentFrame.Definition is { } definition)
        {
            var center = QToScreen(ball.CenterQX, ball.CenterQY);
            var radius = definition.RadiusQ * VerticalScale;
            var diameter = radius * 2;
            ballVisual.Width = ballVisual.Height = ballCircle.Width = ballCircle.Height = diameter;
            ballVisual.Clip = new EllipseGeometry(new Point(radius, radius), radius, radius);
            ballInnerBorder.Width = ballInnerBorder.Height = Math.Max(0, diameter - 1.5);
            Canvas.SetLeft(ballInnerBorder, .75); Canvas.SetTop(ballInnerBorder, .75);
            ballBand.RenderTransform = new ScaleTransform(radius / 24, radius / 24);
            Canvas.SetLeft(ballMarker, radius * 1.72 - 2); Canvas.SetTop(ballMarker, radius - 2);
            ballVisual.RenderTransform = new RotateTransform(-ball.AngleRadians * 180 / Math.PI, radius, radius);
            Canvas.SetLeft(ballVisual, center.X - radius); Canvas.SetTop(ballVisual, center.Y - radius);
            ballVisual.Visibility = Visibility.Visible;
            ballOutline.Width = ballOutline.Height = diameter;
            Canvas.SetLeft(ballOutline, center.X - radius); Canvas.SetTop(ballOutline, center.Y - radius);
            ballOutline.Visibility = outlineToggle.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        }
        else ballVisual.Visibility = ballOutline.Visibility = Visibility.Hidden;
    }

    private void UpdateStatus()
    {
        var nativeBody = currentFrame?.Physics.Bodies.Single();
        var ball = currentFrame?.Ball;
        var support = nativeBody?.IsGrounded == true ? "团子在地面" : "团子在空中";
        var ballStatus = ball is null ? "无球对照" : ball.IsSleeping ? "球已停稳" : "球在运动";
        status.Text = currentFailure is not null ? $"已停止：{currentFailure}\n可以重置当前状态或选择场景重新开始。" :
            $"{(paused ? "已暂停" : "运行中")} · {support} · {ballStatus}\n球只通过与团子、地面和边界接触运动；球顶不能增加起跳资格。";
        if (diagnosticToggle.IsChecked == true && currentFrame is not null)
            status.Text += $"\n场景修订 {currentFrame.SceneRevision} · 成功步 {currentFrame.Step} · s={VisualScale:F3} · " +
                $"球角度 {ball?.AngleRadians ?? 0:F4} rad · 显示轮廓间隙 {latestPaintedGapQ:F5} q";
        status.Foreground = faulted ? Brushes.Firebrick : Brushes.DarkSlateGray;
    }

    private object Snapshot()
    {
        var dpi = VisualTreeHelper.GetDpi(scene);
        var sceneScreen = PresentationSource.FromVisual(scene) is not null ? scene.PointToScreen(new()) : new Point();
        var bodyBounds = displayedGeometry?.Bounds ?? Rect.Empty;
        if (!bodyBounds.IsEmpty && currentFrame is not null)
        {
            bodyBounds = body.RenderTransform.TransformBounds(bodyBounds);
            bodyBounds.Offset(Canvas.GetLeft(body), Canvas.GetTop(body));
        }
        var ballRadius = currentFrame?.Definition is { } definition ? definition.RadiusQ * VerticalScale : 0;
        var marker = currentFrame?.Ball is not null && ballVisual.Visibility == Visibility.Visible ?
            ballVisual.TranslatePoint(new(ballRadius * 1.72, ballRadius), scene) : new Point();
        var ballSamples = currentFrame?.Ball is not null ? Enumerable.Range(0, 8).Select(index =>
        {
            var angle = index * Math.PI / 4;
            var radiusX = ballCircle.ActualWidth / 2;
            var radiusY = ballCircle.ActualHeight / 2;
            var point = ballCircle.TranslatePoint(new(radiusX * (1 + Math.Cos(angle)), radiusY * (1 + Math.Sin(angle))), scene);
            return new { point.X, point.Y };
        }).ToArray() : null;
        return new
        {
            SchemaVersion = 1,
            NativeModuleSha256 = moduleHash,
            EvidenceMode = diagnostics is null ? "InteractiveAcceptance" : diagnostics.AutomaticObservation ? "TenMinuteObservation" : "ExternalWindowCheck",
            ProcessId = Environment.ProcessId,
            WindowHandle = new WindowInteropHelper(this).Handle.ToInt64(),
            Scenario = scenario.ToString(),
            Frame = currentFrame,
            NativeError = session?.LastFrame.Physics.ErrorCode ?? currentFrame?.Physics.ErrorCode ?? 0,
            ActiveSeconds = activeSeconds,
            Paused = paused, Visible = IsVisible, WindowState = WindowState.ToString(), Minimized = WindowState == WindowState.Minimized,
            Faulted = faulted, Failure = currentFailure,
            SchedulerCallbacks = schedulerCallbacks, StepCalls = lifetimeStepCalls, SuccessfulSteps = successfulSteps,
            DrawCalls = drawCalls, NewFrameDraws = newFrameDraws, PresenterAdvances = presenterAdvances,
            VisualScale, HorizontalScale, VerticalScale,
            SceneWidthDip = scene.ActualWidth, SceneHeightDip = scene.ActualHeight,
            SceneOriginScreenPixels = new { sceneScreen.X, sceneScreen.Y },
            DpiScaleX = dpi.DpiScaleX, DpiScaleY = dpi.DpiScaleY,
            ProjectionOriginDip = new { X = OriginX, Y = OriginY },
            BodyLayout = currentFrame is null ? null : new { LeftDip = Canvas.GetLeft(body), TopDip = Canvas.GetTop(body), WidthDip = body.Width * VisualScale, HeightDip = body.Height * VisualScale },
            BodyPaintBoundsDip = bodyBounds.IsEmpty ? null : new { bodyBounds.X, bodyBounds.Y, bodyBounds.Width, bodyBounds.Height },
            BallCenterDip = currentFrame?.Ball is { } ball ? new { X = QToScreen(ball.CenterQX, ball.CenterQY).X, Y = QToScreen(ball.CenterQX, ball.CenterQY).Y } : null,
            BallRadiusDip = ballRadius,
            BallVisualLayoutDip = currentFrame?.Ball is null ? null : new { Left = Canvas.GetLeft(ballVisual), Top = Canvas.GetTop(ballVisual) },
            BallVisualWidthDip = ballVisual.Width, BallVisualHeightDip = ballVisual.Height,
            BallCircleActualWidthDip = ballCircle.ActualWidth, BallCircleActualHeightDip = ballCircle.ActualHeight,
            BallCircleEightPointsDip = ballSamples,
            BallVisualVisible = ballVisual.Visibility == Visibility.Visible,
            BallWpfAngleDegrees = ballVisual.RenderTransform is RotateTransform rotation ? rotation.Angle : 0,
            MarkerCenterDip = new { marker.X, marker.Y },
            MouseCaptureOwned = body.IsMouseCaptured, Dragging = dragging,
            SceneHasKeyboardFocus = Keyboard.FocusedElement == scene,
            InputClearCount = inputClearCount,
            JumpPending = input.JumpPending, JumpHeld = input.JumpHeld,
            JumpRequestsConsumed = jumpRequestsConsumed, LastStepJumpRequested = lastStepJumpRequested,
            PeakNativeContactErrorQ = peakNativeContactErrorQ, PeakPublicPaintedContactErrorQ = peakPaintedContactErrorQ,
            BallGravityScale = ballGravityScale,
            DeferredStatePublications = diagnostics?.DeferredStatePublications ?? 0,
            LatestPublicPaintedGapQ = latestPaintedGapQ,
            PauseCycles = pauseCycles, HideCycles = hideCycles,
            OwnedWorldCount = RoundBallSession.OwnedWorldCount,
        };
    }

    private void StopWithFailure(Exception error)
    {
        currentFailure = error.Message; failure ??= error.Message; faulted = true; ExitCode = 1;
        ClearInput(); ResetClock(); UpdateStatus();
        if (diagnostics is not null) Dispatcher.BeginInvoke(new Action(Close));
    }

    private void Observe()
    {
        if (diagnostics is null) return;
        var seconds = diagnostics.ElapsedSeconds;
        if (diagnostics.AutomaticObservation && observationCycle < RoundBallWindowDiagnostics.RequiredCycles)
        {
            var start = (observationCycle + 1) * 15d;
            if (observationStage == 0 && seconds >= start)
            { suspendedStepCalls = lifetimeStepCalls; SetPaused(true); observationStage = 1; pauseCycles++; }
            if (observationStage == 1 && seconds >= start + 1)
            { Hide(); observationStage = 2; hideCycles++; }
            if (observationStage == 2 && seconds >= start + 2)
            {
                if (lifetimeStepCalls != suspendedStepCalls)
                    throw new InvalidOperationException("暂停／隐藏期间发生了物理推进调用。");
                Show(); Activate(); SetPaused(false); FocusScene(); observationCycle++; observationStage = 0;
            }
        }
        diagnostics.Record(Snapshot());
        if (diagnostics.AutomaticObservation && seconds >= RoundBallWindowDiagnostics.MinimumObservationSeconds && activeSeconds >= RoundBallWindowDiagnostics.MinimumActiveSeconds)
            Close();
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
            if (diagnostics is null) return;
            if (diagnostics.AutomaticObservation && (diagnostics.ElapsedSeconds < RoundBallWindowDiagnostics.MinimumObservationSeconds ||
                activeSeconds < RoundBallWindowDiagnostics.MinimumActiveSeconds ||
                pauseCycles != RoundBallWindowDiagnostics.RequiredCycles || hideCycles != RoundBallWindowDiagnostics.RequiredCycles ||
                observationCycle != RoundBallWindowDiagnostics.RequiredCycles))
            { failure ??= "观察不足600秒墙钟、560秒活动时间或20次完整暂停／隐藏恢复；不能计为完整观察通过。"; ExitCode = 1; }
            var ordered = stepMilliseconds.Order().ToArray();
            diagnostics.Complete(new
            {
                NativeModuleSha256 = moduleHash,
                EvidenceMode = diagnostics.AutomaticObservation ? "TenMinuteObservation" : "ExternalWindowCheck",
                FullObservationPerformed = diagnostics.AutomaticObservation && failure is null,
                ElapsedSeconds = diagnostics.ElapsedSeconds, ActiveSeconds = activeSeconds,
                RoundBallWindowDiagnostics.MinimumObservationSeconds, RoundBallWindowDiagnostics.MinimumActiveSeconds,
                RoundBallWindowDiagnostics.RequiredCycles,
                TimeMeasurement = "Wall time and active time; active excludes paused, hidden and minimized intervals; dropped simulation time is separate",
                StepCalls = lifetimeStepCalls, SuccessfulSteps = successfulSteps, NewFrameDraws = newFrameDraws,
                NewFrameDrawsPerSecond = activeSeconds > 0 ? newFrameDraws / activeSeconds : 0,
                FrameMeasurement = "New native-state Draw updates per active second; not display composition or presentation FPS",
                PhysicsStepsPerSecond = activeSeconds > 0 ? successfulSteps / activeSeconds : 0,
                StepP95Milliseconds = ordered.Length == 0 ? 0 : ordered[(int)Math.Floor((ordered.Length - 1) * .95)],
                StepMaximumMilliseconds = ordered.LastOrDefault(),
                diagnostics.PeakWorkingSetBytes,
                diagnostics.DeferredStatePublications,
                PeakNativeContactErrorQ = peakNativeContactErrorQ, PeakPublicPaintedContactErrorQ = peakPaintedContactErrorQ,
                RoundBallWindowDiagnostics.ContactBudgetQ,
                ContactMeasurement = "Native diagnostics plus independent 64-samples-per-segment public painted contour in q; not raster or human acceptance",
                DroppedSeconds = droppedSeconds, PauseCycles = pauseCycles, HideCycles = hideCycles,
                WorldDisposed = session is null, TimerStopped = !timer.IsEnabled && !restoreTimer.IsEnabled,
                OwnedWorldCount = RoundBallSession.OwnedWorldCount,
                ActiveTimerOwners = (timer.IsEnabled ? 1 : 0) + (restoreTimer.IsEnabled ? 1 : 0),
                MouseCaptureReleased = !body.IsMouseCaptured,
                FinalState = Snapshot(), ExitCode, Failure = failure,
                HardwareInputAndHumanAcceptance = "Not verified by this observation",
                OtherDpiHardwareMultiMonitorSleepAnd59Fps = "Not verified by this observation",
            });
        }
        catch (Exception error) { failure ??= error.Message; ExitCode = 1; Console.Error.WriteLine(error); }
        finally { diagnostics?.Dispose(); }
    }
}

/// <summary>Latches a real press until one fixed step; repeat never creates another jump.</summary>
internal sealed class RoundBallInputState
{
    private readonly HashSet<Key> held = [];
    private bool pendingJump;

    internal bool JumpPending => pendingJump;
    internal bool JumpHeld => held.Contains(Key.Space);

    internal bool Contains(Key key) => held.Contains(key);

    internal void Press(Key key, bool repeat)
    {
        if (repeat && !held.Contains(key)) return;
        if (key == Key.Space && !repeat && !held.Contains(key)) pendingJump = true;
        held.Add(key);
    }

    internal void Release(Key key) => held.Remove(key);

    internal bool ConsumeJump()
    {
        var value = pendingJump;
        pendingJump = false;
        return value;
    }

    internal void Clear() { held.Clear(); pendingJump = false; }
}
