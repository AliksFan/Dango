using System.IO;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation;

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
internal sealed record DangoRoomPositionDocument(int SchemaVersion, Guid ProfileId, Guid RoomId, Guid DangoId,
    int GeometryVersion, long LayoutVersion, long ResumeVersion, RoomSafePosition? Position)
{
    internal void Validate(Guid profile, Guid room, Guid dango)
    {
        if (DangoId == Guid.Empty || DangoId != dango) throw new InvalidDataException("团子身份不匹配；原件已保护。");
        new RoomResumeDocument(SchemaVersion, ProfileId, RoomId, GeometryVersion, LayoutVersion, ResumeVersion,
            Position, new(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0)).Validate(profile, room);
    }
}
internal sealed record RoomSharedCameraDocument(int SchemaVersion, Guid ProfileId, Guid RoomId, long Version, RoomCameraSnapshot Camera)
{
    internal void Validate(Guid profile, Guid room) =>
        new RoomResumeDocument(SchemaVersion, ProfileId, RoomId, 1, 0, Version, null, Camera).Validate(profile, room);
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
    private readonly HashSet<string> uncertainTargets = new(StringComparer.Ordinal);
    private static string PositionRelative(Guid room, Guid dango) => $"device/rooms/{room:D}/dangos/{dango:D}/resume.json";
    private static string CameraRelative(Guid room) => $"device/rooms/{room:D}/camera.json";
    private RoomDocument<T> ReadTarget<T>(string path, Action<T> validate) where T : class
    {
        var read = files.Read(path, validate);
        if (read.Valid) lock (uncertainTargets) uncertainTargets.Remove(path);
        return read;
    }
    internal RoomDocument<RoomResumeDocument> LoadForDango(Guid roomId, Guid dangoId, Guid? legacyDangoId = null)
    {
        if (dangoId == Guid.Empty) throw new ArgumentException("团子身份不能为空。", nameof(dangoId));
        var profile = catalog.ReadCurrent().ProfileId;
        var position = ReadTarget<DangoRoomPositionDocument>(PositionRelative(roomId, dangoId), v => v.Validate(profile, roomId, dangoId));
        var camera = ReadTarget<RoomSharedCameraDocument>(CameraRelative(roomId), v => v.Validate(profile, roomId));
        if (position.Snapshot.State == RoomFileState.Protected) return new(null, position.Snapshot);
        if (camera.Snapshot.State == RoomFileState.Protected) return new(null, camera.Snapshot);
        // The legacy file remains an immutable source. Only its original identity may inherit its point.
        var legacy = position.Value is null || camera.Value is null ? Load(roomId) : null;
        var point = position.Value?.Position;
        if (position.Value is null && legacyDangoId == dangoId) point = legacy?.Value?.Position;
        var view = camera.Value?.Camera ?? legacy?.Value?.Camera;
        if (view is null)
        {
            if (legacy?.Snapshot.State == RoomFileState.Protected) return new(null, legacy.Snapshot);
            view = new(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
        }
        var result = new RoomResumeDocument(1, profile, roomId, 1,
            position.Value?.LayoutVersion ?? (legacyDangoId == dangoId ? legacy?.Value?.LayoutVersion : null) ?? 0,
            position.Value?.ResumeVersion ?? 1, point, view.Value);
        return new(result, new(RoomFileState.Present, RoomJson.Write(result)));
    }
    internal FurnitureCommitResult SaveForDango(Guid roomId, Guid dangoId, long layoutVersion, RoomSafePosition? position, RoomCameraSnapshot camera)
    {
        var pointCommitted = false;
        try
        {
            if (dangoId == Guid.Empty) throw new ArgumentException("团子身份不能为空。", nameof(dangoId));
            using var room = catalog.AcquireRoomGuard(roomId);
            var profile = catalog.ReadCurrent().ProfileId;
            if (catalog.ReadLayout(roomId).LayoutVersion != layoutVersion)
                return new(FurnitureCommitState.NotCommitted, "布局版本已变化，未覆盖位置／视角。");
            var pointPath = PositionRelative(roomId, dangoId); var cameraPath = CameraRelative(roomId);
            using var pointLock = files.Acquire(pointPath);
            using var cameraLock = files.Acquire(cameraPath);
            var oldPoint = files.Read<DangoRoomPositionDocument>(pointPath, v => v.Validate(profile, roomId, dangoId));
            var oldCamera = files.Read<RoomSharedCameraDocument>(cameraPath, v => v.Validate(profile, roomId));
            lock (uncertainTargets)
                if (uncertainTargets.Contains(pointPath) || uncertainTargets.Contains(cameraPath))
                    return new(FurnitureCommitState.Unknown, "位置或视角保存结果待核对。");
            if (oldPoint.Snapshot.State == RoomFileState.Protected || oldCamera.Snapshot.State == RoomFileState.Protected)
                return new(FurnitureCommitState.NotCommitted, oldPoint.Snapshot.Warning ?? oldCamera.Snapshot.Warning);
            var pointDoc = new DangoRoomPositionDocument(1, profile, roomId, dangoId, 1, layoutVersion,
                checked((oldPoint.Value?.ResumeVersion ?? 0) + 1), position);
            var cameraDoc = new RoomSharedCameraDocument(1, profile, roomId, checked((oldCamera.Value?.Version ?? 0) + 1), camera);
            pointDoc.Validate(profile, roomId, dangoId); cameraDoc.Validate(profile, roomId);
            var pointResult = files.Write(pointPath, oldPoint.Snapshot, RoomJson.Write(pointDoc),
                bytes => RoomJson.Read<DangoRoomPositionDocument>(bytes).Validate(profile, roomId, dangoId));
            if (pointResult.State == FurnitureCommitState.Unknown) lock (uncertainTargets) uncertainTargets.Add(pointPath);
            if (pointResult.State != FurnitureCommitState.Committed) return pointResult;
            pointCommitted = true;
            var cameraResult = files.Write(cameraPath, oldCamera.Snapshot, RoomJson.Write(cameraDoc),
                bytes => RoomJson.Read<RoomSharedCameraDocument>(bytes).Validate(profile, roomId));
            if (cameraResult.State == FurnitureCommitState.Unknown) lock (uncertainTargets) uncertainTargets.Add(cameraPath);
            return cameraResult.State == FurnitureCommitState.Committed ? cameraResult :
                new(FurnitureCommitState.Unknown, "位置已保存，视角未确认保存；请重新核对。" + cameraResult.Warning);
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { return pointCommitted ? new(FurnitureCommitState.Unknown, "位置已保存，视角未确认保存。") :
            new(FurnitureCommitState.NotCommitted, "位置／视角未保存；上次存档保留（" + error.GetType().Name + "）。"); }
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
        => CanRestore(point, layout, new BodySupportEnvelope(-width / 2, width / 2, height));

    internal static bool CanRestore(RoomSafePosition point, FurnitureLayout layout, BodySupportEnvelope envelope)
    {
        var width = envelope.Right - envelope.Left;
        var height = envelope.Height;
        var room = FurnitureLayoutContract.OrdinaryRoom;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.SupportHeight) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(envelope.Left) || !double.IsFinite(envelope.Right) ||
            width <= 0 || height <= 0 || point.SupportHeight < 0 || point.SupportHeight + height > room.Ceiling ||
            point.X + envelope.Left < room.Left || point.X + envelope.Right > room.Right) return false;
        if (point.SupportHeight > .5 && !layout.Items.Any(item => item.Kind == FurnitureItemKind.StaticPlatform &&
            Math.Abs(item.Anchor.Y - point.SupportHeight) <= .5 && Math.Abs(item.Anchor.X - point.X) + width / 2 <= room.PlatformWidth / 2))
            return false;
        var left = point.X + envelope.Left; var right = point.X + envelope.Right;
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
