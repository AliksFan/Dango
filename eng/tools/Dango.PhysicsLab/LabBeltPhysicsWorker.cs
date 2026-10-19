using System.Collections.Concurrent;
using System.Diagnostics;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics.Configuration;

namespace Dango.PhysicsLab;

internal interface ILabBeltWorkerSession : IDisposable
{
    LabBeltCapabilities Capabilities { get; }
    LabBeltStepSnapshot Step(long epoch, int tick, LabInputSnapshot input, LabBeltInput actualInput);
    Dictionary<string, byte[]> ReadState();
}

internal sealed class LabBeltNativeWorkerSession : ILabBeltWorkerSession
{
    private readonly LabBeltWorld world;
    private readonly DangoSelfPhysicsParameters config;
    private readonly bool calibrated;
    public LabBeltCapabilities Capabilities { get; }
    internal LabBeltNativeWorkerSession(string module, DangoSelfPhysicsParameters config, bool shortBones,
        LabBeltCalibrationParameters? calibration, bool guard, bool radial)
    {
        this.config = config; calibrated = calibration is not null;
        world = new(module, config, shortBones, calibration, enableShapeGuard: guard,
            enableSpeedSaturation: guard, enableWhaleDrive: false, enableRadialDrive: radial);
        try
        {
            if (calibrated) world.SetOriginalGroundJump();
            world.SetBounds(-256, 256); Capabilities = LabBeltCapabilities.From(world);
        }
        catch { world.Dispose(); throw; }
    }
    public LabBeltStepSnapshot Step(long epoch, int tick, LabInputSnapshot input, LabBeltInput actualInput) =>
        LabBeltStepPipeline.Step(world, epoch, tick, input, actualInput, config.MaximumSpeed, calibrated);
    public Dictionary<string, byte[]> ReadState() => world.ReadPerformanceState();
    public void Dispose() => world.Dispose();
}

internal sealed record LabBeltWorkerMetrics(long Epoch, int ProducedTicks, int AppliedTicks, int QueueDepth,
    int MaximumQueueDepth, long RejectedSnapshots, int BackpressurePauses, double DiscardedSeconds, bool Running,
    bool TerminalPending, double OldestSnapshotAgeMilliseconds);

