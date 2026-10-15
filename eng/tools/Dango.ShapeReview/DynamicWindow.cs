using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.ShapeReview;

internal sealed class DynamicWindow : Window
{
    private const double Zoom = 2;
    private readonly Canvas scene = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly ShapePath skin = new() { Fill = new SolidColorBrush(Color.FromRgb(255, 232, 194)) };
    private readonly System.Windows.Shapes.Line floor = new() { Stroke = Brushes.SlateGray, StrokeThickness = 1, IsHitTestVisible = false };
    private readonly TextBlock status = new() { Margin = new Thickness(14), TextWrapping = TextWrapping.Wrap };
    private readonly HashSet<Key> keys = [];
    private readonly Stopwatch watch = new();
    private readonly string executable;
    private DynamicSession? session;
    private Task<DynamicSession>? starting;
    private bool busy, closed, failed, resetRequested, jump, dragging;
    private Point target;
    private double previousTime, accumulator;
    private DynamicFrame? latest;

    public DynamicWindow(string executable)
    {
        this.executable = executable;
        Title = "Dango 动态评审｜隔离真实物理｜待人工验收";
        Width = 1040;
        Height = 720;
        MinWidth = 800;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Background = Brushes.WhiteSmoke };
        Content = root;
        var header = new StackPanel { Margin = new Thickness(14) };
        header.Children.Add(new TextBlock { Text = "隔离单体动态评审 · 实时 Box2D 候选 4", FontSize = 20 });
        header.Children.Add(new TextBlock { Text = "A / D 移动，S 下压，W 起跳；按住身体拖动，松开或切换窗口释放。无左右墙；移出画面可显式重置。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var reset = new Button { Content = "重置本次试验", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { ClearInput(); resetRequested = true; };
        header.Children.Add(reset);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(scene);
        scene.Children.Add(floor);
        scene.Children.Add(skin);
        skin.MouseLeftButtonDown += (_, e) =>
        {
            if (failed || session is null)
            {
                return;
            }
            dragging = true;
            target = Pointer(e.GetPosition(scene));
            scene.CaptureMouse();
            Focus();
            e.Handled = true;
        };
        scene.MouseMove += (_, e) => { if (dragging) target = Pointer(e.GetPosition(scene)); };
        scene.MouseLeftButtonUp += (_, _) => ClearInput();
        scene.LostMouseCapture += (_, _) => { dragging = false; };
        Deactivated += (_, _) => ClearInput();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.A or Key.D or Key.S or Key.W)
            {
                keys.Add(e.Key);
                if (e.Key == Key.W && !e.IsRepeat) jump = true;
                e.Handled = true;
            }
        };
        PreviewKeyUp += (_, e) => { keys.Remove(e.Key); };
        scene.SizeChanged += (_, _) => { if (latest is not null) Draw(latest); };
        Loaded += async (_, _) =>
        {
            try
            {
                starting = DynamicSession.Start(this.executable);
                session = await starting;
                if (closed)
                {
                    await session.DisposeAsync();
                    return;
                }
                watch.Start();
                CompositionTarget.Rendering += Advance;
                Focus();
            }
            catch (Exception error)
            {
                failed = true;
                status.Text = $"启动失败：{error.Message}。未替换正式程序。";
            }
        };
    }

    private Point Pointer(Point point) => new(
        Math.Clamp((point.X - scene.ActualWidth / 2) / (72 * Zoom), -100, 100),
        Math.Clamp((GroundY - point.Y) / (58 * Zoom) - .72, -100, 100));

    private double GroundY => scene.ActualHeight * .75;

    internal static void VerifyView(DynamicFrame frame, string output)
    {
        var window = new DynamicWindow("not-started");
        var root = (DockPanel)window.Content;
        root.Measure(new Size(1040, 720));
        root.Arrange(new Rect(0, 0, 1040, 720));
        root.UpdateLayout();
        window.Draw(frame);
        root.UpdateLayout();
        var ground = window.Pointer(new Point(window.scene.ActualWidth / 2, window.GroundY));
        var unit = window.Pointer(new Point(window.scene.ActualWidth / 2 + 144, window.GroundY - 116));
        if (Math.Abs(ground.X) > 1e-10 || Math.Abs(ground.Y + .72) > 1e-10 || Math.Abs(unit.X - 1) > 1e-10 || Math.Abs(unit.Y - .28) > 1e-10)
            throw new InvalidDataException("Pointer/ground mapping mismatch");
        window.keys.Add(Key.D); window.jump = true; window.dragging = true;
        window.ClearInput();
        if (window.keys.Count != 0 || window.jump || window.dragging) throw new InvalidDataException("Input clear failed");
        var bitmap = new RenderTargetBitmap(1040, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "dynamic-review-render.png"));
        png.Save(stream);
    }

    private void ClearInput()
    {
        keys.Clear();
        jump = false;
        dragging = false;
        if (scene.IsMouseCaptured) scene.ReleaseMouseCapture();
    }

    private async void Advance(object? sender, EventArgs args)
    {
        if (closed || busy || session is null) return;
        var now = watch.Elapsed.TotalSeconds;
        var elapsed = now - previousTime;
        previousTime = now;
        if (failed && !resetRequested) return;
        busy = true;
        try
        {
            if (resetRequested)
            {
                await session.Reset();
                resetRequested = false;
                failed = false;
                accumulator = 0;
            }
            accumulator = Math.Min(accumulator + elapsed, 4d / 60);
            for (var steps = 0; steps < 4 && accumulator >= 1d / 60; steps++)
            {
                var frame = await session.Step(
                    (keys.Contains(Key.D) ? 1 : 0) - (keys.Contains(Key.A) ? 1 : 0),
                    keys.Contains(Key.S) ? 1 : 0, jump, dragging, target.X, target.Y);
                jump = false;
                accumulator -= 1d / 60;
                if (closed) return;
                latest = frame;
                if (frame.Failed)
                {
                    failed = true;
                    ClearInput();
                    status.Text = $"原生故障已锁存（第 {frame.Number} 帧）。已停止推进，保留最后画面；只有显式重置可重新试验。";
                    return;
                }
                Draw(frame);
            }
        }
        catch (Exception error)
        {
            failed = true;
            ClearInput();
            status.Text = $"通信或渲染失败，已停止推进：{error.Message}。请关闭后重新启动本次试验。";
        }
        finally
        {
            busy = false;
        }
    }

    private void Draw(DynamicFrame frame)
    {
        if (frame.Number % 60 == 0)
            Title = $"Dango 动态评审｜隔离真实物理｜模拟 {frame.Number / 60} 秒｜awake {frame.Awake}/17";
        var points = frame.Nodes.Select(point =>
        {
            var length = Math.Sqrt(point.X * point.X + point.Y * point.Y);
            return new PresentationPoint(point.X * (1 + .045 / length), point.Y * (1 + .045 / length));
        }).ToArray();
        var maximum = points.Max(point => Math.Max(Math.Abs(point.X), Math.Abs(point.Y)));
        var scale = maximum > .98 ? .98 / maximum : 1;
        var contour = points.Select(point => new PresentationPoint(point.X * scale, point.Y * scale)).ToArray();
        var geometry = DangoStreamGeometryFactory.TryCreate(contour, 160, 120);
        if (!geometry.Succeeded || geometry.Geometry is null) throw new InvalidOperationException($"轮廓被真实渲染器拒绝：{geometry.Failure}");
        skin.Data = geometry.Geometry;
        skin.RenderTransform = new ScaleTransform(Zoom, Zoom);
        Canvas.SetLeft(skin, scene.ActualWidth / 2 + frame.X * 72 * Zoom - 80 * Zoom);
        Canvas.SetTop(skin, GroundY - (frame.Y + .72) * 58 * Zoom - 66 * Zoom);
        floor.X1 = 0; floor.X2 = scene.ActualWidth; floor.Y1 = floor.Y2 = GroundY;
        status.Text = $"{(frame.Sleeping ? "休眠" : "活动")}｜原生 awake {frame.Awake}/17｜资格 {frame.Stable}/120｜速度 {frame.Speed:F5}｜模拟 {frame.Number / 60d:F2} 秒\n"
            + "整场景等比 2×，原显示比例保持。默认初次休眠约 35 秒；卡顿时限制补帧，以显示的模拟时间为准。\n"
            + "静态外形已接受；动态效果待你确认。本窗口只显示身体皮肤，无多体/左右墙，不改正式 DLL 或设置。";
    }

    protected override async void OnClosed(EventArgs e)
    {
        closed = true;
        CompositionTarget.Rendering -= Advance;
        ClearInput();
        base.OnClosed(e);
        try
        {
            if (session is null && starting is not null) session = await starting;
            if (session is not null) await session.DisposeAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine(error.Message);
        }
        Application.Current.Shutdown();
    }
}
