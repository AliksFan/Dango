using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;

namespace Dango.Client.Sync.Accounts;

public sealed partial class FormalAccountClient
{
    private const int MaximumRoomPageBytes = 65536;
    private static readonly HashSet<string> RoomErrorCodes = new(StringComparer.Ordinal)
    {
        "room_query_invalid", "room_name_invalid", "room_kind_invalid", "room_capacity_invalid", "room_request_invalid",
        "room_version_invalid", "room_version_conflict", "room_version_exhausted", "room_quota_reached",
        "room_capacity_conflict", "room_state_conflict", "room_desktop_protected", "room_recovery_expired",
        "room_list_state_invalid", "room_page_size_invalid", "room_confirmation_required", "room_request_conflict",
        "room_admission_version_exhausted", "room_state_version_exhausted", "room_cursor_invalid", "room_not_found"
    };

    public IRoomManagementClient CreateRoomManagementClient(AccountIdentity identity)
    {
        ThrowIfDisposed();
        if (identity is null || identity.UserId == Guid.Empty || identity.PetId == Guid.Empty || CurrentIdentity != identity)
            throw new RoomManagementClientException(RoomManagementFailure.Authentication, "room_binding_changed");
        return new BoundRoomManagementClient(this, identity, sessionGeneration);
    }

    private sealed class BoundRoomManagementClient(FormalAccountClient owner, AccountIdentity identity, int generation) : IRoomManagementClient
    {
        public Uri Origin => owner.Origin;
        public Guid UserId => identity.UserId;
        public Guid PetId => identity.PetId;

        public Task<RoomManagementPage> ListAsync(string state, Guid? after, int limit, CancellationToken ct) => ExecuteAsync(async token =>
        {
            if (state is not ("active" or "dissolved" or "all")) throw Rejected("room_list_state_invalid");
            if (after == Guid.Empty) throw Rejected("room_cursor_invalid");
            if (limit is < 1 or > 50) throw Rejected("room_page_size_invalid");
            var path = $"api/v1/me/rooms?state={state}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
            if (after is { } cursor) path += $"&after={cursor:D}";
            var json = await owner.RequestAsync(HttpMethod.Get, path, null, null, token,
                maximumSuccessBytes: MaximumRoomPageBytes, additionalErrorCodes: RoomErrorCodes);
            Fields(json, "rooms", "next");
            var array = json.GetProperty("rooms");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > limit) throw AccountInputs.InvalidResponse();
            var rooms = array.EnumerateArray().Select(ParseRoom).ToArray();
            Guid? previous = after;
            foreach (var room in rooms)
            {
                if (previous is { } prior && room.RoomId.CompareTo(prior) <= 0
                    || state != "all" && room.Status != state) throw AccountInputs.InvalidResponse();
                previous = room.RoomId;
            }
            var next = json.GetProperty("next").ValueKind == JsonValueKind.Null ? (Guid?)null : GuidValue(json, "next");
            if (next is not null && (rooms.Length != limit || next != rooms[^1].RoomId)) throw AccountInputs.InvalidResponse();
            return new RoomManagementPage(rooms, next);
        }, ct);

