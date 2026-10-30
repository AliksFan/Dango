using System.Diagnostics;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

/// <summary>
/// One long-lived native owner, a FIFO of eight complete successful ticks, and event-only sleep.
/// Native work never holds gate. Lifecycle invalidation happens immediately; the command barrier
/// completes only after the finite in-flight native call has returned to this owner.
/// </summary>
public sealed class BeltBodyRuntime : IBodyPreviewRuntime
{
    private const int Capacity = 8;
    private const double BackpressureTimeoutSeconds = .5;
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Queue<Action> commands = new();
    private readonly Queue<BeltBodySnapshot> snapshots = new();
    private readonly Func<DangoBodyControls, IBeltBodySession> factory;
    private readonly Func<long> timestamp;
    private readonly long frequency;
    private readonly bool manualScheduling;
    private readonly BeltSleepPolicy sleep = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread owner;
    private IBeltBodySession? session;
    private (double Left, double Right)? requestedBounds;
    private bool solidWalls;
    private ConfigurationRequest? pending;
    private DangoBodyControls body = DangoBodyControls.Default;
    private DangoBeltInput latest;
    private BeltBodySnapshot? terminal;
    private BeltBodySnapshot? lastFailure;
    private Exception? fault;
    private long epoch, bodyEpoch = -1, configurationRevision = -1, requestedRevision = -1;
    private long sequence, displayedSequence, invalidated, wakeSequence, jumpSequence, consumedJump, cancelSequence, consumedCancel;
    private long stepCalls, fullSince;
    private int tick, produced, displayed, maximumDepth, backpressurePauses;
    private double stepMilliseconds, readMilliseconds, discarded;
    private bool running, sleeping, closing, previousJump, previousCancel, backpressureWaiting, resetClock, resetSleep, sleepEnabled;
    internal Action? BeforeSleepWait { get; set; }

    public BeltBodyRuntime(string modulePath) : this(body => new BeltNativeSession(modulePath, body)) { }

