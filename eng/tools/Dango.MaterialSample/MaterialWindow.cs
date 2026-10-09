using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Core.Animation;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

internal sealed class MaterialWindow : Window, IDisposable
{
    private readonly string library;
    private readonly DockPanel surface = new() { Background = Brushes.White };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(20, 10, 20, 16) };
    private readonly Slider size = MakeSlider(80, 120, 100);
    private readonly Slider softness = MakeSlider(0, 100, 50);
    private readonly Slider settling = MakeSlider(0, 100, 90);
    private readonly SceneView[] scenes = [new(productionMapping: true), new(productionMapping: false)];
    private readonly TextBlock[] metrics = [new(), new()];
    private readonly DispatcherTimer timer = new() { Interval = MaterialSession.FixedStep };
    private readonly ToggleButton squeeze = new() { Content = "持续下压", Margin = new(4), Padding = new(12, 6, 12, 6) };
    private Button? pauseButton;
    private MaterialSession[] sessions = [];
    private bool paused, faulted, jump, pending;
    private double spawnHeight;
    private bool disposed;

    internal MaterialWindow(string library)
    {
        this.library = library;
        Title = "Dango · 圆形自然态与材料小样";
        Width = 1120; Height = 760; MinWidth = 960; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        Content = surface;
        var heading = new TextBlock
        {
            Text = "圆形自然态 · 落地形态由材料产生",
            FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(20, 18, 20, 8),
        };
        AddTop(heading);
        AddTop(new TextBlock
        {
            Text = "左侧保留原版的身体显示比例，右侧落地形状由材料产生。拖动后松开可观察回圆；修改参数后按“应用参数并下落”。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new(20, 0, 20, 12),
        });
        var sliders = new Grid { Margin = new(20, 0, 20, 10) };
        for (var i = 0; i < 3; i++) sliders.ColumnDefinitions.Add(new());
        AddControl(sliders, 0, "大小", size, "%");
        AddControl(sliders, 1, "柔软度", softness, "");
        AddControl(sliders, 2, "晃动抑制", settling, "");
        AddTop(sliders);
        var toolbar = new WrapPanel { Margin = new(16, 0, 16, 8) };
        Button AddButton(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new(4), Padding = new(12, 6, 12, 6) };
            button.Click += (_, _) => action(); toolbar.Children.Add(button); return button;
        }
        AddButton("应用参数并下落", () => Reset(0));
        AddButton("高处自由落体", () => Reset(8));
        AddButton("同时跳跃", () => jump = true);
        toolbar.Children.Add(squeeze);
        pauseButton = AddButton("暂停", () =>
        {
            paused = !paused; ClearInput(); pauseButton!.Content = paused ? "继续" : "暂停";
            foreach (var scene in scenes) scene.AllowDrag = !paused && !faulted;
        });
        AddButton("恢复默认并下落", () =>
        {
            size.Value = 100; softness.Value = 50; settling.Value = 90; Reset(0);
        });
        var skeleton = new CheckBox { Content = "显示骨架", IsChecked = false, Margin = new(12, 8, 4, 4) };
        skeleton.Click += (_, _) =>
        {
            foreach (var scene in scenes) { scene.Skeleton = skeleton.IsChecked == true; scene.InvalidateVisual(); }
        };
        toolbar.Children.Add(skeleton); AddTop(toolbar);
        DockPanel.SetDock(status, Dock.Bottom); surface.Children.Add(status);
        var panels = new Grid { Margin = new(16, 0, 16, 0) };
        panels.ColumnDefinitions.Add(new()); panels.ColumnDefinitions.Add(new());
        for (var i = 0; i < 2; i++)
        {
            var panel = new DockPanel { Margin = new(4), Background = new SolidColorBrush(Color.FromRgb(248, 248, 246)) };
            var label = new TextBlock
            {
                Text = i == 0 ? "原版对照 · 原物理与原身体比例" : "材料候选 · 默认落地对齐参考图",
                FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(14, 12, 14, 6),
            };
            DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            metrics[i].Margin = new(14, 8, 14, 12); metrics[i].TextWrapping = TextWrapping.Wrap;
            metrics[i].FontFamily = new("Microsoft YaHei UI");
            DockPanel.SetDock(metrics[i], Dock.Bottom); panel.Children.Add(metrics[i]);
            panel.Children.Add(scenes[i]); Grid.SetColumn(panel, i); panels.Children.Add(panel);
            scenes[i].RenderFailed += Fail;
        }
        surface.Children.Add(panels);
        Loaded += (_, _) => { Reset(0); timer.Start(); };
        Deactivated += (_, _) => ClearInput();
        Closed += (_, _) => Dispose();
        timer.Tick += (_, _) => { if (!paused && !faulted) Tick(); };
    }

    private static Slider MakeSlider(double minimum, double maximum, double value) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value,
        TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new(0, 8, 14, 0),
    };

    private void AddControl(Grid grid, int column, string title, Slider slider, string suffix)
    {
        var stack = new StackPanel();
        var label = new TextBlock { Text = $"{title}  {slider.Value:F0}{suffix}" };
        slider.ValueChanged += (_, _) =>
        {
            label.Text = $"{title}  {slider.Value:F0}{suffix}";
            pending = true;
            status.Text = "参数尚未应用。按“应用参数并下落”查看实际变化。";
        };
        stack.Children.Add(label); stack.Children.Add(slider);
        Grid.SetColumn(stack, column); grid.Children.Add(stack);
    }

    private void AddTop(UIElement element) { DockPanel.SetDock(element, Dock.Top); surface.Children.Add(element); }

    private void Reset(double height)
    {
        ClearInput();
        foreach (var session in sessions) session.Dispose();
        sessions = [];
        try
        {
            var candidate = new MaterialControls(size.Value, softness.Value, settling.Value).Configure();
            var original = new MaterialSession(library, DangoPhysicsDefaults.Current, height);
            try { sessions = [original, new MaterialSession(library, candidate, height)]; }
            catch { original.Dispose(); throw; }
            spawnHeight = height; paused = faulted = pending = false;
            if (pauseButton is not null) pauseButton.Content = "暂停";
            for (var i = 0; i < scenes.Length; i++)
            {
                scenes[i].Session = sessions[i]; scenes[i].AllowDrag = true;
            }
            Tick();
        }
        catch (Exception error) { Fail(error.Message); }
    }

    private void Tick()
    {
        var currentScene = -1;
        try
        {
            for (var i = 0; i < sessions.Length; i++)
            {
                currentScene = i;
                var view = scenes[i];
                var state = sessions[i].Step(jump, squeeze.IsChecked == true,
                    view.Dragging, view.DragTarget.X, view.DragTarget.Y);
                var shape = ShapeMetrics.Measure(state);
                var bounds = SampleGeometry.Create(state, i == 0, bodySize: sessions[i].BodySize).Bounds;
                var renderedAspect = bounds.Width / bounds.Height;
                metrics[i].Text = $"{(state.IsGrounded ? "已接地" : "离地")}  ·  身体显示宽高比 {renderedAspect:F3}\n" +
                    $"骨架径向差 {shape.RadialSpread:P1}  ·  {(Math.Abs(renderedAspect - 1) <= .04 && shape.IsRound ? "显示接近圆形" : "形变中")}  ·  帧 {sessions[i].Frames}";
                view.InvalidateVisual();
            }
            jump = false;
            status.Foreground = Brushes.DimGray;
            status.Text = pending ? "参数尚未应用；正在显示上次应用的物理。按“应用参数并下落”查看实际变化。" : spawnHeight > 0
                ? "高处自由落体：镜头跟随身体；松手后不再施加拖动力。两侧是独立单体对照世界。"
                : "默认落地目标宽高比约 1.498；软硬与大小会改变落地形态。眼睛和芽为基础装饰，身体来自真实模拟。";
        }
        catch (Exception error)
        {
            Fail($"{(currentScene == 0 ? "左侧原版" : "右侧候选")}：{error.Message}");
            foreach (var view in scenes) view.InvalidateVisual();
        }
    }

    private void Fail(string message)
    {
        faulted = true; ClearInput();
        foreach (var view in scenes) view.AllowDrag = false;
        status.Foreground = Brushes.Firebrick; status.Text = message;
    }

    private void ClearInput()
    {
        jump = false; squeeze.IsChecked = false;
        foreach (var scene in scenes) scene.StopDrag();
    }

    internal void SaveSnapshot(string output)
    {
        Reset(0);
        for (var i = 0; i < 600 && !faulted; i++) Tick();
        if (faulted) throw new InvalidOperationException(status.Text);
        surface.Measure(new Size(1080, 690)); surface.Arrange(new Rect(0, 0, 1080, 690)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1080, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        if (faulted) throw new InvalidOperationException(status.Text);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = new FileStream(output, FileMode.Create); png.Save(file);
    }

    public void Dispose()
    {
        if (disposed) return;
        timer.Stop(); ClearInput();
        foreach (var session in sessions) session.Dispose();
        sessions = []; disposed = true;
    }
}

