using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Furniture;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics.Configuration;
using Point = System.Windows.Point;

namespace Dango.Desktop.AppShell.Rooms;

internal sealed class LocalRoomSceneDriver : IDisposable
{
    private static readonly LogicalRect Surface = new(0, 0, LocalRoomSceneView.WorldWidth, LocalRoomSceneView.WorldHeight);
    private readonly DesktopSceneSession session;
    private readonly StackedDangoSceneRuntime runtime;
    private readonly HostFrameScheduler scheduler;
    private LightInteractionSession pointer;
    private LogicalRect activeSurface = Surface;
    private LogicalPoint lastPointerWorld;
    private LogicalPoint lastClampedPointerWorld;
    private LogicalVector lastDragAnchor;
    private string lastPointerCancellation = "none";
    private readonly RoomPositionInterpolator positions = new();
    private HostFrameDiagnostics presentationSnapshot;
    private long? previousPresentationTicks;
    private bool hasPositionSample;
    private LocalRoomSceneView? view;
    private DirectionalControlIntent intent;
    private readonly bool furnitureEnabled;
    private bool furnitureGloveActive;
    internal OrdinaryRoomFurnitureSession? Furniture => runtime.Furniture;
    private bool alignmentPending;
    private bool readySent;
    private long generation;
    private EventHandler? confirmation;
    private double resumeX = LocalRoomSceneView.WorldWidth / 2;
    internal event Action<long>? HealthyFrameReady;
    internal event Action<long, string>? Failed;
    internal event Action? PointerCancelled;
    internal bool OwnsRuntime => scheduler.OwnsRuntime;
    internal bool IsReady => readySent && scheduler.OwnsRuntime;
    internal HostFrameDiagnostics FrameDiagnostics => scheduler.Snapshot;
    internal bool RuntimeRunning => runtime.IsRunning;
    internal bool RuntimeHealthy => runtime.LastError is null && runtime.LastFrame is not null;
    internal bool IsPointerActive => pointer.IsActive;
    internal string DesktopPetPointerDiagnostics => $"Pointer={pointer.IsActive}; PhysicalDrag={runtime.IsDragging}; " +
        $"Ready={readySent}; Owner={scheduler.OwnsRuntime}; InputBlocked={view?.InputBlocked}; ViewVisible={view?.IsVisible}; " +
        $"WorldTarget={lastPointerWorld}; ClampedTarget={lastClampedPointerWorld}; Anchor={lastDragAnchor}; " +
        $"Center={runtime.CurrentSelectedCenter}; SnapshotBounds={session.Controller?.Snapshot.Dango.VisualBounds}; " +
        $"SafeBounds={(view?.DesktopPetPresentationEnabled == true ? view.DesktopPetSafeWorldBounds : activeSurface)}; " +
        $"NativeBody={runtime.LastPhysicsFrame?.Bodies.FirstOrDefault()}; Cancel={lastPointerCancellation}";
    internal void ResetEntryPosition() => resumeX = LocalRoomSceneView.WorldWidth / 2;
    internal LocalRoomSceneDriver(DesktopSceneSession session, StackedDangoSceneRuntime runtime,
        IHostFrameSource? frameSource = null, Func<long>? timestamp = null, long frequency = 0, string? furnitureDataRoot = null)
    {
        this.session = session;
        this.runtime = runtime;
        furnitureEnabled = furnitureDataRoot is not null;
        if (furnitureDataRoot is not null) runtime.ConfigureOrdinaryFurniture(furnitureDataRoot);
        pointer = new(runtime, allowPhysicalDrag: false);
        runtime.InputCancelled += OnRuntimeInputCancelled;
        scheduler = new(runtime, Prepare, CreateInput, Apply, frameSource, timestamp, frequency);
        scheduler.CallbackCompleted += Present;
    }

