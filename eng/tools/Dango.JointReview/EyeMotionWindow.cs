using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.JointReview;

/// <summary>Isolated one-body review. Both columns consume the same successful native frame.</summary>
internal sealed class EyeMotionWindow : Window
{
    private readonly DesktopPhysicsScene physics;
    private AttachmentCommandRouter router = new();
    private AttachmentTarget target;
    private AttachmentToken token;
    private AttachmentMotionController controller = null!;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private readonly Canvas oldCanvas = new() { Width = 430, Height = 330, Background = Brushes.White, ClipToBounds = true };
    private readonly Canvas newCanvas = new() { Width = 430, Height = 330, Background = Brushes.White, ClipToBounds = true };
    private readonly DeformableDangoVisual oldBody = new(), newBody = new();
    private readonly Viewbox oldView, newView;
    private readonly ComboBox selection = new() { ItemsSource = new[] { "双眼", "左眼", "右眼" }, SelectedIndex = 0, Width = 90, Margin = new(5), VerticalAlignment = VerticalAlignment.Center };
    private static readonly string[] ShapeNames = ["圆眼", "柔和", "自然合眼", "紧闭 ><", "开心", "困倦", "横线", "生气", "惊讶", "疑惑"];
    private readonly ComboBox shape = new() { ItemsSource = ShapeNames, SelectedIndex = 0, Width = 90, Margin = new(5) };
    private readonly Slider openness = new() { Minimum = 0, Maximum = 1, Value = 1, Width = 110, Margin = new(5) };
    private readonly Slider strength = new() { Minimum = 0, Maximum = 1, Value = 0.7, Width = 100, Margin = new(5) };
    private readonly TextBlock status = new() { Margin = new(16), TextWrapping = TextWrapping.Wrap };
    private readonly DockPanel surface = new() { Background = Brushes.WhiteSmoke };
    private Button blinkButton = null!, closeButton = null!, openButton = null!;
    private SetEyes leftPose = new(EyeSelection.Left, 1), rightPose = new(EyeSelection.Right, 1);
    private long step, sequence;
    private DangoFacingDirection facing = DangoFacingDirection.Center;
    private bool paused, dragging, ready, closed;
    private string lastRequest = "尚无动作";

