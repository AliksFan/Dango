using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Physics;
using Dango.Physics.Configuration;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Desktop.Physics.Presentation;

public sealed record DesktopPhysicsBody(
    DangoEntityId EntityId,
    LogicalRect Bounds,
    PhysicsPresentationFrame Presentation);

public sealed record DesktopPhysicsSceneFrame(
    DangoPhysicsFrame Physics,
    IReadOnlyList<DesktopPhysicsBody> Bodies);

/// <summary>
/// Owns the desktop scene's single physics world. Host windows only translate
/// coordinates and render these ID-bearing snapshots; they do not own worlds.
/// This adapter is staged separately from the existing host entry point.
/// </summary>
public sealed partial class DesktopPhysicsScene : IDisposable
{
    private readonly DangoPhysicsWorld world = null!;
    private readonly BeltRoomSceneAdapter? beltRoom;
    private readonly Dictionary<DangoEntityId, DangoPhysicsPresentationAdapter> presenters = [];
    private readonly Dictionary<DangoEntityId, DangoFacingDirection> facing = [];
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly double width;
    private readonly double height;
    private LogicalPoint origin;
    private DirectionalControlIntent intent;
    private LogicalPoint dragTarget;
    private bool dragActive;
    private bool jumpWasHeld;
    private bool paused;
    private bool disposed;
    private bool unboundedHorizontalPreview;
    private double horizontalCoreOffset;
    private const double HorizontalRebaseThreshold = 4;

