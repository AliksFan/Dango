namespace Dango.Desktop.AppShell.Rooms;

internal enum LocalRoomPresentation { Porch, Window, Hidden, DesktopPet }

// A successful Show/Loaded event or a stale scheduler callback cannot commit a handoff.
internal sealed class LocalRoomHandoffGate
{
    internal long Generation { get; private set; }
    internal LocalRoomPresentation Current { get; private set; } = LocalRoomPresentation.Porch;
    internal LocalRoomPresentation Target { get; private set; }
    internal bool Pending { get; private set; }

    internal long Begin(LocalRoomPresentation target)
    {
        if (Pending) throw new InvalidOperationException("场景切换尚未完成。");
        Target = target;
        Pending = true;
        return ++Generation;
    }

    internal bool TryComplete(long generation, LocalRoomPresentation target, bool healthyFrame)
    {
        if (!Pending || generation != Generation || target != Target || !healthyFrame) return false;
        Current = target;
        Pending = false;
        return true;
    }

    internal bool Fail(long generation)
    {
        if (!Pending || generation != Generation) return false;
        Pending = false;
        ++Generation;
        return true;
    }

    internal void Hide()
    {
        Pending = false;
        Current = LocalRoomPresentation.Hidden;
        ++Generation;
    }
}