    internal void Start(LocalRoomSceneView target, long attempt, bool enterWorld)
    {
        scheduler.VerifyCanStart();
        ClearInput();
        pointer = new(runtime, allowPhysicalDrag: target.DesktopPetPresentationEnabled,
            constrainPhysicalTarget: target.DesktopPetPresentationEnabled ? ClampPhysicalDragTarget : null);
        activeSurface = target.DesktopPetPresentationEnabled ? target.DesktopPetWorldSurface : Surface;
        if (view is not null) view.InputInvalidated -= CancelRoomPointers;
        view = target;
        target.InputInvalidated += CancelRoomPointers;
        generation = attempt;
        readySent = false;
        intent = DirectionalControlIntent.None;
        target.BeginPresentation();
        if (enterWorld)
        {
            session.AttachSurface(activeSurface, Environment.TickCount64);
            var bounds = session.Controller!.Snapshot.Dango.VisualBounds;
            session.Controller.MoveTo(new(resumeX - bounds.Width / 2, activeSurface.Bottom - bounds.Height));
            runtime.SetViewport(activeSurface);
            alignmentPending = true;
        }
        runtime.Start();
        runtime.AttachQuietIdleHost(this, true);
        runtime.UpdateWanderScreenWidth(this, activeSurface.Width);
        scheduler.Start();
        presentationSnapshot = scheduler.Snapshot;
        positions.Clear();
        previousPresentationTicks = null;
        hasPositionSample = false;
    }

    internal void Stop()
    {
        if (runtime.CurrentSelectedCenter is { } center && scheduler.OwnsRuntime) resumeX = center.X;
        ClearInput();
        if (view is not null) view.InputInvalidated -= CancelRoomPointers;
        scheduler.Stop();
        positions.Clear();
        hasPositionSample = false;
        previousPresentationTicks = null;
        view?.ResetPresentationMotion(invalidateInput: true);
        if (confirmation is not null) CompositionTarget.Rendering -= confirmation;
        confirmation = null;
        ++generation;
        if (runtime.FrameDriver is null && runtime.IsRunning) runtime.Stop();
    }

    internal void ClearInput()
    {
        CancelRoomPointers();
        intent = DirectionalControlIntent.None;
        view?.ClearWheel();
        if (scheduler.OwnsRuntime) runtime.CancelInput();
    }

    // Hosts retain their own OS/logical capture; drag conversion must not release it.
    internal bool PointerDown(Point sceneDip, bool shift = false, long? nowMilliseconds = null)
    {
        if (!CanContinuePointer() || pointer.IsActive || !view!.IsPetHit(sceneDip) ||
            runtime.LastFrame is not { EntityIds.Count: 1 } frame || !TryPointerWorld(sceneDip, out var world)) return false;
        lastPointerCancellation = "none";
        return pointer.TryBegin(frame.EntityIds[0], world, nowMilliseconds ?? Environment.TickCount64, shift,
            () => true, () => { }, CanContinuePointer);
    }

    internal bool PointerMove(Point sceneDip, bool shift = false, long? nowMilliseconds = null)
    {
        if (!pointer.IsActive) return false;
        if (!CanContinuePointer() || !TryPointerWorld(sceneDip, out var world)) { CancelPointer(); return true; }
        pointer.Move(world, nowMilliseconds ?? Environment.TickCount64, shift);
        if (!pointer.IsActive) PointerCancelled?.Invoke();
        return true;
    }

    internal bool PointerUp(Point sceneDip, bool shift = false, long? nowMilliseconds = null)
    {
        if (!pointer.IsActive) return false;
        if (!CanContinuePointer() || !TryPointerWorld(sceneDip, out var world)) { CancelPointer(); return true; }
        pointer.End(world, nowMilliseconds ?? Environment.TickCount64, shift);
        return true;
    }

    private void CancelRoomPointers()
    {
        CancelPointer();
        CancelFurnitureGlove();
    }

