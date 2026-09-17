using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.JointReview;

internal sealed class AttachmentCalibrationWindow : Window
{
    private sealed record Fixture(string Name, PresentationPoint[] Contour);
    private readonly List<Fixture> fixtures = [];
    private readonly ComboBox selection = new() { MinWidth = 240, Margin = new(8) };
    private readonly Slider slide = new() { Minimum = -1, Maximum = 1, Width = 220, Margin = new(8), TickFrequency = 0.25, IsSnapToTickEnabled = true };
    private readonly CheckBox guides = new() { Content = "显示参考轨道与帽根", IsChecked = false, Margin = new(8) };
    private readonly TextBlock metrics = new() { Margin = new(18), TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly Grid comparison = new() { Margin = new(12) };
    public DockPanel Surface { get; } = new() { Background = Brushes.WhiteSmoke };

    public AttachmentCalibrationWindow(string fixtureDirectory)
    {
        Title = "Dango 眼帽 EH-02R｜保留旧版埋入感（待人工确认）";
        Width = 1080; Height = 660; MinWidth = 900; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = Surface;
        AddTop(new TextBlock { Text = "帽根位置 · 保留旧版埋入感", FontSize = 24, Margin = new(18, 16, 18, 4) });
        AddTop(new TextBlock { Text = "中立位置沿用左侧旧版的帽根位置与埋入感，不再追求帽底严格贴在表面。\n拖动滑块查看沿头顶的轻微滑动；身体、眼睛和叶芽素材不变，尚未启用动态回弹。", FontSize = 14, Margin = new(18, 4, 18, 8) });
        var controls = new WrapPanel();
        controls.Children.Add(selection);
        controls.Children.Add(new TextBlock { Text = "轨道位置", VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(slide); controls.Children.Add(guides);
        var neutral = new Button { Content = "回到中立", Margin = new(8), Padding = new(12, 5, 12, 5) };
        neutral.Click += (_, _) => { slide.Value = 0; Refresh(); };
        controls.Children.Add(neutral); AddTop(controls);
        DockPanel.SetDock(metrics, Dock.Bottom); Surface.Children.Add(metrics);
        comparison.ColumnDefinitions.Add(new()); comparison.ColumnDefinitions.Add(new()); Surface.Children.Add(comparison);
        fixtures.Add(new("圆冠（虚构形状）", Oval()));
        fixtures.Add(new("平顶（虚构形状）", Oval().Select(p => new PresentationPoint(p.X, Math.Min(0.5, p.Y))).ToArray()));
        fixtures.Add(new("左偏 / 右偏镜像 A", Oval(shear: -0.16)));
        fixtures.Add(new("左偏 / 右偏镜像 B", Oval(shear: 0.16)));
        fixtures.Add(new("对称压扁（虚构形状）", Oval(height: 0.45)));
        foreach (var file in Directory.EnumerateFiles(fixtureDirectory, "body-*.json").Order())
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var contour = json.RootElement.GetProperty("Contour").EnumerateArray().Select(p => new PresentationPoint(p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble())).ToArray();
            fixtures.Add(new("模块静态快照 " + Path.GetFileNameWithoutExtension(file), contour));
        }
        selection.ItemsSource = fixtures.Select(f => f.Name).ToArray();
        selection.SelectionChanged += (_, _) => Refresh(); slide.ValueChanged += (_, _) => Refresh();
        guides.Checked += (_, _) => Refresh(); guides.Unchecked += (_, _) => Refresh();
        selection.SelectedIndex = 0;
    }

    private void AddTop(UIElement element) { DockPanel.SetDock(element, Dock.Top); Surface.Children.Add(element); }

    private void Refresh()
    {
        if (selection.SelectedIndex < 0) return;
        comparison.Children.Clear();
        var contour = fixtures[selection.SelectedIndex].Contour;
        var leftEye = new PresentationAttachment(new(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05), 0);
        var rightEye = new PresentationAttachment(new(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05), 0);
        var top = contour.MaxBy(p => p.Y);
        var crown = contour.OrderByDescending(p => p.Y).Take(3).ToArray();
        var l = crown.MinBy(p => p.X); var r = crown.MaxBy(p => p.X);
        var frame = new PhysicsPresentationFrame(true, contour, new(new(0, -0.05), 0),
            new(new(top.X * 0.08, top.Y), Math.Atan2(r.Y - l.Y, r.X - l.X)),
            PhysicsPresentationFallbackReason.None, new(PhysicsPresentationBackend.Box2D, 0, PhysicsPresentationDiagnosticCode.SoftBodyActive), leftEye, rightEye);
        var shape = AttachmentShapeGeometry.Create(contour);
        var placement = HatPlacementSolver.Create(shape, frame.Hat);
        var s = placement is null ? 0 : slide.Value < 0 ? -slide.Value * placement.Minimum : slide.Value * placement.Maximum;
        var pose = placement?.Fit(Math.Clamp(s, placement.Minimum, placement.Maximum));
        for (var column = 0; column < 2; column++)
        {
            var panel = new DockPanel { Margin = new(8), Background = Brushes.White };
            var title = new TextBlock { Text = column == 0 ? "旧映射（基准）" : "保留旧版埋入感（修订候选）", FontSize = 18, Margin = new(16) };
            DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
            var canvas = new Canvas { Width = 160, Height = 140, ClipToBounds = false };
            var body = new DeformableDangoVisual { ShowHat = column == 0 };
            body.ApplyFrame(frame); canvas.Children.Add(body);
            if (column == 1 && pose is not null)
            {
                var drawing = AttachmentGeometryFactory.CreateStaticHat(pose);
                canvas.Children.Add(new DrawingSurface(drawing) { Width = 160, Height = 140, IsHitTestVisible = false });
                if (guides.IsChecked == true)
                {
                    var guideDrawing = new DrawingGroup();
                    var path = new StreamGeometry();
                    using (var context = path.Open())
                    {
                        var pts = placement!.Guide.Select(p => new Point(80 + p.X * 160, 66 - p.Y * 160)).ToArray();
                        context.BeginFigure(pts[0], false, false); context.PolyLineTo(pts.Skip(1).ToArray(), true, false);
                    }
                    guideDrawing.Children.Add(new GeometryDrawing(null, new Pen(Brushes.SteelBlue, 0.45), path));
                    var transform = drawing.Transform;
                    guideDrawing.Children.Add(new GeometryDrawing(null, new Pen(Brushes.Crimson, 0.7), new LineGeometry(transform.Transform(new(77, 25)), transform.Transform(new(83, 25)))));
                    canvas.Children.Add(new DrawingSurface(guideDrawing) { Width = 160, Height = 140, IsHitTestVisible = false });
                }
            }
            panel.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new(24) });
            Grid.SetColumn(panel, column); comparison.Children.Add(panel);
        }
        var tilt = shape.EstimateTilt();
        var neutralPose = placement?.Fit(0);
        metrics.Text = pose is null || neutralPose is null ? "无法建立安全的几何参考：候选帽子隐藏。该形状标定失败，不能算作通过。" :
            $"中立状态与旧版帽根位置、角度一致；几何轨道只提供滑动和倾斜变化。\n当前相对中立：横移 {(pose.Center.X - neutralPose.Center.X) * 160:F3} DIP，竖移 {-(pose.Center.Y - neutralPose.Center.Y) * 160:F3} DIP，角度变化 {(pose.Angle - neutralPose.Angle) * 180 / Math.PI:F2}°。\n形状倾斜 {tilt.Angle * 180 / Math.PI:F2}°（仅诊断，尚不驱动眼睛）。请观察埋入感与滑动后的连接是否自然。";
    }

    internal void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        for (var i = 0; i < fixtures.Count; i++)
        {
            selection.SelectedIndex = i; Refresh();
            Surface.Measure(new(1080, 620)); Surface.Arrange(new Rect(0, 0, 1080, 620)); Surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1080, 620, 96, 96, PixelFormats.Pbgra32); bitmap.Render(Surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(Path.Combine(directory, $"calibration-{i:D2}.png"), FileMode.CreateNew);
            encoder.Save(stream);
        }
    }

    private static PresentationPoint[] Oval(double height = 0.8, double shear = 0) => Enumerable.Range(0, 16)
        .Select(i => { var a = i * Math.PI / 8; var y = height * Math.Sin(a); return new PresentationPoint(0.7 * Math.Cos(a) + shear * y, y); }).ToArray();

    private sealed class DrawingSurface(Drawing drawing) : FrameworkElement
    {
        protected override void OnRender(DrawingContext context) { base.OnRender(context); context.DrawDrawing(drawing); }
    }
}
