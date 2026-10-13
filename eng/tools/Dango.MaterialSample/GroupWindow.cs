using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Review-only window: two or three bodies in one shared multi-body world, each with its own
// colour and shape (size, softness, build). Shapes apply on restart, not live. Click a body to
// focus it; only the focused body takes keyboard input. Rest state is read from the native module.
internal sealed class GroupWindow : Window
{
    private const LandingBuildTable Table = LandingBuildTable.SizeDampingPreview;
    private static readonly string[] Layouts = ["双团子并排", "三团子叠放", "三团子并排"];

    internal static readonly Color[] Colors =
    [
        // Product slot colours (DangoHostFrameBinding); body 1 shows the saved appearance in formal mode.
        Color.FromRgb(255, 232, 194),
        Color.FromRgb(201, 226, 255),
        Color.FromRgb(221, 204, 248),
    ];

    private readonly string library;
    private readonly DispatcherTimer timer = new() { Interval = MaterialSession.FixedStep };
    private readonly GroupView view = new();
    private readonly FormalDangoLayer formal = new();
    private readonly CheckBox formalBox = new() { Content = "正式外观（眼睛、帽子、图片配饰）", IsChecked = true, Focusable = false, Margin = new(0, 0, 0, 6) };
    private readonly ComboBox hatBox = new() { Focusable = false, Margin = new(0, 0, 0, 6), ItemsSource = new[] { "叶芽", "小礼帽", "圆顶帽" } };
    private readonly CheckBox imageHatBox = new() { Content = "1 号的帽子作为图片配饰（随身体变形）", Focusable = false, Margin = new(0, 0, 0, 6) };
    private readonly TextBlock settingsText = new() { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 0, 0, 10) };
    private static readonly string[] HatIds = ["sprout", "top-hat", "bowler"];
    private readonly TextBlock status = new() { Margin = new(16, 6, 16, 10), Foreground = Brushes.DimGray };
    private readonly CheckBox skeleton = new() { Content = "显示骨架（节点、辐条、周边）", IsChecked = true, Focusable = false, Margin = new(0, 0, 0, 6) };
    private readonly CheckBox outline = new() { Content = "显示碰撞轮廓（原生接触用）", IsChecked = true, Focusable = false, Margin = new(0, 0, 0, 6) };
    private readonly ShapeEditor[] editors = [new(0, 100, 50, 50), new(1, 100, 50, 50), new(2, 100, 50, 50)];
    private int layout;
    private bool left, right, down, jump;

    internal GroupWindow(string library)
    {
        this.library = library;
        Title = "Dango · 多团子互动";
        Width = 1440; Height = 820; MinWidth = 1000; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(252, 250, 246));
        var root = new DockPanel();
        var heading = new TextBlock { Text = "多团子互动 · 同一物理世界 · 每只独立体型", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(16, 12, 16, 2) };
        var help = new TextBlock
        {
            Text = "点击团子切换焦点 · 焦点团子：A/D 左右 · W/空格 跳 · S 下压 · 按住拖动 · 1/2/3 切换摆放 · R 重新开始 · K 骨架 · C 碰撞轮廓 · 体型在右侧调整后点“应用”",
            Foreground = Brushes.DimGray, Margin = new(16, 0, 16, 6),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 0, 12, 6) };
        for (var i = 0; i < Layouts.Length; i++)
        {
            var index = i;
            var button = new Button { Content = $"{i + 1} {Layouts[i]}", Margin = new(4, 0, 4, 0), Padding = new(10, 3, 10, 3), Focusable = false };
            button.Click += (_, _) => Reset(index);
            buttons.Children.Add(button);
        }
        var side = new StackPanel { Width = 260, Margin = new(0, 0, 12, 0) };
        foreach (var editor in editors) side.Children.Add(editor.Panel);
        skeleton.Checked += (_, _) => view.ShowSkeleton = true;
        skeleton.Unchecked += (_, _) => view.ShowSkeleton = false;
        side.Children.Add(skeleton);
        outline.Checked += (_, _) => view.ShowOutline = true;
        outline.Unchecked += (_, _) => view.ShowOutline = false;
        side.Children.Add(outline);
        formal.LoadProductSettings();
        view.Formal = formal;
        formalBox.Checked += (_, _) => { view.ShowFormal = true; formal.Visibility = Visibility.Visible; };
        formalBox.Unchecked += (_, _) => { view.ShowFormal = false; formal.Visibility = Visibility.Collapsed; };
        hatBox.SelectedIndex = Array.IndexOf(HatIds, formal.HatPresetId);
        hatBox.SelectionChanged += (_, _) => { formal.HatPresetId = HatIds[hatBox.SelectedIndex]; formal.ApplyAccessoryMode(null); };
        imageHatBox.IsChecked = formal.ImageAccessories;
        imageHatBox.IsEnabled = !formal.SavedAccessoriesEnabled;
        imageHatBox.Checked += (_, _) => { formal.ImageAccessories = true; formal.ApplyAccessoryMode(null); };
        imageHatBox.Unchecked += (_, _) => { formal.ImageAccessories = false; formal.ApplyAccessoryMode(null); };
        settingsText.Text = formal.SettingsSource;
        side.Children.Add(formalBox);
        side.Children.Add(new TextBlock { Text = "帽型", Foreground = Brushes.DimGray, FontSize = 12 });
        side.Children.Add(hatBox);
        side.Children.Add(imageHatBox);
        side.Children.Add(settingsText);
        var apply = new Button { Content = "应用体型并重新开始", Margin = new(0, 8, 0, 0), Padding = new(10, 5, 10, 5), Focusable = false };
        apply.Click += (_, _) => Reset(layout);
        side.Children.Add(apply);
        side.Children.Add(new TextBlock
        {
            Text = "体型参数只在重新开始时生效。焦点团子显示棕色描边。",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), FontSize = 12,
        });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        DockPanel.SetDock(buttons, Dock.Top); root.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var scroller = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        DockPanel.SetDock(scroller, Dock.Right); root.Children.Add(scroller);
        root.Children.Add(new Border
        {
            Child = new Grid { Children = { formal, view } }, Margin = new(12, 0, 12, 0), CornerRadius = new(6), Background = Brushes.White,
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
            case Key.K: if (pressed && !repeat) skeleton.IsChecked = !skeleton.IsChecked; break;
            case Key.C: if (pressed && !repeat) outline.IsChecked = !outline.IsChecked; break;
        }
    }

    private void Reset(int index)
    {
        layout = index;
        var count = index == 0 ? 2 : 3;
        for (var i = 0; i < editors.Length; i++) editors[i].Panel.IsEnabled = i < count;
        var shapes = editors.Take(count).Select(e => new LandingBuildControls(e.Size, e.Softness, e.Build).Configure(Table)).ToArray();
        view.Reset(library, shapes, Spawns(index, shapes));
        Title = $"Dango · 多团子互动 · {Layouts[index]}";
    }

    // Spawn points spaced by each body's own extent so no two bodies start overlapping.
    private static (double X, double Y)[] Spawns(int index, DangoSelfPhysicsParameters[] shapes)
    {
        var r = shapes.Select(s => s.RadialRestLength + s.NodeRadius).ToArray();
        const double gap = .12;
        return index switch
        {
            0 => [(-(r[0] + gap / 2), 0), (r[1] + gap / 2, 0)],
            1 => [(-(r[0] + gap / 2), 0), (r[1] + gap / 2, 0), (0, 2 * Math.Max(r[0], r[1]) + r[2] - .2)],
            _ => [(-(r[0] + r[1] + gap), 0), (0, 0), (r[1] + r[2] + gap, 0)],
        };
    }

    // Review-only offscreen capture: scripted input, then the window content rendered to a PNG.
    internal static int Snapshot(string library, string path, int layout, int frames, string mode)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new GroupWindow(library) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
        window.snapshot = (path, frames);
        window.Loaded += (_, _) =>
        {
            window.imageHatBox.IsChecked = mode.StartsWith("image", StringComparison.Ordinal);
            if (mode.EndsWith("mixed", StringComparison.Ordinal))
            { window.editors[0].Set(80, 0, 0); window.editors[1].Set(120, 100, 100); window.editors[2].Set(100, 50, 50); }
            window.Reset(layout);
        };
        window.Show();
        return app.Run();
    }

    private (string Path, int Frames)? snapshot;

    private void Tick()
    {
        if (snapshot is { } shot)
        {
            // Walk the focused body right for 0.5 s, then let it settle.
            right = view.Frames is >= 120 and < 150;
            if (view.Frames >= shot.Frames)
            {
                timer.Stop();
                var root = (FrameworkElement)Content;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var dc = background.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bitmap.Render(background); bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = System.IO.File.Create(shot.Path)) encoder.Save(file);
                Console.WriteLine(status.Text + " · 轮廓校验 " + formal.ContourFailures);
                Console.WriteLine(formal.Debug(view.Bodies, 100));
                Close(); Application.Current.Shutdown();
                return;
            }
        }
        view.Step((right ? 1 : 0) - (left ? 1 : 0), down, jump);
        jump = false;
        status.Text = $"{Layouts[layout]} · 帧 {view.Frames} · {view.Summary}{view.Overlap}{(formal.Error is { } e ? " · 外观：" + e : "")} · 原生库：{System.IO.Path.GetFileName(library)}";
    }

    private sealed class ShapeEditor
    {
        private readonly Slider size, softness, build;
        internal StackPanel Panel { get; } = new() { Margin = new(0, 0, 0, 10) };
        internal double Size => size.Value;
        internal double Softness => softness.Value;
        internal double Build => build.Value;
        internal void Set(double s, double f, double b) { size.Value = s; softness.Value = f; build.Value = b; }

        internal ShapeEditor(int index, double size, double softness, double build)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 4) };
            header.Children.Add(new Border
            {
                Width = 14, Height = 14, CornerRadius = new(7), Background = new SolidColorBrush(Colors[index]),
                BorderBrush = new SolidColorBrush(Color.FromRgb(190, 172, 150)), BorderThickness = new(1), Margin = new(0, 0, 6, 0),
            });
            header.Children.Add(new TextBlock { Text = $"团子 {index + 1}", FontWeight = FontWeights.SemiBold });
            Panel.Children.Add(header);
            this.size = Row("大小", 80, 120, size);
            this.softness = Row("柔软度", 0, 100, softness);
            this.build = Row("胖瘦（0 胖 · 100 瘦）", 0, 100, build);
        }

        private Slider Row(string label, double minimum, double maximum, double value)
        {
            var text = new TextBlock { Foreground = Brushes.DimGray, FontSize = 12 };
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, IsSnapToTickEnabled = true, TickFrequency = 5, Focusable = false };
            void Show() => text.Text = $"{label}：{slider.Value:0}";
            slider.ValueChanged += (_, _) => Show();
            Show();
            Panel.Children.Add(text); Panel.Children.Add(slider);
            return slider;
        }
    }
}