    internal bool BeginFurnitureGlove(Point sceneDip)
    {
        if (!CanContinuePointer() || Furniture?.CanEdit != true || !view!.IsPetHit(sceneDip)) return false;
        CancelRoomPointers();
        var anchor = view.ScreenToFurnitureAnchor(sceneDip);
        furnitureGloveActive = runtime.BeginOrdinaryFurnitureGlove(anchor.X, anchor.Y);
        return furnitureGloveActive;
    }

    internal bool MoveFurnitureGlove(Point sceneDip)
    {
        if (!furnitureGloveActive) return false;
        if (!CanContinuePointer()) { CancelFurnitureGlove(); return false; }
        var anchor = view!.ScreenToFurnitureAnchor(sceneDip);
        runtime.UpdateOrdinaryFurnitureGlove(anchor.X, anchor.Y);
        return true;
    }

    internal bool EndFurnitureGlove(Point sceneDip)
    {
        if (!furnitureGloveActive) return false;
        MoveFurnitureGlove(sceneDip);
        CancelFurnitureGlove();
        return true;
    }

    internal void CancelFurnitureGlove()
    {
        if (!furnitureGloveActive) return;
        furnitureGloveActive = false;
        runtime.EndOrdinaryFurnitureGlove();
    }

    internal void CancelPointer()
        => CancelPointerCore("host, capture or view cancelled");

    private void CancelPointerCore(string reason)
    {
        if (!pointer.IsActive) return;
        lastPointerCancellation = reason;
        pointer.Cancel();
        PointerCancelled?.Invoke();
    }

    private bool CanContinuePointer() => scheduler.OwnsRuntime && readySent && runtime.IsRunning && runtime.LastError is null &&
        view is { HasHealthyFrame: true, IsVisible: true, InputBlocked: false, ActualWidth: > 0, ActualHeight: > 0 };

    private bool TryPointerWorld(Point sceneDip, out LogicalPoint world)
    {
        world = default;
        if (view is null || !double.IsFinite(sceneDip.X) || !double.IsFinite(sceneDip.Y)) return false;
        var displayedWorld = view.ScreenToPetWorld(sceneDip);
        world = new(displayedWorld.X, displayedWorld.Y - runtime.NeutralBodyGroundOffset);
        lastPointerWorld = world;
        lastClampedPointerWorld = world;
        return double.IsFinite(world.X) && double.IsFinite(world.Y);
    }

    // Click and petting qualify against the actual contact point. Only a
    // controller-classified physical drag targets the native top-node anchor.
    private LogicalPoint ClampPhysicalDragTarget(LogicalPoint world)
    {
        lastDragAnchor = DesktopPetDragAnchor();
        lastClampedPointerWorld = view!.ClampDesktopPetPointer(world, lastDragAnchor);
        return lastClampedPointerWorld;
    }

    private LogicalVector DesktopPetDragAnchor()
    {
        if (runtime.LastPhysicsFrame?.Bodies.FirstOrDefault(body => body.EntityId == runtime.SelectedEntity) is not { } physical ||
            physical.PointCount == 0) return default;
        var node = physical.SelectedDragNode;
        if (node < 0 || node >= physical.PointCount)
            node = Enumerable.Range(0, physical.PointCount).MaxBy(index => physical.Contour[index * 2 + 1]);
        var x = (double)physical.Contour[node * 2];
        var y = (double)physical.Contour[node * 2 + 1];
        var length = Math.Sqrt(x * x + y * y);
        var nodeRadius = runtime.NodeRadiusOf(physical.EntityId);
        var contraction = length > nodeRadius ? 1 - nodeRadius / length : 1;
        // Native drag pulls the highest node toward the pointer; it does not retain the initial click offset.
        return new(x * contraction * 80, -y * contraction * 60);
    }

