using System.Reflection;
using System.Windows;
using System.Windows.Threading;
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
            var center = fixture.BodyBounds.Center;
            var point = fixture.ToScene(center);
            Assert.IsTrue(fixture.Driver.PointerDown(point, nowMilliseconds: 1000));
            Assert.IsTrue(fixture.Driver.PointerUp(point, nowMilliseconds: 1010));
            Assert.IsFalse(fixture.Driver.IsPointerActive);
            Assert.IsTrue(fixture.Runtime.HasLightFeedback, "A short body click must reach the real attachment runtime.");
            Pump(30);
            Assert.IsTrue(fixture.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness < 1);

            fixture.Driver.ClearInput();
            center = fixture.BodyBounds.Center;
            point = fixture.ToScene(center);
            Assert.IsTrue(fixture.Driver.PointerDown(point, shift: true, nowMilliseconds: 2000));
            var petPoint = fixture.ToScene(new(center.X + 16.1, center.Y));
            Assert.IsTrue(fixture.Driver.PointerMove(petPoint, shift: true, nowMilliseconds: 2100));
            Assert.IsFalse(fixture.Runtime.IsDragging, "Shift keeps the existing pet branch at every camera scale.");
            Assert.IsTrue(fixture.Runtime.HasLightFeedback);
            Assert.IsTrue(fixture.Driver.PointerUp(petPoint, shift: true, nowMilliseconds: 2110));
            Pump(200);
            Assert.IsTrue(fixture.Runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness < .4);
            Assert.AreSame(owner, fixture.Runtime.FrameDriver);
            Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PlainPointerMoveDragsActualPhysicsAndUpEndsIt()
    {
        using var fixture = new Fixture();
        fixture.Scene.SafeInsets = new Thickness(51, 21, 0, 0);
        fixture.Scene.SelectView(RoomViewPreset.Whole);
        var center = fixture.BodyBounds.Center;
        var physicalCenter = fixture.Runtime.CurrentSelectedCenter!.Value;
        var owner = fixture.Runtime.FrameDriver;
        var entity = fixture.Runtime.EntityIds.Single();
        Assert.IsTrue(fixture.Driver.PointerDown(fixture.ToScene(center), nowMilliseconds: 0));
        Assert.IsTrue(fixture.Driver.PointerMove(fixture.ToScene(new(center.X + 3.01, center.Y)), nowMilliseconds: 10));
        Assert.IsTrue(fixture.Runtime.IsDragging, "The drag threshold is measured in world units after inverse projection.");
        var destination = new LogicalPoint(center.X + 80, center.Y - 35);
        Assert.IsTrue(fixture.Driver.PointerMove(fixture.ToScene(destination), nowMilliseconds: 20));
        Pump(140);
        Assert.IsTrue(fixture.Runtime.CurrentSelectedCenter!.Value.X > physicalCenter.X + 1,
            "A drag must move the real native body, rather than only moving a visual or setting a flag.");
        Assert.IsTrue(fixture.Driver.PointerUp(fixture.ToScene(destination), nowMilliseconds: 170));
        Assert.IsFalse(fixture.Runtime.IsDragging);
        Assert.IsFalse(fixture.Driver.IsPointerActive);
        Assert.IsFalse(fixture.Runtime.HasLightFeedback, "Dragging cannot fall back to a click.");
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
            Assert.IsTrue(fixture.Driver.PointerDown(point, nowMilliseconds: 100));
            Assert.IsTrue(fixture.Driver.PointerMove(new(point.X + fixture.Scene.Camera.Scale * 4, point.Y), nowMilliseconds: 110));
            Assert.IsTrue(fixture.Runtime.IsDragging);
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
        internal LogicalRect BodyBounds => DangoHostFrameBinding.Bounds(Runtime.LastFrame!, 0)
            .Translate(new LogicalVector(0, Runtime.NeutralBodyGroundOffset));

        internal Fixture(bool waitForReady = true)
        {
            Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
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
