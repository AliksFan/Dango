using System.IO;

namespace Dango.Desktop.AppShell.Rooms.Storage;

internal enum LocalRoomKind { Private, Public }
internal enum LocalRoomSource { LocalOnly, ServerCache }
internal enum LocalRoomLife { Active, Dissolved }
internal enum RoomRestoreAvailability { Available, Expired, TimeUncertain, RequiresOnline, NotDissolved, QuotaFull }

// Evidence is supplied by a future authoritative adapter; online head counts are deliberately absent.
internal sealed record RoomAccessEvidence(bool OwnerKnown, bool OnlyOwnerCanEnter, bool OpeningKnown,
    bool HasEverOpened, bool PendingInvitation, bool PublicEntryEnabled, bool OtherEntitlements)
{
    internal static RoomAccessEvidence Unopened { get; } = new(true, true, true, false, false, false, false);
    internal bool CanEditOffline(int capacity) => OwnerKnown &&
        !PendingInvitation && !PublicEntryEnabled && !OtherEntitlements &&
        ((capacity == 1 && OnlyOwnerCanEnter) || (OpeningKnown && !HasEverOpened));
}

internal sealed record LocalRoomRecord(Guid RoomId, string Name, LocalRoomKind Kind, int Capacity,
    DateTimeOffset CreatedAtUtc, LocalRoomSource Source, Guid? ServerRoomId, long? ServerVersion,
    RoomAccessEvidence Access, LocalRoomLife Life, DateTimeOffset? DissolvedAtUtc, DateTimeOffset? RestoreUntilUtc)
{
    internal bool CanEditOffline => Life == LocalRoomLife.Active && Access.CanEditOffline(Capacity);
    internal void Validate()
    {
        if (RoomId == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 64 ||
            Name.Any(char.IsControl) || Capacity is < 1 or > 12 || !Enum.IsDefined(Kind) ||
            !Enum.IsDefined(Source) || !Enum.IsDefined(Life) || CreatedAtUtc.Offset != TimeSpan.Zero || Access is null)
            throw new InvalidDataException("房间条目无效。");
        if (Source == LocalRoomSource.LocalOnly &&
            (ServerRoomId is not null || ServerVersion is not null || Access != RoomAccessEvidence.Unopened))
            throw new InvalidDataException("本地房间来源无效。");
        if (Source == LocalRoomSource.ServerCache && (ServerRoomId is null || ServerRoomId == Guid.Empty || ServerVersion is null or < 0))
            throw new InvalidDataException("关联房间身份无效。");
        if (Life == LocalRoomLife.Active && (DissolvedAtUtc is not null || RestoreUntilUtc is not null))
            throw new InvalidDataException("有效房间含解散状态。");
        if (Life == LocalRoomLife.Dissolved && (DissolvedAtUtc is not { Offset: var offset } from ||
            offset != TimeSpan.Zero || RestoreUntilUtc is not { Offset: var endOffset } until || endOffset != TimeSpan.Zero ||
            from > DateTimeOffset.MaxValue.AddDays(-7) || until != from.AddDays(7)))
            throw new InvalidDataException("解散恢复期限无效。");
    }
}

internal sealed record LegacyRoomImport(int SchemaVersion, Guid ProfileId, Guid RoomId, string SourceRelativePath, string? SourceSha256)
{
    internal const string LegacyPath = "rooms/local-ordinary-room/furniture-layout.json";
    internal void Validate()
    {
        if (SchemaVersion != 1 || ProfileId == Guid.Empty || RoomId == Guid.Empty || SourceRelativePath != LegacyPath ||
            (SourceSha256 is not null && (SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit))))
            throw new InvalidDataException("旧布局导入记录无法识别。");
    }
}

internal sealed record LocalRoomCatalog(int SchemaVersion, Guid ProfileId, long CatalogVersion, Guid LastOperationId, string LastOperationFingerprint,
    LocalRoomRecord[] Rooms, LegacyRoomImport? LegacyImport)
{
    internal int ActiveCount => Rooms.Count(room => room.Life == LocalRoomLife.Active);
    internal LocalRoomRecord? Find(Guid id) => Rooms.SingleOrDefault(room => room.RoomId == id);
    internal void Validate()
    {
        if (SchemaVersion != 1 || ProfileId == Guid.Empty || CatalogVersion < 1 || LastOperationId == Guid.Empty ||
            LastOperationFingerprint is null || LastOperationFingerprint.Length != 64 || !LastOperationFingerprint.All(Uri.IsHexDigit) ||
            Rooms is null || Rooms.Any(room => room is null) || Rooms.Select(room => room.RoomId).Distinct().Count() != Rooms.Length || ActiveCount > 3)
            throw new InvalidDataException("房间目录无法识别；原件已保护。");
        foreach (var room in Rooms) room.Validate();
        if (LegacyImport is { } legacy)
        {
            legacy.Validate();
            if (legacy.ProfileId != ProfileId || Find(legacy.RoomId) is null)
                throw new InvalidDataException("旧布局导入身份不匹配。");
        }
    }
}
