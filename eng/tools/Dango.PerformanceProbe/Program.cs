using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Dango.ProbeDiagnostics;

namespace Dango.PerformanceProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var memoryDiagnostic = args.Length == 4 && args[3] == "--memory-diagnostic";
        if ((args.Length != 3 && !memoryDiagnostic) || !int.TryParse(args[1], out var seconds) || seconds < 10 ||
            !Enum.TryParse<DesktopDisplayMode>(args[2], out var mode) || !Enum.IsDefined(mode)) return 2;
        var output = Path.GetFullPath(args[0]);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var protectedRoot in new[] { Path.Combine(local, "Dango"), Path.Combine(local, "Programs", "Dango") })
        {
            if (output.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 4;
        }
        if (Directory.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Probe? probe = null;
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new
            { utc = DateTimeOffset.UtcNow, kind = e.Exception.GetType().Name }));
            e.Handled = true;
            app.Shutdown(10);
        };
        app.Startup += (_, _) =>
        {
            probe = new Probe(app, output, seconds, mode, memoryDiagnostic);
            probe.Start();
        };
        try { return app.Run(); }
        finally { probe?.Dispose(); }
    }
}

// This process owns only its own host and fictional settings. Reflection is read-only,
// except for constructing/starting/disposing the existing internal application host.
internal sealed class Probe : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions MemoryJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly System.Windows.Application app;
    private readonly string output;
    private readonly int observationSeconds;
    private readonly DesktopDisplayMode mode;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Process process = Process.GetCurrentProcess();
    private readonly DispatcherTimer sampleTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly StreamWriter samples;
    private readonly StreamWriter events;
    private readonly List<double> intervals = [];
    private readonly Dictionary<string, int> stateTicks = [];
    private readonly List<DispatcherTimer> motionTimers = [];
    private readonly List<SchedulerObserver> schedulers = [];
    private readonly List<double> callbackIntervals = [], handlerDurations = [];
    private readonly MemoryDiagnostic? memoryDiagnostic;
    private SchedulerSnapshot previousSample = SchedulerSnapshot.Zero;
    private long lastAppliedTimestamp, lastCallbackTimestamp;
    private int schema = 1;
    private object? host;
    private DesktopHostCoordinator? coordinator;
    private StackedDangoSceneRuntime? runtime;
    private object? doze;
    private object? wander;
    private System.Threading.Timer? watchdog;
    private double lastFrameTime = double.NaN, lastSampleTime, lastCpu, phaseStarted;
    private string phase = "warmup", frameState = "idle";
    private bool hostDisposed, disposed;
    private int cycle, motionTicks;
    private long tickTotal, lastHeartbeat;
    private DateTimeOffset started;
    private double? firstSleep;

    internal Probe(System.Windows.Application app, string output, int seconds, DesktopDisplayMode mode, bool diagnoseMemory)
    {
        this.app = app; this.output = output; observationSeconds = seconds; this.mode = mode;
        if (diagnoseMemory) memoryDiagnostic = new();
        samples = new(Path.Combine(output, diagnoseMemory ? "memory-samples.jsonl" : "samples.jsonl")) { AutoFlush = true };
        events = new(Path.Combine(output, "events.jsonl")) { AutoFlush = true };
        sampleTimer.Tick += Sample;
    }

    internal void Start()
    {
        started = DateTimeOffset.UtcNow;
        var settingsFile = Path.Combine(output, "fictional-settings.json");
        new ControlCenterWindowSettingsStore().Save(settingsFile,
            ControlCenterWindowSettings.CreateDefault() with { IntroSeen = true, DisplayMode = mode });
        var type = typeof(StackedDangoSceneRuntime).Assembly.GetType("Dango.Desktop.AppShell.DangoApplicationHost", true)!;
        host = Activator.CreateInstance(type, Flags, null,
            [false, false, settingsFile, Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"), null], null)!;
        coordinator = Field<DesktopHostCoordinator>(host, "desktopHost");
        runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        doze = Property<object>(runtime, "Doze");
        wander = Property<object>(runtime, "Wander");
        foreach (var name in new[] { "workerWWindow", "sceneWindow" })
        {
            var window = Field<object>(host, name);
            if (SchedulerObserver.IsCandidate(window)) schedulers.Add(new(window, ScheduledFrame));
            else
            {
                var timer = Field<DispatcherTimer>(window, "motionTimer");
                motionTimers.Add(timer);
                timer.Tick += Frame;
            }
        }
        if (schedulers.Count != 0 && motionTimers.Count != 0)
            throw new InvalidOperationException("Host diagnostic schemas differ.");
        schema = schedulers.Count == 2 ? 2 : 1;
        type.GetMethod("Start")!.Invoke(host, null);
        var visual = (Visual)Field<object>(host, mode == DesktopDisplayMode.Wallpaper ? "workerWWindow" : "sceneWindow");
        WriteJson("environment.json", new
        {
            schema,
            startedUtc = started,
            processId = process.Id,
            mode = mode.ToString(),
            observationSeconds,
            os = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            logicalProcessors = Environment.ProcessorCount,
            screenCount = System.Windows.Forms.Screen.AllScreens.Length,
            screens = System.Windows.Forms.Screen.AllScreens.Select(s => new { s.Bounds.Width, s.Bounds.Height, s.Primary }),
            dpi = VisualTreeHelper.GetDpi(visual).PixelsPerInchX,
            renderTier = RenderCapability.Tier >> 16,
            binaries = new[] { "Dango.Desktop.dll", "Dango.Core.dll", "Dango.Physics.dll", "dango_physics_module.dll", "dango_box2d.dll", "Dango.PerformanceProbe.dll" }
                .Select(name => new { name, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name)))) }),
            frameMetric = schema == 2 ? "Actual successful applied-frame completion intervals; not successful physics steps, Rendering callbacks, or GPU presents"
                : "Time between actual product motionTimer callbacks; not GPU presents or CPU render duration",
            counterContract = schema == 2 ? "frameCounters are sample deltas; frameTotals are monotonic lifetime totals; motionTicks/tickTotal alias appliedFrames only; dropped/remainder units are TimeSpan ticks" : null,
            settings = "Fictional isolated settings; keyboard control disabled; product defaults; no real settings loaded",
            observer = "One background sample/second; no CompositionTarget.Rendering subscriber; no global input injection",
        });
        if (runtime.InitializationState != SceneInitializationState.Ready || !coordinator.GetSnapshot().IsVisible)
            throw new InvalidOperationException("Isolated host was not ready and visible.");
        lastSampleTime = phaseStarted = clock.Elapsed.TotalSeconds;
        lastCpu = process.TotalProcessorTime.TotalSeconds;
        lastHeartbeat = Stopwatch.GetTimestamp();
        watchdog = new(_ =>
        {
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastHeartbeat)).TotalSeconds <= 15) return;
            File.WriteAllText(Path.Combine(output, "watchdog-failure.json"), "{\"kind\":\"UI heartbeat missing for 15 seconds\"}");
            // Only this dedicated probe process exits. Incomplete evidence cannot pass.
            Environment.Exit(11);
        }, null, 2000, 2000);
        Event("started");
        if (memoryDiagnostic is not null)
        {
            WriteJson("memory-protocol.json", new
            {
                diagnosticSchema = 1,
                runKind = "memory-diagnostic",
                stableSeconds = observationSeconds,
                checkpointRestoreCounts = MemoryDiagnostic.Checkpoints,
                warmupSeconds = 60,
                cycleHiddenSeconds = 3,
                cycleVisibleSeconds = 3,
                disposedSeconds = 30,
                minimumScheduledSeconds = 60 + 5 * (double)observationSeconds + 20 * 6 + 30,
                purpose = "Resource attribution only; never performance acceptance or proof of a leak",
                managedBytesEstimate = "GC.GetTotalMemory(false): estimate of allocated managed memory, including not-yet-collected garbage; no forced GC",
                lastCompletedGc = "GC.GetGCMemoryInfo: metadata of the last completed GC, not a current live-heap census; null before index 1",
                allocation = "GC.GetTotalAllocatedBytes(false): approximate process-wide cumulative allocation; deltas include probe overhead and are not surviving memory",
                cpu = "Own process cumulative TotalProcessorTime and delta/elapsed/logicalProcessors; diagnostic snapshots, not formal resource gates",
                collections = "GC.CollectionCount(0..2): observed collections; this probe never requests GC or working-set trimming",
                resources = "Weak identities only; kind is assigned at first observation, roles can overlap and must not be added as independent resources. Observed identities, field/source detachment, explicit disposed state and weak collection are different facts. Each Capture and growing weak history allocate and contribute to diagnostic overhead.",
                limitations = "No GPU/DWM/native-heap inventory. No complete OS creation/destruction event count. A weak target alive after detachment does not prove a leak. Window/host objects already retained by product or existing scheduler observers can remain alive after host disposal.",
            });
            RecordMemory("phase-enter");
        }
        sampleTimer.Start();
    }

    private void Frame(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        if (!double.IsNaN(lastFrameTime)) intervals.Add((now - lastFrameTime) * 1000);
        lastFrameTime = now;
        motionTicks++; tickTotal++;
        RecordFrameState(now);
    }

    private void ScheduledFrame(SchedulerSnapshot snapshot, SchedulerSnapshot delta)
    {
        if (lastCallbackTimestamp != 0 && snapshot.FinishedTimestamp > lastCallbackTimestamp)
            callbackIntervals.Add((snapshot.FinishedTimestamp - lastCallbackTimestamp) * 1000d / Stopwatch.Frequency);
        lastCallbackTimestamp = snapshot.FinishedTimestamp;
        handlerDurations.Add(snapshot.HandlerElapsedMs);
        if (delta.Counters["appliedFrames"] == 0) return;
        if (lastAppliedTimestamp != 0)
            intervals.Add((snapshot.FinishedTimestamp - lastAppliedTimestamp) * 1000d / Stopwatch.Frequency);
        lastAppliedTimestamp = snapshot.FinishedTimestamp;
        motionTicks++; tickTotal++;
        RecordFrameState(clock.Elapsed.TotalSeconds);
    }

    private void RecordFrameState(double now)
    {
        var state = State();
        stateTicks[state] = stateTicks.GetValueOrDefault(state) + 1;
        if (state == "doze" && firstSleep is null) { firstSleep = now; Event("first-sleep"); }
        if (frameState != state) { frameState = state; Event("state-change"); }
    }

    private string State() => hostDisposed ? "disposed" : !runtime!.IsRunning ? "hidden" :
        Property<bool>(doze!, "Sleeping") ? "doze" :
        Property<bool>(wander!, "Active") || runtime.HasLightFeedback || runtime.HasQuietIdleAction ? "active" : "idle";

    private void Sample(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref lastHeartbeat, Stopwatch.GetTimestamp());
        var now = clock.Elapsed.TotalSeconds;
        var duration = now - lastSampleTime;
        process.Refresh();
        var cpu = process.TotalProcessorTime.TotalSeconds;
        var snapshot = hostDisposed ? null : coordinator!.GetSnapshot();
        var error = runtime!.LastError ?? runtime.AttachmentError ?? runtime.QuietIdleError ?? runtime.LightInteractionError ?? snapshot?.LastError;
        var state = State();
        // A second crossing state boundaries is explicitly mixed, not credited as idle/doze.
        if (stateTicks.Count > 1 || stateTicks.Count == 1 && !stateTicks.ContainsKey(state)) state = "mixed";
        var totals = SchedulerSnapshot.Sum(schedulers.Select(s => s.Read()));
        var counters = totals.Subtract(previousSample);
        if (schema == 2 && (counters.Counters["appliedFrames"] != motionTicks || totals.Counters["appliedFrames"] != tickTotal))
            throw new InvalidOperationException("Applied-frame observation differs from scheduler counts.");
        if (memoryDiagnostic is not null) RecordMemory("sample", state);
        else samples.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = schema,
            schedulerEnabled = schema == 2 ? (bool?)totals.IsRunning : null,
            frameCounters = schema == 2 ? counters.Counters : null,
            frameTotals = schema == 2 ? totals.Counters : null,
            remainderTicks = schema == 2 ? (long?)totals.RemainderTicks : null,
            handlerElapsedMs = schema == 2 ? handlerDurations.ToArray() : null,
            utc = DateTimeOffset.UtcNow,
            elapsedSeconds = now,
            intervalSeconds = duration,
            phase,
            phaseAgeSeconds = now - phaseStarted,
            cycle,
            state,
            cpuMachinePercent = (cpu - lastCpu) / duration / Environment.ProcessorCount * 100,
            cpuSeconds = cpu,
            workingSetBytes = process.WorkingSet64,
            privateBytes = process.PrivateMemorySize64,
            handles = process.HandleCount,
            threads = process.Threads.Count,
            gdiObjects = GetGuiResources(process.Handle, 0),
            userObjects = GetGuiResources(process.Handle, 1),
            visible = snapshot?.IsVisible ?? false,
            runtimeRunning = runtime.IsRunning,
            motionTimerEnabled = motionTimers.Any(t => t.IsEnabled) || totals.IsRunning,
            motionTicks,
            tickTotal,
            appIntervalsMs = intervals.ToArray(),
            renderIntervalsMs = callbackIntervals.ToArray(),
            stateTicks,
            dozeSeconds = Property<bool>(doze!, "Sleeping") ? (double?)Property<double>(doze!, "SleepSeconds") : null,
            dangoCount = runtime.DangoCount,
            error,
        }));
        previousSample = totals;
        lastSampleTime = now; lastCpu = cpu; intervals.Clear(); stateTicks.Clear(); motionTicks = 0;
        callbackIntervals.Clear(); handlerDurations.Clear();
        if (error is not null) Event("runtime-error");
        var age = now - phaseStarted;
        if (memoryDiagnostic is not null)
        {
            AdvanceMemoryProtocol(age);
            return;
        }
        if (phase == "warmup" && age >= 60) ChangePhase("observe");
        else if (phase == "observe" && age >= observationSeconds) { cycle = 1; Visibility(false); ChangePhase("cycle-hidden"); }
        else if (phase == "cycle-hidden" && age >= 3) { Visibility(true); ChangePhase("cycle-visible"); }
        else if (phase == "cycle-visible" && age >= 3)
        {
            Visibility(false);
            if (cycle >= 20) ChangePhase("hidden");
            else { cycle++; ChangePhase("cycle-hidden"); }
        }
        else if (phase == "hidden" && age >= 180) { Visibility(true); ChangePhase("restored"); }
        else if (phase == "restored" && age >= 180) BeginDisposal();
        else if (phase == "disposed" && age >= 30)
        {
            WriteJson("complete.json", new { schema, frameTotals = schema == 2 ? totals.Counters : null, startedUtc = started, completedUtc = DateTimeOffset.UtcNow, elapsedSeconds = now, observationSeconds, tickTotal, firstSleep, cycles = cycle });
            app.Shutdown(0);
        }
    }

    private void Visibility(bool visible)
    {
        RecordMemory(visible ? "before-restore" : "before-hide");
        coordinator!.SetVisibility(visible);
        lastFrameTime = double.NaN; lastAppliedTimestamp = lastCallbackTimestamp = 0;
        Event(visible ? "restored" : "hidden");
        RecordMemory(visible ? "after-restore" : "after-hide");
    }

    private void ChangePhase(string next)
    {
        phase = next; phaseStarted = clock.Elapsed.TotalSeconds;
        lastFrameTime = double.NaN; lastAppliedTimestamp = lastCallbackTimestamp = 0;
        Event("phase-change");
        RecordMemory("phase-enter");
    }

    private void AdvanceMemoryProtocol(double age)
    {
        if (phase == "warmup" && age >= 60) ChangePhase("memory-stable");
        else if (phase == "memory-stable" && age >= observationSeconds)
        {
            if (cycle == 20) BeginDisposal();
            else { Visibility(false); ChangePhase("memory-cycle-hidden"); }
        }
        else if (phase == "memory-cycle-hidden" && age >= 3)
        {
            cycle++;
            Visibility(true);
            ChangePhase("memory-cycle-visible");
        }
        else if (phase == "memory-cycle-visible" && age >= 3)
        {
            if (MemoryDiagnostic.Checkpoints.Contains(cycle)) ChangePhase("memory-stable");
            else { Visibility(false); ChangePhase("memory-cycle-hidden"); }
        }
        else if (phase == "disposed" && age >= 30)
        {
            WriteJson("complete.json", new
            {
                diagnosticSchema = 1,
                runKind = "memory-diagnostic",
                startedUtc = started,
                completedUtc = DateTimeOffset.UtcNow,
                elapsedSeconds = clock.Elapsed.TotalSeconds,
                stableSeconds = observationSeconds,
                restores = cycle,
                checkpointRestoreCounts = MemoryDiagnostic.Checkpoints,
            });
            app.Shutdown(0);
        }
    }

    private void RecordMemory(string recordKind, string? sampledState = null)
    {
        if (memoryDiagnostic is null) return;
        var now = clock.Elapsed.TotalSeconds;
        var hostState = hostDisposed ? null : coordinator!.GetSnapshot();
        var scheduler = SchedulerSnapshot.Sum(schedulers.Select(observer => observer.Read()));
        samples.WriteLine(JsonSerializer.Serialize(new
        {
            diagnosticSchema = 1,
            runKind = "memory-diagnostic",
            recordKind,
            utc = DateTimeOffset.UtcNow,
            elapsedSeconds = now,
            phase,
            phaseAgeSeconds = now - phaseStarted,
            restores = cycle,
            checkpoint = phase == "memory-stable" ? (int?)cycle : null,
            state = sampledState ?? State(),
            visible = hostState?.IsVisible ?? false,
            runtimeRunning = runtime!.IsRunning,
            hostDisposed,
            schedulerEnabled = schema == 2 ? (bool?)scheduler.IsRunning : null,
            frameTotals = schema == 2 ? scheduler.Counters : null,
            error = runtime.LastError ?? runtime.AttachmentError ?? runtime.QuietIdleError ?? runtime.LightInteractionError ?? hostState?.LastError,
            metrics = memoryDiagnostic.Capture(host!, app, process, now),
        }, MemoryJsonOptions));
    }

    private void BeginDisposal()
    {
        RecordMemory("before-dispose");
        DisposeHost();
        ChangePhase("disposed");
        var attachments = Field<object?>(runtime!, "attachments");
        WriteJson("disposal.json", new
        {
            schema,
            schedulersStopped = schedulers.All(s => !s.IsRunning),
            attachmentEntriesRemaining = attachments is null ? 0 : Field<System.Collections.IDictionary>(attachments, "entries").Count,
            attachmentRouterDisposed = attachments is null || Field<bool>(Field<object>(attachments, "router"), "disposed"),
            runtimeRunning = runtime!.IsRunning,
            timersStopped = motionTimers.All(t => !t.IsEnabled) && schedulers.All(s => !s.IsRunning),
            runtimeDisposed = Field<bool>(runtime, "disposed"),
            sceneReleased = Field<object?>(runtime, "scene") is null,
            attachmentsReleased = Field<object?>(runtime, "attachments") is null,
            trayReleased = Field<object?>(host!, "trayIcon") is null,
            windowsRemaining = app.Windows.Count,
        });
        RecordMemory("after-dispose");
    }

    private void Event(string kind) => events.WriteLine(JsonSerializer.Serialize(new
    { utc = DateTimeOffset.UtcNow, elapsedSeconds = clock.Elapsed.TotalSeconds, kind, phase, state = State(), cycle, tickTotal }));

    private void WriteJson(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name, Flags)!.GetValue(target)!;
    private void DisposeHost()
    {
        if (hostDisposed || host is null) return;
        ((IDisposable)host).Dispose(); hostDisposed = true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; sampleTimer.Stop(); watchdog?.Dispose();
        foreach (var timer in motionTimers) timer.Tick -= Frame;
        foreach (var scheduler in schedulers) scheduler.Dispose();
        DisposeHost(); samples.Dispose(); events.Dispose(); process.Dispose();
    }
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}

