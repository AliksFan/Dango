using System.Diagnostics;

namespace Dango.PhysicsLab;

internal sealed partial class LabBeltWindow
{
    private long workerJumpSequence, workerCancelSequence, workerInteractSequence;
    private double workerMaximumQueueAgeMs;
    private int workerAppliedTicks;
    private int workerReplayStopTick;
    private bool workerManualStepping;
    private int workerRenderConsumers, workerManualConsumers;
    private bool workerPresentationPrimed;
    private long workerPresentationEpoch = -1;
    private int workerPresentationPrimeWaits, workerPresentationUnderflows, workerPresentationCatchups, workerPresentationFlushes;
    private readonly List<WorkerPresentationSample> workerPresentationSamples = [];
    private readonly Queue<object> workerLifecycle = new();

    private sealed record WorkerPresentationSample(double ElapsedMilliseconds, long Epoch, int QueueBefore,
        double OldestAgeBeforeMilliseconds, bool PrimedBefore, bool PrimedAfter, int ApplyBudget,
        bool Flush, int AppliedTicks, int QueueAfter, double OldestAgeAfterMilliseconds, bool ProducerRunning);

    private void ResetWorkerPresentation(bool clearMetrics = false)
    {
        workerPresentationPrimed = false;
        workerPresentationEpoch = physicsWorker?.Epoch ?? -1;
        if (!clearMetrics) return;
        workerPresentationPrimeWaits = workerPresentationUnderflows = workerPresentationCatchups = workerPresentationFlushes = 0;
        workerPresentationSamples.Clear();
    }

    private object WorkerPresentationState() => new
    {
        policy = "Prime with two complete snapshots; normally apply one per Rendering; catch up two at depth>=4; flush stopped/faulted producer tail. No interpolation or skipped appearance ticks.",
        workerPresentationPrimed, workerPresentationEpoch, workerPresentationPrimeWaits, workerPresentationUnderflows,
        workerPresentationCatchups, workerPresentationFlushes, samples = workerPresentationSamples,
    };

    private void RecordWorkerLifecycle(string reason)
    {
        if (!usePhysicsWorker) return;
        workerLifecycle.Enqueue(new { reason, timestamp = Stopwatch.GetTimestamp(), steps,
            manual = workerManualStepping, workerClockRunning, resetting, closingWorker, resetRequest,
            active = IsActive, visible = IsVisible, windowState = WindowState.ToString(),
            metrics = physicsWorker?.Metrics, renderConsumers = workerRenderConsumers, manualConsumers = workerManualConsumers });
        while (workerLifecycle.Count > 64) workerLifecycle.Dequeue();
    }
    private object WorkerRuntimeState() => new { worker = physicsWorker?.Metrics, workerRenderConsumers,
        workerManualConsumers, workerManualStepping, workerClockRunning, resetting, resetRequest,
        presentation = WorkerPresentationState(),
        timestampFrequency = Stopwatch.Frequency, lifecycle = workerLifecycle.ToArray() };

    private LabBeltInput ActualInput(LabInputSnapshot sample) => new(Horizontal: sample.Horizontal,
        Stretch: sample.Stretch, Downward: sample.Downward, JumpPressed: sample.JumpPressed,
        InputActive: sample.InputActive, CancelForces: sample.CancelForces,
        DragActive: dragging, DragTargetX: dragX, DragTargetY: dragY);

    private void PublishWorkerInput()
    {
        if (physicsWorker is null || !ActiveBelt || resetting || closingWorker || performanceRunning || workerManualStepping) return;
        var sample = input.Consume();
        if (sample.JumpPressed) workerJumpSequence++;
        if (sample.InteractPressed) workerInteractSequence++;
        physicsWorker.PublishInput(physicsWorker.Epoch, sample, ActualInput(sample),
            workerJumpSequence, workerCancelSequence, workerInteractSequence);
    }

