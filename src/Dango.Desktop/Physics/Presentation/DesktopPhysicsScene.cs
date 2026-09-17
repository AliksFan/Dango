using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Physics;
using Dango.Physics.Configuration;

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
public sealed class DesktopPhysicsScene : IDisposable
{
    private readonly DangoPhysicsWorld world;
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

    public DangoEntityId SelectedEntity { get; private set; }
    public int Count => presenters.Count;
    public bool IsPaused => paused;
    public int LastErrorCode => world.LastErrorCode;
    public DesktopPhysicsSceneFrame? LastFrame { get; private set; }
    public IReadOnlyList<DangoEntityId> EntityIds => Array.AsReadOnly(presenters.Keys.ToArray());

    public DangoEntityId Add(double x, double y)
    {
        VerifyAccess();
        EnsureHealthy();
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
        return new((hostPoint.X - origin.X) / (width / 2), (origin.Y - hostPoint.Y) / (height / 2));
    }

    public LogicalPoint ToHost(LogicalPoint worldPoint)
    {
        VerifyAccess();
        ValidatePoint(worldPoint);
        return new(origin.X + worldPoint.X * width / 2, origin.Y - worldPoint.Y * height / 2);
    }

    public DesktopPhysicsSceneFrame Step(TimeSpan fixedStep)
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
        var horizontal = intent.Left == intent.Right ? 0 : intent.Left ? -1 : 1;
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
        world.Reset();
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
        world.Dispose();
        presenters.Clear();
        facing.Clear();
        LastFrame = null;
    }

    private void VerifyEntity(DangoEntityId id)
    {
        VerifyAccess();
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
        intent = DirectionalControlIntent.None;
        dragTarget = default;
        dragActive = false;
        jumpWasHeld = false;
    }
}