// No event subscriptions or strong resource references survive Capture. The identity table has
// weak keys and its values have no reference back to a resource; history contains WeakReference only.
internal sealed class MemoryDiagnostic
{
    internal static readonly int[] Checkpoints = [0, 1, 5, 10, 20];
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly ConditionalWeakTable<object, ResourceIdentity> identities = new();
    private readonly List<ResourceIdentity> history = [];
    private double? previousSeconds;
    private long previousAllocated;
    private double previousCpuSeconds;
    private int[]? previousCollections;

    // Inlining must not extend a transient resource target into the caller that changes visibility.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal object Capture(object host, System.Windows.Application app, Process process, double seconds)
    {
        var captureStarted = Stopwatch.GetTimestamp();
        process.Refresh();
        var cpuSeconds = process.TotalProcessorTime.TotalSeconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: false);
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var gcInfo = GC.GetGCMemoryInfo();
        var managedEstimate = GC.GetTotalMemory(forceFullCollection: false);
        var interval = previousSeconds.HasValue ? seconds - previousSeconds.Value : (double?)null;
        var allocatedDelta = previousSeconds.HasValue ? allocated - previousAllocated : (long?)null;
        var cpuMachinePercent = interval > 0 ? (cpuSeconds - previousCpuSeconds) / interval / Environment.ProcessorCount * 100 : null;
        if (interval is <= 0 || allocatedDelta is < 0 ||
            previousCollections is not null && collections.Where((value, index) => value < previousCollections[index]).Any())
            throw new InvalidOperationException("Memory diagnostic counters regressed.");
        var collectionDeltas = previousCollections is null ? null : collections.Select((value, index) => value - previousCollections[index]).ToArray();
        previousSeconds = seconds;
        previousAllocated = allocated;
        previousCpuSeconds = cpuSeconds;
        previousCollections = collections;

