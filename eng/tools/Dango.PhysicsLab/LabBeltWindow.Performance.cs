using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dango.PhysicsLab;

internal sealed partial class LabBeltWindow
{
    private const int PerformanceReplaySteps = 1860;
    private bool performanceRunning, performanceFinishing, performanceOverlay;
    private long performanceStarted, performanceAllocated;
    private int[] performanceCollections = [];
    private int performanceInactiveObservations, performanceRenderStep;
    private int performanceStartStep, performanceDeactivations, performanceInactiveRenderFrames;
    private int performanceNonVisibleObservations;
    private int performanceRightSteps, performanceLeftSteps, performanceUnexpectedInputSteps;
    private TimeSpan? performanceLastRenderingTime;
    private long performanceLastRenderingTimestamp;
    private readonly List<LabWindowCadenceFrame> performanceFrames = [];
    private readonly DispatcherTimer performanceWatchdog = new(DispatcherPriority.Normal);

    // Uses the selected actual-window clock, original catch-up policy and walk/stop replay.
    internal void StartWindowPerformance(bool overlayVisible)
    {
        StopPhysicsClock();
        WindowState = WindowState.Normal;
        size.Value = 120; softness.Value = 100; build.Value = 0;
        model.SelectedIndex = 0; radialDrive.IsChecked = true;
        showAppearance.IsChecked = showGait.IsChecked = true;
        showSkeleton.IsChecked = showForces.IsChecked = true; showReference.IsChecked = false;
        Reset();
        if (fault) throw new InvalidOperationException("Performance fixture failed to initialize: " + failureReason);
        surface.SetPerformanceOverlayVisible(overlayVisible);
        surface.Refresh();
        // This explicit benchmark keeps its scripted playback running when the
        // user switches to Codex. Ordinary interactive windows still pause.
        Topmost = true;
        Activate();
        if (Content is UIElement content) content.IsHitTestVisible = false;
        performanceOverlay = overlayVisible;
        performance = new LabWindowPerformance();
        appearance.PerformanceSample = performance.Add;
        performanceRunning = true;
        performanceStartStep = steps;
        performanceRenderStep = steps;
        performanceStarted = Stopwatch.GetTimestamp();
        performanceAllocated = GC.GetAllocatedBytesForCurrentThread();
        performanceCollections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        Deactivated += (_, _) => { if (performanceRunning) performanceDeactivations++; };
        StateChanged += (_, _) => { if (performanceRunning && WindowState == WindowState.Minimized) performanceNonVisibleObservations++; };
        Closing += (_, e) =>
        {
            if (!performanceRunning) return;
            e.Cancel = true;
            if (performanceFinishing) return;
            performanceFinishing = true;
            StopPhysicsClock();
            Dispatcher.BeginInvoke(new Action(() => FinishWindowPerformance("Measurement window closed before replay completed.")), DispatcherPriority.Normal);
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= PerformanceRendering;
            performanceWatchdog.Stop();
        };
        replaying = true; replayFrame = 0;
        previousTime = clock.Elapsed.TotalSeconds; accumulator = discardedSeconds = 0;
        performanceWatchdog.Interval = TimeSpan.FromMilliseconds(250);
        performanceWatchdog.Tick += (_, _) =>
        {
            if (!IsActive || !IsVisible || WindowState == WindowState.Minimized) performanceInactiveObservations++;
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                performanceNonVisibleObservations++;
                FinishWindowPerformance($"Window became unavailable: visible={IsVisible}, state={WindowState}.");
                return;
            }
            if (fault || Stopwatch.GetElapsedTime(performanceStarted).TotalSeconds > 150)
                FinishWindowPerformance(fault ? failureReason : "Wall-clock timeout; active-window measurement incomplete.");
        };
        performanceWatchdog.Start();
        StartPhysicsClock();
        CompositionTarget.Rendering += PerformanceRendering;
        Title = overlayVisible ? "团子·窗口性能测量·诊断图层开启" : "团子·窗口性能测量·诊断图层隐藏";
    }

    private void PerformanceStepCompleted()
    {
        if (performanceRunning)
        {
            if (lastInput.Horizontal == 1) performanceRightSteps++;
            if (lastInput.Horizontal == -1) performanceLeftSteps++;
            if (lastInput.Stretch != 0 || lastInput.Downward != 0 || lastInput.JumpPressed || dragging)
                performanceUnexpectedInputSteps++;
        }
        if (!performanceRunning || performanceFinishing || steps - performanceStartStep < PerformanceReplaySteps) return;
        performanceFinishing = true;
        StopPhysicsClock();
        // Let the current callback finish its existing catch-up loop and render work.
        Dispatcher.BeginInvoke(new Action(() => FinishWindowPerformance(null)), DispatcherPriority.Normal);
    }

    private void PerformanceRendering(object? sender, EventArgs args)
    {
        if (!performanceRunning || args is not RenderingEventArgs rendering ||
            performanceLastRenderingTime == rendering.RenderingTime) return;
        var now = useRenderClock && lastClockRenderingTime == rendering.RenderingTime
            ? renderClockCallbackTimestamp : Stopwatch.GetTimestamp();
        if (!IsActive) performanceInactiveRenderFrames++;
        if (performanceLastRenderingTimestamp != 0)
        {
            var milliseconds = Stopwatch.GetElapsedTime(performanceLastRenderingTimestamp, now).TotalMilliseconds;
            performance!.Add("rendering-callback-interval", milliseconds);
            performanceFrames.Add(new(Stopwatch.GetElapsedTime(performanceStarted, now).TotalMilliseconds,
                milliseconds, steps, steps - performanceRenderStep, IsActive, discardedSeconds));
        }
        performanceLastRenderingTime = rendering.RenderingTime;
        performanceLastRenderingTimestamp = now;
        performanceRenderStep = steps;
    }

    private void FinishWindowPerformance(string? error)
    {
        if (!performanceRunning) return;
        performanceRunning = false;
        StopPhysicsClock(); performanceWatchdog.Stop();
        CompositionTarget.Rendering -= PerformanceRendering;
        appearance.PerformanceSample = null;
        var wall = Stopwatch.GetElapsedTime(performanceStarted).TotalSeconds;
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - performanceAllocated;
        var collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - performanceCollections[i]).ToArray();
        var intervals = performance!.Samples.GetValueOrDefault("rendering-callback-interval") ?? [];
        var completedSteps = steps - performanceStartStep;
        var steady57 = LabWindowCadence.Summarize(performanceFrames, performanceStartStep);
        var passed = error is null && !fault && completedSteps >= PerformanceReplaySteps &&
            completedSteps <= PerformanceReplaySteps + 3 && intervals.Count >= 200 && performanceNonVisibleObservations == 0 &&
            performanceRightSteps == 600 && performanceLeftSteps == 600 && performanceUnexpectedInputSteps == 0 &&
            IsVisible && WindowState != WindowState.Minimized;
        File.WriteAllText(Path.Combine(session, "window-performance.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, PerformanceTargetPassed = passed && steady57.Passed,
            PassedMeaning = "Measurement completed and remained valid; performance acceptance additionally requires PerformanceTargetPassed.",
            Error = error, PhysicsFault = fault, FailureReason = failureReason,
            AppSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LabBeltWindow).Assembly.Location))),
            PresenterAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(appearance.GetType().Assembly.Location))),
            Controls = new { Size = size.Value, Softness = softness.Value, LandingBuild = build.Value },
            Script = "existing1860: rest300/right600/stop180/left600/stop180; selected clock and original up-to-four catch-up steps",
            RequestedSteps = PerformanceReplaySteps, CompletedSteps = completedSteps, StartStep = performanceStartStep, FinalStep = steps, WallSeconds = wall,
            SimulatedSeconds = completedSteps * LabWindow.FixedDt, DiscardedSeconds = discardedSeconds,
            InactiveObservations = performanceInactiveObservations, Deactivations = performanceDeactivations,
            InactiveRenderFrames = performanceInactiveRenderFrames, RenderCallbacks = intervals.Count,
            NonVisibleObservations = performanceNonVisibleObservations,
            FinalWindowVisible = IsVisible, FinalWindowState = WindowState.ToString(),
            ClockMode = useRenderClock ? "rendering-callback-fixed-physics" : "dispatcher-timer-8ms",
            Steady57 = steady57,
            ScriptedRightSteps = performanceRightSteps, ScriptedLeftSteps = performanceLeftSteps,
            UnexpectedInputSteps = performanceUnexpectedInputSteps,
            Playback = "Explicit benchmark only: visible/topmost scripted replay continues across activation changes; ordinary interactive pause/input behavior unchanged.",
            DebugOverlayVisible = performanceOverlay, SidebarDiagnosticsCollectedAndVisible = true,
            NativeParametersAndGaitUnchanged = true,
            Timing = "Inclusive CPU wall-clock buckets. Rendering callbacks are pre-render cadence, not GPU/DWM present or scanout. No per-sample I/O. No warmup samples removed.",
            FrameBudgetMilliseconds = 1000d / 60,
            CallbackIntervalsOver16_667Ms = intervals.Count(x => x > 1000d / 60),
            CallbackIntervalsOver25Ms = intervals.Count(x => x > 25),
            CallbackIntervalsOver33_333Ms = intervals.Count(x => x > 1000d / 30),
            UiThreadAllocatedBytes = allocatedBytes,
            ProcessGarbageCollections = collections,
            Buckets = performance.Report(), Samples = performance.Samples, Frames = performanceFrames,
        }, new JsonSerializerOptions { WriteIndented = true }));
        ((Application)Application.Current).Shutdown(passed ? 0 : 10);
    }

}