    public DesktopPhysicsScene(string nativeLibraryPath, LogicalRect referenceBounds)
    {
        width = referenceBounds.Width;
        height = referenceBounds.Height;
        origin = referenceBounds.Center;
        world = DangoPhysicsWorld.Create(nativeLibraryPath);
        try
        {
            SelectedEntity = Add(0, 0);
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    public DesktopPhysicsScene(string nativeLibraryPath, LogicalRect referenceBounds, DangoBodyControls controls)
    {
        width = 160; height = 120;
        beltRoom = new(nativeLibraryPath, referenceBounds, controls);
        SelectedEntity = new(1);
        LastFrame = beltRoom.LastFrame;
    }
    public bool UsesBeltRoom => beltRoom is not null;
    public DangoBeltRoomFrame? BeltRoomFrame => beltRoom?.Frame;
    internal BodySupportEnvelope? SupportEnvelope => beltRoom is not null
        ? BeltRoomBodyGeometry.SupportEnvelope(beltRoom.Frame) : new BodySupportEnvelope(-80, 80, 120);
    internal bool HasFloorSupport => beltRoom is not null ? BeltRoomBodyGeometry.HasFloorSupport(beltRoom.Frame)
        : LastFrame?.Physics.Bodies.SingleOrDefault() is { IsGrounded: true, GroundContactCount: > 0 };
    internal void ConfigureBeltSleepPresentationScale(double? scale) => beltRoom?.ConfigureSleepPresentationScale(scale);
    internal bool BeltRoomSleeping => beltRoom?.IsSleeping == true;
    internal long BeltRoomNativeStepCalls => beltRoom?.NativeStepCalls ?? 0;
    public LogicalPoint SelectedCoreHost => beltRoom?.CoreHost ??
        LastFrame?.Bodies.Single(body => body.EntityId == SelectedEntity).Bounds.Center ?? origin;

    public DangoEntityId SelectedEntity { get; private set; }
    public int Count => beltRoom is null ? presenters.Count : 1;
    public bool IsPaused => paused;
    public int LastErrorCode => beltRoom?.LastErrorCode ?? world.LastErrorCode;
    public DesktopPhysicsSceneFrame? LastFrame { get; private set; }
    public IReadOnlyList<DangoEntityId> EntityIds => beltRoom is null ? Array.AsReadOnly(presenters.Keys.ToArray()) : Array.AsReadOnly(new[] { SelectedEntity });
    public double GroundY => beltRoom?.GroundY ?? ToHost(new(0, DangoPhysicsDefaults.Current.GroundHeight)).Y;
    internal bool UnboundedHorizontalPreview => unboundedHorizontalPreview;
    internal int HorizontalRebaseCount { get; private set; }
    internal double PreviewJumpSpeedScale => beltRoom is null ? world.PreviewJumpSpeedScale : 1;
    internal double ContinuousCoreX(DangoBodyState body) => body.CoreX + horizontalCoreOffset;
    internal bool SupportsMuscleActuation => beltRoom?.SupportsMuscleActuation == true;
    internal DangoActiveForceFrame ReadActiveForces() => beltRoom?.ReadActiveForces()
        ?? throw new NotSupportedException("当前场景不支持真实主动施力诊断。");
    internal DangoMuscleDiagnostics ReadMuscleDiagnostics()
    {
        VerifyAccess();
        return beltRoom?.ReadMuscleDiagnostics() ?? throw new NotSupportedException("内部肌肉动作需要连续带物理。");
    }

    internal void SetActionJumpSpeedScale(double scale)
    {
        VerifyAccess(); EnsureHealthy();
        if (beltRoom is null) throw new NotSupportedException("轻跳实验需要连续带房间物理。");
        beltRoom.SetActionJumpSpeedScale(scale);
    }

    internal void SetPreviewJumpSpeedScale(double scale)
    {
        VerifyAccess();
        if (beltRoom is not null) throw new NotSupportedException("Belt room preview scaling is unsupported.");
        if (scale != 1) EnsureHealthy();
        world.SetPreviewJumpSpeedScale(scale);
    }

    /// <summary>
    /// Uses a rolling local origin for the single-body editor preview. The native
    /// floor remains finite; rebasing before a step keeps the body over that floor
    /// while host coordinates, velocities, input and presentation history continue.
    /// </summary>
    internal void SetUnboundedHorizontalPreview(bool enabled)
    {
        VerifyAccess();
        EnsureHealthy();
        if (beltRoom is not null) throw new NotSupportedException("Belt room unbounded preview is unsupported.");
        if (unboundedHorizontalPreview == enabled) return;
        if (enabled)
        {
            if (Count != 1) throw new InvalidOperationException("无边界编辑预览只支持当前单团子。");
            // Validate both optional exports before the first walking step. A
            // missing capability must not become a picture-only approximation.
            world.TranslateHorizontal(0);
            world.ClearHorizontalBounds();
        }
        unboundedHorizontalPreview = enabled;
    }

    private void RebaseHorizontalPreview()
    {
        if (!unboundedHorizontalPreview || LastFrame is not { Physics.Succeeded: true } frame) return;
        if (Count != 1) throw new InvalidOperationException("无边界编辑预览只支持当前单团子。");
        var selected = frame.Physics.Bodies.Single(body => body.EntityId == SelectedEntity);
        if (Math.Abs(selected.CoreX) < HorizontalRebaseThreshold) return;
        var shift = (double)selected.CoreX;
        // Translate moves the existing nodes, core and floor together. Restoring
        // the floor afterwards leaves the rig near zero without clearing input,
        // cooldowns, smoothing state or adding a simulation step.
        world.TranslateHorizontal(-shift);
        world.ClearHorizontalBounds();
        origin = new(origin.X + shift * width / 2, origin.Y);
        horizontalCoreOffset += shift;
        HorizontalRebaseCount++;
        LastFrame = frame with
        {
            Physics = frame.Physics with
            {
                Bodies = frame.Physics.Bodies.Select(body => body with { CoreX = (float)(body.CoreX - shift) }).ToArray(),
            },
        };
    }

    public void SetHorizontalBounds(double hostLeft, double hostRight)
    {
        VerifyAccess();
        EnsureHealthy();
        if (unboundedHorizontalPreview) throw new InvalidOperationException("请先退出无边界编辑预览，再设置房间边界。");
        if (beltRoom is not null) { beltRoom.SetBounds(hostLeft, hostRight); return; }
        world.SetHorizontalBounds(ToWorld(new(hostLeft, origin.Y)).X, ToWorld(new(hostRight, origin.Y)).X);
    }

    public void ClearHorizontalBounds()
    {
        VerifyAccess();
        EnsureHealthy();
        if (beltRoom is not null) { beltRoom.ClearBounds(); return; }
        world.ClearHorizontalBounds();
    }

    /// <summary>
    /// Queries a local handoff placement from the last published physical node
    /// circles, including every entity's offset from the selected core. This
    /// neither advances nor moves the world; callers use it only before rehost,
    /// never to clamp continuous movement or to relax native wall containment.
    /// </summary>
    public bool TryGetHorizontalPlacement(LogicalPoint requestedCenter, double hostLeft, double hostRight,
        out LogicalPoint safeCenter)
    {
        VerifyAccess();
        EnsureHealthy();
        ValidatePoint(requestedCenter);
        if (!double.IsFinite(hostLeft) || !double.IsFinite(hostRight) || hostLeft >= hostRight)
            throw new ArgumentOutOfRangeException(nameof(hostRight));
        if (beltRoom is not null) return beltRoom.TryPlacement(requestedCenter, hostLeft, hostRight, out safeCenter);
        safeCenter = default;
        if (LastFrame is not { Physics.Succeeded: true } frame) return false;
        var defaults = DangoPhysicsDefaults.Current;
        var scale = width / 2;
        if (hostRight - hostLeft < 2 * (defaults.RadialRestLength + defaults.NodeRadius) * scale) return false;
        var selected = frame.Physics.Bodies.Single(body => body.EntityId == SelectedEntity);
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var body in frame.Physics.Bodies)
        {
            var relativeCoreX = (double)body.CoreX - selected.CoreX;
            for (var n = 0; n < body.PointCount; n++)
            {
                var x = (double)body.Contour[2 * n];
                var y = (double)body.Contour[2 * n + 1];
                var length = Math.Sqrt(x * x + y * y);
                // dango_self_read_frame radially expands node-center vectors
                // by node_radius when their native length exceeds .0001.
                // Invert that expansion, then use circle X extents instead of
                // treating the rendered radial contour as collision geometry.
                var nodeX = length > defaults.NodeRadius
                    ? x * (1 - defaults.NodeRadius / length) : x;
                minimum = Math.Min(minimum, relativeCoreX + nodeX - defaults.NodeRadius);
                maximum = Math.Max(maximum, relativeCoreX + nodeX + defaults.NodeRadius);
            }
        }
        var minimumCenter = hostLeft - minimum * scale;
        var maximumCenter = hostRight - maximum * scale;
        if (minimumCenter > maximumCenter) return false;
        safeCenter = new(Math.Clamp(requestedCenter.X, minimumCenter, maximumCenter), requestedCenter.Y);
        return true;
    }

    /// <summary>
    /// Local scene handoff, not a network teleport. The caller configures the new
    /// stage bounds (or clears them for home) before any next physics step.
    /// </summary>
    public bool RehostSelectedEntityAt(LogicalPoint actualCenter)
    {
        VerifyAccess();
        EnsureHealthy();
        ValidatePoint(actualCenter);
        if (beltRoom is not null) { ClearInput(); var ok = beltRoom.RehostSelected(actualCenter); LastFrame = beltRoom.LastFrame; return ok; }
        if (LastFrame is not { Physics.Succeeded: true } frame) return false;
        var selected = frame.Physics.Bodies.Single(body => body.EntityId == SelectedEntity);
        var selectedBounds = frame.Bodies.Single(body => body.EntityId == SelectedEntity).Bounds;
        ClearInput();
        // No Step may occur between restoring geometry, recentering and the
        // caller configuring the new floor. Optional-export failures propagate.
        world.ClearHorizontalBounds();
        var horizontal = -(double)selected.CoreX;
        world.TranslateHorizontal(horizontal);
        horizontalCoreOffset = 0;
        origin = new(actualCenter.X, actualCenter.Y + selected.CoreY * height / 2);
        var translation = new LogicalVector(actualCenter.X - selectedBounds.Center.X, actualCenter.Y - selectedBounds.Center.Y);
        LastFrame = frame with
        {
            Physics = frame.Physics with
            {
                Bodies = frame.Physics.Bodies.Select(body => body with { CoreX = (float)(body.CoreX + horizontal) }).ToArray(),
            },
            Bodies = frame.Bodies.Select(body => body with { Bounds = body.Bounds.Translate(translation) }).ToArray(),
        };
        return true;
    }

    public DangoEntityId Add(double x, double y)
    {
        VerifyAccess();
        EnsureHealthy();
        if (beltRoom is not null) throw new NotSupportedException("Belt rooms currently support one body.");
        var id = world.Add(x, y);
        presenters.Add(id, new(DangoPhysicsDefaults.Current));
        facing.Add(id, DangoFacingDirection.Center);
        return id;
    }

    public bool Remove(DangoEntityId id)
    {
        VerifyEntity(id);
        EnsureHealthy();
        if (Count == 1) return false;
        world.Remove(id);
        presenters.Remove(id);
        facing.Remove(id);
        if (SelectedEntity == id)
        {
            SelectedEntity = presenters.Keys.First();
            ClearInput();
        }
        // Do not publish stale removed IDs as a current snapshot.
        LastFrame = null;
        return true;
    }

    public void Select(DangoEntityId id)
    {
        VerifyEntity(id);
        EnsureHealthy();
        if (SelectedEntity == id) return;
        SelectedEntity = id;
        ClearInput();
    }

    public void SetInput(DirectionalControlIntent value)
    {
        VerifyAccess();
        EnsureHealthy();
        if (paused) throw new InvalidOperationException("场景已暂停。");
        intent = value;
    }

    public void BeginDrag(DangoEntityId id, LogicalPoint hostPoint)
    {
        VerifyAccess();
        if (paused) throw new InvalidOperationException("场景已暂停。");
        Select(id);
        dragTarget = ToWorld(hostPoint);
        dragActive = true;
    }

    public void MoveDrag(LogicalPoint hostPoint)
    {
        VerifyAccess();
        EnsureHealthy();
        if (!dragActive) throw new InvalidOperationException("尚未开始拖动。");
        dragTarget = ToWorld(hostPoint);
    }

    public void CancelInput()
    {
        VerifyAccess();
        ClearInput();
    }

    public void EndDrag()
    {
        VerifyAccess();
        dragActive = false;
        dragTarget = default;
    }

    public void SetPaused(bool value)
    {
        VerifyAccess();
        paused = value;
        ClearInput();
    }

    public void Rehost(LogicalPoint referenceCenter)
    {
        VerifyAccess();
        ValidatePoint(referenceCenter);
        if (beltRoom is not null) { beltRoom.Rehost(referenceCenter); ClearInput(); LastFrame = beltRoom.LastFrame; return; }
        var translation = new LogicalVector(referenceCenter.X - origin.X, referenceCenter.Y - origin.Y);
        origin = referenceCenter;
        ClearInput();
        // Keep the world and per-entity presentation history across hosts.
        if (LastFrame is not null)
            LastFrame = LastFrame with
            {
                Bodies = Array.AsReadOnly(LastFrame.Bodies.Select(body => body with
                {
                    Bounds = body.Bounds.Translate(translation),
                }).ToArray()),
            };
    }

    public LogicalPoint ToWorld(LogicalPoint hostPoint)
    {
        VerifyAccess();
        ValidatePoint(hostPoint);
        if (beltRoom is not null) return beltRoom.ToWorld(hostPoint);
        return new((hostPoint.X - origin.X) / (width / 2), (origin.Y - hostPoint.Y) / (height / 2));
    }

    public LogicalPoint ToHost(LogicalPoint worldPoint)
    {
        VerifyAccess();
        ValidatePoint(worldPoint);
        if (beltRoom is not null) return beltRoom.ToHost(worldPoint);
        return new(origin.X + worldPoint.X * width / 2, origin.Y - worldPoint.Y * height / 2);
    }

    public DesktopPhysicsSceneFrame Step(TimeSpan fixedStep, double autonomousHorizontal = 0) =>
        StepCore(fixedStep, autonomousHorizontal, null);

    // Explicit, single-step actuation for the isolated action consumer. Never stored
    // as held keyboard input, so cancellation cannot leave a force behind.
    internal DesktopPhysicsSceneFrame StepActionPreview(TimeSpan fixedStep, DangoBeltInput input) =>
        StepCore(fixedStep, 0, input);

    private DesktopPhysicsSceneFrame StepCore(TimeSpan fixedStep, double autonomousHorizontal, DangoBeltInput? actionInput)
    {
        VerifyAccess();
        if (paused) throw new InvalidOperationException("场景已暂停；恢复后不得补算隐藏时间。");
        if (fixedStep <= TimeSpan.Zero || fixedStep > TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        if (LastErrorCode != 0)
        {
            ClearInput();
            return LastFrame ?? throw new InvalidOperationException("故障场景必须显式重置。");
        }
        if (!double.IsFinite(autonomousHorizontal) || Math.Abs(autonomousHorizontal) > .35)
            throw new ArgumentOutOfRangeException(nameof(autonomousHorizontal));
        if (actionInput is { } motor)
        {
            if (beltRoom is null) throw new NotSupportedException("动作实验需要连续带物理场景。");
            return LastFrame = beltRoom.Step(motor);
        }
        if (beltRoom is not null)
        {
            var axis = intent.Left == intent.Right ? 0d : intent.Left ? -1d : 1d;
            if (axis == 0 && !intent.Jump && !intent.Squash && !intent.Stretch && !dragActive) axis = autonomousHorizontal;
            // Native owns vertical-only cancellation when both channels are held;
            // a full cancellation would also erase the current A/D force.
            var result = beltRoom.Step(new(axis, intent.Stretch ? 1 : 0, intent.Squash ? 1 : 0,
                intent.Jump && !jumpWasHeld, dragActive, dragTarget.X, dragTarget.Y,
                intent.Left || intent.Right || intent.Jump || intent.Squash || intent.Stretch || dragActive));
            jumpWasHeld = intent.Jump;
            return LastFrame = result;
        }
        RebaseHorizontalPreview();
        var horizontal = intent.Left == intent.Right ? 0d : intent.Left ? -1d : 1d;
        if (horizontal == 0 && !intent.Jump && !intent.Squash && !dragActive && Count == 1)
            horizontal = autonomousHorizontal;
        if (horizontal != 0)
            facing[SelectedEntity] = horizontal < 0 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
        var jump = intent.Jump && !jumpWasHeld;
        var inputs = presenters.Keys.Select(id => id == SelectedEntity
            ? new DangoPhysicsInput(id, horizontal, intent.Squash ? 1 : 0, jump,
                dragActive, dragTarget.X, dragTarget.Y)
            : new DangoPhysicsInput(id)).ToArray();
        var physics = world.Step(fixedStep, inputs);
        jumpWasHeld = intent.Jump;
        if (!physics.Succeeded)
        {
            ClearInput();
            // The module supplies the last safe physics state. Do not advance
            // attachment smoothing again when freezing the last rendered frame.
            if (LastFrame is not null)
                return LastFrame = LastFrame with { Physics = physics };
        }
        var bodies = physics.Bodies.Select(state =>
        {
            var center = ToHost(new(state.CoreX, state.CoreY));
            return new DesktopPhysicsBody(state.EntityId,
                new(center.X - width / 2, center.Y - height / 2, width, height),
                presenters[state.EntityId].Present(state, fixedStep, facing[state.EntityId]));
        }).ToArray();
        return LastFrame = new(physics, Array.AsReadOnly(bodies));
    }

    public void Reset()
    {
        VerifyAccess();
        if (beltRoom is not null) { beltRoom.Reset(); ClearInput(); LastFrame = beltRoom.LastFrame; return; }
        world.Reset();
        horizontalCoreOffset = 0;
        HorizontalRebaseCount = 0;
        foreach (var presenter in presenters.Values) presenter.Reset();
        foreach (var id in facing.Keys.ToArray()) facing[id] = DangoFacingDirection.Center;
        ClearInput();
        LastFrame = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        VerifyAccess();
        disposed = true;
        ClearInput();
        if (beltRoom is not null) beltRoom.Dispose(); else world.Dispose();
        presenters.Clear();
        facing.Clear();
        LastFrame = null;
    }

    private void VerifyEntity(DangoEntityId id)
    {
        VerifyAccess();
        if (beltRoom is not null && id == SelectedEntity) return;
        if (!presenters.ContainsKey(id))
            throw new DangoPhysicsException(DangoPhysicsFailureKind.InvalidEntity, "桌面实体已失效。", -412);
    }

    private void EnsureHealthy()
    {
        if (LastErrorCode != 0) throw new InvalidOperationException("物理故障已锁存；请显式重置或退出。");
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("桌面物理场景只能由创建线程访问。");
    }

    private static void ValidatePoint(LogicalPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
    }

    private void ClearInput()
    {
        beltRoom?.Wake();
        intent = DirectionalControlIntent.None;
        dragTarget = default;
        dragActive = false;
        jumpWasHeld = false;
    }
}
