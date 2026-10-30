using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;

namespace Dango.Desktop.Accessories.Editing;

/// <summary>
/// A temporary editor-owned world. Native work belongs solely to BeltBodyRuntime; this view
/// consumes its snapshots and uses the product presenter, including the real accessory solvers.
/// Camera changes never configure the body, change wearing values, or step native physics.
/// </summary>
internal sealed class AccessorySimulationView : Grid, IDisposable
{
    private const double InitialScale = 1.75;
    private readonly Canvas scene = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Line floorLine = new() { IsHitTestVisible = false, StrokeThickness = 1 / InitialScale };
    private readonly Canvas ghost = new() { IsHitTestVisible = false, Opacity = .45, Visibility = Visibility.Collapsed };
    private readonly BeltBodyAppearancePresenter presenter;
    private readonly Func<IBodyPreviewRuntime> createRuntime;
    private IBodyPreviewRuntime? owner;
    private readonly HomeBodyPreviewSession input = new();
    private IBodyPreviewRuntime runtime
    {
        get
        {
            owner ??= createRuntime();
            if (input.Runtime is null) input.Bind(owner);
            return owner;
        }
    }
    private readonly DangoAppearanceConfiguration appearance;
    private readonly DispatcherTimer timer;
    private AccessoryEditResult? content;
    private BeltBodySnapshot? snapshot, pendingDisplay;
    private IReadOnlyList<BeltBodyBoundaryPoint> boundary = [];
    private Task? initialization, refresh;
    private long presentationTick, renderStamp;
    private double scale = InitialScale, left, top;
    private Vector pan;
    private Point pointer, gestureStart;
    private bool requestedActive, active, disposed, dragging, panning, zooming, pointerPresent;
    private int lifecycle, activationGeneration = -1;
    private string tool = "default";

    internal AccessorySimulationView(DangoAppearanceConfiguration appearance, ProductPhysicsComposition? composition = null)
        : this(appearance, () => (composition ?? ProductPhysicsComposition.Default).CreatePreview()) { }

    internal AccessorySimulationView(DangoAppearanceConfiguration appearance, IBodyPreviewRuntime runtime)
        : this(appearance, () => runtime) { owner = runtime; }

    private AccessorySimulationView(DangoAppearanceConfiguration appearance, Func<IBodyPreviewRuntime> createRuntime)
    {
        this.appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        this.createRuntime = createRuntime ?? throw new ArgumentNullException(nameof(createRuntime));
        presenter = new(appearance) { GaitEnabled = true, Paused = true };
        // Resolve the same semantic resources as the surrounding editor; no independent theme.
        SetResourceReference(BackgroundProperty, "HomeBackground");
        ClipToBounds = true; Focusable = true; FocusVisualStyle = null;
        floorLine.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "HomeLine");
        Children.Add(scene); scene.Children.Add(floorLine); scene.Children.Add(presenter.View); scene.Children.Add(ghost);
        timer = new(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
        timer.Tick += OnTick;
        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
        MouseLeftButtonDown += OnPointerDown;
        MouseMove += OnPointerMove;
        MouseLeftButtonUp += OnPointerUp;
        MouseLeave += OnPointerLeave;
        LostMouseCapture += OnLostCapture;
        LostKeyboardFocus += OnLostKeyboardFocus;
        MouseWheel += OnWheel;
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        UpdateCamera();
    }

    internal event Action? Activated;
    internal event Action<double, double>? AnchorPicked;
    internal event Action<string>? Failed;
    internal event Action<string>? ToolChanged;
    internal FrameworkElement View => this;
    internal bool IsActive => active;
    internal bool IsDragging => dragging;
    internal bool IsPicking => tool == "pick";
    internal string Tool => tool;
    internal string? Error { get; private set; }
    internal BeltBodySnapshot? Snapshot => snapshot;
    internal BeltBodyAppearancePresenter Presenter => presenter;
    internal IBodyPreviewRuntime? Runtime => owner;
    internal Canvas AnchorGhost => ghost;
    internal double CameraScale => scale;
    internal Vector CameraPan => pan;

