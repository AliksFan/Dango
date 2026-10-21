using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
[TestCategory("LegacyModuleIntegration")]
[DoNotParallelize]
public sealed class AccessoryHeldMovementTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void StableWardrobeRebuildsTheBodyPathExactlyOncePerAppliedNativeFrame()
    {
        using var rig = new Rig();
        var descriptor = DependencyPropertyDescriptor.FromProperty(ShapePath.DataProperty, typeof(ShapePath))!;
        var changes = 0; var previousChanges = 0;
        var previous = rig.Scheduler.Snapshot;
        var counts = new List<int>();
        var pump = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
        void Changed(object? sender, EventArgs args) => changes++;
        void Completed(object? sender, EventArgs args)
        {
            var next = rig.Scheduler.Snapshot;
            var rebuilt = changes - previousChanges;
            previousChanges = changes;
            if (next.AppliedFrames - previous.AppliedFrames == 1 && next.SuccessfulSteps > previous.SuccessfulSteps)
                counts.Add(rebuilt);
            previous = next;
            if (counts.Count >= 60) pump.Continue = false;
        }
        void TimedOut(object? sender, EventArgs args) => pump.Continue = false;
        try
        {
            descriptor.AddValueChanged(rig.BodyPath, Changed);
            rig.Scheduler.CallbackCompleted += Completed;
            timeout.Tick += TimedOut; timeout.Start();
            Dispatcher.PushFrame(pump);
        }
        finally
        {
            timeout.Stop(); timeout.Tick -= TimedOut;
            rig.Scheduler.CallbackCompleted -= Completed;
            descriptor.RemoveValueChanged(rig.BodyPath, Changed);
        }
        var evidence = rig.Save("body-rebuilds.json", new
        {
            kind = "actual-visible-wardrobe-body-rebuilds",
            rig.NativeSha256,
            appliedNativeFrames = counts.Count,
            bodyPathDataChangesPerAppliedFrame = counts,
        });
        TestContext.WriteLine(evidence);
        Assert.HasCount(60, counts, "The mechanism gate must observe real applied native frames.");
        Assert.IsTrue(counts.All(count => count == 1),
            $"Each successfully applied wardrobe frame must rebuild the native body path once: counts={string.Join(',', counts.Distinct().Order())}.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("Performance")]
    public void ActualVisibleWardrobeKeepsHeldDAndAWhileRecordingAppliedFrameCadenceAndStageCosts()
    {
        using var rig = new Rig();
        var wasDetailed = rig.Scheduler.DetailedTimingEnabled;
        rig.Scheduler.DetailedTimingEnabled = true;
        PhaseReport[] phases;
        try
        {
            phases =
            [
                Measure(rig, "idle", null, TimeSpan.FromSeconds(2)),
                Measure(rig, "D", Key.D, TimeSpan.FromSeconds(8)),
                Measure(rig, "A", Key.A, TimeSpan.FromSeconds(8)),
            ];
        }
        finally { rig.Scheduler.DetailedTimingEnabled = wasDetailed; }
        var dpi = VisualTreeHelper.GetDpi(rig.Window);
        var evidence = rig.Save("held-movement.json", new
        {
            kind = "actual-visible-wardrobe-held-movement",
            rig.NativeSha256,
            dpiScaleX = dpi.DpiScaleX,
            dpiScaleY = dpi.DpiScaleY,
            renderTier = RenderCapability.Tier >> 16,
            windowWidthDip = rig.Window.ActualWidth,
            windowHeightDip = rig.Window.ActualHeight,
            fixture = "fictional local settings; optional read-only copy of the supplied accessory fixture",
            fpsSource = "successfully applied product frames per Stopwatch wall-clock second; not GPU presentation",
            phases,
        });
        TestContext.WriteLine(evidence);
        foreach (var phase in phases)
        {
            TestContext.WriteLine($"{phase.Name}: {phase.ElapsedSeconds:F3}s, applied FPS {phase.AppliedFps:F3}, native steps {phase.SuccessfulNativeSteps}, X {phase.StartX:F3}->{phase.EndX:F3}, intervals p95 {phase.AppliedIntervals.P95:F3}ms, prepare/loop/apply p95 {phase.Prepare.P95:F3}/{phase.Loop.P95:F3}/{phase.Apply.P95:F3}ms.");
            Assert.IsTrue(phase.ElapsedSeconds >= (phase.Name == "idle" ? 2 : 8), "Continuous movement uses actual elapsed wall time.");
            Assert.IsTrue(phase.AppliedFrames > 0 && phase.SuccessfulNativeSteps > 0);
            Assert.AreEqual(phase.AppliedFrames, phase.ObservedAppliedFrames, "Rendering callbacks and refreshes cannot count as new application frames.");
            Assert.HasCount(0, phase.Failures);
            Assert.AreEqual(phase.FirstCameraY, phase.LastCameraY, 1e-8, "Horizontal walking must retain the fixed ground camera.");
            Assert.IsTrue(phase.CameraYRange <= 1e-8);
            if (phase.Name == "idle") continue;
            Assert.IsTrue(phase.CallbackTrace.All(frame => frame.WindowActive && frame.HomeFocusWithin) &&
                phase.FocusTrace.All(focus => focus.WindowActive && focus.HomeFocusWithin),
                $"Held {phase.Name} was interrupted by loss of the owned test window's activation or Home keyboard focus; preserve the test window in the foreground for a continuous-input measurement.");
            Assert.IsTrue(phase.AppliedFps >= 59,
                $"Held {phase.Name} must retain the existing 59 applied FPS floor: {phase.AppliedFps:F3} FPS.");
            var signedDistance = (phase.EndX - phase.StartX) * (phase.Name == "D" ? 1 : -1);
            Assert.IsTrue(signedDistance > 100, $"Held {phase.Name} must produce sustained actual native movement: {signedDistance:F3} DIP.");
            Assert.IsTrue(phase.RepeatEvents > 0 && phase.MovingAppliedFrames > 0);
            Assert.AreEqual(0, phase.LostHeldIntentFrames, "Native stepping or keyboard repeats must not discard held movement.");
        }
        Assert.AreEqual(DirectionalControlIntent.None, Field<DirectionalControlIntent>(rig.Home, "liveIntent"));
        Assert.AreSame(rig.Scheduler, rig.Runtime.FrameDriver);
        Assert.AreEqual(rig.Entity, rig.Runtime.EntityIds.Single());
    }

    private static PhaseReport Measure(Rig rig, string name, Key? heldKey, TimeSpan duration)
    {
        rig.FocusStage();
        if (heldKey is { } down) Assert.IsTrue(rig.Key(down, true).Handled);
        var start = rig.Runtime.CurrentSelectedCenter!.Value.X;
        var firstCameraY = rig.Camera.CameraY;
        var minimumX = start; var maximumX = start;
        var minimumCameraY = firstCameraY; var maximumCameraY = firstCameraY;
        var before = rig.Scheduler.Snapshot; var previous = before;
        var intervals = new List<double>(); var prepare = new List<double>();
        var loop = new List<double>(); var apply = new List<double>();
        var failures = new List<string>();
        var callbackTrace = new List<CallbackTrace>();
        var focusTrace = new List<FocusTrace>();
        var lastApplied = 0L; var applied = 0L; var movingFrames = 0; var lostIntent = 0; var repeats = 0;
        var previousX = start;
        var pump = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        focusTrace.Add(new(0, "phase-start", rig.Window.IsActive, rig.Home.IsKeyboardFocusWithin));
        var finish = new DispatcherTimer(DispatcherPriority.Normal) { Interval = duration };
        var repeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromSeconds(1d / 30) };
        var repeatMethod = typeof(ProductHomeView).GetMethod("SetLiveDirectionalKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Complete(object? sender, EventArgs args)
        {
            var next = rig.Scheduler.Snapshot;
            var appliedDelta = next.AppliedFrames - previous.AppliedFrames;
            callbackTrace.Add(new(elapsed.Elapsed.TotalSeconds, rig.Window.IsActive, rig.Home.IsKeyboardFocusWithin,
                appliedDelta, rig.Scheduler.LastStageTiming));
            if (appliedDelta == 1)
            {
                applied++;
                if (lastApplied != 0) intervals.Add((next.LastFinishedTimestamp - lastApplied) * 1000d / Stopwatch.Frequency);
                lastApplied = next.LastFinishedTimestamp;
                if (rig.Runtime.LastError is not null || rig.Runtime.LastPhysicsFrame?.Succeeded != true ||
                    rig.Body.LastContourFailure != DangoContourFailure.None || rig.Runtime.AccessoryError is not null)
                    failures.Add("unhealthy applied native/body/accessory frame");
                if (!rig.Home.OwnsLiveFrameDriver || rig.Runtime.EntityIds.Single() != rig.Entity)
                    failures.Add("frame owner or entity changed during continuous movement");
                var x = rig.Runtime.CurrentSelectedCenter!.Value.X;
                minimumX = Math.Min(minimumX, x); maximumX = Math.Max(maximumX, x);
                var cameraY = rig.Camera.CameraY;
                minimumCameraY = Math.Min(minimumCameraY, cameraY); maximumCameraY = Math.Max(maximumCameraY, cameraY);
                if (heldKey is { } key)
                {
                    var intent = Field<DirectionalControlIntent>(rig.Home, "liveIntent");
                    if (key == Key.D ? !intent.Right || intent.Left : !intent.Left || intent.Right) lostIntent++;
                    if ((x - previousX) * (key == Key.D ? 1 : -1) > 1e-6) movingFrames++;
                }
                previousX = x;
                if (rig.Scheduler.LastStageTiming is { } timing)
                {
                    AddDuration(prepare, timing.PrepareStartedTimestamp, timing.PrepareFinishedTimestamp);
                    AddDuration(loop, timing.LoopStartedTimestamp, timing.LoopFinishedTimestamp);
                    AddDuration(apply, timing.ApplyStartedTimestamp, timing.ApplyFinishedTimestamp);
                }
                else failures.Add("missing actual scheduler stage timing");
            }
            else if (appliedDelta is < 0 or > 1) failures.Add("invalid applied-frame counter delta");
            previous = next;
        }
        void Repeat(object? sender, EventArgs args)
        {
            if (heldKey is not { } key) return;
            repeats++;
            var intent = Field<DirectionalControlIntent>(rig.Home, "liveIntent");
            if (!(bool)repeatMethod.Invoke(rig.Home, [key, true, true])! ||
                Field<DirectionalControlIntent>(rig.Home, "liveIntent") != intent)
                failures.Add("autorepeat changed or rejected the already-held intent");
        }
        void Finished(object? sender, EventArgs args)
        {
            // A dispatcher timer can fire slightly before its nominal interval.
            // Stop only after the measured wall-clock duration has really elapsed.
            var remaining = duration - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) { pump.Continue = false; return; }
            finish.Stop(); finish.Interval = remaining; finish.Start();
        }
        void Activated(object? sender, EventArgs args) =>
            focusTrace.Add(new(elapsed.Elapsed.TotalSeconds, "activated", rig.Window.IsActive, rig.Home.IsKeyboardFocusWithin));
        void Deactivated(object? sender, EventArgs args) =>
            focusTrace.Add(new(elapsed.Elapsed.TotalSeconds, "deactivated", rig.Window.IsActive, rig.Home.IsKeyboardFocusWithin));
        void FocusChanged(object sender, DependencyPropertyChangedEventArgs args) =>
            focusTrace.Add(new(elapsed.Elapsed.TotalSeconds, "home-focus-within", rig.Window.IsActive, rig.Home.IsKeyboardFocusWithin));
        try
        {
            rig.Window.Activated += Activated; rig.Window.Deactivated += Deactivated;
            rig.Home.IsKeyboardFocusWithinChanged += FocusChanged;
            rig.Scheduler.CallbackCompleted += Complete;
            finish.Tick += Finished; finish.Start();
            if (heldKey is not null) { repeat.Tick += Repeat; repeat.Start(); }
            Dispatcher.PushFrame(pump);
        }
        finally
        {
            elapsed.Stop(); finish.Stop(); repeat.Stop();
            finish.Tick -= Finished; repeat.Tick -= Repeat;
            rig.Scheduler.CallbackCompleted -= Complete;
            rig.Window.Activated -= Activated; rig.Window.Deactivated -= Deactivated;
            rig.Home.IsKeyboardFocusWithinChanged -= FocusChanged;
            if (heldKey is { } up) rig.Key(up, false);
        }
        var after = rig.Scheduler.Snapshot;
        var camera = rig.Camera;
        if (after.Faults != before.Faults) failures.Add("scheduler fault occurred during measured phase");
        return new(name, elapsed.Elapsed.TotalSeconds,
            after.Callbacks - before.Callbacks, after.AppliedFrames - before.AppliedFrames, applied,
            after.SuccessfulSteps - before.SuccessfulSteps,
            after.OverloadDroppedTicks - before.OverloadDroppedTicks,
            after.PauseDroppedTicks - before.PauseDroppedTicks,
            after.FaultDroppedTicks - before.FaultDroppedTicks,
            start, rig.Runtime.CurrentSelectedCenter!.Value.X, minimumX, maximumX,
            firstCameraY, camera.CameraY, maximumCameraY - minimumCameraY,
            repeats, movingFrames, lostIntent,
            Summary(intervals), Summary(prepare), Summary(loop), Summary(apply), failures, callbackTrace, focusTrace);
    }

    private static void AddDuration(List<double> values, long? start, long? finish)
    {
        if (start is { } first && finish is { } last) values.Add((last - first) * 1000d / Stopwatch.Frequency);
    }

    private static Distribution Summary(List<double> values)
    {
        if (values.Count == 0) return new(0, 0, 0, 0, 0, 0);
        values.Sort();
        return new(values.Count, values[values.Count / 2], values[(int)(values.Count * .95)], values[^1],
            values.Count(value => value > 50), values.Count(value => value > 100));
    }

    private sealed record Distribution(int Count, double Median, double P95, double Maximum, int Over50Milliseconds, int Over100Milliseconds);
    private readonly record struct CallbackTrace(double Seconds, bool WindowActive, bool HomeFocusWithin,
        long AppliedDelta, HostFrameStageTiming? Timing);
    private readonly record struct FocusTrace(double Seconds, string Event, bool WindowActive, bool HomeFocusWithin);
    private sealed record PhaseReport(string Name, double ElapsedSeconds, long RenderingCallbacks, long AppliedFrames,
        long ObservedAppliedFrames, long SuccessfulNativeSteps, long OverloadDroppedTicks, long PauseDroppedTicks,
        long FaultDroppedTicks, double StartX, double EndX, double MinimumX, double MaximumX,
        double FirstCameraY, double LastCameraY, double CameraYRange,
        int RepeatEvents, int MovingAppliedFrames, int LostHeldIntentFrames,
        Distribution AppliedIntervals, Distribution Prepare, Distribution Loop, Distribution Apply, List<string> Failures,
        IReadOnlyList<CallbackTrace> CallbackTrace, IReadOnlyList<FocusTrace> FocusTrace)
    {
        public double AppliedFps => AppliedFrames / ElapsedSeconds;
        public double NativeStepsPerSecond => SuccessfulNativeSteps / ElapsedSeconds;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private sealed class Rig : IDisposable
    {
        private readonly LegacyProductPreviewFixture fixture;
        internal string RunRoot { get; }
        internal string NativeSha256 { get; }
        internal Window Window { get; }
        internal ProductHomeView Home => fixture.Home;
        internal StackedDangoSceneRuntime Runtime { get; }
        internal HostFrameScheduler Scheduler { get; }
        internal DeformableDangoVisual Body { get; }
        internal ShapePath BodyPath { get; }
        internal DangoEntityId Entity { get; }
        internal RoomViewController Camera => Field<RoomViewController>(Home, "accessoryCamera");

        internal Rig()
        {
            RunRoot = Path.Combine(TestRepository.Root, "artifacts", "accessory-held-movement-v4", Guid.NewGuid().ToString("N"));
            var settings = Path.Combine(RunRoot, "settings");
            Directory.CreateDirectory(settings);
            var source = Environment.GetEnvironmentVariable("DANGO_ACCESSORY_MOVEMENT_FIXTURE_ROOT");
            if (!string.IsNullOrWhiteSpace(source))
            {
                foreach (var name in new[] { "settings.json", "appearance.json", "accessories.json" })
                    File.Copy(Path.Combine(source, name), Path.Combine(settings, name), overwrite: false);
            }
            else
            {
                new ControlCenterWindowSettingsStore().Save(Path.Combine(settings, "settings.json"),
                    ControlCenterWindowSettings.CreateDefault() with { IntroSeen = true, DisplayMode = DesktopDisplayMode.Window });
                var session = new AccessorySettingsSession(settings);
                session.Stage(new([
                    new("fictional-held-leaf", "虚构持续移动叶芽", "builtin-sprout", 64, 64)
                    {
                        BodyAnchorX = .228, BodyAnchorY = .756, Softness = 84.46,
                        ConnectionStrength = 0, RotationDamping = 0, LockRotation = false,
                        ReturnStrength = 60, MaxSwingDegrees = 178, LimitSwing = true, GravityInfluence = 20,
                    },
                ]));
                Assert.IsTrue(session.TrySave());
            }
            var native = Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
                Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
            Assert.IsTrue(File.Exists(native), "The low-jump native candidate must be supplied to the actual Home fixture.");
            NativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native)));
            fixture = new(settings, native);
            Runtime = fixture.Runtime;
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            fixture.Show();
            Window = fixture.Window;
            Home.ShowEditor(HomeEditor.Wardrobe);
            Pump(1200);
            Assert.IsTrue(Home.OwnsLiveFrameDriver && Runtime.IsRunning);
            Assert.IsTrue(Runtime.UnboundedHorizontalPreview);
            Assert.AreEqual(.5, Runtime.PreviewJumpSpeedScale);
            Assert.IsNull(Runtime.LastError);
            Entity = Runtime.EntityIds.Single();
            Scheduler = Field<HostFrameScheduler>(Home, "liveScheduler");
            Body = (DeformableDangoVisual)Home.FindName("LiveDangoVisual");
            BodyPath = (ShapePath)Body.FindName("BodyPath");
            Assert.AreEqual(Visibility.Visible, Body.Visibility);
            Assert.AreEqual(DangoContourFailure.None, Body.LastContourFailure);
            Assert.IsTrue(Runtime.AccessoriesEnabled);
            Assert.IsTrue(Runtime.GetAccessoryFrames(Runtime.LastFrame!, 0).Count > 0, "Measured walking must include real equipped accessories.");
            FocusStage();
        }

        internal void FocusStage()
        {
            Window.Activate();
            ((FrameworkElement)Home.FindName("Stage")).Focus();
            Assert.IsTrue(Home.IsKeyboardFocusWithin);
        }

        internal KeyEventArgs Key(Key key, bool down)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window)!, 0, key)
            { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
            Home.RaiseEvent(args);
            return args;
        }

        internal string Save(string name, object value)
        {
            var path = Path.Combine(RunRoot, name);
            File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        public void Dispose() { fixture.Dispose(); }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
