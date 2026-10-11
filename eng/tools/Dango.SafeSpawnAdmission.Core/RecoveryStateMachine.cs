namespace Dango.SafeSpawnAdmission.Core;

public static class LeaseGate
{
    public static readonly TimeSpan SleepGrace = TimeSpan.FromSeconds(30);
    // Latest facts must explicitly identify one active control lease per lifecycle.
    // A greater generation is not evidence of authorization; overlapping controls are ambiguous.
    public static bool ValidAtTime(RoomFacts facts, DateTimeOffset now) => ContractValidation.Facts(facts) &&
        facts.Leases.Where(l => l.Kind == LeaseKind.Control && now < l.ExpiresAt)
            .GroupBy(l => l.Member).All(group => group.Count() <= 1);
    public static bool HasActiveLease(RoomFacts facts, DateTimeOffset now) => facts.Leases.Any(l => now < l.ExpiresAt);
    public static bool CanRelease(RoomFacts facts, DateTimeOffset now) => ValidAtTime(facts, now) &&
        facts.Disposition == RuntimeDisposition.HealthyRetained && facts.SleepEligible && !HasActiveLease(facts, now) &&
        facts.AllLeasesAbsentSince is { } absent && now >= absent && now - absent >= SleepGrace;
    public static ReleasePermit? PrepareRelease(RoomFacts facts, Guid epoch, DateTimeOffset now) =>
        epoch != Guid.Empty && CanRelease(facts, now) ? new(facts.RoomId, facts.OwnerId, facts.RuntimeRevision, facts.FactsRevision, epoch) : null;
    public static bool RevalidateRelease(ReleasePermit permit, RoomFacts latest, Guid epoch, DateTimeOffset now) =>
        permit.RoomId == latest.RoomId && permit.OwnerId == latest.OwnerId && permit.RuntimeRevision == latest.RuntimeRevision &&
        permit.FactsRevision == latest.FactsRevision && permit.Epoch == epoch && CanRelease(latest, now);
}