    public EyeMotionWindow(string module, bool autoStart = true)
    {
        Title = "Dango 眼睛 EH-04R｜重绘眼形与经典表情（待人工确认）";
        Width = 1050; Height = 730; MinWidth = 980; MinHeight = 690;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = surface;
        physics = new(module, new LogicalRect(95, 35, 240, 180));
        oldView = new() { Child = oldBody, Width = 240, Height = 180 };
        newView = new() { Child = newBody, Width = 240, Height = 180 };
        oldCanvas.Children.Add(oldView); newCanvas.Children.Add(newView);
        ResetAttachments();
        AddTop(new TextBlock { Text = "眼睛 · 重绘开合与经典表情", FontSize = 23, Margin = new(16, 12, 16, 6) });
        AddTop(new TextBlock { Text = "左右同步展示同一只团子。右侧可拖动；按住 S 下压，A / D 移动，空格跳跃。\n本轮只检查眼睛，帽子保持原样。", Margin = new(16, 0, 16, 8) });
        var row = new WrapPanel();
        row.Children.Add(selection); row.Children.Add(Label("注视"));
        var gaze = new UniformGrid { Rows = 3, Columns = 3 };
        for (var y = 1; y >= -1; y--)
            for (var x = -1; x <= 1; x++)
            {
                var gx = x; var gy = y;
                gaze.Children.Add(Button(x == 0 && y == 0 ? "•" : y > 0 ? x < 0 ? "↖" : x == 0 ? "↑" : "↗" : y < 0 ? x < 0 ? "↙" : x == 0 ? "↓" : "↘" : x < 0 ? "←" : "→", () => Submit(new SetGaze(SelectedEye, gx, gy))));
            }
        row.Children.Add(gaze); blinkButton = Button("眨一下", () => Submit(Blink.Default(SelectedEye))); row.Children.Add(blinkButton);
        row.Children.Add(Button("反向注视", () => { Submit(new SetGaze(EyeSelection.Left, -1, 1)); Submit(new SetGaze(EyeSelection.Right, 1, -1)); }));
        row.Children.Add(Button("释放所选眼", () => Submit(new Release(SelectedChannels))));
        AddTop(row);
        var pose = new WrapPanel(); pose.Children.Add(Label("开合")); pose.Children.Add(openness); pose.Children.Add(shape);
        pose.Children.Add(Label("柔和程度")); pose.Children.Add(strength);
        closeButton = Button("闭眼", () => { openness.Value = 0; SendPose(); }); pose.Children.Add(closeButton);
        openButton = Button("睁眼", () => { openness.Value = 1; SendPose(); }); pose.Children.Add(openButton);
        pose.Children.Add(Button("重置", ResetAll));
        var pause = Button("暂停 / 恢复", () => SetPaused(!paused)); pose.Children.Add(pause); AddTop(pose);
        openness.ValueChanged += (_, _) => SendPose(); strength.ValueChanged += (_, _) => SendPose(); shape.SelectionChanged += (_, _) => { SendPose(); RefreshAvailability(); };
        selection.SelectionChanged += (_, _) => RefreshEditor();
        DockPanel.SetDock(status, Dock.Bottom); surface.Children.Add(status);
        var columns = new Grid { Margin = new(10) }; columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new());
        AddColumn(columns, "旧眼睛（对照）", oldCanvas, 0); AddColumn(columns, "独立眼睛（候选）", newCanvas, 1); surface.Children.Add(columns);
        newView.MouseLeftButtonDown += (_, e) =>
        {
            if (paused || physics.LastErrorCode != 0) return;
            var p = e.GetPosition(newCanvas); physics.BeginDrag(physics.SelectedEntity, new(p.X, p.Y));
            dragging = true; newCanvas.CaptureMouse(); e.Handled = true;
        };
        newCanvas.MouseMove += (_, e) => { if (dragging) { var p = e.GetPosition(newCanvas); physics.MoveDrag(new(p.X, p.Y)); } };
        newCanvas.MouseLeftButtonUp += (_, _) => EndDrag();
        newCanvas.LostMouseCapture += (_, _) => { if (dragging) EndDrag(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { EndDrag(); physics.CancelInput(); facing = DangoFacingDirection.Center; return; }
            if (!paused && e.Key is Key.A or Key.D or Key.S or Key.Space) { SetKeys(); e.Handled = true; }
        };
        PreviewKeyUp += (_, e) => { if (!paused && e.Key is Key.A or Key.D or Key.S or Key.Space) { SetKeys(); e.Handled = true; } };
        Deactivated += (_, _) => { EndDrag(); physics.CancelInput(); facing = DangoFacingDirection.Center; };
        StateChanged += (_, _) => SetPaused(WindowState == WindowState.Minimized);
        timer.Tick += (_, _) => Tick();
        Closed += (_, _) => { closed = true; timer.Stop(); physics.Dispose(); router.Dispose(); };
        ready = true; RefreshEditor();
        if (autoStart) { Tick(); timer.Start(); }
    }