internal sealed class GroupView : FrameworkElement, IDisposable
{
    private const double Scale = 100;
    private static readonly Pen FocusPen = new(new SolidColorBrush(Color.FromRgb(140, 96, 60)), 2.5);
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(43, 32, 30));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(150, 136, 120));
    private readonly DangoSelfPhysicsParameters world0 = DangoPhysicsDefaults.Current;
    private DangoPhysicsWorld? world;
    private NativeRestReader? rest;
    private readonly List<DangoEntityId> ids = [];
    private double[] bodySizes = [], nodeRadii = [];
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

    internal IReadOnlyList<DangoBodyState> Bodies => bodies;

    internal bool ShowSkeleton { get; set; } = true;

    internal FormalDangoLayer? Formal { get; set; }

    internal bool ShowFormal { get; set; } = true;

    internal bool ShowOutline { get; set; } = true;

    // Pairwise overlap: contact outline depth (and % of the smaller width), and node-circle depth.
    internal string Overlap
    {
        get
        {
            var parts = new List<string>();
            for (var a = 0; a < bodies.Count; a++)
                for (var b = a + 1; b < bodies.Count; b++)
                {
                    var (gap, width) = ContactOutline.Separation(ContactOutline.Build(bodies[a], bodySizes[a]), ContactOutline.Build(bodies[b], bodySizes[b]));
                    if (gap > .05) continue;
                    var skeleton = ContactOutline.SkeletonOverlap(bodies[a], nodeRadii[a], bodies[b], nodeRadii[b]);
                    parts.Add($"{a + 1}-{b + 1} 轮廓{(gap < 0 ? "重叠" : "间隙")} {Math.Abs(gap):F3}（{Math.Abs(gap) / width:P1}）骨架重叠 {Math.Max(0, skeleton):F3}");
                }
            return parts.Count == 0 ? "" : " · " + string.Join("  ", parts);
        }
    }

    internal string Summary => bodies.Count == 0 ? "" :
        string.Join("  ", Enumerable.Range(0, bodies.Count).Select(i => $"{i + 1}{(i == focus ? "（焦点）" : "")} {(resting[i] ? "睡" : "醒")}"));

    internal GroupView()
    {
        ClipToBounds = true; Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            var hit = HitBody(p);
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

    private Point ToWorld(Point p) => new((p.X - ActualWidth / 2) / Scale + cameraX, world0.GroundHeight + (GroundY - p.Y) / Scale);

    private Point ToScreen(double x, double y) => new(ActualWidth / 2 + (x - cameraX) * Scale, GroundY - (y - world0.GroundHeight) * Scale);

    private int HitBody(Point screen)
    {
        for (var i = 0; i < bodies.Count; i++)
        {
            var s = bodies[i];
            if (Body(s, bodySizes[i]).FillContains(screen)) return i;
        }
        return -1;
    }

    internal void Reset(string library, DangoSelfPhysicsParameters[] shapes, (double X, double Y)[] spawns)
    {
        Dispose();
        ids.Clear(); bodies = []; error = null; focus = 0; Frames = 0; cameraX = 0; pressed = dragging = false;
        resting = new bool[spawns.Length]; previous = new float[spawns.Length][];
        bodySizes = [.. shapes.Select(s => s.RadialRestLength / DangoPhysicsDefaults.Current.RadialRestLength)];
        nodeRadii = [.. shapes.Select(s => s.NodeRadius)];
        try
        {
            world = DangoPhysicsWorld.Create(library, capacity: spawns.Length, configuration: world0);
            for (var i = 0; i < spawns.Length; i++) ids.Add(world.Add(spawns[i].X, spawns[i].Y, shapes[i]));
            rest = NativeRestReader.TryCreate(library, world);
            Formal?.Reset(library, ids, bodySizes);
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
            Formal?.Apply(frame, ids, focus, horizontal, (x, y) => ToScreen(x, y), Scale);
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
            // The visible body is the native contact outline: per P2D-007C it uses the product
            // display mapping (72/80, 58/60), so visible overlap equals the physical 3%.
            var geometry = Body(s, bodySizes[i]);
            var b = geometry.Bounds;
            if (!ShowFormal)
            {
                var fill = GroupWindow.Colors[i];
                if (ShowSkeleton) fill.A = 150;
                drawing.DrawGeometry(new SolidColorBrush(fill), i == focus ? FocusPen : null, geometry);
                var eye = 3.2 * bodySizes[i];
                foreach (var side in new[] { -1, 1 })
                    drawing.DrawEllipse(Ink, null, new((b.Left + b.Right) / 2 + side * b.Width * .17, b.Top + b.Height * .5), eye, eye * 1.15);
            }
            // Leave room for the hat above a formally drawn body.
            if (ShowFormal) b.Offset(0, -34 * bodySizes[i]);
            var label = Text($"{i + 1} {(resting[i] ? "睡" : "醒")}{(i == focus ? " · 焦点" : "")}", 13,
                i == focus ? FocusPen.Brush : Muted, i == focus ? FontWeights.SemiBold : FontWeights.Normal);
            drawing.DrawText(label, new((b.Left + b.Right) / 2 - label.Width / 2, b.Top - label.Height - 4));
            if (ShowSkeleton) DrawSkeleton(drawing, s, nodeRadii[i]);
            if (ShowOutline) DrawOutline(drawing, ContactOutline.Build(s, bodySizes[i]));
        }
    }

    private static readonly Pen Spoke = new(new SolidColorBrush(Color.FromArgb(150, 120, 84, 52)), 1);
    private static readonly Pen Rim = new(new SolidColorBrush(Color.FromArgb(220, 120, 84, 52)), 1.4);
    private static readonly Brush Node = new SolidColorBrush(Color.FromArgb(70, 120, 84, 52));
    private static readonly Brush Core = new SolidColorBrush(Color.FromRgb(140, 96, 60));

    // Physical rig in world coordinates: 16 node bodies (drawn at their collision radius) around
    // the core, radial spokes core-node and perimeter links node-node. The silhouette above is
    // the presentation mapping.
    private void DrawSkeleton(DrawingContext drawing, DangoBodyState s, double nodeRadius)
    {
        var count = s.Contour.Count / 2;
        var core = ToScreen(s.CoreX, s.CoreY);
        Point P(int n) { var v = ContactOutline.Node(s, n, nodeRadius); return ToScreen(s.CoreX + v.X, s.CoreY + v.Y); }
        for (var n = 0; n < count; n++)
        {
            drawing.DrawLine(Spoke, core, P(n));
            drawing.DrawLine(Rim, P(n), P((n + 1) % count));
        }
        for (var n = 0; n < count; n++) drawing.DrawEllipse(Node, Rim, P(n), nodeRadius * Scale, nodeRadius * Scale);
        drawing.DrawEllipse(Core, null, core, 3.5, 3.5);
    }

    private Geometry Body(DangoBodyState s, double size)
    {
        var points = ContactOutline.Build(s, size);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(ToScreen(points[0].X, points[0].Y), true, true);
            g.PolyLineTo([.. points.Skip(1).Select(p => ToScreen(p.X, p.Y))], true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static readonly Pen OutlinePen = new(new SolidColorBrush(Color.FromRgb(60, 90, 160)), 1.4) { DashStyle = DashStyles.Dash };

    private void DrawOutline(DrawingContext drawing, Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(ToScreen(points[0].X, points[0].Y), false, true);
            g.PolyLineTo([.. points.Skip(1).Select(p => ToScreen(p.X, p.Y))], true, false);
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, OutlinePen, geometry);
    }

    private FormattedText Text(string text, double size, Brush brush, FontWeight? weight = null) =>
        new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    public void Dispose() { world?.Dispose(); world = null; rest = null; }
}
