using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Accessories;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass, DoNotParallelize]
public sealed class BeltBodyProductTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public void CandidateRequiresOwnedFictionalSettingsAndSeparateInstance()
    {
        var root = NewRoot();
        Assert.ThrowsExactly<FileNotFoundException>(() => Resolve(root));
        File.WriteAllText(Path.Combine(root, ".dango-belt-body-review.json"), """{"application":"Dango","beltBodyReviewFormat":1,"fictional":true}""");
        Assert.AreEqual(Path.Combine(root, "settings.json"), Resolve(root));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(root).Length);
        Assert.AreNotEqual(App.ResolveSingleInstanceName([]), App.ResolveSingleInstanceName(["--belt-body-review"]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ResolveBeltBodyReviewSettingsFile(["--belt-body-review"]));
        Assert.ThrowsExactly<ArgumentException>(() => App.ValidateIsolatedReviewSelection(["--belt-body-review", "--face-review"]));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango")));
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(AppContext.BaseDirectory));
        File.WriteAllText(Path.Combine(root, ".dango-belt-body-review.json"), """{"application":"Dango","beltBodyReviewFormat":1,"fictional":false}""");
        Assert.ThrowsExactly<InvalidDataException>(() => Resolve(root));
    }

    [STATestMethod]
    public void ActualHostUsesBeltAndDoesNotInitializeOldRoomWorld()
    {
        RequireModule();
        var root = NewRoot();
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), isolatedBeltBodyReview: true);
        var window = (ControlCenterWindow)typeof(DangoApplicationHost).GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        window.ShowOverview(); window.Show(); window.Activate();
        var home = window.ProductHome;
        Wait(() => home.DisplayedBeltSnapshot is not null && home.BoundAppearance?.RequiresBodyPreview == false, 8000, "initial belt pose");
        var old = (StackedDangoSceneRuntime)typeof(DangoApplicationHost).GetField("stackedRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Assert.IsNull(home.BoundRuntime);
        Assert.AreEqual(0, old.EntityIds.Count);
        Assert.AreEqual(DangoBeltWorld.Model, home.DisplayedBeltSnapshot!.ModelVersion);
        Click(home, "EnterRoomButton");
        Assert.IsFalse(home.IsInPorch);
        StringAssert.Contains(Element<Button>(home, "NoticeButton").ToolTip.ToString()!, "尚未接入房间");
        Assert.IsFalse(home.ReadBeltBodySettingsScene().CanRestoreDisplay);
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsFalse(home.IsEditing);
    }

    [STATestMethod]
    public void ActualEditorHotUpdatesAndPreservesBodyAcrossColorEyesSaveCancelHideAndRestart()
    {
        RequireModule();
        var root = NewRoot();
        var session = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
        var home = CreateHome(root, session);
        var window = Show(home);
        try
        {
            WaitReady(home);
            home.ShowEditor(HomeEditor.Face);
            Click(home, "AppearanceBodyTab");
            var before = home.DisplayedBeltSnapshot!;
            var owner = home.BoundBeltBody;
            Set(home, "BodySizeSlider", 120);
            Set(home, "BodySoftnessSlider", 100);
            Set(home, "BodyLandingBuildSlider", 0);
            Assert.IsTrue(session.RequiresBodyPreview);
            Assert.IsFalse(Element<Button>(home, "ApplyButton").IsEnabled);
            Assert.IsFalse(session.TrySave(), "Old displayed pose must not save the new tuple.");
            WaitReady(home);
            var after = home.DisplayedBeltSnapshot!;
            Assert.AreSame(owner, home.BoundBeltBody);
            Assert.AreEqual(before.Epoch, after.Epoch);
            Assert.IsTrue(after.Tick > before.Tick);
            Assert.AreEqual(new DangoBodyControls(120, 100, 0), after.Body);
            Assert.AreEqual(session.BodyConfigurationRevision, after.ConfigurationRevision);
            Click(home, "PeachButton");
            Set(home, "EyeSizeSlider", 120);
            Assert.AreEqual(after.Body, session.DraftAppearance.Body);
            Click(home, "ApplyButton");
            Assert.IsFalse(session.HasPendingSave, session.StatusMessage);
            var saved = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
            Assert.AreEqual(session.DraftAppearance, saved.SavedAppearance);
            var bytes = File.ReadAllBytes(Path.Combine(root, "appearance.json"));
            Set(home, "BodySizeSlider", 80);
            Click(home, "CancelAppearanceButton");
            WaitReady(home);
            Assert.AreEqual(saved.SavedAppearance, session.DraftAppearance);
            Assert.AreEqual(saved.SavedAppearance.Body, home.DisplayedBeltSnapshot!.Body);
            Assert.IsGreaterThan(after.BodyEpoch, home.DisplayedBeltSnapshot.BodyEpoch);
            home.ShowEditor(HomeEditor.Face);
            Set(home, "BodySoftnessSlider", 0);
            Click(home, "DefaultAppearanceButton");
            WaitReady(home);
            Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
            window.Hide(); Pump(100);
            Assert.AreEqual(saved.SavedAppearance, session.DraftAppearance);
            Assert.IsFalse(home.BoundBeltBody!.Metrics.Running);
            window.Show(); window.Activate(); WaitReady(home);
            Assert.AreEqual(saved.SavedAppearance.Body, home.DisplayedBeltSnapshot!.Body);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(root, "appearance.json")));
            home.UnbindBeltBody();
            window.Close();
            var second = CreateHome(root, new AppearanceSettingsSession(Path.Combine(root, "appearance.json")));
            var restarted = Show(second);
            try { WaitReady(second); Assert.AreEqual(saved.SavedAppearance, second.DraftAppearance); Assert.AreEqual(saved.SavedAppearance.Body, second.DisplayedBeltSnapshot!.Body); }
            finally { second.UnbindBeltBody(); restarted.Close(); }
        }
        finally { home.UnbindBeltBody(); window.Close(); }
    }

    [STATestMethod]
    [DataRow(640, 360)]
    [DataRow(960, 540)]
    [DataRow(736, 560)]
    [DataRow(480, 800)]
    [DataRow(336, 540)]
    [DataRow(1280, 720)]
    public void RealWindowBodyEditorKeepsSizeFloorAndFixedSaveControls(int width, int height)
    {
        RequireModule();
        var root = NewRoot(); var home = CreateHome(root, new(Path.Combine(root, "appearance.json")));
        var window = Show(home, width, height, activate: false);
        try
        {
            WaitReady(home); var previewWidth = Element<Viewbox>(home, "PetPreview").Width;
            home.ShowEditor(HomeEditor.Face); Click(home, "AppearanceBodyTab"); Pump(100);
            Set(home, "BodySizeSlider", 120); Set(home, "BodySoftnessSlider", 100); Set(home, "BodyLandingBuildSlider", 0); WaitReady(home); Pump(200);
            var outer = Element<ScrollViewer>(home, "HomeContent");
            if (width > height)
            {
                Assert.IsLessThanOrEqualTo(.5, outer.ScrollableHeight,
                    $"{width}x{height}: stage={Element<Grid>(home, "Stage").ActualHeight}, viewport={outer.ViewportHeight}; body editing must fit the page.");
                Assert.IsLessThanOrEqualTo(.5, Element<ScrollViewer>(home, "OptionsBody").ScrollableHeight,
                    "All three body parameters must fit without a second scrollbar.");
            }
            foreach (var name in new[] { "AppearanceBodyTab", "ApplyButton", "CancelAppearanceButton", "DefaultAppearanceButton" })
            {
                var b = Bounds(home, name);
                Assert.IsTrue(b.Left >= 0 && b.Top >= 0 && b.Right <= home.ActualWidth + 1 && b.Bottom <= home.ActualHeight + 1, $"{width}x{height} {name}: {b}");
                var button = Element<Button>(home, name);
                foreach (var p in new[] { new Point(b.Left + 4, b.Top + b.Height / 2), new Point(b.Right - 4, b.Top + b.Height / 2) })
                {
                    var hit = home.InputHitTest(p) as DependencyObject;
                    Assert.IsTrue(ReferenceEquals(hit, button) || hit is not null && button.IsAncestorOf(hit),
                        $"{name} at {p} must remain clickable through ancestor layout clips in {width}x{height}.");
                }
            }
            Assert.AreEqual(previewWidth, Element<Viewbox>(home, "PetPreview").Width, .01, "Keep the existing product preview size through editor changes.");
            var body = home.BoundBeltPresenter!.Inspect().Body!;
            Assert.AreEqual(256, body.Contour.Count);
            var apply = Bounds(home, "ApplyButton");
            Element<ScrollViewer>(home, "OptionsBody").ScrollToEnd(); Pump(40);
            Assert.AreEqual(apply, Bounds(home, "ApplyButton"));
            SaveImage(home, $"body-{width}x{height}.png");
            Click(home, "AppearanceEyesTab"); Pump(40);
            Set(home, "EyeSizeSlider", 85);
            Assert.AreEqual(85d, home.DraftAppearance.EyeLayout.SizePercent);
            Assert.AreEqual(new DangoBodyControls(120, 100, 0), home.DraftAppearance.Body);
            if (width > height) Assert.IsLessThanOrEqualTo(.5, outer.ScrollableHeight, "Changing to eye controls must not reintroduce page scrolling.");
            Pump(40); home.UpdateLayout();
            SaveImage(home, $"eyes-{width}x{height}.png");
        }
        finally { home.UnbindBeltBody(); window.Close(); }
    }

    [STATestMethod]
    public void FocusedOsKeysAndMouseReachNewBodyAndReleaseOnFocusLoss()
    {
        RequireModule();
        var root = NewRoot(); var home = CreateHome(root, new(Path.Combine(root, "appearance.json"))); var window = Show(home);
        var sibling = new Window { Width = 220, Height = 100, Left = 30, Top = 30, Title = "Dango fictional focus test" };
        var trace = new List<string>();
        home.PreviewKeyDown += (_, e) => trace.Add($"Down:{e.Key}/{e.ImeProcessedKey}/{e.SystemKey},mods:{Keyboard.Modifiers},focus:{Keyboard.FocusedElement?.GetType().Name},handled:{e.Handled}");
        home.PreviewKeyUp += (_, e) => trace.Add($"Up:{e.Key},mods:{Keyboard.Modifiers}");
        try
        {
            WaitReady(home); home.ShowEditor(HomeEditor.Face); Click(home, "AppearanceBodyTab");
            window.Activate(); Element<Slider>(home, "BodySizeSlider").Focus(); Pump(60);
            var hwnd = new WindowInteropHelper(window).Handle;
            FocusOwnedWindow(window);
            Element<Slider>(home, "BodySizeSlider").Focus(); Pump(30);
            Assert.IsTrue(Element<Slider>(home, "BodySizeSlider").IsKeyboardFocused);
            Assert.AreEqual(ModifierKeys.None, Keyboard.Modifiers);
            KeyDown(0x44); // D while slider owns keyboard focus.
            Wait(() => home.DisplayedBeltSnapshot?.ActualInput.Horizontal > 0, 2000, "OS D reaches body with slider focus");
            KeyDown(0x41);
            Wait(() => home.DisplayedBeltSnapshot is { ActualInput: { Horizontal: 0, InputActive: true } }, 2000, "opposing keys remain active");
            KeyUp(0x41); KeyUp(0x44); Pump(80);
            KeyDown(0x57);
            Wait(() => home.DisplayedBeltSnapshot?.ActualInput.Stretch > 0, 2000, "OS W stretch");
            KeyDown(0x53);
            Wait(() => home.DisplayedBeltSnapshot is { ActualInput: { Stretch: 0, Downward: 0, CancelForces: true, InputActive: true } }, 2000, "opposing W/S cancellation");
            KeyUp(0x57); KeyUp(0x53); Pump(80);
            KeyDown(0x44); Pump(80);
            sibling.Show(); FocusOwnedWindow(sibling); Pump(80);
            KeyUp(0x44); Pump(40);
            FocusOwnedWindow(window); Element<FrameworkElement>(home, "Stage").Focus();
            Wait(() => home.DisplayedBeltSnapshot is { ActualInput: { InputActive: false, Horizontal: 0 } }, 2000, "focus loss cancels held movement");
            KeyDown(0x44); Wait(() => home.DisplayedBeltSnapshot?.ActualInput.Horizontal > 0, 2000, "released-outside D recovers"); KeyUp(0x44);
            var point = Element<Viewbox>(home, "PetPreview").PointToScreen(new(120, 110));
            SetCursorPos((int)point.X, (int)point.Y); Mouse(0x0002);
            Wait(() => home.DisplayedBeltSnapshot?.ActualInput.DragActive == true, 2000, "actual contour mouse hit");
            SetCursorPos((int)point.X + 20, (int)point.Y + 10); Pump(100); Mouse(0x0004);
            Wait(() => home.DisplayedBeltSnapshot?.ActualInput.DragActive == false, 2000, "mouse release");
            Assert.IsFalse(home.BoundBeltBody!.Metrics.Error is not null, home.ReadBeltBodySettingsScene().Diagnostics);
        }
        finally
        {
            foreach (var key in new byte[] { 0x41, 0x44, 0x57, 0x53, 0x20 }) KeyUp(key);
            TestContext.WriteLine(string.Join("\n", trace));
            TestContext.WriteLine(home.ReadBeltBodySettingsScene().Diagnostics);
            Mouse(0x0004); sibling.Close(); home.UnbindBeltBody(); window.Close();
        }
    }

    [STATestMethod]
    [DataRow(50d, 50d)]
    [DataRow(100d, 0d)]
    public void ActualSliderDragAppliesBeforeReleaseAndMeetsFrozenPresentationTargets(double softness, double build)
    {
        RequireModule();
        var root = NewRoot(); var session = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
        var home = CreateHome(root, session); var window = Show(home, 960, 640);
        var requests = new Dictionary<long, long>(); var latencies = new List<double>();
        var confirmations = new List<(long Stamp, long Revision)>(); var heldConfirmations = 0; var held = false;
        home.BeltBodyDisplayConfirmed += snapshot =>
        {
            confirmations.Add((Stopwatch.GetTimestamp(), snapshot.ConfigurationRevision));
            if (requests.TryGetValue(snapshot.ConfigurationRevision, out var requested))
            {
                latencies.Add(Stopwatch.GetElapsedTime(requested).TotalMilliseconds); requests.Remove(snapshot.ConfigurationRevision);
                if (held) heldConfirmations++;
            }
        };
        try
        {
            WaitReady(home); home.BoundBeltBody!.SleepEnabled = false;
            home.ShowEditor(HomeEditor.Face); Click(home, "AppearanceBodyTab");
            Set(home, "BodySoftnessSlider", softness); Set(home, "BodyLandingBuildSlider", build); Set(home, "BodySizeSlider", 120);
            WaitReady(home); Pump(250);
            FocusOwnedWindow(window);
            var slider = Element<Slider>(home, "BodySizeSlider"); slider.ApplyTemplate();
            var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider)!;
            var start = track.Thumb.PointToScreen(new(track.Thumb.ActualWidth / 2, track.Thumb.ActualHeight / 2));
            slider.ValueChanged += (_, _) => requests[session.BodyConfigurationRevision] = Stopwatch.GetTimestamp();
            SetCursorPos((int)start.X, (int)start.Y); Mouse(0x0002); held = true; Pump(30);
            Assert.IsTrue(slider.IsMouseCaptureWithin, "OS pointer must capture the real Slider thumb.");
            var travel = (track.ActualWidth - track.Thumb.ActualWidth) * VisualTreeHelper.GetDpi(slider).DpiScaleX;
            for (var i = 1; i <= 30; i++)
            {
                SetCursorPos((int)(start.X - travel * i / 30), (int)start.Y); Pump(24);
            }
            Assert.IsGreaterThan(5, heldConfirmations, "Native/config/display must advance continuously while the mouse button is held.");
            var released = Stopwatch.GetTimestamp(); Mouse(0x0004); held = false; WaitReady(home);
            var finalMs = Stopwatch.GetElapsedTime(released).TotalMilliseconds;
            Assert.AreEqual(slider.Value, home.DisplayedBeltSnapshot!.Body.SizePercent, .01);
            Assert.IsGreaterThan(20, latencies.Count);
            var p95 = latencies.Order().ElementAt((int)Math.Floor((latencies.Count - 1) * .95));
            Assert.IsLessThanOrEqualTo(150d, p95, "Frozen request-to-successful-WPF-confirmation P95 target.");
            Assert.IsLessThanOrEqualTo(250d, finalMs, "Frozen final release-to-display target.");
            // Measure a separate steady window at the demanding 120% endpoint, with full eyes/accessories.
            Set(home, "BodySizeSlider", 120); WaitReady(home); Pump(250);
            confirmations.Clear(); var watch = Stopwatch.StartNew(); Pump(4000); watch.Stop();
            var freshPerSecond = confirmations.Count / watch.Elapsed.TotalSeconds;
            var receipt = new { softness, build, p95Milliseconds = p95, releaseFinalMilliseconds = finalMs,
                visibleNativeFramesPerSecond = freshPerSecond, frames = confirmations.Count, seconds = watch.Elapsed.TotalSeconds,
                heldConfirmations, latenciesMilliseconds = latencies, metrics = home.BoundBeltBody.Metrics };
            var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
            TestContext.WriteLine(json); using (var file = TestEvidence.Create("belt-product", $"performance-{softness}-{build}.json")) JsonSerializer.Serialize(file, receipt);
            Assert.IsGreaterThanOrEqualTo(57d, freshPerSecond, "Frozen steady successful physical-frame presentation target; not GPU present.");
            Assert.IsNull(home.BoundBeltBody.Metrics.Error);
        }
        finally { Mouse(0x0004); home.UnbindBeltBody(); window.Close(); }
    }

    [STATestMethod, TestCategory("BeltLongRunning")]
    [DataRow("natural")]
    [DataRow("walk-stop")]
    [DataRow("drag-release")]
    public void ActualProductVisibleV2SleepSavesCpuAndKeepsAppearanceAlive(string condition)
    {
        RequireModule();
        if (Environment.GetEnvironmentVariable("DANGO_BELT_LONG_CHECK") != "1") Assert.Inconclusive("Explicit 10-second product CPU comparison requires DANGO_BELT_LONG_CHECK=1.");
        var root = NewRoot();
        new AccessorySettingsStore().Save(root, new([new AccessoryItem("fictional-sleep-hat", "Fictional sleep hat", "builtin-sprout", 64, 64)]));
        var startup = Stopwatch.StartNew();
        var home = CreateHome(root, new(Path.Combine(root, "appearance.json"))); var window = Show(home, 1280, 720);
        var outlines = new Queue<(int Tick, Point[] Points)>();
        var confirmedTick = -1;
        home.BeltBodyDisplayConfirmed += snapshot => confirmedTick = snapshot.Tick;
        home.BeltBodyPoseApplied += snapshot =>
        {
            var presenter = home.BoundBeltPresenter!;
            var contour = presenter.Inspect().Body!.Contour;
            var layer = (Canvas)presenter.View.Children[0];
            var transform = ((Visual)presenter.View.Parent).TransformToAncestor(home);
            // Canvas.Top is queued for layout here; compose its actual set value explicitly so
            // both poses in a catch-up callback are measured, including the intermediate extreme.
            var top = Canvas.GetTop(presenter.View);
            outlines.Enqueue((snapshot.Tick, contour.Select(point =>
            {
                var local = layer.RenderTransform.Transform(new Point(80 + point.X * BeltBodyAppearancePresenter.WorldToCanvas,
                    66 - point.Y * BeltBodyAppearancePresenter.WorldToCanvas));
                return transform.Transform(new Point(local.X, local.Y + top));
            }).ToArray()));
            if (outlines.Count > 121) outlines.Dequeue();
        };
        try
        {
            WaitReady(home); home.ShowEditor(HomeEditor.Face);
            var runtime = home.BoundBeltBody!;
            Assert.AreEqual(420d, Element<Viewbox>(home, "PetPreview").ActualWidth, .01, "Validate the maximum contracted product preview.");
            runtime.SleepEnabled = true;
            var enabled = Stopwatch.StartNew();
            if (condition != "natural") Pump(2000);
            if (condition == "walk-stop") { runtime.PublishInput(new(Horizontal: 1, InputActive: true)); Pump(800); runtime.ClearInput(); }
            if (condition == "drag-release")
            {
                var f = home.DisplayedBeltSnapshot!.Frame!;
                runtime.PublishInput(new(DragActive: true, DragTargetX: f.CoreX + .04, DragTargetY: f.CoreY - .04, InputActive: true));
                Pump(800); runtime.ClearInput();
            }
            var released = Stopwatch.StartNew();
            var recoverySeconds = int.TryParse(Environment.GetEnvironmentVariable("DANGO_BELT_RECOVERY_SECONDS"), out var seconds) ? seconds : 60;
            Assert.AreEqual(60, recoverySeconds, "The independent v2 recovery observation is frozen before measurement.");
            Wait(() => runtime.Metrics.Sleeping || runtime.Metrics.Error is not null, recoverySeconds * 1000, condition + " sleep eligibility");
            var enabledToSleepMilliseconds = enabled.Elapsed.TotalMilliseconds;
            var releaseToSleepMilliseconds = released.Elapsed.TotalMilliseconds;
            var startupToSleepMilliseconds = startup.Elapsed.TotalMilliseconds;
            Assert.IsNull(runtime.Metrics.Error, home.ReadBeltBodySettingsScene().Diagnostics);
            Pump(100); // Consume the final real physical snapshots before sampling the sleeping period.
            var terminalTick = runtime.Metrics.NativeStepCalls;
            Wait(() => confirmedTick == terminalTick && runtime.Metrics.QueueDepth == 0, 2000, "final sleeping pose successfully display-confirmed");
            Assert.AreEqual(121, outlines.Count, "Observe every one of the final 120 physical intervals.");
            var rows = outlines.ToArray();
            var outlineTickSpan = rows[^1].Tick - rows[0].Tick;
            Assert.AreEqual(120, outlineTickSpan);
            Assert.IsTrue(rows.Zip(rows.Skip(1)).All(pair => pair.Second.Tick == pair.First.Tick + 1), "Catch-up must feed both complete successful poses to the measured appearance pipeline.");
            Assert.AreEqual(terminalTick, (long)rows[^1].Tick);
            var maximumOutlineWindowDip = Enumerable.Range(0, 256).Max(i =>
            {
                var dx = rows.Max(row => row.Points[i].X) - rows.Min(row => row.Points[i].X);
                var dy = rows.Max(row => row.Points[i].Y) - rows.Min(row => row.Points[i].Y);
                return Math.Sqrt(dx * dx + dy * dy);
            });
            TestContext.WriteLine(JsonSerializer.Serialize(new { condition, enabledToSleepMilliseconds, releaseToSleepMilliseconds, startupToSleepMilliseconds, maximumOutlineWindowDip, outlineTickSpan }));
            Assert.IsLessThanOrEqualTo(.1, maximumOutlineWindowDip, "The actual camera, gait and Viewbox transforms must preserve the visible 2D budget.");
            var stateBefore = runtime.CaptureStateAsync(); Wait(() => stateBefore.IsCompleted, 2000, "sleep state snapshot");
            var visualBefore = home.BoundBeltPresenter!.Inspect(); var physicsTick = home.DisplayedBeltSnapshot!.Tick;
            Click(home, "PreviewBlinkButton");
            Assert.IsTrue(runtime.Metrics.Sleeping, "An eye comparison may animate without waking a fully quiet body.");
            var sleeping = SampleProductCpu(home, 10000);
            var stateAfter = runtime.CaptureStateAsync(); Wait(() => stateAfter.IsCompleted, 2000, "sleep state after comparison");
            CollectionAssert.AreEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(stateBefore.Result.State.AsSpan()).ToArray(),
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(stateAfter.Result.State.AsSpan()).ToArray(), "Sleep must preserve every native state byte.");
            Assert.AreEqual(physicsTick, home.DisplayedBeltSnapshot!.Tick);
            var visualAfter = home.BoundBeltPresenter.Inspect();
            Assert.IsGreaterThan(visualBefore.Tick, visualAfter.Tick);
            Assert.AreSame(visualBefore.Shape, visualAfter.Shape, "Sleeping body geometry is reused while eyes and accessories advance.");
            Assert.AreEqual(1, visualAfter.Accessories.Count);
            Assert.IsGreaterThan(visualBefore.Accessories.Single().Frame.StepIndex, visualAfter.Accessories.Single().Frame.StepIndex);
            // Same configuration and comparison animation, ten seconds each; the body resumes from its frozen state.
            runtime.SleepEnabled = false;
            Click(home, "PreviewBlinkButton");
            var baseline = SampleProductCpu(home, 10000);
            Assert.IsGreaterThanOrEqualTo(500L, baseline.NativeCalls, "The off side must really keep solving.");
            runtime.SleepEnabled = true;
            Wait(() => runtime.Metrics.Sleeping, 60000, "v2 sleep after off-side comparison");
            Pump(100);
            physicsTick = home.DisplayedBeltSnapshot!.Tick;
            var cpuReduction = 1 - (sleeping.CpuMilliseconds / sleeping.Seconds) / (baseline.CpuMilliseconds / baseline.Seconds);
            var callsReduction = 1 - sleeping.NativeCalls / (double)baseline.NativeCalls;
            Assert.IsGreaterThanOrEqualTo(.30, cpuReduction, "Frozen whole product process CPU reduction gate.");
            Assert.IsGreaterThanOrEqualTo(.99, callsReduction, "Frozen solver-call reduction gate.");
            // Opposing keys are an active occupation and must wake, with no synthetic catch-up.
            FocusOwnedWindow(window); Element<FrameworkElement>(home, "Stage").Focus();
            var wakeConfirmed = false;
            void OnWake(Dango.Desktop.Physics.Belt.BeltBodySnapshot snapshot)
            {
                if (snapshot.Tick > physicsTick && snapshot.ActualInput is { InputActive: true, Horizontal: 0 }) wakeConfirmed = true;
            }
            home.BeltBodyDisplayConfirmed += OnWake;
            var wake = Stopwatch.StartNew(); KeyDown(0x41); KeyDown(0x44);
            Wait(() => wakeConfirmed, 2000, "successful display confirmation contains opposing-key occupation");
            home.BeltBodyDisplayConfirmed -= OnWake;
            wake.Stop(); Assert.IsLessThanOrEqualTo(100d, wake.Elapsed.TotalMilliseconds, "Frozen wake to successful displayed body gate.");
            var awakeTick = home.DisplayedBeltSnapshot!.Tick; Pump(250);
            Assert.IsFalse(runtime.Metrics.Sleeping);
            Assert.IsLessThan(30, home.DisplayedBeltSnapshot!.Tick - awakeTick, "Ten seconds of sleeping wall time must not be replayed.");
            Assert.IsTrue(home.DisplayedBeltSnapshot!.ActualInput.InputActive);
            Assert.AreEqual(0d, home.DisplayedBeltSnapshot!.ActualInput.Horizontal);
            var receipt = new { contract = "visible-deformation-v2", condition, recoveryObservationSeconds = recoverySeconds,
                enabledToSleepMilliseconds, releaseToSleepMilliseconds, startupToSleepMilliseconds, maximumOutlineWindowDip, outlineTickSpan,
                wakeToDisplayMilliseconds = wake.Elapsed.TotalMilliseconds, baseline, sleeping, cpuReduction, callsReduction,
                physicsTick, presentationTicks = visualAfter.Tick - visualBefore.Tick };
            TestContext.WriteLine(JsonSerializer.Serialize(receipt));
            using (var file = TestEvidence.Create("belt-product-visible-v2", "sleep-" + condition + ".json")) JsonSerializer.Serialize(file, receipt);
            KeyUp(0x41); KeyUp(0x44); runtime.ClearInput();
        }
        finally { KeyUp(0x41); KeyUp(0x44); home.UnbindBeltBody(); window.Close(); }
    }

    [STATestMethod]
    public void VisibleV2SleepingProductHotEditCancelDefaultHideAndCloseRespectLifecycle()
    {
        RequireModule();
        var root = NewRoot(); var session = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
        var home = CreateHome(root, session); var window = Show(home);
        try
        {
            WaitReady(home); home.ShowEditor(HomeEditor.Face); Click(home, "AppearanceBodyTab");
            var runtime = home.BoundBeltBody!; runtime.SleepEnabled = true;
            WaitSleep();
            var original = home.DisplayedBeltSnapshot!;
            Set(home, "BodySizeSlider", 80); Set(home, "BodySoftnessSlider", 0); Set(home, "BodyLandingBuildSlider", 0);
            Assert.IsTrue(session.RequiresBodyPreview);
            WaitReady(home);
            Assert.AreSame(runtime, home.BoundBeltBody);
            Assert.AreEqual(original.Epoch, home.DisplayedBeltSnapshot!.Epoch);
            Assert.AreEqual(new DangoBodyControls(80, 0, 0), home.DisplayedBeltSnapshot!.Body);
            WaitSleep();
            Click(home, "CancelAppearanceButton"); WaitReady(home);
            Assert.AreEqual(DangoBodyControls.Default, home.DisplayedBeltSnapshot!.Body);
            Assert.IsGreaterThan(original.Epoch, home.DisplayedBeltSnapshot!.Epoch);
            WaitSleep();
            home.ShowEditor(HomeEditor.Face); Click(home, "AppearanceBodyTab");
            var restoredRevision = home.DisplayedBeltSnapshot!.ConfigurationRevision;
            Set(home, "BodySizeSlider", 80); Set(home, "BodySoftnessSlider", 0); Set(home, "BodyLandingBuildSlider", 0);
            WaitReady(home); WaitSleep();
            Assert.AreEqual(new DangoBodyControls(80, 0, 0), home.DisplayedBeltSnapshot!.Body);
            Assert.IsGreaterThan(restoredRevision, home.DisplayedBeltSnapshot.ConfigurationRevision);
            Click(home, "DefaultAppearanceButton"); WaitReady(home);
            Assert.AreEqual(DangoAppearanceConfiguration.Default, session.DraftAppearance);
            Assert.AreEqual(DangoBodyControls.Default, home.DisplayedBeltSnapshot!.Body);
            WaitSleep();
            window.Hide(); Pump(100); Assert.IsFalse(runtime.Metrics.Running);
            window.Show(); window.Activate(); WaitReady(home); WaitSleep();
            var close = Stopwatch.StartNew(); home.UnbindBeltBody(); close.Stop();
            Assert.IsLessThanOrEqualTo(2000d, close.Elapsed.TotalMilliseconds, "Sleeping owner must close through an event barrier.");
            Assert.IsFalse(File.Exists(Path.Combine(root, "appearance.json")), "Neither cancel, default, hide nor close applies the draft to disk.");

            void WaitSleep()
            {
                Wait(() => runtime.Metrics.Sleeping || runtime.Metrics.Error is not null, 60000, "v2 lifecycle sleep");
                Assert.IsNull(runtime.Metrics.Error); Pump(100);
            }
        }
        finally { home.UnbindBeltBody(); window.Close(); }
    }

    [STATestMethod]
    public void AsyncFailureWithoutSynchronizationContextReportsOnOwningDispatcher()
    {
        RequireModule();
        var root = NewRoot(); var home = CreateHome(root, new(Path.Combine(root, "appearance.json"))); var window = Show(home);
        try
        {
            WaitReady(home);
            var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                typeof(ProductHomeView).GetMethod("ObserveBeltOperation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(home, [failure.Task]);
            }
            finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
            Task.Run(() => failure.SetException(new InvalidOperationException("Fictional asynchronous body failure"))).GetAwaiter().GetResult();
            Wait(() => home.ReadBeltBodySettingsScene().CanReinitialize, 2000, "asynchronous failure reaches the actual UI owner");
            StringAssert.Contains(Element<TextBlock>(home, "LiveSceneStatusText").Text, "重新初始化");
        }
        finally { home.UnbindBeltBody(); window.Close(); }
    }

    [STATestMethod]
    [DataRow(60)]
    [DataRow(120)]
    public void AppearanceClockUsesOneWallSecondAcrossPhysicalCatchUpAndEmptyRenders(int refreshRate)
    {
        RequireModule();
        var root = NewRoot(); var home = CreateHome(root, new(Path.Combine(root, "appearance.json"))); var window = Show(home);
        try
        {
            WaitReady(home);
            var template = home.DisplayedBeltSnapshot!;
            using var native = new BeltNativeSession(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll"), template.Body);
            var start = home.BeltBodyPresentationSeconds;
            var sequence = template.Sequence + 10000;
            // A synthetic render schedule consumes real independent native snapshots. No WPF
            // dispatcher pumping occurs inside this schedule, so real Rendering cannot add time.
            for (var render = 0; render < refreshRate; render++)
            {
                var count = refreshRate == 120 ? render % 4 == 0 ? 2 : 0 : render % 4 == 0 ? 2 : render % 4 == 1 ? 0 : 1;
                var batch = new List<BeltBodySnapshot>();
                for (var pose = 0; pose < count; pose++)
                {
                    var step = native.Step(default); Assert.IsNull(step.Error);
                    batch.Add(template with { Sequence = ++sequence, Tick = (int)sequence, Frame = step.Frame, DebugState = step.State });
                }
                home.PresentBeltBodyBatch(batch, 1d / refreshRate);
            }
            Assert.AreEqual(1d, home.BeltBodyPresentationSeconds - start, 1e-10,
                "Complete physical poses and empty renders must share one wall-clock budget.");
            Assert.IsNull(home.BoundBeltPresenter!.RuntimeError);
        }
        finally { home.UnbindBeltBody(); window.Close(); }
    }

    private sealed record CpuSample(double Seconds, double CpuMilliseconds, long NativeCalls, double NativeMilliseconds,
        long AllocatedBytes, int Gen0, int Gen1, int Gen2, int QueueDepth, long InvalidatedSnapshots);
    private static CpuSample SampleProductCpu(ProductHomeView home, int ms)
    {
        using var process = Process.GetCurrentProcess(); var before = home.BoundBeltBody!.Metrics;
        var allocations = GC.GetTotalAllocatedBytes(); var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
        var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        do { Pump(Math.Max(1, ms - (int)watch.ElapsedMilliseconds)); } while (watch.Elapsed.TotalMilliseconds < ms);
        watch.Stop(); process.Refresh();
        var after = home.BoundBeltBody.Metrics;
        return new(watch.Elapsed.TotalSeconds, (process.TotalProcessorTime - cpu).TotalMilliseconds, after.NativeStepCalls - before.NativeStepCalls,
            after.NativeStepMilliseconds - before.NativeStepMilliseconds, GC.GetTotalAllocatedBytes() - allocations,
            GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2, after.QueueDepth, after.InvalidatedSnapshots);
    }

    private static void RequireModule()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll"))) Assert.Inconclusive("Build with DangoBeltModulePath pointing to the independent product native.");
    }
    private static ProductHomeView CreateHome(string root, AppearanceSettingsSession appearance)
    {
        var home = new ProductHomeView { ReduceMotion = true }; home.BindAppearance(appearance);
        home.BindBeltBody(Path.Combine(root, "settings.json")); return home;
    }
    private static Window Show(ProductHomeView home, int width = 960, int height = 640, bool activate = true)
    {
        var window = new Window { Content = home, Width = width, Height = height, Left = activate ? 80 : -10000, Top = 40,
            ShowActivated = activate, ShowInTaskbar = activate, Title = "Dango product body verification" };
        window.Show(); if (activate) window.Activate(); Pump(60); return window;
    }
    private static string NewRoot()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "belt-product", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root;
    }
    private static string? Resolve(string root) => App.ResolveBeltBodyReviewSettingsFile(["--belt-body-review", "--belt-body-review-settings", root]);
    private static T Element<T>(ProductHomeView home, string name) => (T)home.FindName(name);
    private static void Click(ProductHomeView home, string name) => Element<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Set(ProductHomeView home, string name, double value) => Element<Slider>(home, name).Value = value;
    private static Rect Bounds(ProductHomeView home, string name) { var e = Element<FrameworkElement>(home, name); return e.TransformToAncestor(home).TransformBounds(new Rect(e.RenderSize)); }
    private static void WaitReady(ProductHomeView home) => Wait(() => home.BoundAppearance?.RequiresBodyPreview == false && home.DisplayedBeltSnapshot is not null, 8000, "latest body display: " + home.ReadBeltBodySettingsScene().Diagnostics);
    private static void Wait(Func<bool> ready, int timeout, string message)
    {
        var watch = Stopwatch.StartNew(); while (!ready() && watch.ElapsedMilliseconds < timeout) Pump(15); Assert.IsTrue(ready(), message);
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void SaveImage(ProductHomeView home, string name)
    {
        var image = new RenderTargetBitmap((int)home.ActualWidth, (int)home.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(home);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = TestEvidence.Create("belt-product", name); png.Save(file);
    }
    private static void KeyDown(byte key) => SendKey(key, 0);
    private static void KeyUp(byte key) => SendKey(key, 2);
    private static void SendKey(ushort key, uint flags)
    {
        var input = new NativeInput { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = flags } } };
        Assert.AreEqual(1u, SendInput(1, [input], Marshal.SizeOf<NativeInput>()), "Win32 must accept the actual key injection.");
    }
    private static void Mouse(uint flags) => mouse_event(flags, 0, 0, 0, 0);
    private static void FocusOwnedWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        window.Activate(); _ = SetForegroundWindow(hwnd); Pump(40);
        if (GetForegroundWindow() != hwnd)
        {
            // Give this explicitly authorized test process an OS input event before activation.
            KeyDown(0x12); KeyUp(0x12); _ = SetForegroundWindow(hwnd); window.Activate(); Pump(60);
        }
        Assert.AreEqual(hwnd, GetForegroundWindow(), "Real OS focus must belong to this test window before injecting gameplay input.");
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public NativeKeyboard Keyboard;
        [FieldOffset(0)] public NativeMouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeKeyboard { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
}
