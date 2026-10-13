using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Review-only window: two or three bodies in one shared multi-body world, so contact,
// stacking and group rest/wake can be watched. Click a body to focus it; only the focused
// body takes keyboard input. Rest state is read from the native module.
internal sealed class GroupWindow : Window
{
    private static readonly (string Name, (double X, double Y)[] Spawns)[] Layouts =
    [
        ("双团子并排", [(-.75, 0), (.75, 0)]),
        ("三团子叠放", [(-.7, 0), (.7, 0), (0, 2.0)]),
        ("三团子并排", [(-1.5, 0), (0, 0), (1.5, 0)]),
    ];

    private readonly string library;
    private readonly DispatcherTimer timer = new() { Interval = MaterialSession.FixedStep };
    private readonly GroupView view = new();
    private readonly TextBlock status = new() { Margin = new(16, 6, 16, 10), Foreground = Brushes.DimGray };
    private int layout;
    private bool left, right, down, jump;

    internal GroupWindow(string library)
    {
        this.library = library;
        Title = "Dango · 多团子互动";
        Width = 1300; Height = 760; MinWidth = 800; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(252, 250, 246));
        var root = new DockPanel();
        var heading = new TextBlock { Text = "多团子互动 · 同一物理世界", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(16, 12, 16, 2) };
        var help = new TextBlock
        {
            Text = "点击团子切换焦点 · 焦点团子：A/D 左右 · W/空格 跳 · S 下压 · 按住拖动 · 1/2/3 切换摆放 · R 重新开始",
            Foreground = Brushes.DimGray, Margin = new(16, 0, 16, 6),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 0, 12, 6) };
        for (var i = 0; i < Layouts.Length; i++)
        {
            var index = i;
            var button = new Button { Content = $"{i + 1} {Layouts[i].Name}", Margin = new(4, 0, 4, 0), Padding = new(10, 3, 10, 3), Focusable = false };
            button.Click += (_, _) => Reset(index);
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        DockPanel.SetDock(buttons, Dock.Top); root.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(new Border
        {
            Child = view, Margin = new(12, 0, 12, 0), CornerRadius = new(6), Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 214, 200)), BorderThickness = new(1),
        });
        Content = root;
        KeyDown += (_, e) => SetKey(e.Key, true, e.IsRepeat);
        KeyUp += (_, e) => SetKey(e.Key, false, false);
        Deactivated += (_, _) => { left = right = down = jump = false; };
        Loaded += (_, _) => { Reset(0); timer.Tick += (_, _) => Tick(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); view.Dispose(); };
    }

    private void SetKey(Key key, bool pressed, bool repeat)
    {
        switch (key)
        {
            case Key.A or Key.Left: left = pressed; break;
            case Key.D or Key.Right: right = pressed; break;
            case Key.S or Key.Down: down = pressed; break;
            case Key.Space or Key.W or Key.Up: if (pressed && !repeat) jump = true; break;
            case Key.D1 or Key.NumPad1: if (pressed && !repeat) Reset(0); break;
            case Key.D2 or Key.NumPad2: if (pressed && !repeat) Reset(1); break;
            case Key.D3 or Key.NumPad3: if (pressed && !repeat) Reset(2); break;
            case Key.R: if (pressed && !repeat) Reset(layout); break;
        }
    }

    private void Reset(int index)
    {
        layout = index;
        view.Reset(library, Layouts[index].Spawns);
        Title = $"Dango · 多团子互动 · {Layouts[index].Name}";
    }

    private void Tick()
    {
        view.Step((right ? 1 : 0) - (left ? 1 : 0), down, jump);
        jump = false;
        status.Text = $"{Layouts[layout].Name} · 帧 {view.Frames} · {view.Summary} · 原生库：{System.IO.Path.GetFileName(library)}";
    }
}

