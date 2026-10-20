using System.IO;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Physics;

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
    internal readonly record struct Envelope(double Left, double Right, double Height);

    // q contains the constant term followed by eight cosine/sine pairs, not scaffold nodes.
    // Each harmonic is bounded by hypot(cosineCoefficient, sineCoefficient) for every theta.
    internal static Envelope? BeltEnvelope(DangoBeltRoomFrame? frame)
    {
        if (frame is not { Fault: 0 } || frame.BodySnapshot.Count != 200 ||
            frame.BodySnapshot[0] != 1 || frame.BodySnapshot[1] != 200) return null;
        var q = frame.BodySnapshot;
        if (q.Skip(2).Take(36).Any(value => !double.IsFinite(value))) return null;
        double rx = 0, ry = 0;
        for (var harmonic = 1; harmonic <= 8; harmonic++)
        {
            var i = 2 + 4 * harmonic - 2;
            rx += Math.Sqrt(q[i] * q[i] + q[i + 2] * q[i + 2]);
            ry += Math.Sqrt(q[i + 1] * q[i + 1] + q[i + 3] * q[i + 3]);
        }
        // Outward rounding margin in room DIP; never use the sampled curve as a safety proof.
        const double margin = .001;
        var offset = (q[2] - q[36]) * 72;
        var result = new Envelope(offset - rx * 72 - margin, offset + rx * 72 + margin, 2 * ry * 72 + 2 * margin);
        return double.IsFinite(result.Left) && double.IsFinite(result.Right) && double.IsFinite(result.Height) &&
            result.Left < result.Right && result.Height > 0 ? result : null;
    }

    // Stats[12] is belt_bounds' continuous-extremum floor gap, not curve[256]'s sampled minimum.
    // A grounded platform/ball is insufficient; require ground-level bottom and no platform support ID.
    internal static bool HasBeltFloorSupport(DangoBeltRoomFrame? frame) =>
        frame is { Fault: 0, Tick: > 0 } && frame.BodySnapshot.Count == 200 && frame.Stats.Count >= 26 &&
        frame.Diagnostics.Count >= 20 && frame.BodySnapshot[82] == 1 && frame.Stats[14] == 1 &&
        frame.Stats[25] > 0 && frame.Diagnostics[18] == 0 && frame.Diagnostics[19] == 0 &&
        double.IsFinite(frame.Stats[12]) && Math.Abs(frame.Stats[12]) <= .003;

    // Conservative neutral envelope only. Never changes native geometry, contacts or solver state.
    internal static bool CanRestore(RoomSafePosition point, FurnitureLayout layout, double width = 160, double height = 120)
        => CanRestore(point, layout, new Envelope(-width / 2, width / 2, height));

    internal static bool CanRestore(RoomSafePosition point, FurnitureLayout layout, Envelope envelope)
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
