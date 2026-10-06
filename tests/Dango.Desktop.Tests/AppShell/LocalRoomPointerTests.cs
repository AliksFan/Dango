using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class LocalRoomPointerTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void VisibleExcludedSidesAndRaisedPaintKeepTheOriginalNativeGroundAndLimits()
    {
        using var fixture = new Fixture();
        var ground = fixture.Runtime.LocalGroundY!.Value;
        var limits = ((double Left, double Right))typeof(StackedDangoSceneRuntime)
            .GetField("localHorizontalBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Runtime)!;
        Assert.AreEqual(24d, limits.Left);
        Assert.AreEqual(2136d, limits.Right);
        var snapshot = ((DesktopSceneSession)typeof(LocalRoomSceneDriver)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Driver)!).Snapshot;
        var owner = fixture.Runtime.FrameDriver;
        var entity = fixture.Runtime.EntityIds.Single();
        foreach (var preset in new[] { RoomViewPreset.Standard, RoomViewPreset.Near, RoomViewPreset.Whole })
        {
            fixture.Scene.SelectView(preset);
            var edges = fixture.Scene.EnvironmentEdges;
            Assert.AreEqual(limits.Left, fixture.Scene.ScreenToWorld(new(edges.Left, edges.CollisionY)).X, 1e-8);
            Assert.AreEqual(limits.Right, fixture.Scene.ScreenToWorld(new(edges.Right, edges.CollisionY)).X, 1e-8);
            Assert.AreEqual(ground, fixture.Scene.ScreenToWorld(new(edges.Left, edges.CollisionY)).Y, 1e-8);
            Assert.AreEqual(10d, edges.CollisionY - edges.FloorY, 1e-8, "Only the drawn floor moves up by ten screen DIP.");
            Assert.AreEqual(ground, fixture.Runtime.LocalGroundY);
            Assert.AreEqual(limits, typeof(StackedDangoSceneRuntime)
                .GetField("localHorizontalBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Runtime));
            Assert.AreSame(snapshot, ((DesktopSceneSession)typeof(LocalRoomSceneDriver)
                .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Driver)!).Snapshot);
            Assert.AreSame(owner, fixture.Runtime.FrameDriver);
            Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
        }
        // Whole view exposes both original unreachable strips. Check actual painted pixels, not only geometry helpers.
        fixture.Scene.UpdateLayout();
        var e = fixture.Scene.EnvironmentEdges;
        Assert.IsTrue(e.Left > 4 && e.Right < fixture.Scene.ActualWidth - 4);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(fixture.Scene.ActualWidth),
            (int)Math.Ceiling(fixture.Scene.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(fixture.Scene), null,
                new Rect(0, 0, fixture.Scene.ActualWidth, fixture.Scene.ActualHeight));
        bitmap.Render(visual);
        var floor = Pixel(bitmap, e.Left + 50, e.CollisionY + 4);
        CollectionAssert.AreEqual(floor, Pixel(bitmap, e.Left + 50, e.CollisionY - 5),
            "The raised band must already display the floor above the unchanged collision line.");
        var wall = Pixel(bitmap, e.Left + 50, e.FloorY - 8);
        CollectionAssert.AreNotEqual(wall, floor);
        foreach (var x in new[] { e.Left / 2, (e.Right + fixture.Scene.ActualWidth) / 2 })
        {
            CollectionAssert.AreNotEqual(wall, Pixel(bitmap, x, e.FloorY - 8), "Excluded sides cannot use the normal wall material.");
            CollectionAssert.AreNotEqual(floor, Pixel(bitmap, x, e.CollisionY + 4), "Excluded sides cannot use the normal floor material.");
        }
        var directory = Path.Combine(TestRepository.Root, "artifacts", "local-room", "visual");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, "room-visible-boundaries.png"));
        encoder.Save(stream);
    }

    private static byte[] Pixel(BitmapSource bitmap, double x, double y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)Math.Floor(x), (int)Math.Floor(y), 1, 1), pixel, 4, 0);
        return pixel;
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void BodyHitAndInverseProjectionIncludeInsetsAtEveryPreset()
    {
        using var fixture = new Fixture();
        fixture.Scene.SafeInsets = new Thickness(37, 29, 18, 11);
        foreach (var preset in new[] { RoomViewPreset.Standard, RoomViewPreset.Near, RoomViewPreset.Whole })
        {
            fixture.Scene.SelectView(preset);
            var bounds = fixture.BodyBounds;
            var point = fixture.ToScene(bounds.Center);
            var inverse = fixture.Scene.ScreenToWorld(point);
            Assert.AreEqual(bounds.Center.X, inverse.X, 1e-8);
            Assert.AreEqual(bounds.Center.Y, inverse.Y, 1e-8);
            Assert.IsTrue(fixture.Scene.IsPetHit(point), preset.ToString());
            Assert.IsFalse(fixture.Scene.IsPetHit(fixture.ToScene(new(bounds.X - 1, bounds.Center.Y))),
                "The body boundary, rather than the wider appearance envelope, controls input.");
            var edgeY = fixture.Runtime.NeutralBodyGroundOffset >= 0 ? bounds.Bottom - .01 : bounds.Y + .01;
            Assert.IsTrue(fixture.Driver.PointerDown(fixture.ToScene(new(bounds.Center.X, edgeY)), nowMilliseconds: 0),
                "The aligned rendered body edge must map back into the unaligned physics interaction bounds.");
            fixture.Driver.CancelPointer();
            Assert.IsFalse(fixture.Driver.PointerDown(new Point(0, 0), nowMilliseconds: 0));
        }
        fixture.Scene.ShowUnavailable("fictional unavailable frame");
        Assert.IsFalse(fixture.Scene.IsPetHit(fixture.ToScene(fixture.BodyBounds.Center)));
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SceneDipClickAndShiftPettingDriveRealFeedbackWithoutReplacingOwner()
    {
        using var fixture = new Fixture();
        var owner = fixture.Runtime.FrameDriver;
        var entity = fixture.Runtime.EntityIds.Single();
        fixture.Scene.SafeInsets = new Thickness(43, 25, 0, 0);
        foreach (var preset in new[] { RoomViewPreset.Near, RoomViewPreset.Whole })
        {
            fixture.Driver.ClearInput();
            fixture.Scene.SelectView(preset);
            WaitForNeutralFrame(fixture);
            var center = fixture.BodyBounds.Center;
            var point = fixture.ToScene(center);
            var beforeClick = fixture.Runtime.LastFrame;
            Assert.IsTrue(fixture.Driver.PointerDown(point, nowMilliseconds: 1000));
            Assert.IsTrue(fixture.Driver.PointerUp(point, nowMilliseconds: 1010));
            Assert.IsFalse(fixture.Driver.IsPointerActive);
            Assert.IsTrue(fixture.Runtime.HasLightFeedback, "A short body click must reach the real attachment runtime.");
            WaitForFeedbackFrame(fixture, beforeClick!, 1);

            fixture.Driver.ClearInput();
            WaitForNeutralFrame(fixture);
            center = fixture.BodyBounds.Center;
            point = fixture.ToScene(center);
            Assert.IsTrue(fixture.Driver.PointerDown(point, shift: true, nowMilliseconds: 2000));
            var petPoint = fixture.ToScene(new(center.X + 16.1, center.Y));
            Assert.IsTrue(fixture.Driver.PointerMove(petPoint, shift: true, nowMilliseconds: 2100));
            Assert.IsFalse(fixture.Runtime.IsDragging, "Shift keeps the existing pet branch at every camera scale.");
            Assert.IsTrue(fixture.Runtime.HasLightFeedback);
            var beforePetFrame = fixture.Runtime.LastFrame;
            Assert.IsTrue(fixture.Driver.PointerUp(petPoint, shift: true, nowMilliseconds: 2110));
            WaitForFeedbackFrame(fixture, beforePetFrame!, .4);
            Assert.AreSame(owner, fixture.Runtime.FrameDriver);
            Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
        }
    }

    private static void WaitForFeedbackFrame(Fixture fixture,
        Dango.Desktop.Physics.Presentation.StackedPhysicsPresentationFrame previous, double maximumOpenness)
    {
        var scheduler = (HostFrameScheduler)typeof(LocalRoomSceneDriver)
            .GetField("scheduler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Driver)!;
        var before = scheduler.Snapshot;
        var cancellations = fixture.InputCancelledCount;
        var minimumOpenness = 1d;
        var activeFrames = 0;
        var observed = false;
        void Sample()
        {
            if (fixture.Runtime.HasLightFeedback) activeFrames++;
            minimumOpenness = Math.Min(minimumOpenness, fixture.Runtime.LastFrame?.Bodies[0].Attachments?.Face.Left.Openness ?? 1);
            var current = fixture.Runtime.LastFrame?.Bodies[0].Attachments;
            var baseline = previous.Bodies[0].Attachments!;
            observed |= fixture.Runtime.HasLightFeedback && current is not null &&
                current.Generation == baseline.Generation && current.StepIndex > baseline.StepIndex &&
                current.Face.Left.Openness < maximumOpenness;
        }
        var watch = Stopwatch.StartNew();
        fixture.Scene.FrameApplied += Sample;
        try { while (!observed && watch.Elapsed < TimeSpan.FromSeconds(1) && fixture.Runtime.LastError is null) Pump(10); }
        finally { fixture.Scene.FrameApplied -= Sample; }
        Assert.IsTrue(observed, $"A newly advanced real feedback frame must render eye closure <{maximumOpenness}. " +
            $"NewFrame={!ReferenceEquals(previous, fixture.Runtime.LastFrame)}, Feedback={fixture.Runtime.HasLightFeedback}, " +
            $"Openness={fixture.Runtime.LastFrame?.Bodies[0].Attachments?.Face.Left.Openness}, " +
            $"Steps={scheduler.Snapshot.SuccessfulSteps - before.SuccessfulSteps}, Applied={scheduler.Snapshot.AppliedFrames - before.AppliedFrames}, " +
            $"Cancelled={fixture.InputCancelledCount - cancellations}, ActiveFrames={activeFrames}, MinimumOpenness={minimumOpenness}, " +
            $"Error={fixture.Runtime.LastError}, LightError={fixture.Runtime.LightInteractionError}, AttachmentError={fixture.Runtime.AttachmentError}.");
    }

    private static void WaitForNeutralFrame(Fixture fixture)
    {
        bool Neutral() => !fixture.Runtime.HasLightFeedback && fixture.Runtime.LastFrame?.Bodies[0].Attachments is { } frame &&
            frame.Face.Left.Openness >= .98 && frame.Face.Right.Openness >= .98;
        var watch = Stopwatch.StartNew();
        while (!Neutral() && watch.Elapsed < TimeSpan.FromSeconds(1) && fixture.Runtime.LastError is null) Pump(10);
        Assert.IsTrue(Neutral(), "Each gesture must begin with open eyes and no residual light feedback.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PlainPointerMoveCannotDragPhysicsOrReplayClick()
    {
        using var fixture = new Fixture();
        fixture.Scene.SafeInsets = new Thickness(51, 21, 0, 0);
        fixture.Scene.SelectView(RoomViewPreset.Whole);
        var center = fixture.BodyBounds.Center;
        var owner = fixture.Runtime.FrameDriver;
        var entity = fixture.Runtime.EntityIds.Single();
        var physicalCancellations = 0;
        var pointerCancellations = 0;
        fixture.Runtime.InputCancelled += () => physicalCancellations++;
        fixture.Driver.PointerCancelled += () => pointerCancellations++;
        Assert.IsTrue(fixture.Driver.PointerDown(fixture.ToScene(center), nowMilliseconds: 0));
        Assert.IsTrue(fixture.Driver.PointerMove(fixture.ToScene(new(center.X + 3.01, center.Y)), nowMilliseconds: 10));
        Assert.IsFalse(fixture.Runtime.IsDragging, "Ordinary rooms reject physical drag conversion after inverse projection.");
        Assert.IsFalse(fixture.Driver.IsPointerActive);
        Assert.AreEqual(0, physicalCancellations, "Disabling drag cannot begin and immediately end a native drag.");
        Assert.AreEqual(1, pointerCancellations);
        var destination = new LogicalPoint(center.X + 80, center.Y - 35);
        Assert.IsFalse(fixture.Driver.PointerMove(fixture.ToScene(destination), nowMilliseconds: 20));
        Pump(140);
        Assert.IsFalse(fixture.Driver.PointerUp(fixture.ToScene(center), nowMilliseconds: 170));
        Assert.IsFalse(fixture.Runtime.IsDragging);
        Assert.IsFalse(fixture.Driver.IsPointerActive);
        Assert.IsFalse(fixture.Runtime.HasLightFeedback, "A rejected drag cannot replay a click when the pointer returns.");
        Assert.AreSame(owner, fixture.Runtime.FrameDriver);
        Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ClearMappingChangesAndBlockingCancelWithoutLateReplay()
    {
        using var fixture = new Fixture();
        var cancellations = 0;
        fixture.Driver.PointerCancelled += () => { cancellations++; fixture.Driver.CancelPointer(); };
        Action[] cancel =
        [
            fixture.Driver.ClearInput,
            () => fixture.Scene.SelectView(RoomViewPreset.Near),
            () => fixture.Scene.ApplyWheel(-60),
            () => { fixture.Window.Width += 20; fixture.Window.UpdateLayout(); },
            () => fixture.Scene.SafeInsets = new Thickness(19, 13, 0, 0),
            () => fixture.Scene.InputBlocked = true,
            fixture.Runtime.CancelInput,
            () => fixture.Scene.ShowUnavailable("fictional unavailable frame"),
        ];
        foreach (var invalidate in cancel)
        {
            fixture.Scene.InputBlocked = false;
            Pump(40);
            var point = fixture.ToScene(fixture.BodyBounds.Center);
            Assert.IsTrue(fixture.Driver.PointerDown(point, shift: true, nowMilliseconds: 100));
            Assert.IsTrue(fixture.Driver.PointerMove(new(point.X + fixture.Scene.Camera.Scale * 4, point.Y), shift: true, nowMilliseconds: 110));
            Assert.IsTrue(fixture.Driver.IsPointerActive, "Each cancellation must begin with a live Shift gesture, rather than an already rejected drag.");
            Assert.IsFalse(fixture.Runtime.IsDragging);
            var before = cancellations;
            invalidate();
            Assert.AreEqual(before + 1, cancellations, "Cancellation must notify once, even with reentrant capture loss.");
            Assert.IsFalse(fixture.Driver.IsPointerActive);
            Assert.IsFalse(fixture.Runtime.IsDragging);
            Assert.IsFalse(fixture.Driver.PointerUp(point, nowMilliseconds: 120));
            Assert.IsFalse(fixture.Runtime.HasLightFeedback);
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void UnreadyBlockedInvalidAndNonOwnerInputsCannotMutateRuntime()
    {
        using var fixture = new Fixture(waitForReady: false);
        Assert.IsFalse(fixture.Driver.PointerDown(new(100, 100), nowMilliseconds: 0), "Show/start alone does not grant input readiness.");
        bool? firstFrameAccepted = null;
        fixture.Scene.FrameApplied += () =>
        {
            if (firstFrameAccepted is null)
                firstFrameAccepted = fixture.Driver.PointerDown(fixture.ToScene(fixture.BodyBounds.Center), nowMilliseconds: 0);
        };
        fixture.WaitForReady();
        Assert.AreEqual(false, firstFrameAccepted, "A healthy applied frame still needs the subsequent presentation confirmation before input is granted.");
        var point = fixture.ToScene(fixture.BodyBounds.Center);
        Assert.IsFalse(fixture.Driver.PointerDown(new(double.NaN, point.Y), nowMilliseconds: 0));
        fixture.Scene.InputBlocked = true;
        Assert.IsFalse(fixture.Driver.PointerDown(point, nowMilliseconds: 0));
        fixture.Scene.InputBlocked = false;
        Assert.IsTrue(fixture.Driver.PointerDown(point, nowMilliseconds: 0));
        Assert.IsTrue(fixture.Driver.PointerMove(new(double.PositiveInfinity, point.Y), nowMilliseconds: 1));
        Assert.IsFalse(fixture.Driver.IsPointerActive);
        Assert.IsFalse(fixture.Runtime.IsDragging);
        fixture.Driver.Stop();
        fixture.Runtime.Start();
        var other = new HostFrameScheduler(fixture.Runtime, () => true, () => throw new InvalidOperationException(), _ => { });
        other.Start();
        try
        {
            Assert.IsFalse(fixture.Driver.PointerDown(point, nowMilliseconds: 2));
            fixture.Runtime.BeginPhysicalDrag(fixture.Runtime.EntityIds.Single(), fixture.Runtime.CurrentSelectedCenter!.Value);
            fixture.Driver.ClearInput();
            Assert.IsTrue(fixture.Runtime.IsDragging, "An inactive driver cannot cancel another owner's input.");
        }
        finally { fixture.Runtime.EndPhysicalDrag(); other.Stop(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ReplacedViewEventsCannotCancelTheNewHostGesture()
    {
        using var fixture = new Fixture();
        var oldView = fixture.Scene;
        fixture.Driver.Stop();
        fixture.Scene = new LocalRoomSceneView();
        fixture.Window.Content = fixture.Scene;
        fixture.Window.UpdateLayout();
        fixture.Driver.Start(fixture.Scene, 2, enterWorld: false);
        fixture.WaitForReady();
        var point = fixture.ToScene(fixture.BodyBounds.Center);
        Assert.IsTrue(fixture.Driver.PointerDown(point, nowMilliseconds: 0));
        oldView.SelectView(RoomViewPreset.Whole);
        oldView.InputBlocked = true;
        oldView.ShowUnavailable("fictional old host");
        Assert.IsTrue(fixture.Driver.IsPointerActive, "Stopped hosts must be unsubscribed from the active driver.");
        Assert.IsTrue(fixture.Driver.PointerUp(point, nowMilliseconds: 10));
        Assert.IsTrue(fixture.Runtime.HasLightFeedback);
    }

    private sealed class Fixture : IDisposable
    {
        internal StackedDangoSceneRuntime Runtime { get; } = StackedDangoSceneRuntime.CreateProduct();
        internal LocalRoomSceneDriver Driver { get; }
        internal LocalRoomSceneView Scene { get; set; } = new();
        internal System.Windows.Window Window { get; }
        internal int InputCancelledCount { get; private set; }
        internal LogicalRect BodyBounds => DangoHostFrameBinding.Bounds(Runtime.LastFrame!, 0)
            .Translate(new LogicalVector(0, Runtime.NeutralBodyGroundOffset));

        internal Fixture(bool waitForReady = true)
        {
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
            Runtime.InputCancelled += () => InputCancelledCount++;
            Driver = new(new DesktopSceneSession(), Runtime);
            Window = new() { Width = 1000, Height = 640, Content = Scene, Title = "Fictional local pointer fixture" };
            Window.Show(); Window.UpdateLayout();
            Driver.Start(Scene, 1, enterWorld: true);
            if (waitForReady) WaitForReady();
        }

        internal void WaitForReady()
        {
            Pump(450);
            Assert.IsTrue(Scene.HasHealthyFrame, Runtime.LastError);
            Assert.IsTrue(Driver.OwnsRuntime);
            Assert.IsTrue((bool)typeof(LocalRoomSceneDriver).GetField("readySent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Driver)!);
        }

        internal Point ToScene(LogicalPoint world)
        {
            var point = Scene.Camera.Projection.WorldToScreen(world);
            return new(point.X + Scene.SafeInsets.Left, point.Y + Scene.SafeInsets.Top);
        }

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
