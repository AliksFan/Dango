using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;
using ShapePath = System.Windows.Shapes.Path;
using Path = System.IO.Path;

namespace Dango.JointReview;

/// <summary>Isolated single-player acceptance entry; the editor never replaces its scene session.</summary>
internal sealed class FurnitureLayoutWindow : Window
{
    private enum Tool { Furniture, Glove }
    private enum Gesture { None, Furniture, Glove, Camera }
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private readonly string module;
    private readonly string dataRoot;
    private readonly Canvas scene = new() { Background = ColorBrush(250, 246, 239), ClipToBounds = true, Focusable = true };
    private readonly DeformableDangoVisual body = new() { ShowHat = false, IsHitTestVisible = false };
    private readonly DangoHatVisual hat = new() { IsHitTestVisible = false };
    private readonly Image platformImage = new() { Source = CreatePlatformImage(), Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Image ghostImage = new() { Source = CreatePlatformImage(), Stretch = Stretch.Fill, Opacity = .42, IsHitTestVisible = false };
    private readonly Rectangle selectionOutline = new() { Stroke = ColorBrush(96, 122, 76), StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };
    private readonly Rectangle ghostOutline = new() { StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };
    private readonly Line floor = new() { Stroke = ColorBrush(197, 162, 122), StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Line leftWall = new() { Stroke = ColorBrush(224, 210, 189), StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Line rightWall = new() { Stroke = ColorBrush(224, 210, 189), StrokeThickness = 1, IsHitTestVisible = false };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = ColorBrush(100, 98, 87), Margin = new(16, 8, 16, 10), MinHeight = 36 };
    private readonly TextBlock modeLabel = new() { FontSize = 13, Foreground = ColorBrush(92, 109, 73), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel sidebar = new() { Width = 182, Margin = new(14, 16, 14, 14) };
    private readonly StackPanel editTools = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock parameterSelection = new() { Text = "木平台", Foreground = ColorBrush(118, 112, 99), Margin = new(0, 0, 0, 10) };
    private readonly List<Button> nudgeButtons = [];
    private readonly Button furnitureButton;
    private readonly Button gloveButton;
    private readonly Button deleteButton;
    private readonly Button confirmButton;
    private readonly Button cancelButton;
    private readonly Button exitButton;
    private readonly Button editButton;
    private readonly Button recoverButton;
    private readonly Button platformButton;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = FixedStep };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Stopwatch observationClock = new();
    private readonly HashSet<Key> keys = [];
    private readonly DangoPhysicsPresentationAdapter presenter = new(DangoPhysicsDefaults.Current);
    private readonly List<double> stepCosts = [];
    private readonly List<double> commandCosts = [];
    private readonly string? evidenceDirectory;
    private readonly bool observe;
    private readonly StreamWriter? observations;
    private readonly Process process = Process.GetCurrentProcess();
    private FurnitureLayoutSceneSession? session;
    private DangoBodyState? nativeBody;
    private PhysicsPresentationFrame? presentation;
    private Geometry? displayedGeometry;
    private DangoFacingDirection facing;
    private Tool tool;
    private Gesture gesture;
    private Point lastPointer, dragOffset, requestedGloveTarget, gloveTarget;
    private FurnitureAnchor? requestedAnchor;
    private double cameraX = FurnitureLayoutProjection.InitialCameraOffsetX, cameraY, lastTick, accumulator, nextNudge, nextPreview;
    private double activeSeconds, droppedSeconds, nextObservation = 1;
    private double nextCycle = 10;
    private int editCycles, renderedFrames, physicsSteps, schedulerCallbacks;
    private bool editing = true, selected, jumpHeld, closing, closeAfterCommand, localFault;
    private string? failure;
    private long peakWorkingSet;

    internal int ExitCode { get; private set; }
    private FurnitureLayoutProjection Projection
    {
        get
        {
            var projection = new FurnitureLayoutProjection(scene.ActualWidth, scene.ActualHeight, 0, 0);
            // Store the camera offset in world units so resize/DPI changes preserve its center.
            return projection with { PanX = cameraX * projection.HorizontalScale, PanY = cameraY * projection.VerticalScale };
        }
    }

    internal FurnitureLayoutWindow(string module, string dataRoot, string? evidenceDirectory = null, bool observe = false)
    {
        this.module = Path.GetFullPath(module);
        this.dataRoot = Path.GetFullPath(dataRoot);
        this.observe = observe;
        if (observe && evidenceDirectory is null) throw new ArgumentException("定时观察需要独立证据目录。", nameof(evidenceDirectory));
        if (evidenceDirectory is not null)
        {
            this.evidenceDirectory = Path.GetFullPath(evidenceDirectory);
            if (Directory.Exists(this.evidenceDirectory) || File.Exists(this.evidenceDirectory))
                throw new IOException("观察记录必须使用不存在的新目录。");
            Directory.CreateDirectory(this.evidenceDirectory);
            observations = new(new FileStream(Path.Combine(this.evidenceDirectory, "window-observation.jsonl"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
        Title = "Dango · 家具布置（独立单人验收）";
        Width = 1120; Height = 760; MinWidth = 820; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ColorBrush(255, 253, 249);

        furnitureButton = IconButton("FurnitureSelectTool", "家具选择与拖动", "M5,4 L5,23 L11,17 L16,26 L20,24 L15,15 L25,15 Z", () => ChangeTool(Tool.Furniture));
        gloveButton = IconButton("FurnitureGloveTool", "手套：拖动自己的团子", "M9,16 L9,6 Q9,3 12,6 L12,14 L12,4 Q15,1 15,5 L15,14 L15,6 Q18,3 18,7 L18,15 L18,10 Q21,7 21,11 L21,22 Q21,27 15,28 Q11,28 8,24 L4,18 Q3,14 6,15 Z", () => ChangeTool(Tool.Glove));
        deleteButton = IconButton("FurnitureDeleteButton", "删除已选家具", "M7,9 L8,26 L22,26 L23,9 M5,8 L25,8 M11,8 L11,4 L19,4 L19,8 M12,12 L12,22 M18,12 L18,22", () => StartCommand(() => session!.DeleteAsync()));
        confirmButton = IconButton("FurnitureConfirmButton", "确认家具预览", "M5,16 L12,23 L25,7", () => StartCommand(() => session!.ConfirmAsync()));
        cancelButton = IconButton("FurnitureCancelPreviewButton", "取消未确认预览", "M7,7 L23,23 M23,7 L7,23", CancelPreview);
        exitButton = IconButton("FurnitureExitEditButton", "结束布置，保留已确认操作", "M14,5 L5,5 L5,25 L14,25 M13,15 L27,15 M21,9 L27,15 L21,21", () => SetEditing(false));
        editButton = IconButton("FurnitureEnterEditButton", "布置家具", "M6,22 L5,27 L11,26 L25,12 L20,7 Z M18,9 L23,14", () => SetEditing(true));
        recoverButton = IconButton("FurnitureRecoverButton", "重新确认保存与房间状态", "M25,13 A10,10 0 1 0 24,22 M25,4 L25,13 L16,13", () => StartCommand(() => session!.RecoverAsync()));
        platformButton = IconButton("FurnitureCatalogPlatform", "木平台：选择或添加预览", null, ChoosePlatform);
        platformButton.Content = new Image { Source = CreatePlatformImage(), Width = 58, Height = 12, Stretch = Stretch.Fill };
        platformButton.Width = 72; platformButton.Height = 68;

        BuildSurface();
        AutomationProperties.SetName(scene, "家具与团子真实单人场景");
        AutomationProperties.SetAutomationId(scene, "FurnitureLayoutScene");
        var focusBorder = new FrameworkElementFactory(typeof(Border));
        focusBorder.SetValue(Border.BorderBrushProperty, ColorBrush(110, 133, 86));
        focusBorder.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        focusBorder.SetValue(Border.MarginProperty, new Thickness(3));
        var focusStyle = new Style(typeof(Control));
        focusStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = focusBorder }));
        scene.FocusVisualStyle = focusStyle;
        InputMethod.SetIsInputMethodEnabled(scene, false);
        foreach (var element in new UIElement[] { floor, leftWall, rightWall, platformImage, selectionOutline, ghostImage, ghostOutline, body, hat }) scene.Children.Add(element);
        Panel.SetZIndex(body, 10); Panel.SetZIndex(hat, 20); Panel.SetZIndex(ghostImage, 30); Panel.SetZIndex(ghostOutline, 31);
        scene.MouseLeftButtonDown += OnLeftDown;
        scene.MouseLeftButtonUp += OnLeftUp;
        scene.MouseRightButtonDown += OnRightDown;
        scene.MouseRightButtonUp += (_, e) => { if (gesture == Gesture.Camera) { ClearGesture(); e.Handled = true; } };
        scene.MouseMove += OnPointerMove;
        scene.LostMouseCapture += (_, _) => ClearInput();
        scene.LostKeyboardFocus += (_, _) => ClearInput();
        scene.SizeChanged += (_, _) => { ClearInput(); SafeDraw(); };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => ClearInput();
        DpiChanged += (_, _) => { ClearInput(); SafeDraw(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearInput(); ResetClock(); };
        StateChanged += (_, _) => { ClearInput(); ResetClock(); };
        Loaded += (_, _) => LoadScene();
        Closing += OnClosing;
        Closed += (_, _) => Cleanup();
        timer.Tick += OnTick;
        UpdateControls();
    }

    private void BuildSurface()
    {
        var root = new DockPanel(); Content = root;
        var heading = new TextBlock { Text = "团子的房间", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new(18, 14, 18, 12), Foreground = ColorBrush(74, 67, 54) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var toolbar = new DockPanel { Margin = new(18, 0, 12, 10), LastChildFill = false };
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(modeLabel, Dock.Left); toolbar.Children.Add(modeLabel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Right); toolbar.Children.Add(actions);
        foreach (var button in new[] { furnitureButton, gloveButton, deleteButton, confirmButton, cancelButton }) editTools.Children.Add(button);
        actions.Children.Add(editTools); actions.Children.Add(recoverButton); actions.Children.Add(exitButton); actions.Children.Add(editButton);
        var footer = new TextBlock { Text = "A / D 移动 · S 下压 · Space 跳跃 · 右键拖动视角  |  独立单人", FontSize = 11, Foreground = ColorBrush(130, 123, 109), Margin = new(16, 0, 16, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var center = new Grid(); center.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); center.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(center);
        var sideBorder = new Border { Background = ColorBrush(248, 246, 240), BorderBrush = ColorBrush(229, 224, 213), BorderThickness = new(0, 1, 1, 0), Child = sidebar };
        center.Children.Add(sideBorder);
        var sceneBorder = new Border { BorderBrush = ColorBrush(229, 224, 213), BorderThickness = new(0, 1, 0, 0), Child = scene };
        Grid.SetColumn(sceneBorder, 1); center.Children.Add(sceneBorder);
        sidebar.Children.Add(SectionTitle("微调"));
        var nudge = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 3, 0, 10) };
        for (var i = 0; i < 3; i++) { nudge.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); nudge.RowDefinitions.Add(new() { Height = new GridLength(38) }); }
        AddNudge(nudge, "FurnitureNudgeUp", "向上微调家具预览", "M6,18 L15,9 L24,18", 1, 0, 0, 1);
        AddNudge(nudge, "FurnitureNudgeLeft", "向左微调家具预览", "M18,6 L9,15 L18,24", 0, 1, -1, 0);
        AddNudge(nudge, "FurnitureNudgeRight", "向右微调家具预览", "M12,6 L21,15 L12,24", 2, 1, 1, 0);
        AddNudge(nudge, "FurnitureNudgeDown", "向下微调家具预览", "M6,12 L15,21 L24,12", 1, 2, 0, -1);
        sidebar.Children.Add(nudge); sidebar.Children.Add(Divider());
        sidebar.Children.Add(SectionTitle("家具参数")); sidebar.Children.Add(parameterSelection);
        sidebar.Children.Add(new TextBlock { Text = "大小", Foreground = ColorBrush(126, 120, 107), FontSize = 12 });
        var size = new Slider { IsEnabled = false, Minimum = 0, Maximum = 100, Value = 50, Margin = new(0, 8, 0, 0), ToolTip = "参数调节后续接入" };
        AutomationProperties.SetName(size, "平台大小，后续接入"); sidebar.Children.Add(size);
        sidebar.Children.Add(new TextBlock { Text = "后续可调", FontSize = 11, Foreground = ColorBrush(157, 148, 131), Margin = new(0, 4, 0, 16) });
        sidebar.Children.Add(Divider()); sidebar.Children.Add(SectionTitle("家具仓库"));
        var warehouse = new Grid(); warehouse.ColumnDefinitions.Add(new()); warehouse.ColumnDefinitions.Add(new());
        warehouse.Children.Add(platformButton);
        var ball = IconButton("FurnitureCatalogBall", "圆球，后续接入", null, () => { });
        ball.Width = 72; ball.Height = 68; ball.IsEnabled = false;
        ball.Content = new Ellipse { Width = 30, Height = 30, Fill = ColorBrush(205, 167, 149), Stroke = ColorBrush(173, 131, 113), StrokeThickness = 1 };
        Grid.SetColumn(ball, 1); warehouse.Children.Add(ball); sidebar.Children.Add(warehouse);
    }

    private static TextBlock SectionTitle(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = ColorBrush(88, 80, 63), Margin = new(0, 0, 0, 12) };
    private static Border Divider() => new() { BorderBrush = ColorBrush(229, 224, 213), BorderThickness = new(0, 1, 0, 0), Margin = new(0, 3, 0, 16) };
    private static SolidColorBrush ColorBrush(byte red, byte green, byte blue) => new(Color.FromRgb(red, green, blue));

    private Button IconButton(string id, string name, string? data, Action action)
    {
        var button = new Button { Width = 36, Height = 36, Margin = new(3, 0, 3, 0), Padding = new(5), Background = ColorBrush(255, 253, 249), BorderBrush = ColorBrush(224, 218, 203), BorderThickness = new(1), ToolTip = name };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, name);
        if (data is not null)
            button.Content = new Viewbox { Width = 23, Height = 23, Child = new ShapePath { Data = Geometry.Parse(data), Stroke = ColorBrush(94, 98, 79), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform } };
        button.Click += (_, _) => { ClearInput(); try { action(); UpdateControls(); SafeDraw(); } catch (Exception error) { StopWithFailure(error); } };
        return button;
    }

    private void AddNudge(Grid grid, string id, string name, string data, int column, int row, int dx, int dy)
    {
        var button = IconButton(id, name, data, () => Nudge(dx, dy));
        nudgeButtons.Add(button); Grid.SetColumn(button, column); Grid.SetRow(button, row); grid.Children.Add(button);
    }

    private void LoadScene()
    {
        try
        {
            session = new(module, dataRoot); selected = session.Layout.Platform is not null;
            nativeBody = session.LastFrame?.Bodies.Single();
            if (nativeBody is not null) Present(nativeBody);
            ResetClock(); UpdateControls(); Draw(); observationClock.Start(); timer.Start(); Keyboard.Focus(scene);
        }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void ChangeTool(Tool value) { ClearInput(); tool = value; }
    private void SetEditing(bool value)
    {
        if (session?.Busy == true || editing == value) return;
        ClearInput(); session?.CancelPreview(); selected = session?.Layout.Platform is not null; editing = value;
        // This changes UI ownership only. The same body and world continue to tick.
        UpdateControls(); Draw();
    }

    private void ChoosePlatform()
    {
        if (!CanInteract) return;
        tool = Tool.Furniture; selected = true;
        if (session!.Layout.Platform is null && !session.HasPreview)
            session.PreviewPlatform(LocalFurnitureSceneGeometry.DefaultAnchor.X, LocalFurnitureSceneGeometry.DefaultAnchor.Y);
    }

    private void CancelPreview()
    {
        if (session?.Busy == true) return;
        ClearGesture(); session?.CancelPreview(); selected = session?.Layout.Platform is not null;
    }
    private bool CanInteract => editing && !localFault && session is { CanEdit: true, Busy: false };

    private void Nudge(int dx, int dy)
    {
        if (!CanInteract || !selected || clock.Elapsed.TotalSeconds < nextNudge) return;
        var platform = session!.Preview ?? session.Layout.Platform;
        if (platform is null) return;
        nextNudge = clock.Elapsed.TotalSeconds + 1d / LocalFurnitureSceneGeometry.MaxNudgePerSecond;
        session.PreviewPlatform(platform.Anchor.X + dx * LocalFurnitureSceneGeometry.NudgeStep,
            platform.Anchor.Y + dy * LocalFurnitureSceneGeometry.NudgeStep);
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (gesture != Gesture.None || e.RightButton == MouseButtonState.Pressed || localFault) return;
        Keyboard.Focus(scene); e.Handled = true;
        if (!editing || session is null) return;
        var point = e.GetPosition(scene); var projection = Projection;
        if (tool == Tool.Glove)
        {
            if (!session.CanStep || nativeBody is null || displayedGeometry is null) return;
            var core = projection.ToScreen(nativeBody.CoreX, nativeBody.CoreY);
            var local = new Point((point.X - core.X) / projection.VisualScale + 80,
                (point.Y - core.Y) / projection.VisualScale + 66);
            if (!displayedGeometry.FillContains(local)) return;
            // First select the actual click point. Subsequent targets are limited to 8 p-units per second.
            gloveTarget = requestedGloveTarget = FurnitureLayoutProjection.ClampGloveTarget(projection.ToWorld(point));
            gesture = Gesture.Glove;
        }
        else if (CanInteract)
        {
            var platform = session.Preview is { } preview && projection.PlatformRect(preview.Anchor).Contains(point)
                ? preview : session.Layout.Platform is { } current && projection.PlatformRect(current.Anchor).Contains(point) ? current : null;
            if (platform is null) { selected = false; UpdateControls(); Draw(); return; }
            selected = true;
            var world = projection.ToWorld(point);
            dragOffset = new(platform.Anchor.X - world.X, platform.Anchor.Y - world.Y);
            gesture = Gesture.Furniture;
        }
        if (gesture != Gesture.None && !scene.CaptureMouse()) ClearGesture();
        UpdateControls(); Draw();
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (gesture is not (Gesture.Furniture or Gesture.Glove)) return;
        if (gesture == Gesture.Furniture) ApplyRequestedPreview();
        ClearGesture(); e.Handled = true; UpdateControls(); SafeDraw();
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        if (gesture != Gesture.None || e.LeftButton == MouseButtonState.Pressed || localFault) return;
        Keyboard.Focus(scene); lastPointer = e.GetPosition(scene); gesture = Gesture.Camera;
        if (!scene.CaptureMouse()) ClearGesture(); e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!scene.IsMouseCaptured) return;
        var point = e.GetPosition(scene);
        if (gesture == Gesture.Camera && e.RightButton == MouseButtonState.Pressed)
        {
            var delta = point - lastPointer; lastPointer = point;
            var projection = Projection;
            cameraX = Math.Clamp(cameraX + delta.X / projection.HorizontalScale,
                -scene.ActualWidth / projection.HorizontalScale, scene.ActualWidth / projection.HorizontalScale);
            cameraY = Math.Clamp(cameraY + delta.Y / projection.VerticalScale,
                -scene.ActualHeight / projection.VerticalScale, scene.ActualHeight / projection.VerticalScale);
            SafeDraw(); e.Handled = true;
        }
        else if (gesture == Gesture.Furniture && e.LeftButton == MouseButtonState.Pressed)
        {
            var world = Projection.ToWorld(point); requestedAnchor = new(world.X + dragOffset.X, world.Y + dragOffset.Y);
            e.Handled = true;
        }
        else if (gesture == Gesture.Glove && e.LeftButton == MouseButtonState.Pressed)
        { requestedGloveTarget = FurnitureLayoutProjection.ClampGloveTarget(Projection.ToWorld(point)); e.Handled = true; }
        else ClearGesture();
    }

    private void ApplyRequestedPreview()
    {
        if (requestedAnchor is not { } anchor || !CanInteract) return;
        requestedAnchor = null; session!.PreviewPlatform(anchor.X, anchor.Y);
        nextPreview = clock.Elapsed.TotalSeconds + 1d / 20;
    }

    private void ClearGesture()
    {
        gesture = Gesture.None; requestedAnchor = null; gloveTarget = requestedGloveTarget = default;
        if (scene.IsMouseCaptured) scene.ReleaseMouseCapture();
    }
    private void ClearInput() { keys.Clear(); jumpHeld = false; ClearGesture(); }
    private void ResetClock() { accumulator = 0; lastTick = clock.Elapsed.TotalSeconds; }
    private static bool IsActionKey(Key key) => key is Key.A or Key.D or Key.S or Key.Space;
    private static bool IsArrow(Key key) => key is Key.Up or Key.Down or Key.Left or Key.Right;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.IsRepeat)
        {
            if (session?.Busy == true) { e.Handled = true; return; }
            if (gesture != Gesture.None || session?.HasPreview == true) { ClearInput(); CancelPreview(); }
            else if (editing) SetEditing(false);
            else SetEditing(true);
            e.Handled = true; UpdateControls(); SafeDraw(); return;
        }
        if (!FurnitureLayoutInputPolicy.HasSceneFocus(this, scene, Keyboard.FocusedElement)) return;
        if (IsActionKey(e.Key) || IsArrow(e.Key))
        {
            e.Handled = true;
            if (!localFault && session?.CanStep == true && (!e.IsRepeat || keys.Contains(e.Key))) keys.Add(e.Key);
        }
        else if (e.Key == Key.Enter && editing && !e.IsRepeat)
        {
            e.Handled = true;
            if (session is { CanConfirm: true, Busy: false }) StartCommand(() => session.ConfirmAsync());
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!IsActionKey(e.Key) && !IsArrow(e.Key)) return;
        keys.Remove(e.Key);
        if (FurnitureLayoutInputPolicy.HasSceneFocus(this, scene, Keyboard.FocusedElement)) e.Handled = true;
    }