        var current = new HashSet<int>();
        var worker = RequiredField(host, "workerWWindow")!;
        Observe(RequiredField(worker, "nativeHost"), "native-host", "workerW.nativeHost", current, seconds);
        ObserveSource((HwndSource?)RequiredField(worker, "windowSource"), "workerW.windowSource", current, seconds);
        ObserveWindow((Window)RequiredField(host, "sceneWindow")!, "host.sceneWindow", current, seconds);
        foreach (var field in new[] { "dangoAboveWindow", "cushionAboveWindow", "islandOverlayWindow" })
            ObserveWindow((Window)RequiredField(worker, field)!, "workerW." + field, current, seconds);
        // Only this dedicated WPF Application's windows; no OS-wide window enumeration or content.
        foreach (Window window in app.Windows) ObserveWindow(window, "application.windows", current, seconds);
        var resources = history.Select(entry => ObserveIdentity(entry, current.Contains(entry.Id), process.Id, seconds)).ToArray();
        var counts = resources.GroupBy(entry => entry.Kind).ToDictionary(group => group.Key, group => new
        {
            observedIdentities = group.Count(),
            currentlyReferenced = group.Count(entry => entry.CurrentlyReferenced),
            weakAlive = group.Count(entry => entry.WeakAlive),
            detachedWeakAlive = group.Count(entry => !entry.CurrentlyReferenced && entry.WeakAlive),
            collected = group.Count(entry => !entry.WeakAlive),
            observedDisposed = group.Count(entry => entry.DisposedObserved),
            observedDetached = group.Count(entry => entry.DetachedObserved),
        });
        process.Refresh();
        return new
        {
            workingSetBytes = process.WorkingSet64,
            cpuSeconds,
            cpuMachinePercent,
            logicalProcessors = Environment.ProcessorCount,
            privateBytes = process.PrivateMemorySize64,
            handles = process.HandleCount,
            threads = process.Threads.Count,
            gdiObjects = GuiResourceCount(process.Handle, 0),
            userObjects = GuiResourceCount(process.Handle, 1),
            applicationWindowCount = app.Windows.Count,
            managedBytesEstimate = managedEstimate,
            lastCompletedGc = gcInfo.Index == 0 ? null : new
            {
                index = gcInfo.Index,
                generation = gcInfo.Generation,
                heapSizeBytes = gcInfo.HeapSizeBytes,
                fragmentedBytes = gcInfo.FragmentedBytes,
                totalCommittedBytes = gcInfo.TotalCommittedBytes,
                pinnedObjectsCount = gcInfo.PinnedObjectsCount,
                finalizationPendingCount = gcInfo.FinalizationPendingCount,
                concurrent = gcInfo.Concurrent,
                compacted = gcInfo.Compacted,
            },
            collectionCounts = collections,
            collectionDeltas,
            totalAllocatedBytes = allocated,
            allocatedDeltaBytes = allocatedDelta,
            allocationIntervalSeconds = interval,
            allocationBytesPerSecond = interval > 0 ? allocatedDelta / interval : null,
            resourceCounts = counts,
            resources,
            captureElapsedMs = Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds,
        };
    }

    private void ObserveWindow(Window window, string role, HashSet<int> current, double seconds)
    {
        Observe(window, "window", role, current, seconds);
        // FromVisual and WindowInteropHelper.Handle only inspect existing sources/handles.
        // Never call EnsureHandle, Show, UpdateLayout or otherwise materialize a diagnostic surface.
        ObserveSource(PresentationSource.FromVisual(window) as HwndSource, role + ".source", current, seconds);
    }

    private void ObserveSource(HwndSource? source, string role, HashSet<int> current, double seconds)
    {
        Observe(source, "hwnd-source", role, current, seconds);
        if (source is null || source.IsDisposed) return;
        Observe(source.CompositionTarget, "composition-target", role + ".compositionTarget", current, seconds);
        Observe(source.RootVisual, "surface-root", role + ".rootVisual", current, seconds);
    }

    private void Observe(object? resource, string kind, string role, HashSet<int> current, double seconds)
    {
        if (resource is null) return;
        if (!identities.TryGetValue(resource, out var entry))
        {
            entry = new(history.Count + 1, kind, resource.GetType().FullName!, new(resource), seconds);
            identities.Add(resource, entry);
            history.Add(entry);
        }
        entry.Roles.Add(role);
        current.Add(entry.Id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ResourceObservation ObserveIdentity(ResourceIdentity entry, bool current, int processId, double seconds)
    {
        var alive = entry.Target.TryGetTarget(out var target);
        bool? disposed = null, visible = null;
        nint? handle = null;
        if (target is HwndSource source)
        {
            disposed = source.IsDisposed;
            handle = disposed.Value ? 0 : source.Handle;
        }
        else if (entry.Kind == "native-host" && target is not null)
        {
            disposed = (bool)RequiredField(target, "disposed")!;
            handle = ((System.Windows.Forms.NativeWindow)target).Handle;
        }
        else if (target is Window window)
        {
            handle = new WindowInteropHelper(window).Handle;
            visible = window.IsVisible;
        }
        if (disposed == true) entry.DisposedObserved = true;
        if (entry.PreviouslyCurrent && !current) entry.DetachedObserved = true;
        entry.PreviouslyCurrent = current;
        if (!alive && entry.FirstCollectedSeconds is null) entry.FirstCollectedSeconds = seconds;
        bool? validHandle = null;
        if (handle is not null)
        {
            validHandle = false;
            if (handle != 0)
            {
                _ = GetWindowThreadProcessId(handle.Value, out var owningProcessId);
                validHandle = owningProcessId == processId && IsWindow(handle.Value);
            }
        }
        return new(entry.Id, entry.Kind, entry.Type, entry.Roles.Order().ToArray(), entry.FirstSeenSeconds,
            current, alive, disposed, entry.DisposedObserved, entry.DetachedObserved,
            entry.FirstCollectedSeconds, handle?.ToInt64(), validHandle, visible);
    }

    private static object? RequiredField(object target, string name) =>
        (target.GetType().GetField(name, Flags) ?? throw new InvalidOperationException("Missing memory diagnostic field: " + name)).GetValue(target);

    private static uint GuiResourceCount(nint handle, uint flags)
    {
        Marshal.SetLastPInvokeError(0);
        var count = GetGuiResources(handle, flags);
        var error = Marshal.GetLastPInvokeError();
        if (count == 0 && error != 0) throw new System.ComponentModel.Win32Exception(error);
        return count;
    }

    private sealed class ResourceIdentity(int id, string kind, string type, WeakReference<object> target, double firstSeenSeconds)
    {
        internal int Id { get; } = id;
        internal string Kind { get; } = kind;
        internal string Type { get; } = type;
        internal WeakReference<object> Target { get; } = target;
        internal double FirstSeenSeconds { get; } = firstSeenSeconds;
        internal HashSet<string> Roles { get; } = [];
        internal bool PreviouslyCurrent { get; set; }
        internal bool DisposedObserved { get; set; }
        internal bool DetachedObserved { get; set; }
        internal double? FirstCollectedSeconds { get; set; }
    }

    private sealed record ResourceObservation(int Id, string Kind, string Type, string[] Roles, double FirstSeenSeconds,
        bool CurrentlyReferenced, bool WeakAlive, bool? IsDisposed, bool DisposedObserved, bool DetachedObserved,
        double? FirstCollectedSeconds, long? Hwnd, bool? HwndValidForProcess, bool? Visible);

    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(nint process, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