    private void TickWorker()
    {
        // A queued Rendering callback can outlive unsubscription. The manual
        // harness is the only FIFO consumer for its entire initialized lifetime.
        if (workerManualStepping || physicsWorker is null || resetting || closingWorker) return;
        if (paused || fault || (!IsActive && !performanceRunning) || !IsVisible || WindowState == System.Windows.WindowState.Minimized)
        {
            if (workerClockRunning) { _ = physicsWorker.PauseAsync(); workerClockRunning = false; }
            ResetWorkerPresentation();
            return;
        }
        appearance.Paused = false;
        PublishWorkerInput();
        if (!workerClockRunning)
        {
            workerClockRunning = true;
            if (replaying) workerReplayStopTick = physicsWorker.Metrics.ProducedTicks + PerformanceReplaySteps;
            RecordWorkerLifecycle("render-clock-resume");
            _ = physicsWorker.ResumeAsync(replaying);
        }
        var before = physicsWorker.Metrics;
        if (workerPresentationEpoch != before.Epoch) ResetWorkerPresentation();
        var primedBefore = workerPresentationPrimed;
        var decision = LabBeltPresentationPacing.Decide(primedBefore, before.QueueDepth, before.Running, before.TerminalPending);
        workerPresentationPrimed = decision.Primed;
        if (!decision.Primed && before.Running) workerPresentationPrimeWaits++;
        if (decision.Primed && !decision.Flush && before.QueueDepth == 0) workerPresentationUnderflows++;
        if (!decision.Flush && decision.ApplyBudget == 2) workerPresentationCatchups++;
        if (decision.Flush && decision.ApplyBudget > 0) workerPresentationFlushes++;
        var appliedThisCallback = 0;
        for (var consumed = 0; consumed < decision.ApplyBudget && physicsWorker.TryDequeue(out var snapshot); consumed++)
        {
            if (snapshot!.Epoch != physicsWorker.Epoch) continue;
            var age = Stopwatch.GetElapsedTime(snapshot.PublishedTimestamp).TotalMilliseconds;
            workerMaximumQueueAgeMs = Math.Max(workerMaximumQueueAgeMs, age);
            performance?.Add("worker-queue-age", age);
            workerRenderConsumers++;
            if (workerManualStepping) RecordWorkerLifecycle("render-consumed-manual-harness-fifo");
            applyingWorkerSnapshot = snapshot;
            StepOnce();
            if (!fault)
            {
                physicsWorker.MarkApplied(snapshot.Epoch); workerAppliedTicks++; appliedThisCallback++;
                PerformanceStepCompleted();
            }
            if (fault || performanceFinishing) break;
        }
        discardedSeconds = physicsWorker.Metrics.DiscardedSeconds;
        var metrics = physicsWorker.Metrics;
        if (performanceRunning)
            workerPresentationSamples.Add(new(Stopwatch.GetElapsedTime(performanceStarted).TotalMilliseconds,
                before.Epoch, before.QueueDepth, before.OldestSnapshotAgeMilliseconds, primedBefore,
                workerPresentationPrimed, decision.ApplyBudget, decision.Flush, appliedThisCallback,
                metrics.QueueDepth, metrics.OldestSnapshotAgeMilliseconds, metrics.Running));
        if (replaying && !performanceRunning && !fault && metrics.ProducedTicks >= workerReplayStopTick &&
            metrics.QueueDepth == 0 && !metrics.Running)
        {
            replaying = false; paused = true; appearance.Paused = true;
            status.Text = "回放完成。可切换模型重复同输入。";
        }
        surface.Refresh();
    }

    private async Task StepOnceAsync()
    {
        if (physicsWorker is null || !ActiveBelt) { StepOnce(); return; }
        var sample = input.Consume();
        if (sample.JumpPressed) workerJumpSequence++;
        if (sample.InteractPressed) workerInteractSequence++;
        var epoch = physicsWorker.Epoch;
        physicsWorker.PublishInput(epoch, sample, ActualInput(sample), workerJumpSequence, workerCancelSequence, workerInteractSequence);
        var before = physicsWorker.Metrics;
        if (workerManualStepping) RecordWorkerLifecycle("manual-advance-request");
        var advanced = await physicsWorker.AdvanceAsync();
        if (workerManualStepping) RecordWorkerLifecycle("manual-advance-resumed");
        if (epoch != physicsWorker.Epoch) throw new OperationCanceledException("Manual tick superseded by a lifecycle change.");
        if (!physicsWorker.TryDequeue(out var snapshot))
        {
            RecordWorkerLifecycle("manual-fifo-missing");
            throw new InvalidOperationException($"Worker tick did not publish its complete snapshot. advanced={advanced}; epoch={epoch}; before={before}; after={physicsWorker.Metrics}; renderConsumers={workerRenderConsumers}; manualConsumers={workerManualConsumers}.");
        }
        workerManualConsumers++;
        applyingWorkerSnapshot = snapshot;
        StepOnce();
        if (!fault) { physicsWorker.MarkApplied(epoch); workerAppliedTicks++; }
    }

    private async Task<double[]> ReadWorkerDoublesAsync(string channel)
    {
        var state = await physicsWorker!.ReadStateAsync();
        var result = new double[state[channel].Length / sizeof(double)];
        Buffer.BlockCopy(state[channel], 0, result, 0, state[channel].Length); return result;
    }
    private Task<double[]> ReadDebugStateAsync() => physicsWorker is not null && ActiveBelt
        ? ReadWorkerDoublesAsync("debug") : Task.FromResult(belt!.ReadDebugState());
    private Task<double[]> ReadForceLedgerAsync() => physicsWorker is not null && ActiveBelt
        ? ReadWorkerDoublesAsync("ledger") : Task.FromResult(belt!.ReadForceLedger());
    private Task<double[]> ReadRearDriveAsync() => physicsWorker is not null && ActiveBelt
        ? ReadWorkerDoublesAsync("rear") : Task.FromResult(belt!.ReadRearDrive());
}
