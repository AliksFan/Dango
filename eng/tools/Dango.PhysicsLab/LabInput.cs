using Dango.Physics;

namespace Dango.PhysicsLab;

public enum LabKey { Left, Right, Stretch, Squash, Jump, Interact }
[Flags]
public enum LabModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Windows = 8 }

public readonly record struct LabInputSnapshot(double Horizontal, double Downward, double Stretch,
    bool InputActive, bool JumpPressed, bool InteractPressed, bool CancelForces)
{
    public DangoLabInput ToPhysicsInput(DangoEntityId id) => new(id, Horizontal, Downward, JumpPressed,
        Stretch: Stretch, InputActive: InputActive, CancelForces: CancelForces);
}

/// <summary>Window-local input ownership. No global hooks, polling, or synthetic key repeats.</summary>
public sealed class LabInput
{
    private readonly HashSet<LabKey> held = [];
    private readonly HashSet<LabKey> suppressed = [];
    private bool jump, interact, cancel;

    public void KeyDown(LabKey key, bool isRepeat = false, LabModifiers modifiers = LabModifiers.None, bool blocked = false)
    {
        if (!Enum.IsDefined(key)) throw new ArgumentOutOfRangeException(nameof(key));
        if (blocked || modifiers != LabModifiers.None)
        {
            Cancel();
            suppressed.Add(key);
            return;
        }
        if (isRepeat || suppressed.Contains(key) || !held.Add(key)) return;
        if (key == LabKey.Jump) jump = true;
        if (key == LabKey.Interact) interact = true;
    }

    // Always call this before focus/modifier/Handled filtering.
    public void KeyUp(LabKey key)
    {
        held.Remove(key);
        suppressed.Remove(key);
    }

    public void Cancel()
    {
        suppressed.UnionWith(held);
        held.Clear();
        jump = interact = false;
        cancel = true;
    }

    /// <summary>On activation: observe releases, suppress any still-held keys; never start actions.</summary>
    public void ReconcileHeld(IEnumerable<LabKey> physicallyHeld)
    {
        ArgumentNullException.ThrowIfNull(physicallyHeld);
        var physical = physicallyHeld.ToHashSet();
        suppressed.IntersectWith(physical);
        foreach (var key in physical)
            if (!held.Contains(key)) suppressed.Add(key);
        held.IntersectWith(physical);
    }

    public LabInputSnapshot Consume()
    {
        var stretch = held.Contains(LabKey.Stretch);
        var squash = held.Contains(LabKey.Squash);
        var result = new LabInputSnapshot(
            (held.Contains(LabKey.Right) ? 1 : 0) - (held.Contains(LabKey.Left) ? 1 : 0),
            squash && !stretch ? 1 : 0, stretch && !squash ? 1 : 0,
            held.Count > 0, jump, interact, cancel || (stretch && squash));
        jump = interact = cancel = false;
        return result;
    }
}
