using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.ShapeReview;

internal static class Program
{
    private static readonly int[] Frames = [900, 2085, 6000, 36000];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--dynamic")
            {
                var dynamicApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                dynamicApp.Run(new DynamicWindow(args[1]));
                return 0;
            }
            if (args.Length == 3 && args[0] == "--verify-dynamic")
            {
                DynamicVerification.Run(args[1], args[2]).GetAwaiter().GetResult();
                DynamicWindow.VerifyView(DynamicVerification.VerifiedFrame ?? throw new InvalidOperationException("Missing verified frame"), args[2]);
                return 0;
            }
            if (args.Length is not (1 or 3))
            {
                throw new ArgumentException("传入模拟快照日志；自动验证另加 --verify 输出目录。");
            }
            var snapshots = Load(args[0]);
            if (args.Length == 3)
            {
                if (args[1] != "--verify")
                {
                    throw new ArgumentException("未知验证参数。");
                }
                Verify(snapshots, args[2]);
                return 0;
            }
            var app = new Application();
            app.Run(new ReviewWindow(snapshots));
            return 0;
        }
        catch (Exception exception)
        {
            if (args.Length == 1)
            {
                MessageBox.Show(exception.Message, "外形评审无法打开", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                Console.Error.WriteLine(exception.Message);
            }
            return 1;
        }
    }

    private static Dictionary<(string Pass, int Frame), PresentationPoint[]> Load(string path)
    {
        var rows = new Dictionary<(string Pass, int Frame), Dictionary<int, PresentationPoint>>();
        foreach (var line in File.ReadLines(path).Where(line => line.StartsWith("shape_node ", StringComparison.Ordinal)))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .Select(field => field.Split('=')).ToDictionary(field => field[0], field => field[1]);
            var pass = fields["pass"];
            if (pass is not ("production" or "ground_sleep"))
            {
                continue;
            }
            var frame = int.Parse(fields["frame"], CultureInfo.InvariantCulture);
            var index = int.Parse(fields["index"], CultureInfo.InvariantCulture);
            var x = double.Parse(fields["x"], CultureInfo.InvariantCulture);
            var y = double.Parse(fields["y"], CultureInfo.InvariantCulture);
            var radius = Math.Sqrt(x * x + y * y);
            if (!Frames.Contains(frame) || index is < 0 or >= 16 || !double.IsFinite(radius) || radius <= 0)
            {
                throw new InvalidDataException("快照包含非法坐标、帧或节点编号。");
            }
            if (!rows.TryGetValue((pass, frame), out var points))
            {
                points = [];
                rows.Add((pass, frame), points);
            }
            if (!points.TryAdd(index, new PresentationPoint(x * (1 + .045 / radius), y * (1 + .045 / radius))))
            {
                throw new InvalidDataException("快照包含重复节点。");
            }
        }
        var result = new Dictionary<(string, int), PresentationPoint[]>();
        foreach (var pass in new[] { "production", "ground_sleep" })
        {
            foreach (var frame in Frames)
            {
                if (!rows.TryGetValue((pass, frame), out var points) || points.Count != 16)
                {
                    throw new InvalidDataException($"缺少完整快照：{pass}，第 {frame} 帧。");
                }
                var ordered = Enumerable.Range(0, 16).Select(index => points[index]).ToArray();
                var maximum = ordered.Max(point => Math.Max(Math.Abs(point.X), Math.Abs(point.Y)));
                var scale = maximum > .98 ? .98 / maximum : 1;
                result.Add((pass, frame), ordered.Select(point => new PresentationPoint(point.X * scale, point.Y * scale)).ToArray());
            }
        }
        return result;
    }

    private static StreamGeometry Geometry(PresentationPoint[] points)
    {
        var result = DangoStreamGeometryFactory.TryCreate(points, 160, 120);
        return result.Succeeded && result.Geometry is not null
            ? result.Geometry
            : throw new InvalidDataException($"真实渲染器拒绝轮廓：{result.Failure}");
    }

    private static void Verify(Dictionary<(string Pass, int Frame), PresentationPoint[]> snapshots, string output)
    {
        Directory.CreateDirectory(output);
        var window = new ReviewWindow(snapshots);
        var metrics = snapshots.Select(pair =>
        {
            var geometry = Geometry(pair.Value);
            var path = PathGeometry.CreateFromGeometry(geometry);
            if (path.Figures.Count != 1 || !path.Figures[0].IsClosed || geometry.Bounds.IsEmpty)
            {
                throw new InvalidDataException("不是单一闭合轮廓。");
            }
            return new { pair.Key.Pass, pair.Key.Frame, geometry.Bounds.Width, geometry.Bounds.Height, Area = geometry.GetArea() };
        }).ToArray();
        foreach (var frame in Frames)
        {
            foreach (var zoom in new[] { 1d, 3d, 6d })
            {
                foreach (var overlay in new[] { false, true })
                {
                    window.Select(frame, zoom, overlay);
                    window.Surface.Measure(new Size(1060, 670));
                    window.Surface.Arrange(new Rect(0, 0, 1060, 670));
                    window.Surface.UpdateLayout();
                }
            }
        }
        window.Select(36000, 3, false);
        window.Surface.Measure(new Size(1060, 670));
        window.Surface.Arrange(new Rect(0, 0, 1060, 670));
        window.Surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1060, 670, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window.Surface);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "shape-review-render.png")))
        {
            png.Save(stream);
        }
        File.WriteAllText(Path.Combine(output, "shape-review-verification.json"),
            JsonSerializer.Serialize(new { StaticSnapshots = 8, LayoutStates = 24, Renderer = typeof(DangoStreamGeometryFactory).Assembly.GetName().Name, Metrics = metrics },
                new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }

    private sealed class ReviewWindow : Window
    {
        private readonly Dictionary<(string Pass, int Frame), PresentationPoint[]> snapshots;
        private readonly ComboBox frameChoice = new() { Width = 170, Margin = new Thickness(8) };
        private readonly ComboBox zoomChoice = new() { Width = 90, Margin = new Thickness(8) };
        private readonly CheckBox overlayChoice = new() { Content = "重叠查看（虚线为候选）", Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
        private readonly Grid drawings = new();
        private readonly TextBlock status = new() { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };
        public Grid Surface { get; } = new() { Background = Brushes.WhiteSmoke };

        public ReviewWindow(Dictionary<(string Pass, int Frame), PresentationPoint[]> snapshots)
        {
            this.snapshots = snapshots;
            Title = "Dango 外形评审｜隔离快照｜非整体验收";
            Width = 1100;
            Height = 740;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = Surface;
            Surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Surface.RowDefinitions.Add(new RowDefinition());
            Surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock
            {
                Text = "隔离外形评审：原单团子 / 休眠候选\n真实模拟快照＋现有 WPF 曲线；外形数值筛查仍未通过。本窗口不运行交互物理。",
                FontSize = 18,
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
            };
            Surface.Children.Add(heading);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            controls.Children.Add(new TextBlock { Text = "模拟时刻", VerticalAlignment = VerticalAlignment.Center });
            foreach (var frame in Frames)
            {
                frameChoice.Items.Add(frame);
            }
            foreach (var zoom in new[] { 1d, 3d, 6d })
            {
                zoomChoice.Items.Add(zoom);
            }
            controls.Children.Add(frameChoice);
            controls.Children.Add(new TextBlock { Text = "等比倍数", VerticalAlignment = VerticalAlignment.Center });
            controls.Children.Add(zoomChoice);
            controls.Children.Add(overlayChoice);
            Grid.SetRow(controls, 1);
            Surface.Children.Add(controls);
            Grid.SetRow(drawings, 2);
            Surface.Children.Add(drawings);
            Grid.SetRow(status, 3);
            Surface.Children.Add(status);
            frameChoice.SelectionChanged += (_, _) => Refresh();
            zoomChoice.SelectionChanged += (_, _) => Refresh();
            overlayChoice.Checked += (_, _) => Refresh();
            overlayChoice.Unchecked += (_, _) => Refresh();
            Select(36000, 1, false);
        }

        public void Select(int frame, double zoom, bool overlay)
        {
            frameChoice.SelectedItem = frame;
            zoomChoice.SelectedItem = zoom;
            overlayChoice.IsChecked = overlay;
            Refresh();
        }

        private void Refresh()
        {
            if (frameChoice.SelectedItem is not int frame || zoomChoice.SelectedItem is not double zoom)
            {
                return;
            }
            drawings.Children.Clear();
            drawings.ColumnDefinitions.Clear();
            var overlay = overlayChoice.IsChecked == true;
            var count = overlay ? 1 : 2;
            for (var panel = 0; panel < count; panel++)
            {
                drawings.ColumnDefinitions.Add(new ColumnDefinition());
                var host = new Grid { ClipToBounds = true };
                host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                host.RowDefinitions.Add(new RowDefinition());
                host.Children.Add(new TextBlock
                {
                    Text = overlay ? "核心对齐：原实现实线 / 候选虚线" : panel == 0 ? "原单团子（生产路径）" : "休眠候选（地面候选 4）",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(8),
                    FontSize = 16,
                });
                // Uniform Viewbox preserves the production 160x120 mapping, even if a 6x request cannot fit.
                var view = new Viewbox { Width = 160 * zoom, Height = 120 * zoom, Stretch = Stretch.Uniform, MaxWidth = overlay ? 980 : 490, MaxHeight = 430 };
                var canvas = new Canvas { Width = 160, Height = 120 };
                foreach (var pass in overlay ? new[] { "production", "ground_sleep" } : new[] { panel == 0 ? "production" : "ground_sleep" })
                {
                    var path = new ShapePath { Data = Geometry(snapshots[(pass, frame)]) };
                    if (overlay)
                    {
                        path.Stroke = pass == "production" ? Brushes.DarkSlateGray : Brushes.DarkOrange;
                        path.StrokeThickness = .5;
                        if (pass == "ground_sleep")
                        {
                            path.StrokeDashArray = [3, 2];
                        }
                    }
                    else
                    {
                        path.Fill = new SolidColorBrush(Color.FromRgb(255, 232, 194));
                    }
                    canvas.Children.Add(path);
                }
                view.Child = canvas;
                Grid.SetRow(view, 1);
                host.Children.Add(view);
                Grid.SetColumn(host, panel);
                drawings.Children.Add(host);
            }
            status.Text = $"第 {frame:N0} 帧（{frame / 60d:N2} 秒模拟时间）｜请求 {zoom}×，空间不足时等比缩小｜1× 为原控件 160×120 DIP。\n"
                + "请看顶部、两侧和底部：是否有不能接受的高矮、胖瘦或局部变形？仅显示身体轮廓，不含眼睛/装饰；这不是上次验收截图，也不验证拖动、碰撞、休眠过程。\n"
                + "请在 Codex 中反馈：可接受 / 不可接受（注明时刻与部位）。关闭窗口不代表批准；本工具不写设置、不提交验收。";
        }
    }
}
