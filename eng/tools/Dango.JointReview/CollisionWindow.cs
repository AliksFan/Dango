using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.JointReview;

internal sealed class CollisionWindow : Window
{
    private readonly string libraryPath;
    private readonly int bodyCount;
    private readonly DockPanel surface = new() { Background = Brushes.White };
    private readonly Canvas scene = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly TextBlock status = new() { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox selected = new() { ItemsSource = new[] { "团子 1", "团子 2" }, SelectedIndex = 0, Width = 100, Margin = new Thickness(8) };
    private readonly ComboBox layout = new() { ItemsSource = new[] { "并排碰撞", "上下落体（探索）" }, SelectedIndex = 0, Width = 170, Margin = new Thickness(8) };
    private readonly DeformableDangoVisual[] bodies;
    private readonly DangoHatVisual[] hats;
    private readonly DangoPhysicsPresentationAdapter[] presenters;
    private readonly DangoFacingDirection[] facing;
    private readonly HashSet<Key> keys = [];
    private readonly DispatcherTimer timer = new() { Interval = BaselineWindow.FixedStep };
    private PhysicsModuleSession? session;
    private PhysicsPresentationFrame[]? frames;
    private DangoBodyState[]? nativeFrames;
    private bool dragging, paused, jumpHeld;
    private int frameCount;
    private double OriginX => scene.ActualWidth / 2 - (bodyCount == 3 ? 0 : 112);
    private double OriginY => scene.ActualHeight - 110;
    private static DangoPhysicsPresentationAdapter CreatePresenter() => new(DangoPhysicsDefaults.Current);

    internal static bool VerifyAppearance(string path)
    {
        var window = new CollisionWindow(path);
        var valid = window.hats.Min(Panel.GetZIndex) > window.bodies.Max(Panel.GetZIndex)
            && window.bodies[0].BodyFill.ToString() != window.bodies[1].BodyFill.ToString();
        return valid;
    }

    internal CollisionWindow(string path, int count = 2)
    {
        if (count is < 2 or > 3) throw new ArgumentOutOfRangeException(nameof(count));
        libraryPath = path; bodyCount = count;
        bodies = Enumerable.Range(0, count).Select(_ => new DeformableDangoVisual { ShowHat = false }).ToArray();
        hats = Enumerable.Range(0, count).Select(_ => new DangoHatVisual()).ToArray();
        presenters = Enumerable.Range(0, count).Select(_ => CreatePresenter()).ToArray();
        facing = new DangoFacingDirection[count];
        selected.ItemsSource = Enumerable.Range(1, count).Select(i => $"团子 {i}").ToArray(); selected.SelectedIndex = 0;
        if (count == 3) { layout.ItemsSource = new[] { "三体并排", "三层落下", "两下托一上" }; layout.SelectedIndex = 0; }
        Title = count == 3 ? "Dango 三体人工测试｜实验" : "Dango 原版双体碰撞｜实验";
        Width = 980; Height = 700; MinWidth = 800; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Content = surface;
        var title = new TextBlock { Text = $"原版团子 · {count} 体人工测试\n点击/拖动选中团子，A/D 移动，W 跳跃，S 下压。选择场景后按重置；自然滑落允许。", FontSize = 17, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(title, Dock.Top); surface.Children.Add(title);
        var controls = new WrapPanel(); controls.Children.Add(selected); controls.Children.Add(layout);
        var reset = new Button { Content = "按场景重置", Margin = new Thickness(8) };
        reset.Click += (_, _) => Reset(); controls.Children.Add(reset);
        if (count == 3)
        {
            var remove = new Button { Content = "移除选中团子", Margin = new Thickness(8) };
            remove.Click += (_, _) =>
            {
                if (session is null || session.Error != 0) return;
                try
                {
                    ClearInput(); session.Remove(selected.SelectedIndex);
                    selected.SelectedIndex = Enumerable.Range(0, bodyCount).FirstOrDefault(session.IsActive, -1);
                    Apply(session.Step(new float[bodyCount * 6]));
                    status.Text = "已移除选中团子；按场景重置恢复三只。"; Focus();
                }
                catch (Exception error) { paused = true; status.Text = error.Message; }
            };
            controls.Children.Add(remove);
        }
        var pause = new Button { Content = "暂停 / 继续", Margin = new Thickness(8) };
        pause.Click += (_, _) => { paused = !paused; ClearInput(); Focus(); }; controls.Children.Add(pause);
        var debug = new CheckBox { Content = "原版骨架", Margin = new Thickness(8) };
        debug.Click += (_, _) => { foreach (var body in bodies) body.ShowDebugGeometry = debug.IsChecked == true; Draw(); };
        controls.Children.Add(debug); DockPanel.SetDock(controls, Dock.Top); surface.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom); surface.Children.Add(status); surface.Children.Add(scene);
        for (int i = 0; i < bodyCount; i++)
        {
            var index = i; var body = bodies[i]; body.Cursor = Cursors.Hand;
            body.BodyFill = new SolidColorBrush(i == 0 ? Color.FromRgb(255, 232, 194) : i == 1 ? Color.FromRgb(241, 189, 208) : Color.FromRgb(183, 222, 216));
            Panel.SetZIndex(body, 0); Panel.SetZIndex(hats[i], 100);
            scene.Children.Add(body); scene.Children.Add(hats[i]);
            body.MouseLeftButtonDown += (_, e) => { if (paused || session?.Error != 0) return; selected.SelectedIndex = index; Focus(); dragging = true; if (!body.CaptureMouse()) dragging = false; e.Handled = true; };
            body.MouseLeftButtonUp += (_, _) => ClearDrag();
            body.LostMouseCapture += (_, _) => dragging = false;
        }
        selected.SelectionChanged += (_, _) => ClearInput();
        PreviewKeyDown += (_, e) => { if (e.Key is Key.A or Key.D or Key.W or Key.S) { keys.Add(e.Key); e.Handled = true; } };
        PreviewKeyUp += (_, e) => keys.Remove(e.Key);
        Deactivated += (_, _) => ClearInput();
        scene.SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => { Reset(); timer.Start(); Focus(); };
        Closed += (_, _) => { timer.Stop(); ClearInput(); session?.Dispose(); };
        timer.Tick += (_, _) => Tick();
    }

    private void ClearDrag() { dragging = false; foreach (var body in bodies) if (body.IsMouseCaptured) body.ReleaseMouseCapture(); }
    private void ClearInput() { keys.Clear(); ClearDrag(); jumpHeld = false; }
    private void Reset()
    {
        try
        {
            ClearInput(); session?.Dispose(); session = null;
            foreach (var presenter in presenters) presenter.Reset();
            Array.Clear(facing); frames = null; nativeFrames = null;
            session = new PhysicsModuleSession(libraryPath, bodyCount, layout.SelectedIndex);
            if (selected.SelectedIndex < 0) selected.SelectedIndex = 0;
            paused = false; frameCount = 0; Focus(); Tick();
        }
        catch (Exception error) { paused = true; status.Text = error.Message; }
    }

    private void Tick()
    {
        if (paused || session is null || session.Error != 0) return;
        try
        {
            var c = new float[bodyCount * 6]; var r = selected.SelectedIndex;
            if (session.IsActive(r))
            {
                c[r * 6] = (keys.Contains(Key.D) ? 1 : 0) - (keys.Contains(Key.A) ? 1 : 0);
                c[r * 6 + 1] = keys.Contains(Key.S) ? 1 : 0;
                c[r * 6 + 2] = keys.Contains(Key.W) && !jumpHeld ? 1 : 0; jumpHeld = keys.Contains(Key.W);
                c[r * 6 + 3] = dragging ? 1 : 0;
                var pointer = Mouse.GetPosition(scene);
                c[r * 6 + 4] = (float)((pointer.X - OriginX) / 80);
                c[r * 6 + 5] = (float)((OriginY - pointer.Y) / 60);
                if (c[r * 6] != 0) facing[r] = c[r * 6] < 0 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
            }
            Apply(session.Step(c)); frameCount++;
            status.Text = $"选中团子 {r + 1} · 帧 {frameCount} · 跨体接触小步 {session.ContactSteps}\n" +
                (session.Error == 0 ? "运行中 · 贴合显示轮廓接触 · 跨体摩擦 0" : $"已停止 {session.Error}，显示最后安全帧；按场景重置可恢复") +
                "\n全方向允许较小团子接触方向尺寸的3%重叠 · 保留竖直低跳、静止抗推与头顶受压展开。";
            status.Foreground = session.Error == 0 ? Brushes.DarkSlateGray : Brushes.Firebrick;
        }
        catch (Exception error) { paused = true; status.Text = $"碰撞预览停止：{error.Message}"; }
    }

    private void Apply(DangoBodyState[] native)
    {
        nativeFrames = native;
        frames = native.Select(f => { var slot = session?.SlotFor(f.EntityId) ?? f.EntityId.Value - 1; return presenters[slot].Present(f, BaselineWindow.FixedStep, facing[slot]); }).ToArray();
        Draw();
    }
    private void Draw()
    {
        if (frames is null || nativeFrames is null) return;
        foreach (var visual in bodies.Cast<FrameworkElement>().Concat(hats)) visual.Visibility = Visibility.Hidden;
        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            int i = session?.SlotFor(nativeFrames[frameIndex].EntityId) ?? nativeFrames[frameIndex].EntityId.Value - 1;
            foreach (var visual in new FrameworkElement[] { bodies[i], hats[i] })
            {
                visual.Visibility = Visibility.Visible;
                Canvas.SetLeft(visual, OriginX + nativeFrames[frameIndex].CoreX * 80 - 80);
                Canvas.SetTop(visual, OriginY - nativeFrames[frameIndex].CoreY * 60 - 60);
            }
            bodies[i].ApplyFrame(frames[frameIndex]); hats[i].ApplyFrame(frames[frameIndex]);
            if (bodies[i].LastContourFailure != DangoContourFailure.None) throw new InvalidDataException("Collision contour failed validation");
        }
    }
    internal static void SaveSnapshot(string path, Box2DSelfSoftBodyNativeFrame[] frame, string output)
    {
        var window = new CollisionWindow(path);
        window.status.Text = "双体真实接触实验快照\n原版完整组件与坐标映射\n稳定堆叠未验收";
        window.surface.Measure(new Size(960, 660)); window.surface.Arrange(new Rect(0, 0, 960, 660)); window.surface.UpdateLayout();
        window.Apply(frame.Select((value, index) => new DangoBodyState(new DangoEntityId(index + 1), value.Points,
            value.CoreX, value.CoreY, value.SignedArea, value.CentroidX, value.CentroidY, value.MaximumSpeed,
            value.GroundContactCount, value.Grounded, value.SelectedDragNode, value.PointCount)).ToArray()); window.surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(960, 660, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window.surface);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(output, FileMode.CreateNew); png.Save(stream);
    }

