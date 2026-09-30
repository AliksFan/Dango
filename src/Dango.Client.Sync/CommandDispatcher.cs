using Dango.Protocol;

namespace Dango.Client.Sync;

public enum CommandDisposition { Ignored, Completed, Retry, ResumeRequired, ResetRequired }
public enum ResumeDisposition { Ready, QueryConsumedPending, DiscardedUnconsumedPending, LedgerMismatch }
public sealed class CommandDispatcher
{
    public const int RetryWindowMs = 30000;
    private readonly object _gate = new();
    private long _fence;
    private long _generation;
    private long _nextOrdinal = 1;
    private string? _roomId;
    private string? _epoch;
    private bool _online;
    private bool _needsResume;
    private (string Type, string? Direction)? _intent;
    private RoomCommand? _pending;
    private double _pendingSince;
    private string? _lastMoveDirection;
    private double _lastMoveAt = double.NegativeInfinity;
    private string? _lastOutcome;
    private string? _lastError;
    public long NextCommandOrdinal { get { lock (_gate) return _nextOrdinal; } }
    public RoomCommand? Pending { get { lock (_gate) return _pending; } }
    public bool NeedsResume { get { lock (_gate) return _needsResume; } }
    public string? LastOutcome { get { lock (_gate) return _lastOutcome; } }
    public string? LastError { get { lock (_gate) return _lastError; } }

    public ResumeDisposition BeginSession(long connectionFence, string roomId, string epoch, long generation, long nextOrdinal)
    {
        lock (_gate)
        {
            if (connectionFence < _fence) return ResumeDisposition.LedgerMismatch;
            if (generation < 1 || nextOrdinal < 1 || !RoomProtocol.IsId(roomId) || !RoomProtocol.IsId(epoch))
                throw new ArgumentException("invalid_session");
            _fence = connectionFence;
            _generation = generation;
            _intent = null;
            _lastMoveDirection = null;
            _lastMoveAt = double.NegativeInfinity;
            _online = true;
            _needsResume = false;
            var sameEpoch = _epoch == epoch && _roomId == roomId;
            if (!sameEpoch) _pending = null;
            _epoch = epoch;
            _roomId = roomId;
            _nextOrdinal = nextOrdinal;
            if (_pending is null) return ResumeDisposition.Ready;
            if (_pending.CommandOrdinal > nextOrdinal)
            {
                _online = false;
                _needsResume = true;
                return ResumeDisposition.LedgerMismatch;
            }
            if (_pending.CommandOrdinal == nextOrdinal)
            {
                _pending = null;
                return ResumeDisposition.DiscardedUnconsumedPending;
            }
            _pending = _pending with { ConnectionGeneration = generation };
            return ResumeDisposition.QueryConsumedPending;
        }
    }

    public bool EnqueueMove(string direction)
    {
        if (direction is not ("left" or "right" or "stopped")) throw new ArgumentException("invalid_direction", nameof(direction));
        return Enqueue(RoomProtocol.Move, direction);
    }
    public bool EnqueueWave() => Enqueue(RoomProtocol.Wave, null);
    private bool Enqueue(string type, string? direction)
    {
        lock (_gate)
        {
            if (!_online || _needsResume) return false;
            _intent = (type, direction); // One unnumbered slot: stop/direction changes supersede stale queued movement.
            return true;
        }
    }

    public RoomCommand? Prepare(long connectionFence, long lastSequence, double nowMs)
    {
        lock (_gate)
        {
            if (!_online || _needsResume || connectionFence != _fence) return null;
            if (_pending is not null)
            {
                if (nowMs - _pendingSince > RetryWindowMs)
                {
                    _needsResume = true;
                    _online = false;
                    _intent = null;
                    return null;
                }
                _pending = _pending with { ConnectionGeneration = _generation, LastAppliedSequence = lastSequence };
                return _pending;
            }
            if (_intent is not { } intent) return null;
            if (intent.Type == RoomProtocol.Move && intent.Direction == _lastMoveDirection && nowMs - _lastMoveAt < 250)
                return null;
            _intent = null;
            _pendingSince = nowMs;
            if (intent.Type == RoomProtocol.Move) { _lastMoveAt = nowMs; _lastMoveDirection = intent.Direction; }
            _pending = new(RoomProtocol.Version, _roomId!, _epoch!, _generation, Guid.NewGuid().ToString("N"),
                _nextOrdinal, lastSequence, intent.Type, new(intent.Direction));
            return _pending;
        }
    }

    public void Disconnect(long connectionFence)
    {
        lock (_gate)
        {
            if (connectionFence != _fence) return;
            _online = false;
            _intent = null;
        }
    }

    public void ClearForReset()
    {
        lock (_gate) { _online = false; _intent = null; _pending = null; _needsResume = false; }
    }

    public CommandDisposition ApplyResult(long connectionFence, CommandResult result)
    {
        lock (_gate)
        {
            if (connectionFence != _fence || !_online || _pending is null || result.CommandId != _pending.CommandId)
                return CommandDisposition.Ignored;
            if (result.RoomEpoch != _epoch || result.Error?.Code is "room_reset_required" or "room_epoch_mismatch")
            {
                ClearForReset();
                return CommandDisposition.ResetRequired;
            }
            if (result.ProtocolVersion != RoomProtocol.Version || result.RoomId != _roomId ||
                result.NextCommandOrdinal < 1 || result.CurrentSequence < 0 || result.ServerTimeMs < 0 ||
                result.Outcome is not ("accepted" or "rejected" or "duplicate") ||
                (result.Outcome == "duplicate" && result.OriginalOutcome is not ("accepted" or "rejected")) ||
                (result.OrdinalConsumed && result.NextCommandOrdinal <= _pending.CommandOrdinal))
            {
                _needsResume = true;
                _intent = null;
                return CommandDisposition.ResumeRequired;
            }
            _lastOutcome = result.Outcome == "duplicate" ? result.OriginalOutcome : result.Outcome;
            _lastError = result.Error?.Code;
            if (result.OrdinalConsumed)
            {
                _nextOrdinal = Math.Max(_nextOrdinal, result.NextCommandOrdinal);
                _pending = null;
                return CommandDisposition.Completed;
            }
            if (result.Error?.Code == "rate_limited") return CommandDisposition.Retry;
            _intent = null;
            _needsResume = true;
            return CommandDisposition.ResumeRequired;
        }
    }
}
