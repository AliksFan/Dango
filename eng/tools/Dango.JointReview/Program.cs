using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.JointReview;

internal sealed record JointFrame(int Frame, int Count, int Error, int Warnings, double Self, double Contact,
    double Velocity, double Energy, double Ground, double Radius, double[][] Nodes);

internal sealed class JointSession : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> errors;
    private readonly SemaphoreSlim gate = new(1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JointSession(string executable)
    {
        process = Process.Start(new ProcessStartInfo(executable, "preview-host")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new IOException("无法启动隔离物理进程。");
        errors = process.StandardError.ReadToEndAsync();
    }

    public async Task<JointFrame> Request(string command)
    {
        await gate.WaitAsync();
        try
        {
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))
                ?? throw new IOException("物理进程已退出。");
            var frame = JsonSerializer.Deserialize<JointFrame>(line, JsonOptions) ?? throw new InvalidDataException("缺少快照。");
            if (frame.Count is < 1 or > 3 || frame.Nodes.Length != frame.Count * 17 ||
                frame.Nodes.Any(p => p.Length != 2 || p.Any(v => !double.IsFinite(v))))
            {
                throw new InvalidDataException("物理进程返回非法快照。");
            }
            return frame;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("quit");
                await process.StandardInput.FlushAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { process.Kill(); await process.WaitForExitAsync(); }
            }
            var text = await errors;
            if (process.ExitCode != 0) throw new IOException($"物理进程退出码 {process.ExitCode}：{text}");
        }
        finally
        {
            process.Dispose();
            gate.Release();
        }
    }
}

internal sealed class JointWindow : Window
{
    private readonly Canvas scene = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly TextBlock status = new() { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly ComboBox count = new() { Width = 70, ItemsSource = new[] { 1, 2, 3 }, SelectedIndex = 0 };
    private readonly ComboBox skin = new() { Width = 90, ItemsSource = new[] { "0.045", "0.020" }, SelectedIndex = 0 };
    private readonly ComboBox offset = new() { Width = 160, ItemsSource = new[] { "居中", "左偏物理直径5%", "右偏物理直径5%" }, SelectedIndex = 0 };
    private readonly CheckBox skeleton = new() { Content = "显示骨架", IsChecked = true, Margin = new Thickness(10) };
    private readonly Button pause = new() { Content = "暂停", Margin = new Thickness(8) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16.67) };
    private readonly string executable;
    private JointSession? session;
    private JointFrame? current;
    private bool busy;
    private bool paused;
    private bool closing;
    private const double Scale = 95;
    private double GroundY => Math.Max(300, scene.ActualHeight - 70);
    public DockPanel Surface { get; } = new() { Background = Brushes.White };

