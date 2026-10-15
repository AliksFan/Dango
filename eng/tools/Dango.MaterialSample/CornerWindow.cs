using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dango.MaterialSample;

// Review-only window: the eight size/softness/landing-build extremes, each in its own
// single-body world, all driven by the same keyboard input.
internal sealed class CornerWindow : Window
{
    private const LandingBuildTable Table = LandingBuildTable.SizeDampingPreview;
    private readonly string library;
    private readonly DispatcherTimer timer = new() { Interval = MaterialSession.FixedStep };
    private readonly CornerView[] views;
    private readonly TextBlock status = new() { Margin = new(16, 6, 16, 10), Foreground = Brushes.DimGray };
    private bool left, right, jump;

    private static readonly (double Size, double Softness, double Build)[] Corners =
    [
        (80, 0, 0), (80, 0, 100), (80, 100, 0), (80, 100, 100),
        (120, 0, 0), (120, 0, 100), (120, 100, 0), (120, 100, 100),
    ];

    internal CornerWindow(string library)
    {
        this.library = library;
        Title = "Dango · 八项极端组合";
        Width = 1500; Height = 860; MinWidth = 1000; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(252, 250, 246));
        var root = new DockPanel();
        var heading = new TextBlock
        {
            Text = $"大小 × 柔软度 × 胖瘦 极端组合 · 映射 {LandingBuildMapping.Version(Table)}",
            FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(16, 12, 16, 2),
        };
        var help = new TextBlock
        {
            Text = "A/D 或 ←/→ 左右移动 · 空格/W 跳跃 · R 重新下落 · 八只团子同时响应；各自独立物理世界，可用鼠标拖动",
            Foreground = Brushes.DimGray, Margin = new(16, 0, 16, 6),
        };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new UniformGrid4x2();
        views = Corners.Select(c => new CornerView(c.Size, c.Softness, c.Build)).ToArray();
        foreach (var view in views) grid.Children.Add(view.Frame);
        root.Children.Add(grid);
        Content = root;
        KeyDown += (_, e) => SetKey(e.Key, true, e.IsRepeat);
        KeyUp += (_, e) => SetKey(e.Key, false, false);
        Deactivated += (_, _) => { left = right = jump = false; };
        Loaded += (_, _) => { Reset(); timer.Tick += (_, _) => Tick(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); foreach (var view in views) view.Dispose(); };
    }

    private void SetKey(Key key, bool down, bool repeat)
    {
        switch (key)
        {
            case Key.A or Key.Left: left = down; break;
            case Key.D or Key.Right: right = down; break;
            case Key.Space or Key.W or Key.Up: if (down && !repeat) jump = true; break;
            case Key.R: if (down && !repeat) Reset(); break;
            default: return;
        }
    }

    private void Reset()
    {
        foreach (var view in views) view.Reset(library, Table);
        status.Text = $"原生库：{System.IO.Path.GetFileName(library)}";
    }

    private void Tick()
    {
        var horizontal = (right ? 1 : 0) - (left ? 1 : 0);
        foreach (var view in views) view.Step(horizontal, jump);
        jump = false;
    }

    private sealed class UniformGrid4x2 : System.Windows.Controls.Primitives.UniformGrid
    {
        internal UniformGrid4x2() { Columns = 4; Rows = 2; Margin = new(8, 0, 8, 0); }
    }
}

internal sealed class CornerView : FrameworkElement, IDisposable
{
    private const double Scale = 78;
    private readonly double size, softness, build;
    private readonly TextBlock label = new() { Margin = new(8, 6, 8, 0), FontWeight = FontWeights.SemiBold };
    private readonly TextBlock metrics = new() { Margin = new(8, 0, 8, 6), Foreground = Brushes.DimGray, FontSize = 12 };
    private MaterialSession? session;
    private string? error;
    private bool dragging;
    private Point dragTarget;
    private double cameraX;

    internal Border Frame { get; }

