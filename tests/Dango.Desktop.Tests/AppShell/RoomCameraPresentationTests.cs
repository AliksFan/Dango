using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomCameraPresentationTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void NoStepRenderMovesPresentationAndCameraTogetherWithoutSteppingPhysicsOrFakingFrameApplied()
    {
        using var rig = new Rig();
        var applied = 0;
        rig.Scene.FrameApplied += () => applied++;
        Assert.IsTrue(rig.Driver.Key(Key.D, true, false));
        for (var i = 0; i < 80; i++) rig.Advance(HostFrameClock.FixedStep.Ticks);
        var before = rig.Scheduler.Snapshot;
        var physical = rig.Runtime.LastFrame;
        var nativeCenter = rig.Runtime.CurrentSelectedCenter;
        var offset = rig.Scene.PresentationOffset;
        var cameraX = rig.Scene.Camera.CameraX;
        var owner = rig.Runtime.FrameDriver;
        var entity = rig.Runtime.EntityIds.Single();
        var count = applied;

        rig.Advance(HostFrameClock.FixedStep.Ticks / 2);
        Assert.AreEqual(before.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(before.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.AreEqual(count, applied);
        Assert.AreSame(physical, rig.Runtime.LastFrame);
        Assert.AreEqual(nativeCenter, rig.Runtime.CurrentSelectedCenter);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        Assert.IsTrue(rig.Scene.PresentationOffset.X > offset.X, "The body cannot remain a 60Hz staircase while the camera advances.");
        Assert.IsTrue(rig.Scene.Camera.CameraX > cameraX, "Camera must also advance on this zero-step render.");
        AssertProjectionAndHit(rig);

        var finalOffset = rig.Scene.PresentationOffset;
        var finalCamera = rig.Scene.Camera.CameraX;
        rig.Duplicate();
        Assert.AreEqual(finalOffset, rig.Scene.PresentationOffset);
        Assert.AreEqual(finalCamera, rig.Scene.Camera.CameraX, "Duplicated RenderingTime must not advance the camera twice.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void MultiStepBatchUsesItsFullTimeSpanAndOverloadRebasesWithoutReplayingDroppedTime()
    {
        using var rig = new Rig();
        Assert.IsTrue(rig.Driver.Key(Key.D, true, false));
        for (var i = 0; i < 30; i++) rig.Advance(HostFrameClock.FixedStep.Ticks);
        var previous = rig.Runtime.CurrentSelectedCenter!.Value;
        var before = rig.Scheduler.Snapshot;
        var step = HostFrameClock.FixedStep.Ticks;
        rig.Advance(step * 3);
        var current = rig.Runtime.CurrentSelectedCenter!.Value;
        Assert.AreEqual(before.SuccessfulSteps + 3, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(-(current.X - previous.X) / 3, rig.Scene.PresentationOffset.X, 1e-8);
        Assert.AreEqual(-(current.Y - previous.Y) / 3, rig.Scene.PresentationOffset.Y, 1e-8);
        var partial = step / 2;
        rig.Advance(partial);
        var remaining = (step - partial) / (double)(3 * step);
        Assert.AreEqual(-(current.X - previous.X) * remaining, rig.Scene.PresentationOffset.X, 1e-8);
        AssertProjectionAndHit(rig);
        before = rig.Scheduler.Snapshot;
        rig.Advance(step * 12);
        Assert.AreEqual(before.Overloads + 1, rig.Scheduler.Snapshot.Overloads);
        Assert.AreEqual(before.SuccessfulSteps + HostFrameClock.MaximumSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(default(LogicalVector), rig.Scene.PresentationOffset);
        AssertProjectionAndHit(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void NativeJumpCrossesTheMidlineBeforeFollowingAndKeepsThePaintedBodyInsideTheViewport()
    {
        using var rig = new Rig();
        var initial = rig.Scene.Camera.Projection.OffsetY;
        var scale = rig.Scene.Camera.Scale;
        var crossed = false;
        Assert.IsTrue(rig.Driver.Key(Key.Space, true, false));
        var excursion = 0d;
        var body = Field<DeformableDangoVisual>(rig.Scene, "body");
        for (var i = 0; i < 100; i++)
        {
            rig.Advance(HostFrameClock.FixedStep.Ticks);
            rig.Advance(HostFrameClock.FixedStep.Ticks / 3);
            var core = rig.Runtime.CurrentSelectedCenter!.Value;
            var focusY = core.Y + rig.Scene.PresentationOffset.Y + rig.Runtime.NeutralBodyGroundOffset;
            if (focusY * scale + initial < rig.Scene.ActualHeight / 2) crossed = true;
            if (!crossed)
                Assert.AreEqual(initial, rig.Scene.Camera.Projection.OffsetY, 1e-8,
                    "Native jumps wholly below the midline must preserve ground composition.");
            excursion = Math.Max(excursion, Math.Abs(rig.Scene.Camera.Projection.OffsetY - initial));
            var paintedBottom = body.TransformToAncestor(rig.Scene)
                .Transform(new System.Windows.Point(0, body.RenderedBodyBottom!.Value)).Y;
            Assert.IsTrue(paintedBottom <= rig.Scene.ActualHeight - 32 + 1e-8,
                "The actual rendered path, including gait and interpolation, must keep its lower margin.");
            AssertProjectionAndHit(rig);
        }
        if (crossed) Assert.IsTrue(excursion > .01);
        else Assert.AreEqual(0, excursion, 1e-8);
        Assert.IsFalse(rig.Runtime.IsDragging);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PauseHideAndRestartDiscardOldSamplesAndKeepOneOwner()
    {
        using var rig = new Rig();
        Assert.IsTrue(rig.Driver.Key(Key.D, true, false));
        for (var i = 0; i < 30; i++) rig.Advance(HostFrameClock.FixedStep.Ticks);
        rig.Advance(TimeSpan.TicksPerSecond * 2);
        Assert.AreEqual(default(LogicalVector), rig.Scene.PresentationOffset, "Long pause must not replay the old position interval.");
        AssertProjectionAndHit(rig);
        rig.Window.Hide();
        var camera = rig.Scene.Camera.CameraX;
        var steps = rig.Scheduler.Snapshot.SuccessfulSteps;
        rig.Advance(HostFrameClock.FixedStep.Ticks * 4);
        Assert.AreEqual(camera, rig.Scene.Camera.CameraX);
        Assert.AreEqual(steps, rig.Scheduler.Snapshot.SuccessfulSteps);
        rig.Driver.Stop();
        Assert.IsNull(rig.Runtime.FrameDriver);
        rig.Window.Show(); rig.Window.UpdateLayout();
        rig.Driver.Start(rig.Scene, 2, enterWorld: false);
        rig.Advance(HostFrameClock.FixedStep.Ticks);
        Assert.AreEqual(default(LogicalVector), rig.Scene.PresentationOffset);
        Assert.AreSame(rig.Scheduler, rig.Runtime.FrameDriver);
        AssertProjectionAndHit(rig);
    }

    private static void AssertProjectionAndHit(Rig rig)
    {
        var p = rig.Scene.Camera.Projection;
        var offset = rig.Scene.PresentationOffset;
        var layer = Field<Canvas>(rig.Scene, "entityLayer");
        var matrix = ((MatrixTransform)layer.RenderTransform).Matrix;
        Assert.AreEqual(p.OffsetX + offset.X * p.Scale, matrix.OffsetX, 1e-8);
        Assert.AreEqual(p.OffsetY + offset.Y * p.Scale, matrix.OffsetY, 1e-8);
        Assert.AreEqual(2, layer.Children.Count, "Body and hat share exactly the same presentation transform.");
        var raw = DangoHostFrameBinding.Bounds(rig.Runtime.LastFrame!, 0)
            .Translate(new LogicalVector(0, rig.Runtime.NeutralBodyGroundOffset)).Center;
        var displayed = p.WorldToScreen(new(raw.X + offset.X, raw.Y + offset.Y));
        var screen = new System.Windows.Point(displayed.X, displayed.Y);
        var hit = rig.Scene.ScreenToPetWorld(screen);
        Assert.AreEqual(raw.X, hit.X, 1e-8);
        Assert.AreEqual(raw.Y, hit.Y, 1e-8);
        Assert.IsTrue(rig.Scene.IsPetHit(screen));
        Assert.IsTrue(rig.Scene.PetScreenBounds!.Value.Contains(displayed));
        var edge = rig.Scene.EnvironmentEdges;
        Assert.AreEqual(rig.Runtime.LocalGroundY!.Value,
            rig.Scene.ScreenToWorld(new(edge.Left, edge.CollisionY)).Y, 1e-8,
            "Environment receives camera projection without the entity interpolation offset.");
    }

    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    private sealed class Source : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long time) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { TimeSpan.FromTicks(time) }, null)!);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Source source = new();
        private long ticks;
        internal StackedDangoSceneRuntime Runtime { get; } = StackedDangoSceneRuntime.CreateProduct();
        internal LocalRoomSceneDriver Driver { get; }
        internal LocalRoomSceneView Scene { get; } = new();
        internal System.Windows.Window Window { get; }
        internal HostFrameScheduler Scheduler => Field<HostFrameScheduler>(Driver, "scheduler");

        internal Rig()
        {
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            Scene.EnableWindowCamera();
            Driver = new(new DesktopSceneSession(), Runtime, source, () => ticks, TimeSpan.TicksPerSecond);
            Window = new() { Width = 960, Height = 600, Content = Scene, Title = "Fictional window camera fixture" };
            Window.Show(); Window.UpdateLayout();
            Driver.Start(Scene, 1, enterWorld: true);
            for (var i = 0; i < 6; i++) Advance(HostFrameClock.FixedStep.Ticks);
            Pump(120);
            Assert.IsTrue(Scene.HasHealthyFrame, Runtime.LastError);
            Assert.IsTrue(Field<bool>(Driver, "readySent"));
        }
        internal void Advance(long delta) { ticks += delta; source.Raise(ticks); }
        internal void Duplicate() => source.Raise(ticks);
        public void Dispose() { Driver.Dispose(); Window.Close(); Runtime.Dispose(); }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