        public Task<RoomManagementView> ReadAsync(Guid roomId, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RequireRoomId(roomId);
            var room = ParseRoom(await owner.RequestAsync(HttpMethod.Get, $"api/v1/rooms/{roomId:D}", null, null, token,
                additionalErrorCodes: RoomErrorCodes));
            if (room.RoomId != roomId) throw AccountInputs.InvalidResponse();
            return room;
        }, ct);

        public Task<RoomOperationResult> CreateAsync(RoomCreateRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            if (request is null) throw Rejected("room_request_invalid");
            RequireRequest(request.RequestId);
            if (request.Kind is not ("privateShared" or "publicShared")) throw Rejected("room_kind_invalid");
            RequireCapacity(request.Capacity);
            request = request with { Name = Name(request.Name) };
            return ParseOperation(await owner.RequestAsync(HttpMethod.Post, "api/v1/rooms", request, null, token,
                HttpStatusCode.Created, additionalErrorCodes: RoomErrorCodes), "created", null, 1);
        }, ct);

        public Task<RoomOperationResult> UpdateAsync(Guid roomId, RoomUpdateRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RequireRoomId(roomId);
            if (request is null) throw Rejected("room_request_invalid");
            RequireRequest(request.RequestId);
            RequireVersion(request.ExpectedVersion);
            RequireCapacity(request.Capacity);
            request = request with { Name = Name(request.Name) };
            return ParseOperation(await owner.RequestAsync(HttpMethod.Patch, $"api/v1/rooms/{roomId:D}", request, null, token,
                additionalErrorCodes: RoomErrorCodes), "updated", roomId, request.ExpectedVersion + 1);
        }, ct);

        public Task<RoomOperationResult> DissolveAsync(Guid roomId, RoomDissolveRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RequireRoomId(roomId);
            if (request is null) throw Rejected("room_request_invalid");
            RequireRequest(request.RequestId);
            RequireVersion(request.ExpectedVersion);
            if (!request.Confirmed) throw Rejected("room_confirmation_required");
            return ParseOperation(await owner.RequestAsync(HttpMethod.Post, $"api/v1/rooms/{roomId:D}/dissolve", request, null, token,
                additionalErrorCodes: RoomErrorCodes), "dissolved", roomId, request.ExpectedVersion + 1);
        }, ct);

        public Task<RoomOperationResult> RestoreAsync(Guid roomId, RoomRestoreRequest request, CancellationToken ct) => ExecuteAsync(async token =>
        {
            RequireRoomId(roomId);
            if (request is null) throw Rejected("room_request_invalid");
            RequireRequest(request.RequestId);
            RequireVersion(request.ExpectedVersion);
            return ParseOperation(await owner.RequestAsync(HttpMethod.Post, $"api/v1/rooms/{roomId:D}/restore", request, null, token,
                additionalErrorCodes: RoomErrorCodes), "restored", roomId, request.ExpectedVersion + 1);
        }, ct);

        private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        {
            try
            {
                return await owner.LockedAsync(async token =>
                {
                    RequireBinding(token);
                    var result = await operation(token);
                    RequireBinding(token);
                    return result;
                }, ct);
            }
            catch (AccountClientException error)
            {
                throw new RoomManagementClientException(error.Failure switch
                {
                    AccountFailure.Authentication => RoomManagementFailure.Authentication,
                    AccountFailure.Conflict => RoomManagementFailure.Conflict,
                    AccountFailure.Unavailable => RoomManagementFailure.Unavailable,
                    AccountFailure.InvalidResponse => RoomManagementFailure.InvalidResponse,
                    _ => RoomManagementFailure.Rejected
                }, error.Code);
            }
            catch (Exception error) when (error is not ObjectDisposedException && (error is JsonException or InvalidOperationException or ArgumentException))
            { throw new RoomManagementClientException(RoomManagementFailure.InvalidResponse, "remote_response_invalid"); }
        }

        private void RequireBinding(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            owner.ThrowIfDisposed();
            if (generation != owner.sessionGeneration || owner.CurrentIdentity != identity)
                throw new RoomManagementClientException(RoomManagementFailure.Authentication, "room_binding_changed");
        }

        private static RoomManagementView ParseRoom(JsonElement json)
        {
            Fields(json, "roomId", "kind", "name", "capacity", "status", "version", "reservedSeats", "occupancy",
                "recoverUntil", "recoverable", "templateId", "templateVersion");
            var kind = StringValue(json, "kind");
            var name = StringValue(json, "name");
            try { if (Name(name) != name) throw AccountInputs.InvalidResponse(); }
            catch (RoomManagementClientException) { throw AccountInputs.InvalidResponse(); }
            var capacity = Integer(json, "capacity");
            var seats = Integer(json, "reservedSeats");
            var occupancy = Integer(json, "occupancy");
            var status = StringValue(json, "status");
            var version = PositiveVersion(json, "version");
            var recoverUntil = json.GetProperty("recoverUntil").ValueKind == JsonValueKind.Null ? (DateTimeOffset?)null : DateValue(json, "recoverUntil");
            var recoverableValue = json.GetProperty("recoverable");
            if (recoverableValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw AccountInputs.InvalidResponse();
            var recoverable = recoverableValue.GetBoolean();
            var template = StringValue(json, "templateId");
            if (kind is not ("desktopPet" or "privateShared" or "publicShared") || status is not ("active" or "dissolved")
                || (kind == "desktopPet" ? capacity != 2 : capacity is < 1 or > 12)
                || seats < 0 || seats > capacity || occupancy < 0 || occupancy > capacity
                || (kind == "publicShared" ? seats != 0 : seats < 1 || occupancy > seats)
                || status == "active" && (recoverUntil is not null || recoverable)
                || status == "dissolved" && (kind == "desktopPet" || recoverUntil is null || occupancy != 0)
                || template != (kind == "desktopPet" ? "desktop-pet-default" : "ordinary-default")
                || Integer(json, "templateVersion") != 1) throw AccountInputs.InvalidResponse();
            return new(GuidValue(json, "roomId"), kind, name, capacity, status, version, seats, occupancy, recoverUntil,
                recoverable, template, 1);
        }

        private static RoomOperationResult ParseOperation(JsonElement json, string operation, Guid? roomId, long appliedVersion)
        {
            Fields(json, "roomId", "appliedVersion", "operation");
            var resultId = GuidValue(json, "roomId");
            var version = PositiveVersion(json, "appliedVersion");
            if (roomId is not null && resultId != roomId || version != appliedVersion || StringValue(json, "operation") != operation)
                throw AccountInputs.InvalidResponse();
            return new(resultId, version, operation);
        }

        private static int Integer(JsonElement json, string name) => json.GetProperty(name) is { ValueKind: JsonValueKind.Number } value
            && value.TryGetInt32(out var number) ? number : throw AccountInputs.InvalidResponse();
        private static long PositiveVersion(JsonElement json, string name) => json.GetProperty(name) is { ValueKind: JsonValueKind.Number } value
            && value.TryGetInt64(out var number) && number > 0 ? number : throw AccountInputs.InvalidResponse();
        private static void RequireRoomId(Guid id) { if (id == Guid.Empty) throw Rejected("room_not_found"); }
        private static void RequireRequest(Guid id) { if (id == Guid.Empty) throw Rejected("room_request_invalid"); }
        private static void RequireVersion(long version) { if (version is < 1 or long.MaxValue) throw Rejected("room_version_invalid"); }
        private static void RequireCapacity(int capacity) { if (capacity is < 1 or > 12) throw Rejected("room_capacity_invalid"); }
        private static RoomManagementClientException Rejected(string code) => new(RoomManagementFailure.Rejected, code);

        private static string Name(string? input)
        {
            if (input is null) throw Rejected("room_name_invalid");
            var remaining = input.AsSpan();
            while (!remaining.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
                    throw Rejected("room_name_invalid");
                remaining = remaining[consumed..];
            }
            var name = input.Trim().Normalize(NormalizationForm.FormC);
            if (name.EnumerateRunes().Count() is < 1 or > 64) throw Rejected("room_name_invalid");
            return name;
        }
    }
}
