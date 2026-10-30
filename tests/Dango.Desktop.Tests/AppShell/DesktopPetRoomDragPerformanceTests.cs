using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.Win32;
using Dango.Desktop.Rendering.Visuals;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

// This observes real WPF host/physics work. Fake input is confined to the owned
// bridge; it does not prove OS routing, compositor delivery or human smoothness.
[TestClass]
[DoNotParallelize]
public sealed class DesktopPetRoomDragPerformanceTests
{
    private const double FrameBudgetMs = 1000d / 60;
    private const int MeasurementMilliseconds = 20000;
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("DesktopPetDragPerformance")]
    [DataRow("idle")]
    [DataRow("direct-driver")]
    [DataRow("bridge-burst")]
    public void RealOwnedRoomHasBoundedDragPresentationWorkAndLatency(string group)
    {
        using var rig = new PerformanceRoom();
        PumpFor(1000);
        var foreground = GetForegroundWindow();
        using var input = new ControlledInput(rig, group);
        input.Start();
        PumpFor(1000);
        Assert.IsTrue(group == "idle" || rig.Runtime.IsDragging, "The measured drag must be physical, not a frozen visual.");
        using var observation = new Observation(rig, group);
        input.Admitted = observation.Admit;
        observation.Start();
        try { PumpFor(MeasurementMilliseconds); }
        finally { input.StopFeed(); observation.Stop(); input.Admitted = null; }
        var report = observation.Report(rig.Platform.ReadCount);
        TestContext.WriteLine(JsonSerializer.Serialize(report));
        // The report contains distributions/counters only, never points, input
        // traces, HWNDs, window titles, desktop pixels or other application data.
        input.Release();
        PumpFor(100);
        Assert.IsFalse(rig.Window.IsPointerCaptured || rig.Window.IsGestureListening || rig.Driver.IsPointerActive,
            "The controlled gesture must leave neither capture nor its short-lived bridge active.");
        if (foreground != GetForegroundWindow())
            Assert.Inconclusive("Foreground changed during the owned performance observation; focus preservation is not established.");
        var failures = GateFailures(report);
        Assert.IsEmpty(failures, string.Join("; ", failures));
    }

    [TestMethod]
    public void DiagnosticGateRejectsFrozenPresentationAndStallsHiddenByAnAverageRate()
    {
        var healthy = new ProbeReport("direct-driver", 20, 1200, 60,
            Statistics.From(Enumerable.Repeat(16.667, 1201)), Statistics.From([1d]),
            Statistics.From([2d]), Statistics.From([3d]), Statistics.From(Enumerable.Repeat(12d, 1200)),
            0, 0, 0, 1200, 2400, 0, 0, 0, 0, 0, 0, 0, false)
        { Callback = Statistics.From([3d]) };
        Assert.IsEmpty(GateFailures(healthy));
        var frozen = healthy with { Frames = 0, FrameRate = 0, FrameIntervals = Statistics.From([]) };
        Assert.IsNotEmpty(GateFailures(frozen));
        var stalled = healthy with
        {
            // The average stays at 60: short catch-up frames cannot erase stalls.
            FrameIntervals = Statistics.From([8d, 8d, 8d, 110d]),
            GapsOver100Ms = 1,
        };
        Assert.IsNotEmpty(GateFailures(stalled));
        Assert.IsNotEmpty(GateFailures(healthy with { InputToAppliedFrame = Statistics.From([]) }));
    }

    // Explicit 60 Hz diagnostic budget, not a substitute for the product's native
    // contour/focus/lifecycle gates or the user's real-input/manual acceptance.
    private static List<string> GateFailures(ProbeReport report)
    {
        var failures = new List<string>();
        if (report.Seconds < 19 || report.Frames < 500 || report.FrameRate < 59)
            failures.Add("Insufficient fresh presentation or less than 59 applied frames/s.");
        if (report.FrameIntervals.P95 is not { } gap95 || gap95 > 2 * FrameBudgetMs + 1 ||
            report.FrameIntervals.P99 is not { } gap99 || gap99 > 3 * FrameBudgetMs + 1 ||
            report.GapsOver100Ms != 0 || report.GapsOver50Ms > Math.Max(1, report.Frames / 100))
            failures.Add("Fresh-frame stalls exceed the interval budget.");
        if (report.Apply.P95 is not { } apply95 || apply95 > FrameBudgetMs / 2 ||
            report.Apply.P99 is not { } apply99 || apply99 > FrameBudgetMs ||
            report.Callback.P95 is not { } callback95 || callback95 > FrameBudgetMs)
            failures.Add("Measured host work exceeds the per-frame CPU budget.");
        if (report.Group != "idle" &&
            (report.InputToAppliedFrame.Count < report.Frames / 2 ||
             report.InputToAppliedFrame.P95 is not { } latency95 || latency95 > 2 * FrameBudgetMs + 1 ||
             report.InputToAppliedFrame.P99 is not { } latency99 || latency99 > 3 * FrameBudgetMs + 1 ||
             report.InputToAppliedFrame.Max is not { } latencyMax || latencyMax > 100))
            failures.Add("Consumed controlled input is missing or its applied-frame latency exceeds budget.");
        if (report.Overloads != 0 || report.LongPauses != 0 || report.ClockResets != 0 || report.Faults != 0 ||
            report.DroppedTicks != 0 || report.EvictedUnobservedInputs != 0 || report.InvariantFailure)
            failures.Add("Owner/frame health, clock, dropped-work or bounded-input observation failed.");
        if (report.Group == "bridge-burst" && report.FakeHookReads == 0)
            failures.Add("The bridge sample did not travel through the injected production hook callback.");
        return failures;
    }

    private sealed record Statistics(int Count, double? P50, double? P95, double? P99, double? Max)
    {
        internal static Statistics From(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            if (sorted.Length == 0) return new(0, null, null, null, null);
            double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
            return new(sorted.Length, Percentile(.5), Percentile(.95), Percentile(.99), sorted[^1]);
        }
    }

    private sealed record ProbeReport(string Group, double Seconds, int Frames, double FrameRate,
        Statistics FrameIntervals, Statistics Prepare, Statistics Loop, Statistics Apply, Statistics InputToAppliedFrame,
        int GapsOver33Ms, int GapsOver50Ms, int GapsOver100Ms, long SuccessfulSteps, long AdmittedInputs,
        long FakeHookReads, long Overloads, long LongPauses, long ClockResets, long Faults, long DroppedTicks,
        int EvictedUnobservedInputs, bool InvariantFailure)
    {
        // Scheduler timing stops before CallbackCompleted subscribers. This
        // callback is measured scheduler work; it is not DWM/display latency.
        public Statistics Callback { get; init; } = Statistics.From([]);
        public WindowApplyStatistics WindowApply { get; init; } = WindowApplyStatistics.Empty;
        public long UniqueCallbacks { get; init; }
        public long ZeroStepCallbacks { get; init; }
        public long OneStepCallbacks { get; init; }
        public long MultiStepCallbacks { get; init; }
        public string EvidenceBoundary => "Real native/WPF owner and applied frames; controlled input; no OS input routing or DWM/manual smoothness claim.";
    }

    private sealed record WindowApplyStatistics(Statistics Total, Statistics Binding, Statistics FirstLayoutAndBounds,
        Statistics PositionAndSize, Statistics SecondLayout, Statistics NativeRegion, Statistics Qualification)
    {
        internal static WindowApplyStatistics Empty => new(Statistics.From([]), Statistics.From([]), Statistics.From([]),
            Statistics.From([]), Statistics.From([]), Statistics.From([]), Statistics.From([]));
    }

    private sealed class Observation : IDisposable
    {
        private static readonly FieldInfo PointerWorld = typeof(LocalRoomSceneDriver)
            .GetField("lastPointerWorld", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly PerformanceRoom rig;
        private readonly string group;
        private readonly object owner;
        private readonly DesktopPetDangoWindow window;
        private readonly List<double> intervals = [], prepare = [], loop = [], apply = [], callback = [], latencies = [];
        private readonly List<double> windowTotal = [], binding = [], firstLayoutAndBounds = [], positionAndSize = [],
            secondLayout = [], nativeRegion = [], qualification = [];
        private readonly List<Admission> pending = [];
        private object? lastFrame;
        private long started, finished, previousFrameTimestamp, sequence, lastObservedInput, baselineHookReads;
        private long zeroStepCallbacks, oneStepCallbacks, multiStepCallbacks;
        private int frames, evicted;
        private bool invariantFailure, subscribed;
        private HostFrameDiagnostics baseline, final, previousCallbackDiagnostics;
        internal Observation(PerformanceRoom rig, string group)
        { this.rig = rig; this.group = group; owner = rig.Runtime.FrameDriver!; window = rig.Window; }
        internal void Start()
        {
            rig.Scheduler.DetailedTimingEnabled = true;
            window.DetailedApplyTimingEnabled = true;
            baseline = rig.Scheduler.Snapshot;
            previousCallbackDiagnostics = baseline;
            baselineHookReads = rig.Platform.ReadCount;
            lastFrame = rig.Host.Scene.LastAppliedFrame;
            started = previousFrameTimestamp = Stopwatch.GetTimestamp();
            rig.Scheduler.CallbackCompleted += Observe;
            subscribed = true;
        }
        internal void Admit(LogicalPoint world, long timestamp)
        {
            pending.Add(new(++sequence, world, timestamp));
            if (pending.Count <= 256) return;
            if (pending[0].Sequence > lastObservedInput) evicted++;
            pending.RemoveAt(0);
        }
        private void Observe(object? sender, EventArgs args)
        {
            var diagnostics = rig.Scheduler.Snapshot;
            try
            {
                if (!rig.Driver.OwnsRuntime || !ReferenceEquals(owner, rig.Runtime.FrameDriver) ||
                    !rig.Host.IsReady || !rig.Host.IsVisible || !rig.Host.Scene.HasHealthyFrame ||
                    !rig.Window.HasHealthyFrame || !ReferenceEquals(rig.Host.Scene.LastAppliedFrame, rig.Window.LastAppliedFrame) ||
                    rig.Runtime.EntityIds.Count != 1 || rig.Runtime.LastError is not null ||
                    group != "idle" && (!rig.Runtime.IsDragging || !rig.Driver.IsPointerActive))
                    invariantFailure = true;
                if (rig.Scheduler.LastStageTiming is { } timing)
                {
                    // Attribute fresh-frame cadence without changing its gate:
                    // a healthy unique callback can request no fixed step, or
                    // several fixed steps with only the final record applied.
                    if (diagnostics.DuplicateCallbacks == previousCallbackDiagnostics.DuplicateCallbacks &&
                        timing.SameGenerationAtExit && !timing.Threw && timing.PrepareFinishedTimestamp is not null &&
                        diagnostics.Overloads == previousCallbackDiagnostics.Overloads &&
                        diagnostics.LongPauses == previousCallbackDiagnostics.LongPauses &&
                        diagnostics.ClockResets == previousCallbackDiagnostics.ClockResets &&
                        diagnostics.Faults == previousCallbackDiagnostics.Faults &&
                        rig.Driver.OwnsRuntime && rig.Host.IsReady && rig.Host.Scene.HasHealthyFrame && window.HasHealthyFrame)
                    {
                        if (timing.RequestedSteps == 0) zeroStepCallbacks++;
                        else if (timing.RequestedSteps == 1) oneStepCallbacks++;
                        else multiStepCallbacks++;
                    }
                    if (timing.ApplyStartedTimestamp is not null)
                        callback.Add(Milliseconds(timing.StartedTimestamp, timing.FinishedTimestamp));
                    AddDuration(prepare, timing.PrepareStartedTimestamp, timing.PrepareFinishedTimestamp);
                    AddDuration(loop, timing.LoopStartedTimestamp, timing.LoopFinishedTimestamp);
                    AddDuration(apply, timing.ApplyStartedTimestamp, timing.ApplyFinishedTimestamp);
                }
                var frame = rig.Host.Scene.LastAppliedFrame;
                if (frame is null || ReferenceEquals(lastFrame, frame)) return;
                if (window.LastApplyTiming is { Succeeded: true } windowTiming)
                {
                    windowTotal.Add(windowTiming.TotalMilliseconds);
                    binding.Add(windowTiming.BindingMilliseconds);
                    firstLayoutAndBounds.Add(windowTiming.FirstLayoutAndBoundsMilliseconds);
                    positionAndSize.Add(windowTiming.PositionAndSizeMilliseconds);
                    secondLayout.Add(windowTiming.SecondLayoutMilliseconds);
                    nativeRegion.Add(windowTiming.NativeRegionMilliseconds);
                    qualification.Add(windowTiming.QualificationMilliseconds);
                }
                else invariantFailure = true;
                var now = Stopwatch.GetTimestamp();
                intervals.Add(Milliseconds(previousFrameTimestamp, now));
                previousFrameTimestamp = now;
                lastFrame = frame;
                frames++;
                // Correlate only a target actually consumed by the real driver;
                // merely admitting fake hook input does not count as delivery.
                var consumed = (LogicalPoint)PointerWorld.GetValue(rig.Driver)!;
                for (var index = pending.Count - 1; index >= 0; index--)
                {
                    var admission = pending[index];
                    if (Math.Abs(admission.World.X - consumed.X) > 1e-6 || Math.Abs(admission.World.Y - consumed.Y) > 1e-6) continue;
                    if (admission.Sequence > lastObservedInput)
                    {
                        latencies.Add(Milliseconds(admission.Timestamp, now));
                        lastObservedInput = admission.Sequence;
                    }
                    pending.RemoveRange(0, index + 1);
                    break;
                }
            }
            catch
            {
                // Assertions must not escape into/corrupt the production scheduler.
                invariantFailure = true;
            }
            finally { previousCallbackDiagnostics = diagnostics; }
        }
        internal void Stop()
        {
            if (!subscribed) return;
            rig.Scheduler.CallbackCompleted -= Observe;
            subscribed = false;
            finished = Stopwatch.GetTimestamp();
            final = rig.Scheduler.Snapshot;
            intervals.Add(Milliseconds(previousFrameTimestamp, finished));
            rig.Scheduler.DetailedTimingEnabled = false;
            window.DetailedApplyTimingEnabled = false;
        }
        internal ProbeReport Report(long hookReads)
        {
            var seconds = (finished - started) / (double)Stopwatch.Frequency;
            return new(group, seconds, frames, frames / seconds, Statistics.From(intervals), Statistics.From(prepare),
                Statistics.From(loop), Statistics.From(apply), Statistics.From(latencies),
                intervals.Count(value => value > 33), intervals.Count(value => value > 50), intervals.Count(value => value > 100),
                final.SuccessfulSteps - baseline.SuccessfulSteps, sequence, hookReads - baselineHookReads,
                final.Overloads - baseline.Overloads, final.LongPauses - baseline.LongPauses,
                final.ClockResets - baseline.ClockResets, final.Faults - baseline.Faults,
                Dropped(final) - Dropped(baseline), evicted, invariantFailure)
            {
                Callback = Statistics.From(callback),
                WindowApply = new(Statistics.From(windowTotal), Statistics.From(binding), Statistics.From(firstLayoutAndBounds),
                    Statistics.From(positionAndSize), Statistics.From(secondLayout), Statistics.From(nativeRegion), Statistics.From(qualification)),
                UniqueCallbacks = final.Callbacks - baseline.Callbacks - (final.DuplicateCallbacks - baseline.DuplicateCallbacks),
                ZeroStepCallbacks = zeroStepCallbacks,
                OneStepCallbacks = oneStepCallbacks,
                MultiStepCallbacks = multiStepCallbacks,
            };
        }
        private static long Dropped(HostFrameDiagnostics value) => value.OverloadDroppedTicks + value.PauseDroppedTicks +
            value.FaultDroppedTicks + value.LifecycleDroppedTicks;
        private static void AddDuration(List<double> target, long? a, long? b)
        { if (a is { } start && b is { } end) target.Add(Milliseconds(start, end)); }
        public void Dispose() => Stop();
        private readonly record struct Admission(long Sequence, LogicalPoint World, long Timestamp);
    }

    private sealed class ControlledInput : IDisposable
    {
        private readonly PerformanceRoom rig;
        private readonly string group;
        private readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(8) };
        private LogicalPoint origin, latest;
        private long started;
        private double previousSeconds;
        private bool down;
        internal Action<LogicalPoint, long>? Admitted;
        internal ControlledInput(PerformanceRoom rig, string group)
        { this.rig = rig; this.group = group; timer.Tick += Tick; }
        internal void Start()
        {
            if (group == "idle") return;
            origin = rig.Runtime.CurrentSelectedCenter ?? throw new AssertFailedException("A real physical body is required.");
            latest = origin;
            if (group == "direct-driver") Assert.IsTrue(rig.Driver.PointerDown(rig.ToScene(origin)));
            else _ = SendOwnMessage(rig.Window.Handle, 0x0201, 1, rig.ClientParameter(origin));
            down = true;
            Submit(new(origin.X + 30, origin.Y - 90));
            DesktopPetRoomFixture.WaitUntil(() => rig.Runtime.IsDragging, rig.Runtime);
            started = Stopwatch.GetTimestamp();
            timer.Start();
        }
        private void Tick(object? sender, EventArgs args)
        {
            var seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            var count = group == "bridge-burst" ? 8 : 1;
            for (var index = 1; index <= count; index++)
            {
                var t = previousSeconds + (seconds - previousSeconds) * index / count;
                Submit(new(origin.X + Math.Sin(t * Math.PI * 1.2) * 80,
                    origin.Y - 110 + Math.Sin(t * Math.PI * .8) * 24));
            }
            previousSeconds = seconds;
        }
        private void Submit(LogicalPoint world)
        {
            latest = world;
            if (group == "direct-driver")
            {
                Admitted?.Invoke(world, Stopwatch.GetTimestamp());
                _ = rig.Driver.PointerMove(rig.ToScene(world));
            }
            else
            {
                // Low-level mouse coordinates are integral physical pixels.
                // Correlate the actual submitted point through an independent
                // owned HWND/DPI inverse, retaining the strict world tolerance.
                var screen = rig.ToQuantizedScreen(world);
                Admitted?.Invoke(rig.ScreenToPhysicalWorld(screen), Stopwatch.GetTimestamp());
                rig.Platform.Emit(0x0200, screen);
            }
        }
        internal void StopFeed() => timer.Stop();
        internal void Release()
        {
            StopFeed();
            if (!down) return;
            down = false;
            if (group == "direct-driver") _ = rig.Driver.PointerUp(rig.ToScene(latest));
            else rig.Platform.Emit(0x0202, rig.ToQuantizedScreen(latest));
        }
        public void Dispose() { StopFeed(); timer.Tick -= Tick; rig.Driver.ClearInput(); }
    }

    private sealed class PerformanceRoom : IDisposable
    {
        internal StackedDangoSceneRuntime Runtime { get; } = StackedDangoSceneRuntime.CreateProduct();
        private readonly DesktopSceneSession session = new();
        internal ControlledPlatform Platform { get; } = new();
        internal DesktopPetRoomHost Host { get; }
        internal LocalRoomSceneDriver Driver { get; }
        internal HostFrameScheduler Scheduler => DesktopPetRoomFixture.Field<HostFrameScheduler>(Driver, "scheduler");
        internal DesktopPetDangoWindow Window => Host.PetWindow ?? throw new AssertFailedException("The owned pet window is missing.");
        internal PerformanceRoom()
        {
            Driver = new(session, Runtime);
            Host = new(Platform);
            try
            {
                Assert.IsTrue(Runtime.TryInitialize(), "The supplied isolated native module must initialize.");
                Host.Bind(Driver);
                Host.HealthyFrameReady += Confirm;
                Host.Show(1, Forms.Screen.PrimaryScreen ?? throw new AssertFailedException("Primary display is unavailable."));
                Driver.Start(Host.Scene, 1, enterWorld: true);
                DesktopPetRoomFixture.WaitUntil(() => Host.IsReady, Runtime);
                Assert.IsTrue(Driver.OwnsRuntime && Runtime.EntityIds.Count == 1);
            }
            catch { Dispose(); throw; }
        }
        private void Confirm(long attempt) => Host.ConfirmReady(attempt);
        internal Point ToScene(LogicalPoint point)
        {
            var offset = Host.Scene.PresentationOffset;
            var projected = Host.Scene.RoomToScene(new(point.X + offset.X,
                point.Y + offset.Y + Runtime.NeutralBodyGroundOffset));
            return new(projected.X, projected.Y);
        }
        internal Point ToScreen(LogicalPoint point) => Host.Scene.PointToScreen(ToScene(point));
        internal Point ToQuantizedScreen(LogicalPoint point)
        {
            var screen = ToScreen(point);
            return new(Math.Round(screen.X), Math.Round(screen.Y));
        }
        internal LogicalPoint ScreenToPhysicalWorld(Point screen)
        {
            var source = System.Windows.PresentationSource.FromVisual(Window) as System.Windows.Interop.HwndSource
                ?? throw new AssertFailedException("The owned pet HWND is missing.");
            Assert.AreSame(source, System.Windows.PresentationSource.FromVisual(Host.Scene));
            var origin = Host.SceneOriginPixels;
            var sceneDip = source.CompositionTarget.TransformFromDevice.Transform(new Point(screen.X - origin.X, screen.Y - origin.Y));
            var displayed = Host.Scene.CurrentProjection.ScreenToWorld(new(sceneDip.X, sceneDip.Y));
            var offset = Host.Scene.PresentationOffset;
            return new(displayed.X - offset.X, displayed.Y - offset.Y - Runtime.NeutralBodyGroundOffset);
        }
        internal nint ClientParameter(LogicalPoint point)
        {
            var screen = ToScreen(point);
            var local = new NativePoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
            Assert.IsTrue(ScreenToClient(Window.Handle, ref local));
            return (nint)((uint)(ushort)(short)local.X | ((uint)(ushort)(short)local.Y << 16));
        }
        public void Dispose() { Driver.Dispose(); Host.HealthyFrameReady -= Confirm; Host.Dispose(); Runtime.Dispose(); }
    }

    private sealed class ControlledPlatform : IDesktopPetPointerGesturePlatform
    {
        private NativeMethods.LowLevelMouseProcedure? callback;
        private DesktopPetPointerSample current;
        internal long ReadCount;
        public nint Install(NativeMethods.LowLevelMouseProcedure value) { callback = value; return 71; }
        public bool Uninstall(nint hook) { callback = null; return true; }
        public nint Next(nint hook, int code, nint message, nint data) => 0;
        public DesktopPetPointerSample Read(nint data) { ReadCount++; return current; }
        internal void Emit(int message, Point screen)
        {
            current = new(screen, false, false, unchecked((uint)Environment.TickCount));
            _ = (callback ?? throw new AssertFailedException("The owned production bridge has not installed its fake platform."))(0, message, 1);
        }
    }

    private static double Milliseconds(long start, long end) => (end - start) * 1000d / Stopwatch.Frequency;
    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        // A high-priority end sentinel bounds the observation even if the very
        // Normal-priority queue under test starves Background-priority timers.
        var end = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        end.Tick += (_, _) => { end.Stop(); frame.Continue = false; };
        end.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { end.Stop(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendOwnMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