    internal BeltBodyRuntime(Func<DangoBodyControls, IBeltBodySession> factory, bool manualScheduling = false,
        Func<long>? timestamp = null, long? frequency = null)
    {
        this.factory = factory; this.manualScheduling = manualScheduling;
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp; this.frequency = frequency ?? Stopwatch.Frequency;
        if (this.frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        owner = new Thread(Run) { Name = "Dango continuous belt native owner", IsBackground = true };
        owner.Start();
    }

    public long Epoch { get { lock (gate) return epoch; } }
    public BeltBodySnapshot? LastFailure { get { lock (gate) return lastFailure; } }
    public bool SleepEnabled
    {
        get { lock (gate) return sleepEnabled; }
        set { lock (gate) { ThrowIfClosing(); if (sleepEnabled == value) return; sleepEnabled = value; SignalLocked(); } }
    }
    public BeltBodyMetrics Metrics
    {
        get
        {
            lock (gate)
                return new(epoch, bodyEpoch, produced, displayed, snapshots.Count, maximumDepth, invalidated,
                    running, sleeping, backpressureWaiting, backpressurePauses, wakeSequence, stepCalls,
                    stepMilliseconds, readMilliseconds, discarded, fault, configurationRevision,
                    snapshots.TryPeek(out var oldest) ? Stopwatch.GetElapsedTime(oldest.PublishedTimestamp).TotalMilliseconds : 0);
        }
    }

    /// <summary>Coalesces only unstarted same-body-epoch requests. A hot edit preserves every queued tick.</summary>
    public Task ConfigureAsync(DangoBodyControls body, long revision, long bodyEpoch)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (revision < 0 || bodyEpoch < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        lock (gate)
        {
            ThrowIfClosing();
            if (bodyEpoch < this.bodyEpoch || bodyEpoch == this.bodyEpoch && revision < requestedRevision)
                return Task.FromException(new OperationCanceledException("Stale body configuration."));
            var lifecycleChange = bodyEpoch != this.bodyEpoch;
            if (lifecycleChange)
            {
                InvalidateLocked(); this.bodyEpoch = bodyEpoch; requestedRevision = -1;
            }
            requestedRevision = revision;
            pending?.Completion.TrySetException(new OperationCanceledException("Body configuration superseded."));
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = new(body, revision, bodyEpoch, epoch, lifecycleChange || pending?.LifecycleChange == true, completion);
            SignalLocked();
            return completion.Task;
        }
    }

    public Task<long> PauseAsync()
    {
        lock (gate)
        {
            ThrowIfClosing(); InvalidateLocked(); var current = epoch;
            // Pending configuration remains associated with its new lifecycle, so hide does not
            // strand a requested restore. The barrier still invalidates its old presentation.
            if (pending is { } request) pending = request with { Epoch = current };
            return CommandLocked(() => current);
        }
    }

    public Task<bool> ResumeAsync()
    {
        lock (gate)
        {
            ThrowIfClosing(); var requested = epoch;
            return CommandLocked(() =>
            {
                lock (gate)
                {
                    if (epoch != requested || session is null || fault is not null) return false;
                    running = true; resetClock = true; SignalLocked(); return true;
                }
            });
        }
    }

    public void PublishInput(DangoBeltInput input) => PublishInput(Epoch, input);
    public void PublishInput(long inputEpoch, DangoBeltInput input)
    {
        lock (gate)
        {
            if (closing || inputEpoch != epoch) return;
            if (input == latest) return;
            if (input.JumpPressed && !previousJump) jumpSequence++;
            if (input.CancelForces && !previousCancel)
            {
                consumedJump = jumpSequence; cancelSequence++;
            }
            previousJump = input.JumpPressed; previousCancel = input.CancelForces;
            latest = input; SignalLocked();
        }
    }
    public void ClearInput()
    {
        lock (gate)
        {
            if (closing) return;
            ClearInputLocked(); cancelSequence++; SignalLocked();
        }
    }

    public Task SetSolidWallsAsync(bool enabled)
    {
        lock (gate)
        {
            ThrowIfClosing();
            return CommandLocked(() =>
            {
                session?.SetSolidWalls(enabled); solidWalls = enabled;
                lock (gate) SignalLocked();
                return true;
            });
        }
    }

    public Task SetBoundsAsync(double left, double right)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right) || left >= right) throw new ArgumentOutOfRangeException(nameof(left));
        lock (gate)
        {
            ThrowIfClosing(); SignalLocked();
            return CommandLocked(() =>
            {
                session?.SetBounds(left, right);
                requestedBounds = (left, right);
                lock (gate) SignalLocked();
                return true;
            });
        }
    }

    /// <summary>Recreates only this owner's transient native pose; parameters and persistence are unchanged.</summary>
    public Task ResetPoseAsync()
    {
        lock (gate)
        {
            ThrowIfClosing();
            var resume = running;
            InvalidateLocked();
            var requestedEpoch = epoch;
            if (pending is { } request) pending = request with { Epoch = requestedEpoch };
            return CommandLocked(() =>
            {
                lock (gate)
                    if (closing || epoch != requestedEpoch) return false;
                session?.Dispose();
                session = null;
                var created = factory(body);
                try
                {
                    if (solidWalls) created.SetSolidWalls(true);
                    if (requestedBounds is { } bounds) created.SetBounds(bounds.Left, bounds.Right);
                    session = created;
                }
                catch { created.Dispose(); throw; }
                lock (gate)
                {
                    fault = null; lastFailure = null;
                    sleep.Reset(); resetClock = true;
                    SignalLocked();
                }
                // A paused reset still needs one genuine, measurable initial frame. This is
                // performed by the existing native owner, never by capture or a second loop.
                var sampled = Produce();
                lock (gate)
                {
                    if (epoch == requestedEpoch && !closing) running = resume && fault is null;
                    if (!sampled && fault is { } error) throw error;
                }
                return true;
            });
        }
    }

    public bool TryDequeue(out BeltBodySnapshot? snapshot)
    {
        lock (gate)
        {
            CheckBackpressureLocked();
            if (snapshots.TryDequeue(out snapshot))
            {
                if (backpressureWaiting) { backpressureWaiting = false; resetClock = true; }
                if (!closing) wake.Set(); return true;
            }
            snapshot = terminal; terminal = null; return snapshot is not null;
        }
    }
    public bool MarkDisplayed(BeltBodySnapshot snapshot)
    {
        lock (gate)
        {
            if (closing || snapshot.Error is not null || snapshot.Epoch != epoch || snapshot.BodyEpoch != bodyEpoch ||
                snapshot.Sequence <= displayedSequence || snapshot.Sequence > sequence) return false;
            displayedSequence = snapshot.Sequence; displayed++; return true;
        }
    }
    public Task CloseAsync()
    {
        lock (gate)
        {
            if (closing) return stopped.Task;
            InvalidateLocked(); closing = true;
            pending?.Completion.TrySetException(new ObjectDisposedException(nameof(BeltBodyRuntime))); pending = null;
            wake.Set(); return stopped.Task;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAsync());

    private Task<T> CommandLocked<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue(() =>
        {
            try { lock (gate) ThrowIfClosing(); completion.TrySetResult(action()); }
            catch (Exception error) { completion.TrySetException(error); }
        });
        wake.Set(); return completion.Task;
    }
    private void ThrowIfClosing() => ObjectDisposedException.ThrowIf(closing, this);
    private void ClearInputLocked()
    {
        latest = default; previousJump = previousCancel = false;
        consumedJump = jumpSequence; consumedCancel = cancelSequence;
    }
    private void InvalidateLocked()
    {
        epoch++; running = false; sleeping = false; backpressureWaiting = false;
        invalidated += snapshots.Count; snapshots.Clear(); terminal = null;
        ClearInputLocked(); SignalLocked();
    }
    private void SignalLocked()
    {
        wakeSequence++; sleeping = false; resetClock = resetSleep = true; wake.Set();
    }

    // Configuration processing and all session operations execute only on owner.
    private void ApplyConfiguration()
    {
        ConfigurationRequest request;
        lock (gate)
        {
            if (pending is null || closing || snapshots.Count >= Capacity) return;
            request = pending; pending = null;
        }
        try
        {
            if (session is null)
            {
                var created = factory(request.Body);
                try
                {
                    if (solidWalls) created.SetSolidWalls(true);
                    if (requestedBounds is { } bounds) created.SetBounds(bounds.Left, bounds.Right);
                    session = created;
                }
                catch { created.Dispose(); throw; }
            }
            else session.Reconfigure(request.Body);
            lock (gate)
            {
                // Even an invalidated in-flight reconfigure really happened. Record its physical
                // parameters; only the latest live epoch can acknowledge or publish it.
                body = request.Body; configurationRevision = request.Revision;
                sleep.Reset(); resetClock = true;
                if (request.Epoch != epoch || request.BodyEpoch != bodyEpoch || closing)
                    request.Completion.TrySetException(new OperationCanceledException("Configuration crossed a lifecycle barrier."));
                else
                {
                    if (request.LifecycleChange) fault = null;
                    request.Completion.TrySetResult();
                }
            }
        }
        catch (Exception error) { request.Completion.TrySetException(error); }
    }

    private bool Produce()
    {
        long currentEpoch, currentBodyEpoch, revision, eventSequence;
        long previousSequence; int previousTick;
        DangoBodyControls currentBody; DangoBeltInput input;
        lock (gate)
        {
            if (closing || session is null || fault is not null || sleeping) return false;
            if (resetSleep) { sleep.Reset(); resetSleep = false; }
            if (snapshots.Count >= Capacity) { BeginBackpressureLocked(); CheckBackpressureLocked(); return false; }
            currentEpoch = epoch; currentBodyEpoch = bodyEpoch; revision = configurationRevision;
            currentBody = body; eventSequence = wakeSequence;
            previousTick = tick; previousSequence = sequence;
            var jump = consumedJump < jumpSequence; if (jump) consumedJump++;
            var cancel = consumedCancel < cancelSequence; if (cancel) consumedCancel++;
            input = latest with { JumpPressed = jump, CancelForces = cancel || latest.CancelForces };
        }
        BeltNativeStep result;
        try { result = session.Step(input); }
        catch (Exception error) { result = new(null, null, null, null, null, null, null, null, null, error, 0, 0); }
        lock (gate)
        {
            stepCalls++; stepMilliseconds += result.StepMilliseconds; readMilliseconds += result.ReadMilliseconds;
            if (currentEpoch != epoch || closing)
            {
                invalidated++;
                // A lifecycle barrier may reject presentation, never the evidence of an
                // in-flight native failure. Keep the old epoch without poisoning the new one.
                if (result.Error is not null)
                    lastFailure = new(currentEpoch, currentBodyEpoch, revision, previousSequence, previousTick + 1,
                        currentBody, input, result.Frame, result.State, result.Error, result.StepMilliseconds,
                        result.ReadMilliseconds, Stopwatch.GetTimestamp())
                    { Guard = result.Guard, Speed = result.Speed, Radial = result.Radial, Rear = result.Rear,
                        Ledger = result.Ledger, PreState = result.PreState, SolverFailure = result.SolverFailure };
                return false;
            }
            var successful = result.Error is null && result.Frame is not null && result.State is not null;
            if (successful) { tick++; produced++; sequence++; }
            var asleep = successful && sleepEnabled && eventSequence == wakeSequence && pending is null &&
                !BeltSleepPolicy.Active(latest) && consumedJump == jumpSequence && consumedCancel == cancelSequence &&
                sleep.Observe(result.Frame!, result.State!, input, true);
            if (!sleepEnabled || eventSequence != wakeSequence || pending is not null) sleep.Reset();
            var snapshot = new BeltBodySnapshot(currentEpoch, currentBodyEpoch, revision, sequence, tick,
                currentBody, input, result.Frame, result.State, result.Error, result.StepMilliseconds,
                result.ReadMilliseconds, Stopwatch.GetTimestamp(), asleep)
            { Guard = result.Guard, Speed = result.Speed, Radial = result.Radial, Rear = result.Rear,
                Ledger = result.Ledger, PreState = result.PreState, SolverFailure = result.SolverFailure };
            if (!successful)
            {
                fault = result.Error ?? new InvalidOperationException("Native tick returned incomplete state.");
                terminal = lastFailure = snapshot with { Error = fault }; running = sleeping = false; sleep.Reset(); return false;
            }
            snapshots.Enqueue(snapshot); maximumDepth = Math.Max(maximumDepth, snapshots.Count);
            sleeping = asleep;
            if (!sleeping && snapshots.Count >= Capacity) BeginBackpressureLocked();
            return true;
        }
    }

    private void BeginBackpressureLocked()
    {
        if (backpressureWaiting) return;
        fullSince = timestamp(); backpressureWaiting = true; resetClock = true;
    }
    private double FullSeconds => Math.Max(0, (timestamp() - fullSince) / (double)frequency);
    private void CheckBackpressureLocked()
    {
        if (!backpressureWaiting || FullSeconds < BackpressureTimeoutSeconds || fault is not null) return;
        fault = new InvalidOperationException("身体显示持续回压，物理已暂停；请恢复预览。");
        backpressureWaiting = false; running = sleeping = false; backpressurePauses++;
        terminal = lastFailure = new(epoch, bodyEpoch, configurationRevision, sequence, tick, body, latest, null, null,
            fault, 0, 0, Stopwatch.GetTimestamp());
    }

    internal Task<bool> AdvanceAsync()
    {
        lock (gate) { ThrowIfClosing(); return CommandLocked(() => { ApplyConfiguration(); return Produce(); }); }
    }
    internal Task OwnerBarrierAsync()
    {
        lock (gate) { ThrowIfClosing(); return CommandLocked(() => true); }
    }
    internal Task<(DangoBeltFrame Frame, double[] State)> CaptureStateAsync()
    {
        lock (gate) { ThrowIfClosing(); return CommandLocked(() => session!.CaptureState()); }
    }

    private void Run()
    {
        var previous = Stopwatch.GetTimestamp(); double accumulator = 0;
        Exception? finalError = null;
        try
        {
            while (true)
            {
                while (true)
                {
                    Action? command; lock (gate) command = commands.TryDequeue(out var next) ? next : null;
                    if (command is null) break;
                    command();
                }
                lock (gate) if (closing) break;
                ApplyConfiguration();
                bool active, asleep; int pressureWait = -1;
                lock (gate)
                {
                    CheckBackpressureLocked(); active = running && !manualScheduling; asleep = sleeping;
                    if (backpressureWaiting) pressureWait = Math.Max(1, (int)Math.Ceiling((BackpressureTimeoutSeconds - FullSeconds) * 1000));
                    if (resetClock) { previous = Stopwatch.GetTimestamp(); accumulator = 0; resetClock = false; }
                }
                if (!active || asleep)
                {
                    if (asleep) BeforeSleepWait?.Invoke();
                    // No sleep timeout: producer input/config/lifecycle events are retained by
                    // AutoResetEvent even if delivered between the decision and this WaitOne.
                    wake.WaitOne(); previous = Stopwatch.GetTimestamp(); accumulator = 0; continue;
                }
                if (pressureWait >= 0) { wake.WaitOne(pressureWait); previous = Stopwatch.GetTimestamp(); accumulator = 0; continue; }
                var now = Stopwatch.GetTimestamp(); accumulator += Math.Min(Stopwatch.GetElapsedTime(previous, now).TotalSeconds, .25); previous = now;
                var count = 0;
                while (accumulator >= DangoBeltWorld.FixedDt && count++ < 4)
                {
                    lock (gate) if (!running || commands.Count != 0 || pending is not null || closing || sleeping) break;
                    if (!Produce()) break;
                    accumulator -= DangoBeltWorld.FixedDt;
                    lock (gate) if (backpressureWaiting || resetClock) break;
                }
                lock (gate)
                {
                    if (resetClock || backpressureWaiting || sleeping) { accumulator = 0; previous = Stopwatch.GetTimestamp(); continue; }
                    if (accumulator > 4 * DangoBeltWorld.FixedDt) { discarded += accumulator; accumulator = 0; }
                }
                var remaining = DangoBeltWorld.FixedDt - accumulator - Stopwatch.GetElapsedTime(previous).TotalSeconds;
                wake.WaitOne(remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000));
            }
        }
        catch (Exception error) { finalError = error; }
        finally
        {
            try { session?.Dispose(); } catch (Exception error) { finalError = finalError is null ? error : new AggregateException(finalError, error); }
            lock (gate)
            {
                closing = true; running = sleeping = false;
                pending?.Completion.TrySetException(finalError ?? new ObjectDisposedException(nameof(BeltBodyRuntime))); pending = null;
                while (commands.TryDequeue(out var command)) command();
                wake.Dispose();
            }
            if (finalError is null) stopped.TrySetResult(); else stopped.TrySetException(finalError);
        }
    }

    private sealed record ConfigurationRequest(DangoBodyControls Body, long Revision, long BodyEpoch,
        long Epoch, bool LifecycleChange, TaskCompletionSource Completion);
}
