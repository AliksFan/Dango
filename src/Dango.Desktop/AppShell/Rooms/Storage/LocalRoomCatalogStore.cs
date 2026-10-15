using System.IO;
using System.Security.Cryptography;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;

namespace Dango.Desktop.AppShell.Rooms.Storage;

internal sealed record RoomCatalogResult(LocalRoomCatalog? Catalog, FurnitureCommitState State, string? Warning = null)
{
    internal bool Success => Catalog is not null && State == FurnitureCommitState.Committed;
}

internal sealed class LocalRoomCatalogStore
{
    internal const string CatalogPath = "rooms/catalog.json";
    private const string ImportPath = "rooms/legacy-import.json";
    private readonly RoomFileStore files;
    private readonly TimeProvider clock;
    private readonly DateTimeOffset initialUtc;
    private readonly long initialTimestamp;
    private bool uncertain;
    internal LocalRoomCatalogStore(string root, IFurnitureFileWriter? writer = null, TimeProvider? clock = null)
    {
        files = new(root, writer);
        this.clock = clock ?? TimeProvider.System;
        initialUtc = this.clock.GetUtcNow();
        initialTimestamp = this.clock.GetTimestamp();
    }
    internal string DataRoot => files.Root;
    internal static string LayoutRelativePath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("房间身份不能为空。", nameof(id));
        return $"rooms/{id:D}/furniture-layout.json";
    }
    internal string LayoutPath(Guid id) => files.PathFor(LayoutRelativePath(id));
    internal bool ClockReliable => Math.Abs(((clock.GetUtcNow() - initialUtc) - clock.GetElapsedTime(initialTimestamp)).TotalMinutes) < 2;

    internal RoomCatalogResult Refresh()
    {
        try
        {
            using var held = files.Acquire(CatalogPath);
            var read = ReadCatalog();
            if (read.Valid)
            {
                uncertain = false;
                var warning = LegacyChanged(read.Value!);
                return new(read.Value, FurnitureCommitState.Committed, warning);
            }
            if (read.Snapshot.State != RoomFileState.Missing) return Failure(read.Snapshot.Warning);
            if (uncertain) return Failure("目录保存结果待核对；不能按首次使用重新初始化。");
            return Initialize(read.Snapshot);
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { return Failure("目录暂不可用；原件已保护（" + error.GetType().Name + "）。"); }
    }

    private RoomCatalogResult Initialize(RoomFileSnapshot baseline)
    {
        // Hold the old layout lease after the catalog lease. Older versions use this same layout lock.
        using var oldLease = files.Acquire(LegacyRoomImport.LegacyPath);
        var old = files.Read(LegacyRoomImport.LegacyPath);
        if (old.State == RoomFileState.Protected) return Failure(old.Warning);
        FurnitureLayout layout;
        try { layout = old.State == RoomFileState.Missing ? FurnitureLayout.Empty :
                FurnitureLayoutSerializer.Deserialize(old.Bytes!, FurnitureLayoutContract.OrdinaryRoom); }
        catch (Exception error) when (RoomFileStore.IsStorageError(error)) { return Failure("旧布局无法识别；原件已保护。"); }
        var hash = old.Bytes is null ? null : Convert.ToHexString(SHA256.HashData(old.Bytes));
        var prepare = files.Read<LegacyRoomImport>(ImportPath, value => value.Validate());
        LegacyRoomImport import;
        if (prepare.Valid)
        {
            import = prepare.Value!;
            if (import.SourceSha256 != hash) return Failure("旧布局已变化；保留来源与导入现场，请核对后重试。");
        }
        else if (prepare.Snapshot.State == RoomFileState.Missing)
        {
            import = new(1, Guid.NewGuid(), Guid.NewGuid(), LegacyRoomImport.LegacyPath, hash);
            var result = files.Write(ImportPath, prepare.Snapshot, RoomJson.Write(import), bytes => RoomJson.Read<LegacyRoomImport>(bytes).Validate());
            if (result.State != FurnitureCommitState.Committed) return ObserveFailure(result);
        }
        else return Failure(prepare.Snapshot.Warning);

        var target = LayoutRelativePath(import.RoomId);
        var targetSnapshot = files.Read(target);
        var candidate = FurnitureLayoutSerializer.Serialize(layout, FurnitureLayoutContract.OrdinaryRoom, import.RoomId);
        if (targetSnapshot.State == RoomFileState.Missing)
        {
            var result = files.Write(target, targetSnapshot, candidate,
                bytes => FurnitureLayoutSerializer.Deserialize(bytes, FurnitureLayoutContract.OrdinaryRoom, import.RoomId));
            if (result.State != FurnitureCommitState.Committed) return ObserveFailure(result);
        }
        else if (targetSnapshot.State != RoomFileState.Present || !candidate.AsSpan().SequenceEqual(targetSnapshot.Bytes))
            return Failure("导入目标已存在不同内容；禁止覆盖。");
        if (!RoomFileStore.Same(files.Read(LegacyRoomImport.LegacyPath), old)) return Failure("旧布局在导入期间变化；原件与目标已保留。");
        var room = NewRoom(import.RoomId, "普通房", LocalRoomKind.Private, 6);
        var catalog = new LocalRoomCatalog(1, import.ProfileId, 1, Guid.NewGuid(), Fingerprint(import), [room], import);
        return Publish(catalog, baseline);
    }

    internal RoomCatalogResult Create(Guid operationId, Guid roomId, string name, LocalRoomKind kind, int capacity)
        => Mutate(operationId, Fingerprint(new { Action = "Create", roomId, name, kind, capacity }), catalog =>
        {
            if (catalog.Find(roomId) is { } existing)
            {
                if (existing.Life == LocalRoomLife.Active && existing.Name == name.Trim() && existing.Kind == kind && existing.Capacity == capacity)
                    return catalog;
                throw new InvalidOperationException("房间身份已使用，不能覆盖。");
            }
            if (catalog.ActiveCount >= 3) throw new InvalidOperationException("普通房已满3间，请先解散一间。");
            var room = NewRoom(roomId, name.Trim(), kind, capacity);
            room.Validate();
            var path = LayoutRelativePath(roomId);
            var snapshot = files.Read(path);
            var bytes = FurnitureLayoutSerializer.Serialize(FurnitureLayout.Empty, FurnitureLayoutContract.OrdinaryRoom, roomId);
            if (snapshot.State == RoomFileState.Missing)
            {
                var result = files.Write(path, snapshot, bytes,
                    value => FurnitureLayoutSerializer.Deserialize(value, FurnitureLayoutContract.OrdinaryRoom, roomId));
                if (result.State != FurnitureCommitState.Committed) throw new InvalidOperationException(result.Warning);
            }
            else if (snapshot.State != RoomFileState.Present || !bytes.AsSpan().SequenceEqual(snapshot.Bytes))
                throw new InvalidOperationException("目标存档已存在不明内容；禁止覆盖。");
            return catalog with { Rooms = [.. catalog.Rooms, room] };
        });

    internal RoomCatalogResult Dissolve(Guid operationId, Guid roomId) => Mutate(operationId, Fingerprint(new { Action = "Dissolve", roomId }), catalog =>
    {
        var room = catalog.Find(roomId) ?? throw new InvalidOperationException("房间已不在目录中。");
        if (room.Source != LocalRoomSource.LocalOnly) throw new InvalidOperationException("关联房间需联网确认解散，不能仅移除本机缓存。");
        if (room.Life == LocalRoomLife.Dissolved) return catalog;
        if (!ClockReliable) throw new InvalidOperationException("本机时间异常，请校准后重试。");
        var now = clock.GetUtcNow();
        return Replace(catalog, room with { Life = LocalRoomLife.Dissolved, DissolvedAtUtc = now, RestoreUntilUtc = now.AddDays(7) });
    });

    internal RoomRestoreAvailability RestoreAvailability(LocalRoomCatalog catalog, LocalRoomRecord room)
    {
        if (room.Life != LocalRoomLife.Dissolved) return RoomRestoreAvailability.NotDissolved;
        if (room.Source != LocalRoomSource.LocalOnly) return RoomRestoreAvailability.RequiresOnline;
        var now = clock.GetUtcNow();
        if (!ClockReliable || now < room.DissolvedAtUtc) return RoomRestoreAvailability.TimeUncertain;
        if (now >= room.RestoreUntilUtc) return RoomRestoreAvailability.Expired;
        return catalog.ActiveCount >= 3 ? RoomRestoreAvailability.QuotaFull : RoomRestoreAvailability.Available;
    }
    internal RoomCatalogResult Restore(Guid operationId, Guid roomId) => Mutate(operationId, Fingerprint(new { Action = "Restore", roomId }), catalog =>
    {
        var room = catalog.Find(roomId) ?? throw new InvalidOperationException("房间已不在目录中。");
        if (room.Life == LocalRoomLife.Active) return catalog;
        var availability = RestoreAvailability(catalog, room);
        if (availability != RoomRestoreAvailability.Available) throw new InvalidOperationException(availability switch
        {
            RoomRestoreAvailability.QuotaFull => "普通房已满3间，请先解散一间。",
            RoomRestoreAvailability.Expired => "已超过7天常规恢复期；本机存档保留。",
            RoomRestoreAvailability.TimeUncertain => "本机时间异常，请校准后重试。",
            _ => "关联房间需联网确认恢复。",
        });
        ReadLayout(roomId); // A missing or wrong-room layout must never publish a recovered empty room.
        return Replace(catalog, room with { Life = LocalRoomLife.Active, DissolvedAtUtc = null, RestoreUntilUtc = null });
    });

    internal FurnitureLayout ReadLayout(Guid id)
    {
        var read = files.Read(LayoutRelativePath(id));
        if (read.State != RoomFileState.Present) throw new InvalidDataException("房间布局缺失或受保护。");
        return FurnitureLayoutSerializer.Deserialize(read.Bytes!, FurnitureLayoutContract.OrdinaryRoom, id);
    }
    internal IDisposable AcquireLayoutGuard(Guid id)
    {
        if (uncertain) throw new InvalidOperationException("目录结果待核对，暂不可编辑。");
        var held = files.Acquire(CatalogPath);
        try
        {
            var read = ReadCatalog();
            if (!read.Valid || read.Value!.Find(id) is not { CanEditOffline: true })
                throw new InvalidOperationException("房间已解散、资格未知或离线布局只读。");
            _ = files.PathFor(LayoutRelativePath(id));
            return held;
        }
        catch { held.Dispose(); throw; }
    }
    internal FurnitureLayoutStore CreateLayoutStore(Guid id) => new(contract: FurnitureLayoutContract.OrdinaryRoom,
        roomId: id, acquireWriteGuard: () => AcquireLayoutGuard(id));

    private RoomCatalogResult Mutate(Guid operationId, string fingerprint, Func<LocalRoomCatalog, LocalRoomCatalog> mutation)
    {
        if (uncertain) return Failure("目录保存结果待核对，请先重新读取目录。");
        if (operationId == Guid.Empty) return Failure("操作身份不能为空。");
        try
        {
            using var held = files.Acquire(CatalogPath);
            var read = ReadCatalog();
            if (!read.Valid) return Failure(read.Snapshot.Warning ?? "目录尚未建立，请先重试读取。");
            var current = read.Value!;
            if (current.LastOperationId == operationId) return current.LastOperationFingerprint == fingerprint ?
                    new(current, FurnitureCommitState.Committed) : Failure("同一操作身份不能改变内容。");
            var candidate = mutation(current);
            if (ReferenceEquals(candidate, current)) return new(current, FurnitureCommitState.Committed);
            candidate = candidate with { CatalogVersion = checked(current.CatalogVersion + 1), LastOperationId = operationId, LastOperationFingerprint = fingerprint };
            return Publish(candidate, read.Snapshot);
        }
        catch (Exception error) when (RoomFileStore.IsStorageError(error))
        { return Failure(error is InvalidOperationException ? error.Message : "目录操作失败；原件保留（" + error.GetType().Name + "）。"); }
    }
    private RoomCatalogResult Publish(LocalRoomCatalog catalog, RoomFileSnapshot baseline)
    {
        catalog.Validate();
        var result = files.Write(CatalogPath, baseline, RoomJson.Write(catalog), bytes => RoomJson.Read<LocalRoomCatalog>(bytes).Validate());
        if (result.State != FurnitureCommitState.Committed) return ObserveFailure(result);
        return new(catalog, result.State, result.Warning);
    }
    private RoomCatalogResult ObserveFailure(FurnitureCommitResult result)
    {
        if (result.State == FurnitureCommitState.Unknown) uncertain = true;
        return new(null, result.State, result.Warning);
    }
    private RoomDocument<LocalRoomCatalog> ReadCatalog() => files.Read<LocalRoomCatalog>(CatalogPath, value => value.Validate());
    private string? LegacyChanged(LocalRoomCatalog catalog)
    {
        if (catalog.LegacyImport is not { } import) return null;
        var source = files.Read(LegacyRoomImport.LegacyPath);
        var hash = source.Bytes is null ? null : Convert.ToHexString(SHA256.HashData(source.Bytes));
        return source.State == RoomFileState.Protected || hash != import.SourceSha256 ?
            "旧固定房文件已变化；新房存档与旧文件均保留，未自动覆盖。" : null;
    }
    private LocalRoomRecord NewRoom(Guid id, string name, LocalRoomKind kind, int capacity) => new(id, name, kind, capacity,
        clock.GetUtcNow(), LocalRoomSource.LocalOnly, null, null, RoomAccessEvidence.Unopened, LocalRoomLife.Active, null, null);
    private static LocalRoomCatalog Replace(LocalRoomCatalog catalog, LocalRoomRecord room) =>
        catalog with { Rooms = catalog.Rooms.Select(value => value.RoomId == room.RoomId ? room : value).ToArray() };
    private static RoomCatalogResult Failure(string? warning) => new(null, FurnitureCommitState.NotCommitted, warning ?? "目录原件受保护。");
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(RoomJson.Write(value)));
}
