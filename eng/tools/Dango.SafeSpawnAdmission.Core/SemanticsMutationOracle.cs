using System.Collections.Immutable;

namespace Dango.SafeSpawnAdmission.Core;

public sealed record MutationOracleResult(string Name, bool OraclePassed, string Detail);

// Explicit wrong algorithms in the isolated fixture. The oracle observes release decisions,
// matched lifecycle identities, stored records or input acceptance, rather than error strings.
public static class SemanticsMutationOracle
{
    public static ImmutableArray<string> Names { get; } =
        ["observerIgnored", "earlyRelease", "badRecordAsNew", "onlyPetId", "oldWriter", "unknownFaultAsLost", "staleReady"];
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ConfigurationContract Configuration = SafeSpawnFixtures.Configuration(new string('a', 64));
    public static MutationOracleResult Evaluate(string name)
    {
        if (name == "none")
        {
            bool all = Names.All(n => Run(n, false));
            return new(name, all, "Unmutated semantic fixture decisions satisfy every independent oracle");
        }
        if (!Names.Contains(name)) throw new ArgumentException("Unknown semantic mutation", nameof(name));
        bool passed = Run(name, true);
        return new(name, passed, "Observed wrong algorithm compared with release/lifecycle/store/input oracle");
    }
    private static bool Run(string name, bool mutated)
    {
        var member = SafeSpawnFixtures.Members(1)[0];
        var observer = new LeaseFact(Guid.Parse("00000000-0000-4000-8000-000000000021"),
            Guid.Parse("00000000-0000-4000-8000-000000000022"), member, LeaseKind.Observer, 1, Now.AddSeconds(1));
        RoomFacts facts = SafeSpawnFixtures.Facts(RuntimeDisposition.HealthyRetained, count: 1) with
        { SleepEligible = true, AllLeasesAbsentSince = Now.AddSeconds(-30) };
        switch (name)
        {
            case "observerIgnored":
                facts = facts with { Leases = [observer] };
                bool observerRelease = mutated ? facts.SleepEligible && !facts.Leases.Any(l => l.Kind == LeaseKind.Control && Now < l.ExpiresAt) : LeaseGate.CanRelease(facts, Now);
                return !observerRelease; // Valid observation must retain the world.
            case "earlyRelease":
                facts = facts with { AllLeasesAbsentSince = Now.AddSeconds(-29.999) };
                bool earlyRelease = mutated ? facts.SleepEligible && !LeaseGate.HasActiveLease(facts, Now) : LeaseGate.CanRelease(facts, Now);
                return !earlyRelease; // 29.999s is strictly short of the frozen grace.
            case "badRecordAsNew":
                var firstStore = new ConditionalRecoveryStoreFixture(facts.OwnerId, 1);
                firstStore.TryClaim(facts.OwnerId, 1, facts.OwnerId, out RecoveryClaim? firstClaim);
                facts = facts with { RuntimeRevision = firstClaim!.RuntimeRevision, Disposition = RuntimeDisposition.ConfirmedFirstStart, SleepEligible = false };
                var firstMachine = new RecoveryStateMachine(Configuration, firstStore);
                byte[] damaged = [0x7b, 0x7d];
                MetadataInput actualMetadata = mutated && !RecoveryRecordCodec.TryDecode(damaged, Configuration, out _, out _)
                    ? MetadataInput.Missing() : MetadataInput.Existing(damaged);
                RecoveryCandidate? born = firstMachine.Prepare(actualMetadata, facts, firstClaim, Now).Candidate;
                return born is null; // Existing damaged bytes can never be promoted to confirmed absence.
            case "onlyPetId":
                MemberIdentity prior = member with { PresenceGeneration = 1 };
                MemberIdentity current = member with { PresenceGeneration = 2 };
                ImmutableArray<MemberIdentity> matches = mutated
                    ? new[] { current }.Where(m => m.PetId == prior.PetId).ToImmutableArray()
                    : new[] { current }.Where(m => m == prior).ToImmutableArray();
                return matches.IsEmpty; // Same pet, different lifecycle is fresh.
            case "oldWriter":
                Guid oldEpoch = Guid.Parse("00000000-0000-4000-8000-000000000031");
                RecoveryRecord priorRecord = SafeSpawnFixtures.Record(Configuration, oldEpoch, tick: 80, count: 1);
                var store = new ConditionalRecoveryStoreFixture(facts.OwnerId, 1, priorRecord);
                store.TryClaim(facts.OwnerId, 1, facts.OwnerId, out RecoveryClaim? claim);
                RecoveryRecord good = priorRecord with { RuntimeRevision = claim!.RuntimeRevision, RecordRevision = 2, PlayEpoch = claim.Epoch, LastSuccessfulTick = 1 };
                if (!store.TryWrite(claim, good)) return false;
                var staleClaim = new RecoveryClaim(facts.OwnerId, 1, oldEpoch, priorRecord);
                RecoveryRecord late = priorRecord with { RecordRevision = 3, LastSuccessfulTick = 90 };
                RecoveryRecord observed;
                if (mutated) observed = new UnconditionalWriterFixture(good).Write(late);
                else { store.TryWrite(staleClaim, late); observed = store.LastSuccessfulRecord!; }
                return observed.PlayEpoch == good.PlayEpoch && observed.RuntimeRevision == good.RuntimeRevision && observed.LastSuccessfulTick == 1;
            case "unknownFaultAsLost":
                return new[] { RuntimeDisposition.Unknown, RuntimeDisposition.FaultLatched }.All(disposition =>
                {
                    RecoveryRecord saved = SafeSpawnFixtures.Record(Configuration, Guid.Parse("00000000-0000-4000-8000-000000000041"), count: 1);
                    var s = new ConditionalRecoveryStoreFixture(facts.OwnerId, 1, saved); s.TryClaim(facts.OwnerId, 1, facts.OwnerId, out RecoveryClaim? c);
                    RoomFacts f = facts with { RuntimeRevision = c!.RuntimeRevision, SleepEligible = false,
                        Disposition = mutated ? RuntimeDisposition.ConfirmedWorldLost : disposition };
                    return new RecoveryStateMachine(Configuration, s).Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(saved)), f, c, Now).Candidate is null;
                });
            case "staleReady":
                var setup = PublishedFixture();
                LeaseFact control = observer with { Kind = LeaseKind.Control };
                RoomFacts live = setup.Facts with { Leases = [control], Disposition = RuntimeDisposition.HealthyRetained };
                Guid binding = Guid.Parse("00000000-0000-4000-8000-000000000051");
                ReadyChallenge challenge = setup.Machine.IssueReady(binding, control, live, Now, TimeSpan.FromSeconds(1))!;
                if (!setup.Machine.ConfirmReady(challenge, live, Now)) return false;
                var stale = new InputIntent(Guid.Parse("00000000-0000-4000-8000-000000000052"), binding, control.LeaseId, control.ConnectionId, member, 1, 1, true, Now);
                bool accepted = mutated ? stale.Member.PetId == control.Member.PetId : setup.Machine.AcceptInput(stale, live, Now);
                return !accepted;
            default: throw new ArgumentException("Unknown mutation", nameof(name));
        }
    }
    private sealed class UnconditionalWriterFixture(RecoveryRecord initial)
    {
        private RecoveryRecord record = initial;
        public RecoveryRecord Write(RecoveryRecord value) { record = value; return record; }
    }
    private static (RecoveryStateMachine Machine, RoomFacts Facts) PublishedFixture()
    {
        RoomFacts facts = SafeSpawnFixtures.Facts(RuntimeDisposition.ConfirmedFirstStart, count: 1);
        var store = new ConditionalRecoveryStoreFixture(facts.OwnerId, 1); store.TryClaim(facts.OwnerId, 1, facts.OwnerId, out RecoveryClaim? claim);
        facts = facts with { RuntimeRevision = claim!.RuntimeRevision };
        var machine = new RecoveryStateMachine(Configuration, store);
        RecoveryCandidate candidate = machine.Prepare(MetadataInput.Missing(), facts, claim, Now).Candidate!;
        if (!machine.CommitPublication(candidate, new(candidate.Epoch, 1, 1, facts.Members, true), facts, Now)) throw new InvalidOperationException("Fixture setup failed");
        return (machine, facts);
    }
}
