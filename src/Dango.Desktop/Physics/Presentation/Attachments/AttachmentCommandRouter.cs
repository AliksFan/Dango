using Dango.Core.Animation;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation.Attachments;

public readonly record struct AttachmentTarget(Guid Scope, DangoEntityId Entity, int Generation);
public sealed record AttachmentChannelIntent(AttachmentChannel Channel, AttachmentToken Token, long Sequence, AttachmentIntent Intent);
public sealed record AttachmentEvent(AttachmentToken Token, long Sequence, AttachmentChannel Channel, AttachmentEventStatus Status);

/// <summary>Scene-owned command ledger. Submission reserves channels atomically;
/// CommitStep publishes an immutable view for one successful fixed-step consumer.</summary>
public sealed class AttachmentCommandRouter(bool enableHatMotion = false) : IDisposable
{
    public bool HatMotionEnabled => enableHatMotion;
    private sealed class Registration(int priority)
    {
        public int Priority { get; } = priority;
        public long Sequence { get; set; }
    }
    private sealed class Ledger(AttachmentTarget target)
    {
        public AttachmentTarget Target { get; } = target;
        public Dictionary<AttachmentToken, Registration> Tokens { get; } = [];
        public Dictionary<AttachmentChannel, AttachmentChannelIntent> Pending { get; } = [];
        public List<AttachmentEvent> Events { get; } = [];
        public HashSet<(AttachmentToken, long, AttachmentChannel)> CommittedEvents { get; } = [];
        public int PendingCount { get; set; }
    }
    private readonly Guid scope = Guid.NewGuid();
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<DangoEntityId, Ledger> entities = [];
    private int generation;
    private bool paused;
    private bool disposed;

    public AttachmentTarget Add(DangoEntityId entity)
    {
        VerifyAccess();
        if (disposed) throw new ObjectDisposedException(nameof(AttachmentCommandRouter));
        if (entity.Value <= 0) throw new ArgumentOutOfRangeException(nameof(entity));
        var target = new AttachmentTarget(scope, entity, checked(++generation));
        entities.Add(entity, new(target));
        return target;
    }

    public AttachmentToken RegisterPreview(AttachmentTarget target) => Register(target, 2);
    internal AttachmentToken RegisterAction(AttachmentTarget target) => Register(target, 1);

    private AttachmentToken Register(AttachmentTarget target, int priority)
    {
        var ledger = Require(target);
        if (paused) throw new InvalidOperationException("Attachment commands are paused.");
        if (ledger.Tokens.Count >= 8) throw new InvalidOperationException("Attachment token capacity reached.");
        var token = new AttachmentToken(Guid.NewGuid());
        ledger.Tokens.Add(token, new(priority));
        return token;
    }

    public AttachmentRequestResult Submit(AttachmentTarget target, AttachmentToken token, long sequence, AttachmentIntent intent)
    {
        VerifyAccess();
        AttachmentRequestResult Reject(AttachmentRejection reason) => new(sequence, AttachmentRequestStatus.Rejected, reason);
        if (disposed || paused) return Reject(AttachmentRejection.Stopped);
        if (!TryGet(target, out var ledger)) return Reject(AttachmentRejection.InvalidTarget);
        if (!ledger.Tokens.TryGetValue(token, out var registration)) return Reject(AttachmentRejection.InvalidToken);
        if (intent is null || !intent.IsValid) return Reject(AttachmentRejection.InvalidParameter);
        if (sequence <= registration.Sequence) return Reject(AttachmentRejection.OldSequence);
        // Dynamic hat excitation is enabled only after the geometry/manual gate.
        if (intent is not (SetGaze or SetEyes or Blink or SetHatSlide or Release) && !(enableHatMotion && intent is PulseHat)) return Reject(AttachmentRejection.Unsupported);
        if (ledger.PendingCount >= 64) return Reject(AttachmentRejection.Capacity);
        var channels = Split(intent.Channels).ToArray();
        if (intent.IsEvent && ledger.Events.Count + channels.Length > 128) return Reject(AttachmentRejection.Capacity);
        if (intent is not Release)
        {
            foreach (var channel in channels)
            {
                if (!ledger.Pending.TryGetValue(channel, out var current)) continue;
                var priority = ledger.Tokens[current.Token].Priority;
                if (priority > registration.Priority || (current.Intent.IsEvent && priority >= registration.Priority))
                    return Reject(AttachmentRejection.Busy);
            }
        }
        // All validation precedes mutations, including the sequence watermark.
        foreach (var channel in channels)
        {
            if (intent is Release)
            {
                if (ledger.Pending.TryGetValue(channel, out var owned) && owned.Token == token) Cancel(ledger, channel);
                continue;
            }
            Cancel(ledger, channel);
            ledger.Pending[channel] = new(channel, token, sequence, intent);
            if (intent.IsEvent) ledger.Events.Add(new(token, sequence, channel, AttachmentEventStatus.Active));
        }
        registration.Sequence = sequence;
        ledger.PendingCount++;
        return new(sequence, AttachmentRequestStatus.Accepted, AttachmentRejection.None);
    }

