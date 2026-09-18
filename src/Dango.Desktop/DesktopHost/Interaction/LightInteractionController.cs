using Dango.Core.Coordinates;
using Dango.Physics;

namespace Dango.Desktop.DesktopHost.Interaction;

public readonly record struct LightInteractionTarget(DangoEntityId Entity, long Generation);

public enum LightInteractionResult { None, BeginDrag, Click, Pet, Cancelled }

public sealed class LightInteractionController
{
    private LightInteractionTarget? target;
    private LogicalPoint down;
    private LogicalPoint previous;
    private long downTime;
    private long lastTime;
    private long nextSession;
    private bool petBranch;
    private bool consumed;
    private double maximumDistance;
    private double maximumVerticalOffset;

    public bool IsPressed => target is not null;
    public long SessionId { get; private set; }
    public LightInteractionTarget? Target => target;

    public bool TryBegin(LightInteractionTarget target, LogicalPoint point, long nowMilliseconds, bool shift, out long sessionId)
    {
        sessionId = 0;
        if (IsPressed || target.Entity.Value <= 0 || target.Generation <= 0 || !Finite(point)) return false;
        this.target = target;
        down = previous = point;
        downTime = lastTime = nowMilliseconds;
        petBranch = shift;
        consumed = false;
        maximumDistance = maximumVerticalOffset = 0;
        SessionId = checked(++nextSession);
        sessionId = SessionId;
        return true;
    }

    public LightInteractionResult Move(long sessionId, LogicalPoint point, long nowMilliseconds, bool shift, LogicalRect? targetBounds) =>
        Process(sessionId, point, nowMilliseconds, shift, targetBounds, ending: false);

    public LightInteractionResult End(long sessionId, LogicalPoint point, long nowMilliseconds, bool shift, LogicalRect? targetBounds) =>
        Process(sessionId, point, nowMilliseconds, shift, targetBounds, ending: true);

    public bool Cancel(long sessionId)
    {
        if (!Matches(sessionId)) return false;
        Clear();
        return true;
    }

    public void Clear()
    {
        target = null;
        petBranch = consumed = false;
        maximumDistance = maximumVerticalOffset = 0;
    }

    private LightInteractionResult Process(long sessionId, LogicalPoint point, long nowMilliseconds, bool shift,
        LogicalRect? targetBounds, bool ending)
    {
        if (!Matches(sessionId)) return LightInteractionResult.None;
        if (!Finite(point) || nowMilliseconds < lastTime)
        {
            Clear();
            return LightInteractionResult.Cancelled;
        }
        lastTime = nowMilliseconds;

        if (consumed)
        {
            if (ending || !shift) Clear();
            return LightInteractionResult.None;
        }

        if (!petBranch)
        {
            Observe(point);
            if (!ending && maximumDistance >= 3)
            {
                Clear();
                return LightInteractionResult.BeginDrag;
            }

            if (ending)
            {
                var click = maximumDistance < 3 && nowMilliseconds - downTime <= 250 && targetBounds is { } bounds && bounds.Contains(point);
                Clear();
                return click ? LightInteractionResult.Click : LightInteractionResult.None;
            }

            return LightInteractionResult.None;
        }

        if (!shift || targetBounds is not { } petBounds || !petBounds.Contains(previous) || !petBounds.Contains(point) ||
            nowMilliseconds - downTime > 600)
        {
            Clear();
            return LightInteractionResult.Cancelled;
        }

        Observe(point);
        if (maximumVerticalOffset > 8)
        {
            Clear();
            return LightInteractionResult.Cancelled;
        }

        if (Math.Abs(point.X - down.X) >= 16)
        {
            consumed = true;
            if (ending) Clear();
            return LightInteractionResult.Pet;
        }

        if (ending) Clear();
        return LightInteractionResult.None;
    }

    private bool Matches(long sessionId) => IsPressed && sessionId == SessionId;

    private void Observe(LogicalPoint point)
    {
        var dx = point.X - down.X;
        var dy = point.Y - down.Y;
        maximumDistance = Math.Max(maximumDistance, Math.Sqrt((dx * dx) + (dy * dy)));
        maximumVerticalOffset = Math.Max(maximumVerticalOffset, Math.Abs(dy));
        previous = point;
    }

    private static bool Finite(LogicalPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
