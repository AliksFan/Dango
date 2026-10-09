using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Client.Sync.Accounts;
using Dango.Protocol;

namespace Dango.Client.Sync.Rooms;

internal static class RoomAccessValidation
{
    internal const int MaximumBytes = 65536;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };
    internal static readonly HashSet<string> Errors = new(StringComparer.Ordinal)
    {
        "authentication_required", "room_connection_rejected", "room_rate_limited", "room_query_invalid", "room_not_found", "pet_not_found",
        "room_request_invalid", "room_request_conflict", "room_instance_invalid", "room_device_invalid", "room_assignment_invalid",
        "room_control_invalid", "room_lease_invalid", "room_assignment_conflict", "room_control_conflict", "room_lease_forbidden",
        "room_transport_conflict", "room_lease_expired", "room_device_forbidden", "room_access_forbidden", "room_inactive", "room_full",
        "room_device_limit", "room_banned", "room_code_invalid", "room_code_required", "room_code_rejected", "room_code_lifetime_invalid",
        "room_version_invalid", "room_version_conflict", "room_version_exhausted", "room_state_version_exhausted", "room_public_required",
        "room_state_conflict", "room_presence_retry", "room_admission_generation_exhausted", "room_code_replay_unavailable",
        "request_protection_invalid", "origin_invalid", "request_rate_limited", "request_invalid", "write_conflict", "service_unavailable"
    };
    private static readonly HashSet<string> Conflicts = new(StringComparer.Ordinal)
    {
        "room_request_conflict", "room_assignment_conflict", "room_control_conflict", "room_transport_conflict", "room_lease_expired",
        "room_inactive", "room_full", "room_device_limit", "room_version_conflict", "room_version_exhausted", "room_state_version_exhausted",
        "room_public_required", "room_state_conflict", "room_presence_retry", "room_admission_generation_exhausted", "room_code_replay_unavailable", "write_conflict"
    };
    private static readonly HashSet<string> Notices = new(StringComparer.Ordinal)
    { "created", "updated", "dissolved", "restored", "expired", "entered", "left", "control", "lease_ended", "offline", "code_reset", "removed", "ban_set", "ban_removed" };

    internal static RoomAccessClientException Invalid() => new(RoomAccessFailure.InvalidResponse, "remote_response_invalid");
    internal static RoomAccessClientException Reject(string code) => new(RoomAccessFailure.Rejected, code);
    internal static RoomAccessClientException Unknown() => new(RoomAccessFailure.Unavailable, "request_outcome_unknown");
    internal static RoomAccessClientException HubError(string? code)
    {
        if (code is null || !Errors.Contains(code) || code == "service_unavailable") return Unknown();
        return new(code is "authentication_required" or "request_protection_invalid" or "origin_invalid"
            ? RoomAccessFailure.Authentication : Conflicts.Contains(code) ? RoomAccessFailure.Conflict : RoomAccessFailure.Rejected, code);
    }
    internal static RoomAccessClientException AccountError(AccountClientException error)
    {
        var failure = error.Failure switch
        {
            AccountFailure.Authentication => error.Code is "room_lease_forbidden" or "room_device_forbidden" or "room_access_forbidden"
                or "room_banned" or "room_code_required" or "room_code_rejected" ? RoomAccessFailure.Rejected : RoomAccessFailure.Authentication,
            AccountFailure.Conflict => RoomAccessFailure.Conflict,
            AccountFailure.Unavailable => RoomAccessFailure.Unavailable,
            AccountFailure.InvalidResponse => RoomAccessFailure.InvalidResponse,
            _ => RoomAccessFailure.Rejected
        };
        return new(failure, error.Code);
    }
    internal static T Parse<T>(JsonElement value) where T : class
    {
        try { return value.Deserialize<T>(Json) ?? throw Invalid(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { throw Invalid(); }
    }
    internal static void Id(Guid id, string code = "room_request_invalid") { if (id == Guid.Empty) throw Reject(code); }
    internal static void Version(long version) { if (version < 1) throw Reject("room_assignment_invalid"); }
    internal static void Lease(RoomLeaseRequest? request)
    { if (request is null) throw Reject("room_request_invalid"); Id(request.LeaseId, "room_lease_invalid"); Id(request.InstanceId, "room_instance_invalid"); }
    internal static void Enter(RoomEnterRequest? request)
    {
        if (request is null) throw Reject("room_request_invalid");
        Id(request.RequestId); Id(request.InstanceId, "room_instance_invalid"); Version(request.ExpectedAssignmentVersion);
        if (request.ExpectedRoomId == Guid.Empty) throw Reject("room_assignment_invalid");
        if (request.DeviceKind is not ("desktop" or "mobile")) throw Reject("room_device_invalid");
        if (request.Code is not null) Code(request.Code);
    }
    internal static void Control(RoomControlRequest? request)
    {
        if (request is null) throw Reject("room_request_invalid");
        Id(request.RequestId); Lease(new(request.LeaseId, request.InstanceId)); Id(request.ExpectedRoomId, "room_assignment_invalid");
        Version(request.ExpectedAssignmentVersion); if (request.ExpectedControlGeneration < 0) throw Reject("room_control_invalid");
    }
    internal static void End(RoomLeaseEndRequest? request)
    { if (request is null) throw Reject("room_request_invalid"); Id(request.RequestId); Lease(new(request.LeaseId, request.InstanceId)); }
    internal static void Code(string? code)
    { try { AccountInputs.Proof(code); } catch (AccountClientException) { throw Reject("room_code_invalid"); } }
    internal static void Reset(RoomCodeResetRequest? request)
    {
        if (request is null) throw Reject("room_request_invalid"); Id(request.RequestId);
        if (request.ExpectedVersion < 1) throw Reject("room_version_invalid");
        if (request.LifetimeHours is < 1 or > 168) throw Reject("room_code_lifetime_invalid");
    }
    private static bool ValidId(Guid id) => id != Guid.Empty;
    private static bool OptionalId(Guid? id) => id != Guid.Empty;
    private static void Time(DateTimeOffset time) { if (time == default) throw Invalid(); }
    private static void Deadline(DateTimeOffset deadline, DateTimeOffset now)
    { Time(now); if (deadline <= now || deadline - now > TimeSpan.FromSeconds(30)) throw Invalid(); }
    private static void Room(string kind, string name, int capacity, string template, int templateVersion)
    {
        if (kind is not ("desktopPet" or "privateShared" or "publicShared")
            || (kind == "desktopPet" ? capacity != 2 : capacity is < 1 or > 12)
            || template != (kind == "desktopPet" ? "desktop-pet-default" : "ordinary-default") || templateVersion != 1 || name is null) throw Invalid();
        var span = name.AsSpan();
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out var rune, out var used) != OperationStatus.Done || Rune.IsControl(rune)) throw Invalid();
            span = span[used..];
        }
        if (name != name.Trim() || !name.IsNormalized(NormalizationForm.FormC) || name.EnumerateRunes().Count() is < 1 or > 64) throw Invalid();
    }
    internal static PersonalRoomSnapshot Personal(JsonElement json, Guid userId, Guid petId)
    {
        var value = Parse<PersonalRoomSnapshot>(json);
        if (value.SchemaVersion != 1 || value.UserId != userId || value.PetId != petId || !OptionalId(value.RoomId)
            || !OptionalId(value.ControlLeaseId) || value.AssignmentVersion < 1 || value.ControlGeneration < 0
            || value.Leases is null || value.Leases.Length > 8
            || (value.RoomId is null ? value.RoomKind is not null || value.ControlLeaseId is not null || value.IsSleeping || value.Leases.Length != 0
                : value.RoomKind is not ("desktopPet" or "privateShared" or "publicShared"))
            || value.IsSleeping && (value.ControlLeaseId is not null || value.Leases.Length != 0)) throw Invalid();
        Time(value.ServerTime);
        var ids = new HashSet<Guid>(); var instances = new HashSet<Guid>(); var controllers = 0;
        foreach (var lease in value.Leases)
        {
            if (lease is null || !ValidId(lease.LeaseId) || !ValidId(lease.InstanceId) || !ids.Add(lease.LeaseId) || !instances.Add(lease.InstanceId)
                || lease.DeviceKind is not ("desktop" or "mobile") || value.RoomKind == "desktopPet" && lease.DeviceKind != "desktop"
                || lease.Controls && lease.LeaseId != value.ControlLeaseId) throw Invalid();
            if (lease.Controls) controllers++;
            if (lease.LeaseId == value.ControlLeaseId && !lease.Controls) throw Invalid();
            Deadline(lease.ExpiresAt, value.ServerTime);
        }
        if (controllers > 1) throw Invalid();
        return value;
    }
    internal static SharedRoomSnapshot Snapshot(JsonElement json, Guid roomId)
    {
        var value = Parse<SharedRoomSnapshot>(json);
        if (value.SchemaVersion != 1 || value.RoomId != roomId || !ValidId(roomId) || value.RoomVersion < 1
            || value.Occupants is null || value.Occupants.Length > 12 || value.Occupants.Length > value.Capacity) throw Invalid();
        Room(value.Kind, value.Name, value.Capacity, value.TemplateId, value.TemplateVersion); Time(value.ServerTime);
        var pets = new HashSet<Guid>(); var users = new HashSet<Guid>();
        foreach (var occupant in value.Occupants)
        {
            if (occupant is null || !ValidId(occupant.PetId) || !ValidId(occupant.UserId) || !pets.Add(occupant.PetId) || !users.Add(occupant.UserId)
                || occupant.ConfigurationVersion < 1 || occupant.AssignmentVersion < 1 || !double.IsFinite(occupant.PositionX) || !double.IsFinite(occupant.PositionY)
                || value.Kind == "publicShared" && occupant.IsSleeping) throw Invalid();
            try { _ = AppearanceContract.ValidateAndClone(occupant.Configuration); }
            catch (AppearanceContractException) { throw Invalid(); }
        }
        return value;
    }
    internal static RoomAccessResult Result(JsonElement json, Guid petId, string operation, Guid? expectedRoom = null, Guid? expectedLease = null,
        long? expectedAssignmentVersion = null, long? expectedControlGeneration = null)
    {
        var value = Parse<RoomAccessResult>(json);
        if (value.PetId != petId || value.Operation != operation || value.AssignmentVersion < 1 || value.ControlGeneration < 0 || value.RoomVersion < 1
            || !OptionalId(value.RoomId) || !OptionalId(value.LeaseId) || (value.LeaseId is null) != (value.LeaseExpiresAt is null)
            || expectedRoom is not null && value.RoomId != expectedRoom || expectedLease is not null && value.LeaseId != expectedLease
            || expectedAssignmentVersion is not null && value.AssignmentVersion <= expectedAssignmentVersion
            || expectedControlGeneration is not null && value.ControlGeneration <= expectedControlGeneration
            || operation is "entered" or "attached" or "heartbeat" or "control" && (value.RoomId is null || value.LeaseId is null)
            || operation == "left" && (value.RoomId is not null || value.LeaseId is not null)
            || operation == "lease_ended" && value.LeaseId is not null) throw Invalid();
        Time(value.ServerTime); if (value.LeaseExpiresAt is { } until) Deadline(until, value.ServerTime);
        return value;
    }
    internal static RoomDoorView Door(JsonElement json)
    {
        var value = Parse<RoomDoorView>(json);
        if (!ValidId(value.RoomId) || value.Kind != "publicShared" || value.Version < 1) throw Invalid();
        Room(value.Kind, value.Name, value.Capacity, value.TemplateId, value.TemplateVersion);
        return value;
    }
    internal static RoomCodeResult CodeResult(JsonElement json, Guid roomId, long expectedVersion)
    {
        var value = Parse<RoomCodeResult>(json);
        if (value.RoomId != roomId || !ValidId(value.CredentialId) || expectedVersion == long.MaxValue || value.RoomVersion != expectedVersion + 1
            || value.Generation < 1 || value.ExpiresAt == default) throw Invalid();
        try { Code(value.Code); } catch (RoomAccessClientException) { throw Invalid(); }
        return value;
    }
    internal static RoomChangeNotice Notice(JsonElement json)
    {
        var value = Parse<RoomChangeNotice>(json);
        if (!ValidId(value.EventId) || !ValidId(value.RoomId) || value.RoomVersion < 1 || !Notices.Contains(value.EventType)) throw Invalid();
        return value;
    }
}