// Single-owner, pure decision fixture. Native state is owned elsewhere and never reset by this class.
public sealed class RecoveryStateMachine(ConfigurationContract configuration, ConditionalRecoveryStoreFixture store)
{
    private RecoveryCandidate? pending;
    private RecoveryClaim? currentClaim;
    private readonly Dictionary<Guid, ReadyChallenge> challenges = [];
    private readonly Dictionary<Guid, ReadyChallenge> bindings = [];
    private readonly Dictionary<Guid, InputIntent> inputs = [];
    public RecoveryState State { get; private set; } = RecoveryState.RecoveryBlocked;
    public Guid CurrentEpoch { get; private set; }
    public RecoveryReason? PublishedReason { get; private set; }
    public HealthyCommonFrameProof? PublishedFrame { get; private set; }
    public RecoveryDecision Prepare(MetadataInput metadata, RoomFacts facts, RecoveryClaim claim, DateTimeOffset now)
    {
        pending = null;
        if (State == RecoveryState.FaultLatched || facts.Disposition == RuntimeDisposition.FaultLatched)
        { State = RecoveryState.FaultLatched; return new(State, "Fault remains latched"); }
        if (!LeaseGate.ValidAtTime(facts, now) || !ContractValidation.Configuration(configuration) || !store.IsCurrent(claim) ||
            facts.OwnerId != claim.OwnerId || facts.RuntimeRevision != claim.RuntimeRevision || facts.Disposition == RuntimeDisposition.Unknown)
            return Block("Incomplete or stale facts");
        if (facts.Disposition == RuntimeDisposition.HealthyRetained)
        {
            if (CurrentEpoch == Guid.Empty) return Block("Retained world must be attached explicitly");
            State = RecoveryState.Live; return new(State, "Continue retained world");
        }
        RecoveryRecord? record;
        if (metadata.ConfirmedMissing)
        {
            if (!metadata.Bytes.IsEmpty || claim.LastSuccessfulRecord is not null || facts.Disposition != RuntimeDisposition.ConfirmedFirstStart)
                return Block("Missing is not a damaged record or a recovery shortcut");
            record = null;
        }
        else
        {
            if (!RecoveryRecordCodec.TryDecode(metadata.Bytes, configuration, out record, out _) || record!.RoomId != facts.RoomId ||
                record.RuntimeRevision >= claim.RuntimeRevision || claim.LastSuccessfulRecord is null ||
                !RecoveryRecordCodec.Encode(record).AsSpan().SequenceEqual(RecoveryRecordCodec.Encode(claim.LastSuccessfulRecord)))
                return Block("Record not valid for this room/claim");
        }
        RecoveryReason? reason = facts.Disposition switch
        {
            RuntimeDisposition.ConfirmedFirstStart when record is null => RecoveryReason.FirstStart,
            RuntimeDisposition.ConfirmedWorldLost when record is not null => RecoveryReason.ConfirmedWorldLost,
            RuntimeDisposition.NormallyReleased when record is not null => RecoveryReason.NormallyReleased,
            _ => null
        };
        if (reason is null || facts.Members.IsEmpty || LeaseGate.CanRelease(facts with { Disposition = RuntimeDisposition.HealthyRetained }, now))
            return Block("No eligible playable members or recovery reason");
        pending = new(facts.RoomId, facts.OwnerId, facts.RuntimeRevision, facts.FactsRevision, claim.Epoch, reason.Value,
            configuration, ContractValidation.CanonicalMembers(facts.Members), claim);
        State = RecoveryState.PreparingCandidate;
        return new(State, "Private candidate only", pending);
    }
    public bool CommitPublication(RecoveryCandidate candidate, HealthyCommonFrameProof proof, RoomFacts latest, DateTimeOffset now)
    {
        if (State != RecoveryState.PreparingCandidate || !ReferenceEquals(pending, candidate) || !LeaseGate.ValidAtTime(latest, now) ||
            latest.RoomId != candidate.RoomId || latest.OwnerId != candidate.OwnerId || latest.RuntimeRevision != candidate.RuntimeRevision ||
            latest.FactsRevision != candidate.FactsRevision || !ContractValidation.SameMembers(latest.Members, candidate.Members) ||
            latest.Disposition != Disposition(candidate.Reason) || !store.IsCurrent(candidate.Claim) ||
            LeaseGate.CanRelease(latest with { Disposition = RuntimeDisposition.HealthyRetained }, now) ||
            proof.Epoch != candidate.Epoch || !proof.Healthy || !ContractValidation.Counter(proof.Tick) ||
            !ContractValidation.Counter(proof.SceneRevision) || !ContractValidation.SameMembers(proof.Members, candidate.Members)) return false;
        ulong priorRevision = candidate.Claim.LastSuccessfulRecord?.RecordRevision ?? 0;
        if (priorRevision >= ContractValidation.MaximumCounter) return false;
        var record = new RecoveryRecord(RecoveryRecordCodec.CurrentVersion, candidate.RoomId, candidate.RuntimeRevision, priorRevision + 1,
            candidate.Epoch, proof.Tick, candidate.Configuration, candidate.Members);
        if (!store.TryWrite(candidate.Claim, record)) return false;
        currentClaim = candidate.Claim; CurrentEpoch = candidate.Epoch; PublishedReason = candidate.Reason; PublishedFrame = proof;
        pending = null; challenges.Clear(); bindings.Clear(); inputs.Clear(); State = RecoveryState.AwaitReady; return true;
    }
    public void AbortCandidate() { pending = null; if (State == RecoveryState.PreparingCandidate) State = RecoveryState.RecoveryBlocked; }
    public void LatchFault() { AbortCandidate(); inputs.Clear(); bindings.Clear(); challenges.Clear(); State = RecoveryState.FaultLatched; }
    public void PauseRetained() { if (State is RecoveryState.Live or RecoveryState.AwaitReady) { inputs.Clear(); State = RecoveryState.PausedRetained; } }
    public bool ResumeRetained(RoomFacts facts)
    { if (State != RecoveryState.PausedRetained || !CurrentFacts(facts)) return false; State = RecoveryState.Live; return true; }
    // Call only after the external owner actually disposes its native world following RevalidateRelease.
    public bool CompleteRelease(ReleasePermit permit, RoomFacts latest, DateTimeOffset now)
    {
        if (State is not (RecoveryState.Live or RecoveryState.PausedRetained or RecoveryState.AwaitReady) ||
            !CurrentFacts(latest) || !LeaseGate.RevalidateRelease(permit, latest, CurrentEpoch, now)) return false;
        inputs.Clear(); bindings.Clear(); challenges.Clear(); State = RecoveryState.Released; return true;
    }
    public ReadyChallenge? IssueReady(Guid bindingId, LeaseFact lease, RoomFacts facts, DateTimeOffset now, TimeSpan lifetime)
    {
        if (bindingId == Guid.Empty || lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromSeconds(30) ||
            State is not (RecoveryState.AwaitReady or RecoveryState.Live) || !CurrentFacts(facts) || !Active(lease, facts, now)) return null;
        RevokeBindings(lease.LeaseId, bindingId);
        var challenge = new ReadyChallenge(Guid.NewGuid(), CurrentEpoch, bindingId, lease, configuration, now + lifetime);
        challenges[challenge.ChallengeId] = challenge; return challenge;
    }
    public bool ConfirmReady(ReadyChallenge response, RoomFacts latest, DateTimeOffset now)
    {
        if (State is not (RecoveryState.AwaitReady or RecoveryState.Live) || !CurrentFacts(latest) ||
            !challenges.TryGetValue(response.ChallengeId, out ReadyChallenge? issued) || issued != response || response.Epoch != CurrentEpoch ||
            response.Configuration != configuration || now >= response.ExpiresAt || !Active(response.Lease, latest, now)) return false;
        challenges.Remove(response.ChallengeId); bindings[response.BindingId] = response; inputs.Remove(response.BindingId); State = RecoveryState.Live; return true;
    }
    public bool AcceptInput(InputIntent intent, RoomFacts latest, DateTimeOffset now)
    {
        if (State != RecoveryState.Live || !CurrentFacts(latest) || intent.Epoch != CurrentEpoch || intent.Move is < -1 or > 1 ||
            intent.ReceivedAt > now || now - intent.ReceivedAt >= TimeSpan.FromMilliseconds(350) ||
            !bindings.TryGetValue(intent.BindingId, out ReadyChallenge? binding) || binding.Lease.Kind != LeaseKind.Control ||
            binding.Epoch != intent.Epoch || binding.Lease.LeaseId != intent.LeaseId || binding.Lease.ConnectionId != intent.ConnectionId ||
            binding.Lease.Member != intent.Member || binding.Lease.ControlGeneration != intent.ControlGeneration || !Active(binding.Lease, latest, now) ||
            (inputs.TryGetValue(intent.BindingId, out InputIntent? prior) && intent.ReceivedAt <= prior.ReceivedAt)) return false;
        inputs[intent.BindingId] = intent; return true;
    }
    public InputSample ReadInput(Guid bindingId, RoomFacts latest, DateTimeOffset now)
    {
        if (!inputs.TryGetValue(bindingId, out InputIntent? input) || !AcceptableStored(input, latest, now))
        { inputs.Remove(bindingId); return default; }
        // Jump is an edge, consumed once. Watchdog never modifies native velocity.
        inputs[bindingId] = input with { Jump = false }; return new(input.Move, input.Jump);
    }
    private bool AcceptableStored(InputIntent i, RoomFacts f, DateTimeOffset now) => State == RecoveryState.Live && CurrentFacts(f) &&
        bindings.TryGetValue(i.BindingId, out ReadyChallenge? b) && Active(b.Lease, f, now) && now >= i.ReceivedAt && now - i.ReceivedAt < TimeSpan.FromMilliseconds(350);
    private bool CurrentFacts(RoomFacts facts) => currentClaim is not null && store.IsCurrent(currentClaim) && ContractValidation.Facts(facts) &&
        facts.OwnerId == currentClaim.OwnerId && facts.RuntimeRevision == currentClaim.RuntimeRevision &&
        PublishedFrame is not null && facts.RoomId == store.LastSuccessfulRecord?.RoomId &&
        ContractValidation.SameMembers(facts.Members, PublishedFrame.Members) && facts.Disposition == RuntimeDisposition.HealthyRetained;
    private static bool Active(LeaseFact lease, RoomFacts facts, DateTimeOffset now) => LeaseGate.ValidAtTime(facts, now) && facts.Leases.Contains(lease) &&
        facts.Members.Contains(lease.Member) && now < lease.ExpiresAt;
    private void RevokeBindings(Guid leaseId, Guid bindingId)
    {
        foreach (Guid id in challenges.Where(pair => pair.Value.Lease.LeaseId == leaseId || pair.Value.BindingId == bindingId).Select(pair => pair.Key).ToArray())
            challenges.Remove(id);
        foreach (Guid id in bindings.Where(pair => pair.Value.Lease.LeaseId == leaseId || pair.Key == bindingId).Select(pair => pair.Key).ToArray())
        { bindings.Remove(id); inputs.Remove(id); }
    }
    private RecoveryDecision Block(string detail) { State = RecoveryState.RecoveryBlocked; return new(State, detail); }
    private static RuntimeDisposition Disposition(RecoveryReason reason) => reason switch
    {
        RecoveryReason.FirstStart => RuntimeDisposition.ConfirmedFirstStart,
        RecoveryReason.ConfirmedWorldLost => RuntimeDisposition.ConfirmedWorldLost,
        RecoveryReason.NormallyReleased => RuntimeDisposition.NormallyReleased,
        _ => RuntimeDisposition.Unknown
    };
}
