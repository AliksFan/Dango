using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dango.Protocol;

public static class RoomProtocol
{
    public const string Version = "0.1";
    public const string RoomId = "p2a-room";
    public const string Move = "setMoveIntent";
    public const string Wave = "startWave";
    public const string MotionChanged = "pet.motion.changed";
    public const string ActionStarted = "pet.action.started";
    public static JsonSerializerOptions Json { get; } = CreateJsonOptions();

    public static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
    };

    public static bool IsId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
    public static bool IsPoint(RoomPoint? value) => value is not null && double.IsFinite(value.X) && double.IsFinite(value.Y)
        && value.X is >= 0 and <= 1 && value.Y is >= 0 and <= 1;
    public static bool IsFacing(string? value) => value is "left" or "right" or "center";
    public static bool IsPreset(PresetRef? value) => value is { Version: 1, Id: "default" };

    public static string? Validate(RoomCommand? value)
    {
        if (value is null) return "invalid_command";
        if (value.ProtocolVersion != Version) return "unsupported_protocol";
        if (!IsId(value.RoomId) || !IsId(value.RoomEpoch) || !IsId(value.CommandId) ||
            value.ConnectionGeneration < 1 || value.CommandOrdinal < 1 || value.LastAppliedSequence < 0 || value.Payload is null)
            return "invalid_command";
        string[] forbidden = ["actorId", "participantId", "petId", "userId", "position", "speed", "duration", "participants", "from", "to"];
        if (value.Extra?.Keys.Any(k => forbidden.Contains(k, StringComparer.OrdinalIgnoreCase)) == true ||
            value.Payload.Extra?.Keys.Any(k => forbidden.Contains(k, StringComparer.OrdinalIgnoreCase)) == true)
            return "forbidden";
        return value.Type switch
        {
            Move when value.Payload.Direction is "left" or "right" or "stopped" => null,
            Wave when value.Payload.Direction is null => null,
            _ => "invalid_command",
        };
    }

    public static bool IsPet(PetState? value) => value is not null && IsId(value.PetId) && IsId(value.ParticipantId)
        && IsPoint(value.AnchorPosition) && IsFacing(value.Facing) && IsPreset(value.AppearanceRef) && IsPreset(value.PhysicsPresentationPresetRef)
        && (value.Motion is null || IsMotion(value.Motion)) && (value.Action is null || IsAction(value.Action, value.PetId));

    public static bool IsMotion(MotionSegment value) => IsId(value.MotionId) && IsPoint(value.From) && IsPoint(value.To)
        && value.From.Y == value.To.Y && value.StartedAtServerTimeMs >= 0 && value.EndsAtServerTimeMs > value.StartedAtServerTimeMs
        && IsFacing(value.Facing) && value.MovementProfileVersion == 1;

    public static bool IsAction(ActionInstance value, string petId) => IsId(value.ActionId) && value.ActionKey == "wave"
        && value.Participants is { Count: 1 } && value.Participants[0] == petId && value.StartedAtServerTimeMs >= 0
        && value.EndsAtServerTimeMs > value.StartedAtServerTimeMs && value.PresentationVersion == 1 && value.FallbackKey == "wave-nod";

    public static bool IsSnapshot(RoomSnapshot? value) => value is not null && value.ProtocolVersion == Version
        && value.SnapshotSchemaVersion == 1 && IsId(value.RoomId) && IsId(value.RoomEpoch) && value.LastSequence >= 0
        && value.ServerTimeMs >= 0 && value.CoordinateSpace == CoordinateSpace.Default && value.Pets is { Count: 2 }
        && value.Pets.All(IsPet) && value.Pets.Select(p => p.PetId).Distinct().Count() == 2
        && value.Pets.Select(p => p.ParticipantId).Distinct().Count() == 2 && value.Pets.Any(p => p.PetId == value.SelfPetId);

    public static bool IsEvent(RoomEvent? value) => value is not null && value.ProtocolVersion == Version
        && IsId(value.EventId) && IsId(value.RoomId) && IsId(value.RoomEpoch) && value.Sequence > 0 && value.ServerTimeMs >= 0
        && value.Type is MotionChanged or ActionStarted && IsPet(value.Payload)
        && (value.Type != ActionStarted || value.Payload.Action is not null);
}

