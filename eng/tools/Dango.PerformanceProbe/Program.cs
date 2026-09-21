using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
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
        if (args.Length != 3 || !int.TryParse(args[1], out var seconds) || seconds < 10 ||
            !Enum.TryParse<DesktopDisplayMode>(args[2], out var mode) || !Enum.IsDefined(mode)) return 2;
        var output = Path.GetFullPath(args[0]);
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
            probe = new Probe(app, output, seconds, mode);
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

    internal Probe(System.Windows.Application app, string output, int seconds, DesktopDisplayMode mode)
    {
        this.app = app; this.output = output; observationSeconds = seconds; this.mode = mode;
        samples = new(Path.Combine(output, "samples.jsonl")) { AutoFlush = true };
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
        samples.WriteLine(JsonSerializer.Serialize(new
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
        else if (phase == "restored" && age >= 180)
        {
            DisposeHost();
            ChangePhase("disposed");
            var attachments = Field<object?>(runtime, "attachments");
            WriteJson("disposal.json", new
            {
                schema,
                schedulersStopped = schedulers.All(s => !s.IsRunning),
                attachmentEntriesRemaining = attachments is null ? 0 : Field<System.Collections.IDictionary>(attachments, "entries").Count,
                attachmentRouterDisposed = attachments is null || Field<bool>(Field<object>(attachments, "router"), "disposed"),
                runtimeRunning = runtime.IsRunning,
                timersStopped = motionTimers.All(t => !t.IsEnabled) && schedulers.All(s => !s.IsRunning),
                runtimeDisposed = Field<bool>(runtime, "disposed"),
                sceneReleased = Field<object?>(runtime, "scene") is null,
                attachmentsReleased = Field<object?>(runtime, "attachments") is null,
                trayReleased = Field<object?>(host!, "trayIcon") is null,
                windowsRemaining = app.Windows.Count,
            });
        }
        else if (phase == "disposed" && age >= 30)
        {
            WriteJson("complete.json", new { schema, frameTotals = schema == 2 ? totals.Counters : null, startedUtc = started, completedUtc = DateTimeOffset.UtcNow, elapsedSeconds = now, observationSeconds, tickTotal, firstSleep, cycles = cycle });
            app.Shutdown(0);
        }
    }

    private void Visibility(bool visible)
    {
        coordinator!.SetVisibility(visible);
        lastFrameTime = double.NaN; lastAppliedTimestamp = lastCallbackTimestamp = 0;
        Event(visible ? "restored" : "hidden");
    }

    private void ChangePhase(string next)
    {
        phase = next; phaseStarted = clock.Elapsed.TotalSeconds;
        lastFrameTime = double.NaN; lastAppliedTimestamp = lastCallbackTimestamp = 0;
        Event("phase-change");
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
