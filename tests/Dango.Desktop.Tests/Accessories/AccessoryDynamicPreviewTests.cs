using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Belt;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Tests.Physics.Belt;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryDynamicPreviewTests
{
    [STATestMethod]
    public void PausedResetPresentsANewRealNativePoseWithoutAWindowOrResumingTheOwner()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "dango_belt_product.dll")))
            Assert.Inconclusive("The product native module is required for this detached-view test.");
        var root = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "dynamic-preview", Guid.NewGuid().ToString("N"));
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(new AppearanceSettingsSession(Path.Combine(root, "appearance.json")));
        home.BindBeltBody(Path.Combine(root, "settings.json"));
        using var preview = home.CreateAccessoryDynamicPreview();
        try
        {
            home.Measure(new Size(960, 640)); home.Arrange(new Rect(0, 0, 960, 640)); home.UpdateLayout();
            var runtime = home.BoundBeltBody!;
            Wait(() => runtime.Metrics.ConfigurationRevision >= 0);
            preview.SetVisible(true); preview.Command("pause");
            var before = runtime.Metrics.NativeStepCalls;
            preview.Command("reset");
            Wait(() => home.DisplayedBeltSnapshot?.Epoch == runtime.Epoch);
            home.UpdateLayout();
            Assert.IsNotNull(home.DisplayedBeltSnapshot?.Frame);
            Assert.AreEqual(before + 1, runtime.Metrics.NativeStepCalls);
            Assert.IsFalse(runtime.Metrics.Running);
            var image = preview.Capture();
            Assert.IsNotNull(image, "A paused reset must have a capturable real pose before Resume is pressed.");
            using var stream = new MemoryStream(image);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[288 * 160 * 4]; bitmap.CopyPixels(pixels, 288 * 4, 0);
            Assert.IsTrue(Enumerable.Range(0, 288 * 160).Any(index => pixels[index * 4 + 3] != 0), "The real body must occupy the captured image.");
            Assert.IsTrue(home.BoundBeltPresenter!.Paused);
            var still = System.Diagnostics.Stopwatch.StartNew();
            Wait(() => still.ElapsedMilliseconds >= 50);
            Assert.IsNotNull(preview.Capture());
            Assert.AreEqual(before + 1, runtime.Metrics.NativeStepCalls, "Neither paused capture nor reset starts a second physics loop.");
            Assert.IsFalse(File.Exists(Path.Combine(root, "appearance.json")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "settings.json")));
        }
        finally { preview.Dispose(); home.UnbindBeltBody(); }

        static void Wait(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 3000)
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
            }
            Assert.IsTrue(condition(), "The detached preview did not finish its initial reset presentation.");
        }
    }

    [TestMethod]
    public void MovementJumpDragAndClearReachTheExistingInputContractWithoutStickyForces()
    {
        var values = new List<DangoBeltInput>();
        var input = new AccessoryDynamicPreviewInput(values.Add);
        input.Command("left", default);
        Assert.AreEqual(-1d, values[^1].Horizontal);
        input.Command("right", default);
        Assert.AreEqual(1d, values[^1].Horizontal);
        input.Command("jump", default);
        Assert.IsTrue(values[^2].JumpPressed);
        Assert.IsFalse(values[^1].JumpPressed, "A second click needs another jump edge.");
        Assert.AreEqual(1d, values[^1].Horizontal, "Jump preserves the held direction.");
        input.Command("stop", default);
        Assert.AreEqual(0d, values[^1].Horizontal);
        input.Command("drag-start", new Point(2, 3));
        Assert.IsTrue(values[^1].DragActive);
        input.Command("drag-move", new Point(4, 5));
        Assert.AreEqual(4d, values[^1].DragTargetX);
        Assert.AreEqual(5d, values[^1].DragTargetY);
        input.Command("drag-end", default);
        Assert.IsFalse(values[^1].DragActive);
        var count = values.Count;
        input.Command("drag-move", new Point(100, 100));
        Assert.AreEqual(count, values.Count, "A released pointer cannot revive dragging.");
        input.Command("left", default);
        input.Command("drag-start", new Point(4, 5));
        input.Clear();
        Assert.AreEqual(0d, values[^1].Horizontal);
        Assert.IsFalse(values[^1].DragActive);
        Assert.IsFalse(values[^1].JumpPressed);
        Assert.IsTrue(values[^2].CancelForces);
        Assert.IsFalse(values[^1].CancelForces, "Cancel is an edge, never a persistent force override after resuming.");
    }

    [STATestMethod]
    public void CaptureReadsOnlyTheSuppliedVisualAndUsesFixedTransparentPngDimensions()
    {
        var view = new Border { Width = 160, Height = 120, Background = Brushes.Red };
        view.Measure(new Size(160, 120)); view.Arrange(new Rect(0, 0, 160, 120));
        var encoded = AccessoryDynamicPreviewImage.Capture(view, 50, 20);
        Assert.IsNotNull(encoded);
        using var stream = new MemoryStream(encoded);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.AreEqual(288, frame.PixelWidth); Assert.AreEqual(160, frame.PixelHeight);
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[288 * 160 * 4]; bitmap.CopyPixels(pixels, 288 * 4, 0);
        Assert.AreEqual((byte)0, pixels[3], "Space outside this visual remains transparent.");
        var center = (50 * 288 + 100) * 4;
        Assert.AreEqual((byte)255, pixels[center + 2]);
        Assert.AreEqual((byte)255, pixels[center + 3]);
        view.Background = Brushes.Blue;
        view.InvalidateArrange(); view.UpdateLayout();
        var changed = AccessoryDynamicPreviewImage.Capture(view, 50, 20);
        Assert.IsNotNull(changed);
        Assert.IsFalse(encoded.SequenceEqual(changed), "Capture reflects the actual visual's current content.");
    }

    [TestMethod]
    public async Task ResetUsesTheSameOwnerThreadPreservesConfigurationAndInvalidatesOldInputAndFrames()
    {
        var sessions = new List<BeltBodyRuntimeTests.FakeSession>();
        var bodies = new List<DangoBodyControls>();
        var creationThreads = new List<int>();
        await using var runtime = new BeltBodyRuntime(body =>
        {
            bodies.Add(body); creationThreads.Add(Environment.CurrentManagedThreadId);
            var created = new BeltBodyRuntimeTests.FakeSession(); sessions.Add(created); return created;
        }, true);
        var body = new DangoBodyControls(110, 20, 80);
        await runtime.ConfigureAsync(body, 7, 3);
        await runtime.SetBoundsAsync(-7, 22);
        await runtime.ResumeAsync();
        runtime.PublishInput(new(Horizontal: 1, JumpPressed: true, DragActive: true, DragTargetX: 4, DragTargetY: 5, InputActive: true));
        Assert.IsTrue(await runtime.AdvanceAsync());
        Assert.AreEqual(1, runtime.Metrics.QueueDepth);
        var oldEpoch = runtime.Epoch;
        await runtime.ResetPoseAsync();
        Assert.AreEqual(2, sessions.Count);
        Assert.AreEqual(1, sessions[0].Disposals);
        Assert.AreEqual(0, sessions[1].Disposals);
        Assert.AreEqual(creationThreads[0], creationThreads[1]);
        Assert.IsTrue(sessions[0].Threads.All(thread => thread == creationThreads[0]));
        Assert.IsTrue(sessions[1].Threads.All(thread => thread == creationThreads[0]));
        Assert.AreEqual(body, bodies[1]);
        Assert.AreEqual((-7d, 22d), sessions[1].LastBounds);
        Assert.IsTrue(runtime.Epoch > oldEpoch);
        Assert.AreEqual(1, runtime.Metrics.QueueDepth, "Reset publishes one genuine initial pose even without another physics tick.");
        Assert.AreEqual(3L, runtime.Metrics.BodyEpoch);
        Assert.AreEqual(7L, runtime.Metrics.ConfigurationRevision);
        Assert.IsTrue(runtime.Metrics.Running);
        Assert.IsTrue(await runtime.AdvanceAsync());
        Assert.IsTrue(runtime.TryDequeue(out var frame)); Assert.IsNotNull(frame);
        Assert.AreEqual(0d, frame.ActualInput.Horizontal);
        Assert.IsFalse(frame.ActualInput.DragActive);
        Assert.IsFalse(frame.ActualInput.JumpPressed);
        await runtime.PauseAsync();
        var stepsBeforePausedReset = runtime.Metrics.NativeStepCalls;
        var producedBeforePausedReset = runtime.Metrics.ProducedTicks;
        await runtime.ResetPoseAsync();
        Assert.IsFalse(runtime.Metrics.Running, "Reset preserves an explicit pause.");
        Assert.AreEqual(stepsBeforePausedReset + 1, runtime.Metrics.NativeStepCalls);
        Assert.AreEqual(producedBeforePausedReset + 1, runtime.Metrics.ProducedTicks);
        Assert.AreEqual(1, runtime.Metrics.QueueDepth);
        Assert.IsTrue(runtime.TryDequeue(out var pausedFrame)); Assert.IsNotNull(pausedFrame?.Frame);
        Assert.AreEqual(runtime.Epoch, pausedFrame.Epoch);
        Assert.IsNull(pausedFrame.Error);
        var pausedSteps = runtime.Metrics.NativeStepCalls;
        await Task.Delay(50);
        Assert.AreEqual(pausedSteps, runtime.Metrics.NativeStepCalls, "The initial sample must not start a paused owner.");
    }
}