    internal void SetContent(AccessoryEditResult result)
    {
        VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(result);
        content = result;
        var item = result.Item with { IsEquipped = true, Order = 0 };
        presenter.SetAccessories(new([item]), id => id == result.Image.AssetId ? result.Image.Bitmap : null,
            _ => result.Project);
        if (presenter.AccessoryError is { } error) { Fail(new InvalidOperationException(error)); return; }
        BuildGhost();
        if (pointerPresent) UpdatePick(pointer);
        TryActivate();
    }

    internal void SetActive(bool value)
    {
        VerifyAccess();
        if (disposed || requestedActive == value) return;
        requestedActive = value;
        ++lifecycle;
        if (!value)
        {
            active = false; timer.Stop(); ClearInput(); CancelPick();
            presenter.Paused = true; pendingDisplay = null;
            if (owner is not null) Observe(owner.PauseAsync());
            return;
        }
        TryActivate();
    }

    private void TryActivate()
    {
        if (disposed || !requestedActive || ActualWidth <= 0 || ActualHeight <= 0 || active || activationGeneration == lifecycle) return;
        if (Error is not null) return;
        var generation = activationGeneration = lifecycle;
        initialization ??= ConfigureBodyAsync();
        Observe(ActivateAsync(generation));
    }

    private async Task ConfigureBodyAsync()
    {
        // A hide can invalidate an in-flight initial configure. It still applied the same
        // physical parameters; retry that revision on this owner rather than faulting reentry.
        while (true)
        {
            try { await runtime.ConfigureAsync(appearance.Body, 0, 0); return; }
            catch (OperationCanceledException) when (!disposed) { }
        }
    }

    private async Task ActivateAsync(int generation)
    {
        await initialization!;
        // All continuations return to this view's dispatcher, including detached test views.
        await Dispatcher.InvokeAsync(async () =>
        {
            if (disposed || !requestedActive || lifecycle != generation || Error is not null) return;
            await runtime.SetSolidWallsAsync(true);
            if (disposed || !requestedActive || lifecycle != generation) return;
            await SetWorldBoundsAsync();
            if (disposed || !requestedActive || lifecycle != generation) return;
            var resumed = await runtime.ResumeAsync();
            if (disposed || !requestedActive || lifecycle != generation) return;
            if (!resumed) throw new InvalidOperationException("模拟身体暂不可用。");
            presenter.Paused = false; active = true;
            renderStamp = Stopwatch.GetTimestamp(); timer.Start(); Activated?.Invoke();
        }).Task.Unwrap();
    }

    /// <summary>Resets this temporary body's pose while preserving content and camera.</summary>
    internal Task RefreshBodyAsync()
    {
        VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (refresh is { IsCompleted: false }) return refresh;
        if (!requestedActive && Error is null) return Task.CompletedTask;
        requestedActive = true;
        var generation = ++lifecycle;
        activationGeneration = generation;
        active = false; timer.Stop(); ClearInput();
        presenter.Paused = true; pendingDisplay = snapshot = null; boundary = [];
        Error = null;
        return refresh = RefreshBodyCoreAsync(generation);
    }

