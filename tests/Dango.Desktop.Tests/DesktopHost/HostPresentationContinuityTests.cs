using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.DesktopHost.StandardWindow;
using Dango.Desktop.DesktopHost.WorkerW;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Tests.DesktopHost;

[TestClass]
public sealed class HostPresentationContinuityTests
{
    private sealed class Source : IHostFrameSource
    {
        public event EventHandler? Rendering;

        internal void Raise(long renderingTicks) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                [TimeSpan.FromTicks(renderingTicks)], null)!);
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ZeroStepAndDuplicateCallbacksPreserveTheLastPresentedBodyAndHat(bool wallpaper)
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var scene = new DesktopSceneSession();
        scene.AttachSurface(new(0, 0, 800, 600), 1000);
        object host = wallpaper ? new WorkerWWindow(scene, runtime) : new DangoSceneWindow(scene, runtime);
        // The wallpaper control is hosted only by our hidden fixture HWND, never Explorer.
        var fixture = host as System.Windows.Window ?? new System.Windows.Window
        {
            Content = host,
            Width = 800,
            Height = 600,
        };
        fixture.ShowActivated = false;
        fixture.ShowInTaskbar = false;
        fixture.Opacity = 0;
        fixture.WindowStartupLocation = WindowStartupLocation.Manual;
        fixture.Left = -10000;
        fixture.Top = -10000;
        HostFrameScheduler? scheduler = null;
        try
        {
            fixture.Show();
            fixture.UpdateLayout();
            Get<HostFrameScheduler>(host, "motionScheduler").Stop();
            if (wallpaper) Set(host, "isVisible", true);
            runtime.SetViewport(new(0, 0, 800, 600));
            runtime.Start();
            var input = Invoke<PhysicsPresentationInput>(host, "CreateMotionInput");
            runtime.Tick(input, HostFrameClock.FixedStep);
            runtime.SetDirectionalControl(new(false, true, false, false));
            for (var i = 0; i < 90; i++) runtime.Tick(input, HostFrameClock.FixedStep);
            runtime.SetDirectionalControl(DirectionalControlIntent.None);
            Assert.IsNull(runtime.LastError);
            Invoke<object?>(host, "ApplyPresentationFrame", runtime.LastFrame!);

            var body = Get<DeformableDangoVisual>(host, "DangoVisual");
            var hat = Get<DangoHatVisual>(host, "DangoHat");
            var beforeBody = Position(body);
            var beforeHat = Position(hat);
            var origin = scene.Snapshot!.Dango.VisualBounds;
            Assert.IsGreaterThan(20d, Math.Abs(beforeBody.X - origin.X),
                "Fixture must move real native physics away from the Core spawn position.");

            var source = new Source();
            long now = 0;
            var applied = 0;
            scheduler = new(runtime, () => Invoke<bool>(host, "PrepareMotionFrame"),
                () => Invoke<PhysicsPresentationInput>(host, "CreateMotionInput"), frame =>
                {
                    Invoke<object?>(host, "ApplyPresentationFrame", frame);
                    applied++;
                }, source, () => now, TimeSpan.TicksPerSecond);
            scheduler.Start();
            var frameBefore = runtime.LastFrame;
            now = HostFrameClock.FixedStep.Ticks / 2;
            source.Raise(1);
            var zeroBody = Position(body);
            var zeroHat = Position(hat);
            Assert.AreSame(frameBefore, runtime.LastFrame);
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0L, scheduler.Snapshot.SuccessfulSteps);
            source.Raise(1);
            var duplicateBody = Position(body);
            var duplicateHat = Position(hat);
            Assert.AreEqual(1L, scheduler.Snapshot.DuplicateCallbacks);

            now = HostFrameClock.FixedStep.Ticks;
            source.Raise(2);
            var steppedBounds = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0);
            Assert.AreEqual(new LogicalPoint(steppedBounds.X, steppedBounds.Y), Position(body));
            Assert.AreEqual(Position(body), Position(hat));
            Assert.AreEqual(1, applied);
            Assert.AreEqual(1L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(beforeBody, zeroBody,
                $"Zero-step Rendering exposed Core spawn ({origin.X}, {origin.Y}) instead of the last physical position; next step returned to {Position(body)}.");
            Assert.AreEqual(beforeHat, zeroHat);
            Assert.AreEqual(beforeBody, duplicateBody);
            Assert.AreEqual(beforeHat, duplicateHat);
        }
        finally
        {
            scheduler?.Stop();
            if (host is DangoSceneWindow window) window.PrepareForExit();
            else ((WorkerWWindow)host).Close();
            fixture.Close();
            scene.Exit(2000);
        }
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ZeroStepAndDuplicateCallbacksPreserveActualNonIdentityGaitAndEveryAttachment(bool wallpaper)
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var scene = new DesktopSceneSession();
        scene.AttachSurface(new(0, 0, 800, 600), 1000);
        object host = wallpaper ? new WorkerWWindow(scene, runtime) : new DangoSceneWindow(scene, runtime);
        var fixture = host as System.Windows.Window ?? new System.Windows.Window { Content = host, Width = 800, Height = 600 };
        fixture.ShowActivated = false;
        fixture.ShowInTaskbar = false;
        fixture.Opacity = 0;
        fixture.WindowStartupLocation = WindowStartupLocation.Manual;
        fixture.Left = fixture.Top = -10000;
        HostFrameScheduler? scheduler = null;
        try
        {
            fixture.Show();
            fixture.UpdateLayout();
            Get<HostFrameScheduler>(host, "motionScheduler").Stop();
            if (wallpaper) Set(host, "isVisible", true);
            runtime.Stop();
            runtime.SetViewport(new(0, 0, 800, 600));
            runtime.Start();
            runtime.AttachQuietIdleHost(host, true);
            runtime.UpdateWanderScreenWidth(host, 1600);
            var input = Invoke<PhysicsPresentationInput>(host, "CreateMotionInput");
            for (var i = 0; i < 150; i++) runtime.Tick(input, HostFrameClock.FixedStep);
            var origin = scene.Snapshot!.Dango.VisualBounds;
            for (var i = 0; i < 240; i++)
            {
                runtime.SetDirectionalControl(new(false, true, false, false));
                var next = runtime.Tick(input, HostFrameClock.FixedStep);
                if (next.Gait is { } gait && Math.Abs(gait.ScaleY - 1) > .003 &&
                    Math.Abs(DangoHostFrameBinding.Bounds(next, 0).X - origin.X) > 20) break;
            }
            Assert.IsNull(runtime.LastError);
            var frame = runtime.LastFrame!;
            Assert.IsNotNull(frame.Gait, "This regression requires actual native Product gait, not an identity fixture.");
            Assert.IsTrue(Math.Abs(frame.Gait.ScaleY - 1) > .003);
            Assert.IsGreaterThan(20d, Math.Abs(DangoHostFrameBinding.Bounds(frame, 0).X - origin.X));
            runtime.SetDirectionalControl(DirectionalControlIntent.None);
            Invoke<object?>(host, "ApplyPresentationFrame", frame);
            fixture.UpdateLayout();
            var body = Get<DeformableDangoVisual>(host, "DangoVisual");
            var hat = Get<DangoHatVisual>(host, "DangoHat");
            var beforeBody = Position(body);
            var beforeHat = Position(hat);
            var beforeDrawing = CaptureDrawing(body, hat);
            Assert.IsFalse(beforeDrawing.Matrices[0].IsIdentity, "The actual body drawing matrix must contain gait.");
            Assert.AreEqual(frame.Gait.ScaleX, beforeDrawing.Matrices[0].M11, 1e-8);
            Assert.AreEqual(frame.Gait.ScaleY, beforeDrawing.Matrices[0].M22, 1e-8);

            var source = new Source();
            long now = 0;
            var applied = 0;
            scheduler = new(runtime, () => Invoke<bool>(host, "PrepareMotionFrame"),
                () => Invoke<PhysicsPresentationInput>(host, "CreateMotionInput"), value =>
                { Invoke<object?>(host, "ApplyPresentationFrame", value); applied++; },
                source, () => now, TimeSpan.TicksPerSecond);
            scheduler.Start();
            now = HostFrameClock.FixedStep.Ticks / 2;
            source.Raise(1);
            Assert.AreSame(frame, runtime.LastFrame);
            AssertDrawingUnchanged(beforeDrawing, CaptureDrawing(body, hat), "zero-step callback");
            Assert.AreEqual(beforeBody, Position(body));
            Assert.AreEqual(beforeHat, Position(hat));
            source.Raise(1);
            Assert.AreSame(frame, runtime.LastFrame);
            AssertDrawingUnchanged(beforeDrawing, CaptureDrawing(body, hat), "duplicate callback");
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(1L, scheduler.Snapshot.DuplicateCallbacks);

            // A rebind before another physics step must also retain the same complete pose.
            Invoke<object?>(host, "ApplyAuthoritativePosition");
            fixture.UpdateLayout();
            AssertDrawingUnchanged(beforeDrawing, CaptureDrawing(body, hat), "layout rebind");
            now = HostFrameClock.FixedStep.Ticks;
            source.Raise(2);
            Assert.AreEqual(1, applied);
            Assert.AreEqual(1L, scheduler.Snapshot.SuccessfulSteps);
            Assert.AreNotSame(frame, runtime.LastFrame);
            var bounds = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0);
            Assert.AreEqual(new LogicalPoint(bounds.X, bounds.Y), Position(body));
            Assert.AreEqual(Position(body), Position(hat));
        }
        finally
        {
            scheduler?.Stop();
            if (host is DangoSceneWindow window) window.PrepareForExit();
            else ((WorkerWWindow)host).Close();
            fixture.Close();
            scene.Exit(2000);
        }
    }

    private sealed record DrawingSnapshot(Matrix[] Matrices, Rect[] Bounds, System.Windows.Point HatRoot);

    private static DrawingSnapshot CaptureDrawing(DeformableDangoVisual body, DangoHatVisual hat)
    {
        var paths = new List<(System.Windows.Shapes.Path Path, FrameworkElement Root)>
        { ((System.Windows.Shapes.Path)body.FindName("BodyPath"), body) };
        foreach (var name in new[] { "LeftEyeLayer", "RightEyeLayer" })
            paths.AddRange(((Canvas)body.FindName(name)).Children.OfType<System.Windows.Shapes.Path>()
                .Select(path => (path, (FrameworkElement)body)));
        paths.AddRange(((Canvas)hat.FindName("HatLayer")).Children.OfType<System.Windows.Shapes.Path>()
            .Select(path => (path, (FrameworkElement)hat)));
        Assert.IsTrue(paths.Count >= 6, "Both complete eyes and the independent hat must be observed.");
        var matrices = new List<Matrix>();
        var bounds = new List<Rect>();
        foreach (var (path, root) in paths)
        {
            var transform = path.TransformToAncestor(root);
            var zero = transform.Transform(new(0, 0));
            var x = transform.Transform(new(1, 0));
            var y = transform.Transform(new(0, 1));
            matrices.Add(new(x.X - zero.X, x.Y - zero.Y, y.X - zero.X, y.Y - zero.Y, zero.X, zero.Y));
            bounds.Add(transform.TransformBounds(path.Data.Bounds));
        }
        var hatRoot = ((Canvas)hat.FindName("HatLayer")).TransformToAncestor(hat).Transform(new(80, 25));
        return new(matrices.ToArray(), bounds.ToArray(), hatRoot);
    }

    private static void AssertDrawingUnchanged(DrawingSnapshot expected, DrawingSnapshot actual, string context)
    {
        CollectionAssert.AreEqual(expected.Matrices, actual.Matrices, $"Internal drawing matrices changed during {context}.");
        CollectionAssert.AreEqual(expected.Bounds, actual.Bounds, $"Body or complete eye/hat geometry changed during {context}.");
        Assert.AreEqual(expected.HatRoot, actual.HatRoot, $"Independent hat root changed during {context}.");
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewHostLayoutUsesTheExistingPhysicalFrameForBothBodyAndHat(bool wallpaper)
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var scene = new DesktopSceneSession();
        scene.AttachSurface(new(0, 0, 800, 600), 1000);
        var frame = MoveAwayFromSpawn(runtime, scene);
        // Construct the new host only after another host's physical state already exists.
        object host = wallpaper ? new WorkerWWindow(scene, runtime) : new DangoSceneWindow(scene, runtime);
        try
        {
            var body = Get<DeformableDangoVisual>(host, "DangoVisual");
            var hat = Get<DangoHatVisual>(host, "DangoHat");
            var bounds = DangoHostFrameBinding.Bounds(frame, 0);
            var expected = new LogicalPoint(bounds.X, bounds.Y);
            Assert.IsGreaterThan(20d, Math.Abs(expected.X - scene.Snapshot!.Dango.VisualBounds.X));

            Invoke<object?>(host, "ApplyAuthoritativePosition");
            Assert.AreEqual(expected, Position(body), "New-host layout must retain the physical position before its first step.");
            Assert.AreEqual(expected, Position(hat), "The body and hat must be initialized from the same existing frame.");
            Invoke<object?>(host, "ApplyAuthoritativePosition");
            Assert.AreEqual(expected, Position(body), "Repeated layout must not restore Core's spawn position.");
            Assert.AreEqual(expected, Position(hat));
            Assert.AreSame(frame, runtime.LastFrame, "Host layout must not advance or replace the physical frame.");
            Assert.IsNull(runtime.FrameDriver);
        }
        finally { CloseHost(host); scene.Exit(2000); }
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FirstLayoutWithoutAPhysicalFrameStillUsesCoreInitialPosition(bool wallpaper)
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        Assert.IsNull(runtime.LastFrame);
        var scene = new DesktopSceneSession();
        scene.AttachSurface(new(0, 0, 800, 600), 1000);
        object host = wallpaper ? new WorkerWWindow(scene, runtime) : new DangoSceneWindow(scene, runtime);
        try
        {
            var body = Get<DeformableDangoVisual>(host, "DangoVisual");
            Canvas.SetLeft(body, -123);
            Canvas.SetTop(body, -456);
            var initial = scene.Snapshot!.Dango.VisualBounds;
            Invoke<object?>(host, "ApplyAuthoritativePosition");
            Assert.AreEqual(new LogicalPoint(initial.X, initial.Y), Position(body));
            Assert.IsNull(runtime.LastFrame, "Initial placement must not synthesize a physical frame.");
            Assert.IsFalse(runtime.IsRunning);
        }
        finally { CloseHost(host); scene.Exit(2000); }
    }

    [STATestMethod]
    public void WallpaperInitialSceneLayoutDoesNotOverwriteAnExistingPhysicalBody()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        var scene = new DesktopSceneSession();
        scene.AttachSurface(new(0, 0, 800, 600), 1000);
        var frame = MoveAwayFromSpawn(runtime, scene);
        var host = new WorkerWWindow(scene, runtime);
        try
        {
            Invoke<object?>(host, "ApplyPresentationFrame", frame);
            var body = Get<DeformableDangoVisual>(host, "DangoVisual");
            var hat = Get<DangoHatVisual>(host, "DangoHat");
            var expectedBody = Position(body);
            var expectedHat = Position(hat);
            // Exercise the compiled control's first size/layout path without any desktop HWND.
            host.Measure(new Size(800, 600));
            host.Arrange(new Rect(0, 0, 800, 600));
            Invoke<object?>(host, "PositionScene");
            Assert.IsTrue(Get<bool>(host, "scenePositionInitialized"));
            Assert.AreEqual(expectedBody, Position(body), "Prototype furniture placement must not move an existing physical body.");
            Assert.AreEqual(expectedHat, Position(hat));
            Assert.AreSame(frame, runtime.LastFrame);
        }
        finally { host.Close(); scene.Exit(2000); }
    }

    private static StackedPhysicsPresentationFrame MoveAwayFromSpawn(StackedDangoSceneRuntime runtime, DesktopSceneSession scene)
    {
        runtime.SetViewport(new(0, 0, 800, 600));
        runtime.Start();
        var controller = scene.Controller!;
        var input = PhysicsPresentationInput.FromSnapshot(controller.Snapshot, controller.SquashAmount,
            controller.GroundFrictionCue, controller.FacingDirection, null);
        runtime.Tick(input, HostFrameClock.FixedStep);
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var i = 0; i < 90; i++) runtime.Tick(input, HostFrameClock.FixedStep);
        runtime.SetDirectionalControl(DirectionalControlIntent.None);
        Assert.IsNull(runtime.LastError);
        return runtime.LastFrame!;
    }

    private static void CloseHost(object host)
    {
        if (host is DangoSceneWindow window) { window.PrepareForExit(); window.Close(); }
        else ((WorkerWWindow)host).Close();
    }

    private static LogicalPoint Position(FrameworkElement element) => new(Canvas.GetLeft(element), Canvas.GetTop(element));

    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static T Invoke<T>(object target, string name, params object[] args) =>
        (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
}
