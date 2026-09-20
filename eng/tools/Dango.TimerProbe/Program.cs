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

namespace Dango.TimerProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var output = Path.GetFullPath(args[0]);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (Within(output, Path.Combine(local, "Programs", "Dango")) || Within(output, Path.Combine(local, "Dango"))) return 4;
        if (Directory.Exists(output) || File.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var probe = new TimerProbe(app, output);
        app.Startup += (_, _) => probe.Start();
        app.DispatcherUnhandledException += (_, e) =>
        {
            probe.Fail(e.Exception.GetType().Name);
            probe.StopAfterFailure();
            e.Handled = true;
            app.Shutdown(10);
        };
        var exitCode = 10;
        try { exitCode = app.Run(); }
        catch (Exception error) { probe.Fail(error.GetType().Name); }
        finally { if (!probe.DisposeAndWrite()) exitCode = 12; }
        return exitCode;
    }

    private static bool Within(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

// The product timer's original handler is called exactly once by the wrapper.
// No product input, physics step, interval, priority, rendering or binary is changed.
internal sealed class TimerProbe(System.Windows.Application app, string output)
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const double PhaseSeconds = 60;
    private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(1d / 60d);
    private readonly long origin = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
    private readonly DispatcherTimer controlTimer = new(DispatcherPriority.Render) { Interval = TimerInterval };
    private readonly List<Phase> phases = [];
    private readonly List<string> errors = [];
    private Phase? current;
    private object? host;
    private StackedDangoSceneRuntime? runtime;
    private DesktopHostCoordinator? coordinator;
    private DispatcherTimer? productTimer;
    private EventHandler? originalHandler;
    private System.Threading.Timer? watchdog;
    private long heartbeat;
    private bool finished, disposed, productTimingActive, hostDisposed, productWrapped;
    private object? environment;
    private object? disposal;
    private double? productDpi;

    internal void Start()
    {
        // Hashing and metadata reads precede every measured interval.
        environment = new
        {
            schema = 1,
            processId = Environment.ProcessId,
            startedUtc,
            os = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            logicalProcessors = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency,
            Stopwatch.IsHighResolution,
            screenCount = System.Windows.Forms.Screen.AllScreens.Length,
            screens = System.Windows.Forms.Screen.AllScreens.Select(s => new { s.Bounds.Width, s.Bounds.Height, s.Primary }).ToArray(),
            renderTier = RenderCapability.Tier >> 16,
            binaries = new[] { "Dango.Desktop.dll", "Dango.Core.dll", "Dango.Physics.dll", "dango_physics_module.dll", "dango_box2d.dll", "Dango.TimerProbe.dll", "PresentationFramework.dll", "PresentationCore.dll", "WindowsBase.dll" }
                .Select(name => new { name, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name)))) }).ToArray(),
            requestedTimerIntervalMs = TimerInterval.TotalMilliseconds,
            priority = DispatcherPriority.Render.ToString(),
            conditions = new[]
            {
                "Sequential 60 s empty timer, 60 s synthetic 5 ms busy handler, 60 s Product Wallpaper warmup, 60 s Product Wallpaper measurement.",
                "Empty and busy controls have a WPF dispatcher but no visual host. They are scheduler controls, not product performance baselines.",
                "All callback timestamps are buffered; there is no per-frame file IO, CPU sampling, CompositionTarget.Rendering subscription, or input injection.",
                "The Product host uses fictional settings and the published binaries, bypassing the usual single-instance entrypoint.",
                "The original OnMotionTimerTick event delegate is replaced only in this process by a wrapper calling it once with its original sender and EventArgs.",
                "Handler elapsed time includes preemption and synchronous native calls; it is not handler CPU time or complete rendering cost.",
                "Exit-to-next-entry includes probe bookkeeping, WPF rearming/deferred work, timer waiting and dispatcher queueing; it is not a pure scheduler-delay measurement.",
                "Callback frequency is not GPU presentation FPS. Synthetic busy work is diagnostic and does not validate product capacity or stability.",
                "A 15 s callback-heartbeat gap fails this probe only; a system suspend or scheduling outage cannot be attributed to a product crash from this flag alone.",
            },
        };
        controlTimer.Tick += OnControlTick;
        heartbeat = Stopwatch.GetTimestamp();
        watchdog = new(_ =>
        {
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref heartbeat)).TotalSeconds <= 15) return;
            // Never access the UI-owned buffers from this thread. Missing timings/complete
            // files are deliberately incomplete evidence if the UI cannot shut down.
            File.WriteAllText(Path.Combine(output, "watchdog-failure.json"), "{\"kind\":\"callback heartbeat missing for 15 seconds\"}");
            Environment.Exit(11);
        }, null, 2000, 2000);
        BeginPhase("empty-timer", synthetic: true);
        controlTimer.Start();
    }

    private void OnControlTick(object? sender, EventArgs args)
    {
        var entry = Stopwatch.GetTimestamp();
        if (current!.Name == "busy-5ms-timer")
            while (Stopwatch.GetElapsedTime(entry).TotalMilliseconds < 5) { }
        var exit = Stopwatch.GetTimestamp();
        Record(entry, exit, handlerCompleted: true);
    }

    private void OnProductTick(object? sender, EventArgs args)
    {
        var entry = Stopwatch.GetTimestamp();
        var completed = false;
        try
        {
            originalHandler!(sender, args);
            completed = true;
        }
        finally
        {
            var exit = Stopwatch.GetTimestamp();
            if (productTimingActive) Record(entry, exit, completed);
        }
    }

    private void Record(long entry, long exit, bool handlerCompleted)
    {
        Interlocked.Exchange(ref heartbeat, exit);
        current!.Ticks.Add(new(entry, exit, handlerCompleted));
        if (!handlerCompleted || Stopwatch.GetElapsedTime(current.Started, exit).TotalSeconds < PhaseSeconds) return;
        current.Completed = exit;
        current.CompletedUtc = DateTimeOffset.UtcNow;
        if (current.Name == "product-warmup")
        {
            VerifyProduct();
            BeginPhase("product-measured", synthetic: false);
            return;
        }
        if (current.Name == "product-measured")
        {
            VerifyProduct();
            productTimer!.Stop();
            finished = true;
            // Verify explicit host cleanup before Application.Shutdown can close
            // windows itself and hide a missed release.
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                DisposeOwnedHost();
                app.Shutdown(0);
            }));
            return;
        }
        controlTimer.Stop();
        var wasEmpty = current.Name == "empty-timer";
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (wasEmpty)
            {
                BeginPhase("busy-5ms-timer", synthetic: true);
                controlTimer.Start();
            }
            else StartProduct();
        }));
    }

    private void StartProduct()
    {
        Interlocked.Exchange(ref heartbeat, Stopwatch.GetTimestamp());
        var settingsPath = Path.Combine(output, "fictional-settings.json");
        new ControlCenterWindowSettingsStore().Save(settingsPath,
            ControlCenterWindowSettings.CreateDefault() with { IntroSeen = true, DisplayMode = DesktopDisplayMode.Wallpaper });
        var type = typeof(StackedDangoSceneRuntime).Assembly.GetType("Dango.Desktop.AppShell.DangoApplicationHost", true)!;
        host = Activator.CreateInstance(type, Flags, null,
            [false, false, settingsPath, Path.Combine(AppContext.BaseDirectory, "dango_physics_module.dll"), null], null)!;
        coordinator = Field<DesktopHostCoordinator>(host, "desktopHost");
        runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        var window = Field<object>(host, "workerWWindow");
        productTimer = Field<DispatcherTimer>(window, "motionTimer");
        if (productTimer.Interval != TimerInterval) throw new InvalidOperationException("Unexpected product timer interval.");
        var method = window.GetType().GetMethod("OnMotionTimerTick", Flags)!;
        originalHandler = method.CreateDelegate<EventHandler>(window);
        // Delegate equality uses the same method and target as the constructor's
        // subscription. Remove the original once, then wrap that exact delegate.
        productTimer.Tick -= originalHandler;
        productTimer.Tick += OnProductTick;
        productWrapped = true;
        type.GetMethod("Start")!.Invoke(host, null);
        productDpi = VisualTreeHelper.GetDpi((Visual)window).PixelsPerInchX;
        VerifyProduct();
        // Initialization time is outside the full 60-second warmup. Any
        // reentrant startup callback still calls the original without recording.
        BeginPhase("product-warmup", synthetic: false);
        productTimingActive = true;
    }

    private void VerifyProduct()
    {
        if (runtime!.InitializationState != SceneInitializationState.Ready || !runtime.IsRunning ||
            !coordinator!.GetSnapshot().IsVisible || productTimer?.IsEnabled != true || runtime.DangoCount != 1)
            throw new InvalidOperationException("Isolated product host did not remain ready, visible and running with one Dango.");
        if (runtime.LastError is not null || runtime.AttachmentError is not null || runtime.QuietIdleError is not null ||
            runtime.LightInteractionError is not null || coordinator.GetSnapshot().LastError is not null)
            throw new InvalidOperationException("Product host reported an error.");
    }

    private void BeginPhase(string name, bool synthetic)
    {
        current = new(name, synthetic, Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow);
        phases.Add(current);
        Interlocked.Exchange(ref heartbeat, current.Started);
    }

    internal void Fail(string kind) => errors.Add(kind);

    internal void StopAfterFailure()
    {
        controlTimer.Stop();
        try { DisposeOwnedHost(); }
        catch (Exception error) { Fail("Dispose:" + error.GetType().Name); }
    }

    internal bool DisposeAndWrite()
    {
        if (disposed) return finished && errors.Count == 0;
        disposed = true;
        watchdog?.Dispose();
        controlTimer.Stop();
        controlTimer.Tick -= OnControlTick;
        try { DisposeOwnedHost(); }
        catch (Exception error) { Fail("Dispose:" + error.GetType().Name); }
        try
        {
            WriteJson("environment.json", new { metadata = environment, productDpi });
            WriteJson("timings.json", new
            {
                schema = 1,
                startedUtc,
                completedUtc = DateTimeOffset.UtcNow,
                complete = finished && errors.Count == 0,
                errors,
                stopwatchFrequency = Stopwatch.Frequency,
                phaseSeconds = PhaseSeconds,
                phases = phases.Select(DescribePhase),
            });
            WriteJson("disposal.json", disposal);
            if (finished && errors.Count == 0)
                WriteJson("complete.json", new { completedUtc = DateTimeOffset.UtcNow, phaseCount = phases.Count, totalElapsedSeconds = Stopwatch.GetElapsedTime(origin).TotalSeconds });
            else WriteJson("failure.json", new { errors, finished });
        }
        catch { return false; }
        return finished && errors.Count == 0;
    }

    private void DisposeOwnedHost()
    {
        if (hostDisposed) return;
        productTimingActive = false;
        if (productTimer is not null)
        {
            productTimer.Stop();
            if (productWrapped)
            {
                productTimer.Tick -= OnProductTick;
                productTimer.Tick += originalHandler;
                productWrapped = false;
            }
        }
        if (host is IDisposable ownedHost) ownedHost.Dispose();
        var runtimeDisposed = runtime is null || Field<bool>(runtime, "disposed");
        var runtimeRunning = runtime?.IsRunning ?? false;
        var sceneReleased = runtime is null || Field<object?>(runtime, "scene") is null;
        var attachments = runtime is null ? null : Field<object?>(runtime, "attachments");
        var attachmentEntriesRemaining = attachments is null ? 0 : Field<System.Collections.IDictionary>(attachments, "entries").Count;
        var attachmentRouterDisposed = attachments is null || Field<bool>(Field<object>(attachments, "router"), "disposed");
        var trayReleased = host is null || Field<object?>(host, "trayIcon") is null;
        var windowsRemaining = app.Windows.Count;
        disposal = new
        {
            productHostCreated = host is not null,
            runtimeDisposed,
            runtimeRunning,
            sceneReleased,
            trayReleased,
            controlTimerStopped = !controlTimer.IsEnabled,
            productTimerStopped = productTimer?.IsEnabled != true,
            windowsRemaining,
            attachmentEntriesRemaining,
            attachmentRouterDisposed,
        };
        hostDisposed = true;
        if (!runtimeDisposed || runtimeRunning || !sceneReleased || !trayReleased || controlTimer.IsEnabled ||
            productTimer?.IsEnabled == true || windowsRemaining != 0 || attachmentEntriesRemaining != 0 || !attachmentRouterDisposed)
            Fail("OwnedHostResourcesNotReleased");
    }

    private object DescribePhase(Phase phase)
    {
        var handler = phase.Ticks.Select(t => Milliseconds(t.Exit - t.Entry)).ToArray();
        var periods = phase.Ticks.Zip(phase.Ticks.Skip(1), (previous, next) => Milliseconds(next.Entry - previous.Entry)).ToArray();
        var waits = phase.Ticks.Zip(phase.Ticks.Skip(1), (previous, next) => Milliseconds(next.Entry - previous.Exit)).ToArray();
        return new
        {
            name = phase.Name,
            synthetic = phase.Synthetic,
            phase.StartedUtc,
            phase.CompletedUtc,
            durationSeconds = phase.Completed is { } end ? Stopwatch.GetElapsedTime(phase.Started, end).TotalSeconds : (double?)null,
            complete = phase.Completed is not null,
            tickCount = phase.Ticks.Count,
            callbackHz = periods.Length > 0 ? periods.Length * 1000 / periods.Sum() : (double?)null,
            handlerElapsedMs = Summary(handler),
            pairedPreviousHandlerElapsedMs = Summary(handler.Length > 1 ? handler[..^1] : []),
            entryToEntryMs = Summary(periods),
            exitToNextEntryMs = Summary(waits),
            rawTicks = phase.Ticks.Select((tick, index) => new
            {
                entryTimestamp = tick.Entry - origin,
                exitTimestamp = tick.Exit - origin,
                tick.HandlerCompleted,
                entryElapsedSeconds = (double)(tick.Entry - origin) / Stopwatch.Frequency,
                handlerElapsedMs = handler[index],
                entryToEntryMs = index > 0 ? periods[index - 1] : (double?)null,
                previousExitToEntryMs = index > 0 ? waits[index - 1] : (double?)null,
            }),
        };
    }

    private static object? Summary(double[] values)
    {
        if (values.Length == 0) return null;
        var sorted = values.Order().ToArray();
        return new { count = values.Length, average = values.Average(), p50 = sorted[(int)Math.Ceiling(sorted.Length * .50) - 1], p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], maximum = sorted[^1] };
    }

    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    private void WriteJson(string name, object? value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private readonly record struct Tick(long Entry, long Exit, bool HandlerCompleted);
    private sealed class Phase(string name, bool synthetic, long started, DateTimeOffset startedUtc)
    {
        internal string Name { get; } = name;
        internal bool Synthetic { get; } = synthetic;
        internal long Started { get; } = started;
        internal DateTimeOffset StartedUtc { get; } = startedUtc;
        internal long? Completed { get; set; }
        internal DateTimeOffset? CompletedUtc { get; set; }
        internal List<Tick> Ticks { get; } = new(8192);
    }
}