internal sealed class GroupView : FrameworkElement, IDisposable
{
    private const double Scale = 100;
    private static readonly Brush Fill = new SolidColorBrush(Color.FromRgb(245, 213, 165));
    private static readonly Pen FocusPen = new(new SolidColorBrush(Color.FromRgb(140, 96, 60)), 2.5);
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(43, 32, 30));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(150, 136, 120));
    private readonly DangoSelfPhysicsParameters configuration = DangoPhysicsDefaults.Current;
    private DangoPhysicsWorld? world;
    private NativeRestReader? rest;
    private readonly List<DangoEntityId> ids = [];
    private IReadOnlyList<DangoBodyState> bodies = [];
    private bool[] resting = [];
    private float[][] previous = [];
    private string? error;
    private int focus;
    private Point press;
    private bool pressed, dragging;
    private Point dragTarget;
    private double cameraX;

    internal int Frames { get; private set; }

    internal string Summary => bodies.Count == 0 ? "" :
        string.Join("  ", Enumerable.Range(0, bodies.Count).Select(i => $"{i + 1}{(i == focus ? "（焦点）" : "")} {(resting[i] ? "睡" : "醒")}"));

    internal GroupView()
    {
        ClipToBounds = true; Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            var hit = HitBody(ToWorld(p));
            if (hit < 0) return;
            focus = hit; press = p; pressed = CaptureMouse(); dragTarget = ToWorld(p);
            InvalidateVisual();
        };
        MouseMove += (_, e) =>
        {
            if (!pressed) return;
            var p = e.GetPosition(this);
            // A click only changes focus; dragging starts after a small movement.
            if (!dragging && (p - press).Length > 4) dragging = true;
            dragTarget = ToWorld(p);
        };
        MouseLeftButtonUp += (_, _) => { pressed = dragging = false; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => pressed = dragging = false;
    }

    private double GroundY => ActualHeight - 40;

    private Point ToWorld(Point p) => new((p.X - ActualWidth / 2) / Scale + cameraX, configuration.GroundHeight + (GroundY - p.Y) / Scale);

    private Point ToScreen(double x, double y) => new(ActualWidth / 2 + (x - cameraX) * Scale, GroundY - (y - configuration.GroundHeight) * Scale);

    private int HitBody(Point w)
    {
        for (var i = 0; i < bodies.Count; i++)
        {
            var s = bodies[i];
            var geometry = SampleGeometry.Create(s, false, Scale);
            var local = ToScreen(w.X, w.Y) - ToScreen(s.CoreX, s.CoreY);
            if (geometry.FillContains(new Point(local.X, local.Y))) return i;
        }
        return -1;
    }

    internal void Reset(string library, (double X, double Y)[] spawns)
    {
        Dispose();
        ids.Clear(); bodies = []; error = null; focus = 0; Frames = 0; cameraX = 0; pressed = dragging = false;
        resting = new bool[spawns.Length]; previous = new float[spawns.Length][];
        try
        {
            world = DangoPhysicsWorld.Create(library, capacity: spawns.Length, configuration: configuration);
            foreach (var (x, y) in spawns) ids.Add(world.Add(x, y));
            rest = NativeRestReader.TryCreate(library, world);
        }
        catch (Exception e) { error = e.Message; }
        InvalidateVisual();
    }

    internal void Step(double horizontal, bool down, bool jump)
    {
        if (world is null || error is not null) return;
        try
        {
            var inputs = ids.Select((id, i) => i != focus ? new DangoPhysicsInput(id) : new DangoPhysicsInput(id,
                Horizontal: horizontal, Downward: down ? 1 : 0, JumpPressed: jump,
                DragActive: dragging, DragTargetX: dragTarget.X, DragTargetY: dragTarget.Y)).ToArray();
            var frame = world.Step(MaterialSession.FixedStep, inputs);
            if (!frame.Succeeded) { error = $"原生错误 {frame.ErrorCode}（按 R 重新开始）"; return; }
            bodies = [.. ids.Select(id => frame.Bodies.Single(b => b.EntityId == id))];
            Frames++;
            if (rest is not null) resting = rest.Read(ids);
            else
                // Fallback for a module without the rest export: a resting body publishes identical frames.
                for (var i = 0; i < bodies.Count; i++)
                {
                    float[] bits = [.. bodies[i].Contour, bodies[i].CoreX, bodies[i].CoreY];
                    resting[i] = previous[i] is { } p && bits.SequenceEqual(p);
                    previous[i] = bits;
                }
            if (!dragging) cameraX += (bodies.Average(b => b.CoreX) - cameraX) * .05;
        }
        catch (Exception e) { error = e.Message; pressed = dragging = false; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var tick = new Pen(new SolidColorBrush(Color.FromRgb(220, 210, 196)), 1);
        drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(190, 172, 150)), 2), new(0, GroundY), new(ActualWidth, GroundY));
        var first = Math.Floor((cameraX - ActualWidth / 2 / Scale) * 2) / 2;
        for (var x = first; x <= cameraX + ActualWidth / 2 / Scale; x += .5)
        {
            var sx = ActualWidth / 2 + (x - cameraX) * Scale;
            drawing.DrawLine(tick, new(sx, GroundY), new(sx, GroundY + 8));
        }
        if (error is not null)
            drawing.DrawText(Text(error, 13, Brushes.Firebrick), new(12, 10));
        for (var i = 0; i < bodies.Count; i++)
        {
            var s = bodies[i];
            Geometry geometry;
            try { geometry = SampleGeometry.Create(s, false, Scale); }
            catch (Exception e) { error ??= $"轮廓绘制停止：{e.Message}"; return; }
            var center = ToScreen(s.CoreX, s.CoreY);
            drawing.PushTransform(new TranslateTransform(center.X, center.Y));
            drawing.DrawGeometry(Fill, i == focus ? FocusPen : null, geometry);
            var b = geometry.Bounds;
            foreach (var side in new[] { -1, 1 })
                drawing.DrawEllipse(Ink, null, new((b.Left + b.Right) / 2 + side * b.Width * .17, b.Top + b.Height * .5), 3.2, 3.7);
            var label = Text($"{i + 1} {(resting[i] ? "睡" : "醒")}{(i == focus ? " · 焦点" : "")}", 13,
                i == focus ? FocusPen.Brush : Muted, i == focus ? FontWeights.SemiBold : FontWeights.Normal);
            drawing.DrawText(label, new((b.Left + b.Right) / 2 - label.Width / 2, b.Top - label.Height - 4));
            drawing.Pop();
        }
    }

    private FormattedText Text(string text, double size, Brush brush, FontWeight? weight = null) =>
        new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    public void Dispose() { world?.Dispose(); world = null; rest = null; }
}
