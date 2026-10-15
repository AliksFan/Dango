using System.IO;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.AppShell.Rooms.Storage;

internal sealed record RoomSafePosition(double X, double SupportHeight);
internal sealed record RoomResumeDocument(int SchemaVersion, Guid ProfileId, Guid RoomId, int GeometryVersion,
    long LayoutVersion, long ResumeVersion, RoomSafePosition? Position, RoomCameraSnapshot Camera)
{
    internal void Validate(Guid profileId, Guid roomId)
    {
        if (SchemaVersion != 1 || ProfileId != profileId || RoomId != roomId || GeometryVersion != 1 ||
            LayoutVersion < 0 || ResumeVersion < 1 || !Enum.IsDefined(Camera.Mode) || !Enum.IsDefined(Camera.Preset) ||
            !double.IsFinite(Camera.RequestedPercent) || Camera.RequestedPercent <= 0 || Camera.RequestedPercent > 1000 ||
            !double.IsFinite(Camera.X) || !double.IsFinite(Camera.Y) ||
            (Position is { } point && (!double.IsFinite(point.X) || !double.IsFinite(point.SupportHeight))))
            throw new InvalidDataException("位置／视角文件无法识别；原件已保护。");
    }
}
internal sealed record RoomSelectionDocument(int SchemaVersion, Guid ProfileId, Guid? RoomId)
{
    internal void Validate(Guid profileId)
    {
        if (SchemaVersion != 1 || ProfileId != profileId || RoomId == Guid.Empty)
            throw new InvalidDataException("上次房间选择无法识别。");
    }
}

internal sealed class RoomResumeStore
{
    private readonly LocalRoomCatalogStore catalog;
    private readonly RoomFileStore files;
    private readonly HashSet<Guid> uncertain = [];
    internal RoomResumeStore(LocalRoomCatalogStore catalog, IFurnitureFileWriter? writer = null)
    { this.catalog = catalog; files = new(catalog.DataRoot, writer); }
    private static string Relative(Guid id) => $"device/rooms/{id:D}/resume.json";
    internal RoomDocument<RoomResumeDocument> Load(Guid roomId)
    {
        var profile = catalog.ReadCurrent().ProfileId;
        var read = files.Read<RoomResumeDocument>(Relative(roomId), value => value.Validate(profile, roomId));
        if (read.Valid) lock (uncertain) uncertain.Remove(roomId);
        return read;
    }
    internal FurnitureCommitResult Save(Guid id, long layoutVersion, RoomSafePosition? position, RoomCameraSnapshot camera)
    {
        try
        {
            lock (uncertain) if (uncertain.Contains(id)) return new(FurnitureCommitState.Unknown, "位置／视角保存结果待核对。");
            using var room = catalog.AcquireRoomGuard(id);
            var current = catalog.ReadCurrent();
            if (catalog.ReadLayout(id).LayoutVersion != layoutVersion)
                return new(FurnitureCommitState.NotCommitted, "布局版本已变化，未覆盖位置／视角。");
            using var held = files.Acquire(Relative(id));
            var old = files.Read<RoomResumeDocument>(Relative(id), value => value.Validate(current.ProfileId, id));
            if (old.Snapshot.State == RoomFileState.Protected) return new(FurnitureCommitState.NotCommitted, old.Snapshot.Warning);
            var candidate = new RoomResumeDocument(1, current.ProfileId, id, 1, layoutVersion,
                checked((old.Value?.ResumeVersion ?? 0) + 1), position, camera);
            candidate.Validate(current.ProfileId, id);
            var result = files.Write(Relative(id), old.Snapshot, RoomJson.Write(candidate),
                bytes => RoomJson.Read<RoomResumeDocument>(bytes).Validate(current.ProfileId, id));
            if (result.State == FurnitureCommitState.Unknown) lock (uncertain) uncertain.Add(id);
            return result;
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { return new(FurnitureCommitState.NotCommitted, "位置／视角未保存；上次存档保留（" + error.GetType().Name + "）。"); }
    }
    internal Guid? LoadSelection(out string? warning)
    {
        var current = catalog.ReadCurrent();
        var read = files.Read<RoomSelectionDocument>("device/rooms/selection.json", value => value.Validate(current.ProfileId));
        warning = read.Snapshot.Warning;
        return read.Value?.RoomId is { } id && current.Find(id)?.Life == LocalRoomLife.Active ? id : null;
    }
    internal FurnitureCommitResult SaveSelection(Guid id)
    {
        try
        {
            using var room = catalog.AcquireRoomGuard(id);
            var current = catalog.ReadCurrent();
            using var held = files.Acquire("device/rooms/selection.json");
            var old = files.Read<RoomSelectionDocument>("device/rooms/selection.json", value => value.Validate(current.ProfileId));
            return files.Write("device/rooms/selection.json", old.Snapshot,
                RoomJson.Write(new RoomSelectionDocument(1, current.ProfileId, id)),
                bytes => RoomJson.Read<RoomSelectionDocument>(bytes).Validate(current.ProfileId));
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { return new(FurnitureCommitState.NotCommitted, "上次房间选择未保存；原件保留。"); }
    }
}

internal static class RoomResumeSafety
{
    // Conservative neutral envelope only. Never changes native geometry, contacts or solver state.
    internal static bool CanRestore(RoomSafePosition point, FurnitureLayout layout, double width = 160, double height = 120)
    {
        var room = FurnitureLayoutContract.OrdinaryRoom;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.SupportHeight) || !double.IsFinite(width) || !double.IsFinite(height) ||
            width <= 0 || height <= 0 || point.SupportHeight < 0 || point.SupportHeight + height > room.Ceiling ||
            point.X - width / 2 < room.Left || point.X + width / 2 > room.Right) return false;
        if (point.SupportHeight > .5 && !layout.Items.Any(item => item.Kind == FurnitureItemKind.StaticPlatform &&
            Math.Abs(item.Anchor.Y - point.SupportHeight) <= .5 && Math.Abs(item.Anchor.X - point.X) + width / 2 <= room.PlatformWidth / 2))
            return false;
        var left = point.X - width / 2; var right = point.X + width / 2;
        var bottom = point.SupportHeight; var top = bottom + height;
        foreach (var item in layout.Items)
        {
            if (item.Kind == FurnitureItemKind.StaticPlatform)
            {
                if (left < item.Anchor.X + room.PlatformWidth / 2 && right > item.Anchor.X - room.PlatformWidth / 2 &&
                    bottom < item.Anchor.Y && top > item.Anchor.Y - room.PlatformHeight) return false;
            }
            else
            {
                var dx = item.Anchor.X - Math.Clamp(item.Anchor.X, left, right);
                var dy = item.Anchor.Y - Math.Clamp(item.Anchor.Y, bottom, top);
                if (dx * dx + dy * dy < room.BallRadius * room.BallRadius) return false;
            }
        }
        return true;
    }
}
