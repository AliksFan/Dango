using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.JointReview;

internal sealed class BaselineWindow : Window
{
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private readonly StackedDangoSceneRuntime runtime = new();
    private readonly Canvas scene = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, Cursor = Cursors.Hand };
    private readonly DangoHatVisual hat = new();
    private readonly TextBlock status = new() { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer timer = new() { Interval = FixedStep };
    private readonly HashSet<Key> keys = [];
    private bool dragging;
    private bool paused;
    private int frames;
    private StackedPhysicsPresentationFrame? displayedFrame;
    private Vector ViewTranslation => new(scene.ActualWidth / 2 - 480, scene.ActualHeight - 450);
    internal DockPanel Surface { get; } = new() { Background = Brushes.White };
    internal static PhysicsPresentationInput Input(DangoFacingDirection facing = DangoFacingDirection.Center) =>
        new("baseline-fictional", new LogicalRect(400, 300, 160, 120), DangoActionState.Idle,
            1000, 17, "baseline-ground", new LogicalVector(0, -1), 0, 1, false, true, [], facingDirection: facing);

    public BaselineWindow()
    {
        Title = "Dango 原版单体基线｜碰撞尚未接入";
        Width = 980; Height = 660; MinWidth = 800; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = Surface;
        var heading = new TextBlock
        {
            Text = "原版单体基线\n复用原运行时、正式 DLL 和完整视觉组件。拖动团子；A/D 移动，W 跳跃，S 下压。",
            FontSize = 17,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(heading, Dock.Top); Surface.Children.Add(heading);
        var controls = new WrapPanel { Margin = new Thickness(12) };
        var reset = new Button { Content = "重新生成", Margin = new Thickness(8) };
        reset.Click += (_, _) => { ClearInput(); runtime.RegenerateDango(); frames = 0; paused = false; Focus(); };
        var pause = new Button { Content = "暂停 / 继续", Margin = new Thickness(8) };
        pause.Click += (_, _) => { paused = !paused; ClearInput(); Focus(); };
        var skeleton = new CheckBox { Content = "显示原版骨架", Margin = new Thickness(8) };
        skeleton.Click += (_, _) => { body.ShowDebugGeometry = skeleton.IsChecked == true; if (runtime.LastFrame is { } frame) Apply(frame); };
        controls.Children.Add(reset); controls.Children.Add(pause); controls.Children.Add(skeleton);
        DockPanel.SetDock(controls, Dock.Top); Surface.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom); Surface.Children.Add(status);
        Surface.Children.Add(scene); scene.Children.Add(body); scene.Children.Add(hat);
        scene.SizeChanged += (_, _) => { if (displayedFrame is { } frame) Apply(frame); };
        // Original host uses the same logical bounds and render offsets. No isotropic rescaling.
        body.MouseLeftButtonDown += (_, e) =>
        {
            if (paused) return;
            Focus(); BeginDrag(Pointer(e), () => { if (!body.CaptureMouse()) EndDrag(); }); e.Handled = true;
        };
        body.MouseMove += (_, e) => { if (dragging) runtime.MovePhysicalDrag(Pointer(e)); };
        body.MouseLeftButtonUp += (_, _) => EndDrag();
        body.LostMouseCapture += (_, _) => { dragging = false; runtime.EndPhysicalDrag(); };
        PreviewKeyDown += (_, e) => { if (e.Key is Key.A or Key.D or Key.W or Key.S) { keys.Add(e.Key); e.Handled = true; } };
        PreviewKeyUp += (_, e) => { keys.Remove(e.Key); };
        Deactivated += (_, _) => ClearInput();
        Loaded += (_, _) => { runtime.Start(); timer.Start(); Focus(); };
        Closed += (_, _) => { timer.Stop(); ClearInput(); runtime.Dispose(); };
        timer.Tick += (_, _) =>
        {
            if (paused) return;
            try
            {
                runtime.SetDirectionalControl(new(keys.Contains(Key.A), keys.Contains(Key.D), keys.Contains(Key.W), keys.Contains(Key.S)));
                var frame = runtime.Tick(Input(), FixedStep);
                Apply(frame); frames++;
                status.Text = $"单体基线 · 模拟帧 {frames} · 碰撞未接入\n{runtime.CreateDiagnosticSummary()}";
                if (frame.Bodies[0].FallbackReason != PhysicsPresentationFallbackReason.None) paused = true;
            }
            catch (Exception error) { paused = true; status.Text = $"基线运行失败：{error.Message}"; }
        };
    }

    private LogicalPoint Pointer(MouseEventArgs e)
    {
        var p = e.GetPosition(scene); var translation = ViewTranslation;
        return new(p.X - translation.X, p.Y - translation.Y);
    }
    private void BeginDrag(LogicalPoint target, Action capture)
    {
        // CaptureMouse synchronously dispatches MouseMove: initialize physics before capturing.
        runtime.BeginPhysicalDrag(target);
        dragging = true;
        capture();
    }
    private void EndDrag() { dragging = false; runtime.EndPhysicalDrag(); body.ReleaseMouseCapture(); }
    private void ClearInput() { keys.Clear(); EndDrag(); runtime.SetDirectionalControl(DirectionalControlIntent.None); }

    internal void Apply(StackedPhysicsPresentationFrame frame)
    {
        displayedFrame = frame;
        var bounds = frame.AuthoritativeBounds[0]; var offset = frame.RenderOffsets[0];
        var translation = ViewTranslation;
        foreach (var visual in new FrameworkElement[] { body, hat })
        {
            Canvas.SetLeft(visual, bounds.X + offset.X + translation.X);
            Canvas.SetTop(visual, bounds.Y + offset.Y + translation.Y);
        }
        body.ApplyFrame(frame.Bodies[0]); hat.ApplyFrame(frame.Bodies[0]);
        if (body.LastContourFailure != DangoContourFailure.None) throw new InvalidDataException("原版轮廓校验失败，不能用 fallback 冒充基线。");
    }

    internal void SaveSnapshot(StackedPhysicsPresentationFrame frame, string path)
    {
        BeginDrag(new LogicalPoint(480, 300), () => runtime.MovePhysicalDrag(new LogicalPoint(481, 300)));
        EndDrag();
        Surface.Measure(new Size(960, 620)); Surface.Arrange(new Rect(0, 0, 960, 620)); Surface.UpdateLayout();
        Apply(frame); status.Text = $"单体基线 · 模拟帧 900 · 碰撞未接入\n{runtime.CreateDiagnosticSummary()}";
        Surface.UpdateLayout();
        Apply(frame);
        if (Canvas.GetTop(body) < 0 || Canvas.GetTop(body) + body.Height > scene.ActualHeight)
            throw new InvalidDataException($"稳定团子被裁剪：top={Canvas.GetTop(body)}, height={body.Height}, viewport={scene.ActualHeight}");
        var bitmap = new RenderTargetBitmap(960, 620, 96, 96, PixelFormats.Pbgra32); bitmap.Render(Surface);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = new FileStream(path, FileMode.CreateNew); png.Save(file);
        runtime.Dispose();
    }
}

