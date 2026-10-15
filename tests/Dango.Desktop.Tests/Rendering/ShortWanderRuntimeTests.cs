using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class ShortWanderRuntimeTests
{
    private static readonly LogicalRect View = new(0, 0, 1200, 900);
    private static StackedDangoSceneRuntime Create(out object owner, bool right = false)
    {
        var runtime = LightFeedbackRuntimeTests.Create();
        runtime.SetViewport(View);
        owner = new(); runtime.AttachQuietIdleHost(owner, true);
        runtime.UpdateWanderScreenWidth(owner, 1200);
        runtime.Wander.Next = (min, _) => min == 0 && right ? 1 : min;
        // Keep deterministic blink/gaze deadlines away from the 8-second movement deadline;
        // overlapping deadlines are separately verified to drop and resample movement.
        runtime.QuietIdle.Next = (min, _) => min == 15000 ? 25000 : min == 4000 ? 6000 : min;
        return runtime;
    }

    private static void WaitForDrive(StackedDangoSceneRuntime runtime)
    {
        for (var i = 0; i < 3300 && !runtime.Wander.Driving; i++) LightFeedbackRuntimeTests.Steps(runtime, 1);
        Assert.IsTrue(runtime.Wander.Driving, $"No autonomous movement; bounds={WanderVisibleBounds.Get(runtime.LastFrame!.Bodies[0], DangoHostFrameBinding.Bounds(runtime.LastFrame, 0), runtime.DisplayHatSize)}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProductReallyMovesThenStopsAndEyesResume(bool right)
    {
        using var runtime = Create(out _, right);
        WaitForDrive(runtime);
        var start = runtime.LastPhysicsFrame!.Bodies.Single().CoreX;
        var sawDrive = false;
        for (var i = 0; i < 400; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1);
            sawDrive |= runtime.Wander.Input(.02) != 0;
            if (runtime.Wander.Active) Assert.IsFalse(runtime.HasQuietIdleAction);
            var bounds = WanderVisibleBounds.Get(runtime.LastFrame!.Bodies[0], DangoHostFrameBinding.Bounds(runtime.LastFrame, 0), runtime.DisplayHatSize)!.Value;
            Assert.IsTrue(new LogicalRect(16, 0, 1168, 900).Contains(bounds));
        }
        Assert.IsTrue(sawDrive); Assert.IsFalse(runtime.Wander.Active);
        var distance = (runtime.LastPhysicsFrame!.Bodies.Single().CoreX - start) * 72 * (right ? 1 : -1);
        Assert.IsTrue(distance is >= 120 and <= 160, $"Real movement was {distance} DIP");
        var eyesResumed = false;
        for (var i = 0; i < 450; i++) { LightFeedbackRuntimeTests.Steps(runtime, 1); eyesResumed |= runtime.HasQuietIdleAction; }
        Assert.IsTrue(eyesResumed);
    }

    [TestMethod]
    public void DownCaptureAndDirectionalInputCancelWithoutChangingManualPhysics()
    {
        using var runtime = Create(out _);
        WaitForDrive(runtime); LightFeedbackRuntimeTests.Steps(runtime, 15);
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        var session = new LightInteractionSession(runtime);
        var point = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0).Center;
        Assert.IsTrue(session.TryBegin(target.Entity, point, 0, false, () => true, () => { }, () => true));
        Assert.IsFalse(runtime.Wander.Active); Assert.AreEqual(0, runtime.Wander.Input(.02));
        LightFeedbackRuntimeTests.Steps(runtime, 1600); Assert.IsFalse(runtime.Wander.Active);
        session.Cancel();
        // Prime only the pure waiting clock here; the next runtime step must enforce manual ownership.
        for (var i = 0; i < 426; i++) runtime.Wander.Advance(new(0, 0, true, 200, 200), .02, true, false);
        Assert.IsTrue(runtime.Wander.Active);
        runtime.SetDirectionalControl(new(true, false, true, false));
        Assert.IsFalse(runtime.Wander.Active);
        LightFeedbackRuntimeTests.Steps(runtime, 20);
        Assert.IsFalse(runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded);
    }

    [TestMethod]
    public void HiddenOwnerResetAndMultipleBodiesCannotReplayMovement()
    {
        using var runtime = Create(out var owner);
        WaitForDrive(runtime);
        runtime.UpdateQuietIdleHost(owner, false);
        Assert.IsFalse(runtime.Wander.Active);
        runtime.UpdateQuietIdleHost(new object(), true);
        LightFeedbackRuntimeTests.Steps(runtime, 1600); Assert.IsFalse(runtime.Wander.Active);
        runtime.UpdateQuietIdleHost(owner, true);
        LightFeedbackRuntimeTests.Steps(runtime, 300); Assert.IsFalse(runtime.Wander.Active);
        runtime.RegenerateDango();
        Assert.IsFalse(runtime.Wander.Active);
        Assert.IsTrue(runtime.AddDango());
        runtime.Wander.Next = (_, _) => throw new AssertFailedException("Multiple entities scheduled a movement");
        LightFeedbackRuntimeTests.Steps(runtime, 100);
        Assert.IsFalse(runtime.Wander.Active);
    }

    [TestMethod]
    public void NearEdgeMovementAndCoastStayInsideVisibleMargin()
    {
        using var runtime = Create(out _);
        LightFeedbackRuntimeTests.Steps(runtime, 200);
        var initial = WanderVisibleBounds.Get(runtime.LastFrame!.Bodies[0], DangoHostFrameBinding.Bounds(runtime.LastFrame, 0), runtime.DisplayHatSize)!.Value;
        var nearEdge = new LogicalRect(initial.X - 16 - 180, 0, 1200, 900);
        runtime.SetViewport(nearEdge);
        WaitForDrive(runtime);
        Assert.AreEqual(-1, runtime.Wander.Direction);
        for (var i = 0; i < 400; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1);
            var visible = WanderVisibleBounds.Get(runtime.LastFrame!.Bodies[0], DangoHostFrameBinding.Bounds(runtime.LastFrame, 0), runtime.DisplayHatSize)!.Value;
            Assert.IsTrue(visible.X >= nearEdge.X + 16 && visible.Right <= nearEdge.Right - 16);
        }
        Assert.IsFalse(runtime.Wander.Active);
    }

    [STATestMethod]
    public void SafetyHullContainsActualRenderedBodyAndAllHatAssets()
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        LightFeedbackRuntimeTests.Steps(runtime, 200);
        var frame = runtime.LastFrame!.Bodies[0];
        var body = DangoHostFrameBinding.Bounds(runtime.LastFrame, 0);
        var hull = WanderVisibleBounds.Get(frame, body, runtime.DisplayHatSize)!.Value;
        void Contains(System.Windows.Rect bounds)
        {
            Assert.IsTrue(hull.Contains(new LogicalRect(body.X + bounds.X, body.Y + bounds.Y, bounds.Width, bounds.Height)));
        }
        Contains(DangoStreamGeometryFactory.TryCreate(frame.Contour, 160, 120).Geometry!.Bounds);
        foreach (var appearance in Enum.GetValues<HatAppearance>())
        {
            var canvas = new System.Windows.Controls.Canvas();
            HatAppearanceGeometry.Apply(canvas, appearance);
            Assert.IsTrue(AttachmentGeometryFactory.ApplyHatFrame(canvas, frame.Attachments!.Hat, runtime.DisplayHatSize));
            foreach (System.Windows.Shapes.Path path in canvas.Children)
                Contains(canvas.RenderTransform.TransformBounds(path.Data.Bounds));
        }
    }

    [TestMethod]
    [DataRow(10, 1200, 900, false)]
    [DataRow(20, 480, 360, true)]
    [DataRow(40, 1200, 900, true)]
    public void DefaultDesktopPlacementCanReachMovementGate(int milliseconds, int width, int height, bool right)
    {
        var view = new LogicalRect(0, 0, width, height);
        var session = new Dango.Desktop.DesktopHost.DesktopSceneSession();
        var snapshot = session.AttachSurface(view, 0);
        var input = new PhysicsPresentationInput("wander-fixture", snapshot.Dango.VisualBounds,
            DangoActionState.Idle, 0, 17, "fixture-ground", new(0, -1), 0, 1, false, true, []);
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize()); runtime.Start();
        runtime.SetViewport(view); var owner = new object(); runtime.AttachQuietIdleHost(owner, true);
        runtime.UpdateWanderScreenWidth(owner, 1920);
        runtime.Wander.Next = (min, _) => min == 0 && right ? 1 : min;
        runtime.QuietIdle.Next = (min, _) => min == 15000 ? 25000 : min == 4000 ? 6000 : min;
        for (var i = 0; i < 66000 / milliseconds && !runtime.Wander.Driving; i++) runtime.Tick(input, TimeSpan.FromMilliseconds(milliseconds));
        Assert.IsTrue(runtime.Wander.Driving, $"Default bounds: {WanderVisibleBounds.Get(runtime.LastFrame!.Bodies[0], DangoHostFrameBinding.Bounds(runtime.LastFrame, 0), runtime.DisplayHatSize)}; viewport: {view}");
        var direction = runtime.Wander.Direction;
        var start = runtime.LastPhysicsFrame!.Bodies[0].CoreX;
        var diagnostics = new Queue<string>();
        var previousX = start * 72d;
        var previousSupport = double.NaN;
        for (var i = 0; i < 7500 / milliseconds; i++)
        {
            runtime.Tick(input, TimeSpan.FromMilliseconds(milliseconds));
            var visible = WanderVisibleBounds.Get(runtime.LastFrame!.Bodies[0], DangoHostFrameBinding.Bounds(runtime.LastFrame, 0), runtime.DisplayHatSize)!.Value;
            Assert.IsTrue(new LogicalRect(16, 0, width - 32, height).Contains(visible), $"Visible hull escaped: {visible}");
            var body = runtime.LastPhysicsFrame!.Bodies[0];
            var support = body.CoreY * 72d + runtime.LastFrame.Bodies[0].Attachments!.SourceShape!.Bottom * 160;
            var speed = Math.Sqrt(Math.Pow((body.CoreX * 72d - previousX) / (milliseconds / 1000d), 2) +
                Math.Pow((support - previousSupport) / (milliseconds / 1000d), 2));
            diagnostics.Enqueue($"{i}: active={runtime.Wander.Active}, drive={runtime.Wander.Driving}, x={body.CoreX:R}, coreY={body.CoreY:R}, support={support:R}, speed={speed:R}, grounded={body.IsGrounded}");
            if (diagnostics.Count > 60) diagnostics.Dequeue();
            previousX = body.CoreX * 72d; previousSupport = support;
        }
        Assert.IsFalse(runtime.Wander.Active,
            $"startX={start:R}; displacement={direction * (runtime.LastPhysicsFrame!.Bodies[0].CoreX - start) * 72:R} DIP" +
            Environment.NewLine + string.Join(Environment.NewLine, diagnostics));
        var distance = direction * (runtime.LastPhysicsFrame!.Bodies[0].CoreX - start) * 72;
        Assert.IsTrue(distance is >= 192 and <= 250, $"Displacement {distance}");
    }

    [TestMethod]
    public void WithoutCurrentHostPhysicalSnapshotsStayIdentical()
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        using var reference = LightFeedbackRuntimeTests.Create();
        runtime.Wander.Next = (_, _) => throw new AssertFailedException("Unowned runtime scheduled movement");
        for (var i = 0; i < 120; i++)
        {
            var input = new DirectionalControlIntent(i < 60, false, i == 70, false);
            runtime.SetDirectionalControl(input); reference.SetDirectionalControl(input);
            LightFeedbackRuntimeTests.Steps(runtime, 1); LightFeedbackRuntimeTests.Steps(reference, 1);
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastPhysicsFrame), JsonSerializer.Serialize(runtime.LastPhysicsFrame));
        }
    }

    [TestMethod]
    public void ScreenChangeCancelsAndOldHostCannotChangeCurrentScreen()
    {
        using var runtime = Create(out var owner);
        for (var i = 0; i < 426; i++) runtime.Wander.Advance(new(0, 0, true, 200, 200), .02, true, false);
        Assert.IsTrue(runtime.Wander.Driving);
        runtime.UpdateWanderScreenWidth(new object(), 3840);
        Assert.IsTrue(runtime.Wander.Driving);
        runtime.UpdateWanderScreenWidth(owner, 1200);
        Assert.IsTrue(runtime.Wander.Driving);
        runtime.UpdateWanderScreenWidth(owner, 1920);
        Assert.IsFalse(runtime.Wander.Active);
    }

    [TestMethod]
    public void LifecycleCancelsControlledPendingDriveAndReviewNeverSchedules()
    {
        using var runtime = Create(out var owner);
        LightFeedbackRuntimeTests.Steps(runtime, 100);
        void Prime()
        {
            runtime.Wander.Cancel();
            for (var i = 0; i < 426; i++) runtime.Wander.Advance(new(0, 0, true, 200, 200), .02, true, false);
            Assert.IsTrue(runtime.Wander.Driving);
        }
        Prime(); runtime.SetViewport(new(0, 0, 1000, 800)); Assert.IsFalse(runtime.Wander.Active);
        Prime(); runtime.CancelInput(); Assert.IsFalse(runtime.Wander.Active);
        Prime(); runtime.Stop(); runtime.Stop(); Assert.IsFalse(runtime.Wander.Active);
        runtime.Start(); runtime.AttachQuietIdleHost(new object(), false);
        runtime.UpdateQuietIdleHost(owner, true);
        runtime.Wander.Next = (_, _) => throw new AssertFailedException("Old host re-enabled movement");
        LightFeedbackRuntimeTests.Steps(runtime, 100);
        using var review = StackedDangoSceneRuntime.CreateEyeHatReview();
        review.Start(); review.SetViewport(View); review.AttachQuietIdleHost(new object(), true);
        review.Wander.Next = (_, _) => throw new AssertFailedException("Review scheduled movement");
        LightFeedbackRuntimeTests.Steps(review, 100);
        Assert.IsFalse(review.Wander.Active);
    }

    [TestMethod]
    public void SceneManualInputOverridesAutonomousDrive()
    {
        var library = Path.Combine(AppContext.BaseDirectory, Dango.Physics.DangoPhysicsWorld.NativeLibraryFileName);
        using var scene = new DesktopPhysicsScene(library, new(0, 0, 160, 120));
        using var reference = new DesktopPhysicsScene(library, new(0, 0, 160, 120));
        for (var i = 0; i < 100; i++)
        {
            var input = new DirectionalControlIntent(true, false, i == 40, false);
            scene.SetInput(input); reference.SetInput(input);
            Assert.AreEqual(JsonSerializer.Serialize(reference.Step(TimeSpan.FromMilliseconds(20)).Physics),
                JsonSerializer.Serialize(scene.Step(TimeSpan.FromMilliseconds(20), .35).Physics));
        }
    }
}
