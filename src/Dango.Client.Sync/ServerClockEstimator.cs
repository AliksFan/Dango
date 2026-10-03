namespace Dango.Client.Sync;

/// <summary>Uses monotonic elapsed time only. No DateTime/UTC participates in room animation.</summary>
public sealed class ServerClockEstimator(TimeProvider? timeProvider = null)
{
    public const int MaximumRttMs = 500;
    public const int SuspendThresholdMs = 6000;
    private readonly object _gate = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly long _origin = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private readonly Queue<(double Received, double Rtt, double Offset)> _samples = [];
    private bool _initialized;
    private bool _needsResync;
    private double? _bestRtt;
    private double _offsetStart;
    private double _offsetTarget;
    private double _correctionStarted;
    private double _lastRead;
    private long _lastReturned;
    public double MonotonicMilliseconds => _time.GetElapsedTime(_origin).TotalMilliseconds;
    public bool NeedsResync { get { lock (_gate) return _needsResync; } }
    public double? BestRttMs { get { lock (_gate) return _bestRtt; } }
    public long EstimatedServerTimeMs
    {
        get
        {
            lock (_gate)
            {
                var now = MonotonicMilliseconds;
                if (!_initialized) return 0;
                if (now - _lastRead > SuspendThresholdMs || now < _lastRead) _needsResync = true;
                _lastRead = now;
                if (_needsResync) return _lastReturned;
                _lastReturned = Math.Max(_lastReturned, (long)Math.Max(0, now + OffsetAt(now)));
                return _lastReturned;
            }
        }
    }

    /// <summary>A fresh authoritative synchronization barrier permits a jump, never an old-frame catch-up loop.</summary>
    public void Rebase(long serverTimeMs)
    {
        if (serverTimeMs < 0) throw new ArgumentOutOfRangeException(nameof(serverTimeMs));
        lock (_gate)
        {
            var now = MonotonicMilliseconds;
            _samples.Clear();
            _bestRtt = null;
            _initialized = true;
            _needsResync = false;
            _lastRead = _correctionStarted = now;
            _lastReturned = Math.Max(_lastReturned, serverTimeMs);
            _offsetStart = _offsetTarget = serverTimeMs - now;
        }
    }

    public bool AddSample(double sentMs, double receivedMs, long serverTimeMs)
    {
        lock (_gate)
        {
            var rtt = receivedMs - sentMs;
            if (!double.IsFinite(sentMs) || !double.IsFinite(receivedMs) || sentMs < 0 ||
                rtt < 0 || rtt > MaximumRttMs || serverTimeMs < 0) return false;
            var offset = serverTimeMs - (sentMs + receivedMs) / 2;
            if (_initialized && (receivedMs - _lastRead > SuspendThresholdMs ||
                Math.Abs(offset - OffsetAt(receivedMs)) > 1000))
            {
                _needsResync = true;
                return false;
            }
            if (_needsResync) return false;
            if (!_initialized)
            {
                _initialized = true;
                _offsetStart = _offsetTarget = offset;
                _lastRead = receivedMs;
            }
            _samples.Enqueue((receivedMs, rtt, offset));
            while (_samples.Count > 8 || _samples.Peek().Received < receivedMs - 30000) _samples.Dequeue();
            var best = _samples.MinBy(s => s.Rtt);
            _bestRtt = best.Rtt;
            _offsetStart = OffsetAt(receivedMs);
            _offsetTarget = best.Offset;
            _correctionStarted = receivedMs;
            return true;
        }
    }

    private double OffsetAt(double now) => _offsetStart + (_offsetTarget - _offsetStart) *
        Math.Clamp((now - _correctionStarted) / 150, 0, 1);
}
