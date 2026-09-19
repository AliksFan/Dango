using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.JointReview;

/// <summary>One native world with two read-only render columns and per-entity attachment scopes.</summary>
internal sealed class HatMotionWindow : Window
{
    private sealed record Actor(AttachmentTarget Target, AttachmentToken Token, AttachmentMotionController Motion,
        DeformableDangoVisual OldBody, DeformableDangoVisual NewBody, DangoHatVisual OldHat, DangoHatVisual NewHat,
        Viewbox[] Views);
    private readonly string module;
    private DesktopPhysicsScene physics = null!;
    private AttachmentCommandRouter router = new(true);
    private readonly Dictionary<DangoEntityId, Actor> actors = [];
    private readonly Canvas oldCanvas = new() { Width = 440, Height = 480, Background = Brushes.White, ClipToBounds = true };
    private readonly Canvas newCanvas = new() { Width = 440, Height = 480, Background = Brushes.White, ClipToBounds = true };
    private readonly DockPanel surface = new() { Background = Brushes.WhiteSmoke };
    private readonly ComboBox count = new() { ItemsSource = new[] { 1, 2, 3 }, SelectedIndex = 0, Width = 55, Margin = new(6) };
    private readonly ComboBox selected = new() { Width = 80, Margin = new(6) };
    private readonly CheckBox eyes = new() { Content = "眼帽同开", IsChecked = true, Margin = new(9) };
    private readonly CheckBox follow = new() { Content = "随身体弹动", IsChecked = true, Margin = new(9) };
    private readonly ComboBox hatStyle = new() { ItemsSource = new[] { "叶芽", "小礼帽", "圆顶帽" }, SelectedIndex = 1, Width = 95, Margin = new(6) };
    private DangoFacingDirection facing = DangoFacingDirection.Center;
    private readonly Slider slide = new() { Minimum = -1, Maximum = 1, Width = 150, Margin = new(8) };
    private readonly TextBlock status = new() { Margin = new(12), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private long step, sequence;
    private bool paused, dragging, ready;
    private string last = "可拖动右侧任一团子";

    public HatMotionWindow(string module, bool autoStart = true)
    {
        this.module = module;
        Title = "Dango 眼帽预览｜切换帽子与弹性面向（待人工确认）";
        Width = 1060; Height = 790; MinWidth = 950; MinHeight = 710;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Content = surface;
        AddTop(new TextBlock { Text = "眼睛弹性面向 · 帽子随身体运动", FontSize = 23, Margin = new(14) });
        AddTop(new TextBlock { Text = "两侧使用同一物理场景；左侧旧表现，右侧新表现。拖动右侧团子；A / D 移动，S 下压，空格跳跃。", Margin = new(14, 0, 14, 8) });
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = "数量 / 操作对象", Margin = new(8) }); row.Children.Add(count); row.Children.Add(selected);
        row.Children.Add(hatStyle); row.Children.Add(Button("移除所选", RemoveSelected)); row.Children.Add(eyes); row.Children.Add(follow); row.Children.Add(Button("眨一下", () => Submit(Blink.Default(EyeSelection.Both))));
        AddTop(row);
        var motion = new WrapPanel(); motion.Children.Add(new TextBlock { Text = "帽子左移 ↔ 右移", Margin = new(8) }); motion.Children.Add(slide);
        motion.Children.Add(Button("轻推向左", () => Submit(new PulseHat(-0.2)))); motion.Children.Add(Button("轻推向右", () => Submit(new PulseHat(0.2))));
        motion.Children.Add(Button("释放回中", () => { Submit(new Release(AttachmentChannel.HatSlide)); ready = false; slide.Value = 0; ready = true; }));
        motion.Children.Add(Button("暂停 / 恢复", () => Pause(!paused))); motion.Children.Add(Button("重置", Reset)); AddTop(motion);
        AddTop(new TextBlock { Text = "试试跳跃、落地、下压和急停；静止后会收稳。取消“随身体弹动”可对照已认可的滑动。", Margin = new(14, 0, 14, 8) });
        DockPanel.SetDock(status, Dock.Bottom); surface.Children.Add(status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        for (var i = 0; i < 2; i++)
        {
            var panel = new DockPanel { Margin = new(8) };
            var label = new TextBlock { Text = i == 0 ? "旧表现（对照）" : "沿头顶滑动（候选）", FontSize = 18, Margin = new(8) };
            DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label); panel.Children.Add(new Viewbox { Child = i == 0 ? oldCanvas : newCanvas });
            Grid.SetColumn(panel, i); grid.Children.Add(panel);
        }
        surface.Children.Add(grid);
        count.SelectionChanged += (_, _) => { if (ready) Reset(); };
        selected.SelectionChanged += (_, _) => { if (ready && selected.SelectedItem is DangoEntityId id) { physics.Select(id); facing = DangoFacingDirection.Center; } };
        hatStyle.SelectionChanged += (_, _) => { if (ready) ApplyHatStyle(); };
        slide.ValueChanged += (_, _) => { if (ready) Submit(new SetHatSlide(slide.Value)); };
        newCanvas.MouseMove += (_, e) => { if (dragging) { var p = e.GetPosition(newCanvas); physics.MoveDrag(new(p.X, p.Y)); } };
        newCanvas.MouseLeftButtonUp += (_, _) => EndDrag(); newCanvas.LostMouseCapture += (_, _) => EndDrag();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { EndDrag(); physics.CancelInput(); facing = DangoFacingDirection.Center; } else Keys(e); };
        PreviewKeyUp += (_, e) => Keys(e);
        Deactivated += (_, _) => { EndDrag(); physics.CancelInput(); facing = DangoFacingDirection.Center; };
        StateChanged += (_, _) => Pause(WindowState == WindowState.Minimized);
        Closed += (_, _) => { timer.Stop(); physics.Dispose(); router.Dispose(); };
        Reset(); timer.Tick += (_, _) => Tick(); if (autoStart) timer.Start();
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new(4), Padding = new(8, 4, 8, 4) };
        button.Click += (_, _) => action(); return button;
    }
    private void AddTop(UIElement element) { DockPanel.SetDock(element, Dock.Top); surface.Children.Add(element); }
    private void Reset()
    {
        ready = false; EndDrag(); physics?.Dispose(); router.Dispose(); router = new(true);
        physics = new(module, new LogicalRect(135, 180, 170, 127.5)); actors.Clear(); oldCanvas.Children.Clear(); newCanvas.Children.Clear();
        // Calibrate the approved embedded root on a settled single body, before stacking.
        for (var i = 0; i < 240; i++)
            if (!physics.Step(TimeSpan.FromSeconds(1d / 60)).Physics.Succeeded) throw new InvalidOperationException("无法建立稳定帽根基准。");
        var hatBaseline = physics.LastFrame!.Bodies.Single().Presentation;
        for (var i = 1; i <= count.SelectedIndex; i++) physics.Add(i * 0.025, i * 1.65);
        foreach (var id in physics.EntityIds)
        {
            var target = router.Add(id); var token = router.RegisterPreview(target);
            var oldBody = new DeformableDangoVisual { ShowHat = false }; var newBody = new DeformableDangoVisual { ShowHat = false };
            var oldHat = new DangoHatVisual(); var newHat = new DangoHatVisual();
            var views = new FrameworkElement[] { oldBody, newBody, oldHat, newHat }.Select(v => new Viewbox { Child = v, Width = 170, Height = 127.5 }).ToArray();
            oldCanvas.Children.Add(views[0]); newCanvas.Children.Add(views[1]); oldCanvas.Children.Add(views[2]); newCanvas.Children.Add(views[3]);
            Panel.SetZIndex(views[2], 1); Panel.SetZIndex(views[3], 1);
            views[2].IsHitTestVisible = views[3].IsHitTestVisible = false;
            views[1].MouseLeftButtonDown += (_, e) =>
            {
                if (paused || physics.LastErrorCode != 0) return;
                selected.SelectedItem = id; var p = e.GetPosition(newCanvas);
                physics.BeginDrag(id, new(p.X, p.Y)); dragging = true; newCanvas.CaptureMouse(); e.Handled = true;
            };
            actors.Add(id, new(target, token, new(router, target, hatBaseline), oldBody, newBody, oldHat, newHat, views));
        }
        selected.ItemsSource = physics.EntityIds; selected.SelectedIndex = 0; slide.Value = 0; step = sequence = 0; paused = false; facing = DangoFacingDirection.Center; ready = true; ApplyHatStyle();
    }
    private void ApplyHatStyle()
    {
        var appearance = (HatAppearance)hatStyle.SelectedIndex;
        foreach (var a in actors.Values)
        {
            a.OldHat.Appearance = a.NewHat.Appearance = appearance;
            a.OldBody.Appearance = a.NewBody.Appearance = appearance;
            a.OldHat.HatSize = a.NewHat.HatSize = a.OldBody.HatSize = a.NewBody.HatSize = 1.35;
        }
    }
    private void SetMovement(bool left, bool right, bool jump = false, bool squash = false)
    {
        facing = left == right ? DangoFacingDirection.Center : left ? DangoFacingDirection.Left : DangoFacingDirection.Right;
        physics.SetInput(new(left, right, jump, squash));
    }
    private void Submit(AttachmentIntent intent)
    {
        if (selected.SelectedItem is not DangoEntityId id) return;
        var actor = actors[id]; var result = router.Submit(actor.Target, actor.Token, ++sequence, intent);
        last = result.Accepted ? "动作已接纳" : $"动作未接纳：{result.Reason}";
    }
    private void EndDrag() { if (!dragging) return; dragging = false; physics.EndDrag(); newCanvas.ReleaseMouseCapture(); }
    private void Pause(bool value) { EndDrag(); facing = DangoFacingDirection.Center; paused = value; physics.SetPaused(value); router.SetPaused(value); foreach (var a in actors.Values) a.Motion.Paused = value; }
    private void Keys(KeyEventArgs e)
    {
        if (paused || e.Key is not (Key.A or Key.D or Key.S or Key.Space)) return;
        SetMovement(Keyboard.IsKeyDown(Key.A), Keyboard.IsKeyDown(Key.D), Keyboard.IsKeyDown(Key.Space), Keyboard.IsKeyDown(Key.S)); e.Handled = true;
    }
    private void RemoveSelected()
    {
        if (paused || selected.SelectedItem is not DangoEntityId id || physics.Count == 1) return;
        EndDrag(); physics.Remove(id); var actor = actors[id]; router.Remove(actor.Target); actors.Remove(id);
        for (var i = 0; i < 4; i++) (i % 2 == 0 ? oldCanvas : newCanvas).Children.Remove(actor.Views[i]);
        ready = false; selected.ItemsSource = physics.EntityIds; selected.SelectedIndex = 0; ready = true;
    }
    private void Tick()
    {
        if (paused) return;
        try
        {
            var frame = physics.Step(TimeSpan.FromSeconds(1d / 60));
            if (!frame.Physics.Succeeded) { Pause(true); status.Text = $"物理故障：{frame.Physics.ErrorCode}，已冻结，请重置。"; return; }
            foreach (var body in frame.Bodies)
            {
                var a = actors[body.EntityId]; var physical = frame.Physics.Bodies.Single(b => b.EntityId == body.EntityId);
                var attachment = a.Motion.Update(body.Presentation, step, 1d / 60,
                    facing: body.EntityId == physics.SelectedEntity ? facing : DangoFacingDirection.Center,
                    bodyMotion: new HatBodySample(physical.CoreX, physical.CoreY), hatBodyFollowEnabled: follow.IsChecked == true)!;
                var candidate = body.Presentation with { Attachments = attachment };
                a.OldBody.ApplyFrame(body.Presentation); a.NewBody.ApplyFrame(eyes.IsChecked == true ? candidate : body.Presentation);
                a.OldHat.ApplyFrame(body.Presentation); a.NewHat.ApplyFrame(candidate);
                foreach (var view in a.Views) { Canvas.SetLeft(view, body.Bounds.X); Canvas.SetTop(view, body.Bounds.Y); }
                if (router.ConsumeResults(a.Target).Count > 0) last = "动作已完成";
            }
            step++;
            status.Text = last + "。" + string.Join("；", actors.Select(a => $"{a.Key}：{(a.Value.Motion.Current?.Hat?.Pose is null ? "帽子暂不可放置" : "帽根有效")}"));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ArithmeticException or DangoPhysicsException)
        { Pause(true); status.Text = "预览已冻结：" + e.Message; }
    }

    internal void Export(string directory)
    {
        if (Directory.Exists(directory)) throw new IOException("Evidence directory already exists.");
        Directory.CreateDirectory(directory);
        for (var n = 1; n <= 3; n++)
        {
            ready = false; count.SelectedIndex = n - 1; Reset();
            for (var i = 0; i < 180; i++) Tick(); Save($"{n}-neutral");
            Submit(new SetHatSlide(0.8)); for (var i = 0; i < 60; i++) Tick(); Save($"{n}-slide");
            Submit(new Release(AttachmentChannel.HatSlide)); Submit(new PulseHat(-0.2));
            for (var i = 0; i < 5; i++) Tick(); Save($"{n}-pulse");
            physics.SetInput(new(false, false, false, true)); for (var i = 0; i < 30; i++) Tick(); Save($"{n}-squash");
            physics.CancelInput(); for (var i = 0; i < 90; i++) Tick(); Save($"{n}-recovered");
            physics.SetInput(new(false, false, true, false)); Tick(); physics.CancelInput();
            for (var i = 0; i < 4; i++) Tick(); Save($"{n}-jump");
            for (var i = 0; i < 120; i++) Tick(); Save($"{n}-settled");
        }
        ready = false; count.SelectedIndex = 0; Reset();
        foreach (var style in Enum.GetValues<HatAppearance>())
        {
            hatStyle.SelectedIndex = (int)style;
            for (var i = 0; i < 30; i++) Tick(); Save("hat-" + style);
        }
        hatStyle.SelectedIndex = 1;
        SetMovement(true, false); for (var i = 0; i < 12; i++) Tick(); Save("facing-left");
        SetMovement(false, false); for (var i = 0; i < 30; i++) Tick(); Save("facing-released");
        SetMovement(false, true); for (var i = 0; i < 12; i++) Tick(); Save("facing-right");
        SetMovement(false, false);
        void Save(string name)
        {
            if (paused) throw new InvalidOperationException(status.Text);
            surface.Measure(new(1060, 790)); surface.Arrange(new Rect(0, 0, 1060, 790)); surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1060, 790, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(Path.Combine(directory, name + ".png"), FileMode.CreateNew); encoder.Save(output);
            File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(actors.Values.Select(a => a.Motion.Current)));
        }
    }
}