    public JointWindow(string executable)
    {
        this.executable = executable;
        Title = "Dango 联合求解预览｜实验候选";
        Width = 1080; Height = 820; MinWidth = 900; MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = Surface;
        var heading = new TextBlock
        {
            Text = "联合求解 · 人工观察\n真实物理快照；数值警告与安全停止会直接显示。此预览尚未通过完整验收。",
            FontSize = 18,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(heading, Dock.Top); Surface.Children.Add(heading);
        var controls = new WrapPanel { Margin = new Thickness(12) };
        foreach (var pair in new[] { ("数量", count), ("接触厚度", skin), ("摆放", offset) })
        {
            controls.Children.Add(new TextBlock { Text = pair.Item1, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center });
            controls.Children.Add(pair.Item2);
        }
        controls.Children.Add(skeleton);
        var reset = new Button { Content = "按设置重置", Margin = new Thickness(8) };
        reset.Click += async (_, _) => await Reset(); controls.Children.Add(reset);
        pause.Click += (_, _) => { paused = !paused; pause.Content = paused ? "继续" : "暂停"; }; controls.Children.Add(pause);
        var remove = new Button { Content = "移开底层支撑", Margin = new Thickness(8) };
        remove.Click += async (_, _) => await Command("remove"); controls.Children.Add(remove);
        DockPanel.SetDock(controls, Dock.Top); Surface.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom); Surface.Children.Add(status);
        Surface.Children.Add(scene);
        skeleton.Checked += (_, _) => Redraw(); skeleton.Unchecked += (_, _) => Redraw();
        scene.SizeChanged += (_, _) => Redraw();
        timer.Tick += async (_, _) => { if (!paused && current?.Error == 0) await Command("step"); };
        Loaded += async (_, _) =>
        {
            try { session = new JointSession(this.executable); await Reset(); timer.Start(); }
            catch (Exception e) { ShowFailure(e); }
        };
        Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; timer.Stop();
            try { if (session is not null) await session.DisposeAsync(); }
            catch (Exception error) { status.Text = error.Message; }
            Close();
        };
    }

    private Task Reset()
    {
        var displacement = offset.SelectedIndex switch { 1 => "-0.05", 2 => "0.05", _ => "0" };
        paused = false; pause.Content = "暂停";
        return Command($"reset {count.SelectedItem} {skin.SelectedItem} {displacement}");
    }

    private async Task Command(string command)
    {
        if (busy || closing || session is null) return;
        busy = true;
        try { Draw(await session.Request(command)); }
        catch (Exception error) { ShowFailure(error); }
        finally { busy = false; }
    }

    private void ShowFailure(Exception error) { paused = true; status.Text = $"预览已停止：{error.Message}"; }
    private void Redraw() { if (current is not null) Draw(current); }

    internal void Draw(JointFrame frame)
    {
        current = frame; scene.Children.Clear();
        scene.Children.Add(new Line { X1 = 0, X2 = scene.ActualWidth, Y1 = GroundY, Y2 = GroundY, Stroke = Brushes.SlateGray, StrokeThickness = 2 });
        var fills = new Brush[] { Brushes.PeachPuff, Brushes.LightSkyBlue, Brushes.Plum };
        for (int r = 0; r < frame.Count; ++r)
        {
            var core = frame.Nodes[r * 17 + 16];
            var points = Enumerable.Range(0, 16).Select(n =>
            {
                var p = frame.Nodes[r * 17 + n]; var x = p[0] - core[0]; var y = p[1] - core[1];
                var length = Math.Sqrt(x * x + y * y);
                if (length <= 0) throw new InvalidDataException("节点位于核心，拒绝绘制。");
                return new PresentationPoint(x * (1 + frame.Radius / length), y * (1 + frame.Radius / length));
            }).ToArray();
            var width = Scale / .45; var height = Scale / .48333333333333334;
            var geometry = DangoStreamGeometryFactory.TryCreate(points, width, height);
            if (!geometry.Succeeded || geometry.Geometry is null) throw new InvalidDataException($"轮廓校验失败：{geometry.Failure}");
            var shape = new ShapePath { Data = geometry.Geometry, Fill = fills[r], Stroke = Brushes.DimGray, StrokeThickness = 1 };
            Canvas.SetLeft(shape, scene.ActualWidth / 2 + core[0] * Scale - width / 2);
            Canvas.SetTop(shape, GroundY - (core[1] - frame.Ground) * Scale - height * .55);
            scene.Children.Add(shape);
            if (skeleton.IsChecked == true) for (int n = 0; n < 16; ++n)
            {
                var p = frame.Nodes[r * 17 + n]; var q = frame.Nodes[r * 17 + (n + 1) % 16];
                foreach (var end in new[] { q, core })
                {
                    scene.Children.Add(new Line
                    {
                        X1 = scene.ActualWidth / 2 + p[0] * Scale,
                        Y1 = GroundY - (p[1] - frame.Ground) * Scale,
                        X2 = scene.ActualWidth / 2 + end[0] * Scale,
                        Y2 = GroundY - (end[1] - frame.Ground) * Scale,
                        Stroke = Brushes.DarkSlateGray,
                        StrokeThickness = .7,
                        Opacity = .6
                    });
                }
            }
        }
        var result = frame.Error == 0 ? (frame.Warnings > 0 ? "运行中 · 存在位置残差警告" : "运行中") : $"已安全停止（{frame.Error}），保留最后安全姿态；可重置";
        status.Text = FormattableString.Invariant($"{result}\n真实模拟帧 {frame.Frame} · {frame.Count}只 · 警告小步 {frame.Warnings}\n单体 {frame.Self:G5} ｜接触 {frame.Contact:G5} ｜速度残差 {frame.Velocity:G5} ｜动能增量 {frame.Energy:G5}\n-205：速度在96次扫描内未收敛；-102：位置超过预览上限。显示帧率不代表完整实时性能通过。");
        status.Foreground = frame.Error != 0 ? Brushes.Firebrick : Brushes.DarkSlateGray;
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--static-platform")
            {
                var platformApp = new Application(); var window = new StaticPlatformWindow(args[1]);
                platformApp.Run(window); return window.ExitCode;
            }
            if (args.Length == 3 && args[0] == "--static-platform-smoke")
            {
                var platformApp = new Application(); var window = new StaticPlatformWindow(args[1], args[2]);
                platformApp.Run(window); return window.ExitCode;
            }
            if (args.Length == 2 && args[0] == "--hat-motion-review")
            {
                var hatApp = new Application(); hatApp.Run(new HatMotionWindow(args[1])); return 0;
            }
            if (args.Length == 3 && args[0] == "--verify-hat-motion")
            {
                var hatApp = new Application(); var window = new HatMotionWindow(args[1], false);
                try { window.Export(args[2]); } finally { window.Close(); hatApp.Shutdown(); }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--eye-motion-review")
            {
                var eyeApp = new Application(); eyeApp.Run(new EyeMotionWindow(args[1])); return 0;
            }
            if (args.Length == 3 && args[0] == "--verify-eye-motion")
            {
                var eyeApp = new Application(); var window = new EyeMotionWindow(args[1], false);
                try { window.Export(args[2]); } finally { window.Close(); eyeApp.Shutdown(); }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--attachment-calibration")
            {
                var calibrationApp = new Application(); calibrationApp.Run(new AttachmentCalibrationWindow(args[1])); return 0;
            }
            if (args.Length == 3 && args[0] == "--verify-attachment-calibration")
            {
                var calibrationApp = new Application();
                var window = new AttachmentCalibrationWindow(args[1]); window.Export(args[2]); window.Close(); calibrationApp.Shutdown(); return 0;
            }
            if (args.Length == 3 && args[0] is "--verify-cushion" or "--verify-cushion-broken")
            {
                CushionVerification.Run(args[1], args[2], args[0] == "--verify-cushion-broken"); return 0;
            }
            if (args.Length == 4 && args[0] is "--verify-natural" or "--verify-natural-broken")
            {
                NaturalVerification.Run(args[1], args[2], args[3], args[0] == "--verify-natural-broken"); return 0;
            }
            if (args.Length == 3 && args[0] is "--verify-patch" or "--verify-patch-broken")
            {
                PatchVerification.Run(args[1], args[2], args[0] == "--verify-patch-broken"); return 0;
            }
            if (args.Length == 3 && args[0] is "--verify-support" or "--verify-support-broken")
            {
                SupportVerification.Run(args[1], args[2], args[0] == "--verify-support-broken"); return 0;
            }
            if (args.Length == 3 && args[0] is "--verify-interaction" or "--verify-interaction-broken")
            {
                SupportVerification.Run(args[1], args[2], args[0] == "--verify-interaction-broken"); return 0;
            }
            if (args.Length == 3 && args[0] is "--verify-collision" or "--verify-collision-broken")
            {
                CollisionVerification.Run(args[1], args[2], args[0] == "--verify-collision-broken"); return 0;
            }
            if (args.Length is 2 or 3 && args[0] == "--physics")
            {
                var count = args.Length == 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 3;
                var physicsApp = new Application(); physicsApp.Run(new CollisionWindow(args[1], count)); return 0;
            }
            if (args.Length == 2 && args[0] == "--three-collision")
            {
                var threeApp = new Application(); threeApp.Run(new CollisionWindow(args[1], 3)); return 0;
            }
            if (args.Length == 3 && args[0] == "--verify-three-window")
            {
                CollisionWindow.VerifyThreeWindow(args[1], args[2]); return 0;
            }
            if (args.Length == 2 && args[0] == "--collision")
            {
                var collisionApp = new Application(); collisionApp.Run(new CollisionWindow(args[1])); return 0;
            }
            if (args.Length == 2 && args[0] == "--verify-baseline")
            {
                BaselineVerification.Run(args[1]);
                return 0;
            }
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--baseline"))
            {
                var baselineApp = new Application();
                baselineApp.Run(new BaselineWindow());
                return 0;
            }
            if (args.Length == 3 && args[0] == "--verify")
            {
                Verify(args[1], args[2]).GetAwaiter().GetResult();
                return 0;
            }
            if (args.Length != 2 || args[0] != "--candidate") throw new ArgumentException("默认运行原版单体；旧联合诊断使用 --candidate NATIVE_EXE。");
            var app = new Application(); app.Run(new JointWindow(args[1])); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task Verify(string executable, string output)
    {
        Directory.CreateDirectory(output);
        var frames = new List<JointFrame>();
        await using (var session = new JointSession(executable))
        {
            for (int count = 1; count <= 3; ++count)
            {
                var frame = await session.Request($"reset {count} 0.045 0");
                for (int i = 0; i < 120 && frame.Error == 0; ++i) frame = await session.Request("step");
                frames.Add(frame);
            }
        }
        // Marshal back to a dedicated STA for a genuine WPF layout/render check.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new JointWindow(executable);
                window.Surface.Measure(new Size(1080, 820)); window.Surface.Arrange(new Rect(0, 0, 1080, 820)); window.Surface.UpdateLayout();
                window.Draw(frames[^1]); window.Surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1080, 820, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window.Surface);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = new FileStream(System.IO.Path.Combine(output, "joint-preview.png"), FileMode.CreateNew); png.Save(file);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
        await File.WriteAllTextAsync(System.IO.Path.Combine(output, "verification.json"), JsonSerializer.Serialize(frames, new JsonSerializerOptions { WriteIndented = true }));
    }
}
