using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;

namespace Dango.Server.Rooms.Management;

public static class RoomManagementPolicy
{
    public const int MaxActiveSharedRooms = 3;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(168);

    public static string Name(string? input)
    {
        if (input is null) throw new AccountFault("room_name_invalid");
        // EnumerateRunes replaces invalid UTF-16. Decode explicitly so malformed input is rejected.
        var remaining = input.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
                throw new AccountFault("room_name_invalid");
            remaining = remaining[consumed..];
        }
        var name = input.Trim().Normalize(NormalizationForm.FormC);
        if (name.EnumerateRunes().Count() is < 1 or > 64) throw new AccountFault("room_name_invalid");
        return name;
    }

    public static PersistentRoomKind CreateKind(string? kind) => kind switch
    {
        "privateShared" => PersistentRoomKind.PrivateShared,
        "publicShared" => PersistentRoomKind.PublicShared,
        _ => throw new AccountFault("room_kind_invalid")
    };

    public static string Kind(PersistentRoomKind kind) => kind switch
    {
        PersistentRoomKind.DesktopPet => "desktopPet",
        PersistentRoomKind.PrivateShared => "privateShared",
        PersistentRoomKind.PublicShared => "publicShared",
        _ => throw new AccountFault("room_invalid_state", 503)
    };

    public static void Capacity(PersistentRoomKind kind, int capacity)
    {
        if (kind == PersistentRoomKind.DesktopPet ? capacity != 2 : capacity is < 1 or > 12)
            throw new AccountFault("room_capacity_invalid");
        _ = Kind(kind);
    }

    public static void Request(Guid requestId)
    {
        if (requestId == Guid.Empty) throw new AccountFault("room_request_invalid");
    }

    public static void ExpectedVersion(long version)
    {
        if (version < 1) throw new AccountFault("room_version_invalid");
    }

    public static long NextVersion(long current, long expected)
    {
        ExpectedVersion(expected);
        if (current < 1) throw new AccountFault("room_invalid_state", 503);
        if (current != expected) throw new AccountFault("room_version_conflict", 409);
        if (current == long.MaxValue) throw new AccountFault("room_version_exhausted", 409);
        return current + 1;
    }

    public static void Quota(int activeSharedRooms)
    {
        if (activeSharedRooms >= MaxActiveSharedRooms) throw new AccountFault("room_quota_reached", 409);
    }

    public static void Seats(PersistentRoomKind kind, int capacity, int reservedSeats, int occupancy)
    {
        Capacity(kind, capacity);
        if ((kind == PersistentRoomKind.PublicShared ? occupancy : reservedSeats) > capacity)
            throw new AccountFault("room_capacity_conflict", 409);
    }

    public static void Active(PersistentRoom room)
    {
        if (room.Status != PersistentRoomStatus.Active) throw new AccountFault("room_state_conflict", 409);
    }

    public static void Shared(PersistentRoom room)
    {
        if (room.Kind == PersistentRoomKind.DesktopPet) throw new AccountFault("room_desktop_protected", 409);
    }

    public static void RecoveryDeadline(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is null || now >= until) throw new AccountFault("room_recovery_expired", 409);
    }

    public static PersistentRoomStatus? ListState(string? state) => state switch
    {
        "active" => PersistentRoomStatus.Active,
        "dissolved" => PersistentRoomStatus.Dissolved,
        "all" => null,
        _ => throw new AccountFault("room_list_state_invalid")
    };

    public static void PageSize(int limit)
    {
        if (limit is < 1 or > MaxPageSize) throw new AccountFault("room_page_size_invalid");
    }

    // Fixed property order and typed values avoid concatenation ambiguity and culture-sensitive hashes.
    public static string RequestHash(string operation, Guid? roomId, long? expectedVersion = null,
        PersistentRoomKind? kind = null, string? name = null, int? capacity = null, bool? confirmed = null) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            operation,
            roomId,
            expectedVersion,
            kind = kind is null ? null : Kind(kind.Value),
            name = name is null ? null : Name(name),
            capacity,
            confirmed
        })));
}