    // Pure snapshot for the gesture hook's constant comparisons. Reuse the exact
    // anchor and clamp that PointerMove will apply; do not step or move the body.
    internal bool TryCaptureDesktopPetPointerClamp(out LogicalRect clamp)
    {
        clamp = default;
        if (view is not { DesktopPetPresentationEnabled: true, HasHealthyFrame: true, NativePetCenter: not null, PetScreenBounds: not null } ||
            !scheduler.OwnsRuntime || runtime.LastPhysicsFrame is null) return false;
        var anchor = DesktopPetDragAnchor();
        var low = view.ClampDesktopPetPointer(new(-double.MaxValue, -double.MaxValue), anchor);
        var high = view.ClampDesktopPetPointer(new(double.MaxValue, double.MaxValue), anchor);
        var width = high.X - low.X;
        var height = high.Y - low.Y;
        if (!double.IsFinite(low.X) || !double.IsFinite(low.Y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            width < 0 || height < 0) return false;
        clamp = new(low.X, low.Y, width, height);
        return true;
    }

    private void OnRuntimeInputCancelled()
    {
        // BeginPhysicalDrag can cancel the old light target during conversion.
        // LightInteractionSession clears it first and then establishes the drag target.
        if (pointer.IsActive) CancelPointerCore("runtime cancelled input");
        if (furnitureGloveActive)
        {
            furnitureGloveActive = false;
            PointerCancelled?.Invoke();
        }
    }

    internal bool Key(System.Windows.Input.Key key, bool down, bool repeat)
    {
        if (!scheduler.OwnsRuntime || view?.InputBlocked == true || view?.DesktopPetPresentationEnabled == true || !readySent) return false;
        if (down && repeat) return key is System.Windows.Input.Key.A or System.Windows.Input.Key.D or System.Windows.Input.Key.S or System.Windows.Input.Key.Space;
        intent = key switch
        {
            System.Windows.Input.Key.A => intent with { Left = down },
            System.Windows.Input.Key.D => intent with { Right = down },
            System.Windows.Input.Key.S => intent with { Squash = down },
            System.Windows.Input.Key.Space => intent with { Jump = down },
            _ => intent,
        };
        return key is System.Windows.Input.Key.A or System.Windows.Input.Key.D or System.Windows.Input.Key.S or System.Windows.Input.Key.Space;
    }

    private bool Prepare()
    {
        if ((pointer.IsActive || furnitureGloveActive) && !CanContinuePointer()) CancelRoomPointers();
        if (!scheduler.OwnsRuntime || view is null || !view.IsVisible || view.ActualWidth <= 0 || view.ActualHeight <= 0) return false;
        if (runtime.LastError is not null) { ReportFailure("团子暂不可用，请返回设置重试。"); return false; }
        try
        {
            if (alignmentPending)
            {
                if (runtime.LastFrame is null)
                { runtime.SetDirectionalControl(DirectionalControlIntent.None); return true; }
                var bounds = session.Controller!.Snapshot.Dango.VisualBounds;
                var left = view.DesktopPetPresentationEnabled ? activeSurface.X : LocalRoomSceneView.SideBoundaryInset;
                var right = view.DesktopPetPresentationEnabled ? activeSurface.Right : Surface.Right - LocalRoomSceneView.SideBoundaryInset;
                if (!runtime.TryGetHorizontalPlacement(bounds.Center, left, right, out var safe))
                { ReportFailure("房间放置不可用，请返回门廊重试。"); return false; }
                session.Controller.MoveTo(new(safe.X - bounds.Width / 2, bounds.Y));
                if (!runtime.RehostSelectedEntityAt(session.Controller.Snapshot.Dango.VisualBounds.Center)) return false;
                runtime.SetHorizontalBounds(left, right);
                alignmentPending = false;
            }
            if (furnitureEnabled && view.WindowCameraEnabled && !view.DesktopPetPresentationEnabled &&
                !runtime.AttachOrdinaryFurniture())
            { ReportFailure(Furniture?.Status ?? "家具布局暂不可用，请返回门廊重试。"); return false; }
            runtime.SetDirectionalControl(view.InputBlocked ? DirectionalControlIntent.None : intent);
            runtime.UpdateQuietIdleHost(this, true);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { ReportFailure("房间物理准备失败，请返回门廊重试。"); return false; }
    }

    private PhysicsPresentationInput CreateInput()
    {
        var c = session.Controller!;
        return PhysicsPresentationInput.FromSnapshot(c.Snapshot, c.SquashAmount, c.GroundFrictionCue, c.FacingDirection, null);
    }

    private void Apply(StackedPhysicsPresentationFrame frame)
    {
        if (view?.WindowCameraEnabled == true && furnitureEnabled)
            view.SetFurnitureFrame(Furniture?.RenderItems ?? [], Furniture?.Preview, false);
        if (view is null || alignmentPending || !view.ApplyFrame(frame, runtime))
        { if (runtime.LastError is not null) ReportFailure("房间首帧不可用，请返回门廊重试。"); return; }
        if (readySent || confirmation is not null) return;
        var attempt = generation;
        var target = view;
        // Wait one subsequent composition callback after the healthy body/hat were applied.
        confirmation = (_, _) =>
        {
            CompositionTarget.Rendering -= confirmation;
            confirmation = null;
            target.Dispatcher.BeginInvoke(() =>
            {
                if (generation != attempt || !scheduler.OwnsRuntime || !ReferenceEquals(target, view) ||
                    !target.HasHealthyFrame || !target.IsVisible || PresentationSource.FromVisual(target) is null || runtime.LastError is not null) return;
                readySent = true;
                HealthyFrameReady?.Invoke(attempt);
            }, DispatcherPriority.Render);
        };
        CompositionTarget.Rendering += confirmation;
    }

    private void ReportFailure(string message)
    {
        var attempt = generation;
        view!.Dispatcher.BeginInvoke(() => { if (attempt == generation) Failed?.Invoke(attempt, message); });
    }

    private void Present(object? sender, EventArgs args)
    {
        var now = scheduler.Snapshot;
        var before = presentationSnapshot;
        presentationSnapshot = now;
        if (view is not { WindowCameraEnabled: true }) return;
        // CallbackCompleted also runs for duplicated WPF RenderingTime and failed preparation.
        if (now.DuplicateCallbacks != before.DuplicateCallbacks) return;
        if (!scheduler.OwnsRuntime || !scheduler.IsRunning || !view.IsVisible || !view.HasHealthyFrame ||
            runtime.LastError is not null || view.NativePetCenter is not { } center)
        {
            positions.Clear();
            hasPositionSample = false;
            previousPresentationTicks = null;
            view.ResetPresentationMotion(invalidateInput: true);
            return;
        }
        var simulationTicks = now.SuccessfulSteps * HostFrameClock.FixedStep.Ticks;
        var clockTicks = simulationTicks + now.RemainderTicks;
        var discontinuity = now.Overloads != before.Overloads || now.LongPauses != before.LongPauses ||
            now.ClockResets != before.ClockResets || now.Faults != before.Faults ||
            now.RefreshFrames != before.RefreshFrames ||
            previousPresentationTicks is { } previous && clockTicks < previous;
        if (!hasPositionSample || discontinuity)
        {
            positions.Reset(center, simulationTicks);
            hasPositionSample = true;
            previousPresentationTicks = null;
            view.ResetPresentationMotion(invalidateInput: discontinuity);
        }
        else if (now.AppliedFrames != before.AppliedFrames)
            positions.Add(center, simulationTicks);
        var elapsedMs = previousPresentationTicks is { } last ?
            Math.Clamp((clockTicks - last) / (double)TimeSpan.TicksPerMillisecond, 0, 100) : 0;
        previousPresentationTicks = clockTicks;
        view.Present(positions.At(clockTicks - HostFrameClock.FixedStep.Ticks), elapsedMs);
    }

    public void Dispose()
    {
        Stop();
        scheduler.CallbackCompleted -= Present;
        runtime.InputCancelled -= OnRuntimeInputCancelled;
    }
}