    private EyeSelection SelectedEye => selection.SelectedIndex == 1 ? EyeSelection.Left : selection.SelectedIndex == 2 ? EyeSelection.Right : EyeSelection.Both;
    private AttachmentChannel SelectedChannels => SelectedEye == EyeSelection.Left ? AttachmentChannel.LeftGaze | AttachmentChannel.LeftEyePose | AttachmentChannel.LeftBlink :
        SelectedEye == EyeSelection.Right ? AttachmentChannel.RightGaze | AttachmentChannel.RightEyePose | AttachmentChannel.RightBlink :
        AttachmentChannel.LeftGaze | AttachmentChannel.LeftEyePose | AttachmentChannel.LeftBlink | AttachmentChannel.RightGaze | AttachmentChannel.RightEyePose | AttachmentChannel.RightBlink;
    private static TextBlock Label(string text) => new() { Text = text, Margin = new(7), VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new(4), Padding = new(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => action(); return button;
    }
    private void AddTop(UIElement element) { DockPanel.SetDock(element, Dock.Top); surface.Children.Add(element); }
    private static void AddColumn(Grid columns, string title, Canvas canvas, int index)
    {
        var panel = new DockPanel { Margin = new(5) }; var label = Label(title); label.FontSize = 18;
        DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label); panel.Children.Add(new Viewbox { Child = canvas });
        Grid.SetColumn(panel, index); columns.Children.Add(panel);
    }
    private static bool IntrinsicallyClosed(EyeShape value) => value is EyeShape.NaturalClosed or EyeShape.Tight or EyeShape.Happy or EyeShape.Deadpan;
    private void RefreshAvailability()
    {
        var value = (EyeShape)shape.SelectedIndex;
        openness.IsEnabled = blinkButton.IsEnabled = closeButton.IsEnabled = openButton.IsEnabled = !IntrinsicallyClosed(value);
        strength.IsEnabled = value == EyeShape.Soft;
        shape.ToolTip = IntrinsicallyClosed(value) ? "该造型本身已闭眼，选择圆眼可恢复开合" : "每种表情使用独立绘制的轮廓";
    }
    private void RefreshEditor()
    {
        ready = false;
        var pose = SelectedEye == EyeSelection.Right ? rightPose : leftPose;
        shape.SelectedIndex = (int)pose.Shape; openness.Value = pose.Openness; strength.Value = pose.Strength;
        ready = true; RefreshAvailability();
    }
    private void SendPose() { if (ready) Submit(new SetEyes(SelectedEye, openness.Value, (EyeShape)shape.SelectedIndex, strength.Value)); }
    private void Submit(AttachmentIntent intent)
    {
        var response = router.Submit(target, token, ++sequence, intent);
        if (response.Accepted && intent is SetEyes pose)
        {
            if (pose.Eye.HasFlag(EyeSelection.Left)) leftPose = pose with { Eye = EyeSelection.Left };
            if (pose.Eye.HasFlag(EyeSelection.Right)) rightPose = pose with { Eye = EyeSelection.Right };
        }
        if (response.Accepted && intent is Release release)
        {
            if (release.Mask.HasFlag(AttachmentChannel.LeftEyePose)) leftPose = new(EyeSelection.Left, 1);
            if (release.Mask.HasFlag(AttachmentChannel.RightEyePose)) rightPose = new(EyeSelection.Right, 1);
            RefreshEditor();
        }
        lastRequest = response.Accepted ? "动作已接纳" : $"动作未接纳：{response.Reason}";
        if (paused) status.Text = "已暂停；" + lastRequest;
    }
    private void SetKeys()
    {
        var left = Keyboard.IsKeyDown(Key.A); var right = Keyboard.IsKeyDown(Key.D);
        facing = left == right ? DangoFacingDirection.Center : left ? DangoFacingDirection.Left : DangoFacingDirection.Right;
        physics.SetInput(new(left, right, Keyboard.IsKeyDown(Key.Space), Keyboard.IsKeyDown(Key.S)));
    }
    private void EndDrag() { if (!dragging) return; dragging = false; physics.EndDrag(); newCanvas.ReleaseMouseCapture(); }
    private void SetPaused(bool value)
    {
        if (closed) return;
        EndDrag(); facing = DangoFacingDirection.Center; paused = value; physics.SetPaused(value); router.SetPaused(value); controller.Paused = value;
        status.Text = value ? "已暂停；恢复后不补算隐藏时间。" : "已恢复。";
    }
    private void ResetAttachments()
    {
        router.Dispose(); router = new(); target = router.Add(physics.SelectedEntity); token = router.RegisterPreview(target);
        controller = new(router, target); sequence = 0; step = 0; facing = DangoFacingDirection.Center;
    }
    private void ResetAll()
    {
        EndDrag(); physics.Reset(); ResetAttachments(); paused = false; physics.SetPaused(false);
        leftPose = new(EyeSelection.Left, 1); rightPose = new(EyeSelection.Right, 1); RefreshEditor(); lastRequest = "已重置";
    }
    private void Tick()
    {
        if (paused || closed) return;
        try
        {
            var frame = physics.Step(FixedStep);
            if (!frame.Physics.Succeeded) { SetPaused(true); status.Text = $"物理故障已冻结：{frame.Physics.ErrorCode}。请显式重置。"; return; }
            var body = frame.Bodies.Single();
            var eyes = controller.Update(body.Presentation, step++, FixedStep.TotalSeconds, facing)!;
            oldBody.ApplyFrame(body.Presentation); newBody.ApplyFrame(body.Presentation with { Attachments = eyes });
            foreach (var view in new[] { oldView, newView }) { Canvas.SetLeft(view, body.Bounds.X); Canvas.SetTop(view, body.Bounds.Y); }
            var results = router.ConsumeResults(target);
            if (results.Count > 0) lastRequest = string.Join("；", results.Select(r => r.Status == AttachmentEventStatus.Completed ? "眨眼已完成" : "眨眼已取消"));
            var safety = eyes.Face.Safety switch { "Normal" => "自然布局", "GazeReduced" => "注视幅度已收拢", "SpacingReduced" => "眼距已收拢", "SizeReduced" => "眼睛已适当缩小", "ClosedFallback" => "空间不足，暂时闭眼", _ => "空间不足，暂时隐藏眼睛" };
            status.Text = $"{lastRequest}。左眼 {Describe(eyes.Face.Left)}，右眼 {Describe(eyes.Face.Right)}。\n{safety}。请检查两眼独立、贴脸和恢复是否自然。";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException or Dango.Physics.DangoPhysicsException)
        { SetPaused(true); status.Text = "预览已停止更新：" + error.Message; }
    }

