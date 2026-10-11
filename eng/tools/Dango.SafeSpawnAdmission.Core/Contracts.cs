using System.Collections.Immutable;

namespace Dango.SafeSpawnAdmission.Core;

public sealed record MemberIdentity(Guid PetId, ulong PresenceGeneration);
public sealed record ConfigurationContract(string GeometryId, uint GeometryVersion, uint SimulationVersion, string ParameterHash);
public sealed record RecoveryRecord(uint RecordVersion, Guid RoomId, ulong RuntimeRevision, ulong RecordRevision,
    Guid PlayEpoch, ulong LastSuccessfulTick, ConfigurationContract Configuration, ImmutableArray<MemberIdentity> Members);
public enum RuntimeDisposition { Unknown, ConfirmedWorldLost, NormallyReleased, HealthyRetained, FaultLatched, ConfirmedFirstStart }
public enum LeaseKind { Control, Observer }
public sealed record LeaseFact(Guid LeaseId, Guid ConnectionId, MemberIdentity Member, LeaseKind Kind,
    ulong ControlGeneration, DateTimeOffset ExpiresAt);
// Trusted, fictitious domain facts. This DTO itself supplies no production authentication or persistence.
public sealed record RoomFacts(Guid RoomId, Guid OwnerId, ulong RuntimeRevision, ulong FactsRevision,
    ImmutableArray<MemberIdentity> Members, ImmutableArray<LeaseFact> Leases, RuntimeDisposition Disposition,
    bool SleepEligible, DateTimeOffset? AllLeasesAbsentSince);
public sealed record RecoveryClaim(Guid OwnerId, ulong RuntimeRevision, Guid Epoch, RecoveryRecord? LastSuccessfulRecord);
public enum RecoveryReason { FirstStart, ConfirmedWorldLost, NormallyReleased }
public enum RecoveryState { RecoveryBlocked, Live, PausedRetained, PreparingCandidate, AwaitReady, Released, FaultLatched }
public sealed record RecoveryCandidate(Guid RoomId, Guid OwnerId, ulong RuntimeRevision, ulong FactsRevision,
    Guid Epoch, RecoveryReason Reason, ConfigurationContract Configuration, ImmutableArray<MemberIdentity> Members,
    RecoveryClaim Claim)
{
    public ImmutableArray<MemberIdentity> MatchingRecordedMembers => Claim.LastSuccessfulRecord is { } record && record.RoomId == RoomId
        ? Members.Where(record.Members.Contains).ToImmutableArray() : [];
    public ImmutableArray<MemberIdentity> FreshMembers => Members.Except(MatchingRecordedMembers).ToImmutableArray();
}
public sealed record HealthyCommonFrameProof(Guid Epoch, ulong Tick, ulong SceneRevision,
    ImmutableArray<MemberIdentity> Members, bool Healthy);
public sealed record RecoveryDecision(RecoveryState State, string Detail, RecoveryCandidate? Candidate = null);
public sealed record MetadataInput(bool ConfirmedMissing, ReadOnlyMemory<byte> Bytes)
{
    public static MetadataInput Missing() => new(true, ReadOnlyMemory<byte>.Empty);
    public static MetadataInput Existing(byte[] bytes) => new(false, bytes);
}
public sealed record ReleasePermit(Guid RoomId, Guid OwnerId, ulong RuntimeRevision, ulong FactsRevision, Guid Epoch);
public sealed record ReadyChallenge(Guid ChallengeId, Guid Epoch, Guid BindingId, LeaseFact Lease,
    ConfigurationContract Configuration, DateTimeOffset ExpiresAt);
public sealed record InputIntent(Guid Epoch, Guid BindingId, Guid LeaseId, Guid ConnectionId,
    MemberIdentity Member, ulong ControlGeneration, int Move, bool Jump, DateTimeOffset ReceivedAt);
public readonly record struct InputSample(int Move, bool Jump);

