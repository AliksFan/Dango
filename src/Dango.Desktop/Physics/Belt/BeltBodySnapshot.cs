using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

/// <summary>Owned arrays from one successful physical tick. The worker never mutates published arrays.</summary>
public sealed record BeltBodySnapshot(long Epoch, long BodyEpoch, long ConfigurationRevision, long Sequence,
    int Tick, DangoBodyControls Body, DangoBeltInput ActualInput, DangoBeltFrame? Frame,
    double[]? DebugState, Exception? Error, double NativeStepMilliseconds, double NativeReadMilliseconds,
    long PublishedTimestamp, bool IsSleeping = false)
{
    public string ModelVersion => DangoBeltWorld.Model;
    public string TuningVersion => DangoBeltWorld.TuningVersion;
    public double FixedDt => DangoBeltWorld.FixedDt;
    public float[]? Guard { get; init; }
    public double[]? Speed { get; init; }
    public double[]? Radial { get; init; }
    public double[]? Rear { get; init; }
    public double[]? Ledger { get; init; }
    public double[]? SolverFailure { get; init; }
    public double[]? PreState { get; init; }
}

public sealed record BeltBodyMetrics(long Epoch, long BodyEpoch, int ProducedTicks, int DisplayedTicks,
    int QueueDepth, int MaximumQueueDepth, long InvalidatedSnapshots, bool Running, bool Sleeping,
    bool BackpressureWaiting, int BackpressurePauses, long WakeSequence, long NativeStepCalls,
    double NativeStepMilliseconds, double NativeReadMilliseconds, double DiscardedSeconds,
    Exception? Error, long ConfigurationRevision, double OldestSnapshotAgeMilliseconds);
