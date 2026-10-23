using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Canvas = System.Windows.Controls.Canvas;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
[DoNotParallelize]
public sealed class AccessoryPreviewPresentationTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void UniqueZeroStepUpdatesActualHomeCameraWithoutRebuildingThePhysicalFrameOrTextures()
    {
        using var rig = new Rig();
        rig.MoveRight(40);
        rig.Window.UpdateLayout();
        var before = rig.Scheduler.Snapshot;
        var cameraX = rig.Camera.CameraX;
        var cameraY = rig.Camera.CameraY;
        var physics = rig.Runtime.LastPhysicsFrame;
        var physical = rig.Runtime.LastFrame;
        var nativeCenter = rig.Runtime.CurrentSelectedCenter;
        var geometry = rig.BodyPath.Data;
        var owner = rig.Runtime.FrameDriver;
        var entity = rig.Runtime.EntityIds.Single();
        var meshes = rig.Meshes.Select(MeshSnapshot.Capture).ToArray();
        var positions = CaptureEntityOrigins(rig);

        rig.Advance(HostFrameClock.FixedStep.Ticks / 2);

        Assert.AreEqual(before.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(before.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.AreSame(physics, rig.Runtime.LastPhysicsFrame);
        Assert.AreSame(physical, rig.Runtime.LastFrame);
        Assert.AreEqual(nativeCenter, rig.Runtime.CurrentSelectedCenter);
        Assert.AreSame(geometry, rig.BodyPath.Data);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(entity, rig.Runtime.EntityIds.Single());
        foreach (var mesh in meshes) mesh.AssertUnchanged();
        // This is the first new-behavior assertion. The old implementation fails
        // here because its camera only advances when a physical frame is applied;
        // absence of the new named layer cannot be the red-test mechanism.
        Assert.IsTrue(rig.Camera.CameraX > cameraX,
            "A unique zero-step callback must advance the real Home camera between actual native position samples.");
        Assert.AreEqual(cameraY, rig.Camera.CameraY, "Accessory motion must keep the ground camera's Y fixed.");
        AssertSharedProjection(rig);
        var remaining = (HostFrameClock.FixedStep.Ticks - HostFrameClock.FixedStep.Ticks / 2)
            / (double)HostFrameClock.FixedStep.Ticks;
        Assert.AreEqual(-(nativeCenter!.Value.X - rig.PreviousAppliedCenter.X) * remaining,
            Offset(rig.Home).X, 1e-8, "The half-step must interpolate the interval between actual native centers.");
        AssertCommonDisplacement(rig, positions);
        Assert.AreEqual(0, Offset(rig.Home).Y, "A grounded sample must never interpolate the body's contact height.");
        AssertGroundContact(rig);

        var finalOffset = Offset(rig.Home);
        var finalCamera = rig.Camera.CameraX;
        var finalTransform = rig.EntityLayer.RenderTransform;
        var finalPresentationTicks = Field<long?>(rig.Home, "previousAccessoryPresentationTicks");
        rig.Duplicate(HostFrameClock.FixedStep.Ticks / 4);
        Assert.AreEqual(before.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(before.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.AreEqual(finalOffset, Offset(rig.Home));
        Assert.AreEqual(finalCamera, rig.Camera.CameraX);
        Assert.AreSame(finalTransform, rig.EntityLayer.RenderTransform);
        Assert.AreEqual(finalPresentationTicks, Field<long?>(rig.Home, "previousAccessoryPresentationTicks"));
        foreach (var mesh in meshes) mesh.AssertUnchanged();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void MultiStepBatchInterpolatesItsWholeNativeSpanAndPreservesRelativeAccessoryPositions()
    {
        using var rig = new Rig();
        rig.MoveRight(30);
        var before = rig.Scheduler.Snapshot;
        var previous = rig.Runtime.CurrentSelectedCenter!.Value;
        var step = HostFrameClock.FixedStep.Ticks;
        rig.Advance(3 * step);
        var current = rig.Runtime.CurrentSelectedCenter!.Value;
        Assert.AreEqual(before.SuccessfulSteps + 3, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(before.AppliedFrames + 1, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.IsTrue(current.X > previous.X,
            $"The span must contain real native motion: previous={previous}, current={current}, " +
            $"homeIntent={Field<DirectionalControlIntent>(rig.Home, "liveIntent")}, " +
            $"runtimeIntent={Field<DirectionalControlIntent>(rig.Runtime, "directionalIntent")}, " +
            $"active={rig.Window.IsActive}, alignment={Field<bool>(rig.Home, "liveSurfaceNeedsAlignment")}, " +
            $"placement={Field<bool>(rig.Home, "liveSurfacePlacementPending")}, error={rig.Runtime.LastError}.");
        Assert.AreEqual(-(current.X - previous.X) / 3, Offset(rig.Home).X, 1e-8,
            "A three-step batch needs its three-step sample interval, rather than one-step interpolation.");
        Assert.AreEqual(0, Offset(rig.Home).Y);
        AssertSharedProjection(rig);
        AssertGroundContact(rig);
        var positions = CaptureEntityOrigins(rig);
        var geometry = rig.BodyPath.Data;
        var meshes = rig.Meshes.Select(MeshSnapshot.Capture).ToArray();
        var applied = rig.Scheduler.Snapshot;
        var partial = step / 2;
        rig.Advance(partial);
        Assert.AreEqual(-(current.X - previous.X) * ((step - partial) / (double)(3 * step)),
            Offset(rig.Home).X, 1e-8);
        Assert.AreEqual(applied.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(applied.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.AreSame(geometry, rig.BodyPath.Data);
        foreach (var mesh in meshes) mesh.AssertUnchanged();
        AssertCommonDisplacement(rig, positions);
        AssertSharedProjection(rig);
        AssertGroundContact(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void AirborneNativeLowJumpInterpolatesYButGroundedFramesKeepExactContactAndFixedCameraY()
    {
        using var rig = new Rig();
        var cameraY = rig.Camera.CameraY;
        var sawAirborne = false;
        var sawVerticalInterpolation = false;
        var landed = false;
        rig.SetIntent(new(Left: false, Right: true, Jump: true, Squash: false));
        for (var frame = 0; frame < 180; frame++)
        {
            if (frame == 3) rig.SetIntent(new(Left: false, Right: true, Jump: false, Squash: false));
            rig.Advance(HostFrameClock.FixedStep.Ticks - rig.Scheduler.Snapshot.RemainderTicks);
            var before = rig.Scheduler.Snapshot;
            rig.Advance(HostFrameClock.FixedStep.Ticks / 3);
            Assert.AreEqual(before.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(before.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
            Assert.AreEqual(cameraY, rig.Camera.CameraY, "Takeoff, descent and landing must retain vertical ground framing.");
            Assert.IsTrue(rig.Runtime.UnboundedHorizontalPreview);
            Assert.AreEqual(.5, rig.Runtime.PreviewJumpSpeedScale);
            AssertSharedProjection(rig);
            if (rig.Grounded)
            {
                Assert.AreEqual(0, Offset(rig.Home).Y, "Grounded interpolation is horizontal only.");
                AssertGroundContact(rig);
                if (sawAirborne) { landed = true; break; }
            }
            else
            {
                sawAirborne = true;
                sawVerticalInterpolation |= Math.Abs(Offset(rig.Home).Y) > 1e-7;
            }
        }
        Assert.IsTrue(sawAirborne, "The fixture must exercise a real low native jump.");
        Assert.IsTrue(sawVerticalInterpolation, "Airborne samples must preserve native Y interpolation.");
        Assert.IsTrue(landed, "The real native low jump must land again.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void OverloadAndLongPauseRebaseWithoutInterpolatingDiscardedPhysicalTime()
    {
        using var rig = new Rig();
        rig.MoveRight(30);
        var before = rig.Scheduler.Snapshot;
        rig.Advance(HostFrameClock.FixedStep.Ticks * 12);
        Assert.AreEqual(before.Overloads + 1, rig.Scheduler.Snapshot.Overloads);
        Assert.AreEqual(before.SuccessfulSteps + HostFrameClock.MaximumSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(before.AppliedFrames + 1, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.AreEqual(default(LogicalVector), Offset(rig.Home));
        AssertSharedProjection(rig);
        before = rig.Scheduler.Snapshot;
        var physics = rig.Runtime.LastPhysicsFrame;
        rig.Advance(2 * TimeSpan.TicksPerSecond);
        Assert.AreEqual(before.LongPauses + 1, rig.Scheduler.Snapshot.LongPauses);
        Assert.AreEqual(before.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
        Assert.AreEqual(before.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
        Assert.AreSame(physics, rig.Runtime.LastPhysicsFrame);
        Assert.AreEqual(default(LogicalVector), Offset(rig.Home));
        AssertSharedProjection(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow("Hide")]
    [DataRow("Stop")]
    [DataRow("Exit")]
    public void HideStopAndEditorExitDiscardTheOldPresentationSamplesAndRestartWithOneOwner(string action)
    {
        using var rig = new Rig();
        rig.MoveRight(30);
        Assert.IsTrue(Field<bool>(rig.Home, "hasAccessoryPositionSample"));
        Assert.IsTrue(Math.Abs(Offset(rig.Home).X) > 1e-7, "The reset must discard a real nonzero position interval.");
        switch (action)
        {
            case "Hide": rig.Window.Hide(); break;
            case "Stop": rig.Home.ReleaseLiveScene(); break;
            default: rig.Home.CloseEditor(); break;
        }
        Assert.IsFalse(Field<bool>(rig.Home, "hasAccessoryPositionSample"));
        Assert.IsNull(Field<long?>(rig.Home, "previousAccessoryPresentationTicks"));
        Assert.AreEqual(default(LogicalVector), Offset(rig.Home));
        Assert.IsTrue(rig.EntityLayer.RenderTransform.Value.IsIdentity);
        if (action != "Exit")
        {
            var before = rig.Scheduler.Snapshot;
            Assert.IsNull(rig.Runtime.FrameDriver);
            rig.Advance(4 * HostFrameClock.FixedStep.Ticks);
            Assert.AreEqual(before.SuccessfulSteps, rig.Scheduler.Snapshot.SuccessfulSteps);
            Assert.AreEqual(before.AppliedFrames, rig.Scheduler.Snapshot.AppliedFrames);
            Assert.IsFalse(Field<bool>(rig.Home, "hasAccessoryPositionSample"));
        }
        rig.Home.ReduceMotion = true;
        if (action == "Hide") { rig.Window.Show(); rig.Window.UpdateLayout(); }
        if (!rig.Home.IsEditing) rig.Home.ShowEditor(HomeEditor.Wardrobe);
        Invoke(rig.Home, "RefreshLiveSceneOwnership");
        Assert.IsTrue(rig.Home.OwnsLiveFrameDriver);
        Assert.AreSame(rig.Scheduler, rig.Runtime.FrameDriver);
        for (var callback = 0; callback < 12 && !Field<bool>(rig.Home, "hasAccessoryPositionSample"); callback++)
            rig.Advance(HostFrameClock.FixedStep.Ticks);
        Assert.IsTrue(Field<bool>(rig.Home, "hasAccessoryPositionSample"));
        Assert.AreEqual(default(LogicalVector), Offset(rig.Home), "The first healthy resumed sample must not reuse the old interval.");
        Assert.IsTrue(rig.Runtime.UnboundedHorizontalPreview);
        Assert.AreEqual(.5, rig.Runtime.PreviewJumpSpeedScale);
        AssertSharedProjection(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SharedCameraTransformKeepsBodyAnchorCoordinatesInvertibleAfterDisplayOnlyMotion()
    {
        using var rig = new Rig();
        rig.MoveRight(30);
        rig.Advance(HostFrameClock.FixedStep.Ticks / 2);
        rig.Window.UpdateLayout();
        AssertSharedProjection(rig);
        var area = Element<FrameworkElement>(rig.Home, "PetVisualArea");
        var transform = rig.Body.TransformToAncestor(area);
        var inverse = transform.Inverse;
        Assert.IsNotNull(inverse);
        foreach (var local in new[] { new Point(80, 60), new Point(37.25, 43.75), new Point(121.5, 81.25) })
        {
            var screen = transform.Transform(local);
            var roundTrip = inverse!.Transform(screen);
            Assert.AreEqual(local.X, roundTrip.X, 1e-8);
            Assert.AreEqual(local.Y, roundTrip.Y, 1e-8);
            var canvas = Element<Canvas>(rig.Home, "LiveSceneCanvas");
            var canvasPoint = canvas.TransformToAncestor(area).Inverse!.Transform(screen);
            var translated = canvas.TranslatePoint(canvasPoint, rig.Body);
            Assert.AreEqual(local.X, translated.X, 1e-8, "WPF coordinate conversion must include the shared camera/presentation transform.");
            Assert.AreEqual(local.Y, translated.Y, 1e-8);
        }
    }

    private static LogicalVector Offset(ProductHomeView home) =>
        Field<LogicalVector>(home, "accessoryPresentationOffset");

    private static (FrameworkElement Element, Point Origin)[] CaptureEntityOrigins(Rig rig)
    {
        rig.Window.UpdateLayout();
        var canvas = Element<Canvas>(rig.Home, "LiveSceneCanvas");
        return new FrameworkElement[] { rig.Body, rig.Hat }.Concat(rig.Meshes)
            .Select(element => (element, element.TransformToAncestor(canvas).Transform(new Point()))).ToArray();
    }

    private static void AssertCommonDisplacement(Rig rig, (FrameworkElement Element, Point Origin)[] before)
    {
        var canvas = Element<Canvas>(rig.Home, "LiveSceneCanvas");
        var displacements = before.Select(item => item.Element.TransformToAncestor(canvas)
            .Transform(new Point()) - item.Origin).ToArray();
        foreach (var displacement in displacements)
        {
            Assert.AreEqual(displacements[0].X, displacement.X, 1e-8);
            Assert.AreEqual(displacements[0].Y, displacement.Y, 1e-8);
        }
        // No sign or magnitude is required: camera follow and entity translation
        // may partially or wholly cancel while all relative positions stay fixed.
    }

    private static void AssertSharedProjection(Rig rig)
    {
        var layer = rig.EntityLayer;
        var p = rig.Camera.Projection;
        var offset = Offset(rig.Home);
        var matrix = layer.RenderTransform.Value;
        Assert.AreEqual(p.Scale, matrix.M11, 1e-8);
        Assert.AreEqual(p.Scale, matrix.M22, 1e-8);
        Assert.AreEqual(p.OffsetX + offset.X * p.Scale, matrix.OffsetX, 1e-8);
        Assert.AreEqual(p.OffsetY + offset.Y * p.Scale, matrix.OffsetY, 1e-8);
        Assert.AreSame(layer, rig.Body.Parent);
        Assert.AreSame(layer, rig.Hat.Parent);
        Assert.HasCount(2, rig.Meshes, "Both a front and a rear real accessory must share the entity transform.");
        foreach (var mesh in rig.Meshes)
        {
            Assert.IsTrue(layer.IsAncestorOf(mesh));
            Assert.AreSame(layer, ((Canvas)mesh.Parent).Parent);
        }
        Assert.IsFalse(layer.UseLayoutRounding);
        Assert.IsFalse(rig.Body.UseLayoutRounding);
    }

    private static void AssertGroundContact(Rig rig)
    {
        Assert.IsTrue(rig.Grounded, "Contact assertions require a real native grounded body.");
        rig.Window.UpdateLayout();
        var area = Element<FrameworkElement>(rig.Home, "PetVisualArea");
        var canvas = Element<Canvas>(rig.Home, "LiveSceneCanvas");
        var floor = rig.Camera.Projection.WorldToScreen(new(rig.Runtime.CurrentSelectedCenter!.Value.X,
            rig.Runtime.LocalGroundY!.Value));
        var projectedFloor = canvas.TransformToAncestor(area).Transform(new Point(floor.X, floor.Y));
        var bottom = rig.Body.TransformToAncestor(area).Transform(new Point(0, rig.Body.RenderedBodyBottom!.Value));
        Assert.AreEqual(projectedFloor.Y - 1, bottom.Y, 1e-6,
            "Interpolation must preserve the actual painted-body contact, including the accepted one-DIP softness.");
    }

    private static T Element<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new AssertFailedException("Missing Home control: " + name);

    private static T Field<T>(object value, string name) =>
        (T)(value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing field: " + name)).GetValue(value)!;

    private static void SetField(object value, string name, object? next) =>
        (value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing field: " + name)).SetValue(value, next);

    private sealed record MeshSnapshot(AccessoryMeshVisual Visual, AccessoryMeshFrame Frame,
        WriteableBitmap Bitmap, int[] Pixels, double RasterMilliseconds, double UploadMilliseconds)
    {
        internal static MeshSnapshot Capture(AccessoryMeshVisual visual)
        {
            var bitmap = Field<WriteableBitmap>(visual, "bitmap");
            var pixels = new int[checked(bitmap.PixelWidth * bitmap.PixelHeight)];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Assert.IsTrue(pixels.Any(pixel => ((uint)pixel >> 24) != 0), "Fixture texture must contain actual visible pixels.");
            return new(visual, visual.Frame!, bitmap, pixels, visual.LastRasterMilliseconds, visual.LastUploadMilliseconds);
        }

        internal void AssertUnchanged()
        {
            Assert.AreSame(Frame, Visual.Frame, "Zero-step presentation must not reconstruct the accessory mesh frame.");
            Assert.AreSame(Bitmap, Field<WriteableBitmap>(Visual, "bitmap"));
            var actual = new int[Pixels.Length];
            Bitmap.CopyPixels(actual, Bitmap.PixelWidth * 4, 0);
            CollectionAssert.AreEqual(Pixels, actual, "Original texture raster pixels must survive display-only presentation.");
            Assert.AreEqual(RasterMilliseconds, Visual.LastRasterMilliseconds);
            Assert.AreEqual(UploadMilliseconds, Visual.LastUploadMilliseconds);
        }
    }

    private sealed class Source : IHostFrameSource
    {
        public event EventHandler? Rendering;
        internal void Raise(long renderingTicks) => Rendering?.Invoke(this,
            (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { TimeSpan.FromTicks(renderingTicks) }, null)!);
    }

    private sealed class Rig : IDisposable
    {
        private readonly Source source = new();
        private long ticks;
        private long renderingTicks;
        internal ProductHomeView Home { get; } = new() { ReduceMotion = true };
        internal StackedDangoSceneRuntime Runtime { get; }
        internal HostFrameScheduler Scheduler { get; }
        internal Window Window { get; }
        internal DeformableDangoVisual Body => Element<DeformableDangoVisual>(Home, "LiveDangoVisual");
        internal DangoHatVisual Hat => Element<DangoHatVisual>(Home, "LiveDangoHat");
        internal ShapePath BodyPath => (ShapePath)Body.FindName("BodyPath");
        internal RoomViewController Camera => Field<RoomViewController>(Home, "accessoryCamera");
        internal Canvas EntityLayer => Element<Canvas>(Home, "LiveEntityLayer");
        internal bool Grounded => Runtime.LastPhysicsFrame?.Bodies[0].IsGrounded == true;
        internal LogicalPoint PreviousAppliedCenter { get; private set; }
        internal AccessoryMeshVisual[] Meshes => Descendants<AccessoryMeshVisual>(Body.Parent as DependencyObject
            ?? throw new AssertFailedException("The actual Home body must have a parent.")).ToArray();

        internal Rig()
        {
            var settingsRoot = System.IO.Path.Combine(TestRepository.Root, "artifacts", "accessory-preview-presentation", Guid.NewGuid().ToString("N"));
            var accessories = new AccessorySettingsSession(settingsRoot);
            accessories.Stage(new([
                new("fictional-presentation-back", "Fictional rear presentation leaf", "builtin-sprout", 64, 64)
                { Layer = AccessoryLayer.Behind, BodyAnchorX = .4, BodyAnchorY = .3,
                    DeformationMode = AccessoryDeformationMode.SingleChain, Rig = new([new(.5, .5), new(.8, .9)], .5) },
                new("fictional-presentation-front", "Fictional front presentation hat", "builtin-top-hat", 64, 64)
                { Layer = AccessoryLayer.Front, BodyAnchorX = .7, BodyAnchorY = .4, Order = 3,
                    DeformationMode = AccessoryDeformationMode.SingleChain, Rig = new([new(.5, .5), new(.8, .9)], .7) },
            ]));
            Assert.IsTrue(accessories.TrySave());
            var native = Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH")
                ?? System.IO.Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
            Assert.IsTrue(File.Exists(native), "The approved low-jump native candidate must be supplied to the Home fixture.");
            Runtime = StackedDangoSceneRuntime.CreateProduct(native);
            Runtime.BindAccessories(accessories);
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            Home.BindAccessories(accessories);
            Home.BindLiveScene(new DesktopSceneSession(), Runtime);
            var original = Field<HostFrameScheduler>(Home, "liveScheduler");
            var subscribers = Field<EventHandler?>(original, "CallbackCompleted");
            original.Stop();
            Scheduler = new(Runtime, Method<Func<bool>>(Home, "PrepareLiveFrame"),
                Method<Func<PhysicsPresentationInput>>(Home, "CreateLiveInput"),
                Method<Action<StackedPhysicsPresentationFrame>>(Home, "ApplyLiveFrame"), source,
                () => ticks, TimeSpan.TicksPerSecond)
            { DetailedTimingEnabled = true };
            if (subscribers is not null) Scheduler.CallbackCompleted += subscribers;
            SetField(Home, "liveScheduler", Scheduler);
            Home.ShowEditor(HomeEditor.Wardrobe);
            Window = new()
            {
                Content = Home,
                Width = 1080,
                Height = 760,
                Left = -10000,
                Top = -10000,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
                Opacity = 0,
                Title = "Fictional accessory presentation fixture",
            };
            Window.Show(); Window.UpdateLayout(); Pump(20);
            Assert.IsTrue(Home.IsLoaded && Home.IsVisible);
            if (!Runtime.IsRunning) Runtime.Start();
            if (!Scheduler.IsRunning) Invoke(Home, "RefreshLiveSceneOwnership");
            Assert.IsTrue(Home.OwnsLiveFrameDriver && Scheduler.IsRunning);
            for (var step = 0; step < 140; step++) Advance(HostFrameClock.FixedStep.Ticks);
            Window.UpdateLayout();
            Assert.IsTrue(Grounded, "The real native fixture must settle before presentation assertions.");
            Assert.AreEqual(Visibility.Visible, Body.Visibility);
            Assert.AreEqual(DangoContourFailure.None, Body.LastContourFailure);
            Assert.IsNull(Runtime.LastError);
            Assert.IsNull(Runtime.AccessoryError);
            Assert.IsTrue(Runtime.UnboundedHorizontalPreview);
            Assert.AreEqual(.5, Runtime.PreviewJumpSpeedScale);
            Assert.HasCount(2, Meshes);
            Home.ReduceMotion = false;
        }

        internal void MoveRight(int steps)
        {
            SetIntent(new(false, true, false, false));
            for (var step = 0; step < steps; step++) Advance(HostFrameClock.FixedStep.Ticks);
        }

        internal void SetIntent(DirectionalControlIntent intent) => SetField(Home, "liveIntent", intent);

        internal void Advance(long delta)
        {
            var before = Scheduler.Snapshot;
            var center = Runtime.CurrentSelectedCenter;
            ticks += delta;
            renderingTicks = ticks;
            source.Raise(renderingTicks);
            if (center.HasValue && Scheduler.Snapshot.AppliedFrames > before.AppliedFrames)
                PreviousAppliedCenter = center.Value;
        }

        internal void Duplicate(long elapsed)
        {
            ticks += elapsed;
            source.Raise(renderingTicks);
        }

        public void Dispose()
        {
            Home.ReleaseLiveScene();
            Window.Close();
            Runtime.Dispose();
        }
    }

    private static T Method<T>(object owner, string name) where T : Delegate =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing method: " + name)).CreateDelegate<T>(owner);

    private static void Invoke(object owner, string name) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing method: " + name)).Invoke(owner, null);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var nested in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return nested;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
