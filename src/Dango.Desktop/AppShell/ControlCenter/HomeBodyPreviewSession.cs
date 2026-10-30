using System.Windows.Input;
using Dango.Desktop.Physics.Belt;
using Point = System.Windows.Point;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>
/// Owns preview binding and input state without owning controls, focus or a frame loop.
/// The view supplies physical key state and releases its own pointer capture.
/// </summary>
internal sealed class HomeBodyPreviewSession
{
    private readonly HashSet<Key> held = [], suppressed = [];
    private ProductPhysicsComposition composition = ProductPhysicsComposition.Default;
    private bool jump, cancel;
    private Point dragTarget;
    internal IBodyPreviewRuntime? Runtime { get; private set; }
    internal bool Dragging { get; private set; }

    internal void Bind(ProductPhysicsComposition? selected = null)
    {
        if (Runtime is not null) throw new InvalidOperationException("主页已绑定实时场景。");
        if (selected is not null) composition = selected;
        Runtime = composition.CreatePreview();
    }

    internal void Bind(IBodyPreviewRuntime runtime)
    {
        if (Runtime is not null) throw new InvalidOperationException("预览已绑定实时场景。");
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    internal void Close()
    {
        if (Runtime is null) return;
        Runtime.CloseAsync().GetAwaiter().GetResult();
        Runtime = null;
    }

    internal bool SetKey(Key key, bool down, bool repeat)
    {
        if (Runtime is null || key is not (Key.A or Key.D or Key.W or Key.S or Key.Space)) return false;
        if (!down && !held.Contains(key) && !suppressed.Contains(key)) return true;
        if (!down) { held.Remove(key); suppressed.Remove(key); }
        else if (!suppressed.Contains(key) && !(repeat && key == Key.Space) && held.Add(key) && key == Key.Space) jump = true;
        PublishInput();
        return true;
    }

    internal void ClearInput(Action releaseCapture)
    {
        if (Runtime is null) return;
        var alreadyQuiet = Runtime.Metrics.Sleeping && held.Count == 0 && !Dragging && !jump;
        suppressed.UnionWith(held);
        held.Clear();
        jump = false;
        Dragging = false;
        releaseCapture();
        if (alreadyQuiet) return;
        cancel = true;
        PublishInput();
        Runtime.ClearInput();
    }

    internal void ReconcileKeyboard(IEnumerable<Key> physicallyDown)
    {
        var before = held.Count;
        var physical = physicallyDown.ToHashSet();
        suppressed.IntersectWith(physical);
        suppressed.UnionWith(physical.Except(held));
        held.IntersectWith(physical);
        if (held.Count != before) PublishInput();
    }

    internal void BeginDrag(Point target)
    {
        Dragging = true;
        dragTarget = target;
    }

    internal void MoveDrag(Point target) { dragTarget = target; PublishInput(); }
    internal void EndDrag(bool cancelForces = false, Action? releaseCapture = null)
    {
        Dragging = false;
        releaseCapture?.Invoke();
        cancel = cancelForces;
        PublishInput();
    }

    internal void PublishInput()
    {
        if (Runtime is null) return;
        Runtime.PublishInput(new((held.Contains(Key.D) ? 1 : 0) - (held.Contains(Key.A) ? 1 : 0),
            held.Contains(Key.W) ? 1 : 0, held.Contains(Key.S) ? 1 : 0, jump, Dragging,
            dragTarget.X, dragTarget.Y, held.Count > 0 || Dragging, cancel));
        jump = cancel = false;
    }
}
