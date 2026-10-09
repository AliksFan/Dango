using System.IO;

namespace Dango.Desktop.AppShell.Persistence;

/// <summary>Retains playable in-memory changes when writes fail; conflicts protect the on-disk original.</summary>
public sealed class LocalRoomSaveSession
{
    private readonly string path;
    private readonly LocalRoomSaveStore store;
    private LocalRoomSaveState state;
    private LocalRoomSaveState savedState;
    private ExactFileSnapshot? original;
    private bool pendingFirstSave;

    public LocalRoomSaveSession(string path, LocalRoomSaveStore? store = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        this.store = store ?? new();
        var result = this.store.LoadOrDefault(path);
        state = savedState = result.State;
        original = result.Original;
        pendingFirstSave = original is { Exists: false } && !result.PreserveOriginal;
        IsWriteProtected = result.PreserveOriginal;
        StatusMessage = result.Warning ?? (pendingFirstSave ? "已创建本地房间；首次保存后保留房间标识。" : "本地房间存档已读取。");
    }

    public LocalRoomSaveData OrdinaryRoom => state.OrdinaryRoom;
    public LocalRoomSaveData DesktopPetRoom => state.DesktopPetRoom;
    public bool HasPendingSave => pendingFirstSave || state != savedState;
    public bool IsWriteProtected { get; private set; }
    public string StatusMessage { get; private set; }

    public void UpdateRoom(LocalRoomSaveData room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var updated = room.RoomType switch
        {
            "ordinary" when room.RoomId == OrdinaryRoom.RoomId => state with { OrdinaryRoom = room },
            "desktopPet" when room.RoomId == DesktopPetRoom.RoomId => state with { DesktopPetRoom = room },
            _ => throw new InvalidDataException("不能改变本地房间标识或类型。"),
        };
        updated.Validate();
        state = updated;
    }

    public bool TrySave()
    {
        if (IsWriteProtected || original is null)
        {
            StatusMessage = "本地房间存档未保存：原文件已保护。本次游玩继续保留；检查原文件后重新加载。";
            return false;
        }
        if (!HasPendingSave) return true;
        try
        {
            if (!store.TrySave(path, state, original, out var saved))
            {
                IsWriteProtected = true;
                StatusMessage = "本地房间存档已被其他操作更改，原文件已保护且未覆盖；本次游玩仍保留，请重新加载。";
                return false;
            }
            original = saved;
            savedState = state;
            pendingFirstSave = false;
            StatusMessage = "本地房间已保存。";
            return true;
        }
        catch (InvalidDataException)
        {
            IsWriteProtected = true;
            StatusMessage = "本地房间存档无法安全替换，原文件已保护且未覆盖；本次游玩仍保留，请检查并重新加载。";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusMessage = $"本地房间尚未保存，本次游玩仍保留（{exception.GetType().Name}）；可以重试。";
            return false;
        }
    }
}
