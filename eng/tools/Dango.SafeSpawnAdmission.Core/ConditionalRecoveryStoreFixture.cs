namespace Dango.SafeSpawnAdmission.Core;

// In-memory CAS fixture only; not a database transaction or a persistent fault fact.
public sealed class ConditionalRecoveryStoreFixture
{
    private readonly object gate = new();
    private Guid owner;
    private ulong runtimeRevision;
    private Guid epoch;
    private RecoveryRecord? last;
    public ConditionalRecoveryStoreFixture(Guid ownerId, ulong revision, RecoveryRecord? lastSuccessfulRecord = null)
    {
        if (ownerId == Guid.Empty || !ContractValidation.Counter(revision) ||
            (lastSuccessfulRecord is not null && (!ContractValidation.Record(lastSuccessfulRecord) || lastSuccessfulRecord.RuntimeRevision > revision)))
            throw new ArgumentException("Invalid store fixture");
        owner = ownerId; runtimeRevision = revision; last = lastSuccessfulRecord; epoch = last?.PlayEpoch ?? Guid.Empty;
    }
    public RecoveryRecord? LastSuccessfulRecord { get { lock (gate) return last; } }
    public bool TryClaim(Guid expectedOwner, ulong expectedRuntimeRevision, Guid newOwner, out RecoveryClaim? claim)
    {
        lock (gate)
        {
            claim = null;
            if (owner != expectedOwner || runtimeRevision != expectedRuntimeRevision || newOwner == Guid.Empty || runtimeRevision >= ContractValidation.MaximumCounter) return false;
            owner = newOwner; runtimeRevision++; epoch = Guid.NewGuid();
            claim = new(owner, runtimeRevision, epoch, last); return true;
        }
    }
    public bool IsCurrent(RecoveryClaim claim)
    { lock (gate) return owner == claim.OwnerId && runtimeRevision == claim.RuntimeRevision && epoch == claim.Epoch; }
    public bool TryWrite(RecoveryClaim claim, RecoveryRecord record)
    {
        lock (gate)
        {
            if (!ContractValidation.Record(record) || owner != claim.OwnerId || runtimeRevision != claim.RuntimeRevision || epoch != claim.Epoch ||
                record.RuntimeRevision != runtimeRevision || record.PlayEpoch != epoch ||
                (last is not null && (record.RoomId != last.RoomId || record.Configuration != last.Configuration ||
                    record.RecordRevision <= last.RecordRevision || (record.PlayEpoch == last.PlayEpoch && record.LastSuccessfulTick < last.LastSuccessfulTick)))) return false;
            last = record; return true;
        }
    }
}