    private async void StartCommand(Func<Task> command)
    {
        if (session is null || session.Busy || localFault) return;
        ClearInput(); var started = Stopwatch.GetTimestamp();
        try
        {
            var pending = command(); UpdateControls(); await pending;
            selected = session.Layout.Platform is not null || session.HasPreview;
        }
        catch (Exception error) { StopWithFailure(error); }
        finally
        {
            commandCosts.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            UpdateControls(); SafeDraw();
            if (closeAfterCommand) Close();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (closing) return;
        schedulerCallbacks++;
        try
        {
            if (session is { Busy: true } && (localFault || !IsVisible || WindowState == WindowState.Minimized || !session.CanStep))
            {
                // A render failure must not strand an already staged commit or dispose its lease early.
                session.Tick(0, false, false, false, 0, 0); ResetClock(); UpdateControls(); Observe(); return;
            }
            if (localFault || !IsVisible || WindowState == WindowState.Minimized || session?.CanStep != true)
            { ResetClock(); Observe(); return; }
            var now = clock.Elapsed.TotalSeconds; var delta = Math.Max(0, now - lastTick); lastTick = now;
            activeSeconds += delta;
            if (delta > .25) { droppedSeconds += delta - .25; delta = .25; }
            accumulator += delta;
            if (requestedAnchor is not null && now >= nextPreview) ApplyRequestedPreview();
            if (editing && tool == Tool.Furniture && gesture == Gesture.None)
            {
                var dx = (keys.Contains(Key.Right) ? 1 : 0) - (keys.Contains(Key.Left) ? 1 : 0);
                var dy = (keys.Contains(Key.Up) ? 1 : 0) - (keys.Contains(Key.Down) ? 1 : 0);
                if (dx != 0 || dy != 0) Nudge(dx, dy);
            }
            var count = 0;
            while (accumulator + 1e-9 >= FixedStep.TotalSeconds && count < 4 && session.CanStep)
            {
                var horizontal = (keys.Contains(Key.D) ? 1 : 0) - (keys.Contains(Key.A) ? 1 : 0);
                var jump = keys.Contains(Key.Space) && !jumpHeld; jumpHeld = keys.Contains(Key.Space);
                if (horizontal != 0) facing = horizontal < 0 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
                if (gesture == Gesture.Glove) gloveTarget = FurnitureLayoutProjection.LimitGloveTarget(gloveTarget, requestedGloveTarget, FixedStep.TotalSeconds);
                var started = Stopwatch.GetTimestamp();
                var state = session.Tick(horizontal, keys.Contains(Key.S), jump, gesture == Gesture.Glove, gloveTarget.X, gloveTarget.Y);
                if (stepCosts.Count < 60_000) stepCosts.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (session.LastFrame is { Succeeded: false }) throw new InvalidOperationException("物理状态异常，保留最后画面。");
                Present(state); physicsSteps++; count++; accumulator -= FixedStep.TotalSeconds;
            }
            if (count == 4 && accumulator >= FixedStep.TotalSeconds) { droppedSeconds += accumulator; accumulator = 0; }
            if (count > 0) { Draw(); renderedFrames++; }
            UpdateControls(); Observe();
        }
        catch (Exception error) { StopWithFailure(error); }
    }

    private void Present(DangoBodyState state)
    {
        if (state.SignedArea < .42 || state.MaximumSpeed > 12 + 1e-5)
            throw new InvalidDataException("团子物理状态超过面积或速度限制。");
        var frame = presenter.Present(state, FixedStep, facing);
        var geometry = DangoStreamGeometryFactory.TryCreate(frame.Contour, 160, 120);
        if (frame.FallbackReason != PhysicsPresentationFallbackReason.None || !geometry.Succeeded)
            throw new InvalidDataException("团子绘制轮廓无效，保留最后画面。");
        nativeBody = state; presentation = frame; displayedGeometry = geometry.Geometry;
    }

    private void SafeDraw() { try { Draw(); } catch (Exception error) { StopWithFailure(error); } }
    private void Draw()
    {
        var projection = Projection;
        var groundLeft = projection.ToScreen(LocalFurnitureSceneGeometry.Left, LocalFurnitureSceneGeometry.Ground);
        var groundRight = projection.ToScreen(LocalFurnitureSceneGeometry.Right, LocalFurnitureSceneGeometry.Ground);
        floor.X1 = groundLeft.X; floor.X2 = groundRight.X; floor.Y1 = floor.Y2 = groundLeft.Y;
        DrawWall(leftWall, LocalFurnitureSceneGeometry.Left); DrawWall(rightWall, LocalFurnitureSceneGeometry.Right);
        SetPlatformVisual(platformImage, session?.Layout.Platform?.Anchor);
        var current = session?.Layout.Platform;
        SetRectangle(selectionOutline, current?.Anchor);
        selectionOutline.Visibility = editing && selected && current is not null && session?.HasPreview != true ? Visibility.Visible : Visibility.Hidden;
        var preview = session?.Preview;
        SetPlatformVisual(ghostImage, preview?.Anchor); SetRectangle(ghostOutline, preview?.Anchor);
        ghostOutline.Stroke = session?.CanConfirm == true ? ColorBrush(96, 122, 76) : ColorBrush(188, 96, 72);
        if (presentation is null || nativeBody is null) { body.Visibility = hat.Visibility = Visibility.Hidden; return; }
        var core = projection.ToScreen(nativeBody.CoreX, nativeBody.CoreY);
        var transform = new ScaleTransform(projection.VisualScale, projection.VisualScale);
        foreach (var visual in new FrameworkElement[] { body, hat })
        {
            visual.RenderTransform = transform;
            Canvas.SetLeft(visual, core.X - 80 * projection.VisualScale); Canvas.SetTop(visual, core.Y - 66 * projection.VisualScale);
            visual.Visibility = Visibility.Visible;
        }
        body.ApplyFrame(presentation); hat.ApplyFrame(presentation);
        if (body.LastContourFailure != DangoContourFailure.None) throw new InvalidDataException("团子绘制轮廓校验失败。");
        scene.Cursor = gesture == Gesture.Camera ? Cursors.ScrollAll : tool == Tool.Glove && editing ? Cursors.Hand : Cursors.Arrow;

        void DrawWall(Line line, double x)
        {
            var top = projection.ToScreen(x, LocalFurnitureSceneGeometry.Ceiling); var bottom = projection.ToScreen(x, LocalFurnitureSceneGeometry.Ground);
            line.X1 = top.X; line.X2 = bottom.X; line.Y1 = top.Y; line.Y2 = bottom.Y;
        }
        void SetPlatformVisual(Image image, FurnitureAnchor? anchor)
        {
            image.Visibility = anchor is null ? Visibility.Hidden : Visibility.Visible;
            if (anchor is not { } value) return;
            var rectangle = projection.PlatformRect(value); image.Width = rectangle.Width; image.Height = rectangle.Height;
            Canvas.SetLeft(image, rectangle.Left); Canvas.SetTop(image, rectangle.Top);
        }
        void SetRectangle(Rectangle image, FurnitureAnchor? anchor)
        {
            image.Visibility = anchor is null ? Visibility.Hidden : Visibility.Visible;
            if (anchor is not { } value) return;
            var rectangle = projection.PlatformRect(value); image.Width = rectangle.Width; image.Height = rectangle.Height;
            Canvas.SetLeft(image, rectangle.Left); Canvas.SetTop(image, rectangle.Top);
        }
    }

    private void UpdateControls()
    {
        modeLabel.Text = editing ? "布置中 · 团子留场" : "游玩中";
        editTools.Visibility = exitButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        editButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        sidebar.IsEnabled = CanInteract;
        furnitureButton.IsEnabled = CanInteract; gloveButton.IsEnabled = editing && !localFault && session?.CanStep == true && session?.Busy != true;
        furnitureButton.Background = tool == Tool.Furniture ? ColorBrush(232, 237, 222) : ColorBrush(255, 253, 249);
        gloveButton.Background = tool == Tool.Glove ? ColorBrush(232, 237, 222) : ColorBrush(255, 253, 249);
        deleteButton.IsEnabled = CanInteract && selected && session?.Layout.Platform is not null;
        confirmButton.Visibility = cancelButton.Visibility = session?.HasPreview == true ? Visibility.Visible : Visibility.Collapsed;
        confirmButton.IsEnabled = CanInteract && session?.CanConfirm == true;
        cancelButton.IsEnabled = CanInteract;
        exitButton.IsEnabled = editButton.IsEnabled = session?.Busy != true;
        platformButton.IsEnabled = CanInteract;
        foreach (var button in nudgeButtons) button.IsEnabled = CanInteract && selected && (session?.Preview is not null || session?.Layout.Platform is not null);
        parameterSelection.Text = selected ? "木平台" : "未选择家具";
        recoverButton.Visibility = session?.CanRecover == true && !localFault ? Visibility.Visible : Visibility.Collapsed;
        recoverButton.IsEnabled = session?.Busy != true;
        status.Text = failure is not null ? $"已停止：{failure}" : session?.Status ?? "正在打开房间…";
        status.Foreground = localFault ? ColorBrush(169, 62, 47) : ColorBrush(100, 98, 87);
    }

    private void StopWithFailure(Exception error)
    {
        failure ??= error.Message; localFault = true; ExitCode = 1; ClearInput(); ResetClock(); UpdateControls();
        if (observe)
        {
            if (session?.Busy == true) closeAfterCommand = true;
            else Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (session?.Busy == true) { e.Cancel = true; closeAfterCommand = true; ClearInput(); }
    }

    private void Observe()
    {
        if (evidenceDirectory is null || session is null) return;
        if (observe && !session.CanStep && !session.Busy)
        { failure ??= session.Status; ExitCode = 1; Close(); return; }
        var seconds = observationClock.Elapsed.TotalSeconds;
        if (observe && seconds >= 660 && (activeSeconds < 560 || editCycles != 50))
        {
            failure ??= "660秒观察上限：未满足560秒活动时间或50次布置循环。";
            ExitCode = 1; ClearInput(); UpdateControls();
            if (session.Busy) closeAfterCommand = true;
            else Close();
            return;
        }
        if (observe && editCycles < 50 && seconds >= nextCycle && !session.Busy && !session.HasPreview && !localFault)
        {
            var before = session.LastFrame;
            SetEditing(false); SetEditing(true);
            if (!ReferenceEquals(before, session.LastFrame)) throw new InvalidOperationException("进入或退出布置改变了物理帧。");
            editCycles++; nextCycle = seconds + 10;
        }
        if (seconds >= nextObservation)
        {
            process.Refresh(); peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            observations!.WriteLine(JsonSerializer.Serialize(new
            {
                ElapsedSeconds = seconds, ActiveSeconds = activeSeconds, PhysicsSteps = physicsSteps,
                NewFrameRenders = renderedFrames, Editing = editing, EditCycles = editCycles,
                session.Entity, session.WorldGeneration, session.SceneRevision, session.PhysicsStep,
                session.Layout.LayoutVersion, session.HasPreview, session.Busy, session.CanStep,
                Visible = IsVisible, Minimized = WindowState == WindowState.Minimized,
                WorkingSetBytes = process.WorkingSet64, StepLastMilliseconds = stepCosts.LastOrDefault(),
                Gesture = gesture.ToString(), TimerOwners = timer.IsEnabled ? 1 : 0,
                MouseCaptured = scene.IsMouseCaptured, Failure = failure,
            }));
            nextObservation = seconds + 1;
        }
        if (observe && seconds >= 600 && activeSeconds >= 560 && editCycles == 50 && !session.Busy) Close();
    }

    private void Cleanup()
    {
        if (closing) return;
        closing = true; timer.Stop(); timer.Tick -= OnTick; ClearInput();
        try { session?.Dispose(); session = null; }
        catch (Exception error) { failure ??= error.Message; ExitCode = 1; }
        try
        {
            if (evidenceDirectory is not null)
            {
                if (observe && (observationClock.Elapsed.TotalSeconds < 600 || activeSeconds < 560 || editCycles != 50))
                { failure ??= "观察不足600秒墙钟、560秒活动时间或50次布置循环。"; ExitCode = 1; }
                var ordered = stepCosts.Order().ToArray();
                using var output = new FileStream(Path.Combine(evidenceDirectory, "window-observation-summary.json"), FileMode.CreateNew);
                JsonSerializer.Serialize(output, new
                {
                    ObservationMode = observe, ElapsedSeconds = observationClock.Elapsed.TotalSeconds, ActiveSeconds = activeSeconds,
                    PhysicsSteps = physicsSteps, NewFrameRenders = renderedFrames, EditCycles = editCycles,
                    SchedulerCallbacks = schedulerCallbacks, DroppedSeconds = droppedSeconds,
                    StepP95Milliseconds = ordered.Length == 0 ? 0 : ordered[(int)Math.Floor((ordered.Length - 1) * .95)],
                    StepMaximumMilliseconds = ordered.LastOrDefault(), CommandWallMaximumMilliseconds = commandCosts.DefaultIfEmpty().Max(),
                    PeakWorkingSetBytes = peakWorkingSet, SceneDisposed = session is null, OwnedWorldCount = FurnitureLayoutSceneSession.OwnedWorldCount,
                    TimerOwners = timer.IsEnabled ? 1 : 0,
                    MouseCaptureReleased = !scene.IsMouseCaptured, ExitCode, Failure = failure,
                    Measurement = "New native-state draws per runnable window time; not display composition FPS. Command wall time includes background staging; step cost includes final commit boundary.",
                    HardwareInputAndHumanAcceptance = "Not verified by programmatic edit cycles or timed observation",
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception error) { ExitCode = 1; Console.Error.WriteLine(error); }
        finally { observations?.Dispose(); process.Dispose(); }
    }

    internal void VerifyInputCleanup()
    {
        foreach (var clear in new Action[] { ClearInput, () => ChangeTool(Tool.Glove), () => SetEditing(!editing) })
        {
            keys.UnionWith([Key.A, Key.D, Key.S, Key.Space, Key.Up]); jumpHeld = true;
            gesture = Gesture.Glove; gloveTarget = requestedGloveTarget = new(2, 3);
            requestedAnchor = new(1, 2);
            clear();
            if (keys.Count != 0 || jumpHeld || gesture != Gesture.None || requestedAnchor is not null ||
                gloveTarget != default || requestedGloveTarget != default || scene.IsMouseCaptured)
                throw new InvalidOperationException("实际输入清理入口保留了持续键或指针状态。");
        }
    }

    private static DrawingImage CreatePlatformImage()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(ColorBrush(177, 122, 76), null, new RectangleGeometry(new Rect(0, 0, 340, 35), 5, 5)));
        drawing.Children.Add(new GeometryDrawing(ColorBrush(227, 180, 123), null, new RectangleGeometry(new Rect(0, 0, 340, 8), 4, 4)));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(ColorBrush(145, 93, 56), 1.5), Geometry.Parse("M12,20 L110,20 M125,17 L220,17 M235,24 L327,24")));
        drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
