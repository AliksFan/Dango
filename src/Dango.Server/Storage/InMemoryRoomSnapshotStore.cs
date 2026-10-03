using Dango.Protocol;

namespace Dango.Server.Storage;

// This is a single-writer, memory-only seam, not persistence or restart recovery.
public interface IRoomSnapshotStore
{
    RoomSnapshot? Read();
    void Save(RoomSnapshot snapshot);
}

public sealed class InMemoryRoomSnapshotStore : IRoomSnapshotStore
{
    private RoomSnapshot? latest;
    public RoomSnapshot? Read() => Volatile.Read(ref latest);
    public void Save(RoomSnapshot snapshot) => Volatile.Write(ref latest, snapshot);
}