internal sealed class SceneView : FrameworkElement
{
    private const double Scale = 105;
    private readonly bool productionMapping;
    private double ScaleY => productionMapping ? Scale * 58 / 72 : Scale;
    internal MaterialSession? Session { get; set; }
    internal bool Skeleton { get; set; }
    internal bool AllowDrag { get; set; }
    internal event Action<string>? RenderFailed;
    internal bool Dragging { get; private set; }
    internal Point DragTarget { get; private set; }
    private Geometry? hitGeometry;
    private Vector hitOffset;
    private readonly SceneCoordinates coordinates = new();

    private double OriginY => coordinates.CameraOrigin(ActualHeight, ScaleY,
        Session?.Configuration.GroundHeight ?? -.72, Session?.State?.CoreY ?? 0);

    internal SceneView(bool productionMapping)
    {
        this.productionMapping = productionMapping;
        ClipToBounds = true; Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (!AllowDrag || hitGeometry is null || !hitGeometry.FillContains(p - hitOffset)) return;
            Dragging = CaptureMouse();
            if (Dragging) coordinates.BeginDrag(ActualHeight, ScaleY, Session?.Configuration.GroundHeight ?? -.72, Session?.State?.CoreY ?? 0);
            UpdateDrag(p); e.Handled = true;
        };
        MouseMove += (_, e) => { if (Dragging) UpdateDrag(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => StopDrag();
        LostMouseCapture += (_, _) => { Dragging = false; coordinates.EndDrag(); InvalidateVisual(); };
    }

    private void UpdateDrag(Point point) => DragTarget = coordinates.WorldTarget(point, ActualWidth, ActualHeight,
        Scale, ScaleY, Session?.Configuration.GroundHeight ?? -.72, Session?.State?.CoreY ?? 0);

    internal void StopDrag() { Dragging = false; coordinates.EndDrag(); if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Session?.State is not { } state) return;
        var groundY = OriginY - Session.Configuration.GroundHeight * ScaleY;
        drawing.DrawLine(new Pen(Brushes.LightGray, 2), new(12, groundY), new(ActualWidth - 12, groundY));
        var center = new Point(ActualWidth / 2 + state.CoreX * Scale, OriginY - state.CoreY * ScaleY);
        Geometry geometry;
        try { geometry = SampleGeometry.Create(state, productionMapping, productionMapping ? Scale / 72 : Scale, Session.BodySize); }
        catch (Exception error)
        {
            RenderFailed?.Invoke($"真实轮廓绘制停止：{error.Message}。请显式重新下落。");
            return;
        }
        hitOffset = new(center.X, center.Y);
        hitGeometry = geometry;
        drawing.PushTransform(new TranslateTransform(hitOffset.X, hitOffset.Y));
        drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(245, 213, 165)), null, geometry);
        var bounds = geometry.Bounds;
        var bodySize = Session.Configuration.RadialRestLength / DangoPhysicsDefaults.Current.RadialRestLength;
        var decorationZoom = Scale / 72 * bodySize;
        var eyeY = bounds.Top + bounds.Height * .5158;
        var eyeCenter = (bounds.Left + bounds.Right) / 2;
        var eyeBrush = new SolidColorBrush(Color.FromRgb(43, 32, 30));
        foreach (var side in new[] { -1, 1 })
        {
            var eye = AttachmentGeometryFactory.CreateEye(new(new(0, 0), .075, .075, 0, 1, EyeShape.Round, 0, true));
            drawing.PushTransform(new TranslateTransform(eyeCenter + side * 19 * decorationZoom, eyeY));
            drawing.PushTransform(new ScaleTransform(decorationZoom, decorationZoom));
            drawing.PushTransform(new TranslateTransform(-80, -66));
            drawing.DrawGeometry(eyeBrush, null, eye);
            drawing.Pop(); drawing.Pop(); drawing.Pop();
        }
        drawing.PushTransform(new TranslateTransform(eyeCenter, bounds.Top + bounds.Height * .09));
        drawing.PushTransform(new ScaleTransform(decorationZoom * 1.35, decorationZoom * 1.35));
        drawing.PushTransform(new TranslateTransform(-80, -66));
        drawing.DrawDrawing(AttachmentGeometryFactory.CreateStaticHat(new HatVisualPose(new(0, 0), 0, 0)));
        drawing.Pop(); drawing.Pop(); drawing.Pop(); drawing.Pop();
        if (!Skeleton) return;
        var bone = new Pen(new SolidColorBrush(Color.FromArgb(110, 120, 115, 100)), 1);
        var node = new SolidColorBrush(Color.FromArgb(150, 100, 140, 120));
        for (var i = 0; i < state.PointCount; i++)
        {
            var p = new Point(center.X + state.Contour[2 * i] * Scale, center.Y - state.Contour[2 * i + 1] * ScaleY);
            drawing.DrawLine(bone, center, p); drawing.DrawEllipse(node, null, p, 2.4, 2.4);
        }
        drawing.DrawEllipse(Brushes.Gray, null, center, 3, 3);
    }
}