public static class ContractValidation
{
    public const ulong MaximumCounter = long.MaxValue;
    public static bool Counter(ulong value, bool zeroAllowed = false) => value <= MaximumCounter && (zeroAllowed || value > 0);
    public static bool Member(MemberIdentity? m) => m is not null && m.PetId != Guid.Empty && Counter(m.PresenceGeneration);
    public static bool Members(ImmutableArray<MemberIdentity> members) => !members.IsDefault && members.Length <= 2 &&
        members.All(Member) && members.Select(m => m.PetId).Distinct().Count() == members.Length;
    public static bool Configuration(ConfigurationContract? c) => c is not null &&
        !string.IsNullOrWhiteSpace(c.GeometryId) && c.GeometryId.Length <= 128 && c.GeometryVersion > 0 &&
        c.SimulationVersion > 0 && c.ParameterHash is { Length: 64 } && c.ParameterHash.All(Uri.IsHexDigit);
    public static bool Record(RecoveryRecord? r) => r is not null && r.RecordVersion == RecoveryRecordCodec.CurrentVersion &&
        r.RoomId != Guid.Empty && r.PlayEpoch != Guid.Empty && Counter(r.RuntimeRevision) && Counter(r.RecordRevision) &&
        Counter(r.LastSuccessfulTick, true) && Configuration(r.Configuration) && Members(r.Members);
    public static bool Facts(RoomFacts f) => f.RoomId != Guid.Empty && f.OwnerId != Guid.Empty && Counter(f.RuntimeRevision) &&
        Counter(f.FactsRevision) && Members(f.Members) && !f.Leases.IsDefault && Enum.IsDefined(f.Disposition) &&
        f.Leases.All(l => l is not null && l.LeaseId != Guid.Empty && l.ConnectionId != Guid.Empty && Member(l.Member) &&
            f.Members.Contains(l.Member) && Counter(l.ControlGeneration) && Enum.IsDefined(l.Kind)) &&
        f.Leases.Select(l => l.LeaseId).Distinct().Count() == f.Leases.Length;
    public static ImmutableArray<MemberIdentity> CanonicalMembers(ImmutableArray<MemberIdentity> members) =>
        members.OrderBy(m => m.PetId.ToString("D"), StringComparer.Ordinal).ToImmutableArray();
    public static bool SameMembers(ImmutableArray<MemberIdentity> a, ImmutableArray<MemberIdentity> b) =>
        Members(a) && Members(b) && CanonicalMembers(a).SequenceEqual(CanonicalMembers(b));
}

public static class SafeSpawnFixtures
{
    public static readonly Guid RoomId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    public static readonly Guid OwnerId = Guid.Parse("00000000-0000-4000-8000-000000000002");
    public static readonly Guid PetOne = Guid.Parse("00000000-0000-4000-8000-000000000011");
    public static readonly Guid PetTwo = Guid.Parse("00000000-0000-4000-8000-000000000012");
    public static ConfigurationContract Configuration(string parameterHash) => new("original-no-furniture", 1, 1, parameterHash);
    public static ImmutableArray<MemberIdentity> Members(int count = 2) => count switch
    {
        0 => [], 1 => [new(PetOne, 1)], 2 => [new(PetOne, 1), new(PetTwo, 1)],
        _ => throw new ArgumentOutOfRangeException(nameof(count))
    };
    public static RoomFacts Facts(RuntimeDisposition disposition, ulong runtimeRevision = 1, ulong factsRevision = 1,
        int count = 2) => new(RoomId, OwnerId, runtimeRevision, factsRevision, Members(count), [], disposition, false, null);
    public static RecoveryRecord Record(ConfigurationContract configuration, Guid epoch, ulong runtimeRevision = 1,
        ulong revision = 1, ulong tick = 0, int count = 2) =>
        new(RecoveryRecordCodec.CurrentVersion, RoomId, runtimeRevision, revision, epoch, tick, configuration, Members(count));
}