    internal static void VerifyThreeWindow(string path, string output)
    {
        Directory.CreateDirectory(output);
        var window = new CollisionWindow(path, 3);
        try
        {
            if (window.hats.Min(Panel.GetZIndex) <= window.bodies.Max(Panel.GetZIndex) ||
                window.bodies.Select(b => b.BodyFill.ToString()).Distinct().Count() != 3)
                throw new InvalidDataException("Three-body colors or hat layering invalid.");
            for (int sceneIndex = 0; sceneIndex < 3; sceneIndex++)
            {
                window.layout.SelectedIndex = sceneIndex; window.Reset();
                for (int i = 1; i < 90; i++) window.Tick();
                if (window.session?.Error != 0 || window.paused || window.nativeFrames?.Length != 3)
                    throw new InvalidDataException("Three-body UI scene failed: " + window.status.Text);
                window.surface.Measure(new Size(980, 700)); window.surface.Arrange(new Rect(0, 0, 980, 700));
                window.surface.UpdateLayout(); window.Draw();
                var bitmap = new RenderTargetBitmap(980, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window.surface);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new FileStream(Path.Combine(output, $"scene-{sceneIndex}.png"), FileMode.CreateNew); png.Save(stream);
            }
            var controls = window.surface.Children.OfType<WrapPanel>().Single();
            var remove = controls.Children.OfType<Button>().Single(b => Equals(b.Content, "移除选中团子"));
            for (int i = 0; i < 3; i++)
            {
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.paused || window.nativeFrames?.Length != 2 - i ||
                    window.bodies.Count(b => b.Visibility == Visibility.Visible) != 2 - i ||
                    window.hats.Count(b => b.Visibility == Visibility.Visible) != 2 - i)
                    throw new InvalidDataException("Three-body remove button failed.");
            }
            controls.Children.OfType<Button>().Single(b => Equals(b.Content, "按场景重置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (window.paused || window.nativeFrames?.Length != 3 || window.selected.SelectedIndex < 0)
                throw new InvalidDataException("Three-body reset button failed.");
            Console.WriteLine("Three-body UI: 3 layouts rendered; distinct colors; hats above all bodies; remove all and reset PASS.");
        }
        finally { window.timer.Stop(); window.session?.Dispose(); }
    }
}