public sealed record RoomPoint(double X, double Y);
public sealed record CoordinateSpace(string Id, int Version, double Left, double Right, double GroundY)
{
    public static CoordinateSpace Default { get; } = new("normalized-room", 1, .08, .92, .12);
}
public sealed record PresetRef(string Id, int Version)
{
    public static PresetRef Default { get; } = new("default", 1);
}
public sealed record MotionSegment(string MotionId, RoomPoint From, RoomPoint To, long StartedAtServerTimeMs,
    long EndsAtServerTimeMs, string Facing, int MovementProfileVersion);
public sealed record ActionInstance(string ActionId, string ActionKey, IReadOnlyList<string> Participants,
    long StartedAtServerTimeMs, long EndsAtServerTimeMs, int VariantSeed, int PresentationVersion, string FallbackKey);
public sealed record PetState(string PetId, string ParticipantId, RoomPoint AnchorPosition, string Facing,
    MotionSegment? Motion, ActionInstance? Action, PresetRef AppearanceRef, PresetRef PhysicsPresentationPresetRef);
public sealed record RoomSnapshot(string ProtocolVersion, int SnapshotSchemaVersion, string RoomId, string RoomEpoch,
    long LastSequence, long ServerTimeMs, string SelfPetId, CoordinateSpace CoordinateSpace, IReadOnlyList<PetState> Pets);
public sealed record RoomEvent(string ProtocolVersion, string EventId, string RoomId, string RoomEpoch, long Sequence,
    long ServerTimeMs, string? CausationCommandId, string Type, PetState Payload);
public sealed record CommandPayload(string? Direction = null)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}
public sealed record RoomCommand(string ProtocolVersion, string RoomId, string RoomEpoch, long ConnectionGeneration,
    string CommandId, long CommandOrdinal, long LastAppliedSequence, string Type, CommandPayload Payload)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}
public sealed record ProtocolError(string Code, int? RetryAfterMs = null);
public sealed record CommandResult(string ProtocolVersion, string RoomId, string RoomEpoch, string CommandId,
    string Outcome, bool StateChanged, long CurrentSequence, bool OrdinalConsumed, long NextCommandOrdinal,
    string? OriginalOutcome, string? EventId, long? EventSequence, long ServerTimeMs, ProtocolError? Error);
public sealed record JoinRoomRequest(string ProtocolVersion, string RoomId, string ClientInstanceId, string JoinTicket);
public sealed record ResumeRoomRequest(string ProtocolVersion, string RoomId, string RoomEpoch, string ClientInstanceId, string ResumeToken);
public sealed record JoinRoomResponse(string ProtocolVersion, long ConnectionGeneration, string SelfParticipantId,
    string SelfPetId, string ResumeToken, long NextCommandOrdinal, RoomSnapshot Snapshot);
public sealed record TimeSample(string ProtocolVersion, string RoomId, string RoomEpoch, long ServerTimeMs, long LastSequence);
public sealed record SessionSuperseded(long ConnectionGeneration);

// Used only on inherited private process pipes. Never persist or log this DTO.
public sealed record LocalBootstrap(string Endpoint, string RoomId, string RoomEpoch, IReadOnlyList<string> JoinTickets);
public sealed record LocalClientBootstrap(string Endpoint, string RoomId, string RoomEpoch, string ClientInstanceId, string JoinTicket);

public static class RoomSampling
{
    public static PetState At(PetState pet, long serverTimeMs)
    {
        var motion = pet.Motion;
        if (motion is not null)
        {
            var progress = Math.Clamp((double)(serverTimeMs - motion.StartedAtServerTimeMs)
                / (motion.EndsAtServerTimeMs - motion.StartedAtServerTimeMs), 0, 1);
            pet = pet with
            {
                AnchorPosition = new(motion.From.X + (motion.To.X - motion.From.X) * progress,
                    motion.From.Y + (motion.To.Y - motion.From.Y) * progress),
                Facing = motion.Facing,
                Motion = serverTimeMs >= motion.EndsAtServerTimeMs ? null : motion,
            };
        }
        return pet.Action is { } action && serverTimeMs >= action.EndsAtServerTimeMs
            ? pet with { Action = null } : pet;
    }
    public static double ActionProgress(ActionInstance action, long serverTimeMs) =>
        Math.Clamp((double)(serverTimeMs - action.StartedAtServerTimeMs) / (action.EndsAtServerTimeMs - action.StartedAtServerTimeMs), 0, 1);
}