    private static string Describe(EyePresentationFrame eye) => ShapeNames[(int)eye.Shape] + (IntrinsicallyClosed(eye.Shape) ? "（闭合）" : $" {eye.Openness:P0}");

    internal void Export(string directory)
    {
        if (Directory.Exists(directory)) throw new IOException("Eye review evidence already exists.");
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 240; i++) Tick();
        Save("neutral");
        Submit(new SetGaze(EyeSelection.Left, -1, 1)); Submit(new SetGaze(EyeSelection.Right, 1, -1));
        for (var i = 0; i < 60; i++) Tick();
        Submit(Blink.Default(EyeSelection.Left));
        for (var i = 0; i < 5; i++) Tick();
        Save("left-blink");
        Submit(new SetEyes(EyeSelection.Right, 0.45, EyeShape.Soft, 1));
        physics.SetInput(new(false, false, false, true));
        for (var i = 0; i < 45; i++) Tick();
        Save("squash-soft");
        physics.CancelInput();
        for (var i = 0; i < 90; i++) Tick();
        Save("recovered");
        Submit(new SetGaze(EyeSelection.Both, 0, 0));
        foreach (var expression in Enum.GetValues<EyeShape>())
        {
            Submit(new SetEyes(EyeSelection.Both, 1, expression, 1));
            for (var i = 0; i < 90; i++) Tick();
            RefreshEditor(); Save("expression-" + expression);
        }
        foreach (var amount in new[] { 1d, 0.75, 0.5, 0.2, 0 })
        {
            Submit(new SetEyes(EyeSelection.Both, amount));
            for (var i = 0; i < 90; i++) Tick();
            RefreshEditor(); Save("openness-" + (int)(amount * 100));
        }
        void Save(string name)
        {
            if (paused || controller.Current is null) throw new InvalidOperationException(status.Text);
            surface.Measure(new(1050, 690)); surface.Arrange(new Rect(0, 0, 1050, 690)); surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1050, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(Path.Combine(directory, name + ".png"), FileMode.CreateNew); encoder.Save(output);
            File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(controller.Current));
        }
    }
}
