using System.Collections.Immutable;
using System.Text;
using Dango.SafeSpawnAdmission.Core;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class SafeSpawnRecoverySemanticsTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ConfigurationContract Config = SafeSpawnFixtures.Configuration(new string('a', 64));
    private static readonly Guid OldEpoch = Guid.Parse("00000000-0000-4000-8000-000000000101");
    private static readonly Guid OtherOwner = Guid.Parse("00000000-0000-4000-8000-000000000102");
    private static readonly Guid OtherRoom = Guid.Parse("00000000-0000-4000-8000-000000000103");
    private static readonly Guid Binding = Guid.Parse("00000000-0000-4000-8000-000000000104");
    private static RecoveryRecord Saved() => SafeSpawnFixtures.Record(Config, OldEpoch, tick: 900);
    private static (RecoveryStateMachine Machine, ConditionalRecoveryStoreFixture Store, RecoveryClaim Claim, RoomFacts Facts) Setup(
        RuntimeDisposition disposition = RuntimeDisposition.ConfirmedWorldLost, RecoveryRecord? saved = null, bool firstStart = false)
    {
        saved = firstStart ? null : saved ?? Saved();
        var store = new ConditionalRecoveryStoreFixture(SafeSpawnFixtures.OwnerId, 1, saved);
        Assert.IsTrue(store.TryClaim(SafeSpawnFixtures.OwnerId, 1, OtherOwner, out RecoveryClaim? claim));
        var facts = SafeSpawnFixtures.Facts(disposition) with { OwnerId = OtherOwner, RuntimeRevision = claim!.RuntimeRevision };
        return (new(Config, store), store, claim, facts);
    }
    private static RecoveryCandidate Prepare((RecoveryStateMachine Machine, ConditionalRecoveryStoreFixture Store, RecoveryClaim Claim, RoomFacts Facts) f)
    {
        MetadataInput metadata = f.Claim.LastSuccessfulRecord is null ? MetadataInput.Missing() : MetadataInput.Existing(RecoveryRecordCodec.Encode(f.Claim.LastSuccessfulRecord));
        RecoveryDecision decision = f.Machine.Prepare(metadata, f.Facts, f.Claim, Now);
        Assert.IsNotNull(decision.Candidate); return decision.Candidate;
    }
    private static HealthyCommonFrameProof Proof(RecoveryCandidate c) => new(c.Epoch, 1, 1, c.Members, true);
    private static LeaseFact Lease(LeaseKind kind = LeaseKind.Control) => new(
        Guid.Parse("00000000-0000-4000-8000-000000000105"), Guid.Parse("00000000-0000-4000-8000-000000000106"),
        SafeSpawnFixtures.Members()[0], kind, 1, Now.AddSeconds(60));

    [TestMethod]
    public void CanonicalRoundTripIsBoundedAndIndependentOfHistoryOrMemberOrder()
    {
        RecoveryRecord record = Saved();
        byte[] bytes = RecoveryRecordCodec.Encode(record);
        Assert.IsTrue(bytes.Length < RecoveryRecordCodec.MaximumBytes);
        Assert.IsTrue(RecoveryRecordCodec.TryDecode(bytes, Config, out RecoveryRecord? decoded, out _));
        CollectionAssert.AreEqual(bytes, RecoveryRecordCodec.Encode(decoded!));
        CollectionAssert.AreEqual(bytes, RecoveryRecordCodec.Encode(record with { Members = record.Members.Reverse().ToImmutableArray() }));
        Assert.AreEqual(bytes.Length, RecoveryRecordCodec.Encode(record with { LastSuccessfulTick = 999 }).Length);
        string json = Encoding.UTF8.GetString(bytes);
        foreach (string forbidden in new[] { "position", "velocity", "support", "nodes", "input" }) Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("nestedDuplicate")]
    [DataRow("null")]
    [DataRow("version")]
    [DataRow("overflow")]
    [DataRow("negative")]
    [DataRow("zero")]
    [DataRow("truncated")]
    [DataRow("oversize")]
    [DataRow("hash")]
    [DataRow("duplicateIdentity")]
    [DataRow("extraMember")]
    [DataRow("emptyGuid")]
    public void StrictCodecRejectsMalformedRecordWithoutTreatingItAsMissing(string variant)
    {
        string json = Encoding.UTF8.GetString(RecoveryRecordCodec.Encode(Saved()));
        string bad = variant switch
        {
            "unknown" => json.Replace("\"recordVersion\":1", "\"recordVersion\":1,\"velocity\":0"),
            "duplicate" => json.Replace("\"recordVersion\":1", "\"recordVersion\":1,\"recordVersion\":1"),
            "nestedDuplicate" => json.Replace("\"geometryVersion\":1", "\"geometryVersion\":1,\"geometryVersion\":1"),
            "null" => json.Replace("\"members\":[", "\"members\":null,\"invalid\":["),
            "version" => json.Replace("\"recordVersion\":1", "\"recordVersion\":2"),
            "overflow" => json.Replace("\"runtimeRevision\":1", "\"runtimeRevision\":18446744073709551616"),
            "negative" => json.Replace("\"runtimeRevision\":1", "\"runtimeRevision\":-1"),
            "zero" => json.Replace("\"presenceGeneration\":1", "\"presenceGeneration\":0"),
            "truncated" => json[..^1], "oversize" => json + new string(' ', 65536),
            "hash" => json.Replace("\"lastSuccessfulTick\":900", "\"lastSuccessfulTick\":901"),
            "duplicateIdentity" => json.Replace(SafeSpawnFixtures.PetTwo.ToString("D"), SafeSpawnFixtures.PetOne.ToString("D")),
            "extraMember" => json.Replace("\"members\":[", $"\"members\":[{{\"petId\":\"{OtherOwner:D}\",\"presenceGeneration\":1}},"),
            "emptyGuid" => json.Replace(SafeSpawnFixtures.RoomId.ToString("D"), Guid.Empty.ToString("D")),
            _ => throw new ArgumentException()
        };
        byte[] bytes = Encoding.UTF8.GetBytes(bad);
        Assert.IsFalse(RecoveryRecordCodec.TryDecode(bytes, Config, out RecoveryRecord? parsed, out _)); Assert.IsNull(parsed);
        var fixture = Setup();
        Assert.IsNull(fixture.Machine.Prepare(MetadataInput.Existing(bytes), fixture.Facts, fixture.Claim, Now).Candidate);
        Assert.AreSame(fixture.Claim.LastSuccessfulRecord, fixture.Store.LastSuccessfulRecord);
        Assert.IsNull(fixture.Machine.PublishedFrame); Assert.AreEqual(Guid.Empty, fixture.Machine.CurrentEpoch);
    }

    [TestMethod]
    public void CounterAndConfigurationMismatchesRejectEvenWithValidHash()
    {
        byte[] bytes = RecoveryRecordCodec.Encode(Saved());
        Assert.IsFalse(RecoveryRecordCodec.TryDecode(bytes, Config with { GeometryVersion = 2 }, out _, out _));
        Assert.IsFalse(RecoveryRecordCodec.TryDecode(bytes, Config with { ParameterHash = new string('b', 64) }, out _, out _));
        Assert.ThrowsExactly<ArgumentException>(() => RecoveryRecordCodec.Encode(Saved() with { LastSuccessfulTick = ulong.MaxValue }));
        Assert.ThrowsExactly<ArgumentException>(() => RecoveryRecordCodec.Encode(Saved() with { RecordRevision = 0 }));
        byte[] exactLimit = [.. bytes, .. Enumerable.Repeat((byte)' ', 65536 - bytes.Length)];
        Assert.IsTrue(RecoveryRecordCodec.TryDecode(exactLimit, Config, out _, out _));
        byte[] overLimit = [.. exactLimit, (byte)' '];
        Assert.IsFalse(RecoveryRecordCodec.TryDecode(overLimit, Config, out _, out _));
    }

    [TestMethod]
    public void FirstStartRequiresConfirmedAbsenceAndDisposition()
    {
        var first = Setup(RuntimeDisposition.ConfirmedFirstStart, firstStart: true);
        Assert.AreEqual(RecoveryReason.FirstStart, Prepare(first).Reason);
        var missingWrong = Setup(RuntimeDisposition.ConfirmedWorldLost, firstStart: true);
        Assert.IsNull(missingWrong.Machine.Prepare(MetadataInput.Missing(), missingWrong.Facts, missingWrong.Claim, Now).Candidate);
        var badFirst = Setup(RuntimeDisposition.ConfirmedFirstStart, firstStart: true);
        Assert.IsNull(badFirst.Machine.Prepare(MetadataInput.Existing([0x7b, 0x7d]), badFirst.Facts, badFirst.Claim, Now).Candidate);
        var known = Setup(); Assert.IsNull(known.Machine.Prepare(MetadataInput.Missing(), known.Facts, known.Claim, Now).Candidate);
    }

    [TestMethod]
    public void LatestMembersFilterExactLifecycleAndRoomAndAllowNewMembers()
    {
        var f = Setup();
        MemberIdentity reentry = f.Facts.Members[0] with { PresenceGeneration = 2 };
        f.Facts = f.Facts with { Members = [reentry, f.Facts.Members[1]] };
        RecoveryCandidate candidate = Prepare(f);
        CollectionAssert.AreEqual(new[] { f.Facts.Members[1] }, candidate.MatchingRecordedMembers.ToArray());
        CollectionAssert.AreEqual(new[] { reentry }, candidate.FreshMembers.ToArray());
        Assert.IsFalse(candidate.Members.Contains(Saved().Members[0]));
        var wrongRoom = Setup(saved: Saved() with { RoomId = OtherRoom });
        Assert.IsNull(wrongRoom.Machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(wrongRoom.Claim.LastSuccessfulRecord!)), wrongRoom.Facts, wrongRoom.Claim, Now).Candidate);
        var zero = Setup(); zero.Facts = zero.Facts with { Members = [] }; Assert.IsNull(zero.Machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(Saved())), zero.Facts, zero.Claim, Now).Candidate);
        var crowded = Setup(); crowded.Facts = crowded.Facts with { Members = [.. crowded.Facts.Members, new(OtherRoom, 1)] };
        Assert.IsNull(crowded.Machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(Saved())), crowded.Facts, crowded.Claim, Now).Candidate);
    }

    [TestMethod]
    [DataRow(RuntimeDisposition.Unknown)]
    [DataRow(RuntimeDisposition.FaultLatched)]
    [DataRow(RuntimeDisposition.HealthyRetained)]
    public void ColdStartCannotTurnUnknownFaultOrRetainedWorldIntoBirth(RuntimeDisposition disposition)
    {
        var f = Setup(disposition); Assert.IsNull(f.Machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(Saved())), f.Facts, f.Claim, Now).Candidate);
        Assert.IsNull(f.Machine.PublishedFrame); Assert.AreEqual(Guid.Empty, f.Machine.CurrentEpoch);
    }

    [TestMethod]
    [DataRow(RuntimeDisposition.ConfirmedWorldLost, RecoveryReason.ConfirmedWorldLost)]
    [DataRow(RuntimeDisposition.NormallyReleased, RecoveryReason.NormallyReleased)]
    public void WholeHealthyGroupCommitsEpochReasonAndRecordTogether(RuntimeDisposition disposition, RecoveryReason expectedReason)
    {
        var f = Setup(disposition); RecoveryCandidate c = Prepare(f);
        Assert.IsNull(f.Machine.PublishedFrame); Assert.AreEqual(Guid.Empty, f.Machine.CurrentEpoch); Assert.AreSame(f.Claim.LastSuccessfulRecord, f.Store.LastSuccessfulRecord);
        Assert.IsTrue(f.Machine.CommitPublication(c, Proof(c), f.Facts, Now));
        Assert.AreEqual(expectedReason, f.Machine.PublishedReason); Assert.AreEqual(c.Epoch, f.Machine.CurrentEpoch); Assert.AreNotEqual(OldEpoch, c.Epoch);
        Assert.AreEqual(RecoveryState.AwaitReady, f.Machine.State); Assert.AreEqual(1UL, f.Store.LastSuccessfulRecord!.LastSuccessfulTick);
        Assert.AreEqual(2UL, f.Store.LastSuccessfulRecord.RecordRevision); Assert.AreEqual(c.Epoch, f.Store.LastSuccessfulRecord.PlayEpoch);
    }

    [TestMethod]
    [DataRow("halfGroup")]
    [DataRow("oldEpoch")]
    [DataRow("unhealthy")]
    [DataRow("tickZero")]
    [DataRow("sceneZero")]
    [DataRow("changedRevision")]
    [DataRow("changedOwner")]
    [DataRow("changedGeneration")]
    [DataRow("fault")]
    public void PublicationRechecksCompleteProofAndLatestFactsBeforeAnyRecordChange(string variant)
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); HealthyCommonFrameProof proof = Proof(c); RoomFacts latest = f.Facts;
        switch (variant)
        {
            case "halfGroup": proof = proof with { Members = [c.Members[0]] }; break;
            case "oldEpoch": proof = proof with { Epoch = OldEpoch }; break;
            case "unhealthy": proof = proof with { Healthy = false }; break;
            case "tickZero": proof = proof with { Tick = 0 }; break;
            case "sceneZero": proof = proof with { SceneRevision = 0 }; break;
            case "changedRevision": latest = latest with { FactsRevision = 2 }; break;
            case "changedOwner": latest = latest with { OwnerId = SafeSpawnFixtures.OwnerId }; break;
            case "changedGeneration": latest = latest with { Members = [c.Members[0] with { PresenceGeneration = 2 }, c.Members[1]] }; break;
            case "fault": latest = latest with { Disposition = RuntimeDisposition.FaultLatched }; break;
        }
        Assert.IsFalse(f.Machine.CommitPublication(c, proof, latest, Now)); f.Machine.AbortCandidate();
        Assert.AreSame(f.Claim.LastSuccessfulRecord, f.Store.LastSuccessfulRecord); Assert.AreEqual(Guid.Empty, f.Machine.CurrentEpoch);
        Assert.IsNull(f.Machine.PublishedFrame); Assert.IsNull(f.Machine.PublishedReason);
    }

    [TestMethod]
    public void ClaimsAndWritesRejectOldOwnerEpochRevisionTickAndPreserveLastSuccess()
    {
        var f = Setup(); RecoveryRecord saved = f.Store.LastSuccessfulRecord!;
        Assert.IsFalse(f.Store.TryClaim(SafeSpawnFixtures.OwnerId, 1, OtherOwner, out _)); Assert.AreSame(saved, f.Store.LastSuccessfulRecord);
        var current = saved with { RuntimeRevision = f.Claim.RuntimeRevision, PlayEpoch = f.Claim.Epoch, RecordRevision = 2, LastSuccessfulTick = 1 };
        Assert.IsTrue(f.Store.TryWrite(f.Claim, current)); // New epoch's lower tick is legal.
        foreach (RecoveryRecord bad in new[] { current with { RecordRevision = 1 }, current with { RecordRevision = 3, LastSuccessfulTick = 0 },
            current with { RecordRevision = 3, PlayEpoch = OldEpoch }, current with { RecordRevision = 3, RuntimeRevision = 1 } })
        { Assert.IsFalse(f.Store.TryWrite(f.Claim, bad)); Assert.AreSame(current, f.Store.LastSuccessfulRecord); }
        Assert.IsTrue(f.Store.TryClaim(OtherOwner, 2, SafeSpawnFixtures.OwnerId, out RecoveryClaim? next));
        Assert.AreSame(current, next!.LastSuccessfulRecord); Assert.IsFalse(f.Store.TryWrite(f.Claim, current with { RecordRevision = 3, LastSuccessfulTick = 10 }));
        Assert.AreSame(current, f.Store.LastSuccessfulRecord);
    }

    [TestMethod]
    public void LeaseReleaseUsesControlObserverExactThirtySecondsAndFinalRevision()
    {
        RoomFacts facts = SafeSpawnFixtures.Facts(RuntimeDisposition.HealthyRetained) with { SleepEligible = true, AllLeasesAbsentSince = Now };
        Assert.IsFalse(LeaseGate.CanRelease(facts, Now.AddSeconds(29.999))); Assert.IsTrue(LeaseGate.CanRelease(facts, Now.AddSeconds(30)));
        Assert.IsFalse(LeaseGate.CanRelease(facts with { SleepEligible = false }, Now.AddSeconds(99)));
        Assert.IsFalse(LeaseGate.CanRelease(facts with { AllLeasesAbsentSince = null }, Now.AddSeconds(99)));
        foreach (LeaseKind kind in new[] { LeaseKind.Control, LeaseKind.Observer })
        {
            RoomFacts active = facts with { Leases = [Lease(kind) with { ExpiresAt = Now.AddSeconds(30) }] };
            Assert.IsFalse(LeaseGate.CanRelease(active, Now.AddSeconds(29.999))); Assert.IsTrue(LeaseGate.CanRelease(active, Now.AddSeconds(30)));
            ReleasePermit permit = LeaseGate.PrepareRelease(facts, OldEpoch, Now.AddSeconds(30))!;
            Assert.IsFalse(LeaseGate.RevalidateRelease(permit, active with { FactsRevision = 2, Leases = [Lease(kind)] }, OldEpoch, Now.AddSeconds(30)));
        }
        ReleasePermit p = LeaseGate.PrepareRelease(facts, OldEpoch, Now.AddSeconds(30))!;
        Assert.IsFalse(LeaseGate.RevalidateRelease(p, facts with { OwnerId = OtherOwner }, OldEpoch, Now.AddSeconds(30)));
        Assert.IsFalse(LeaseGate.RevalidateRelease(p, facts with { FactsRevision = 2 }, OldEpoch, Now.AddSeconds(30)));
        Assert.IsFalse(LeaseGate.RevalidateRelease(p, facts, Guid.Empty, Now.AddSeconds(30)));
    }

    [TestMethod]
    public void ReadyInputWatchdogAndPauseNeverChangeEpochOrPublishedFrame()
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); HealthyCommonFrameProof frame = Proof(c); Assert.IsTrue(f.Machine.CommitPublication(c, frame, f.Facts, Now));
        RoomFacts live = f.Facts with { Disposition = RuntimeDisposition.HealthyRetained, Leases = [Lease()] };
        ReadyChallenge challenge = f.Machine.IssueReady(Binding, Lease(), live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsFalse(f.Machine.ConfirmReady(challenge with { Epoch = OldEpoch }, live, Now));
        Assert.IsFalse(f.Machine.ConfirmReady(challenge, live, Now.AddSeconds(1)));
        Assert.IsTrue(f.Machine.ConfirmReady(challenge, live, Now)); Assert.IsFalse(f.Machine.ConfirmReady(challenge, live, Now));
        var input = new InputIntent(c.Epoch, Binding, Lease().LeaseId, Lease().ConnectionId, Lease().Member, 1, 1, true, Now);
        Assert.IsFalse(f.Machine.AcceptInput(input with { Epoch = OldEpoch }, live, Now));
        Assert.IsFalse(f.Machine.AcceptInput(input with { Member = input.Member with { PresenceGeneration = 2 } }, live, Now));
        Assert.IsFalse(f.Machine.AcceptInput(input with { ControlGeneration = 2 }, live, Now));
        Assert.IsTrue(f.Machine.AcceptInput(input, live, Now)); Assert.AreEqual(new InputSample(1, true), f.Machine.ReadInput(Binding, live, Now));
        Assert.AreEqual(new InputSample(1, false), f.Machine.ReadInput(Binding, live, Now.AddMilliseconds(349)));
        Assert.AreEqual(default(InputSample), f.Machine.ReadInput(Binding, live, Now.AddMilliseconds(350)));
        Assert.AreSame(frame, f.Machine.PublishedFrame); Assert.AreEqual(c.Epoch, f.Machine.CurrentEpoch); Assert.AreEqual(RecoveryState.Live, f.Machine.State);
        f.Machine.PauseRetained(); Assert.IsTrue(f.Machine.ResumeRetained(live)); Assert.AreSame(frame, f.Machine.PublishedFrame); Assert.AreEqual(c.Epoch, f.Machine.CurrentEpoch);
        Assert.IsNull(f.Machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(f.Store.LastSuccessfulRecord!)), live, f.Claim, Now).Candidate);
        Assert.AreEqual(RecoveryState.Live, f.Machine.State); Assert.AreSame(frame, f.Machine.PublishedFrame);
    }

    [TestMethod]
    public void ObserverReadyNeverGrantsControlAndLeaseTakeoverRevokesOldBinding()
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); Assert.IsTrue(f.Machine.CommitPublication(c, Proof(c), f.Facts, Now));
        LeaseFact observer = Lease(LeaseKind.Observer); RoomFacts live = f.Facts with { Disposition = RuntimeDisposition.HealthyRetained, Leases = [observer] };
        ReadyChallenge challenge = f.Machine.IssueReady(Binding, observer, live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsTrue(f.Machine.ConfirmReady(challenge, live, Now));
        var intent = new InputIntent(c.Epoch, Binding, observer.LeaseId, observer.ConnectionId, observer.Member, 1, 1, true, Now);
        Assert.IsFalse(f.Machine.AcceptInput(intent, live, Now));
        LeaseFact control = Lease(); live = live with { Leases = [control], FactsRevision = 2 };
        ReadyChallenge controlChallenge = f.Machine.IssueReady(Binding, control, live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsFalse(f.Machine.ConfirmReady(controlChallenge, live with { Leases = [control with { ControlGeneration = 2 }] }, Now));
        Assert.IsTrue(f.Machine.ConfirmReady(controlChallenge, live, Now)); Assert.IsTrue(f.Machine.AcceptInput(intent, live, Now));
        Assert.AreEqual(default(InputSample), f.Machine.ReadInput(Binding, live with { Leases = [control with { ControlGeneration = 2 }] }, Now));
    }

    [TestMethod]
    public void FaultLatchKeepsLastSafePublicationAndStopsFutureBirthInputAndReady()
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); Assert.IsTrue(f.Machine.CommitPublication(c, Proof(c), f.Facts, Now));
        RecoveryRecord last = f.Store.LastSuccessfulRecord!; HealthyCommonFrameProof frame = f.Machine.PublishedFrame!;
        f.Machine.LatchFault(); Assert.AreSame(last, f.Store.LastSuccessfulRecord); Assert.AreSame(frame, f.Machine.PublishedFrame);
        Assert.IsNull(f.Machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(last)), f.Facts, f.Claim, Now).Candidate);
        Assert.AreEqual(RecoveryState.FaultLatched, f.Machine.State);
        Assert.IsNull(f.Machine.IssueReady(Binding, Lease(), f.Facts, Now, TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void ExplicitWrongAlgorithmsAreKilledByIndependentSemanticOracles()
    {
        Assert.IsTrue(SemanticsMutationOracle.Evaluate("none").OraclePassed);
        foreach (string mutation in SemanticsMutationOracle.Names) Assert.IsFalse(SemanticsMutationOracle.Evaluate(mutation).OraclePassed, mutation);
    }

    [TestMethod]
    public void NewLeaseBindingRevokesOldChallengeBindingAndInput()
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); Assert.IsTrue(f.Machine.CommitPublication(c, Proof(c), f.Facts, Now));
        RoomFacts live = f.Facts with { Disposition = RuntimeDisposition.HealthyRetained, Leases = [Lease()] };
        ReadyChallenge b1 = f.Machine.IssueReady(Binding, Lease(), live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsTrue(f.Machine.ConfirmReady(b1, live, Now));
        var oldInput = new InputIntent(c.Epoch, Binding, Lease().LeaseId, Lease().ConnectionId, Lease().Member, 1, 1, true, Now);
        Assert.IsTrue(f.Machine.AcceptInput(oldInput, live, Now));
        ReadyChallenge b2 = f.Machine.IssueReady(OtherRoom, Lease(), live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsTrue(f.Machine.ConfirmReady(b2, live, Now));
        Assert.IsFalse(f.Machine.AcceptInput(oldInput with { ReceivedAt = Now.AddMilliseconds(1) }, live, Now.AddMilliseconds(1)));
        Assert.AreEqual(default(InputSample), f.Machine.ReadInput(Binding, live, Now));
        ReadyChallenge pending1 = f.Machine.IssueReady(Binding, Lease(), live, Now, TimeSpan.FromSeconds(1))!;
        ReadyChallenge pending2 = f.Machine.IssueReady(OtherRoom, Lease(), live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsTrue(f.Machine.ConfirmReady(pending2, live, Now));
        Assert.IsFalse(f.Machine.ConfirmReady(pending1, live, Now));
    }

    [TestMethod]
    public void AmbiguousActiveControlFactsRejectOldReadyInputAndPreserveObservers()
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); Assert.IsTrue(f.Machine.CommitPublication(c, Proof(c), f.Facts, Now));
        LeaseFact control = Lease(); RoomFacts live = f.Facts with { Disposition = RuntimeDisposition.HealthyRetained, Leases = [control] };
        ReadyChallenge challenge = f.Machine.IssueReady(Binding, control, live, Now, TimeSpan.FromSeconds(1))!;
        Assert.IsTrue(f.Machine.ConfirmReady(challenge, live, Now));
        var input = new InputIntent(c.Epoch, Binding, control.LeaseId, control.ConnectionId, control.Member, 1, 1, true, Now);
        Assert.IsTrue(f.Machine.AcceptInput(input, live, Now));
        LeaseFact second = control with { LeaseId = OtherRoom, ControlGeneration = 2 };
        RoomFacts ambiguous = live with { FactsRevision = 2, Leases = [control, second] };
        Assert.IsFalse(f.Machine.AcceptInput(input with { ReceivedAt = Now.AddMilliseconds(1) }, ambiguous, Now.AddMilliseconds(1)));
        Assert.AreEqual(default(InputSample), f.Machine.ReadInput(Binding, ambiguous, Now));
        Assert.IsNull(f.Machine.IssueReady(Binding, control, ambiguous, Now, TimeSpan.FromSeconds(1)));
        RoomFacts observers = live with { Leases = [control, second with { Kind = LeaseKind.Observer }, control with { LeaseId = OldEpoch, Kind = LeaseKind.Observer }] };
        Assert.IsNotNull(f.Machine.IssueReady(Binding, control, observers, Now, TimeSpan.FromSeconds(1)));
        RoomFacts expiredOld = live with { Leases = [control with { ExpiresAt = Now }, second] };
        Assert.IsNotNull(f.Machine.IssueReady(OtherRoom, second, expiredOld, Now, TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void FixtureReceiptTimestampRejectsExactInputReplayAfterJumpConsumption()
    {
        var f = Setup(); RecoveryCandidate c = Prepare(f); Assert.IsTrue(f.Machine.CommitPublication(c, Proof(c), f.Facts, Now));
        RoomFacts live = f.Facts with { Disposition = RuntimeDisposition.HealthyRetained, Leases = [Lease()] };
        ReadyChallenge challenge = f.Machine.IssueReady(Binding, Lease(), live, Now, TimeSpan.FromSeconds(1))!; Assert.IsTrue(f.Machine.ConfirmReady(challenge, live, Now));
        var input = new InputIntent(c.Epoch, Binding, Lease().LeaseId, Lease().ConnectionId, Lease().Member, 1, 1, true, Now);
        Assert.IsTrue(f.Machine.AcceptInput(input, live, Now)); Assert.IsTrue(f.Machine.ReadInput(Binding, live, Now).Jump);
        Assert.IsFalse(f.Machine.AcceptInput(input, live, Now)); Assert.IsFalse(f.Machine.ReadInput(Binding, live, Now).Jump);
    }
}