    public IReadOnlyList<AttachmentChannelIntent> CommitStep(AttachmentTarget target)
    {
        var ledger = Require(target);
        if (paused) throw new InvalidOperationException("Cannot commit while paused.");
        ledger.PendingCount = 0;
        foreach (var item in ledger.Pending.Values.Where(item => item.Intent.IsEvent))
            ledger.CommittedEvents.Add((item.Token, item.Sequence, item.Channel));
        return Array.AsReadOnly(ledger.Pending.Values.OrderBy(item => item.Channel).ToArray());
    }

    internal bool CompleteEvent(AttachmentTarget target, AttachmentToken token, long sequence, AttachmentChannel channel)
    {
        var ledger = Require(target);
        if (paused || !ledger.CommittedEvents.Remove((token, sequence, channel))) return false;
        if (!ledger.Pending.TryGetValue(channel, out var current) || current.Token != token || current.Sequence != sequence) return false;
        var index = ledger.Events.FindIndex(e => e.Token == token && e.Sequence == sequence && e.Channel == channel && e.Status == AttachmentEventStatus.Active);
        if (index < 0) return false;
        ledger.Events[index] = ledger.Events[index] with { Status = AttachmentEventStatus.Completed };
        ledger.Pending.Remove(channel);
        return true;
    }

    public IReadOnlyList<AttachmentEvent> ConsumeResults(AttachmentTarget target)
    {
        var ledger = Require(target);
        var results = ledger.Events.Where(e => e.Status != AttachmentEventStatus.Active).ToArray();
        ledger.Events.RemoveAll(e => e.Status != AttachmentEventStatus.Active);
        return Array.AsReadOnly(results);
    }

    public AttachmentTarget Reset(AttachmentTarget target) => Reset(target, out _);

    public AttachmentTarget Reset(AttachmentTarget target, out IReadOnlyList<AttachmentEvent> finalResults)
    {
        finalResults = Remove(target);
        return Add(target.Entity);
    }

    public IReadOnlyList<AttachmentEvent> Remove(AttachmentTarget target)
    {
        var ledger = Require(target);
        var results = ledger.Events.Select(e => e.Status == AttachmentEventStatus.Active ? e with { Status = AttachmentEventStatus.Cancelled } : e).ToArray();
        entities.Remove(target.Entity);
        return Array.AsReadOnly(results);
    }

    public void SetPaused(bool value) { VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this); paused = value; }
    public void Dispose() { VerifyAccess(); disposed = true; entities.Clear(); }

    private static void Cancel(Ledger ledger, AttachmentChannel channel)
    {
        if (!ledger.Pending.Remove(channel, out var current) || !current.Intent.IsEvent) return;
        ledger.CommittedEvents.Remove((current.Token, current.Sequence, channel));
        var index = ledger.Events.FindIndex(e => e.Token == current.Token && e.Sequence == current.Sequence && e.Channel == channel && e.Status == AttachmentEventStatus.Active);
        if (index >= 0) ledger.Events[index] = ledger.Events[index] with { Status = AttachmentEventStatus.Cancelled };
    }

    private bool TryGet(AttachmentTarget target, out Ledger ledger)
    {
        if (target.Scope == scope && entities.TryGetValue(target.Entity, out var found) && found.Target == target)
        { ledger = found; return true; }
        ledger = null!;
        return false;
    }

    private Ledger Require(AttachmentTarget target)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        return TryGet(target, out var ledger) ? ledger : throw new ArgumentException("Stale or foreign attachment target.", nameof(target));
    }
    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Attachment router belongs to its scene thread.");
    }
    private static IEnumerable<AttachmentChannel> Split(AttachmentChannel mask) =>
        Enum.GetValues<AttachmentChannel>().Where(c => c != AttachmentChannel.None && c != AttachmentChannel.All && (mask & c) != 0);
}