    internal CornerView(double size, double softness, double build)
    {
        this.size = size; this.softness = softness; this.build = build;
        label.Text = $"大小 {size}  ·  柔软度 {softness}  ·  {(build == 0 ? "胖 0" : "瘦 100")}";
        var panel = new DockPanel();
        DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
        DockPanel.SetDock(metrics, Dock.Bottom); panel.Children.Add(metrics);
        panel.Children.Add(this);
        Frame = new Border
        {
            Child = panel, Margin = new(4), CornerRadius = new(6), Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 214, 200)), BorderThickness = new(1),
        };
        ClipToBounds = true; Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) => { dragging = CaptureMouse(); dragTarget = ToWorld(e.GetPosition(this)); };
        MouseMove += (_, e) => { if (dragging) dragTarget = ToWorld(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => { dragging = false; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => dragging = false;
    }

    private double GroundY => ActualHeight - 28;
    private double Ground => session?.Configuration.GroundHeight ?? -.72;

    private Point ToWorld(Point p) => new((p.X - ActualWidth / 2) / Scale + cameraX, Ground + (GroundY - p.Y) / Scale);

    internal void Reset(string library, LandingBuildTable table)
    {
        session?.Dispose(); session = null; error = null; cameraX = 0;
        try
        {
            var configuration = new LandingBuildControls(size, softness, build).Configure(table);
            session = new MaterialSession(library, configuration, 0);
        }
        catch (Exception e) { error = e.Message; }
        InvalidateVisual();
    }

    internal void Step(double horizontal, bool jump)
    {
        if (session is null || error is not null) return;
        try
        {
            var state = session.Step(jump: jump, drag: dragging, dragX: dragTarget.X, dragY: dragTarget.Y, horizontal: horizontal);
            if (!dragging) cameraX += (state.CoreX - cameraX) * .12;
            var bounds = SampleGeometry.Create(state, false, Scale, session.BodySize).Bounds;
            metrics.Text = $"{(state.IsGrounded ? "接地" : "离地")}  ·  宽高比 {bounds.Width / bounds.Height:F3}  ·  帧 {session.Frames}";
        }
        catch (Exception e) { error = e.Message; dragging = false; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var tick = new Pen(new SolidColorBrush(Color.FromRgb(220, 210, 196)), 1);
        drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(190, 172, 150)), 2), new(0, GroundY), new(ActualWidth, GroundY));
        // Ground ticks every half unit make horizontal travel visible while the camera follows.
        var first = Math.Floor((cameraX - ActualWidth / 2 / Scale) * 2) / 2;
        for (var x = first; x <= cameraX + ActualWidth / 2 / Scale; x += .5)
        {
            var sx = ActualWidth / 2 + (x - cameraX) * Scale;
            drawing.DrawLine(tick, new(sx, GroundY), new(sx, GroundY + 8));
        }
        if (error is not null)
        {
            metrics.Text = "已停止（按 R 重新下落）";
            drawing.DrawText(new FormattedText(error, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12, Brushes.Firebrick, 1)
            { MaxTextWidth = Math.Max(40, ActualWidth - 16) }, new(8, 8));
        }
        if (session?.State is not { } state) return;
        Geometry geometry;
        try { geometry = SampleGeometry.Create(state, false, Scale, session.BodySize); }
        catch (Exception e) { error ??= $"轮廓绘制停止：{e.Message}"; return; }
        var center = new Point(ActualWidth / 2 + (state.CoreX - cameraX) * Scale, GroundY - (state.CoreY - Ground) * Scale);
        drawing.PushTransform(new TranslateTransform(center.X, center.Y));
        drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 213, 165)), null, geometry);
        var b = geometry.Bounds;
        var eye = new SolidColorBrush(Color.FromRgb(43, 32, 30));
        var eyeR = 3.2 * session.BodySize;
        foreach (var side in new[] { -1, 1 })
            drawing.DrawEllipse(eye, null, new((b.Left + b.Right) / 2 + side * b.Width * .17, b.Top + b.Height * .5), eyeR, eyeR * 1.15);
        drawing.Pop();
    }

    public void Dispose() { session?.Dispose(); session = null; }
}
