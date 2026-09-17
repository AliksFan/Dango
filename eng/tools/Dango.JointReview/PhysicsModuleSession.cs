using Dango.Physics;

namespace Dango.JointReview;

internal sealed class PhysicsModuleSession : IDisposable
{
    private readonly DangoPhysicsWorld world;
    private readonly DangoEntityId[] ids;
    private readonly bool[] active;

    internal PhysicsModuleSession(string path, int count, int layout)
    {
        if (count is < 1 or > 3 || layout is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(count));
        world = DangoPhysicsWorld.Create(path, capacity: count);
        try
        {
            ids = Enumerable.Range(0, count).Select(index => count == 3
                ? world.Add(layout == 1 ? 0 : layout == 2 ? (index == 2 ? 0 : index == 0 ? -1 : 1) : (index - 1) * 2.8,
                    layout == 1 ? index * 1.65 : layout == 2 && index == 2 ? 1.65 : 0)
                : world.Add(index == 0 ? 0 : layout == 1 ? 0 : layout == 2 ? 4 : layout == 3 ? -0.35 : layout == 4 ? 0.35 : 2.8,
                    index == 1 && (layout == 1 || layout >= 3) ? 1.65 : 0)).ToArray();
            active = Enumerable.Repeat(true, count).ToArray();
        }
        catch { world.Dispose(); throw; }
    }

    internal bool IsActive(int slot) => slot >= 0 && slot < active.Length && active[slot];
    internal int SlotFor(DangoEntityId id) => Array.IndexOf(ids, id);
    internal void Remove(int slot)
    {
        if (!IsActive(slot)) return;
        world.Remove(ids[slot]); active[slot] = false;
    }

    internal int Error => world.LastErrorCode;

    internal int ContactSteps { get; private set; }

    internal DangoBodyState[] Step(float[] controls)
    {
        if (controls.Length != ids.Length * 6) throw new ArgumentException("Each slot requires six control fields.", nameof(controls));
        var inputs = ids.Select((id, index) => (id, index)).Where(x => active[x.index]).Select(x => new DangoPhysicsInput(x.id,
            controls[x.index * 6], controls[x.index * 6 + 1], controls[x.index * 6 + 2] != 0,
            controls[x.index * 6 + 3] != 0, controls[x.index * 6 + 4], controls[x.index * 6 + 5])).ToArray();
        var frame = world.Step(BaselineWindow.FixedStep, inputs);
        ContactSteps = frame.ContactStepCount;
        return frame.Bodies.ToArray();
    }

    public void Dispose() => world.Dispose();
}