    private async Task RefreshBodyCoreAsync(int generation)
    {
        try
        {
            if (initialization is not null) await initialization;
            await Dispatcher.InvokeAsync(async () =>
            {
                if (disposed || !requestedActive || lifecycle != generation) return;
                await runtime.PauseAsync();
                if (disposed || !requestedActive || lifecycle != generation) return;
                await runtime.ResetPoseAsync();
                if (disposed || !requestedActive || lifecycle != generation) return;
                presenter.Reset();
                await ActivateAsync(generation);
                if (!disposed && requestedActive && lifecycle == generation && active) PresentFrame(1d / 60);
            }).Task.Unwrap();
        }
        catch (Exception error)
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!disposed && requestedActive && lifecycle == generation) Fail(error);
                });
        }
    }
    internal void SetTool(string value)
    {
        VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (value is not ("default" or "pick" or "pan" or "zoom")) throw new ArgumentException("未知模拟工具。", nameof(value));
        ClearInput(); tool = value;
        ghost.Visibility = Visibility.Collapsed;
        Cursor = value switch { "pick" => Cursors.Cross, "pan" => Cursors.Hand, "zoom" => Cursors.SizeNS, _ => Cursors.Arrow };
        if (value == "pick" && pointerPresent) UpdatePick(pointer);
        ToolChanged?.Invoke(tool);
    }

    internal void CancelPick()
    {
        VerifyAccess();
        if (tool == "pick") { tool = "default"; Cursor = Cursors.Arrow; ToolChanged?.Invoke(tool); }
        ghost.Visibility = Visibility.Collapsed;
    }

    internal void ClearInput()
    {
        VerifyAccess();
        dragging = panning = zooming = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (input.Runtime is not null) input.ClearInput(() => { });
        else owner?.ClearInput();
    }

    internal void PanBy(Vector delta)
    {
        VerifyAccess();
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
        pan += delta; UpdateCamera();
    }

    internal void ZoomAt(Point viewportPoint, double factor)
    {
        VerifyAccess();
        if (!Finite(viewportPoint) || !double.IsFinite(factor) || factor <= 0) return;
        var before = ViewportToScene(viewportPoint);
        scale = Math.Clamp(scale * factor, .35, 5);
        pan = (Vector)viewportPoint - new Vector(ActualWidth / 2, Floor) - (Vector)before * scale;
        UpdateCamera();
    }

    private double Floor => Math.Max(32, ActualHeight - 36);
    private double Ground => DangoBodyMaterial.Configure(appearance.Body).GroundHeight;
    internal Point ViewportToWorld(Point point)
    {
        var local = ViewportToScene(point);
        return new(local.X / BeltBodyAppearancePresenter.WorldToCanvas, Ground - local.Y / BeltBodyAppearancePresenter.WorldToCanvas);
    }
    internal Point WorldToViewport(Point point) => SceneToViewport(new(point.X * BeltBodyAppearancePresenter.WorldToCanvas,
        (Ground - point.Y) * BeltBodyAppearancePresenter.WorldToCanvas));
    internal Point PresenterToViewport(Point point) => SceneToViewport(new(point.X + left, point.Y + top));
    private Point SceneToViewport(Point point) => new(ActualWidth / 2 + pan.X + point.X * scale, Floor + pan.Y + point.Y * scale);
    private Point ViewportToScene(Point point) => new((point.X - ActualWidth / 2 - pan.X) / scale, (point.Y - Floor - pan.Y) / scale);
    private Point ViewportToPresenter(Point point) { var p = ViewportToScene(point); return new(p.X - left, p.Y - top); }
    private void UpdateCamera()
    {
        scene.RenderTransform = new MatrixTransform(scale, 0, 0, scale, ActualWidth / 2 + pan.X, Floor + pan.Y);
        if (pointerPresent) UpdatePick(pointer);
    }

    private Task SetWorldBoundsAsync()
    {
        // Resizing the physical space changes its walls. Camera zoom/pan deliberately do not.
        var half = Math.Max(1.25, ActualWidth / (2 * InitialScale * BeltBodyAppearancePresenter.WorldToCanvas));
        floorLine.X1 = -half * BeltBodyAppearancePresenter.WorldToCanvas;
        floorLine.X2 = half * BeltBodyAppearancePresenter.WorldToCanvas;
        return runtime.SetBoundsAsync(-half, half);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCamera();
        if (!disposed && initialization is not null) Observe(SetWorldBoundsAsync());
        TryActivate();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Math.Clamp(Stopwatch.GetElapsedTime(renderStamp, now).TotalSeconds, .001, .05);
        renderStamp = now;
        PresentFrame(seconds);
    }

    /// <summary>Presentation-only seam; tests consume genuine native snapshots without an HWND.</summary>
    internal void PresentFrame(double seconds)
    {
        VerifyAccess();
        if (!active || disposed) return;
        if (runtime.Metrics.Error is { } failure) { Fail(failure); return; }
        if (pendingDisplay is { } displayed && PresentationSource.FromVisual(this) is not null)
            runtime.MarkDisplayed(displayed);
        pendingDisplay = null;
        var batch = new List<BeltBodySnapshot>(2);
        var count = runtime.Metrics.QueueDepth >= 4 ? 2 : 1;
        for (var i = 0; i < count && runtime.TryDequeue(out var next); i++)
        {
            if (next?.Error is { } error) { Fail(error); return; }
            if (next?.Frame is not null && next.Epoch == runtime.Epoch) batch.Add(next);
        }
        try
        {
            foreach (var current in batch)
            {
                snapshot = current;
                boundary = Enumerable.Range(0, current.Frame!.Curve.Length / 2)
                    .Select(i => new BeltBodyBoundaryPoint(current.Frame.Curve[i * 2], current.Frame.Curve[i * 2 + 1])).ToArray();
                ApplyPose(current, seconds / batch.Count);
                pendingDisplay = current;
            }
            if (batch.Count == 0 && snapshot is { } previous) ApplyPose(previous, seconds);
            if ((presenter.RuntimeError ?? presenter.AccessoryError) is { } error) { Fail(new InvalidOperationException(error)); return; }
            if (pointerPresent) UpdatePick(pointer);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or ArithmeticException or System.IO.IOException)
        { Fail(error); }
    }

    private void ApplyPose(BeltBodySnapshot current, double seconds)
    {
        var frame = current.Frame!;
        left = frame.CoreX * BeltBodyAppearancePresenter.WorldToCanvas - 80;
        top = (Ground - frame.CoreY) * BeltBodyAppearancePresenter.WorldToCanvas - 66;
        Canvas.SetLeft(presenter.View, left); Canvas.SetTop(presenter.View, top);
        var input = current.ActualInput;
        presenter.Update(new(++presentationTick, seconds, boundary, frame.CoreX, frame.CoreY,
            frame.Scaffold[2], frame.Scaffold[3], frame.Grounded, input.Horizontal, input.Downward > 0,
            input.Stretch > 0, input.JumpPressed, input.DragActive,
            new(scale, ActualWidth / 2 + pan.X + left * scale, Floor + pan.Y + top * scale, ActualWidth, ActualHeight), current.Tick));
    }

    internal bool BeginPointer(Point point)
    {
        if (!active || disposed || !Finite(point)) return false;
        pointer = point; pointerPresent = true;
        if (tool == "pick") return CommitPick(point);
        if (tool == "pan") { panning = true; gestureStart = point; return true; }
        if (tool == "zoom") { zooming = true; gestureStart = point; return true; }
        if (!HitBody(point)) return false;
        dragging = true; PublishDrag(point); return true;
    }

    internal void MovePointer(Point point)
    {
        if (!active || disposed || !Finite(point)) return;
        pointer = point; pointerPresent = true;
        if (dragging) PublishDrag(point);
        else if (panning) { PanBy(point - gestureStart); gestureStart = point; }
        else if (zooming) { ZoomAt(gestureStart, Math.Exp((gestureStart.Y - point.Y) / 180)); gestureStart = point; }
        else UpdatePick(point);
    }

    internal void EndPointer()
    {
        if (disposed) return;
        dragging = panning = zooming = false;
        input.EndDrag();
    }

    private void PublishDrag(Point point)
    {
        var world = ViewportToWorld(point);
        input.BeginDrag(world); input.PublishInput();
    }

    private bool HitBody(Point point)
    {
        if (!Finite(point) || snapshot is null || point.X < 0 || point.Y < 0 || point.X > ActualWidth || point.Y > ActualHeight) return false;
        var inspection = presenter.Inspect();
        if (inspection.Body is not { } body || inspection.Shape is not { } shape) return false;
        var local = ViewportToPresenter(point);
        var raw = DeformableDangoVisual.CreateGaitTransform(body, presenter.GaitFrame).Inverse?.Transform(local) ?? local;
        var x = (raw.X - 80) / 160; var y = (66 - raw.Y) / 160;
        var inside = false;
        for (int i = 0, j = shape.Boundary.Count - 1; i < shape.Boundary.Count; j = i++)
        {
            var a = shape.Boundary[i]; var b = shape.Boundary[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private void BuildGhost()
    {
        ghost.Children.Clear(); ghost.Visibility = Visibility.Collapsed;
        if (content is not { } result) return;
        var item = result.Item;
        var longest = 64 * item.SizePercent / 100;
        var ratio = item.PixelWidth / (double)item.PixelHeight;
        var width = ratio >= 1 ? longest : longest * ratio;
        var height = ratio >= 1 ? longest / ratio : longest;
        // A separate still solver protects the live accessory's inertia and skeleton state.
        var frames = new AccessoryMotionSolver().RefreshPieces(default, width, height, item, result.Project);
        for (var i = 0; i < frames.Count; i++)
        {
            var texture = item.DeformationMode == AccessoryDeformationMode.Skeleton
                ? result.Project.Assets[result.Project.RenderPieces[i].AssetId].Bitmap : result.Image.Bitmap;
            var visual = new AccessoryMeshVisual(); visual.SetTexture(texture); visual.ApplyFrame(frames[i]);
            ghost.Children.Add(visual);
        }
    }

    internal bool UpdatePick(Point point)
    {
        ghost.Visibility = Visibility.Collapsed;
        if (!active || tool != "pick" || !HitBody(point) ||
            !presenter.TryPickBodyAnchor(ViewportToPresenter(point), out var anchor) || presenter.Inspect().Body is not { } body) return false;
        // A new blank project can still choose its real body anchor. Until actual artwork
        // arrives, there is no image to ghost and no synthetic accessory to display.
        if (content is null) return true;
        var rawAnchor = AccessoryBodyMapper.Map(body, null, anchor.X, anchor.Y);
        var gait = DeformableDangoVisual.CreateGaitTransform(body, presenter.GaitFrame);
        Point Project(Point p) => gait.Transform(p + (Vector)rawAnchor) + new Vector(left, top);
        var origin = Project(default); var x = Project(new(1, 0)) - origin; var y = Project(new(0, 1)) - origin;
        ghost.RenderTransform = new MatrixTransform(x.X, x.Y, y.X, y.Y, origin.X, origin.Y);
        ghost.Visibility = Visibility.Visible;
        return true;
    }

    internal bool CommitPick(Point point)
    {
        if (!UpdatePick(point) || !presenter.TryPickBodyAnchor(ViewportToPresenter(point), out var anchor)) return false;
        CancelPick(); AnchorPicked?.Invoke(anchor.X, anchor.Y); return true;
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !BeginPointer(e.GetPosition(this))) return;
        Focus();
        if (dragging || panning || zooming) CaptureMouse();
        e.Handled = true;
    }
    private void OnPointerMove(object sender, MouseEventArgs e) => MovePointer(e.GetPosition(this));
    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging && !panning && !zooming) return;
        EndPointer(); if (IsMouseCaptured) ReleaseMouseCapture(); e.Handled = true;
    }
    private void OnPointerLeave(object sender, MouseEventArgs e) { pointerPresent = false; ghost.Visibility = Visibility.Collapsed; }
    private void OnLostCapture(object sender, MouseEventArgs e) { if (dragging || panning || zooming) ClearInput(); }
    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ClearInput();
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!active) return;
        ClearInput(); ZoomAt(e.GetPosition(this), Math.Pow(1.12, e.Delta / 120d)); e.Handled = true;
    }
    internal bool SetKey(Key key, bool down, bool repeat = false)
    {
        VerifyAccess();
        if (!active || disposed) return false;
        return input.SetKey(key, down, repeat);
    }
    internal void ReconcileKeyboard(IEnumerable<Key> physicallyDown) => input.ReconcileKeyboard(physicallyDown);
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && tool == "pick") { CancelPick(); e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.None) { ClearInput(); return; }
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
        if (SetKey(key, true, e.IsRepeat)) e.Handled = true;
    }
    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
        if (SetKey(key, false)) e.Handled = true;
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => SetActive(false);
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!(bool)e.NewValue) SetActive(false); }
    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private async void Observe(Task operation)
    {
        try { await operation; }
        catch (Exception error)
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                await Dispatcher.InvokeAsync(() => { if (!disposed) Fail(error); });
        }
    }
    private void Fail(Exception error)
    {
        if (disposed || Error is not null) return;
        Error = error.Message; requestedActive = active = false; ++lifecycle;
        timer.Stop(); ClearInput(); CancelPick(); presenter.Paused = true; pendingDisplay = null;
        if (owner is not null) Observe(owner.PauseAsync());
        Failed?.Invoke(Error);
    }

    public void Dispose()
    {
        VerifyAccess();
        if (disposed) return;
        requestedActive = active = false; ++lifecycle; timer.Stop(); ClearInput(); CancelPick();
        disposed = true;
        timer.Tick -= OnTick;
        // Close's finite barrier guarantees native disposal on its owning worker thread.
        owner?.CloseAsync().GetAwaiter().GetResult();
        presenter.Dispose(); ghost.Children.Clear(); scene.Children.Clear(); Children.Clear();
    }
}
