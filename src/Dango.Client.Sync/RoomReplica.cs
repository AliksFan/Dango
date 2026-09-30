using System.Collections.Immutable;
using System.Text.Json;
using Dango.Protocol;

namespace Dango.Client.Sync;

public enum ReplicaState { Disconnected, Joining, Live, Resyncing, ResetRequired, Incompatible, Faulted }
public enum ReplicaUpdate { Ignored, Applied, Buffered, ResyncRequired, ResetRequired, Invalid }
public readonly record struct SnapshotFence(long Connection, long Attempt, long MinimumSequence);
public sealed record ReplicaFrame(RoomSnapshot? Snapshot, ReplicaState State, long ConnectionFence,
    long SyncAttemptId, string? Reason)
{
    public long LastSequence => Snapshot?.LastSequence ?? 0;
}

/// <summary>Only confirmed snapshots and contiguous public events can replace the immutable frame.</summary>
public sealed class RoomReplica
{
    public const int BufferCapacity = 64;
    private readonly object _gate = new();
    private readonly SortedDictionary<long, RoomEvent> _buffer = [];
    private readonly SortedDictionary<long, string> _seen = [];
    private ReplicaFrame _frame = new(null, ReplicaState.Disconnected, 0, 0, null);
    private string? _roomId;
    private string? _epoch;
    public ReplicaFrame Frame { get { lock (_gate) return _frame; } }
    public long BeginConnection(string roomId, string epoch)
    {
        lock (_gate)
        {
            if (!RoomProtocol.IsId(roomId) || !RoomProtocol.IsId(epoch)) throw new ArgumentException("invalid_room");
            var sameRoom = _roomId == roomId && _epoch == epoch;
            _roomId = roomId;
            _epoch = epoch;
            _buffer.Clear();
            if (!sameRoom) _seen.Clear();
            _frame = _frame with { Snapshot = sameRoom ? _frame.Snapshot : null,
                ConnectionFence = _frame.ConnectionFence + 1, SyncAttemptId = _frame.SyncAttemptId + 1,
                State = ReplicaState.Joining, Reason = null };
            return _frame.ConnectionFence;
        }
    }
    public SnapshotFence BeginSynchronization(long connection)
    {
        lock (_gate)
        {
            if (!IsCurrent(connection)) return new(connection, -1, _frame.LastSequence);
            _frame = _frame with { SyncAttemptId = _frame.SyncAttemptId + 1, State = ReplicaState.Resyncing };
            return new(connection, _frame.SyncAttemptId, _frame.LastSequence);
        }
    }
    public ReplicaUpdate ApplySnapshot(SnapshotFence fence, RoomSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!IsCurrent(fence.Connection) || fence.Attempt != _frame.SyncAttemptId) return ReplicaUpdate.Ignored;
            if (snapshot.RoomEpoch != _epoch) return Reset();
            if (!RoomProtocol.IsSnapshot(snapshot) || snapshot.RoomId != _roomId ||
                (_frame.Snapshot is { } existing && (snapshot.SelfPetId != existing.SelfPetId ||
                    !snapshot.Pets.Select(p => (p.PetId, p.ParticipantId)).Order().SequenceEqual(
                        existing.Pets.Select(p => (p.PetId, p.ParticipantId)).Order()))))
                return Invalid("invalid_snapshot");
            if (snapshot.LastSequence < Math.Max(fence.MinimumSequence, _frame.LastSequence))
                return RequireResync("snapshot_rollback");
            _frame = _frame with { Snapshot = Freeze(snapshot), State = ReplicaState.Live, Reason = null };
            foreach (var key in _buffer.Keys.Where(k => k <= snapshot.LastSequence).ToArray()) _buffer.Remove(key);
            while (_buffer.Remove(_frame.LastSequence + 1, out var next))
            {
                if (!Apply(next)) return Invalid("invalid_event_identity");
            }
            return _buffer.Count > 0 ? RequireResync("sequence_gap") : ReplicaUpdate.Applied;
        }
    }
    public ReplicaUpdate Receive(long connection, RoomEvent value)
    {
        lock (_gate)
        {
            if (!IsCurrent(connection)) return ReplicaUpdate.Ignored;
            if (value.RoomEpoch != _epoch) return Reset();
            if (!RoomProtocol.IsEvent(value) || value.RoomId != _roomId) return Invalid("invalid_event");
            var fingerprint = JsonSerializer.Serialize(value, RoomProtocol.Json);
            if (_seen.TryGetValue(value.Sequence, out var seen) && seen != fingerprint)
                return Invalid("contradictory_event");
            if (_buffer.TryGetValue(value.Sequence, out var buffered))
                return JsonSerializer.Serialize(buffered, RoomProtocol.Json) == fingerprint
                    ? ReplicaUpdate.Ignored : Invalid("contradictory_event");
            if (value.Sequence <= _frame.LastSequence) return ReplicaUpdate.Ignored;
            if (_frame.State == ReplicaState.Live && value.Sequence == _frame.LastSequence + 1)
                return Apply(value) ? ReplicaUpdate.Applied : Invalid("invalid_event_identity");
            if (_buffer.Count == BufferCapacity)
            {
                _buffer.Clear();
                return RequireResync("buffer_overflow");
            }
            _buffer.Add(value.Sequence, Freeze(value));
            return _frame.State == ReplicaState.Live ? RequireResync("sequence_gap") : ReplicaUpdate.Buffered;
        }
    }
    public ReplicaUpdate ObserveWatermark(long connection, TimeSample value)
    {
        lock (_gate)
        {
            if (!IsCurrent(connection)) return ReplicaUpdate.Ignored;
            if (value.RoomEpoch != _epoch) return Reset();
            if (value.ProtocolVersion != RoomProtocol.Version || value.RoomId != _roomId ||
                value.LastSequence < 0 || value.ServerTimeMs < 0) return Invalid("invalid_time_sample");
            return value.LastSequence > _frame.LastSequence ? RequireResync("watermark_ahead") : ReplicaUpdate.Ignored;
        }
    }

    public void Disconnect(long connection, string reason = "disconnected")
    {
        lock (_gate)
        {
            if (connection != _frame.ConnectionFence ||
                _frame.State is ReplicaState.ResetRequired or ReplicaState.Faulted or ReplicaState.Incompatible) return;
            _buffer.Clear();
            _frame = _frame with { State = ReplicaState.Disconnected, SyncAttemptId = _frame.SyncAttemptId + 1,
                Reason = reason };
        }
    }

    public void ResetRequired(long connection)
    {
        lock (_gate) { if (connection == _frame.ConnectionFence) Reset(); }
    }

    private bool IsCurrent(long connection) => connection == _frame.ConnectionFence &&
        _frame.State is ReplicaState.Joining or ReplicaState.Live or ReplicaState.Resyncing;

    private ReplicaUpdate RequireResync(string reason)
    {
        _frame = _frame with { State = ReplicaState.Resyncing, Reason = reason };
        return ReplicaUpdate.ResyncRequired;
    }

    private ReplicaUpdate Invalid(string reason)
    {
        _buffer.Clear();
        _frame = _frame with { State = ReplicaState.Faulted, Reason = reason, SyncAttemptId = _frame.SyncAttemptId + 1 };
        return ReplicaUpdate.Invalid;
    }

    private ReplicaUpdate Reset()
    {
        _buffer.Clear();
        _seen.Clear();
        _frame = _frame with { Snapshot = null, State = ReplicaState.ResetRequired,
            Reason = "room_reset_required", SyncAttemptId = _frame.SyncAttemptId + 1 };
        return ReplicaUpdate.ResetRequired;
    }

    private bool Apply(RoomEvent value)
    {
        var snapshot = _frame.Snapshot!;
        if (!snapshot.Pets.Any(p => p.PetId == value.Payload.PetId && p.ParticipantId == value.Payload.ParticipantId)) return false;
        _frame = _frame with { Snapshot = snapshot with { LastSequence = value.Sequence, ServerTimeMs = value.ServerTimeMs,
            Pets = snapshot.Pets.Select(p => p.PetId == value.Payload.PetId ? Freeze(value.Payload) : p).ToImmutableArray() } };
        _seen[value.Sequence] = JsonSerializer.Serialize(value, RoomProtocol.Json);
        while (_seen.Count > 128) _seen.Remove(_seen.First().Key);
        return true;
    }

    private static PetState Freeze(PetState pet) => pet.Action is { } action
        ? pet with { Action = action with { Participants = action.Participants.ToImmutableArray() } } : pet;
    private static RoomSnapshot Freeze(RoomSnapshot snapshot) => snapshot with { Pets = snapshot.Pets.Select(Freeze).ToImmutableArray() };
    private static RoomEvent Freeze(RoomEvent value) => value with { Payload = Freeze(value.Payload) };
}