/// <summary>One dedicated native owner. All commands complete after the current finite native call.</summary>
internal sealed class LabBeltPhysicsWorker
{
    private readonly object gate = new();
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Queue<LabBeltStepSnapshot> snapshots = new();
    private readonly Thread thread;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int capacity;
    private ILabBeltWorkerSession? session;
    private LabBeltStepSnapshot? terminal;
    private LabInputSnapshot latest;
    private LabBeltInput latestActual;
    private long epoch, jumpSequence, consumedJump, interactSequence, consumedInteract, cancelSequence, consumedCancel;
    private int tick, produced, applied, maximumDepth, backpressure, replayTick;
    private long rejected;
    private double discarded, accumulator;
    private bool running, closing, replay;
    private bool previousJump, previousCancel, previousInteract;
    private long previous;
    private LabBeltCapabilities? capabilities;
    internal LabBeltCapabilities? Capabilities { get { lock (gate) return capabilities; } }
    internal long Epoch { get { lock (gate) return epoch; } }
    internal LabBeltPhysicsWorker(int capacity = 8)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        thread = new Thread(Run) { IsBackground = true, Name = "Dango PhysicsLab native owner" }; thread.Start();
    }
    private Task<T> Command<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (closing) return Task.FromException<T>(new ObjectDisposedException(nameof(LabBeltPhysicsWorker)));
            commands.Enqueue(() => { try { completion.SetResult(action()); } catch (Exception error) { completion.SetException(error); } });
            wake.Set();
        }
        return completion.Task;
    }
    private long Invalidate()
    {
        lock (gate)
        {
            epoch++; running = false; latest = default; latestActual = default;
            consumedJump = jumpSequence; consumedInteract = interactSequence; consumedCancel = cancelSequence;
            previousJump = previousCancel = previousInteract = false;
            rejected += snapshots.Count; snapshots.Clear(); terminal = null;
            return epoch;
        }
    }
    internal Task<LabBeltCapabilities> ConfigureAsync(Func<ILabBeltWorkerSession> factory)
    {
        var requested = Invalidate();
        return Command(() =>
        {
            RequireEpoch(requested);
            session?.Dispose(); session = null; lock (gate) capabilities = null;
            var replacement = factory();
            lock (gate)
            {
                if (requested != epoch || closing) { replacement.Dispose(); throw new OperationCanceledException("Configuration superseded."); }
                session = replacement; capabilities = replacement.Capabilities;
                tick = produced = applied = maximumDepth = backpressure = replayTick = 0; discarded = accumulator = 0;
                replay = false; return capabilities;
            }
        });
    }
    internal Task<long> PauseAsync()
    {
        var requested = Invalidate();
        return Command(() => { accumulator = 0; previous = Stopwatch.GetTimestamp(); return requested; });
    }
    internal Task<long> ClearSessionAsync()
    {
        var requested = Invalidate();
        return Command(() =>
        {
            RequireEpoch(requested);
            var previousSession = session; session = null;
            lock (gate) capabilities = null;
            previousSession?.Dispose();
            RequireEpoch(requested);
            accumulator = 0; replay = false;
            return requested;
        });
    }
    internal Task<bool> ResumeAsync(bool scriptedReplay = false)
    {
        var requested = Epoch;
        return Command(() =>
        {
            lock (gate)
            {
                if (requested != epoch || session is null) return false;
                replay = scriptedReplay; if (scriptedReplay) replayTick = 0;
                accumulator = 0; previous = Stopwatch.GetTimestamp(); running = true; return true;
            }
        });
    }
    internal void PublishInput(long inputEpoch, LabInputSnapshot input, LabBeltInput actualInput)
    {
        lock (gate)
        {
            if (inputEpoch != epoch || closing) return;
            var nextJump = jumpSequence + (input.JumpPressed && !previousJump ? 1 : 0);
            var nextCancel = cancelSequence + (input.CancelForces && !previousCancel ? 1 : 0);
            var nextInteract = interactSequence + (input.InteractPressed && !previousInteract ? 1 : 0);
            previousJump = input.JumpPressed; previousCancel = input.CancelForces; previousInteract = input.InteractPressed;
            PublishLocked(input, actualInput, nextJump, nextCancel, nextInteract); wake.Set();
        }
    }
    internal void PublishInput(long inputEpoch, LabInputSnapshot input, LabBeltInput actualInput,
        long jumpSequence, long cancelSequence, long interactSequence = 0)
    {
        lock (gate)
        {
            if (inputEpoch != epoch || closing) return;
            PublishLocked(input, actualInput, jumpSequence, cancelSequence, interactSequence); wake.Set();
        }
    }
    private void PublishLocked(LabInputSnapshot input, LabBeltInput actualInput, long jump, long cancel, long interact)
    {
        if (jump < jumpSequence || cancel < cancelSequence || interact < interactSequence) return;
        var previousJumpSequence = jumpSequence; var previousInteractSequence = interactSequence;
        jumpSequence = jump; interactSequence = interact;
        if (cancel > cancelSequence)
        {
            consumedJump = previousJumpSequence; consumedInteract = previousInteractSequence;
            latest = input; latestActual = actualInput;
        }
        else { latest = input; latestActual = actualInput; }
        cancelSequence = cancel;
    }
    private void RequireEpoch(long requested)
    {
        lock (gate) if (requested != epoch || closing) throw new OperationCanceledException("Stale worker generation.");
    }
    internal Task<LabBeltStepSnapshot> StepAsync(LabInputSnapshot input, LabBeltInput actualInput)
    {
        var requested = Epoch;
        return Command(() =>
        {
            lock (gate) { if (requested != epoch) throw new OperationCanceledException("Stale manual step epoch."); running = false; }
            var result = Step(requested, input, actualInput);
            RequireEpoch(requested); return result;
        });
    }
    internal Task<Dictionary<string, byte[]>> ReadStateAsync()
    {
        var requested = Epoch;
        return Command(() => { RequireEpoch(requested); var result = session!.ReadState(); RequireEpoch(requested); return result; });
    }
    internal bool TryDequeue(out LabBeltStepSnapshot? snapshot)
    {
        lock (gate)
        {
            if (snapshots.TryDequeue(out snapshot)) return true;
            if (terminal is null) return false;
            snapshot = terminal; terminal = null; return true;
        }
    }
    internal void MarkApplied(long snapshotEpoch)
    {
        lock (gate) { if (snapshotEpoch == epoch) applied++; }
    }
    internal LabBeltWorkerMetrics Metrics
    {
        get
        {
            lock (gate)
            {
                var oldestAge = snapshots.TryPeek(out var oldest)
                    ? Stopwatch.GetElapsedTime(oldest.PublishedTimestamp).TotalMilliseconds : 0;
                return new(epoch, produced, applied, snapshots.Count, maximumDepth, rejected, backpressure,
                    discarded, running, terminal is not null, oldestAge);
            }
        }
    }
    internal Task CloseAsync()
    {
        lock (gate)
        {
            if (closing) return stopped.Task;
            Invalidate(); closing = true; wake.Set(); return stopped.Task;
        }
    }
    private LabBeltStepSnapshot Step(long currentEpoch, LabInputSnapshot input, LabBeltInput actual)
    {
        int next; lock (gate) next = tick + 1;
        LabBeltStepSnapshot result;
        try { result = session!.Step(currentEpoch, next, input, actual); }
        catch (Exception error)
        {
            result = new(currentEpoch, next, input, actual, null, null, null, null, null, null,
                null, null, null, error, 0, 0, Stopwatch.GetTimestamp());
        }
        result = result with
        {
            Frame = result.Frame is { } frame ? new((float[])frame.Scaffold.Clone(), (float[])frame.Curve.Clone(), (float[])frame.Stats.Clone()) : null,
            Guard = result.Guard?.ToArray(), Speed = result.Speed?.ToArray(), Radial = result.Radial?.ToArray(),
            Rear = result.Rear?.ToArray(), Ledger = result.Ledger?.ToArray(), Diagnostics = result.Diagnostics?.ToArray(),
            PreState = result.PreState?.ToArray(), SolverFailure = result.SolverFailure?.ToArray(),
        };
        if (result.Error is null) { lock (gate) { tick++; produced++; } }
        return result;
    }
    internal Task<bool> AdvanceAsync()
    {
        var requested = Epoch;
        return Command(() => { RequireEpoch(requested); lock (gate) running = false; return Produce(requested); });
    }
    private bool Produce(long currentEpoch)
    {
        LabInputSnapshot input; LabBeltInput actual;
        lock (gate)
        {
            if (currentEpoch != epoch || closing || session is null || terminal is not null) return false;
            if (snapshots.Count >= capacity)
            {
                running = false; backpressure++;
                terminal = new(epoch, tick + 1, latest, latestActual, null, null, null, null, null, null,
                    null, null, null, new InvalidOperationException("物理快照FIFO持续回压，实验已暂停；请应用并重置。"), 0, 0, Stopwatch.GetTimestamp());
                return false;
            }
            if (replay)
            {
                if (replayTick >= 1860) { running = false; return false; }
                var h = replayTick is >= 300 and < 900 ? 1 : replayTick is >= 1080 and < 1680 ? -1 : 0;
                replayTick++; input = new(h, 0, 0, h != 0, false, false, false);
                actual = new(Horizontal: h, InputActive: h != 0);
            }
            else
            {
                var jump = consumedJump < jumpSequence; if (jump) consumedJump++;
                var interact = consumedInteract < interactSequence; if (interact) consumedInteract++;
                var cancel = consumedCancel < cancelSequence; if (cancel) consumedCancel++;
                input = latest with { JumpPressed = jump, InteractPressed = interact, CancelForces = cancel || latest.CancelForces };
                actual = latestActual with { JumpPressed = jump, CancelForces = cancel || latest.CancelForces };
            }
        }
        var snapshot = Step(currentEpoch, input, actual);
        lock (gate)
        {
            if (currentEpoch != epoch || closing) { rejected++; return false; }
            snapshots.Enqueue(snapshot); maximumDepth = Math.Max(maximumDepth, snapshots.Count);
            if (snapshot.Error is not null) running = false;
            if (replay && replayTick >= 1860) running = false;
        }
        return snapshot.Error is null;
    }
    internal static int SchedulerWaitMilliseconds(double accumulatorSeconds, double workSeconds)
    {
        var remaining = LabWindow.FixedDt - accumulatorSeconds - workSeconds;
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000);
    }
    private void Run()
    {
        previous = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                while (commands.TryDequeue(out var command)) command();
                long currentEpoch; bool active;
                lock (gate) { if (closing) break; currentEpoch = epoch; active = running; }
                if (!active) { wake.WaitOne(); previous = Stopwatch.GetTimestamp(); continue; }
                var now = Stopwatch.GetTimestamp(); var elapsed = Stopwatch.GetElapsedTime(previous, now).TotalSeconds; previous = now;
                accumulator += Math.Min(elapsed, .25); var count = 0;
                while (accumulator >= LabWindow.FixedDt && count++ < 4)
                {
                    lock (gate) if (!running || currentEpoch != epoch || !commands.IsEmpty) break;
                    if (!Produce(currentEpoch)) break;
                    accumulator -= LabWindow.FixedDt;
                }
                if (accumulator > 4 * LabWindow.FixedDt) { lock (gate) discarded += accumulator; accumulator = 0; }
                // accumulator is current only through previous. Account for this
                // batch's native work before sleeping; the next iteration still
                // adds that elapsed time once, under the unchanged catch-up policy.
                var workSeconds = Stopwatch.GetElapsedTime(previous).TotalSeconds;
                var wait = SchedulerWaitMilliseconds(accumulator, workSeconds);
                wake.WaitOne(wait);
            }
        }
        finally
        {
            Exception? error = null;
            try { session?.Dispose(); } catch (Exception caught) { error = caught; }
            lock (gate) { closing = true; capabilities = null; wake.Dispose(); }
            if (error is null) stopped.TrySetResult(); else stopped.TrySetException(error);
        }
    }
}