internal static class BaselineVerification
{
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static bool SameContour(IReadOnlyList<PresentationPoint> a, IReadOnlyList<PresentationPoint> b) => a.SequenceEqual(b);

    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        // The original native cleanup accounting is process-global: run the two worlds sequentially.
        var expectedFrames = new List<StackedPhysicsPresentationFrame>();
        using (var original = new Box2DSelfPhysicsPresentationWorld(Box2DNativeLoader.DefaultLibraryPath, DangoSelfPhysicsParameters.CreateDefault()))
        {
            var referenceFacing = DangoFacingDirection.Center;
            var referenceJumpHeld = false;
            for (int i = 0; i < 2400; i++)
            {
                if (i == 1800) { original.Reset(); referenceJumpHeld = false; }
                var left = i is >= 1020 and < 1080; var right = i is >= 900 and < 960;
                var jump = i is >= 1100 and < 1110; var squash = i is >= 1200 and < 1260;
                var drag = i is >= 1300 and < 1540;
                var target = new LogicalPoint(480 + 110 * Math.Sin(i * .043), 260 + 80 * Math.Cos(i * .071));
                if (left != right) referenceFacing = left ? DangoFacingDirection.Left : DangoFacingDirection.Right;
                var frame = original.Step(BaselineWindow.Input(referenceFacing), BaselineWindow.FixedStep,
                    new(left == right ? 0 : left ? -1 : 1, squash ? 1 : 0, jump && !referenceJumpHeld, drag, target));
                Require(!original.IsRestartBlocked, $"original latched at {i}");
                expectedFrames.Add(frame); referenceJumpHeld = jump;
            }
        }
        using var runtime = new StackedDangoSceneRuntime(); runtime.Start();
        StackedPhysicsPresentationFrame? stable = null;
        var dragHeld = false;
        for (int i = 0; i < 2400; i++)
        {
            if (i == 1800) { runtime.RegenerateDango(); dragHeld = false; }
            var left = i is >= 1020 and < 1080; var right = i is >= 900 and < 960;
            var jump = i is >= 1100 and < 1110; var squash = i is >= 1200 and < 1260;
            var drag = i is >= 1300 and < 1540;
            var target = new LogicalPoint(480 + 110 * Math.Sin(i * .043), 260 + 80 * Math.Cos(i * .071));
            runtime.SetDirectionalControl(new(left, right, jump, squash));
            if (drag && !dragHeld) runtime.BeginPhysicalDrag(target);
            else if (drag) runtime.MovePhysicalDrag(target);
            else if (dragHeld) runtime.EndPhysicalDrag();
            var actual = runtime.Tick(BaselineWindow.Input(), BaselineWindow.FixedStep);
            var expected = expectedFrames[i];
            dragHeld = drag;
            var a = actual.Bodies[0]; var b = expected.Bodies[0];
            Require(a.FallbackReason == PhysicsPresentationFallbackReason.None, $"fallback at {i}");
            Require(SameContour(a.Contour, b.Contour) && a.LeftEye == b.LeftEye && a.RightEye == b.RightEye && a.Hat == b.Hat,
                $"contour/accessory mismatch at {i}");
            Require(actual.RenderOffsets.SequenceEqual(expected.RenderOffsets) && actual.MaximumNodeSpeed == expected.MaximumNodeSpeed && actual.ContactCount == expected.ContactCount,
                $"motion mismatch at {i}");
            if (i == 899) stable = actual;
        }
        Require(stable is not null, "missing stable frame");
        var changed = stable!.Bodies[0].Contour.ToArray(); changed[0] = new(changed[0].X + .01, changed[0].Y);
        Require(!SameContour(stable.Bodies[0].Contour, changed), "negative control accepted changed silhouette");
        new BaselineWindow().SaveSnapshot(stable, Path.Combine(output, "baseline.png"));
        var evidence = new
        {
            Frames = 2400,
            ExactContourAndAccessories = true,
            ExactMotion = true,
            MutatedContourRejected = true,
            NativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Box2DNativeLoader.DefaultLibraryPath))),
            CollisionImplemented = false,
            HumanAcceptance = "pending",
        };
        using var report = new FileStream(Path.Combine(output, "verification.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, evidence, new JsonSerializerOptions { WriteIndented = true });
    }
}
